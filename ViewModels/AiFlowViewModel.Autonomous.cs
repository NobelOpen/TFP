using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Models.AiFlow;
using TaskFlow.Models.TaskCards;
using TaskFlow.Services;

namespace TaskFlow.ViewModels
{
    /// <summary>
    /// AiFlowViewModel 的自主执行部分：
    /// 包含自主执行循环、Shell 命令执行、截屏、用户批准等逻辑
    /// </summary>
    public partial class AiFlowViewModel
    {
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
                        AiFlowLogger.Info($"AI 自主任务完成: {currentPlan.Summary}");
                        break;
                    }

                    // 没有要运行的卡片且没有其他操作，退出循环
                    if (!currentPlan.HasRunCards && !currentPlan.HasAnyAction && !currentPlan.NeedsScreenshot)
                    {
                        AiFlowLogger.Info($"AI 自主执行结束: {currentPlan.Summary}");
                        break;
                    }

                    if (!currentPlan.HasRunCards)
                    {
                        // 有其他操作但没有 runCards，继续下一轮让 AI 决策
                        AiFlowLogger.Info("AI 执行了操作，继续决策...");
                    }

                    // ===== PowerShell 命令执行 =====
                    string shellResultsText = "";
                    if (currentPlan.HasShellCommands)
                    {
                        shellResultsText = await ExecuteShellCommandsAsync(currentPlan.ShellCommands!, _cts.Token);
                    }

                    string resultsText = "";
                    bool hasFailedCards = false;
                    if (currentPlan.HasRunCards)
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

                    // 构建上下文：流程摘要 + 当前流程详情（自主模式需要卡片状态来决策）
                    var flowContext = _serializer.SerializeCurrentFlow();
                    var currentTabName = _mainViewModel.SelectedTab?.Name;
                    if (!string.IsNullOrEmpty(currentTabName))
                    {
                        var currentDetail = _serializer.SerializeFlowDetail(currentTabName);
                        if (!string.IsNullOrEmpty(currentDetail))
                            flowContext += "\n" + currentDetail;
                    }
                    var history = _serializer.BuildConversationHistory(Messages);

                    // 获取原始用户请求
                    var originalRequest = Messages.LastOrDefault(m => m.Role == AiChatRole.User)?.Content ?? "执行流程";

                    // 构建所有卡片状态清单（包含主流程和子流程）
                    var allCardsInfo = new System.Text.StringBuilder();
                    // 主流程卡片
                    var mainTabName = _mainViewModel.SelectedTab?.Name ?? "主流程";
                    allCardsInfo.AppendLine($"[当前流程] {mainTabName} 的卡片：");
                    if (_mainViewModel.TaskCards.Count == 0)
                        allCardsInfo.AppendLine("  （空，没有任何卡片）");
                    foreach (var c in _mainViewModel.TaskCards)
                    {
                        var statusMark = c.Status == Models.TaskCards.TaskStatus.Success ? "✅" :
                                         c.Status == Models.TaskCards.TaskStatus.Failed ? "❌" :
                                         c.Status == Models.TaskCards.TaskStatus.Running ? "🔄" : "⬜";
                        allCardsInfo.AppendLine($"  {statusMark} #{c.Order} {c.Name} [{c.TaskType}] - 状态: {c.Status}");
                    }
                    // 子流程卡片：让 AI 知道子流程中已有哪些卡片，防止重复创建
                    foreach (var tab in _mainViewModel.Tabs)
                    {
                        if (tab == _mainViewModel.SelectedTab) continue; // 跳过当前流程（已在上面列出）
                        if (tab.TaskCards.Count == 0) continue;
                        allCardsInfo.AppendLine($"\n[子流程] {tab.Name} 的卡片（已创建完成，无需再次创建）：");
                        foreach (var c in tab.TaskCards)
                        {
                            allCardsInfo.AppendLine($"  ✅ #{c.Order} {c.Name} [{c.TaskType}]");
                        }
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
                    autonomousPrompt.AppendLine("- 【子流程多步操作】如果用户要求'在主流程调用子流程'，完成子流程的卡片创建后，" +
                        "必须继续执行：switchFlow 切换回主流程 + plan 在主流程创建 CallSubFlow 卡片，此时绝对不能设置 done: true！");
                    autonomousPrompt.AppendLine("- 当用户请求的操作已完成（所有相关卡片执行成功），必须立即设置 done: true，不要画蛇添足");
                    autonomousPrompt.AppendLine("- 严禁自行添加验证/确认/二次检查步骤（如截图验证、LlmVision 分析结果等），除非用户明确要求验证");
                    autonomousPrompt.AppendLine("- 你自己就是多模态 Vision 模型，不需要创建 LlmVision 卡片来分析图像，你已经能直接看到卡片输出的图像");

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
                    if (currentPlan.NeedsScreenshot)
                    {
                        var target = string.IsNullOrWhiteSpace(currentPlan.ScreenshotTarget)
                            ? "windows" : currentPlan.ScreenshotTarget.Trim();
                        var targetLabel = target == "windows" ? "全屏" : $"窗口:{target}";
                        AiFlowLogger.Info($"Orchid 按需截屏中（{targetLabel}）...");
                        var (scrBase64, sw, sh) = await CaptureScreenForAiAsync(target);
                        if (scrBase64 != null)
                        {
                            autoImageList = new List<string> { scrBase64 };
                            AiFlowLogger.Info($"已附加屏幕截图 ({sw}x{sh})");
                            AddMessage(AiChatRole.System, $"📸 已截取{targetLabel} ({sw}x{sh})");
                        }
                    }

                    // 再次调用 LLM 获取下一步决策（传入截图图像）
                    AiFlowLogger.Info("AI 正在分析结果并决策下一步...");
                    // LLM API 调用（带重试）
                    AiFlowPlanResponse nextPlan;
                    int tokensIn, tokensOut;
                    int maxRetries = 3;
                    int retryCount = 0;

                    // 为自主循环的决策轮也提供流式回调，让 AI 的总结文本能实时显示
                    var loopStreamBuilder = new System.Text.StringBuilder();
                    var loopThinkingBuilder = new System.Text.StringBuilder();
                    StreamingStarted?.Invoke();

                    while (true)
                    {
                        try
                        {
                            var result = await _service.GeneratePlanAsync(
                                autonomousPrompt.ToString(),
                                categories, SelectedModelId, _cts.Token, flowContext, history,
                                imageBase64List: autoImageList,
                                onDelta: delta =>
                                {
                                    loopStreamBuilder.Append(delta);
                                    StreamingDelta?.Invoke(delta);
                                },
                                onThinking: thinking =>
                                {
                                    loopThinkingBuilder.Append(thinking);
                                    StreamingThinking?.Invoke(thinking);
                                },
                                getFlowDetail: (flowName, startOrder, count) => _serializer.SerializeFlowDetail(flowName, startOrder, count),
                                captureScreenshot: async target => await CaptureScreenForAiAsync(
                                    string.IsNullOrWhiteSpace(target) ? "windows" : target));
                            nextPlan = result.Item1;
                            tokensIn = result.Item2;
                            tokensOut = result.Item3;
                            break; // 成功，跳出重试循环
                        }
                        catch (OperationCanceledException) { throw; } // 用户取消不重试
                        catch (Exception apiEx)
                        {
                            retryCount++;
                            if (retryCount >= maxRetries)
                            {
                                AiFlowLogger.Error($"API 调用失败，已达最大重试次数 ({maxRetries})", apiEx);
                                throw; // 超过重试次数，向上抛出
                            }
                            int waitSec = retryCount * 5; // 5, 10, 15 秒递增
                            AiFlowLogger.Warn($"API 调用失败 ({apiEx.Message})，{waitSec} 秒后重试 ({retryCount}/{maxRetries})...");
                            AddMessage(AiChatRole.System, $"⚠️ API 调用失败，{waitSec} 秒后自动重试 ({retryCount}/{maxRetries})...");
                            await Task.Delay(TimeSpan.FromSeconds(waitSec), _cts.Token);
                        }
                    }

                    // 结束流式输出
                    StreamingEnded?.Invoke();
                    // 如果有流式文本，持久化为 Assistant 消息
                    var loopStreamedText = loopStreamBuilder.ToString();
                    var loopThinkingText = loopThinkingBuilder.Length > 0 ? loopThinkingBuilder.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(loopStreamedText))
                    {
                        var streamMsg = new AiChatMessage { Role = AiChatRole.Assistant, Content = loopStreamedText, ThinkingContent = loopThinkingText, IsStreamedToWebView = true };
                        Application.Current.Dispatcher.Invoke(() => Messages.Add(streamMsg));
                    }
                    else if (nextPlan.Done)
                    {
                        // 兜底：如果 AI 宣告自主任务完成，但完全没有输出文本（高冷），强制塞一条回复，避免对话界面死寂
                        var fallbackMsg = new AiChatMessage { Role = AiChatRole.Assistant, Content = string.IsNullOrWhiteSpace(shellResultsText) ? "任务已执行完毕。" : "我已经执行了命令，收集到的结果已输出在右侧全局日志面板中，请查看。", IsStreamedToWebView = false };
                        Application.Current.Dispatcher.Invoke(() => 
                        {
                            Messages.Add(fallbackMsg);
                            MessagesUpdated?.Invoke();
                        });
                    }

                    AiFlowLogger.Info($"AI 决策完成（Token: {tokensIn}+{tokensOut}）");

                    // 处理 AI 的新操作（创建、修改、删除等）
                    // 处理后续轮次的 PowerShell 命令
                    if (nextPlan.ShellCommands != null && nextPlan.ShellCommands.Count > 0)
                    {
                        var nextShellResults = await ExecuteShellCommandsAsync(nextPlan.ShellCommands, _cts.Token);
                        if (!string.IsNullOrEmpty(nextShellResults))
                            shellResultsText = nextShellResults;
                    }

                    bool hasNewActions = nextPlan.HasAnyAction
                        || !string.IsNullOrWhiteSpace(nextPlan.TargetFlow);

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

                    // 处理失败回退策略（仅当上一轮有卡片运行失败 且 任务未完成时才生效）
                    // AI 可能预防性地设置 failureStrategy，但如果没有实际失败，应忽略
                    if (hasFailedCards && !nextPlan.Done && !string.IsNullOrEmpty(nextPlan.FailureStrategy))
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
                            AiFlowLogger.Warn($"AI 中止任务: {nextPlan.Summary}");
                            AddMessage(AiChatRole.System, $"⚠️ AI 未能完成此任务，请尝试更详细地描述您的需求。");
                            ShowRetryButton = true;
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
                ShowRetryButton = true;
            }
            finally
            {
                IsAiExecuting = false;
            }
        }

        /// <summary>
        /// 执行 AI 请求的 PowerShell 命令列表（含安全检查和用户批准流程）
        /// </summary>
        private async Task<string> ExecuteShellCommandsAsync(
            List<Models.AiFlow.AiShellCommand> commands, CancellationToken ct)
        {
            var results = new List<(Models.AiFlow.AiShellCommand Cmd, PowerShellExecutorService.ShellResult Result)>();
            
            string? originalLoadingText = LoadingText;
            LoadingText = "⚡ 正在执行 PowerShell 命令...";

            try
            {
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
                    // 统一模式：方案已通过风险评估（低风险自动确认 / 中高风险用户已审批），
                    // Shell 命令无需二次批准
                    AiFlowLogger.Info($"[PowerShell] 🟡 自动批准: {cmd.Command}");
                }
                else
                {
                    // 白名单命令，自动执行
                    AiFlowLogger.Info($"[PowerShell] 🟢 白名单自动执行: {cmd.Command}");
                }

                // 面板提示
                App.Current.Dispatcher.Invoke(() => _mainViewModel.AddLog($"[AiFlow] 开始执行 PowerShell:\n{cmd.Command}"));

                // 执行命令
                var result = await _psService.ExecuteAsync(cmd, ct);
                results.Add((cmd, result));

                // 显示结果摘要
                if (result.Success)
                {
                    var outputPreview = result.Output.Length > 800
                        ? result.Output[..800] + "..."
                        : result.Output;
                    if (!string.IsNullOrWhiteSpace(outputPreview))
                        App.Current.Dispatcher.Invoke(() => _mainViewModel.AddLog($"[AiFlow] PowerShell 成功，部分输出:\n{outputPreview}"));
                    else
                        App.Current.Dispatcher.Invoke(() => _mainViewModel.AddLog($"[AiFlow] PowerShell 成功执行 (无输出)"));
                }
                else
                {
                    App.Current.Dispatcher.Invoke(() => _mainViewModel.AddLog($"[AiFlow] PowerShell 执行失败:\n{result.Error}"));
                }
            }

            return results.Count > 0
                ? PowerShellExecutorService.SerializeResults(results)
                : "";
            }
            finally
            {
                LoadingText = originalLoadingText ?? "✦ 正在生成方案...";
            }
        }
    }
}
