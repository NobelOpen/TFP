using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using TaskFlow.Models.TaskCards;
using TaskFlow.Models;
using TaskFlow.Resources;

namespace TaskFlow.ViewModels
{
    // 执行调度相关功能
    public partial class MainViewModel
    {
        [RelayCommand]
        private async Task RunAllAsync()
        {
            if (IsBusy) return;

            IsRunning = true;
            _cts = new CancellationTokenSource();
            PreventScreenSleep();

            try
            {
                int round = 0;
                do
                {
                    round++;
                    if (Settings.RepeatRunAll && round > 1)
                        AddLog($"========== 第 {round} 轮循环执行全部流程 ==========");
                    else
                        AddLog(Strings.VM_StartRunAll);

                    // 先保存当前分页状态
                    if (SelectedTab != null)
                        SaveCurrentTabState(SelectedTab);

                    // 依次运行每个分页
                    for (int i = 0; i < Tabs.Count; i++)
                    {
                        if (_cts.Token.IsCancellationRequested) break;

                        var tab = Tabs[i];
                        
                        // 子流程不能被主执行流程自动遍历运行，只能通过 CallSubFlow 调用
                        if (tab.Type == FlowType.SubFlow) continue;

                        AddLog($"--- 开始执行分页: {tab.Name} ({i + 1}/{Tabs.Count}) ---");

                        // 切换到该分页
                        SelectedTab = tab;

                        await _executionService.ExecuteAllTasksAsync(TaskCards.ToList(), _cts.Token);

                        if (_cts.Token.IsCancellationRequested) break;
                        AddLog($"--- 分页 {tab.Name} 执行完成 ---");

                        // 流程执行间隔
                        if (Settings.FlowExecutionIntervalMs > 0 && i < Tabs.Count - 1)
                        {
                            AddLog(string.Format(Strings.VM_WaitInterval, Settings.FlowExecutionIntervalMs));
                            await Task.Delay(Settings.FlowExecutionIntervalMs, _cts.Token);
                        }
                    }

                    // 重复执行间隔
                    if (Settings.RepeatRunAll && Settings.RepeatIntervalMs > 0 && !_cts.Token.IsCancellationRequested)
                    {
                        AddLog(string.Format(Strings.VM_RepeatWait, Settings.RepeatIntervalMs));
                        await Task.Delay(Settings.RepeatIntervalMs, _cts.Token);
                    }
                }
                while (Settings.RepeatRunAll && !_cts.Token.IsCancellationRequested);
            }
            catch (OperationCanceledException)
            {
                AddLog("流程已被取消");
            }
            catch (Exception ex)
            {
                AddLog(string.Format(Strings.VM_ExecutionError, ex.Message));
            }
            finally
            {
                RestoreScreenSleep();
                // 如果还有 InputCombo 后台任务在运行，等它们全部结束再改状态
                if (_executionService.HasActiveInputCombos)
                {
                    _executionService.InputCombosAllDone += OnInputCombosFinished;
                }
                else
                {
                    IsRunning = false;
                }
            }
        }

        [RelayCommand]
        private async Task RunSelectedAsync()
        {
            if (IsBusy || SelectedTask == null) return;

            IsRunning = true;
            _cts = new CancellationTokenSource();
            PreventScreenSleep();

            try
            {
                // 如果选中的是分支卡片，执行整个分支
                if (SelectedTask.BranchGroupId.HasValue)
                {
                    var branchCards = TaskCards.Where(t => t.BranchGroupId == SelectedTask.BranchGroupId).ToList();

                    // 找到分支的起始和结束索引
                    int startIdx = int.MaxValue;
                    int endIdx = -1;

                    foreach (var bc in branchCards)
                    {
                        var idx = TaskCards.IndexOf(bc);
                        if (idx < startIdx) startIdx = idx;
                        if (idx > endIdx) endIdx = idx;
                    }

                    if (startIdx >= 0 && endIdx >= startIdx)
                    {
                        // 提取范围内的所有卡片（包括分支内插入的普通卡片）
                        var branchRange = new List<TaskCardBase>();
                        for (int i = startIdx; i <= endIdx; i++)
                        {
                            branchRange.Add(TaskCards[i]);
                        }

                        AddLog($"========== 开始执行分支: {branchRange.Count} 个卡片 ==========");
                        await _executionService.ExecuteAllTasksAsync(branchRange, _cts.Token, TaskCards.ToList());
                    }
                }
                else
                {
                    AddLog(string.Format(Strings.VM_StartSingle, SelectedTask.Name));
                    await _executionService.ExecuteTaskAsync(SelectedTask, TaskCards.ToList(), _cts.Token);
                }
            }
            catch (Exception ex)
            {
                AddLog(string.Format(Strings.VM_ExecutionError, ex.Message));
            }
            finally
            {
                RestoreScreenSleep();
                if (_executionService.HasActiveInputCombos)
                {
                    _executionService.InputCombosAllDone += OnInputCombosFinished;
                }
                else
                {
                    IsRunning = false;
                }
            }
        }

        [RelayCommand]
        private void StopExecution()
        {
            _cts?.Cancel();
            _executionService.Stop();
            RestoreScreenSleep();
            IsRunning = false;
            AddLog("========== 已停止执行 ==========");
        }

        [RelayCommand]
        private async Task RunCurrentFlowAsync()
        {
            if (IsBusy) return;

            IsRunning = true;
            _cts = new CancellationTokenSource();
            PreventScreenSleep();

            try
            {
                var tabName = SelectedTab?.Name ?? "当前流程";
                AddLog($"========== 开始执行当前流程: {tabName} ==========");
                await _executionService.ExecuteAllTasksAsync(TaskCards.ToList(), _cts.Token);
                AddLog($"========== 当前流程执行完毕: {tabName} ==========");
            }
            catch (Exception ex)
            {
                AddLog(string.Format(Strings.VM_ExecutionError, ex.Message));
            }
            finally
            {
                RestoreScreenSleep();
                if (_executionService.HasActiveInputCombos)
                {
                    _executionService.InputCombosAllDone += OnInputCombosFinished;
                }
                else
                {
                    IsRunning = false;
                }
            }
        }

        private void OnTaskStarted(object? sender, TaskCardBase task)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                CurrentRunningTask = task;
                CurrentTaskBreadcrumb = task.BreadcrumbText;

                // 计算上一个和下一个任务
                var index = TaskCards.IndexOf(task);
                PreviousTask = index > 0 ? TaskCards[index - 1] : null;
                NextTask = index < TaskCards.Count - 1 ? TaskCards[index + 1] : null;
            });
        }

        private void OnTaskCompleted(object? sender, TaskCardBase task)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                // 如果是选中的任务且有输出图像，更新显示
                if (SelectedTask == task && task.OutputImage != null && !task.OutputImage.IsDisposed && !task.OutputImage.Empty())
                {
                    DisplayImage = task.OutputImage;
                }
            });
        }

        private void OnAllTasksCompleted(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                CurrentRunningTask = null;
                CurrentTaskBreadcrumb = null;
                PreviousTask = null;
                NextTask = null;
                // IsRunning 由 finally 块负责（会检查 InputCombo 状态）
                AddLog("========== 全部流程执行完毕 ==========");
            });
        }

        /// <summary>
        /// InputCombo 后台任务全部结束后的回调
        /// </summary>
        private void OnInputCombosFinished(object? sender, EventArgs e)
        {
            _executionService.InputCombosAllDone -= OnInputCombosFinished;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                IsRunning = false;
                AddLog("输入组合后台任务已全部结束");
            });
        }

        private void OnLogMessage(object? sender, string message)
        {
            AddLog(message);
        }

        /// <summary>
        /// 供 AI 自主模式调用：运行单张卡片并返回结果
        /// 不影响全局 IsRunning 状态
        /// </summary>
        public async Task ExecuteSingleCardAsync(TaskCardBase card, CancellationToken cancellationToken)
        {
            await _executionService.ExecuteTaskAsync(card, TaskCards.ToList(), cancellationToken);
        }
    }
}
