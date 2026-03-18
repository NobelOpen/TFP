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

        public AiFlowViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
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
                    var resultInfo = SerializeCardResults(new List<int> { order });
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
                var currentFlowContext = SerializeCurrentFlow();

                // 构建最近对话历史（最多取最近 3 轮用户+助手消息）
                var history = BuildConversationHistory();

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

                // Orchid 直接截屏：如果没有已有截图，自动截全屏让 AI 看到当前画面
                if (imageBase64List.Count == 0)
                {
                    AiFlowLogger.Info("Orchid 自动截屏中...");
                    var (screenBase64, sw, sh) = await CaptureScreenForAiAsync();
                    if (screenBase64 != null)
                    {
                        imageBase64List.Add(screenBase64);
                        lastImageWidth = sw;
                        lastImageHeight = sh;
                        AiFlowLogger.Info($"已附加屏幕截图 ({sw}x{sh})");
                    }
                }

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
                        // 将校正公式注入 prompt，让 AI 在回答坐标时自动应用
                        userInput += $"\n\n[坐标校正] 你在此分辨率({cal.Width}x{cal.Height})下的坐标估算存在系统偏差，" +
                                     $"请对你估算的所有坐标应用以下校正公式：" +
                                     $"correctedX = {cal.ScaleX:F4} * rawX + {cal.OffsetX:F1}，" +
                                     $"correctedY = {cal.ScaleY:F4} * rawY + {cal.OffsetY:F1}。" +
                                     $"请直接回复校正后的坐标，不要回复原始估算值。";
                        AiFlowLogger.Info($"[标定] 已注入校正公式到 prompt");
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
                bool hasFlowOps = (plan.CreateFlows != null && plan.CreateFlows.Count > 0)
                    || (plan.DeleteFlows != null && plan.DeleteFlows.Count > 0)
                    || !string.IsNullOrWhiteSpace(plan.SwitchFlow);

                if (!hasSteps && !hasVariables && !hasDeletes && !hasModifies && !hasCardModifies && !hasCardDeletes && !hasRunCards && !hasInsertCards && !hasFlowOps)
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

                // 显示方案
                PendingPlan = plan;
                var planMsg = FormatPlanAsText(plan);
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
            if (!hasSteps && !hasVariables && !hasDeletes && !hasModifies && !hasCardModifies && !hasCardDeletes && !hasRunCards && !hasInsertCards && !hasFlowOps)
                return;

            try
            {
                AiFlowLogger.Info($"用户确认方案，开始创建（{PendingPlan.Plan.Count} 个步骤, {PendingPlan.Variables?.Count ?? 0} 个变量）");
                var (createdCount, reports) = CreateTaskCardsFromPlan(PendingPlan);

                // 显示报告（仅有待配置项时）
                ReportItems.Clear();
                foreach (var item in reports)
                    ReportItems.Add(item);

                // 自主模式下不显示待配置报告（避免打断自动执行流程）
                if (reports.Count > 0 && CurrentMode != AiAssistantMode.Autonomous)
                {
                    var reportText = FormatReportAsText(createdCount, reports);
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
        /// 将 AI 方案转化为实际的任务卡片（支持嵌套控制流区块）
        /// </summary>
        private (int CreatedCount, List<AiFlowReportItem> Reports) CreateTaskCardsFromPlan(AiFlowPlanResponse plan)
        {
            var stepToCard = new Dictionary<int, TaskCardBase>();
            var reports = new List<AiFlowReportItem>();
            int createdCount = 0;

            // 预填充已有卡片映射：方案中新步骤的 sourceStep 可能引用已有卡片的序号
            // 例如 sourceStep=1 引用画布上 Order=1 的截图卡片
            foreach (var existingCard in _mainViewModel.TaskCards)
            {
                if (!stepToCard.ContainsKey(existingCard.Order))
                    stepToCard[existingCard.Order] = existingCard;
            }

            // ===== 流程（Tab）级操作 =====

            // 创建新流程
            if (plan.CreateFlows != null && plan.CreateFlows.Count > 0)
            {
                foreach (var newFlow in plan.CreateFlows)
                {
                    if (string.IsNullOrWhiteSpace(newFlow.Name)) continue;
                    // 检查是否已存在同名流程
                    if (_mainViewModel.Tabs.Any(t => t.Name == newFlow.Name))
                    {
                        AiFlowLogger.Warn($"流程 \"{newFlow.Name}\" 已存在，跳过创建");
                        continue;
                    }
                    var tab = new WorkflowTab { Name = newFlow.Name };
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _mainViewModel.Tabs.Add(tab);
                    });
                    AiFlowLogger.Info($"已创建流程: {newFlow.Name}");
                }
            }

            // 删除流程
            if (plan.DeleteFlows != null && plan.DeleteFlows.Count > 0)
            {
                foreach (var flowName in plan.DeleteFlows)
                {
                    if (string.IsNullOrWhiteSpace(flowName)) continue;
                    var tab = _mainViewModel.Tabs.FirstOrDefault(t => t.Name == flowName);
                    if (tab == null)
                    {
                        AiFlowLogger.Warn($"流程 \"{flowName}\" 不存在，跳过删除");
                        continue;
                    }
                    if (_mainViewModel.Tabs.Count <= 1)
                    {
                        AiFlowLogger.Warn("至少保留一个流程，跳过删除");
                        continue;
                    }
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // 如果要删除的是当前选中的流程，先切换到其他流程
                        if (_mainViewModel.SelectedTab == tab)
                        {
                            var idx = _mainViewModel.Tabs.IndexOf(tab);
                            _mainViewModel.SelectedTab = idx > 0
                                ? _mainViewModel.Tabs[idx - 1]
                                : _mainViewModel.Tabs[idx + 1];
                        }
                        _mainViewModel.Tabs.Remove(tab);
                    });
                    AiFlowLogger.Info($"已删除流程: {flowName}");
                }
            }

            // 切换到目标流程（在创建卡片之前，确保卡片添加到正确的流程）
            if (!string.IsNullOrWhiteSpace(plan.SwitchFlow))
            {
                var targetTab = _mainViewModel.Tabs.FirstOrDefault(t => t.Name == plan.SwitchFlow);
                if (targetTab != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _mainViewModel.SelectedTab = targetTab;
                    });
                    AiFlowLogger.Info($"已切换到流程: {plan.SwitchFlow}");
                }
                else
                {
                    AiFlowLogger.Warn($"目标流程 \"{plan.SwitchFlow}\" 不存在，保持当前流程");
                }
            }

            // ===== 变量和卡片操作 =====

            var varStore = _mainViewModel.VariableStore;

            // 删除方案指定的变量
            if (plan.DeleteVariables != null && plan.DeleteVariables.Count > 0)
            {
                int varDeleted = 0;
                foreach (var name in plan.DeleteVariables)
                {
                    if (varStore.RemoveVariable(name))
                    {
                        varDeleted++;
                        AiFlowLogger.Info($"删除变量: @{name}");
                    }
                    else
                    {
                        AiFlowLogger.Warn($"变量 @{name} 不存在，跳过删除");
                    }
                }
                if (varDeleted > 0)
                    _mainViewModel.AddLog($"[AI] 已删除 {varDeleted} 个变量");
            }

            // 修改方案指定的变量值
            if (plan.ModifyVariables != null && plan.ModifyVariables.Count > 0)
            {
                int varModified = 0;
                foreach (var v in plan.ModifyVariables)
                {
                    if (varStore.SetValue(v.Name, v.Value))
                    {
                        varModified++;
                        AiFlowLogger.Info($"修改变量: @{v.Name} = {v.Value}");
                    }
                    else
                    {
                        AiFlowLogger.Warn($"变量 @{v.Name} 不存在，跳过修改");
                    }
                }
                if (varModified > 0)
                    _mainViewModel.AddLog($"[AI] 已修改 {varModified} 个变量");
            }

            // 创建方案声明的变量
            if (plan.Variables != null && plan.Variables.Count > 0)
            {
                int varCreated = 0;
                foreach (var v in plan.Variables)
                {
                    if (!Enum.TryParse<VariableType>(v.Type, true, out var varType))
                        varType = VariableType.String;

                    if (varStore.AddVariable(v.Name, varType, v.Value))
                    {
                        varCreated++;
                        AiFlowLogger.Info($"创建变量: @{v.Name} ({varType}) = {v.Value} - {v.Description}");
                    }
                    else
                    {
                        AiFlowLogger.Warn($"变量 @{v.Name} 已存在，跳过创建");
                    }
                }
                if (varCreated > 0)
                    _mainViewModel.AddLog($"[AI] 已创建 {varCreated} 个变量");
            }

            // 修改已有卡片属性
            if (plan.ModifyCards != null && plan.ModifyCards.Count > 0)
            {
                int cardModified = 0;
                foreach (var mod in plan.ModifyCards)
                {
                    var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == mod.Order);
                    if (card == null)
                    {
                        AiFlowLogger.Warn($"卡片 #{mod.Order} 不存在，跳过修改");
                        continue;
                    }
                    foreach (var kv in mod.Properties)
                    {
                        var prop = card.GetType().GetProperty(kv.Key,
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (prop != null && prop.CanWrite && prop.PropertyType == typeof(string))
                        {
                            prop.SetValue(card, kv.Value);
                            AiFlowLogger.Info($"修改卡片 #{mod.Order} {card.Name}: {kv.Key} = {kv.Value}");
                        }
                        else if (prop != null && prop.CanWrite && prop.PropertyType == typeof(int) && int.TryParse(kv.Value, out var intVal))
                        {
                            prop.SetValue(card, intVal);
                            AiFlowLogger.Info($"修改卡片 #{mod.Order} {card.Name}: {kv.Key} = {kv.Value}");
                        }
                        else if (prop != null && prop.CanWrite && prop.PropertyType == typeof(double) && double.TryParse(kv.Value, out var dblVal))
                        {
                            prop.SetValue(card, dblVal);
                            AiFlowLogger.Info($"修改卡片 #{mod.Order} {card.Name}: {kv.Key} = {kv.Value}");
                        }
                        else if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool) && bool.TryParse(kv.Value, out var boolVal))
                        {
                            prop.SetValue(card, boolVal);
                            AiFlowLogger.Info($"修改卡片 #{mod.Order} {card.Name}: {kv.Key} = {kv.Value}");
                        }
                        else
                        {
                            AiFlowLogger.Warn($"卡片 #{mod.Order} 属性 {kv.Key} 无法设置");
                        }
                    }
                    cardModified++;
                }
                if (cardModified > 0)
                    _mainViewModel.AddLog($"[AI] 已修改 {cardModified} 个卡片属性");
            }

            // 删除指定卡片（按序号从大到小删除，避免索引偏移）
            if (plan.DeleteCards != null && plan.DeleteCards.Count > 0)
            {
                int cardDeleted = 0;
                foreach (var order in plan.DeleteCards.OrderByDescending(o => o))
                {
                    var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == order);
                    if (card != null)
                    {
                        _mainViewModel.TaskCards.Remove(card);
                        cardDeleted++;
                        AiFlowLogger.Info($"删除卡片 #{order}: {card.Name}");
                    }
                    else
                    {
                        AiFlowLogger.Warn($"卡片 #{order} 不存在，跳过删除");
                    }
                }
                if (cardDeleted > 0)
                    _mainViewModel.AddLog($"[AI] 已删除 {cardDeleted} 个卡片");
            }

            // 向已有分支/循环中插入卡片
            if (plan.InsertCards != null && plan.InsertCards.Count > 0)
            {
                foreach (var insertReq in plan.InsertCards)
                {
                    var blockStartCard = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == insertReq.TargetBlockOrder);
                    if (blockStartCard == null)
                    {
                        AiFlowLogger.Warn($"插入目标 #{insertReq.TargetBlockOrder} 不存在，跳过");
                        continue;
                    }

                    if (!blockStartCard.BranchGroupId.HasValue)
                    {
                        AiFlowLogger.Warn($"#{insertReq.TargetBlockOrder} 不是 Block 卡片，跳过插入");
                        continue;
                    }

                    var groupId = blockStartCard.BranchGroupId.Value;
                    var branchTarget = insertReq.Branch?.ToLower() ?? "if";

                    // 定位插入位置：找到目标分支范围
                    int insertIndex = -1;
                    var groupCards = _mainViewModel.TaskCards
                        .Where(c => c.BranchGroupId == groupId)
                        .OrderBy(c => _mainViewModel.TaskCards.IndexOf(c))
                        .ToList();

                    if (branchTarget == "if")
                    {
                        // If 分支：在 IfStart 之后、ElseStart 或 ElseEnd 之前
                        var ifStart = groupCards.FirstOrDefault(c => c.BranchRole == BranchRole.IfStart);
                        var nextMarker = groupCards.FirstOrDefault(c =>
                            c.BranchRole == BranchRole.ElseStart || c.BranchRole == BranchRole.ElseEnd);
                        if (ifStart != null && nextMarker != null)
                            insertIndex = _mainViewModel.TaskCards.IndexOf(nextMarker);
                    }
                    else if (branchTarget == "else")
                    {
                        // Else 分支：在 ElseStart 之后、ElseEnd 之前
                        var elseStart = groupCards.FirstOrDefault(c => c.BranchRole == BranchRole.ElseStart);
                        var elseEnd = groupCards.FirstOrDefault(c => c.BranchRole == BranchRole.ElseEnd);
                        if (elseStart != null && elseEnd != null)
                            insertIndex = _mainViewModel.TaskCards.IndexOf(elseEnd);
                    }
                    else if (branchTarget == "loop")
                    {
                        // 循环体：在 ForLoopStart 之后、ForLoopEnd 之前
                        var loopStart = groupCards.FirstOrDefault(c => c.BranchRole == BranchRole.ForLoopStart);
                        var loopEnd = groupCards.FirstOrDefault(c => c.BranchRole == BranchRole.ForLoopEnd);
                        if (loopStart != null && loopEnd != null)
                            insertIndex = _mainViewModel.TaskCards.IndexOf(loopEnd);
                    }

                    if (insertIndex < 0)
                    {
                        AiFlowLogger.Warn($"无法定位 #{insertReq.TargetBlockOrder} 的 {branchTarget} 分支插入位置");
                        continue;
                    }

                    // 在目标位置逐个插入卡片
                    int insertedCount = 0;
                    foreach (var step in insertReq.Cards)
                    {
                        var card = CreateSingleCardFromStep(step, stepToCard, reports);
                        if (card != null)
                        {
                            _mainViewModel.TaskCards.Insert(insertIndex + insertedCount, card);
                            stepToCard[step.Step] = card;
                            insertedCount++;
                            createdCount++;
                            AiFlowLogger.Info($"插入卡片 #{card.Order} {card.Name} 到 #{insertReq.TargetBlockOrder} 的 {branchTarget} 分支");
                        }
                    }
                }
            }

            // 批量创建时，将 SelectedTask 置 null，使所有 AddTaskCommand 调用
            // 回退到 TaskCards.Add() 追加模式，确保卡片按声明顺序排列
            var savedSelectedTask = _mainViewModel.SelectedTask;
            _mainViewModel.SelectedTask = null;

            ProcessSteps(plan.Plan, stepToCard, reports, ref createdCount);

            // 恢复 SelectedTask（选中最后创建的卡片）
            if (_mainViewModel.TaskCards.Count > 0)
                _mainViewModel.SelectedTask = _mainViewModel.TaskCards[^1];

            _mainViewModel.AddLog($"[AI] 已创建 {createdCount} 个任务卡片");
            return (createdCount, reports);
        }

        /// <summary>
        /// 递归处理步骤列表（支持嵌套的 IfElseBlock 和 ForLoopBlock）
        /// </summary>
        private void ProcessSteps(
            List<AiFlowPlanStep> steps,
            Dictionary<int, TaskCardBase> stepToCard,
            List<AiFlowReportItem> reports,
            ref int createdCount)
        {
            foreach (var step in steps)
            {
                if (step.TaskType == "IfElseBlock")
                {
                    ProcessIfElseBlock(step, stepToCard, reports, ref createdCount);
                }
                else if (step.TaskType == "ForLoopBlock")
                {
                    ProcessForLoopBlock(step, stepToCard, reports, ref createdCount);
                }
                else
                {
                    ProcessNormalStep(step, stepToCard, reports, ref createdCount);
                }
            }
        }

        /// <summary>
        /// 处理 IfElseBlock 区块：展开为 IfStart + IfBody + ElseStart + ElseBody + ElseEnd
        /// </summary>
        private void ProcessIfElseBlock(
            AiFlowPlanStep step,
            Dictionary<int, TaskCardBase> stepToCard,
            List<AiFlowReportItem> reports,
            ref int createdCount)
        {
            var branchGroupId = Guid.NewGuid();

            // 创建 IfStart
            var ifStart = new IfElseBranchTaskCard(BranchRole.IfStart)
            {
                BranchGroupId = branchGroupId,
                Order = _mainViewModel.NextTaskNumber++
            };

            // 设置条件表达式
            if (step.Properties.TryGetValue("conditionExpression", out var condExpr) && !string.IsNullOrEmpty(condExpr))
                ifStart.ConditionExpression = condExpr;

            if (!string.IsNullOrEmpty(step.Name))
                ifStart.Name = step.Name;

            _mainViewModel.TaskCards.Add(ifStart);
            _mainViewModel.SelectedTask = null;
            stepToCard[step.Step] = ifStart;
            createdCount++;
            AiFlowLogger.LogCardCreated("IfStart", ifStart.Name, ifStart.Order,
                $"BranchGroupId={branchGroupId}, Condition={ifStart.ConditionExpression}");

            // 递归处理 IfBody
            if (step.IfBody != null && step.IfBody.Count > 0)
                ProcessSteps(step.IfBody, stepToCard, reports, ref createdCount);

            // 创建 ElseStart
            var elseStart = new IfElseBranchTaskCard(BranchRole.ElseStart)
            {
                BranchGroupId = branchGroupId,
                Order = _mainViewModel.NextTaskNumber++
            };

            bool hasElseBody = step.ElseBody != null && step.ElseBody.Count > 0;

            // 如果没有 ElseBody，默认隐藏 Else 分支
            if (!hasElseBody)
            {
                ifStart.IsElseHidden = true;
                elseStart.IsHiddenByCollapse = true;
            }
            else
            {
                ifStart.IsElseHidden = false;
            }

            _mainViewModel.TaskCards.Add(elseStart);
            _mainViewModel.SelectedTask = null;
            createdCount++;

            // 递归处理 ElseBody
            if (hasElseBody)
                ProcessSteps(step.ElseBody!, stepToCard, reports, ref createdCount);

            // 创建 ElseEnd（分支结束标记）
            var elseEnd = new IfElseBranchTaskCard(BranchRole.ElseEnd)
            {
                BranchGroupId = branchGroupId,
                Order = _mainViewModel.NextTaskNumber++
            };

            if (!hasElseBody)
                elseEnd.IsHiddenByCollapse = true;

            _mainViewModel.TaskCards.Add(elseEnd);
            _mainViewModel.SelectedTask = null;
            createdCount++;
        }

        /// <summary>
        /// 处理 ForLoopBlock 区块：展开为 ForLoopStart + LoopBody + ForLoopEnd
        /// </summary>
        private void ProcessForLoopBlock(
            AiFlowPlanStep step,
            Dictionary<int, TaskCardBase> stepToCard,
            List<AiFlowReportItem> reports,
            ref int createdCount)
        {
            var branchGroupId = Guid.NewGuid();

            // 创建 ForLoopStart
            var loopStart = new ForLoopTaskCard(BranchRole.ForLoopStart)
            {
                BranchGroupId = branchGroupId,
                Order = _mainViewModel.NextTaskNumber++
            };

            // 设置循环次数
            if (step.Properties.TryGetValue("loopCount", out var loopCountStr) && int.TryParse(loopCountStr, out var loopCount))
                loopStart.LoopCount = loopCount;
            else
                reports.Add(new AiFlowReportItem
                {
                    TaskCardId = loopStart.Id,
                    CardName = $"#{loopStart.Order} {step.Name}",
                    PropertyName = "LoopCount",
                    Hint = "循环次数"
                });

            if (!string.IsNullOrEmpty(step.Name))
                loopStart.Name = step.Name;

            _mainViewModel.TaskCards.Add(loopStart);
            _mainViewModel.SelectedTask = null;
            stepToCard[step.Step] = loopStart;
            createdCount++;
            AiFlowLogger.LogCardCreated("ForLoopStart", loopStart.Name, loopStart.Order,
                $"BranchGroupId={branchGroupId}, LoopCount={loopStart.LoopCount}");

            // 递归处理 LoopBody
            if (step.LoopBody != null && step.LoopBody.Count > 0)
                ProcessSteps(step.LoopBody, stepToCard, reports, ref createdCount);

            // 创建 ForLoopEnd
            var loopEnd = new ForLoopTaskCard(BranchRole.ForLoopEnd)
            {
                BranchGroupId = branchGroupId,
                Order = _mainViewModel.NextTaskNumber++
            };

            _mainViewModel.TaskCards.Add(loopEnd);
            _mainViewModel.SelectedTask = null;
            createdCount++;
        }

        /// <summary>
        /// 处理普通（线性）步骤
        /// </summary>
        private void ProcessNormalStep(
            AiFlowPlanStep step,
            Dictionary<int, TaskCardBase> stepToCard,
            List<AiFlowReportItem> reports,
            ref int createdCount)
        {
            // 解析 TaskType
            if (!Enum.TryParse<Models.TaskCards.TaskType>(step.TaskType, out var taskType))
            {
                _mainViewModel.AddLog($"[AI] 跳过未知卡片类型: {step.TaskType}");
                return;
            }

            // 创建卡片（通过 ViewModel 的 AddTask 命令）
            _mainViewModel.AddTaskCommand.Execute(taskType);
            var newCard = _mainViewModel.SelectedTask;
            if (newCard == null) return;

            // 设置名称
            if (!string.IsNullOrEmpty(step.Name))
                newCard.Name = step.Name;

            stepToCard[step.Step] = newCard;
            _mainViewModel.SelectedTask = null;
            createdCount++;
            AiFlowLogger.LogCardCreated(step.TaskType, newCard.Name, newCard.Order);

            // 尝试填充属性
            var missingProps = TryFillProperties(newCard, step, stepToCard);
            foreach (var missing in missingProps)
            {
                reports.Add(new AiFlowReportItem
                {
                    TaskCardId = newCard.Id,
                    CardName = $"#{newCard.Order} {newCard.Name}",
                    PropertyName = missing.PropertyName,
                    Hint = missing.Hint
                });
            }
        }

        /// <summary>
        /// 创建单张卡片（不追加到 TaskCards），供 insertCards 使用
        /// </summary>
        private TaskCardBase? CreateSingleCardFromStep(
            AiFlowPlanStep step,
            Dictionary<int, TaskCardBase> stepToCard,
            List<AiFlowReportItem> reports)
        {
            if (!Enum.TryParse<Models.TaskCards.TaskType>(step.TaskType, out var taskType))
            {
                AiFlowLogger.Warn($"跳过未知卡片类型: {step.TaskType}");
                return null;
            }

            var card = _mainViewModel.CreateTaskCard(taskType);
            if (card == null) return null;

            card.Order = _mainViewModel.NextTaskNumber++;
            if (!string.IsNullOrEmpty(step.Name))
                card.Name = step.Name;

            AiFlowLogger.LogCardCreated(step.TaskType, card.Name, card.Order);

            // 填充属性
            var missingProps = TryFillProperties(card, step, stepToCard);
            foreach (var missing in missingProps)
            {
                reports.Add(new AiFlowReportItem
                {
                    TaskCardId = card.Id,
                    CardName = $"#{card.Order} {card.Name}",
                    PropertyName = missing.PropertyName,
                    Hint = missing.Hint
                });
            }

            return card;
        }

        /// <summary>
        /// 尝试填充卡片属性，返回未填写的必要属性列表
        /// </summary>
        private List<AiFlowReportItem> TryFillProperties(
            TaskCardBase card, AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            switch (card)
            {
                case WinLaunchAppTaskCard launch:
                    if (props.TryGetValue("exePath", out var exePath) && !string.IsNullOrEmpty(exePath))
                        launch.ExePath = exePath;
                    else
                        missing.Add(new AiFlowReportItem { PropertyName = "ExePath", Hint = "可执行文件路径" });

                    if (props.TryGetValue("arguments", out var args))
                        launch.Arguments = args;
                    break;

                case WinScreenshotTaskCard screenshot:
                    if (props.TryGetValue("processName", out var procName) && !string.IsNullOrEmpty(procName))
                        screenshot.ProcessName = procName;
                    else
                        missing.Add(new AiFlowReportItem { PropertyName = "ProcessName", Hint = "目标进程名称" });
                    break;

                case WinClickTaskCard click:
                    // 坐标通过表达式引用其他任务的输出（与用户手动配置方式一致）
                    if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var sourceCard))
                    {
                        if (sourceCard.OutputsCoordinates)
                        {
                            // 使用表达式引用：#N 卡片名.X / #N 卡片名.Y
                            click.StartXExpression = $"#{sourceCard.Order} {sourceCard.Name}.X";
                            click.StartYExpression = $"#{sourceCard.Order} {sourceCard.Name}.Y";
                        }
                    }
                    // 设置点击类型（Single/Double/Swipe）
                    if (props.TryGetValue("clickType", out var clickTypeStr)
                        && Enum.TryParse<ClickType>(clickTypeStr, true, out var clickType))
                        click.ClickType = clickType;
                    // 设置静态坐标（仅当用户明确提供或无 sourceStep 时）
                    if (props.TryGetValue("startX", out var sxStr) && int.TryParse(sxStr, out var sx))
                        click.StartX = sx;
                    if (props.TryGetValue("startY", out var syStr) && int.TryParse(syStr, out var sy))
                        click.StartY = sy;
                    // 自主模式下自动校正 AI 估算坐标（使用标定数据）
                    if (CurrentMode == AiAssistantMode.Autonomous
                        && click.StartX != 0 && click.StartY != 0
                        && !step.SourceStep.HasValue)
                    {
                        var screenshotCard = _mainViewModel.TaskCards
                            .LastOrDefault(c => c is WinScreenshotTaskCard && c.OutputImage != null && !c.OutputImage.Empty());
                        if (screenshotCard?.OutputImage != null)
                        {
                            int imgW = screenshotCard.OutputImage.Width;
                            int imgH = screenshotCard.OutputImage.Height;
                            var cal = CalibrationService.GetCalibration(SelectedModelId, imgW, imgH);
                            if (cal != null)
                            {
                                var (cx, cy) = CalibrationService.CalibrateCoordinates(cal, click.StartX, click.StartY);
                                AiFlowLogger.Info($"标定校正: ({click.StartX},{click.StartY}) → ({cx},{cy})");
                                click.StartX = cx;
                                click.StartY = cy;
                            }
                        }
                    }
                    // 设置进程名
                    if (props.TryGetValue("processName", out var clickProc))
                        click.ProcessName = clickProc;
                    break;

                case WinCloseAppTaskCard close:
                    if (props.TryGetValue("processName", out var closeProc) && !string.IsNullOrEmpty(closeProc))
                        close.ProcessName = closeProc;
                    else
                        missing.Add(new AiFlowReportItem { PropertyName = "ProcessName", Hint = "目标进程名称" });
                    break;

                case WinUiAutomationTaskCard uiAuto:
                    if (props.TryGetValue("processName", out var uiProc) && !string.IsNullOrEmpty(uiProc))
                        uiAuto.ProcessName = uiProc;
                    else
                        missing.Add(new AiFlowReportItem { PropertyName = "ProcessName", Hint = "目标进程名称" });

                    if (props.TryGetValue("buttonName", out var btnName) && !string.IsNullOrEmpty(btnName))
                        uiAuto.ButtonName = btnName;
                    else
                        missing.Add(new AiFlowReportItem { PropertyName = "ButtonName", Hint = "按钮名称" });
                    break;

                case ImgOcrTaskCard ocr:
                    // 设置图像来源引用
                    if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var ocrSource))
                    {
                        if (ocrSource.OutputsImage)
                        {
                            ocr.UseSourceTaskImage = true;
                            ocr.SourceTaskIdForImage = ocrSource.Id;
                        }
                    }
                    else
                    {
                        missing.Add(new AiFlowReportItem { PropertyName = "图像来源", Hint = "需要绑定一个输出图像的任务（如截图卡片）" });
                    }
                    break;

                case ImgTemplateMatchTaskCard tmMatch:
                    // 搜索图来源（sourceStep）
                    if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var tmSource))
                    {
                        if (tmSource.OutputsImage)
                        {
                            tmMatch.UseSourceTaskImage = true;
                            tmMatch.SourceTaskIdForImage = tmSource.Id;
                        }
                    }
                    else
                    {
                        missing.Add(new AiFlowReportItem { PropertyName = "图像来源", Hint = "需要绑定一个输出图像的任务" });
                    }
                    // 模板来源（templateSourceStep）—— 引用其他步骤（如 ImgCrop）的输出作为模板
                    if (step.TemplateSourceStep.HasValue && stepToCard.TryGetValue(step.TemplateSourceStep.Value, out var tmplSource))
                    {
                        if (tmplSource.OutputsImage)
                        {
                            tmMatch.UseSourceTaskTemplate = true;
                            tmMatch.SourceTaskIdForTemplate = tmplSource.Id;
                        }
                    }
                    else if (props.TryGetValue("templateImagePath", out var tmplPath) && !string.IsNullOrEmpty(tmplPath))
                    {
                        tmMatch.TemplateImagePath = tmplPath;
                    }
                    else
                    {
                        missing.Add(new AiFlowReportItem { PropertyName = "模板来源", Hint = "需要绑定模板图来源或指定模板图路径" });
                    }
                    // 设置匹配阈值（兼容 threshold 和 matchThreshold 两种属性名）
                    if ((props.TryGetValue("matchThreshold", out var threshStr) || props.TryGetValue("threshold", out threshStr))
                        && double.TryParse(threshStr, out var thresh))
                        tmMatch.MatchThreshold = thresh;
                    break;

                case ImgCropTaskCard crop:
                    if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var cropSource))
                    {
                        if (cropSource.OutputsImage)
                        {
                            crop.UseSourceTaskImage = true;
                            crop.SourceTaskIdForImage = cropSource.Id;
                        }
                    }
                    // 设置 ROI 区域
                    if (props.TryGetValue("roiX", out var cropRxStr) && int.TryParse(cropRxStr, out var cropRx))
                        crop.RoiX = cropRx;
                    if (props.TryGetValue("roiY", out var cropRyStr) && int.TryParse(cropRyStr, out var cropRy))
                        crop.RoiY = cropRy;
                    if (props.TryGetValue("roiWidth", out var cropRwStr) && int.TryParse(cropRwStr, out var cropRw))
                        crop.RoiWidth = cropRw;
                    if (props.TryGetValue("roiHeight", out var cropRhStr) && int.TryParse(cropRhStr, out var cropRh))
                        crop.RoiHeight = cropRh;
                    if (crop.RoiWidth <= 0 || crop.RoiHeight <= 0)
                        missing.Add(new AiFlowReportItem { PropertyName = "ROI区域", Hint = "裁剪区域坐标和尺寸" });
                    break;

                case WinFindFileTaskCard findFile:
                    if (props.TryGetValue("fileName", out var findFileName) && !string.IsNullOrEmpty(findFileName))
                        findFile.FileName = findFileName;
                    else
                        missing.Add(new AiFlowReportItem { PropertyName = "FileName", Hint = "要查找的文件名称" });

                    if (props.TryGetValue("searchRootPath", out var searchRoot) && !string.IsNullOrEmpty(searchRoot))
                        findFile.SearchRootPath = searchRoot;

                    if (props.TryGetValue("maxDepth", out var maxDepthStr) && int.TryParse(maxDepthStr, out var maxDepth))
                        findFile.MaxDepth = maxDepth;

                    if (props.TryGetValue("useWildcard", out var useWild) && bool.TryParse(useWild, out var wildcard))
                        findFile.UseWildcard = wildcard;
                    break;

                case PauseTaskCard pause:
                    if (props.TryGetValue("pauseMs", out var pauseMs) && int.TryParse(pauseMs, out var ms))
                        pause.PauseDurationMs = ms;
                    break;

                case WinSubtitleTaskCard subtitle:
                    if (props.TryGetValue("processName", out var subProc))
                        subtitle.ProcessName = subProc;
                    if (props.TryGetValue("displayText", out var displayText))
                        subtitle.DisplayText = displayText;
                    break;

                case LlmTranslateTaskCard translate:
                    missing.Add(new AiFlowReportItem { PropertyName = "ModelId", Hint = "选择翻译模型" });
                    if (props.TryGetValue("targetLanguage", out var lang))
                        translate.TargetLanguage = lang;
                    break;

                case LlmVisionTaskCard vision:
                    missing.Add(new AiFlowReportItem { PropertyName = "ModelId", Hint = "选择多模态模型" });
                    if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var visionSource))
                    {
                        if (visionSource.OutputsImage)
                        {
                            vision.UseSourceTaskImage = true;
                            vision.SourceTaskIdForImage = visionSource.Id;
                        }
                    }
                    break;

                // === 补全图像类卡片的 sourceStep 映射 ===
                case ImgColorDetectTaskCard colorDetect:
                    if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var cdSource))
                    {
                        if (cdSource.OutputsImage)
                        {
                            colorDetect.UseSourceTaskImage = true;
                            colorDetect.SourceTaskIdForImage = cdSource.Id;
                        }
                    }
                    else
                    {
                        missing.Add(new AiFlowReportItem { PropertyName = "图像来源", Hint = "需要绑定一个输出图像的任务" });
                    }
                    break;

                case ImgColorSegmentTaskCard colorSeg:
                    if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var csSource))
                    {
                        if (csSource.OutputsImage)
                        {
                            colorSeg.UseSourceTaskImage = true;
                            colorSeg.SourceTaskIdForImage = csSource.Id;
                        }
                    }
                    else
                    {
                        missing.Add(new AiFlowReportItem { PropertyName = "图像来源", Hint = "需要绑定一个输出图像的任务" });
                    }
                    break;

                case ImgPreprocessTaskCard preprocess:
                    if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var ppSource))
                    {
                        if (ppSource.OutputsImage)
                        {
                            preprocess.UseSourceTaskImage = true;
                            preprocess.SourceTaskIdForImage = ppSource.Id;
                        }
                    }
                    else
                    {
                        missing.Add(new AiFlowReportItem { PropertyName = "图像来源", Hint = "需要绑定一个输出图像的任务" });
                    }
                    break;

                case ImgBlobAnalysisTaskCard blob:
                    if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var baSource))
                    {
                        if (baSource.OutputsImage)
                        {
                            blob.UseSourceTaskImage = true;
                            blob.SourceTaskIdForImage = baSource.Id;
                        }
                    }
                    else
                    {
                        missing.Add(new AiFlowReportItem { PropertyName = "图像来源", Hint = "需要绑定一个输出图像的任务" });
                    }
                    break;

                case ImgResizeTaskCard resize:
                    if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var rsSource))
                    {
                        if (rsSource.OutputsImage)
                        {
                            resize.UseSourceTaskImage = true;
                            resize.SourceTaskIdForImage = rsSource.Id;
                        }
                    }
                    else
                    {
                        missing.Add(new AiFlowReportItem { PropertyName = "图像来源", Hint = "需要绑定一个输出图像的任务" });
                    }
                    break;
            }

            return missing;
        }

        /// <summary>
        /// 将方案格式化为可读文本（支持嵌套缩进）
        /// </summary>
        private string FormatPlanAsText(AiFlowPlanResponse plan)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"📋 {plan.Summary}\n");

            // 显示流程操作
            if (plan.CreateFlows != null && plan.CreateFlows.Count > 0)
            {
                sb.AppendLine("📁 将创建的流程：");
                foreach (var f in plan.CreateFlows)
                    sb.AppendLine($"  • {f.Name}");
                sb.AppendLine();
            }
            if (plan.DeleteFlows != null && plan.DeleteFlows.Count > 0)
            {
                sb.AppendLine("🗑️ 将删除的流程：");
                foreach (var f in plan.DeleteFlows)
                    sb.AppendLine($"  • {f}");
                sb.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(plan.SwitchFlow))
            {
                sb.AppendLine($"🔀 将切换到流程：{plan.SwitchFlow}\n");
            }

            // 显示要删除的变量
            if (plan.DeleteVariables != null && plan.DeleteVariables.Count > 0)
            {
                sb.AppendLine("🗑️ 将删除的变量：");
                foreach (var name in plan.DeleteVariables)
                    sb.AppendLine($"  • @{name}");
                sb.AppendLine();
            }

            // 显示要创建的变量
            if (plan.Variables != null && plan.Variables.Count > 0)
            {
                sb.AppendLine("📦 需要创建的变量：");
                foreach (var v in plan.Variables)
                    sb.AppendLine($"  • @{v.Name} ({v.Type}) = {v.Value}  — {v.Description}");
                sb.AppendLine();
            }

            // 显示要修改的变量
            if (plan.ModifyVariables != null && plan.ModifyVariables.Count > 0)
            {
                sb.AppendLine("✏️ 将修改的变量：");
                foreach (var v in plan.ModifyVariables)
                    sb.AppendLine($"  • @{v.Name} → {v.Value}");
                sb.AppendLine();
            }

            // 显示要修改的卡片
            if (plan.ModifyCards != null && plan.ModifyCards.Count > 0)
            {
                sb.AppendLine("🔧 将修改的卡片属性：");
                foreach (var mod in plan.ModifyCards)
                {
                    var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == mod.Order);
                    var cardName = card?.Name ?? $"卡片#{mod.Order}";
                    foreach (var kv in mod.Properties)
                        sb.AppendLine($"  • #{mod.Order} {cardName}: {kv.Key} → {kv.Value}");
                }
                sb.AppendLine();
            }

            // 显示要删除的卡片
            if (plan.DeleteCards != null && plan.DeleteCards.Count > 0)
            {
                sb.AppendLine("🗑️ 将删除的卡片：");
                foreach (var order in plan.DeleteCards)
                {
                    var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == order);
                    var cardName = card?.Name ?? $"未知卡片";
                    sb.AppendLine($"  • #{order} {cardName}");
                }
                sb.AppendLine();
            }

            // 显示要插入到分支中的卡片
            if (plan.InsertCards != null && plan.InsertCards.Count > 0)
            {
                sb.AppendLine("📥 将插入到已有分支的卡片：");
                foreach (var ins in plan.InsertCards)
                {
                    var branchLabel = ins.Branch?.ToLower() switch
                    {
                        "else" => "ELSE 分支",
                        "loop" => "循环体",
                        _ => "IF 分支"
                    };
                    var targetCard = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == ins.TargetBlockOrder);
                    var targetName = targetCard?.Name ?? $"Block#{ins.TargetBlockOrder}";
                    sb.AppendLine($"  → #{ins.TargetBlockOrder} {targetName} 的 {branchLabel}：");
                    foreach (var card in ins.Cards)
                        sb.AppendLine($"    • [{card.TaskType}] {card.Name}");
                }
                sb.AppendLine();
            }

            // 显示要运行的卡片
            if (plan.RunCards != null && plan.RunCards.Count > 0)
            {
                sb.AppendLine("▶️ 将运行的卡片：");
                foreach (var order in plan.RunCards)
                {
                    var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == order);
                    var cardName = card?.Name ?? $"未知卡片";
                    sb.AppendLine($"  • #{order} {cardName}");
                }
                sb.AppendLine();
            }

            // 显示步骤
            if (plan.Plan.Count > 0)
            {
                sb.AppendLine("方案步骤：");
                FormatSteps(sb, plan.Plan, "  ");
            }

            bool isAutoMode = plan.RunCards != null && plan.RunCards.Count > 0;
            bool isFlowOp = (plan.CreateFlows != null && plan.CreateFlows.Count > 0)
                || (plan.DeleteFlows != null && plan.DeleteFlows.Count > 0)
                || !string.IsNullOrWhiteSpace(plan.SwitchFlow);
            bool isExecMode = isAutoMode || (isFlowOp && plan.Plan.Count == 0);
            var confirmText = isExecMode ? "确认执行」" : "确认创建」";
            sb.AppendLine($"\n✅ 确认无误后点击「{confirmText}，或点击「重新生成」。");
            return sb.ToString();
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
                    bool hasOtherActions = (currentPlan.Plan?.Count > 0) ||
                        (currentPlan.Variables?.Count > 0) ||
                        (currentPlan.DeleteVariables?.Count > 0) ||
                        (currentPlan.ModifyVariables?.Count > 0) ||
                        (currentPlan.ModifyCards?.Count > 0) ||
                        (currentPlan.DeleteCards?.Count > 0);

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
                        resultsText = SerializeCardResults(currentPlan.RunCards);
                        AiFlowLogger.Info($"运行结果:\n{resultsText}");
                    }

                    // 构建上下文：当前流程 + 运行结果 + 对话历史
                    var flowContext = SerializeCurrentFlow();
                    var history = BuildConversationHistory();

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

                    // Orchid 直接截屏：每轮决策前自动截全屏，让 AI 看到最新画面
                    List<string>? autoImageList = null;
                    int autoScreenW = 0, autoScreenH = 0;
                    {
                        AiFlowLogger.Info("Orchid 自主循环截屏中...");
                        var (scrBase64, sw, sh) = await CaptureScreenForAiAsync();
                        if (scrBase64 != null)
                        {
                            autoImageList = new List<string> { scrBase64 };
                            autoScreenW = sw;
                            autoScreenH = sh;
                            AiFlowLogger.Info($"已附加屏幕截图 ({sw}x{sh})");
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
                    bool hasNewActions = (nextPlan.Plan?.Count > 0) ||
                        (nextPlan.Variables?.Count > 0) ||
                        (nextPlan.DeleteVariables?.Count > 0) ||
                        (nextPlan.ModifyVariables?.Count > 0) ||
                        (nextPlan.ModifyCards?.Count > 0) ||
                        (nextPlan.DeleteCards?.Count > 0);

                    if (hasNewActions)
                    {
                        var (count, reports) = CreateTaskCardsFromPlan(nextPlan);
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
                                var (fbCount, fbReports) = CreateTaskCardsFromPlan(fallbackResponse);
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
        /// 序列化指定卡片的运行结果为 AI 可理解的文本
        /// </summary>
        private string SerializeCardResults(List<int> orders)
        {
            var sb = new System.Text.StringBuilder();

            foreach (var order in orders)
            {
                var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == order);
                if (card == null)
                {
                    sb.AppendLine($"卡片 #{order}: 不存在");
                    continue;
                }

                sb.AppendLine($"卡片 #{order} {card.Name} [{card.TaskType}]:");
                sb.AppendLine($"  状态: {card.Status}");

                if (!string.IsNullOrEmpty(card.ErrorMessage))
                    sb.AppendLine($"  错误: {card.ErrorMessage}");

                if (!string.IsNullOrEmpty(card.OutputText))
                    sb.AppendLine($"  文本输出: {card.OutputText}");

                if (card.OutputX.HasValue || card.OutputY.HasValue)
                    sb.AppendLine($"  坐标: ({card.OutputX}, {card.OutputY})");

                // 布尔结果通过反射获取（不同卡片的属性名可能不同）
                var matchResultProp = card.GetType().GetProperty("MatchResult");
                if (matchResultProp != null)
                {
                    var matchVal = matchResultProp.GetValue(card);
                    if (matchVal != null)
                        sb.AppendLine($"  匹配结果: {matchVal}");
                }

                if (card.OutputImage != null && !card.OutputImage.Empty())
                    sb.AppendLine($"  图像分辨率: {card.OutputImage.Width}x{card.OutputImage.Height}");

                // 通过反射提取路径等其他输出属性
                foreach (var propName in new[] { "OutputPath", "OutputFilePath", "OutputSavePath", "OutputTranslatedFilePath" })
                {
                    var pathProp = card.GetType().GetProperty(propName);
                    if (pathProp != null)
                    {
                        var pathVal = pathProp.GetValue(card) as string;
                        if (!string.IsNullOrEmpty(pathVal))
                        {
                            sb.AppendLine($"  路径输出: {pathVal}");
                            break; // 只显示第一个有值的路径
                        }
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 从当前 Messages 中提取最近的 User + Assistant 对话历史
        /// 最多取最近 3 轮，排除 System 消息和方案 JSON
        /// </summary>
        private List<(string Role, string Content)> BuildConversationHistory()
        {
            var history = new List<(string Role, string Content)>();

            // 从 Messages 中提取 User 和 Assistant 消息（排除 System 消息）
            var relevantMessages = Messages
                .Where(m => m.Role != AiChatRole.System)
                .ToList();

            // 取最近 20 条（约 10 轮对话）
            var recent = relevantMessages.Skip(Math.Max(0, relevantMessages.Count - 20)).ToList();

            foreach (var msg in recent)
            {
                var role = msg.Role == AiChatRole.User ? "user" : "assistant";
                string content;
                if (msg.Role == AiChatRole.User)
                {
                    // 用户消息通常较短，保留全文
                    content = msg.Content;
                }
                else
                {
                    // 助手回复（方案文本）通常较长，截断以控制 Token
                    content = msg.Content.Length > 300
                        ? msg.Content[..300] + "..."
                        : msg.Content;
                }
                history.Add((role, content));
            }

            return history;
        }

        /// <summary>
        /// 递归格式化步骤列表（带缩进）
        /// </summary>
        private void FormatSteps(System.Text.StringBuilder sb, List<AiFlowPlanStep> steps, string indent)
        {
            foreach (var step in steps)
            {
                sb.AppendLine($"{indent}{step.Step}. [{step.TaskType}] {step.Name}");
                sb.AppendLine($"{indent}   {step.Description}");

                if (step.SourceStep.HasValue)
                    sb.AppendLine($"{indent}   ↩ 图像来源: 第 {step.SourceStep} 步");
                if (step.TemplateSourceStep.HasValue)
                    sb.AppendLine($"{indent}   🖼️ 模板来源: 第 {step.TemplateSourceStep} 步");

                if (step.Properties.Count > 0)
                {
                    foreach (var kv in step.Properties)
                        sb.AppendLine($"{indent}   • {kv.Key} = {kv.Value}");
                }

                // 递归显示嵌套区块
                if (step.IfBody != null && step.IfBody.Count > 0)
                {
                    sb.AppendLine($"{indent}   ┣━ If 分支：");
                    FormatSteps(sb, step.IfBody, indent + "   ┃  ");
                }
                if (step.ElseBody != null && step.ElseBody.Count > 0)
                {
                    sb.AppendLine($"{indent}   ┗━ Else 分支：");
                    FormatSteps(sb, step.ElseBody, indent + "      ");
                }
                if (step.LoopBody != null && step.LoopBody.Count > 0)
                {
                    sb.AppendLine($"{indent}   ┗━ 循环体：");
                    FormatSteps(sb, step.LoopBody, indent + "      ");
                }
            }
        }

        /// <summary>
        /// 格式化报告
        /// </summary>
        private string FormatReportAsText(int createdCount, List<AiFlowReportItem> reports)
        {
            if (reports.Count == 0)
                return ""; // 无待配置项，不显示消息

            // 只显示简洁的配置提示标题
            return $"⚠️ {reports.Count} 项需要手动配置：";
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
        /// 将当前画布上的任务卡片序列化为 AI 可理解的文本摘要
        /// </summary>
        private string SerializeCurrentFlow()
        {
            var cards = _mainViewModel.TaskCards;
            var variables = _mainViewModel.VariableStore.Variables;
            bool hasCards = cards != null && cards.Count > 0;
            bool hasVars = variables != null && variables.Count > 0;

            var sb = new System.Text.StringBuilder();

            // 序列化流程列表
            var tabs = _mainViewModel.Tabs;
            if (tabs.Count > 1)
            {
                sb.AppendLine($"当前共有 {tabs.Count} 个流程：");
                foreach (var tab in tabs)
                {
                    var marker = tab == _mainViewModel.SelectedTab ? "（当前）" : "";
                    var cardCount = tab == _mainViewModel.SelectedTab
                        ? (cards?.Count ?? 0)
                        : tab.TaskCards.Count;
                    sb.AppendLine($"  • {tab.Name}{marker} - {cardCount} 个卡片");
                }
                sb.AppendLine();
            }

            // 序列化变量
            if (hasVars)
            {
                sb.AppendLine($"当前变量管理器中已有 {variables!.Count} 个变量：");
                foreach (var v in variables)
                    sb.AppendLine($"  @{v.Name} ({v.Type}) = {v.Value}");
                sb.AppendLine();
            }

            // 序列化当前流程卡片（详细信息）
            if (hasCards)
            {
                var currentTabName = _mainViewModel.SelectedTab?.Name ?? "当前流程";
                sb.AppendLine($"当前流程「{currentTabName}」已有 {cards!.Count} 个任务卡片：");

                // 需要排除的基类属性名
                var excludeProps = new HashSet<string> { "Id", "Name", "Order", "Status", "ErrorMessage",
                    "IndentLevel", "BranchRole", "BranchGroupId", "IsCollapsed", "IsHiddenByCollapse",
                    "TaskType", "OutputsImage", "OutputsText", "OutputsCoordinates", "OutputsBoolResult" };

                foreach (var card in cards)
                {
                    var indent = new string(' ', card.IndentLevel * 2);
                    var typeName = card.GetType().Name.Replace("TaskCard", "");

                    // 控制流卡片特殊处理
                    if (card is IfElseBranchTaskCard ifCard)
                    {
                        var roleStr = ifCard.BranchRole switch
                        {
                            BranchRole.IfStart => "If开始",
                            BranchRole.ElseStart => "Else开始",
                            BranchRole.ElseEnd => "分支结束",
                            _ => ifCard.BranchRole.ToString()
                        };
                        sb.AppendLine($"{indent}#{card.Order} [{roleStr}] {card.Name}");
                        if (ifCard.BranchRole == BranchRole.IfStart && !string.IsNullOrEmpty(ifCard.ConditionExpression))
                            sb.AppendLine($"{indent}  条件: {ifCard.ConditionExpression}");
                    }
                    else if (card is ForLoopTaskCard loopCard)
                    {
                        var roleStr = loopCard.BranchRole == BranchRole.ForLoopStart ? "循环开始" : "循环结束";
                        sb.AppendLine($"{indent}#{card.Order} [{roleStr}] {card.Name}");
                        if (loopCard.BranchRole == BranchRole.ForLoopStart)
                            sb.AppendLine($"{indent}  循环次数: {loopCard.LoopCount}");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}#{card.Order} [{typeName}] {card.Name}");
                    }

                    // 序列化卡片的关键属性值（通过反射提取非空字符串属性）
                    var cardType = card.GetType();
                    foreach (var prop in cardType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        if (excludeProps.Contains(prop.Name)) continue;
                        if (prop.PropertyType != typeof(string)) continue;
                        if (prop.GetCustomAttributes(typeof(Newtonsoft.Json.JsonIgnoreAttribute), true).Length > 0) continue;

                        try
                        {
                            var val = prop.GetValue(card) as string;
                            if (!string.IsNullOrEmpty(val))
                                sb.AppendLine($"{indent}  {prop.Name}: {val}");
                        }
                        catch { /* 忽略反射异常 */ }
                    }
                }
            }

            // 序列化其他流程的卡片摘要
            if (tabs.Count > 1)
            {
                foreach (var tab in tabs)
                {
                    if (tab == _mainViewModel.SelectedTab) continue;
                    if (tab.TaskCards.Count == 0) continue;

                    sb.AppendLine();
                    sb.AppendLine($"流程「{tab.Name}」有 {tab.TaskCards.Count} 个卡片：");
                    foreach (var card in tab.TaskCards)
                    {
                        var typeName = card.GetType().Name.Replace("TaskCard", "");
                        sb.AppendLine($"  #{card.Order} [{typeName}] {card.Name}");
                    }
                }
            }

            return sb.ToString();
        }
    }
}
