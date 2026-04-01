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
        /// 主 ViewModel 引用（供 View 层检查全局状态）
        /// </summary>
        public MainViewModel MainVm => _mainViewModel;
        private CancellationTokenSource? _cts;


        // 加载状态循环提示
        private System.Windows.Threading.DispatcherTimer? _loadingStatusTimer;
        private int _loadingStatusIndex;
        private static readonly string[] LoadingStatusTexts = { "Generating...", "Waiting...", "Running..." };

        /// <summary>
        /// 加载状态提示文本（Generating.../Waiting.../Running... 循环显示）
        /// </summary>
        [ObservableProperty]
        private string _loadingStatusText = "";

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
        /// 是否处于忙碌状态（生成中或自主执行中），用于 XAML 绑定
        /// </summary>
        public bool IsBusy => IsGenerating || IsAiExecuting;

        /// <summary>
        /// 是否有待配置报告项，用于 XAML 绑定
        /// </summary>
        public bool HasReportItems => ReportItems?.Count > 0;

        // CommunityToolkit.Mvvm 生成的 partial method 钩子：
        // 当 IsGenerating / IsAiExecuting 变化时同步通知 IsBusy 和 加载状态
        partial void OnIsGeneratingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsBusy));
            UpdateLoadingStatusTimer();
        }
        partial void OnIsAiExecutingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsBusy));
            UpdateLoadingStatusTimer();
        }

        /// <summary>
        /// 根据 IsBusy 状态启停加载提示定时器
        /// </summary>
        private void UpdateLoadingStatusTimer()
        {
            if (IsBusy)
            {
                if (_loadingStatusTimer == null)
                {
                    _loadingStatusIndex = 0;
                    LoadingStatusText = LoadingStatusTexts[0];
                    _loadingStatusTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(1500)
                    };
                    _loadingStatusTimer.Tick += (_, _) =>
                    {
                        _loadingStatusIndex = (_loadingStatusIndex + 1) % LoadingStatusTexts.Length;
                        LoadingStatusText = LoadingStatusTexts[_loadingStatusIndex];
                    };
                    _loadingStatusTimer.Start();
                }
            }
            else
            {
                _loadingStatusTimer?.Stop();
                _loadingStatusTimer = null;
                LoadingStatusText = "";
            }
        }

        /// <summary>
        /// 是否显示重试按钮（生成失败时）
        /// </summary>
        [ObservableProperty]
        private bool _showRetryButton;

        /// <summary>
        /// 是否显示“请继续”按钮（回复被截断时）
        /// </summary>
        [ObservableProperty]
        private bool _showContinueButton;

        private string? _prefillAssistantContent; // 用于原生的大模型断点续写 (Assistant Prefill)

        /// <summary>
        /// 保存上次用户输入，用于重试
        /// </summary>
        private string? _lastUserInput;

        /// <summary>
        /// 当前显示的加载/思考提示文本，为空表示不显示
        /// </summary>
        [ObservableProperty]
        private string? _loadingText;

        /// <summary>
        /// 选中的模型 ID
        /// </summary>
        [ObservableProperty]
        private string _selectedModelId = "";

        /// <summary>
        /// 当前 AI 助手模式（统一模式：始终为 Autonomous，保留字段仅为序列化兼容）
        /// </summary>
        [ObservableProperty]
        private AiAssistantMode _currentMode = AiAssistantMode.Autonomous;

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

        // WebView2 流式事件：Panel 监听并转发给 JS
        public event Action? StreamingStarted;
        public event Action<string>? StreamingDelta;
        public event Action<string>? StreamingThinking;
        public event Action? StreamingEnded;
        public event Action? MessagesUpdated;

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

            // 后台预热：提前加载 SemanticRouter（ONNX 模型）和卡片描述
            // 避免首次发送消息时因懒加载导致的几秒无响应
            _ = Task.Run(() =>
            {
                try
                {
                    // 1. 初始化语义路由器（触发 ONNX 模型和 BERT 分词器加载）
                    var router = SemanticRouter.GetInstance();
                    AiFlowLogger.Info($"[预热] SemanticRouter 已就绪, IsReady={router.IsReady}");

                    // 2. 加载卡片描述并预计算向量（同步完成，后续请求可直接使用缓存）
                    _service.WarmupCardDescriptions();
                    AiFlowLogger.Info("[预热] 卡片描述和语义向量已预计算完成");
                }
                catch (Exception ex)
                {
                    AiFlowLogger.Warn($"[预热] 后台初始化失败（不影响功能）: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 新建对话：归档当前会话并创建新的空会话
        /// </summary>
        [RelayCommand]
        private void NewChat()
        {
            if (IsBusy) return;
            ArchiveCurrentSession();
            CurrentSession = new AiChatSession();
            Messages.Clear();
            PendingPlan = null;
            ReportItems.Clear();
            ShowRetryButton = false;
            IsHistoryOpen = false;
        }

        /// <summary>
        /// 切换到指定的历史会话
        /// </summary>
        [RelayCommand]
        private void SwitchSession(AiChatSession session)
        {
            if (session == null) return;
            if (IsBusy) return;
            ArchiveCurrentSession();

            // 从历史中移除并设为当前
            Sessions.Remove(session);
            CurrentSession = session;
            Messages.Clear();
            foreach (var msg in session.Messages)
                Messages.Add(msg);

            ShowRetryButton = false;
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
        /// 递归评估步骤列表（含嵌套 IfBody/ElseBody/LoopBody）的最高风险等级
        /// </summary>
        private static void EvaluateStepsRisk(List<AiFlowPlanStep> steps, ref TaskRiskLevel maxRisk)
        {
            foreach (var step in steps)
            {
                if (Enum.TryParse<TaskType>(step.TaskType, out var tt))
                {
                    var risk = TaskRiskClassifier.GetRiskLevel(tt);
                    if (risk > maxRisk) maxRisk = risk;
                }
                // 递归检查嵌套结构
                if (step.IfBody != null && step.IfBody.Count > 0)
                    EvaluateStepsRisk(step.IfBody, ref maxRisk);
                if (step.ElseBody != null && step.ElseBody.Count > 0)
                    EvaluateStepsRisk(step.ElseBody, ref maxRisk);
                if (step.LoopBody != null && step.LoopBody.Count > 0)
                    EvaluateStepsRisk(step.LoopBody, ref maxRisk);
            }
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
            ShowContinueButton = false;

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
                    ShowRetryButton = true;
                }
                return;
            }

            // 检查模型设置
            if (string.IsNullOrEmpty(SelectedModelId))
            {
                AddMessage(AiChatRole.System, "⚠️ 请先在模型管理中配置模型，并在下方选择一个模型。");
                return;
            }

            // 仅当最后一条消息不同于当前用户输入时才添加（防止重试产生气泡堆叠）
            var lastMsg = Messages.LastOrDefault();
            if (lastMsg == null || lastMsg.Role != AiChatRole.User || lastMsg.Content != userInput)
            {
                AddMessage(AiChatRole.User, userInput);
            }
            
            _lastUserInput = userInput;
            InputText = "";
            IsGenerating = true;
            PendingPlan = null;

            _cts = new CancellationTokenSource();

            // 日志记录会话开始
            AiFlowLogger.LogSessionStart(userInput, SelectedModelId);

            // 流式输出文本累积器（用于最终持久化到 Messages 集合）
            var streamBuilder = new System.Text.StringBuilder();
            var thinkingBuilder = new System.Text.StringBuilder();

            int streamInsertIndex = Messages.Count;

            try
            {
                // 阶段1：确定类别（可通过设置跳过）
                LoadingText = "✦ 正在思考中...";

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
                LoadingText = "✦ 正在生成方案...";
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

                // 注意：卡片输出图像不再自动附加。
                // 只有当 Orchid AI 通过工具调用（request_screenshot 等）明确请求图像时，
                // 系统才会在工具结果中返回相应图像。
                // 这样可以避免普通对话（如"你好"）时无谓地发送大量图像数据。



            // 准备流式输出，WebView2 内置加载与流式显示
                LoadingText = "⏳ 正在分析需求...";
                streamInsertIndex = Messages.Count; // 更新为流式消息应当插入的位置

                // 消费一次性的断点续写内容
                var prefill = _prefillAssistantContent;
                _prefillAssistantContent = null;

                // 触发流式开始事件（WebView2 创建占位消息）
                StreamingStarted?.Invoke();

                if (!string.IsNullOrEmpty(prefill))
                {
                    // 顺发之前生成的前半截，使 UI 瞬间恢复原状
                    streamBuilder.Append(prefill);
                    StreamingDelta?.Invoke(prefill);
                }

                // 流式回调：累积文本（用于持久化）+ 触发事件（用于 WebView2 增量渲染）
                Action<string> onDelta = delta =>
                {
                    if (LoadingText != null) LoadingText = null;
                    streamBuilder.Append(delta);
                    StreamingDelta?.Invoke(delta);
                };

                Action<string> onThinking = thinking =>
                {
                    if (LoadingText != null) LoadingText = null;
                    thinkingBuilder.Append(thinking);
                    StreamingThinking?.Invoke(thinking);
                };

                var (plan, tokens2In, tokens2Out, isTruncated) = await _service.GeneratePlanAsync(
                    userInput, categories, SelectedModelId, _cts.Token, currentFlowContext, history,
                    imageBase64List: imageBase64List.Count > 0 ? imageBase64List : null,
                    onDelta: onDelta, onThinking: onThinking,
                    onStatus: status => { LoadingText = status; },
                    getFlowDetail: (flowName, startOrder, count) => _serializer.SerializeFlowDetail(flowName, startOrder, count),
                    captureScreenshot: async target => await CaptureScreenForAiAsync(
                        string.IsNullOrWhiteSpace(target) ? "windows" : target),
                    prefillAssistantMessage: prefill);

                // 判断方案是否有效内容（卡片、变量、流程或删除操作）
                if (!plan.HasAnyAction)
                {
                    // 分析模式：AI 仅返回了分析结果（无需创建卡片或变量）
                    if (!string.IsNullOrEmpty(plan.Summary))
                    {
                        AiFlowLogger.Info($"分析完成（Token: {tokens2In}+{tokens2Out}）");
                        
                        // 检查是否为由 analyze_flow 等内部抛出的 API 请求失败（网络故障等）
                        if (plan.Summary.Contains("请求失败") || plan.Summary.Contains("分析失败"))
                        {
                            ShowRetryButton = true;
                        }
                        // 流式内容已由 WebView2 渲染，finally 中会持久化
                    }
                    else
                    {
                        AddMessage(AiChatRole.System, "❌ AI 未能生成有效方案，请尝试更详细的需求描述。");
                        ShowRetryButton = true;
                    }
                    // 截断检测：如果 API 回复被截断，显示“请继续”按钮
                    if (isTruncated)
                    {
                        ShowContinueButton = true;
                        AiFlowLogger.Info("回复被截断，显示“请继续”按钮");
                    }
                    return;
                }

                AiFlowLogger.Info($"方案生成完成（Token: {tokens2In}+{tokens2Out}）");

                // 纯 shellCommands 方案（无卡片/变量操作）：自主模式下直接执行，无需"确认创建"
                bool isShellOnly = plan.HasShellCommands && !plan.HasSteps && !plan.HasVariables && !plan.HasDeleteVariables
                    && !plan.HasModifyVariables && !plan.HasModifyCards && !plan.HasDeleteCards && !plan.HasRunCards
                    && !plan.HasInsertCards && !plan.HasFlowOps;

                // 纯 shellCommands 方案（无卡片/变量操作）：直接执行，无需"确认创建"
                if (isShellOnly)
                {
                    // 流式内容已由 WebView2 渲染
                    AiFlowLogger.Info("纯 PowerShell 命令方案，直接执行...");
                    await ExecuteAutonomousLoopAsync(plan);
                    return;
                }

                // ===== 统一模式：低风险方案自动确认，中/高风险方案等待用户审批 =====
                {
                    // 评估方案中所有新建卡片的最高风险级别（递归包含嵌套步骤）
                    var maxRisk = TaskRiskLevel.Low;
                    EvaluateStepsRisk(plan.Plan, ref maxRisk);
                    // 运行已有卡片的风险也考虑在内
                    if (plan.RunCards != null)
                    {
                        foreach (var order in plan.RunCards)
                        {
                            var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == order);
                            if (card != null)
                            {
                                var risk = TaskRiskClassifier.GetRiskLevel(card.TaskType);
                                if (risk > maxRisk) maxRisk = risk;
                            }
                        }
                    }

                    if (maxRisk == TaskRiskLevel.Low)
                    {
                        // 全部低风险：自动确认并执行
                        var autoMsg = _serializer.FormatPlanAsText(plan);
                        AddMessage(AiChatRole.Assistant, autoMsg + "\n✅ 低风险操作，已自动确认");

                        AiFlowLogger.Info("方案全部为低风险，自动确认并执行...");

                        var (createdCount, _) = _planExecutor.CreateTaskCardsFromPlan(plan, CurrentMode, SelectedModelId);
                        _mainViewModel.RecalculateIndentLevels();
                        MessagesUpdated?.Invoke();

                        if (plan.HasRunCards)
                        {
                            await ExecuteAutonomousLoopAsync(plan);
                        }
                        else if (!plan.Done && plan.HasSteps && createdCount > 0 && string.IsNullOrWhiteSpace(plan.TargetFlow))
                        {
                            var newOrders = _mainViewModel.TaskCards
                                .OrderByDescending(c => c.Order)
                                .Take(createdCount)
                                .Select(c => c.Order)
                                .OrderBy(o => o)
                                .ToList();
                            plan.RunCards = newOrders;
                            AiFlowLogger.Info($"自动运行新创建的 {createdCount} 张卡片...");
                            await ExecuteAutonomousLoopAsync(plan);
                        }
                        else if (!plan.Done && plan.HasSteps && createdCount > 0 && !string.IsNullOrWhiteSpace(plan.TargetFlow))
                        {
                            AiFlowLogger.Info($"子流程卡片已创建到 {plan.TargetFlow}，继续自主循环处理后续步骤...");
                            await ExecuteAutonomousLoopAsync(plan);
                        }
                        return;
                    }
                    else
                    {
                        AiFlowLogger.Info($"方案含 {maxRisk} 风险操作，需用户确认...");
                    }
                }

                // 显示方案（含确认/拒绝按钮）— 中/高风险操作需用户审批
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
                // 触发流式结束事件，让 WebView2 做最终 Markdown 渲染
                StreamingEnded?.Invoke();

                // 将流式内容存入 Messages 集合（用于会话持久化）
                if (streamBuilder.Length > 0)
                {
                    var finalMsg = new AiChatMessage
                    {
                        Role = AiChatRole.Assistant,
                        Content = streamBuilder.ToString(),
                        ThinkingContent = thinkingBuilder.Length > 0 ? thinkingBuilder.ToString() : null,
                        // 标记该消息已通过 StreamingDelta 流式渲染，CollectionChanged 时跳过 addMessage 避免重复显示
                        IsStreamedToWebView = true
                    };
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (streamInsertIndex <= Messages.Count)
                            Messages.Insert(streamInsertIndex, finalMsg);
                        else
                            Messages.Add(finalMsg);
                    });
                }

                LoadingText = null;
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
            if (!PendingPlan.HasAnyAction)
                return;

            try
            {
                AiFlowLogger.Info($"用户确认方案，开始创建（{PendingPlan.Plan.Count} 个步骤, {PendingPlan.Variables?.Count ?? 0} 个变量）");
                var (createdCount, reports) = _planExecutor.CreateTaskCardsFromPlan(PendingPlan, CurrentMode, SelectedModelId);

                // 显示报告（仅有待配置项时）
                ReportItems.Clear();
                foreach (var item in reports)
                    ReportItems.Add(item);

                // 中/高风险方案经用户审批后，不再显示待配置报告（避免打断执行流程）
                // 报告项已通过 ReportItems 集合暴露，用户可随时查看

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
                                planMsg.Content = oldContent.Substring(0, lineStart).TrimEnd() + "\n✅ 已经确认";
                            }

                            // 直接更新属性（已继承 ObservableObject）
                            planMsg.Plan = null; // 清除关联，触发 UI 按钮隐藏
                        }
                    }
                });
                _mainViewModel.RecalculateIndentLevels();
                MessagesUpdated?.Invoke();

                // 如果方案包含 runCards，进入自主执行循环
                if (currentPlan.HasRunCards)
                {
                    await ExecuteAutonomousLoopAsync(currentPlan);
                }
                // 流程尚未完成，创建了新卡片但没有 runCards，自动将新卡片加入执行循环
                else if (!currentPlan.Done && currentPlan.HasSteps && createdCount > 0)
                {
                    var newOrders = _mainViewModel.TaskCards
                        .OrderByDescending(c => c.Order)
                        .Take(createdCount)
                        .Select(c => c.Order)
                        .OrderBy(o => o)
                        .ToList();

                    currentPlan.RunCards = newOrders;
                    AiFlowLogger.Info($"自动运行新创建的 {createdCount} 张卡片...");
                    await ExecuteAutonomousLoopAsync(currentPlan);
                }
                // 仅有 shellCommands 时，直接执行并进入自主循环
                else if (currentPlan.HasShellCommands)
                {
                    AiFlowLogger.Info("执行 PowerShell 命令...");
                    await ExecuteAutonomousLoopAsync(currentPlan);
                }
            }
            catch (Exception ex)
            {
                AiFlowLogger.Error("创建失败", ex);
                AddMessage(AiChatRole.System, $"❌ 创建失败: {ex.Message}");
                ShowRetryButton = true;
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
        /// 重试上次失败的生成（自动触发断点续写）
        /// </summary>
        [RelayCommand]
        private async Task RetryAsync()
        {
            if (IsGenerating) return;
            ShowRetryButton = false;

            // 1. 从后往前清理因失败残留的 System 报错提示
            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                var m = Messages[i];
                if (m.Role == AiChatRole.System && m.Content != null && m.Content.Contains("❌"))
                {
                    Messages.RemoveAt(i);
                }
                else if (m.Role == AiChatRole.User || m.Role == AiChatRole.Assistant)
                {
                    break; // 遇到用户的实质提问或其它正常流卡片停止清理
                }
            }

            // 2. 提取被中断或报错破坏的最后一个助手回答
            var lastAst = Messages.LastOrDefault(m => m.Role == AiChatRole.Assistant);
            if (lastAst != null && !string.IsNullOrEmpty(lastAst.Content))
            {
                var content = lastAst.Content;
                // 剔除失败尾巴
                int idx = content.IndexOf("❌ API 请求失败");
                if (idx < 0) idx = content.IndexOf("❌ 生成失败");
                if (idx < 0) idx = content.IndexOf("❌ 流程分析失败");
                if (idx > 0) content = content.Substring(0, idx).TrimEnd();

                if (content.Length > 20)
                {
                    // 仅当含有实质性内容时刻作为续写锚点
                    _prefillAssistantContent = content;
                }
                Messages.Remove(lastAst);
            }

            var lastUser = Messages.LastOrDefault(m => m.Role == AiChatRole.User);
            if (lastUser != null)
            {
                // 不要 Remove 用户的提问气泡，只是为了触发 SendMessageAsync
                InputText = lastUser.Content;
            }
            else
            {
                InputText = _lastUserInput;
            }

            // 清理 UI 上无用的残留气泡
            MessagesUpdated?.Invoke();

            await SendMessageAsync();
        }

        /// <summary>
        /// 继续被截断的生成（现在升级为真断点无缝续写）
        /// </summary>
        [RelayCommand]
        private async Task ContinueGenerationAsync()
        {
            if (IsGenerating) return;
            ShowContinueButton = false;

            // 继续操作与重试操作的容错处理如今完全一致（由于引入了预填充机制，都会恢复现场并续写）
            await RetryAsync();
        }

        /// <summary>
        /// 取消显示"请继续"按钮
        /// </summary>
        [RelayCommand]
        private void DismissContinue()
        {
            ShowContinueButton = false;
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


    }
}
