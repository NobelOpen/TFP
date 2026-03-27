using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Resources;
using TaskFlow.Models.TaskCards;
using TaskFlow.Services;

namespace TaskFlow.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        #region Win32 API - 屏幕息屏控制

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        /// <summary>阻止屏幕息屏（需在 UI 线程调用）</summary>
        private void PreventScreenSleep()
        {
            if (Settings.KeepScreenOn)
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
        }

        /// <summary>恢复系统默认息屏策略</summary>
        private void RestoreScreenSleep()
        {
            SetThreadExecutionState(ES_CONTINUOUS);
        }

        #endregion
        private readonly ITaskExecutionService _executionService;
        /// <summary>执行服务（供脚本编辑器窗口调用单步执行）</summary>
        public ITaskExecutionService ExecutionService => _executionService;

        [ObservableProperty]
        private ObservableCollection<TaskCardBase> _taskCards = new();

        [ObservableProperty]
        private TaskCardBase? _selectedTask;

        [ObservableProperty]
        private Mat? _displayImage;

        /// <summary>
        /// DisplayImage 属性变化时，自动释放旧的 Mat 对象（非共享的临时副本）
        /// </summary>
        partial void OnDisplayImageChanging(Mat? oldValue, Mat? newValue)
        {
            if (oldValue != null && !ReferenceEquals(oldValue, newValue))
            {
                // 检查旧值是否为某个 TaskCard 的 OutputImage（共享对象不能释放）
                bool isShared = TaskCards.Any(t => ReferenceEquals(t.OutputImage, oldValue));
                if (!isShared)
                {
                    oldValue.Dispose();
                }
            }
        }

        [ObservableProperty]
        private string _logText = string.Empty;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private bool _isLoading;

        /// <summary>
        /// 是否处于忙碌状态（流程运行中 或 Orchid 生成/执行中）
        /// </summary>
        public bool IsBusy => IsRunning
            || (AiFlowVm?.IsGenerating ?? false)
            || (AiFlowVm?.IsAiExecuting ?? false);

        /// <summary>
        /// IsRunning 变化时同步通知 IsBusy
        /// </summary>
        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(IsBusy));
        }

        /// <summary>
        /// AI 流程助手面板是否展开
        /// </summary>
        [ObservableProperty]
        private bool _isAiPanelOpen;

        /// <summary>
        /// AI 流程助手 ViewModel
        /// </summary>
        public AiFlowViewModel AiFlowVm { get; private set; } = null!;

        /// <summary>
        /// 当前正在执行的任务卡片
        /// </summary>
        [ObservableProperty]
        private TaskCardBase? _currentRunningTask;

        /// <summary>
        /// 当前任务的面包屑上下文路径
        /// </summary>
        [ObservableProperty]
        private string? _currentTaskBreadcrumb;

        /// <summary>
        /// 上一个已执行的任务卡片
        /// </summary>
        [ObservableProperty]
        private TaskCardBase? _previousTask;

        /// <summary>
        /// 下一个将要执行的任务卡片
        /// </summary>
        [ObservableProperty]
        private TaskCardBase? _nextTask;

        [ObservableProperty]
        private int _nextTaskNumber = 1;

        [ObservableProperty]
        private string _windowTitle = "TaskFlowPro";

        /// <summary>
        /// 全局变量仓库
        /// </summary>
        public VariableStore VariableStore { get; } = new();

        /// <summary>
        /// 应用设置
        /// </summary>
        public AppSettings Settings { get; private set; } = AppSettings.Load();

        private CancellationTokenSource? _cts;

        /// <summary>
        /// 当前打开/保存的文件路径，为 null 表示新建流程
        /// </summary>
        private string? _currentFilePath;

        /// <summary>
        /// 只读暴露当前文件路径，供外部窗口（如模型管理）判断是否为新建空项目
        /// </summary>
        public string? CurrentFilePath => _currentFilePath;

        /// <summary>
        /// 分页集合
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<WorkflowTab> _tabs = new();

        /// <summary>
        /// 当前选中的分页
        /// </summary>
        [ObservableProperty]
        private WorkflowTab? _selectedTab;

        /// <summary>
        /// 分页计数器，用于生成默认名称
        /// </summary>
        public int NextTabIndex { get; set; } = 1;

        /// <summary>
        /// 防止切换分页时重入
        /// </summary>
        private bool _isSwitchingTab = false;

        /// <summary>
        /// 配置文件目录
        /// </summary>
        private static readonly string ConfigDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskFlow");

        /// <summary>
        /// 记录上次打开的流程文件路径的配置文件
        /// </summary>
        private static readonly string LastWorkflowConfigPath = System.IO.Path.Combine(ConfigDir, "lastWorkflow.txt");

        /// <summary>
        /// 剪贴板：存储复制的任务卡片 JSON
        /// </summary>
        private string? _copiedTaskJson;

        /// <summary>
        /// 当前已选中的卡片集合（用于快速取消选中，避免遍历全部卡片）
        /// </summary>
        private readonly HashSet<TaskCardBase> _selectedCards = new();

        /// <summary>
        /// 日志刷新后请求 View 滚动到底部
        /// </summary>
        public event EventHandler? LogScrollToEndRequested;

        /// <summary>
        /// 请求 View 清空并重建所有流程 ListBox 缓存（新建/加载项目时触发）
        /// </summary>
        public event EventHandler? FlowListBoxResetRequested;

        public MainViewModel()
        {
            var adbService = new AdbService();
            var screenshotService = new ScreenshotService();
            var openCVService = new OpenCVService();
            var ocrService = new OcrService();
            var weChatOcrService = new WeChatOcrService(Settings);
            var subtitleService = new SubtitleService();

            _executionService = new TaskExecutionService(adbService, screenshotService, openCVService, ocrService, weChatOcrService, VariableStore, subtitleService);
            _executionService.TaskStarted += OnTaskStarted;
            _executionService.TaskCompleted += OnTaskCompleted;
            _executionService.AllTasksCompleted += OnAllTasksCompleted;
            _executionService.LogMessage += OnLogMessage;
            
            // 注入子流程解析器：从当前的所有的 Tabs 查找
            _executionService.SubFlowResolver = (guid) => 
            {
                var tab = Tabs.FirstOrDefault(t => t.Id == guid);
                return tab?.TaskCards;
            };

            // 初始化日志节流定时器，200ms 最多刷新一次 UI
            _logThrottleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _logThrottleTimer.Tick += (s, e) =>
            {
                _logThrottleTimer.Stop();
                string newText;
                lock (_logBuilder)
                {
                    newText = _logBuilder.ToString();
                    _isLogUpdatePending = false;
                }
                LogText = newText;
                LogScrollToEndRequested?.Invoke(this, EventArgs.Empty);
            };

            // 初始化第一个分页
            var firstTab = new WorkflowTab { Name = string.Format(Strings.VM_FlowDefault, NextTabIndex++), IsSelected = true };
            Tabs.Add(firstTab);
            SelectedTab = firstTab;

            // 初始化 AI 流程助手 ViewModel
            AiFlowVm = new AiFlowViewModel(this);

            // 监听 Orchid 状态变化，同步刷新 IsBusy
            AiFlowVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(AiFlowViewModel.IsGenerating)
                                   or nameof(AiFlowViewModel.IsAiExecuting))
                {
                    OnPropertyChanged(nameof(IsBusy));
                }
            };
        }


        #region Commands

        [RelayCommand]
        private void AddTask(TaskType taskType)
        {
            TaskCardBase newTask = CreateTaskCard(taskType);
            newTask.Order = NextTaskNumber++;

            if (SelectedTask != null)
            {
                var index = GetInsertIndexAfter(SelectedTask);
                TaskCards.Insert(index, newTask);
            }
            else
            {
                TaskCards.Add(newTask);
            }

            SelectedTask = newTask;
            AddLog($"添加任务: {newTask.Name}");
            RecalculateIndentLevels();
        }

        [RelayCommand]
        private void AddIfElseBranch()
        {
            var branchGroupId = Guid.NewGuid();

            var ifStart = new IfElseBranchTaskCard(BranchRole.IfStart) { BranchGroupId = branchGroupId, Order = NextTaskNumber++ };
            var elseStart = new IfElseBranchTaskCard(BranchRole.ElseStart) { BranchGroupId = branchGroupId, Order = NextTaskNumber++, IsHiddenByCollapse = true };
            var elseEnd = new IfElseBranchTaskCard(BranchRole.ElseEnd) { BranchGroupId = branchGroupId, Order = NextTaskNumber++ };

            // IfStart 默认隐藏else
            ifStart.IsElseHidden = true;

            int insertIndex = SelectedTask != null ? GetInsertIndexAfter(SelectedTask) : TaskCards.Count;

            TaskCards.Insert(insertIndex, ifStart);
            TaskCards.Insert(insertIndex + 1, elseStart);
            TaskCards.Insert(insertIndex + 2, elseEnd);

            SelectedTask = ifStart;
            AddLog($"添加If-Else分支");
            RecalculateIndentLevels();
        }

        [RelayCommand]
        private void AddElifBranch(TaskCardBase ifStartTask)
        {
            if (ifStartTask == null || ifStartTask.BranchRole != BranchRole.IfStart || !ifStartTask.BranchGroupId.HasValue)
                return;

            var branchGroupId = ifStartTask.BranchGroupId.Value;

            // 找到ElseStart的位置，在其前面插入ElifStart
            int elseStartIndex = -1;
            for (int i = 0; i < TaskCards.Count; i++)
            {
                if (TaskCards[i].BranchGroupId == branchGroupId && TaskCards[i].BranchRole == BranchRole.ElseStart)
                {
                    elseStartIndex = i;
                    break;
                }
            }

            if (elseStartIndex < 0) return;

            var elifStart = new IfElseBranchTaskCard(BranchRole.ElifStart) { BranchGroupId = branchGroupId, Order = NextTaskNumber++ };
            TaskCards.Insert(elseStartIndex, elifStart);

            SelectedTask = elifStart;
            AddLog($"添加Elif分支");
            RecalculateIndentLevels();
        }

        [RelayCommand]
        private void AddForLoop()
        {
            var branchGroupId = Guid.NewGuid();

            var loopStart = new ForLoopTaskCard(BranchRole.ForLoopStart) { BranchGroupId = branchGroupId, Order = NextTaskNumber++ };
            var loopEnd = new ForLoopTaskCard(BranchRole.ForLoopEnd) { BranchGroupId = branchGroupId, Order = NextTaskNumber++ };

            int insertIndex = SelectedTask != null ? GetInsertIndexAfter(SelectedTask) : TaskCards.Count;

            TaskCards.Insert(insertIndex, loopStart);
            TaskCards.Insert(insertIndex + 1, loopEnd);

            SelectedTask = loopStart;
            AddLog($"添加For循环");
            RecalculateIndentLevels();
        }

        [RelayCommand]
        private void DeleteTask(TaskCardBase task)
        {
            if (task == null) return;
            
            // 彻底禁止删除子流程专用的输入定界锚卡片
            if (task.TaskType == TaskType.SubFlowInput) return;

            // ElifStart: 只删除自身及其名下的卡片（到下一个ElifStart/ElseStart之前）
            if (task.BranchRole == BranchRole.ElifStart && task.BranchGroupId.HasValue)
            {
                int elifIndex = TaskCards.IndexOf(task);
                if (elifIndex >= 0)
                {
                    // 找到下一个ElifStart或ElseStart
                    int endIndex = elifIndex + 1;
                    while (endIndex < TaskCards.Count)
                    {
                        var nextCard = TaskCards[endIndex];
                        if (nextCard.BranchGroupId == task.BranchGroupId &&
                            (nextCard.BranchRole == BranchRole.ElifStart ||
                             nextCard.BranchRole == BranchRole.ElseStart ||
                             nextCard.BranchRole == BranchRole.ElseEnd))
                        {
                            break;
                        }
                        endIndex++;
                    }

                    // 删除从elifIndex到endIndex-1的所有卡片
                    var cardsToRemove = new List<TaskCardBase>();
                    for (int i = elifIndex; i < endIndex; i++)
                    {
                        cardsToRemove.Add(TaskCards[i]);
                    }
                    foreach (var card in cardsToRemove)
                    {
                        TaskCards.Remove(card);
                    }
                    AddLog($"删除Elif分支: {cardsToRemove.Count} 个卡片");
                }
            }
            // 处理分支卡片的级联删除
            else if (task.BranchGroupId.HasValue)
            {
                var branchCards = TaskCards.Where(t => t.BranchGroupId == task.BranchGroupId).ToList();

                // 找到分支的起始和结束索引
                int startIdx = -1;
                int endIdx = -1;

                foreach (var bc in branchCards)
                {
                    var idx = TaskCards.IndexOf(bc);
                    if (startIdx == -1 || idx < startIdx) startIdx = idx;
                    if (endIdx == -1 || idx > endIdx) endIdx = idx;
                }

                if (startIdx >= 0 && endIdx >= startIdx)
                {
                    // 删除范围内的所有卡片（包括分支内插入的普通卡片）
                    var cardsToRemove = new List<TaskCardBase>();
                    for (int i = startIdx; i <= endIdx; i++)
                    {
                        cardsToRemove.Add(TaskCards[i]);
                    }
                    foreach (var card in cardsToRemove)
                    {
                        TaskCards.Remove(card);
                    }
                    AddLog($"删除分支组: {cardsToRemove.Count} 个卡片");
                }
                else
                {
                    // 降级：按BranchGroupId删除
                    foreach (var card in branchCards)
                    {
                        TaskCards.Remove(card);
                    }
                    AddLog($"删除分支组: {branchCards.Count} 个卡片");
                }
            }
            else
            {
                TaskCards.Remove(task);
                AddLog($"删除任务: {task.Name}");
            }

            if (SelectedTask == task)
            {
                SelectedTask = null;
            }
            RecalculateIndentLevels();
        }

        [RelayCommand]
        private void RenameTask(TaskCardBase task)
        {
            // 重命名通过属性对话框处理
        }

        /// <summary>
        /// 复制选中的任务卡片（序列化为 JSON 存储）
        /// </summary>
        [RelayCommand]
        private void CopyTask(TaskCardBase task)
        {
            if (task == null) return;

            // 分支卡片（IfStart/ForLoopStart等）结构复杂，不支持复制
            if (task.BranchRole != BranchRole.None)
            {
                AddLog("分支卡片不支持复制");
                return;
            }

            try
            {
                var json = JsonHelper.Serialize(new[] { task });
                _copiedTaskJson = json;
                AddLog(string.Format(Strings.VM_Duplicated, task.Name));
            }
            catch (Exception ex)
            {
                AddLog(string.Format(Strings.VM_DuplicateFailed, ex.Message));
            }
        }

        /// <summary>
        /// 粘贴任务卡片（反序列化并生成新 Id/Order）
        /// </summary>
        [RelayCommand]
        private void PasteTask()
        {
            if (string.IsNullOrEmpty(_copiedTaskJson))
            {
                AddLog(Strings.VM_ClipboardEmpty);
                return;
            }

            try
            {
                var tasks = JsonHelper.Deserialize(_copiedTaskJson);
                if (tasks.Count == 0) return;

                var newTask = tasks[0];
                var isSubFlow = SelectedTab?.Type == FlowType.SubFlow;

                // 进行子流程相关的安全检查
                if (isSubFlow)
                {
                    if (newTask.TaskType == TaskType.CallSubFlow)
                    {
                        AddLog(string.Format(Strings.VM_PasteFailed, "不可在子流程中粘贴并调用另一个子流程"));
                        return;
                    }
                }
                else
                {
                    if (newTask.TaskType == TaskType.SubFlowOutput)
                    {
                        AddLog(string.Format(Strings.VM_PasteFailed, "该特殊卡片只能在子流程中使用"));
                        return;
                    }
                }

                if (newTask.TaskType == TaskType.SubFlowInput)
                {
                    AddLog(string.Format(Strings.VM_PasteFailed, "定界锚卡片不可复制与转移"));
                    return;
                }

                // 生成新的 Id 和 Order
                newTask.Id = Guid.NewGuid();
                newTask.Order = NextTaskNumber++;
                // 重置运行状态
                newTask.Reset();

                if (SelectedTask != null)
                {
                    var index = GetInsertIndexAfter(SelectedTask);
                    TaskCards.Insert(index, newTask);
                }
                else
                {
                    TaskCards.Add(newTask);
                }

                SelectedTask = newTask;
                AddLog(string.Format(Strings.VM_Pasted, newTask.Name));
                RecalculateIndentLevels();
            }
            catch (Exception ex)
            {
                AddLog(string.Format(Strings.VM_PasteFailed, ex.Message));
            }
        }

        [RelayCommand]
        private void SelectTask(TaskCardBase task)
        {
            // 仅取消之前已选中的卡片（避免遍历全部）
            foreach (var t in _selectedCards)
            {
                t.IsSelected = false;
            }
            _selectedCards.Clear();

            // 选中当前任务
            if (task != null)
            {
                task.IsSelected = true;
                _selectedCards.Add(task);
                SelectedTask = task;

                // 如果是控制流卡片，高亮同组的所有控制流卡片
                if (task.BranchGroupId.HasValue)
                {
                    foreach (var t in TaskCards)
                    {
                        if (t.BranchGroupId == task.BranchGroupId &&
                            t.BranchRole != BranchRole.None)
                        {
                            t.IsSelected = true;
                            _selectedCards.Add(t);
                        }
                    }
                }

                // 显示任务的输出图像，如果没有则清空预览
                if (task.OutputImage != null && !task.OutputImage.IsDisposed && !task.OutputImage.Empty())
                {
                    // 裁剪卡片：显示源图+ROI框，而非裁剪结果
                    if (task is ImgCropTaskCard)
                    {
                        var cropPreview = TryLoadSourceImageForRoiPreview(task);
                        DisplayImage = cropPreview ?? task.OutputImage;
                    }
                    else
                    {
                        DisplayImage = DrawRoiOverlay(task, task.OutputImage);
                    }
                }
                else
                {
                    // 对OCR和模板匹配，尝试加载源图以显示ROI
                    var roiPreview = TryLoadSourceImageForRoiPreview(task);
                    DisplayImage = roiPreview;
                }
            }
        }

        /// <summary>
        /// 在图像上绘制ROI矩形框（如果任务设置了ROI区域）
        /// </summary>
        private Mat DrawRoiOverlay(TaskCardBase task, Mat sourceImage)
        {
            int roiX = 0, roiY = 0, roiW = 0, roiH = 0;

            if (task is ImgOcrTaskCard ocrCard)
            {
                roiX = ocrCard.RoiX; roiY = ocrCard.RoiY;
                roiW = ocrCard.RoiWidth; roiH = ocrCard.RoiHeight;
            }
            else if (task is ImgTemplateMatchTaskCard matchCard)
            {
                roiX = matchCard.RoiX; roiY = matchCard.RoiY;
                roiW = matchCard.RoiWidth; roiH = matchCard.RoiHeight;
            }
            else if (task is ImgCropTaskCard cropCard)
            {
                roiX = cropCard.RoiX; roiY = cropCard.RoiY;
                roiW = cropCard.RoiWidth; roiH = cropCard.RoiHeight;
            }

            if (roiW <= 0 || roiH <= 0) return sourceImage;

            // 在副本上画矩形
            var display = sourceImage.Clone();
            Cv2.Rectangle(display,
                new OpenCvSharp.Rect(roiX, roiY, roiW, roiH),
                new Scalar(0, 255, 128), 2);
            return display;
        }

        /// <summary>
        /// 尝试为OCR/模板匹配任务加载源图并绘制ROI预览
        /// </summary>
        private Mat? TryLoadSourceImageForRoiPreview(TaskCardBase task)
        {
            bool useSource = false;
            Guid? sourceTaskId = null;
            string? filePath = null;
            int roiX = 0, roiY = 0, roiW = 0, roiH = 0;

            if (task is ImgOcrTaskCard ocrCard)
            {
                useSource = ocrCard.UseSourceTaskImage;
                sourceTaskId = ocrCard.SourceTaskIdForImage;
                filePath = ocrCard.ImageFilePath;
                roiX = ocrCard.RoiX; roiY = ocrCard.RoiY;
                roiW = ocrCard.RoiWidth; roiH = ocrCard.RoiHeight;
            }
            else if (task is ImgTemplateMatchTaskCard matchCard)
            {
                useSource = matchCard.UseSourceTaskImage;
                sourceTaskId = matchCard.SourceTaskIdForImage;
                filePath = matchCard.ImageFilePath;
                roiX = matchCard.RoiX; roiY = matchCard.RoiY;
                roiW = matchCard.RoiWidth; roiH = matchCard.RoiHeight;
            }
            else if (task is ImgCropTaskCard cropCard)
            {
                useSource = cropCard.UseSourceTaskImage;
                sourceTaskId = cropCard.SourceTaskIdForImage;
                filePath = cropCard.ImageFilePath;
                roiX = cropCard.RoiX; roiY = cropCard.RoiY;
                roiW = cropCard.RoiWidth; roiH = cropCard.RoiHeight;
            }
            else
            {
                return null;
            }

            // 尝试获取源图
            Mat? sourceImage = null;
            if (useSource && sourceTaskId.HasValue)
            {
                var srcTask = TaskCards.FirstOrDefault(t => t.Id == sourceTaskId.Value);
                if (srcTask?.OutputImage != null && !srcTask.OutputImage.IsDisposed && !srcTask.OutputImage.Empty())
                {
                    sourceImage = srcTask.OutputImage.Clone();
                }
            }
            if (sourceImage == null && !string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
            {
                sourceImage = Cv2.ImRead(filePath);
            }
            if (sourceImage == null || sourceImage.Empty())
            {
                sourceImage?.Dispose();
                return null;
            }

            // 有ROI时绘制矩形框，无ROI时直接返回源图
            if (roiW > 0 && roiH > 0)
            {
                Cv2.Rectangle(sourceImage,
                    new OpenCvSharp.Rect(roiX, roiY, roiW, roiH),
                    new Scalar(0, 255, 128), 2);
            }
            return sourceImage;
        }

        [RelayCommand]
        private void DeselectAll()
        {
            // 仅重置已选中的卡片（避免遍历全部）
            foreach (var t in _selectedCards)
            {
                t.IsSelected = false;
            }
            _selectedCards.Clear();
            SelectedTask = null;
            DisplayImage = null;
        }

        [RelayCommand]
        private void ToggleBranchCollapse(TaskCardBase task)
        {
            if (task.BranchRole != BranchRole.IfStart && task.BranchRole != BranchRole.ForLoopStart)
                return;

            if (!task.BranchGroupId.HasValue)
                return;

            task.IsCollapsed = !task.IsCollapsed;

            // 获取分支组的所有卡片
            var branchCards = TaskCards
                .Where(t => t.BranchGroupId == task.BranchGroupId && t != task)
                .ToList();

            // 获取分支内的所有卡片（在IfStart和ElseEnd之间，或ForLoopStart和ForLoopEnd之间）
            var taskIndex = TaskCards.IndexOf(task);
            int endIndex = -1;

            if (task.BranchRole == BranchRole.IfStart)
            {
                var elseEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ElseEnd);
                if (elseEnd != null)
                {
                    endIndex = TaskCards.IndexOf(elseEnd);
                }
            }
            else if (task.BranchRole == BranchRole.ForLoopStart)
            {
                var loopEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ForLoopEnd);
                if (loopEnd != null)
                {
                    endIndex = TaskCards.IndexOf(loopEnd);
                }
            }

            if (endIndex > taskIndex)
            {
                // 检查IfStart上的IsElseHidden状态
                bool isElseHidden = task is IfElseBranchTaskCard ifCard && ifCard.IsElseHidden;

                for (int i = taskIndex + 1; i <= endIndex; i++)
                {
                    if (task.IsCollapsed)
                    {
                        // 折叠：全部隐藏
                        TaskCards[i].IsHiddenByCollapse = true;
                    }
                    else
                    {
                        // 展开：ElseStart如果IsElseHidden则保持隐藏
                        if (isElseHidden && TaskCards[i].BranchRole == BranchRole.ElseStart)
                        {
                            TaskCards[i].IsHiddenByCollapse = true;
                        }
                        else
                        {
                            TaskCards[i].IsHiddenByCollapse = false;
                        }

                        // 如果当前卡片是已折叠的嵌套分支头，跳过其所有子卡片保持隐藏
                        if (TaskCards[i].IsCollapsed && TaskCards[i].BranchGroupId.HasValue &&
                            (TaskCards[i].BranchRole == BranchRole.IfStart || TaskCards[i].BranchRole == BranchRole.ForLoopStart))
                        {
                            var nestedGroupId = TaskCards[i].BranchGroupId;
                            // 找到嵌套分支的结束卡片
                            int nestedEndIndex = i;
                            for (int j = i + 1; j <= endIndex; j++)
                            {
                                if (TaskCards[j].BranchGroupId == nestedGroupId &&
                                    (TaskCards[j].BranchRole == BranchRole.ElseEnd || TaskCards[j].BranchRole == BranchRole.ForLoopEnd))
                                {
                                    nestedEndIndex = j;
                                    break;
                                }
                            }
                            // 嵌套分支内的子卡片保持隐藏
                            for (int j = i + 1; j <= nestedEndIndex; j++)
                            {
                                TaskCards[j].IsHiddenByCollapse = true;
                            }
                            i = nestedEndIndex; // 跳过已处理的嵌套分支
                        }
                    }
                }
            }

            // 刷新当前分页面板绑定的 CollectionView 以应用 Filter，避免 UI 虚拟化时的 Container Churning
            RefreshTaskCardsView();
        }

        /// <summary>
        /// 刷新当前分页面板的 UI 列表
        /// </summary>
        public void RefreshTaskCardsView()
        {
            SelectedTab?.UpdateVisibleTaskCards();
        }

        [RelayCommand]
        private void MoveTask(object parameter)
        {
            if (parameter is not Tuple<int, int> indices) return;

            int oldIndex = indices.Item1;
            int newIndex = indices.Item2;

            if (oldIndex < 0 || oldIndex >= TaskCards.Count ||
                newIndex < 0 || newIndex >= TaskCards.Count ||
                oldIndex == newIndex)
                return;

            var task = TaskCards[oldIndex];

            // 如果是折叠的分支头，移动整个分支组（包含范围内所有卡片）
            if (task.IsCollapsed && task.BranchGroupId.HasValue &&
                (task.BranchRole == BranchRole.IfStart || task.BranchRole == BranchRole.ForLoopStart))
            {
                // 找到分支结束卡片的索引
                int endIdx = -1;
                var branchCards = TaskCards.Where(t => t.BranchGroupId == task.BranchGroupId && t != task).ToList();

                if (task.BranchRole == BranchRole.IfStart)
                {
                    var elseEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ElseEnd);
                    if (elseEnd != null) endIdx = TaskCards.IndexOf(elseEnd);
                }
                else if (task.BranchRole == BranchRole.ForLoopStart)
                {
                    var loopEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ForLoopEnd);
                    if (loopEnd != null) endIdx = TaskCards.IndexOf(loopEnd);
                }

                if (endIdx <= oldIndex) return;

                // 收集范围内的所有卡片（包括分支内用户添加的普通卡片）
                var allCardsInRange = new List<TaskCardBase>();
                for (int i = oldIndex; i <= endIdx; i++)
                {
                    allCardsInRange.Add(TaskCards[i]);
                }

                // 移除所有范围内的卡片
                foreach (var card in allCardsInRange)
                {
                    TaskCards.Remove(card);
                }

                // 调整目标索引
                if (newIndex > oldIndex)
                {
                    newIndex -= allCardsInRange.Count - 1;
                }
                newIndex = Math.Max(0, Math.Min(newIndex, TaskCards.Count));

                // 重新插入
                for (int i = 0; i < allCardsInRange.Count; i++)
                {
                    TaskCards.Insert(newIndex + i, allCardsInRange[i]);
                }
            }
            else if (task.BranchRole == BranchRole.None)
            {
                // 普通卡片直接移动
                TaskCards.Move(oldIndex, newIndex);
            }
            RecalculateIndentLevels();
        }

        /// <summary>
        /// 重新计算所有卡片的缩进等级
        /// </summary>
        public void RecalculateIndentLevels()
        {
            RecalculateIndentLevelsFor(TaskCards);
        }

        /// <summary>
        /// 对指定集合重新计算缩进等级（用于文件加载时处理所有分页）
        /// </summary>
        public static void RecalculateIndentLevelsFor(IList<TaskCardBase> cards)
        {
            int currentIndent = 0;
            foreach (var card in cards)
            {
                switch (card.BranchRole)
                {
                    case BranchRole.IfStart:
                    case BranchRole.ForLoopStart:
                        card.IndentLevel = currentIndent;
                        currentIndent++;
                        break;
                    case BranchRole.ElseStart:
                    case BranchRole.ElifStart:
                        // ElseStart/ElifStart与IfStart同级
                        card.IndentLevel = Math.Max(0, currentIndent - 1);
                        break;
                    case BranchRole.ElseEnd:
                    case BranchRole.ForLoopEnd:
                        currentIndent = Math.Max(0, currentIndent - 1);
                        card.IndentLevel = currentIndent;
                        break;
                    default:
                        card.IndentLevel = currentIndent;
                        break;
                }
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 获取在指定任务之后的插入位置索引。
        /// 如果任务是折叠的分支头，返回整个分支组末尾之后的位置。
        /// </summary>
        private int GetInsertIndexAfter(TaskCardBase task)
        {
            var baseIndex = TaskCards.IndexOf(task);
            if (baseIndex < 0) return TaskCards.Count;

            // 折叠的分支头：跳过整个分支组
            if (task.IsCollapsed && task.BranchGroupId.HasValue &&
                (task.BranchRole == BranchRole.IfStart || task.BranchRole == BranchRole.ForLoopStart))
            {
                var branchCards = TaskCards.Where(t => t.BranchGroupId == task.BranchGroupId && t != task).ToList();

                int endIdx = baseIndex;
                if (task.BranchRole == BranchRole.IfStart)
                {
                    var elseEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ElseEnd);
                    if (elseEnd != null) endIdx = TaskCards.IndexOf(elseEnd);
                }
                else if (task.BranchRole == BranchRole.ForLoopStart)
                {
                    var loopEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ForLoopEnd);
                    if (loopEnd != null) endIdx = TaskCards.IndexOf(loopEnd);
                }

                return endIdx + 1;
            }

            return baseIndex + 1;
        }

        internal TaskCardBase CreateTaskCard(TaskType taskType)
        {
            return taskType switch
            {
                TaskType.EndTask => new EndTaskCard(),
                TaskType.EndAllFlows => new EndAllFlowsTaskCard(),
                TaskType.RestartFlow => new RestartFlowTaskCard(),
                TaskType.PauseTask => new PauseTaskCard(),
                TaskType.WinLaunchApp => new WinLaunchAppTaskCard(),
                TaskType.WinScreenshot => new WinScreenshotTaskCard(),
                TaskType.WinClick => new WinClickTaskCard(),
                TaskType.WinCloseApp => new WinCloseAppTaskCard(),
                TaskType.WinUiAutomation => new WinUiAutomationTaskCard(),
                TaskType.WinSimulateInput => new WinSimulateInputTaskCard(),
                TaskType.WinSubtitle => new WinSubtitleTaskCard(),
                TaskType.WinFindFile => new WinFindFileTaskCard(),
                TaskType.AdbConnect => new AdbConnectTaskCard(),
                TaskType.AdbLaunchApp => new AdbLaunchAppTaskCard(),
                TaskType.AdbScreenshot => new AdbScreenshotTaskCard(),
                TaskType.AdbClick => new AdbClickTaskCard(),
                TaskType.AdbCloseApp => new AdbCloseAppTaskCard(),
                TaskType.AdbDisconnect => new AdbDisconnectTaskCard(),
                TaskType.ImgCrop => new ImgCropTaskCard(),
                TaskType.ImgTemplateMatch => new ImgTemplateMatchTaskCard(),
                TaskType.ImgOcr => new ImgOcrTaskCard(),
                TaskType.ImgColorDetect => new ImgColorDetectTaskCard(),
                TaskType.ImgColorSegment => new ImgColorSegmentTaskCard(),
                TaskType.ImgPreprocess => new ImgPreprocessTaskCard(),
                TaskType.ImgBlobAnalysis => new ImgBlobAnalysisTaskCard(),
                TaskType.ImgResize => new ImgResizeTaskCard(),
                TaskType.ExpressionEval => new ExpressionEvalTaskCard(),
                TaskType.BreakLoop => new BreakLoopTaskCard(),
                TaskType.StringSubstring => new StringSubstringTaskCard(),
                TaskType.TypeConvert => new TypeConvertTaskCard(),
                TaskType.ArrayParse => new ArrayParseTaskCard(),
                TaskType.GetTimestamp => new GetTimestampTaskCard(),
                TaskType.LlmTranslate => new LlmTranslateTaskCard(),
                TaskType.LlmVision => new LlmVisionTaskCard(),
                TaskType.ArrayBuilder => new ArrayBuilderTaskCard(),
                TaskType.LlmFileTranslate => new LlmFileTranslateTaskCard(),
                TaskType.FileRead => new FileReadTaskCard(),
                TaskType.EventListener => new EventListenerTaskCard(),
                TaskType.ArraySearch => new ArraySearchTaskCard(),
                TaskType.WinTextInput => new WinTextInputTaskCard(),
                TaskType.InputCombo => new InputComboTaskCard(),
                TaskType.CallSubFlow => new CallSubFlowTaskCard(),
                TaskType.SubFlowInput => new SubFlowInputTaskCard(),
                TaskType.SubFlowOutput => new SubFlowOutputTaskCard(),
                TaskType.CustomScript => new CustomScriptTaskCard(),
                _ => throw new ArgumentException($"Unsupported task type: {taskType}")
            };
        }

        /// <summary>
        /// 获取可引用的任务列表（用于属性对话框）
        /// </summary>
        public IEnumerable<TaskCardBase> GetReferenceableTasks()
        {
            return TaskCards.Where(t => t.CanBeReferenced);
        }

        /// <summary>
        /// 获取输出图像的任务列表
        /// </summary>
        public IEnumerable<TaskCardBase> GetImageOutputTasks()
        {
            return TaskCards.Where(t => t.OutputsImage);
        }

        /// <summary>
        /// 获取输出坐标的任务列表
        /// </summary>
        public IEnumerable<TaskCardBase> GetCoordinateOutputTasks()
        {
            return TaskCards.Where(t => t.OutputsCoordinates);
        }

        /// <summary>
        /// 获取输出文本的任务列表
        /// </summary>
        public IEnumerable<TaskCardBase> GetTextOutputTasks()
        {
            return TaskCards.Where(t => t.OutputsText);
        }

        /// <summary>
        /// 获取输出字符串数组的任务列表
        /// </summary>
        public IEnumerable<TaskCardBase> GetStringArrayOutputTasks()
        {
            return TaskCards.Where(t => t.OutputsStringArray);
        }

        /// <summary>
        /// 获取输出任意数组形式的任务列表
        /// </summary>
        public IEnumerable<TaskCardBase> GetArrayOutputTasks()
        {
            return TaskCards.Where(t => t.OutputsArray);
        }

        #endregion
    }
}
