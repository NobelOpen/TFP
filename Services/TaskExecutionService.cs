using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenCvSharp;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Resources;
using TaskFlow.Models.TaskCards;
using TaskStatus = TaskFlow.Models.TaskCards.TaskStatus;

namespace TaskFlow.Services
{
    public interface ITaskExecutionService
    {
        event EventHandler<TaskCardBase>? TaskStarted;
        event EventHandler<TaskCardBase>? TaskCompleted;
        event EventHandler? AllTasksCompleted;
        event EventHandler<string>? LogMessage;

        Task ExecuteTaskAsync(TaskCardBase task, IList<TaskCardBase> allTasks, CancellationToken cancellationToken);
        Task ExecuteAllTasksAsync(IList<TaskCardBase> tasks, CancellationToken cancellationToken, IList<TaskCardBase>? allTasksForLookup = null);
        void Stop();
    }

    public partial class TaskExecutionService : ITaskExecutionService
    {
        /// <summary>静态复用 HttpClient，避免每次请求重新建立 TCP/TLS 连接</summary>
        private static readonly System.Net.Http.HttpClient _sharedHttpClient = new System.Net.Http.HttpClient();

        private readonly IAdbService _adbService;
        private readonly IScreenshotService _screenshotService;
        private readonly IOpenCVService _openCVService;
        private readonly IOcrService _ocrService;
        private readonly WeChatOcrService _weChatOcrService;
        private readonly VariableStore _variableStore;
        private readonly SubtitleService _subtitleService;

        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public event EventHandler<TaskCardBase>? TaskStarted;
        public event EventHandler<TaskCardBase>? TaskCompleted;
        public event EventHandler? AllTasksCompleted;
        public event EventHandler<string>? LogMessage;

        public TaskExecutionService(
            IAdbService adbService,
            IScreenshotService screenshotService,
            IOpenCVService openCVService,
            IOcrService ocrService,
            WeChatOcrService weChatOcrService,
            VariableStore variableStore,
            SubtitleService subtitleService)
        {
            _adbService = adbService;
            _screenshotService = screenshotService;
            _openCVService = openCVService;
            _ocrService = ocrService;
            _weChatOcrService = weChatOcrService;
            _variableStore = variableStore;
            _subtitleService = subtitleService;
        }

        public void Stop()
        {
            _cts?.Cancel();
            _isRunning = false;
            _subtitleService.HideAll();
        }

        public async Task ExecuteAllTasksAsync(IList<TaskCardBase> tasks, CancellationToken cancellationToken, IList<TaskCardBase>? allTasksForLookup = null)
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                // 重置所有任务状态
                foreach (var task in tasks)
                {
                    task.Reset();
                }

                int skipToIndex = 0;
                var loopStack = new Stack<(int StartIndex, ForLoopTaskCard Card, int CurrentIteration)>();
                // 记录每个分支组的条件结果
                var branchConditions = new Dictionary<Guid, bool>();

                for (int i = 0; i < tasks.Count; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    // 跳过到指定索引
                    if (i < skipToIndex) continue;

                    var task = tasks[i];
                    await ExecuteTaskAsync(task, allTasksForLookup ?? tasks, _cts.Token);

                    // 处理控制流
                    switch (task.TaskType)
                    {
                        case TaskType.EndTask:
                            i = tasks.Count; // 结束当前流程
                            break;

                        case TaskType.EndAllFlows:
                            _cts?.Cancel(); // 结束全部流程
                            i = tasks.Count;
                            break;

                        case TaskType.IfStart:
                            if (task is IfElseBranchTaskCard ifCard)
                            {
                                var condition = EvaluateCondition(ifCard, allTasksForLookup ?? tasks);
                                ifCard.ConditionResult = condition;

                                if (ifCard.BranchGroupId.HasValue)
                                {
                                    branchConditions[ifCard.BranchGroupId.Value] = condition;
                                }

                                if (!condition)
                                {
                                    // 条件为false，跳转到下一个ElifStart或ElseStart
                                    var nextBranchIndex = FindNextBranchIndex(tasks, i, ifCard.BranchGroupId);
                                    if (nextBranchIndex > i)
                                    {
                                        skipToIndex = nextBranchIndex;
                                    }
                                }
                            }
                            break;

                        case TaskType.IfEnd:
                            // 兼容旧数据：保留IfEnd的跳转逻辑
                            if (task is IfElseBranchTaskCard ifEndCard)
                            {
                                var elseEndIndex = FindBranchIndex(tasks, ifEndCard.BranchGroupId, BranchRole.ElseEnd);
                                if (elseEndIndex > i)
                                {
                                    skipToIndex = elseEndIndex + 1;
                                }
                            }
                            break;

                        case TaskType.ElifStart:
                            if (task is IfElseBranchTaskCard elifCard && elifCard.BranchGroupId.HasValue)
                            {
                                // 如果前面已有分支命中，跳过到ElseEnd+1
                                if (branchConditions.TryGetValue(elifCard.BranchGroupId.Value, out var prevCond) && prevCond)
                                {
                                    var elseEndIndex = FindBranchIndex(tasks, elifCard.BranchGroupId, BranchRole.ElseEnd);
                                    if (elseEndIndex > i)
                                    {
                                        skipToIndex = elseEndIndex + 1;
                                    }
                                }
                                else
                                {
                                    // 评估自身条件
                                    var elifCondition = EvaluateCondition(elifCard, allTasksForLookup ?? tasks);
                                    elifCard.ConditionResult = elifCondition;

                                    if (elifCondition)
                                    {
                                        // 条件为true，标记分支组已命中
                                        branchConditions[elifCard.BranchGroupId.Value] = true;
                                    }
                                    else
                                    {
                                        // 条件为false，跳到下一个ElifStart或ElseStart
                                        var nextBranchIndex = FindNextBranchIndex(tasks, i, elifCard.BranchGroupId);
                                        if (nextBranchIndex > i)
                                        {
                                            skipToIndex = nextBranchIndex;
                                        }
                                    }
                                }
                            }
                            break;

                        case TaskType.ElseStart:
                            // ElseStart：如果前面任何分支为true，跳过Else块
                            if (task is IfElseBranchTaskCard elseStartCard && elseStartCard.BranchGroupId.HasValue)
                            {
                                if (branchConditions.TryGetValue(elseStartCard.BranchGroupId.Value, out var cond) && cond)
                                {
                                    var elseEndIndex = FindBranchIndex(tasks, elseStartCard.BranchGroupId, BranchRole.ElseEnd);
                                    if (elseEndIndex > i)
                                    {
                                        skipToIndex = elseEndIndex + 1;
                                    }
                                }
                            }
                            break;

                        case TaskType.ForLoopStart:
                            if (task is ForLoopTaskCard loopStartCard)
                            {
                                // 解析循环次数
                                int actualLoopCount = loopStartCard.LoopCount;
                                if (loopStartCard.UseExpressionLoopCount && !string.IsNullOrWhiteSpace(loopStartCard.LoopCountExpression))
                                {
                                    try
                                    {
                                        string resolved = _variableStore.ResolveVariableReferences(loopStartCard.LoopCountExpression);
                                        resolved = ExpressionEvaluator.ResolveExpression(resolved, allTasksForLookup ?? tasks, _variableStore);
                                        if (int.TryParse(resolved.Trim(), out int exprCount))
                                        {
                                            actualLoopCount = exprCount;
                                            Log($"[{DateTime.Now:HH:mm:ss}] 循环次数表达式 '{loopStartCard.LoopCountExpression}' => {actualLoopCount}");
                                        }
                                        else
                                        {
                                            loopStartCard.ErrorMessage = $"循环次数表达式解析失败: '{loopStartCard.LoopCountExpression}' => '{resolved}'";
                                            loopStartCard.Status = TaskStatus.Failed;
                                            Log($"[{DateTime.Now:HH:mm:ss}] {loopStartCard.ErrorMessage}");
                                            break;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        loopStartCard.ErrorMessage = $"循环次数表达式解析异常: {ex.Message}";
                                        loopStartCard.Status = TaskStatus.Failed;
                                        Log($"[{DateTime.Now:HH:mm:ss}] {loopStartCard.ErrorMessage}");
                                        break;
                                    }
                                }
                                loopStartCard.LoopCount = actualLoopCount;
                                loopStack.Push((i, loopStartCard, 0));
                                loopStartCard.CurrentLoopIndex = 0;
                                loopStartCard.OutputLoopIndex = 0;
                            }
                            break;

                        case TaskType.ForLoopEnd:
                            if (task is ForLoopTaskCard && loopStack.Count > 0)
                            {
                                var (startIndex, startCard, currentIteration) = loopStack.Pop();

                                if (currentIteration < startCard.LoopCount)
                                {
                                    // 继续循环
                                    loopStack.Push((startIndex, startCard, currentIteration + 1));
                                    startCard.CurrentLoopIndex = currentIteration + 1;
                                    startCard.OutputLoopIndex = currentIteration + 1;

                                    // 重置跳转索引，避免上轮分支跳转影响下轮
                                    skipToIndex = 0;

                                    // 清除循环体内的分支条件记录，使下轮重新评估
                                    for (int j = startIndex + 1; j < i; j++)
                                    {
                                        if (tasks[j].BranchGroupId.HasValue)
                                        {
                                            branchConditions.Remove(tasks[j].BranchGroupId.Value);
                                        }
                                    }

                                    i = startIndex; // 回到循环开始
                                }
                            }
                            break;

                        case TaskType.BreakLoop:
                            if (task is BreakLoopTaskCard breakCard && breakCard.TargetLoopId.HasValue)
                            {
                                // 从 loopStack 中查找目标循环
                                var tempStack = new Stack<(int StartIndex, ForLoopTaskCard Card, int CurrentIteration)>();
                                bool found = false;

                                while (loopStack.Count > 0)
                                {
                                    var entry = loopStack.Pop();
                                    if (entry.Card.Id == breakCard.TargetLoopId.Value)
                                    {
                                        // 找到目标循环，跳到对应 ForLoopEnd 之后
                                        var endIndex = FindBranchIndex(tasks, entry.Card.BranchGroupId, BranchRole.ForLoopEnd);
                                        if (endIndex > i)
                                        {
                                            skipToIndex = endIndex + 1;
                                        }
                                        found = true;
                                        Log($"[调试] 中止循环: 跳转到索引 {skipToIndex}");
                                        break;
                                    }
                                    // 内层循环也一起弹出（不放回）
                                }

                                if (!found)
                                {
                                    // 未找到目标循环，恢复 stack
                                    while (tempStack.Count > 0)
                                    {
                                        loopStack.Push(tempStack.Pop());
                                    }
                                    Log($"[调试] 中止循环: 未找到目标循环 {breakCard.TargetLoopId}");
                                }
                            }
                            break;
                    }

                    // 每执行 10 个任务让出一次控制权，降低上下文切换开销
                    if (i % 10 == 0) await Task.Yield();
                }
            }
            finally
            {
                _isRunning = false;
                AllTasksCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task ExecuteTaskAsync(TaskCardBase task, IList<TaskCardBase> allTasks, CancellationToken cancellationToken)
        {
            // 释放旧的输出图像，避免循环体内多次迭代累积内存
            task.OutputImage?.Dispose();
            task.OutputImage = null;

            task.Status = TaskStatus.Running;
            task.StartTime = DateTime.Now;
            task.ErrorMessage = null;
            TaskStarted?.Invoke(this, task);
            Log($"[{DateTime.Now:HH:mm:ss}] 开始执行: {task.Name}");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool success = task.TaskType switch
                {
                    // 控制流卡片
                    TaskType.IfStart or TaskType.IfEnd or TaskType.ElifStart or TaskType.ElseStart or TaskType.ElseEnd => true,
                    TaskType.ForLoopStart or TaskType.ForLoopEnd => true,
                    TaskType.EndTask or TaskType.EndAllFlows or TaskType.BreakLoop => true,
                    TaskType.PauseTask => await ExecutePauseAsync((PauseTaskCard)task, allTasks, cancellationToken),
                    TaskType.GetTimestamp => ExecuteGetTimestamp((GetTimestampTaskCard)task),

                    // Windows操作
                    TaskType.WinLaunchApp => await ExecuteWinLaunchAppAsync((WinLaunchAppTaskCard)task),
                    TaskType.WinScreenshot => await ExecuteWinScreenshotAsync((WinScreenshotTaskCard)task),
                    TaskType.WinClick => await ExecuteWinClickAsync((WinClickTaskCard)task, allTasks),
                    TaskType.WinCloseApp => ExecuteWinCloseApp((WinCloseAppTaskCard)task),
                    TaskType.WinUiAutomation => await ExecuteWinUiAutomationAsync((WinUiAutomationTaskCard)task),
                    TaskType.WinSimulateInput => await ExecuteWinSimulateInputAsync((WinSimulateInputTaskCard)task),
                    TaskType.WinSubtitle => await ExecuteWinSubtitleAsync((WinSubtitleTaskCard)task, allTasks),

                    // ADB操作
                    TaskType.AdbConnect => await ExecuteAdbConnectAsync((AdbConnectTaskCard)task),
                    TaskType.AdbLaunchApp => await ExecuteAdbLaunchAppAsync((AdbLaunchAppTaskCard)task),
                    TaskType.AdbScreenshot => await ExecuteAdbScreenshotAsync((AdbScreenshotTaskCard)task),
                    TaskType.AdbClick => await ExecuteAdbClickAsync((AdbClickTaskCard)task, allTasks),
                    TaskType.AdbCloseApp => await ExecuteAdbCloseAppAsync((AdbCloseAppTaskCard)task),
                    TaskType.AdbDisconnect => await ExecuteAdbDisconnectAsync((AdbDisconnectTaskCard)task),

                    // 图像处理
                    TaskType.ImgCrop => await ExecuteImgCropAsync((ImgCropTaskCard)task, allTasks),
                    TaskType.ImgTemplateMatch => await ExecuteImgTemplateMatchAsync((ImgTemplateMatchTaskCard)task, allTasks),
                    TaskType.ImgOcr => await ExecuteImgOcrAsync((ImgOcrTaskCard)task, allTasks),
                    TaskType.ImgColorDetect => await ExecuteImgColorDetectAsync((ImgColorDetectTaskCard)task, allTasks),
                    TaskType.ImgColorSegment => await ExecuteImgColorSegmentAsync((ImgColorSegmentTaskCard)task, allTasks),
                    TaskType.ImgPreprocess => ExecuteImgPreprocess((ImgPreprocessTaskCard)task, allTasks),
                    TaskType.ImgBlobAnalysis => ExecuteImgBlobAnalysis((ImgBlobAnalysisTaskCard)task, allTasks),
                    TaskType.ImgResize => ExecuteImgResize((ImgResizeTaskCard)task, allTasks),

                    // 逻辑判断
                    TaskType.ExpressionEval => ExecuteExpressionEval((ExpressionEvalTaskCard)task, allTasks),


                    // 字符串操作
                    TaskType.StringSubstring => ExecuteStringSubstring((StringSubstringTaskCard)task, allTasks),

                    // 数据类型转换
                    TaskType.TypeConvert => ExecuteTypeConvert((TypeConvertTaskCard)task, allTasks),

                    // 数值解析
                    TaskType.ArrayParse => ExecuteArrayParse((ArrayParseTaskCard)task, allTasks),
                    
                    // AI翻译
                    TaskType.LlmTranslate => await ExecuteLlmTranslateAsync((LlmTranslateTaskCard)task, allTasks, cancellationToken),

                    // AI多模态识图
                    TaskType.LlmVision => await ExecuteLlmVisionAsync((LlmVisionTaskCard)task, allTasks, cancellationToken),

                    _ => false
                };

                task.Status = success ? TaskStatus.Success : TaskStatus.Failed;
            }
            catch (OperationCanceledException)
            {
                task.Status = TaskStatus.Failed;
                task.ErrorMessage = "任务已取消";
            }
            catch (Exception ex)
            {
                task.Status = TaskStatus.Failed;
                task.ErrorMessage = ex.Message;
                Log($"[{DateTime.Now:HH:mm:ss}] 错误: {task.Name} - {ex.Message}");
            }
            finally
            {
                task.CompletionTime = DateTime.Now;
                task.ExecutionDuration = task.CompletionTime - task.StartTime;
                TaskCompleted?.Invoke(this, task);
                Log($"[{DateTime.Now:HH:mm:ss}] 完成: {task.Name} - {task.Status}");
            }
        }

        #region Execution Methods

        private async Task<bool> ExecutePauseAsync(PauseTaskCard task, IList<TaskCardBase> allTasks, CancellationToken cancellationToken)
        {
            int durationMs = task.PauseDurationMs;

            // 如果设置了变量/任务引用表达式，优先使用
            if (!string.IsNullOrWhiteSpace(task.PauseDurationExpression))
            {
                try
                {
                    string resolved = _variableStore.ResolveVariableReferences(task.PauseDurationExpression);
                    resolved = ExpressionEvaluator.ResolveExpression(resolved, allTasks, _variableStore);
                    if (int.TryParse(resolved.Trim(), out int varDuration))
                    {
                        durationMs = varDuration;
                    }
                    else
                    {
                        task.ErrorMessage = $"暂停时长表达式解析失败: '{task.PauseDurationExpression}' => '{resolved}'";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    task.ErrorMessage = $"暂停时长表达式解析异常: {ex.Message}";
                    return false;
                }
            }

            Log($"[{DateTime.Now:HH:mm:ss}] 暂停 {durationMs}ms");
            await Task.Delay(durationMs, cancellationToken);
            return true;
        }


        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }

        #endregion
    }
}

