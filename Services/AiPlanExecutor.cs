using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Models.AiFlow;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Services
{
    /// <summary>
    /// AI 方案执行器：将 AI 生成的方案转化为实际的任务卡片和变量操作。
    /// 从 AiFlowViewModel 解耦而来，专注于方案的应用逻辑。
    /// </summary>
    public class AiPlanExecutor
    {
        private readonly ViewModels.MainViewModel _mainViewModel;

        public AiPlanExecutor(ViewModels.MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
        }

        /// <summary>
        /// 将 AI 方案转化为实际的任务卡片（支持嵌套控制流区块）
        /// </summary>
        public (int CreatedCount, List<AiFlowReportItem> Reports) CreateTaskCardsFromPlan(
            AiFlowPlanResponse plan, AiAssistantMode mode, string modelId)
        {
            var stepToCard = new Dictionary<int, TaskCardBase>();
            var reports = new List<AiFlowReportItem>();
            int createdCount = 0;

            // 预填充已有卡片映射：方案中新步骤的 sourceStep 可能引用已有卡片的序号
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

            // 切换到目标流程
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

            // 删除变量
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

            // 修改变量
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

            // 创建变量
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

            // 删除指定卡片
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

                    // 定位插入位置
                    int insertIndex = -1;
                    var groupCards = _mainViewModel.TaskCards
                        .Where(c => c.BranchGroupId == groupId)
                        .OrderBy(c => _mainViewModel.TaskCards.IndexOf(c))
                        .ToList();

                    if (branchTarget == "if")
                    {
                        var ifStart = groupCards.FirstOrDefault(c => c.BranchRole == BranchRole.IfStart);
                        var nextMarker = groupCards.FirstOrDefault(c =>
                            c.BranchRole == BranchRole.ElseStart || c.BranchRole == BranchRole.ElseEnd);
                        if (ifStart != null && nextMarker != null)
                            insertIndex = _mainViewModel.TaskCards.IndexOf(nextMarker);
                    }
                    else if (branchTarget == "else")
                    {
                        var elseStart = groupCards.FirstOrDefault(c => c.BranchRole == BranchRole.ElseStart);
                        var elseEnd = groupCards.FirstOrDefault(c => c.BranchRole == BranchRole.ElseEnd);
                        if (elseStart != null && elseEnd != null)
                            insertIndex = _mainViewModel.TaskCards.IndexOf(elseEnd);
                    }
                    else if (branchTarget == "loop")
                    {
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

                    int insertedCount = 0;
                    foreach (var step in insertReq.Cards)
                    {
                        var card = CreateSingleCardFromStep(step, stepToCard, reports, mode, modelId);
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

            // 批量创建
            var savedSelectedTask = _mainViewModel.SelectedTask;
            _mainViewModel.SelectedTask = null;

            ProcessSteps(plan.Plan, stepToCard, reports, ref createdCount, mode, modelId);

            if (_mainViewModel.TaskCards.Count > 0)
                _mainViewModel.SelectedTask = _mainViewModel.TaskCards[^1];

            _mainViewModel.AddLog($"[AI] 已创建 {createdCount} 个任务卡片");
            return (createdCount, reports);
        }

        /// <summary>
        /// 递归处理步骤列表
        /// </summary>
        private void ProcessSteps(
            List<AiFlowPlanStep> steps,
            Dictionary<int, TaskCardBase> stepToCard,
            List<AiFlowReportItem> reports,
            ref int createdCount,
            AiAssistantMode mode,
            string modelId)
        {
            foreach (var step in steps)
            {
                if (step.TaskType == "IfElseBlock")
                {
                    ProcessIfElseBlock(step, stepToCard, reports, ref createdCount, mode, modelId);
                }
                else if (step.TaskType == "ForLoopBlock")
                {
                    ProcessForLoopBlock(step, stepToCard, reports, ref createdCount, mode, modelId);
                }
                else
                {
                    ProcessNormalStep(step, stepToCard, reports, ref createdCount, mode, modelId);
                }
            }
        }

        /// <summary>
        /// 处理 IfElseBlock 区块
        /// </summary>
        private void ProcessIfElseBlock(
            AiFlowPlanStep step,
            Dictionary<int, TaskCardBase> stepToCard,
            List<AiFlowReportItem> reports,
            ref int createdCount,
            AiAssistantMode mode,
            string modelId)
        {
            var branchGroupId = Guid.NewGuid();

            var ifStart = new IfElseBranchTaskCard(BranchRole.IfStart)
            {
                BranchGroupId = branchGroupId,
                Order = _mainViewModel.NextTaskNumber++
            };

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

            if (step.IfBody != null && step.IfBody.Count > 0)
                ProcessSteps(step.IfBody, stepToCard, reports, ref createdCount, mode, modelId);

            var elseStart = new IfElseBranchTaskCard(BranchRole.ElseStart)
            {
                BranchGroupId = branchGroupId,
                Order = _mainViewModel.NextTaskNumber++
            };

            bool hasElseBody = step.ElseBody != null && step.ElseBody.Count > 0;

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

            if (hasElseBody)
                ProcessSteps(step.ElseBody!, stepToCard, reports, ref createdCount, mode, modelId);

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
        /// 处理 ForLoopBlock 区块
        /// </summary>
        private void ProcessForLoopBlock(
            AiFlowPlanStep step,
            Dictionary<int, TaskCardBase> stepToCard,
            List<AiFlowReportItem> reports,
            ref int createdCount,
            AiAssistantMode mode,
            string modelId)
        {
            var branchGroupId = Guid.NewGuid();

            var loopStart = new ForLoopTaskCard(BranchRole.ForLoopStart)
            {
                BranchGroupId = branchGroupId,
                Order = _mainViewModel.NextTaskNumber++
            };

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

            if (step.LoopBody != null && step.LoopBody.Count > 0)
                ProcessSteps(step.LoopBody, stepToCard, reports, ref createdCount, mode, modelId);

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
            ref int createdCount,
            AiAssistantMode mode,
            string modelId)
        {
            if (!Enum.TryParse<Models.TaskCards.TaskType>(step.TaskType, out var taskType))
            {
                _mainViewModel.AddLog($"[AI] 跳过未知卡片类型: {step.TaskType}");
                return;
            }

            _mainViewModel.AddTaskCommand.Execute(taskType);
            var newCard = _mainViewModel.SelectedTask;
            if (newCard == null) return;

            if (!string.IsNullOrEmpty(step.Name))
                newCard.Name = step.Name;

            stepToCard[step.Step] = newCard;
            _mainViewModel.SelectedTask = null;
            createdCount++;
            AiFlowLogger.LogCardCreated(step.TaskType, newCard.Name, newCard.Order);

            // 调用卡片自身的属性填充方法（多态替代 switch）
            var missingProps = newCard.FillFromAiPlan(step, stepToCard);

            // 自主模式下：对 WinClick 卡片应用标定校正
            if (mode == AiAssistantMode.Autonomous && newCard is WinClickTaskCard click)
                ApplyCalibrationToClick(click, step, modelId);

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
            List<AiFlowReportItem> reports,
            AiAssistantMode mode,
            string modelId)
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

            // 调用卡片自身的属性填充方法
            var missingProps = card.FillFromAiPlan(step, stepToCard);

            // 自主模式下：对 WinClick 卡片应用标定校正
            if (mode == AiAssistantMode.Autonomous && card is WinClickTaskCard click)
                ApplyCalibrationToClick(click, step, modelId);

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
        /// 自主模式下，对 WinClick 卡片的 AI 估算坐标应用标定校正
        /// </summary>
        private void ApplyCalibrationToClick(WinClickTaskCard click, AiFlowPlanStep step, string modelId)
        {
            if (click.StartX == 0 && click.StartY == 0) return;
            if (step.SourceStep.HasValue) return; // 有坐标来源的不校正

            var screenshotCard = _mainViewModel.TaskCards
                .LastOrDefault(c => c is WinScreenshotTaskCard && c.OutputImage != null && !c.OutputImage.Empty());
            if (screenshotCard?.OutputImage == null) return;

            int imgW = screenshotCard.OutputImage.Width;
            int imgH = screenshotCard.OutputImage.Height;
            var cal = CalibrationService.GetCalibration(modelId, imgW, imgH);
            if (cal != null)
            {
                var (cx, cy) = CalibrationService.CalibrateCoordinates(cal, click.StartX, click.StartY);
                AiFlowLogger.Info($"标定校正: ({click.StartX},{click.StartY}) → ({cx},{cy})");
                click.StartX = cx;
                click.StartY = cy;
            }
        }
    }
}
