using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Models.AiFlow;
using TaskFlow.Models.TaskCards;
using TaskFlow.Services;

namespace TaskFlow.ViewModels
{
    /// <summary>
    /// AI 流程助手面板的 ViewModel
    /// </summary>
    public partial class AiFlowViewModel : ObservableObject
    {
        private readonly MainViewModel _mainViewModel;
        private readonly AiFlowGeneratorService _service = new();
        private readonly Services.ScreenshotService _aiScreenshotService = new();
        private readonly PowerShellExecutorService _psService = new();

        /// <summary>
        /// Orchid 直接截屏：截取全屏并返回 base64 编码和分辨率
        /// </summary>
        private async Task<(string? Base64, int Width, int Height)> CaptureScreenForAiAsync(string processName = "windows")
        {
            try
            {
                var result = await _aiScreenshotService.CaptureWindowAsync(processName);
                if (!result.Success || result.Image == null)
                {
                    result.Image?.Dispose();
                    return (null, 0, 0);
                }

                var mat = result.Image;
                int w = mat.Width, h = mat.Height;

                // 先尝试 PNG 编码
                OpenCvSharp.Cv2.ImEncode(".png", mat, out var imgBytes);
                string mimeType = "image/png";

                // 超过 1MB 时降级为 JPEG 80% 压缩
                if (imgBytes.Length > 1024 * 1024)
                {
                    var encodeParams = new[] { new OpenCvSharp.ImageEncodingParam(OpenCvSharp.ImwriteFlags.JpegQuality, 80) };
                    OpenCvSharp.Cv2.ImEncode(".jpg", mat, out imgBytes, encodeParams);
                    mimeType = "image/jpeg";
                }

                mat.Dispose();
                AiFlowLogger.Info($"截图编码完成: {imgBytes.Length / 1024}KB ({w}x{h}, {mimeType})");
                return (Convert.ToBase64String(imgBytes), w, h);
            }
            catch (Exception ex)
            {
                AiFlowLogger.Warn($"Orchid 截屏失败: {ex.Message}");
                return (null, 0, 0);
            }
        }

        /// <summary>
        /// 主 ViewModel 引用（供 View 层检查全局状态）
        /// </summary>
        public MainViewModel MainVm => _mainViewModel;
        private CancellationTokenSource? _cts;

        // 思考中动画
        private System.Windows.Threading.DispatcherTimer? _thinkingTimer;
        private int _thinkingDotCount;
        private string _thinkingBaseText = "";

        /// <summary>
        /// 聊天消息列表
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<AiChatMessage> _messages = new();

        /// <summary>
        /// 用户输入文本
        /// </summary>
        [ObservableProperty]
        private string _inputText = "";

        /// <summary>
        /// 是否正在生成中
        /// </summary>
        [ObservableProperty]
        private bool _isGenerating;

        /// <summary>
        /// AI 是否正在自主执行中（运行卡片 + 决策循环）
        /// </summary>
        [ObservableProperty]
        private bool _isAiExecuting;

        /// <summary>
        /// 是否显示重试按钮（生成失败时）
        /// </summary>
        [ObservableProperty]
        private bool _showRetryButton;

        /// <summary>
        /// 保存上次用户输入，用于重试
        /// </summary>
        private string? _lastUserInput;

        /// <summary>
        /// 选中的模型 ID
        /// </summary>
        [ObservableProperty]
        private string _selectedModelId = "";

        /// <summary>
        /// 当前 AI 助手模式
        /// </summary>
        [ObservableProperty]
        private AiAssistantMode _currentMode = AiAssistantMode.Design;

        /// <summary>
        /// 附件文件路径（图片或文件）
        /// </summary>
        [ObservableProperty]
        private string? _attachedFilePath;

        /// <summary>
        /// 是否正在等待用户批准（自主模式中风险操作）
        /// </summary>
        [ObservableProperty]
        private bool _awaitingApproval;

        /// <summary>
        /// 等待批准的操作描述
        /// </summary>
        [ObservableProperty]
        private string _approvalDescription = "";

        /// <summary>
        /// 用于在自主循环中暂停等待用户批准的信号
        /// </summary>
        private TaskCompletionSource<bool>? _approvalTcs;

        /// <summary>
        /// 当前待确认的方案
        /// </summary>
        [ObservableProperty]
        private AiFlowPlanResponse? _pendingPlan;

        /// <summary>
        /// 创建完成后的报告项列表
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<AiFlowReportItem> _reportItems = new();

        /// <summary>
        /// 所有历史会话列表
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<AiChatSession> _sessions = new();

        /// <summary>
        /// 当前活跃会话
        /// </summary>
        [ObservableProperty]
        private AiChatSession _currentSession = new();

        /// <summary>
        /// 是否显示历史对话面板
        /// </summary>
        [ObservableProperty]
        private bool _isHistoryOpen;

        /// <summary>
        /// 请求打开任务卡片属性对话框的事件
        /// </summary>
        public event Action<Guid>? OpenCardPropertyRequested;

        /// <summary>
        /// 请求滚动到底部
        /// </summary>
        public event EventHandler? ScrollToEndRequested;

        /// <summary>
        /// 请求关闭 Orchid 面板的事件
        /// </summary>
        public event Action? ClosePanelRequested;

        /// <summary>
        /// AI 方案执行器
        /// </summary>
        private readonly AiPlanExecutor _planExecutor;

        /// <summary>
        /// AI 流程序列化器
        /// </summary>
        private readonly AiFlowSerializer _serializer;

        public AiFlowViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _planExecutor = new AiPlanExecutor(mainViewModel);
            _serializer = new AiFlowSerializer(mainViewModel);
            // 从磁盘加载历史会话
            var saved = AiChatSessionStore.Load();
            foreach (var s in saved)
                Sessions.Add(s);
        }

        /// <summary>
        /// 新建对话：归档当前会话并创建新的空会话
        /// </summary>
        [RelayCommand]
        private void NewChat()
        {
            ArchiveCurrentSession();
            CurrentSession = new AiChatSession();
            Messages.Clear();
            PendingPlan = null;
            ReportItems.Clear();
            IsHistoryOpen = false;
        }

        /// <summary>
        /// 切换到指定的历史会话
        /// </summary>
        [RelayCommand]
        private void SwitchSession(AiChatSession session)
        {
            if (session == null) return;
            ArchiveCurrentSession();

            // 从历史中移除并设为当前
            Sessions.Remove(session);
            CurrentSession = session;
            Messages.Clear();
            foreach (var msg in session.Messages)
                Messages.Add(msg);

            IsHistoryOpen = false;
            ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 删除指定的历史会话
        /// </summary>
        [RelayCommand]
        private void DeleteSession(AiChatSession session)
        {
            if (session == null) return;
            Sessions.Remove(session);
            SaveSessionsToDisk();
        }

        /// <summary>
        /// 切换历史面板显隐
        /// </summary>
        [RelayCommand]
        private void ToggleHistory()
        {
            IsHistoryOpen = !IsHistoryOpen;
        }

        /// <summary>
        /// 请求关闭面板
        /// </summary>
        public void ClosePanel()
        {
            ClosePanelRequested?.Invoke();
        }

        /// <summary>
        /// 将当前会话归档到历史列表并持久化
        /// </summary>
        private void ArchiveCurrentSession()
        {
            if (CurrentSession.Messages.Count == 0 && Messages.Count == 0) return;

            // 同步 Messages 到 CurrentSession
            CurrentSession.Messages = new List<AiChatMessage>(Messages);
            CurrentSession.UpdatedAt = DateTime.Now;

            // 如果标题还是默认的，尝试从第一条用户消息自动截取
            if (CurrentSession.Title == "新对话")
            {
                var firstUser = CurrentSession.Messages.FirstOrDefault(m => m.Role == AiChatRole.User);
                if (firstUser != null && !string.IsNullOrWhiteSpace(firstUser.Content))
                {
                    CurrentSession.Title = firstUser.Content.Length > 20
                        ? firstUser.Content.Substring(0, 20) + "…"
                        : firstUser.Content;
                }
            }

            Sessions.Insert(0, CurrentSession);
            SaveSessionsToDisk();
        }

        /// <summary>
        /// 将所有历史会话保存到磁盘
        /// </summary>
        private void SaveSessionsToDisk()
        {
            AiChatSessionStore.Save(new List<AiChatSession>(Sessions));
        }

        /// <summary>
        /// 发送用户消息并生成方案
        /// </summary>
        [RelayCommand]
        private async Task SendMessageAsync()
        {
            var userInput = InputText?.Trim();
            if (string.IsNullOrEmpty(userInput) || IsGenerating)
                return;

            // 清除重试按钮
            ShowRetryButton = false;

            // 强制命令解析：运行#N / 执行#N / 单步运行#N（跳过 AI，直接执行）
            var cmdMatch = System.Text.RegularExpressions.Regex.Match(
                userInput, @"^(运行|执行|单步运行)\s*#(\d+)\s*(.*)$");
            if (cmdMatch.Success)
            {
                var order = int.Parse(cmdMatch.Groups[2].Value);
                AddMessage(AiChatRole.User, userInput);
                InputText = "";

                var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == order);
                if (card == null)
                {
                    AddMessage(AiChatRole.System, $"❌ 未找到 #{order} 号卡片");
                    return;
                }

                AiFlowLogger.Info($"正在执行 #{order} {card.Name}...");
                try
                {
                    using var cts = new CancellationTokenSource();
                    await _mainViewModel.ExecuteSingleCardAsync(card, cts.Token);
                    var statusText = card.Status == Models.TaskCards.TaskStatus.Success ? "✅ 成功" : $"❌ 失败: {card.ErrorMessage}";
                    AiFlowLogger.Info($"执行结果: {statusText}");

                    // 序列化输出信息
                    var resultInfo = _serializer.SerializeCardResults(new List<int> { order });
                    if (!string.IsNullOrWhiteSpace(resultInfo))
                        AiFlowLogger.Info($"输出详情:\n{resultInfo}");
                }
                catch (Exception ex)
                {
                    AddMessage(AiChatRole.System, $"❌ 执行异常: {ex.Message}");
                }
                return;
            }

            // 检查模型设置
            if (string.IsNullOrEmpty(SelectedModelId))
            {
                AddMessage(AiChatRole.System, "⚠️ 请先在模型管理中配置模型，并在下方选择一个模型。");
                return;
            }

            // 添加用户消息
            AddMessage(AiChatRole.User, userInput);
            _lastUserInput = userInput;
            InputText = "";
            IsGenerating = true;
            PendingPlan = null;

            _cts = new CancellationTokenSource();

            // 日志记录会话开始
            AiFlowLogger.LogSessionStart(userInput, SelectedModelId);

            try
            {
                // 阶段1：确定类别（可通过设置跳过）
                AddMessage(AiChatRole.System, "✦ 正在思考中.");
                StartThinkingAnimation("✦ 正在思考中");

                List<string> categories;
                if (_mainViewModel.Settings.OrchidSingleStage)
                {
                    // 单次调用模式：跳过类别判断，直接使用全部类别
                    categories = new List<string>();
                    AiFlowLogger.Info("单次调用模式：跳过类别判断，直接生成方案...");
                }
                else
                {
                    AiFlowLogger.Info("正在分析需求，确定涉及的卡片类别...");
                    var (cats, tokens1In, tokens1Out) = await _service.DetermineCategoriesAsync(
                        userInput, SelectedModelId, _cts.Token);
                    categories = cats;
                    AiFlowLogger.Info($"已确定涉及类别：{string.Join("、", categories)}（Token: {tokens1In}+{tokens1Out}）");
                }

                // 阶段2：生成详细方案
                UpdateLastSystemMessage("✦ 正在生成方案.");
                _thinkingBaseText = "✦ 正在生成方案";
                _thinkingDotCount = 0;
                AiFlowLogger.Info("正在生成流程方案...");

                // 序列化当前画布卡片作为上下文
                var currentFlowContext = _serializer.SerializeCurrentFlow();

                // 构建最近对话历史（最多取最近 3 轮用户+助手消息）
                var history = _serializer.BuildConversationHistory(Messages);

                // 收集图片数据（附件 + 卡片输出图像）
                var imageBase64List = new List<string>();

                // 用户附件图片
                if (!string.IsNullOrEmpty(AttachedFilePath) && System.IO.File.Exists(AttachedFilePath))
                {
                    try
                    {
                        var ext = System.IO.Path.GetExtension(AttachedFilePath).ToLower();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")
                        {
                            var bytes = System.IO.File.ReadAllBytes(AttachedFilePath);
                            imageBase64List.Add(Convert.ToBase64String(bytes));
                            AiFlowLogger.Info($"已附加用户图片: {System.IO.Path.GetFileName(AttachedFilePath)} ({bytes.Length / 1024}KB)");
                        }
                        else
                        {
                            // 非图片文件：读取内容作为文本附加到 prompt
                            var fileContent = System.IO.File.ReadAllText(AttachedFilePath);
                            userInput += $"\n\n[附件内容: {System.IO.Path.GetFileName(AttachedFilePath)}]\n{fileContent}";
                            AiFlowLogger.Info($"已附加文件内容: {System.IO.Path.GetFileName(AttachedFilePath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        AiFlowLogger.Warn($"读取附件失败: {ex.Message}");
                    }
                }

                // 卡片输出图像（收集所有有 OutputImage 的卡片）
                int? lastImageWidth = null, lastImageHeight = null;
                foreach (var card in _mainViewModel.TaskCards)
                {
                    if (card.OutputsImage && card.OutputImage != null && !card.OutputImage.Empty())
                    {
                        try
                        {
                            OpenCvSharp.Cv2.ImEncode(".png", card.OutputImage, out var pngBytes);
                            imageBase64List.Add(Convert.ToBase64String(pngBytes));
                            lastImageWidth = card.OutputImage.Width;
                            lastImageHeight = card.OutputImage.Height;
                            AiFlowLogger.Info($"已附加卡片输出图像: #{card.Order} {card.Name} ({pngBytes.Length / 1024}KB)");
                        }
                        catch (Exception ex)
                        {
                            AiFlowLogger.Warn($"编码卡片输出图像失败 ({card.Name}): {ex.Message}");
                        }
                    }
                }

                // 截屏已改为由 AI 通过 needsScreenshot 按需请求，初始消息不自动截屏

                // 存在截图时，注入标定校正信息
                if (imageBase64List.Count > 0 && lastImageWidth.HasValue && lastImageHeight.HasValue
                    && !string.IsNullOrEmpty(SelectedModelId))
                {
                    var cal = CalibrationService.GetCalibration(SelectedModelId, lastImageWidth.Value, lastImageHeight.Value);
                    if (cal == null)
                    {
                        // 无标定数据，自动执行标定
                        AiFlowLogger.Info("[标定] 检测到截图但无标定数据，自动执行标定...");
                        try
                        {
                            var calibService = new CalibrationService(msg => AiFlowLogger.Info(msg));
                            cal = await calibService.CalibrateAsync(
                                SelectedModelId, lastImageWidth.Value, lastImageHeight.Value,
                                _cts.Token);
                        }
                        catch (Exception ex)
                        {
                            AiFlowLogger.Warn($"[标定] 自动标定失败: {ex.Message}");
                        }
                    }
                    if (cal != null)
                    {
                        // 将校正公式注入到流程上下文中（而非用户消息），避免污染用户输入
                        currentFlowContext += $"\n\n[坐标校正] 当前模型在分辨率({cal.Width}x{cal.Height})下的坐标估算存在系统偏差，" +
                                             $"当需要设置坐标时请应用校正公式：" +
                                             $"correctedX = {cal.ScaleX:F4} * rawX + {cal.OffsetX:F1}，" +
                                             $"correctedY = {cal.ScaleY:F4} * rawY + {cal.OffsetY:F1}。";
                        AiFlowLogger.Info($"[标定] 已注入校正公式到流程上下文");
                    }
                }

                var (plan, tokens2In, tokens2Out) = await _service.GeneratePlanAsync(
                    userInput, categories, SelectedModelId, _cts.Token, currentFlowContext, history, CurrentMode,
                    imageBase64List.Count > 0 ? imageBase64List : null);

                // 判断方案是否有效内容（卡片、变量、流程或删除操作）
                bool hasSteps = plan.Plan.Count > 0;
                bool hasVariables = plan.Variables != null && plan.Variables.Count > 0;
                bool hasDeletes = plan.DeleteVariables != null && plan.DeleteVariables.Count > 0;
                bool hasModifies = plan.ModifyVariables != null && plan.ModifyVariables.Count > 0;
                bool hasCardModifies = plan.ModifyCards != null && plan.ModifyCards.Count > 0;
                bool hasCardDeletes = plan.DeleteCards != null && plan.DeleteCards.Count > 0;
                bool hasRunCards = plan.RunCards != null && plan.RunCards.Count > 0;
                bool hasInsertCards = plan.InsertCards != null && plan.InsertCards.Count > 0;
                bool hasFlowOps2 = (plan.CreateFlows != null && plan.CreateFlows.Count > 0)
                    || (plan.DeleteFlows != null && plan.DeleteFlows.Count > 0)
                    || !string.IsNullOrWhiteSpace(plan.SwitchFlow);
                bool hasShellCmds2 = plan.ShellCommands != null && plan.ShellCommands.Count > 0;

                if (!hasSteps && !hasVariables && !hasDeletes && !hasModifies && !hasCardModifies && !hasCardDeletes && !hasRunCards && !hasInsertCards && !hasFlowOps2 && !hasShellCmds2)
                {
                    // 分析模式：AI 仅返回了分析结果（无需创建卡片或变量）
                    if (!string.IsNullOrEmpty(plan.Summary))
                    {
                        AiFlowLogger.Info($"分析完成（Token: {tokens2In}+{tokens2Out}）");
                        RemoveLastSystemMessage();
                        AddMessage(AiChatRole.Assistant, plan.Summary);
                    }
                    else
                    {
                        AddMessage(AiChatRole.System, "❌ AI 未能生成有效方案，请尝试更详细的需求描述。");
                    }
                    return;
                }

                AiFlowLogger.Info($"方案生成完成（Token: {tokens2In}+{tokens2Out}）");

                // 移除思考中提示
                RemoveLastSystemMessage();

                // 纯 shellCommands 方案（无卡片/变量操作）：自主模式下直接执行，无需"确认创建"
                bool isShellOnly = hasShellCmds2 && !hasSteps && !hasVariables && !hasDeletes
                    && !hasModifies && !hasCardModifies && !hasCardDeletes && !hasRunCards
                    && !hasInsertCards && !hasFlowOps2;

                if (isShellOnly && CurrentMode == AiAssistantMode.Autonomous)
                {
                    if (!string.IsNullOrEmpty(plan.Summary))
                        AddMessage(AiChatRole.Assistant, plan.Summary);

                    AiFlowLogger.Info("纯 PowerShell 命令方案，直接执行...");
                    await ExecuteAutonomousLoopAsync(plan);
                    return;
                }

                // 显示方案（含确认/拒绝按钮）
                PendingPlan = plan;
                var planMsg = _serializer.FormatPlanAsText(plan);
                AddMessage(AiChatRole.Assistant, planMsg, plan);
            }
            catch (OperationCanceledException)
            {
                AiFlowLogger.Info("用户取消了操作");
                AddMessage(AiChatRole.System, "⚠️ 操作已取消。");
            }
            catch (Exception ex)
            {
                AiFlowLogger.Error("方案生成失败", ex);
                AddMessage(AiChatRole.System, $"❌ 生成失败: {ex.Message}");
                ShowRetryButton = true;
            }
            finally
            {
                StopThinkingAnimation();
                IsGenerating = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// 取消当前生成
        /// </summary>
        [RelayCommand]
        private void CancelGeneration()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// 确认当前方案并创建卡片
        /// </summary>
        [RelayCommand]
        private async Task ConfirmPlanAsync()
        {
            if (PendingPlan == null)
                return;

            // 判断方案是否有有效内容（含流程操作）
            bool hasSteps = PendingPlan.Plan.Count > 0;
            bool hasVariables = PendingPlan.Variables != null && PendingPlan.Variables.Count > 0;
            bool hasDeletes = PendingPlan.DeleteVariables != null && PendingPlan.DeleteVariables.Count > 0;
            bool hasModifies = PendingPlan.ModifyVariables != null && PendingPlan.ModifyVariables.Count > 0;
            bool hasCardModifies = PendingPlan.ModifyCards != null && PendingPlan.ModifyCards.Count > 0;
            bool hasCardDeletes = PendingPlan.DeleteCards != null && PendingPlan.DeleteCards.Count > 0;
            bool hasRunCards = PendingPlan.RunCards != null && PendingPlan.RunCards.Count > 0;
            bool hasInsertCards = PendingPlan.InsertCards != null && PendingPlan.InsertCards.Count > 0;
            bool hasFlowOps = (PendingPlan.CreateFlows != null && PendingPlan.CreateFlows.Count > 0)
                || (PendingPlan.DeleteFlows != null && PendingPlan.DeleteFlows.Count > 0)
                || !string.IsNullOrWhiteSpace(PendingPlan.SwitchFlow);
            bool hasShellCmds = PendingPlan.ShellCommands != null && PendingPlan.ShellCommands.Count > 0;
            if (!hasSteps && !hasVariables && !hasDeletes && !hasModifies && !hasCardModifies && !hasCardDeletes && !hasRunCards && !hasInsertCards && !hasFlowOps && !hasShellCmds)
                return;

            try
            {
                AiFlowLogger.Info($"用户确认方案，开始创建（{PendingPlan.Plan.Count} 个步骤, {PendingPlan.Variables?.Count ?? 0} 个变量）");
                var (createdCount, reports) = _planExecutor.CreateTaskCardsFromPlan(PendingPlan, CurrentMode, SelectedModelId);

                // 显示报告（仅有待配置项时）
                ReportItems.Clear();
                foreach (var item in reports)
                    ReportItems.Add(item);

                // 自主模式下不显示待配置报告（避免打断自动执行流程）
                if (reports.Count > 0 && CurrentMode != AiAssistantMode.Autonomous)
                {
                    var reportText = _serializer.FormatReportAsText(createdCount, reports);
                    AddMessage(AiChatRole.System, reportText, reportItems: reports);
                }

                var currentPlan = PendingPlan;
                PendingPlan = null;

                // 将方案气泡底部提示文字更新为"已确认"
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var planMsg = Messages.LastOrDefault(m => m.Plan != null);
                    if (planMsg != null)
                    {
                        var idx = Messages.IndexOf(planMsg);
                        if (idx >= 0)
                        {
                            // 替换底部确认提示（兼容 \r\n 换行）
                            var oldContent = planMsg.Content;
                            var marker = "✅ 确认无误后点击";
                            var confirmIdx = oldContent.IndexOf(marker);
                            if (confirmIdx >= 0)
                            {
                                // 截取到该行之前
                                var lineStart = confirmIdx;
                                while (lineStart > 0 && oldContent[lineStart - 1] != '\n' && oldContent[lineStart - 1] != '\r')
                                    lineStart--;
                                planMsg.Content = oldContent.Substring(0, lineStart).TrimEnd() + "\n✅ 已确认";
                            }

                            // 直接更新属性（已继承 ObservableObject）
                            planMsg.Plan = null; // 清除关联，触发 UI 按钮隐藏
                        }
                    }
                });
                _mainViewModel.RecalculateIndentLevels();

                // 如果方案包含 runCards，进入自主执行循环
                if (hasRunCards)
                {
                    await ExecuteAutonomousLoopAsync(currentPlan);
                }
                // 自主模式下：创建了新卡片但没有 runCards 时，自动将新卡片加入执行循环
                else if (CurrentMode == AiAssistantMode.Autonomous && hasSteps && createdCount > 0)
                {
                    // 收集新创建卡片的 order
                    var newOrders = _mainViewModel.TaskCards
                        .OrderByDescending(c => c.Order)
                        .Take(createdCount)
                        .Select(c => c.Order)
                        .OrderBy(o => o)
                        .ToList();

                    currentPlan.RunCards = newOrders;
                    AiFlowLogger.Info($"自主模式：自动运行新创建的 {createdCount} 张卡片...");
                    await ExecuteAutonomousLoopAsync(currentPlan);
                }
                // 自主模式下：仅有 shellCommands 时，直接执行并进入自主循环
                else if (CurrentMode == AiAssistantMode.Autonomous && hasShellCmds)
                {
                    AiFlowLogger.Info("自主模式：执行 PowerShell 命令...");
                    await ExecuteAutonomousLoopAsync(currentPlan);
                }
            }
            catch (Exception ex)
            {
                AiFlowLogger.Error("创建失败", ex);
                AddMessage(AiChatRole.System, $"❌ 创建失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重新生成方案
        /// </summary>
        [RelayCommand]
        private async Task RegenerateAsync()
        {
            // 查找最后一条用户消息
            var lastUserMsg = Messages.LastOrDefault(m => m.Role == AiChatRole.User);
            if (lastUserMsg != null)
            {
                InputText = lastUserMsg.Content;
                PendingPlan = null;
                await SendMessageAsync();
            }
        }

        /// <summary>
        /// 重试上次失败的生成
        /// </summary>
        [RelayCommand]
        private async Task RetryAsync()
        {
            if (string.IsNullOrEmpty(_lastUserInput) || IsGenerating) return;
            ShowRetryButton = false;
            InputText = _lastUserInput;
            await SendMessageAsync();
        }

        /// <summary>
        /// 当报告项被点击时，打开对应的属性对话框
        /// </summary>
        public void OnReportItemClicked(AiFlowReportItem item)
        {
            // 选中卡片并弹出属性对话框
            var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Id == item.TaskCardId);
            if (card != null)
            {
                _mainViewModel.SelectTaskCommand.Execute(card);
                OpenCardPropertyRequested?.Invoke(item.TaskCardId);
            }

            // 从消息的 ReportItems 中移除该配置项
            Application.Current.Dispatcher.Invoke(() =>
            {
                var msg = Messages.FirstOrDefault(m => m.ReportItems != null && m.ReportItems.Contains(item));
                if (msg != null)
                {
                    msg.ReportItems!.Remove(item);
                    ReportItems.Remove(item);

                    // 如果所有配置项都已处理，移除整条配置消息
                    if (msg.ReportItems.Count == 0)
                    {
                        Messages.Remove(msg);
                    }
                    else
                    {
                        // 刷新消息以触发 UI 更新
                        var idx = Messages.IndexOf(msg);
                        if (idx >= 0)
                        {
                            msg.Content = $"⚠️ {msg.ReportItems.Count} 项需要手动配置：";
                            Messages[idx] = msg; // 触发集合变更通知
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 用户批准执行（自主模式中风险操作暂停时调用）
        /// </summary>
        public void ApproveExecution()
        {
            AwaitingApproval = false;
            ApprovalDescription = "";
            _approvalTcs?.TrySetResult(true);
        }

        /// <summary>
        /// 用户中止执行（自主模式中风险操作暂停时调用）
        /// </summary>
        public void AbortExecution()
        {
            AwaitingApproval = false;
            ApprovalDescription = "";
            _approvalTcs?.TrySetResult(false);
        }

        /// <summary>
        /// 暂停等待用户批准，返回 true=批准，false=中止
        /// </summary>
        private async Task<bool> WaitForApprovalAsync(string description, CancellationToken ct)
        {
            _approvalTcs = new TaskCompletionSource<bool>();
            ApprovalDescription = description;
            AwaitingApproval = true;

            // 注册取消回调
            using var reg = ct.Register(() => _approvalTcs.TrySetResult(false));

            var result = await _approvalTcs.Task;
            _approvalTcs = null;
            return result;
        }



        /// <summary>
        /// AI 自主执行循环：运行指定卡片 → 读取结果 → 再调 LLM 决策 → 重复直到完成
        /// </summary>
        private async Task ExecuteAutonomousLoopAsync(AiFlowPlanResponse initialPlan)
        {
            IsAiExecuting = true;
            _cts = new CancellationTokenSource();

            try
            {
                var currentPlan = initialPlan;
                int maxRounds = 15; // 防止无限循环
                int round = 0;

                while (!_cts.Token.IsCancellationRequested && round < maxRounds)
                {
                    round++;

                    // 已标记完成，退出循环
                    if (currentPlan.Done)
                    {
                        AddMessage(AiChatRole.System, $"✅ AI 自主任务完成: {currentPlan.Summary}");
                        break;
                    }

                    // 没有要运行的卡片且没有其他操作，退出循环
                    bool hasRunCards = currentPlan.RunCards != null && currentPlan.RunCards.Count > 0;
                    bool hasShellCommands = currentPlan.ShellCommands != null && currentPlan.ShellCommands.Count > 0;
                    bool hasOtherActions = (currentPlan.Plan?.Count > 0) ||
                        (currentPlan.Variables?.Count > 0) ||
                        (currentPlan.DeleteVariables?.Count > 0) ||
                        (currentPlan.ModifyVariables?.Count > 0) ||
                        (currentPlan.ModifyCards?.Count > 0) ||
                        (currentPlan.DeleteCards?.Count > 0) ||
                        hasShellCommands;

                    if (!hasRunCards && !hasOtherActions)
                    {
                        AiFlowLogger.Info($"AI 自主执行结束: {currentPlan.Summary}");
                        break;
                    }

                    if (!hasRunCards)
                    {
                        // 有其他操作但没有 runCards，继续下一轮让 AI 决策
                        AiFlowLogger.Info("AI 执行了操作，继续决策...");
                    }

                    // ===== PowerShell 命令执行 =====
                    string shellResultsText = "";
                    if (hasShellCommands)
                    {
                        shellResultsText = await ExecuteShellCommandsAsync(currentPlan.ShellCommands!, _cts.Token);
                    }

                    string resultsText = "";
                    bool hasFailedCards = false;
                    if (hasRunCards)
                    {
                        // 运行指定卡片（带风险分级批准）
                        AiFlowLogger.Info($"AI 自主执行中（第 {round} 轮）：运行卡片 {string.Join(", ", currentPlan.RunCards!.Select(o => $"#{o}"))}");

                        foreach (var order in currentPlan.RunCards)
                        {
                            if (_cts.Token.IsCancellationRequested) break;

                            var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == order);
                            if (card == null)
                            {
                                AiFlowLogger.Warn($"自主模式: 卡片 #{order} 不存在，跳过运行");
                                continue;
                            }

                            // 风险分级检查
                            var riskLevel = TaskRiskClassifier.GetRiskLevel(card.TaskType);
                            var riskIcon = TaskRiskClassifier.GetRiskIcon(riskLevel);

                            if (riskLevel == TaskRiskLevel.Low)
                            {
                                // 低风险：自动执行
                                AiFlowLogger.Info($"{riskIcon} 自动执行: #{order} {card.Name}");
                            }
                            else
                            {
                                // 中/高风险：暂停等待用户批准
                                var riskDesc = TaskRiskClassifier.GetRiskDescription(riskLevel);
                                var approvalMsg = riskLevel == TaskRiskLevel.High
                                    ? $"⚠️ 高风险操作: #{order} {card.Name} [{card.TaskType}]"
                                    : $"即将执行: #{order} {card.Name} [{card.TaskType}]";

                                AiFlowLogger.Info($"{riskIcon} {riskDesc} — {approvalMsg}");

                                var approved = await WaitForApprovalAsync(
                                    $"{riskIcon} {approvalMsg}", _cts.Token);

                                if (!approved)
                                {
                                    AddMessage(AiChatRole.System, "⏹ 用户中止了执行。");
                                    return; // 直接退出循环
                                }

                                AiFlowLogger.Info($"已批准，执行 #{order} {card.Name}...");
                            }

                            AiFlowLogger.Info($"自主模式: 运行卡片 #{order} {card.Name} (风险: {riskLevel})");

                            // WinClick 自动标定：执行前检查并触发标定
                            if (card is WinClickTaskCard clickCard
                                && clickCard.StartX != 0 && clickCard.StartY != 0
                                && string.IsNullOrEmpty(clickCard.StartXExpression))
                            {
                                var ssCard = _mainViewModel.TaskCards
                                    .LastOrDefault(c => c is WinScreenshotTaskCard && c.OutputImage != null && !c.OutputImage.Empty());
                                if (ssCard?.OutputImage != null)
                                {
                                    int w = ssCard.OutputImage.Width, h = ssCard.OutputImage.Height;
                                    var cal = CalibrationService.GetCalibration(SelectedModelId, w, h);
                                    if (cal == null)
                                    {
                                        // 没有标定数据，自动执行标定
                                        AiFlowLogger.Info($"[标定] 首次使用模型估坐标，自动执行标定...");
                                        var calibService = new CalibrationService(msg => AiFlowLogger.Info(msg));
                                        cal = await calibService.CalibrateAsync(SelectedModelId, w, h, _cts.Token);
                                    }
                                    if (cal != null)
                                    {
                                        var (cx, cy) = CalibrationService.CalibrateCoordinates(cal, clickCard.StartX, clickCard.StartY);
                                        AiFlowLogger.Info($"标定校正: ({clickCard.StartX},{clickCard.StartY}) → ({cx},{cy})");
                                        clickCard.StartX = cx;
                                        clickCard.StartY = cy;
                                    }
                                }
                            }

                            await _mainViewModel.ExecuteSingleCardAsync(card, _cts.Token);

                            // 追踪失败卡片
                            if (card.Status == Models.TaskCards.TaskStatus.Failed)
                            {
                                hasFailedCards = true;
                                AiFlowLogger.Warn($"卡片 #{order} {card.Name} 执行失败: {card.ErrorMessage}");
                            }
                        }

                        if (_cts.Token.IsCancellationRequested) break;

                        // 序列化运行结果
                        resultsText = _serializer.SerializeCardResults(currentPlan.RunCards);
                        AiFlowLogger.Info($"运行结果:\n{resultsText}");
                    }

                    // 构建上下文：当前流程 + 运行结果 + 对话历史
                    var flowContext = _serializer.SerializeCurrentFlow();
                    var history = _serializer.BuildConversationHistory(Messages);

                    // 获取原始用户请求
                    var originalRequest = Messages.LastOrDefault(m => m.Role == AiChatRole.User)?.Content ?? "执行流程";

                    // 构建所有卡片状态清单
                    var allCardsInfo = new System.Text.StringBuilder();
                    allCardsInfo.AppendLine("当前画布上所有卡片：");
                    foreach (var c in _mainViewModel.TaskCards)
                    {
                        var statusMark = c.Status == Models.TaskCards.TaskStatus.Success ? "✅" :
                                         c.Status == Models.TaskCards.TaskStatus.Failed ? "❌" :
                                         c.Status == Models.TaskCards.TaskStatus.Running ? "🔄" : "⬜";
                        allCardsInfo.AppendLine($"  {statusMark} #{c.Order} {c.Name} [{c.TaskType}] - 状态: {c.Status}");
                    }

                    // 构建详细的用户消息
                    var autonomousPrompt = new System.Text.StringBuilder();
                    autonomousPrompt.AppendLine($"用户的原始请求是: {originalRequest}");
                    autonomousPrompt.AppendLine();
                    autonomousPrompt.AppendLine(allCardsInfo.ToString());

                    if (!string.IsNullOrEmpty(resultsText))
                    {
                        autonomousPrompt.AppendLine($"=== 第 {round} 轮运行结果 ===");
                        autonomousPrompt.AppendLine(resultsText);
                    }

                    if (!string.IsNullOrEmpty(shellResultsText))
                    {
                        autonomousPrompt.AppendLine(shellResultsText);
                    }

                    autonomousPrompt.AppendLine("请根据以上信息决定下一步：");
                    autonomousPrompt.AppendLine("- 如果还有状态为 Idle 的卡片需要运行，请在 runCards 中指定它们的 order");
                    autonomousPrompt.AppendLine("- 如果需要先修改某个卡片的属性再运行，请同时使用 modifyCards 和 runCards");
                    autonomousPrompt.AppendLine("- 当用户请求的操作已完成（所有相关卡片执行成功），必须立即设置 done: true，不要画蛇添足");
                    autonomousPrompt.AppendLine("- 严禁自行添加验证/确认/二次检查步骤（如截图验证、LlmVision 分析结果等），除非用户明确要求验证");
                    autonomousPrompt.AppendLine("- 你自己就是多模态 Vision 模型，不需要创建 LlmVision 卡片来分析图像，你已经能直接看到卡片输出的图像");

                    // 注入标定校正信息
                    var calSsCard = _mainViewModel.TaskCards
                        .LastOrDefault(c => c is Models.TaskCards.WinScreenshotTaskCard && c.OutputImage != null && !c.OutputImage.Empty());
                    if (calSsCard?.OutputImage != null && !string.IsNullOrEmpty(SelectedModelId))
                    {
                        var cal = CalibrationService.GetCalibration(SelectedModelId, calSsCard.OutputImage.Width, calSsCard.OutputImage.Height);
                        if (cal != null)
                        {
                            autonomousPrompt.AppendLine();
                            autonomousPrompt.AppendLine($"[坐标校正] 你在此分辨率({cal.Width}x{cal.Height})下的坐标估算存在系统偏差，" +
                                $"请对你估算的所有坐标应用校正公式：" +
                                $"correctedX = {cal.ScaleX:F4} * rawX + {cal.OffsetX:F1}，" +
                                $"correctedY = {cal.ScaleY:F4} * rawY + {cal.OffsetY:F1}。");
                        }
                    }

                    // 失败回退指令
                    if (hasFailedCards)
                    {
                        autonomousPrompt.AppendLine();
                        autonomousPrompt.AppendLine("⚠️ 有卡片执行失败！请在响应中指定 failureStrategy：");
                        autonomousPrompt.AppendLine("- \"retry\"：重试当前卡片（适用于临时错误，如网络超时）");
                        autonomousPrompt.AppendLine("- \"fallback\"：删除失败卡片(deleteCards)，用替代方案(plan 或 fallbackPlan)代替");
                        autonomousPrompt.AppendLine("  例如：WinUiAutomation 失败 → 改用 WinClick 坐标点击");
                        autonomousPrompt.AppendLine("- \"abort\"：任务无法继续，在 summary 中说明原因，设置 done: true");
                    }

                    // 自主模式下传入空 categories，GeneratePlanAsync 会使用所有类别
                    var categories = new List<string>();

                    // Orchid 按需截屏：仅当 AI 请求时截取屏幕
                    List<string>? autoImageList = null;
                    int autoScreenW = 0, autoScreenH = 0;
                    if (currentPlan.NeedsScreenshot)
                    {
                        AiFlowLogger.Info("Orchid 按需截屏中...");
                        var (scrBase64, sw, sh) = await CaptureScreenForAiAsync();
                        if (scrBase64 != null)
                        {
                            autoImageList = new List<string> { scrBase64 };
                            autoScreenW = sw;
                            autoScreenH = sh;
                            AiFlowLogger.Info($"已附加屏幕截图 ({sw}x{sh})");
                            AddMessage(AiChatRole.System, $"📸 已截取全屏 ({sw}x{sh})");
                        }
                    }

                    // 有截图时自动标定
                    if (autoImageList?.Count > 0 && autoScreenW > 0
                        && !string.IsNullOrEmpty(SelectedModelId))
                    {
                        var existingCal = CalibrationService.GetCalibration(SelectedModelId, autoScreenW, autoScreenH);
                        if (existingCal == null)
                        {
                            AiFlowLogger.Info("[标定] 自主循环检测到截图但无标定数据，自动执行标定...");
                            try
                            {
                                var calibSvc = new CalibrationService(msg => AiFlowLogger.Info(msg));
                                var newCal = await calibSvc.CalibrateAsync(SelectedModelId, autoScreenW, autoScreenH, _cts.Token);
                                if (newCal != null)
                                {
                                    // 重新注入校正公式到 prompt
                                    autonomousPrompt.AppendLine();
                                    autonomousPrompt.AppendLine($"[坐标校正] 你在此分辨率({newCal.Width}x{newCal.Height})下的坐标估算存在系统偏差，" +
                                        $"请对你估算的所有坐标应用校正公式：" +
                                        $"correctedX = {newCal.ScaleX:F4} * rawX + {newCal.OffsetX:F1}，" +
                                        $"correctedY = {newCal.ScaleY:F4} * rawY + {newCal.OffsetY:F1}。");
                                }
                            }
                            catch (Exception ex)
                            {
                                AiFlowLogger.Warn($"[标定] 自动标定失败: {ex.Message}");
                            }
                        }
                    }

                    // 再次调用 LLM 获取下一步决策（传入截图图像）
                    AiFlowLogger.Info("AI 正在分析结果并决策下一步...");
                    var (nextPlan, tokensIn, tokensOut) = await _service.GeneratePlanAsync(
                        autonomousPrompt.ToString(),
                        categories, SelectedModelId, _cts.Token, flowContext, history, AiAssistantMode.Autonomous,
                        autoImageList);

                    AiFlowLogger.Info($"AI 决策完成（Token: {tokensIn}+{tokensOut}）");

                    // 处理 AI 的新操作（创建、修改、删除等）
                    // 处理后续轮次的 PowerShell 命令
                    if (nextPlan.ShellCommands != null && nextPlan.ShellCommands.Count > 0)
                    {
                        var nextShellResults = await ExecuteShellCommandsAsync(nextPlan.ShellCommands, _cts.Token);
                        if (!string.IsNullOrEmpty(nextShellResults))
                            shellResultsText = nextShellResults;
                    }

                    bool hasNewActions = (nextPlan.Plan?.Count > 0) ||
                        (nextPlan.Variables?.Count > 0) ||
                        (nextPlan.DeleteVariables?.Count > 0) ||
                        (nextPlan.ModifyVariables?.Count > 0) ||
                        (nextPlan.ModifyCards?.Count > 0) ||
                        (nextPlan.DeleteCards?.Count > 0);

                    if (hasNewActions)
                    {
                        var (count, reports) = _planExecutor.CreateTaskCardsFromPlan(nextPlan, CurrentMode, SelectedModelId);
                        _mainViewModel.RecalculateIndentLevels();

                        if (!string.IsNullOrEmpty(nextPlan.Summary))
                            AddMessage(AiChatRole.Assistant, nextPlan.Summary);
                    }
                    else if (!string.IsNullOrEmpty(nextPlan.Summary))
                    {
                        AddMessage(AiChatRole.Assistant, nextPlan.Summary);
                    }

                    // 处理失败回退策略
                    if (!string.IsNullOrEmpty(nextPlan.FailureStrategy))
                    {
                        var strategy = nextPlan.FailureStrategy.ToLowerInvariant();
                        if (strategy == "retry")
                        {
                            AiFlowLogger.Info("AI 选择重试失败的卡片...");
                            // retry 时 AI 应在 runCards 中重新指定卡片
                        }
                        else if (strategy == "fallback")
                        {
                            AiFlowLogger.Info("AI 选择使用替代方案...");
                            // fallback 时 AI 应通过 deleteCards + plan/fallbackPlan 提供替代
                            if (nextPlan.FallbackPlan?.Count > 0)
                            {
                                var fallbackResponse = new AiFlowPlanResponse { Plan = nextPlan.FallbackPlan };
                                var (fbCount, fbReports) = _planExecutor.CreateTaskCardsFromPlan(fallbackResponse, CurrentMode, SelectedModelId);
                                _mainViewModel.RecalculateIndentLevels();
                                AiFlowLogger.Info($"已创建 {fbCount} 张替代卡片");
                            }
                        }
                        else if (strategy == "abort")
                        {
                            AddMessage(AiChatRole.System, $"⛔ AI 决定中止任务: {nextPlan.Summary}");
                            break;
                        }
                    }

                    currentPlan = nextPlan;
                }

                if (round >= maxRounds)
                {
                    AddMessage(AiChatRole.System, "⚠️ 自主执行已达最大轮数限制（15 轮），自动停止。");
                }
            }
            catch (OperationCanceledException)
            {
                AddMessage(AiChatRole.System, "⏹ AI 自主执行已被中断。");
            }
            catch (Exception ex)
            {
                AiFlowLogger.Error("自主执行异常", ex);
                AddMessage(AiChatRole.System, $"❌ AI 自主执行异常: {ex.Message}");
            }
            finally
            {
                IsAiExecuting = false;
            }
        }


        /// <summary>
        /// 添加消息到列表
        /// </summary>
        private void AddMessage(AiChatRole role, string content, AiFlowPlanResponse? plan = null, List<AiFlowReportItem>? reportItems = null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Messages.Add(new AiChatMessage
                {
                    Role = role,
                    Content = content,
                    Plan = plan,
                    ReportItems = reportItems
                });
                ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
            });
        }

        /// <summary>
        /// 更新最后一条系统消息
        /// </summary>
        private void UpdateLastSystemMessage(string content)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var last = Messages.LastOrDefault(m => m.Role == AiChatRole.System);
                if (last != null)
                {
                    var idx = Messages.IndexOf(last);
                    Messages[idx] = new AiChatMessage
                    {
                        Role = AiChatRole.System,
                        Content = content,
                        Timestamp = DateTime.Now
                    };
                }
            });
        }

        /// <summary>
        /// 移除最后一条系统消息（用于清除思考中提示）
        /// </summary>
        private void RemoveLastSystemMessage()
        {
            StopThinkingAnimation();
            Application.Current.Dispatcher.Invoke(() =>
            {
                var last = Messages.LastOrDefault(m => m.Role == AiChatRole.System);
                if (last != null)
                    Messages.Remove(last);
            });
        }

        /// <summary>
        /// 启动思考中的点点动画（. → .. → ...）
        /// </summary>
        private void StartThinkingAnimation(string baseText)
        {
            _thinkingBaseText = baseText;
            _thinkingDotCount = 0;
            Application.Current.Dispatcher.Invoke(() =>
            {
                _thinkingTimer?.Stop();
                _thinkingTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(400)
                };
                _thinkingTimer.Tick += (_, _) =>
                {
                    _thinkingDotCount = (_thinkingDotCount % 3) + 1;
                    var dots = new string('.', _thinkingDotCount);
                    var last = Messages.LastOrDefault(m => m.Role == AiChatRole.System);
                    if (last != null)
                    {
                        var idx = Messages.IndexOf(last);
                        if (idx >= 0)
                        {
                            Messages[idx] = new AiChatMessage
                            {
                                Role = AiChatRole.System,
                                Content = $"{_thinkingBaseText}{dots}",
                                Timestamp = DateTime.Now
                            };
                        }
                    }
                };
                _thinkingTimer.Start();
            });
        }

        /// <summary>
        /// 停止思考中动画
        /// </summary>
        private void StopThinkingAnimation()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _thinkingTimer?.Stop();
                _thinkingTimer = null;
            });
        }

        /// <summary>
        /// 执行 AI 请求的 PowerShell 命令列表（含安全检查和用户批准流程）
        /// </summary>
        private async Task<string> ExecuteShellCommandsAsync(
            List<Models.AiFlow.AiShellCommand> commands, CancellationToken ct)
        {
            var results = new List<(Models.AiFlow.AiShellCommand Cmd, PowerShellExecutorService.ShellResult Result)>();

            foreach (var cmd in commands)
            {
                if (ct.IsCancellationRequested) break;

                // 安全检查
                var safety = _psService.CheckCommandSafety(cmd);

                if (!string.IsNullOrEmpty(safety.BlockReason))
                {
                    // 被拦截的危险命令
                    AiFlowLogger.Warn($"[PowerShell] 拦截: {cmd.Command} — {safety.BlockReason}");
                    AddMessage(AiChatRole.System, $"🚫 PowerShell 已拦截: {safety.BlockReason}\n`{cmd.Command}`");
                    results.Add((cmd, safety));
                    continue;
                }

                if (safety.NeedsApproval)
                {
                    // 非白名单命令，需要用户批准
                    AiFlowLogger.Info($"[PowerShell] 需要批准: {cmd.Command}");
                    var approved = await WaitForApprovalAsync(
                        $"💻 PowerShell 执行请求:\n{cmd.Command}\n用途: {cmd.Description}", ct);

                    if (!approved)
                    {
                        AiFlowLogger.Info("[PowerShell] 用户拒绝执行");
                        results.Add((cmd, new PowerShellExecutorService.ShellResult
                        {
                            Success = false,
                            Error = "用户拒绝执行"
                        }));
                        continue;
                    }
                }
                else
                {
                    // 白名单命令，自动执行
                    AiFlowLogger.Info($"[PowerShell] 🟢 白名单自动执行: {cmd.Command}");
                }

                // 面板提示
                AddMessage(AiChatRole.System, $"💻 执行 PowerShell: `{cmd.Command}`");

                // 执行命令
                var result = await _psService.ExecuteAsync(cmd, ct);
                results.Add((cmd, result));

                // 显示结果摘要
                if (result.Success)
                {
                    var outputPreview = result.Output.Length > 100
                        ? result.Output[..100] + "..."
                        : result.Output;
                    if (!string.IsNullOrWhiteSpace(outputPreview))
                        AddMessage(AiChatRole.System, $"📋 输出: {outputPreview}");
                }
                else
                {
                    AddMessage(AiChatRole.System, $"❌ PowerShell 执行失败: {result.Error}");
                }
            }

            return results.Count > 0
                ? PowerShellExecutorService.SerializeResults(results)
                : "";
        }
    }
}
