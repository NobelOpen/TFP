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

            // ===== 流程（Tab）级操作 —— 必须先于 targetFlow 解析执行 =====
            // 原因：AI 可能在同一批次中同时提交 createFlows + targetFlow，
            // 若先解析 targetFlow 再建流程，查找时目标尚不存在，会回退到主流程导致卡片写错位置。

            // 创建新流程（自动加 SUB_ 前缀并标记为子流程类型）
            if (plan.CreateFlows != null && plan.CreateFlows.Count > 0)
            {
                foreach (var newFlow in plan.CreateFlows)
                {
                    if (string.IsNullOrWhiteSpace(newFlow.Name)) continue;

                    // 强制添加 SUB_ 前缀（AI 可能忽略此规范）
                    var flowName = newFlow.Name.StartsWith("SUB_", StringComparison.OrdinalIgnoreCase)
                        ? newFlow.Name
                        : "SUB_" + newFlow.Name;

                    if (_mainViewModel.Tabs.Any(t => t.Name == flowName))
                    {
                        AiFlowLogger.Warn($"流程 \"{flowName}\" 已存在，跳过创建");
                        continue;
                    }
                    var tab = new WorkflowTab
                    {
                        Name = flowName,
                        Type = FlowType.SubFlow  // 标记为子流程
                    };
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _mainViewModel.Tabs.Add(tab);
                    });
                    AiFlowLogger.Info($"已创建子流程: {flowName}");
                }
            }

            // 自动修复 CallSubFlow 的 targetSubFlowId（始终执行，不依赖 createFlows）：
            // AI 可能不传 targetSubFlowId、传错主流程 ID、或传名称而非 GUID。
            // 此修复在所有 plan 步骤中查找 CallSubFlow 并自动推断正确的子流程 ID。
            var mainFlowId = _mainViewModel.SelectedTab?.Id.ToString() ?? "";
            if (!string.IsNullOrEmpty(mainFlowId) && plan.Plan.Count > 0)
            {
                foreach (var step in plan.Plan)
                {
                    FixCallSubFlowId(step, plan.CreateFlows, mainFlowId);
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

            // ===== 确定卡片创建的目标 Tab（支持 targetFlow 直接指向任意流程）=====
            // AI 通过 targetFlow 指定目标流程名，后端直接操作该 Tab，无需切换 UI
            // 注意：此处必须在 createFlows 执行之后，以便能找到本轮新建的流程
            WorkflowTab targetTab = _mainViewModel.SelectedTab!;
            if (!string.IsNullOrWhiteSpace(plan.TargetFlow))
            {
                // 先精确匹配，再尝试 SUB_ 前缀匹配
                var found = _mainViewModel.Tabs.FirstOrDefault(t => t.Name == plan.TargetFlow)
                         ?? _mainViewModel.Tabs.FirstOrDefault(t => t.Name == "SUB_" + plan.TargetFlow);
                if (found != null)
                {
                    targetTab = found;
                    AiFlowLogger.Info($"[TargetFlow] 卡片将直接创建到流程: {targetTab.Name}（不切换 UI）");
                }
                else
                {
                    AiFlowLogger.Warn($"[TargetFlow] 目标流程 \"{plan.TargetFlow}\" 不存在，回退到当前流程");
                }
            }

            // 确定目标 Tab 的卡片集合和计数器
            bool isTargetCurrentTab = (targetTab == _mainViewModel.SelectedTab);
            var targetCards = isTargetCurrentTab ? _mainViewModel.TaskCards : targetTab.TaskCards;

            // 画布为空时重置编号计数器，让新卡片从 #1 开始
            if (targetCards.Count == 0 && targetTab.NextTaskNumber > 1)
            {
                targetTab.NextTaskNumber = 1;
                AiFlowLogger.Info($"目标流程 {targetTab.Name} 为空，重置卡片编号计数器为 1");
            }

            // 预填充已有卡片映射：方案中新步骤的 sourceStep 可能引用已有卡片的序号
            foreach (var existingCard in targetCards)
            {
                if (!stepToCard.ContainsKey(existingCard.Order))
                    stepToCard[existingCard.Order] = existingCard;
            }

            // 切换 UI 到目标流程（支持 SUB_ 前缀，纯 UI 操作，不影响卡片创建目标）
            if (!string.IsNullOrWhiteSpace(plan.SwitchFlow))
            {
                // 先精确匹配，再尝试 SUB_ 前缀匹配
                var switchTarget = _mainViewModel.Tabs.FirstOrDefault(t => t.Name == plan.SwitchFlow)
                             ?? _mainViewModel.Tabs.FirstOrDefault(t => t.Name == "SUB_" + plan.SwitchFlow);
                if (switchTarget != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _mainViewModel.SelectedTab = switchTarget;
                    });
                    AiFlowLogger.Info($"已切换 UI 到流程: {switchTarget.Name}");
                }
                else
                {
                    AiFlowLogger.Warn($"SwitchFlow 目标流程 \"{plan.SwitchFlow}\" 不存在，保持当前流程");
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

            // 批量创建（向目标 Tab 直接写入，支持按步骤级别的 TargetFlowOverride 分组）
            var savedSelectedTask = _mainViewModel.SelectedTask;
            _mainViewModel.SelectedTask = null;

            // 按 TargetFlowOverride 分组步骤：null→使用全局 targetTab，""/其他→各自查找目标
            // 保持原始顺序：使用连续分段而非 GroupBy（避免打乱步骤间引用关系）
            var segments = new List<(WorkflowTab Tab, List<AiFlowPlanStep> Steps)>();
            List<AiFlowPlanStep>? currentSegment = null;
            WorkflowTab? currentSegmentTab = null;

            foreach (var step in plan.Plan)
            {
                WorkflowTab resolvedTab;
                if (step.TargetFlowOverride == null)
                {
                    // 未标记：使用全局 targetTab
                    resolvedTab = targetTab;
                }
                else if (step.TargetFlowOverride == "")
                {
                    // 空字符串标记：表示"当前主流程"（不使用全局 targetFlow）
                    resolvedTab = _mainViewModel.SelectedTab!;
                }
                else
                {
                    // 具体流程名
                    var found = _mainViewModel.Tabs.FirstOrDefault(t => t.Name == step.TargetFlowOverride)
                             ?? _mainViewModel.Tabs.FirstOrDefault(t => t.Name == "SUB_" + step.TargetFlowOverride);
                    resolvedTab = found ?? targetTab;
                    if (found != null && found != targetTab)
                    {
                        AiFlowLogger.Info($"[TargetFlowOverride] 步骤 #{step.Step} \"{step.Name}\" 将创建到流程: {found.Name}");
                    }
                }

                if (currentSegment == null || resolvedTab != currentSegmentTab)
                {
                    currentSegment = new List<AiFlowPlanStep>();
                    currentSegmentTab = resolvedTab;
                    segments.Add((resolvedTab, currentSegment));
                }
                currentSegment.Add(step);
            }

            foreach (var (segTab, segSteps) in segments)
            {
                // 切换目标 Tab 时需要重置编号计数器
                var segCards = segTab == _mainViewModel.SelectedTab ? _mainViewModel.TaskCards : segTab.TaskCards;
                if (segCards.Count == 0 && segTab.NextTaskNumber > 1)
                {
                    segTab.NextTaskNumber = 1;
                    AiFlowLogger.Info($"目标流程 {segTab.Name} 为空，重置卡片编号计数器为 1");
                }
                // 预填充已有卡片映射
                foreach (var existingCard in segCards)
                {
                    if (!stepToCard.ContainsKey(existingCard.Order))
                        stepToCard[existingCard.Order] = existingCard;
                }
                ProcessSteps(segSteps, stepToCard, reports, ref createdCount, mode, modelId, segTab);
            }

            // 引用重映射：将属性中的 #step引用 替换为 #actualOrder引用
            RemapStepReferences(stepToCard);

            if (isTargetCurrentTab && _mainViewModel.TaskCards.Count > 0)
                _mainViewModel.SelectedTask = _mainViewModel.TaskCards[^1];
            else if (!isTargetCurrentTab)
                _mainViewModel.RecalculateIndentLevels(); // 确保目标 Tab 的缩进计算正确

            _mainViewModel.AddLog($"[AI] 已创建 {createdCount} 个任务卡片");
            return (createdCount, reports);
        }

        /// <summary>
        /// 递归处理步骤列表（targetTab 为卡片创建目标，可与当前选中 Tab 不同）
        /// </summary>
        private void ProcessSteps(
            List<AiFlowPlanStep> steps,
            Dictionary<int, TaskCardBase> stepToCard,
            List<AiFlowReportItem> reports,
            ref int createdCount,
            AiAssistantMode mode,
            string modelId,
            WorkflowTab? targetTab = null)
        {
            foreach (var step in steps)
            {
                if (step.TaskType == "IfElseBlock")
                {
                    ProcessIfElseBlock(step, stepToCard, reports, ref createdCount, mode, modelId, targetTab);
                }
                else if (step.TaskType == "ForLoopBlock")
                {
                    ProcessForLoopBlock(step, stepToCard, reports, ref createdCount, mode, modelId, targetTab);
                }
                else
                {
                    ProcessNormalStep(step, stepToCard, reports, ref createdCount, mode, modelId, targetTab);
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
            string modelId,
            WorkflowTab? targetTab = null)
        {
            var cards = GetTargetCards(targetTab);
            var branchGroupId = Guid.NewGuid();

            var ifStart = new IfElseBranchTaskCard(BranchRole.IfStart)
            {
                BranchGroupId = branchGroupId,
                Order = GetAndIncrementOrder(targetTab)
            };

            if (step.Properties.TryGetValue("conditionExpression", out var condExpr) && !string.IsNullOrEmpty(condExpr))
                ifStart.ConditionExpression = condExpr;

            if (!string.IsNullOrEmpty(step.Name))
                ifStart.Name = step.Name;

            cards.Add(ifStart);
            _mainViewModel.SelectedTask = null;
            stepToCard[step.Step] = ifStart;
            createdCount++;
            AiFlowLogger.LogCardCreated("IfStart", ifStart.Name, ifStart.Order,
                $"BranchGroupId={branchGroupId}, Condition={ifStart.ConditionExpression}");

            if (step.IfBody != null && step.IfBody.Count > 0)
                ProcessSteps(step.IfBody, stepToCard, reports, ref createdCount, mode, modelId, targetTab);

            var elseStart = new IfElseBranchTaskCard(BranchRole.ElseStart)
            {
                BranchGroupId = branchGroupId,
                Order = GetAndIncrementOrder(targetTab)
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

            cards.Add(elseStart);
            _mainViewModel.SelectedTask = null;
            createdCount++;

            if (hasElseBody)
                ProcessSteps(step.ElseBody!, stepToCard, reports, ref createdCount, mode, modelId, targetTab);

            var elseEnd = new IfElseBranchTaskCard(BranchRole.ElseEnd)
            {
                BranchGroupId = branchGroupId,
                Order = GetAndIncrementOrder(targetTab)
            };

            if (!hasElseBody)
                elseEnd.IsHiddenByCollapse = true;

            cards.Add(elseEnd);
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
            string modelId,
            WorkflowTab? targetTab = null)
        {
            var cards = GetTargetCards(targetTab);
            var branchGroupId = Guid.NewGuid();

            var loopStart = new ForLoopTaskCard(BranchRole.ForLoopStart)
            {
                BranchGroupId = branchGroupId,
                Order = GetAndIncrementOrder(targetTab)
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

            cards.Add(loopStart);
            _mainViewModel.SelectedTask = null;
            stepToCard[step.Step] = loopStart;
            createdCount++;
            AiFlowLogger.LogCardCreated("ForLoopStart", loopStart.Name, loopStart.Order,
                $"BranchGroupId={branchGroupId}, LoopCount={loopStart.LoopCount}");

            if (step.LoopBody != null && step.LoopBody.Count > 0)
                ProcessSteps(step.LoopBody, stepToCard, reports, ref createdCount, mode, modelId, targetTab);

            var loopEnd = new ForLoopTaskCard(BranchRole.ForLoopEnd)
            {
                BranchGroupId = branchGroupId,
                Order = GetAndIncrementOrder(targetTab)
            };

            cards.Add(loopEnd);
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
            string modelId,
            WorkflowTab? targetTab = null)
        {
            if (!Enum.TryParse<Models.TaskCards.TaskType>(step.TaskType, out var taskType))
            {
                _mainViewModel.AddLog($"[AI] 跳过未知卡片类型: {step.TaskType}");
                return;
            }

            // 直接创建卡片对象（不依赖 AddTaskCommand）
            var newCard = _mainViewModel.CreateTaskCard(taskType);
            if (newCard == null) return;
            newCard.Order = GetAndIncrementOrder(targetTab);
            var cards = GetTargetCards(targetTab);
            cards.Add(newCard);

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
        /// 获取目标 Tab 的卡片集合（targetTab 为 null 时使用当前流程）
        /// </summary>
        private System.Collections.ObjectModel.ObservableCollection<TaskCardBase> GetTargetCards(WorkflowTab? targetTab)
        {
            if (targetTab == null || targetTab == _mainViewModel.SelectedTab)
                return _mainViewModel.TaskCards;
            return targetTab.TaskCards;
        }

        /// <summary>
        /// 获取目标 Tab 的下一个序号，并自动递增
        /// </summary>
        private int GetAndIncrementOrder(WorkflowTab? targetTab)
        {
            if (targetTab == null || targetTab == _mainViewModel.SelectedTab)
                return _mainViewModel.NextTaskNumber++;
            return targetTab.NextTaskNumber++;
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

        /// <summary>
        /// 引用重映射：将新创建卡片属性中的 #step引用 替换为 #actualOrder引用
        /// 例如: "#1 查找浏览器.查找路径" → "#4 查找浏览器.查找路径"
        /// </summary>
        private void RemapStepReferences(Dictionary<int, TaskCardBase> stepToCard)
        {
            // 构建映射表: step编号 → 实际order
            var stepToOrder = new Dictionary<int, int>();
            foreach (var kv in stepToCard)
            {
                if (kv.Key != kv.Value.Order)
                    stepToOrder[kv.Key] = kv.Value.Order;
            }

            if (stepToOrder.Count == 0) return; // 无需映射

            AiFlowLogger.Info($"引用重映射: {string.Join(", ", stepToOrder.Select(kv => $"#{kv.Key}→#{kv.Value}"))}");

            // 正则匹配 #数字 开头的引用模式（如 "#1 xxx.yyy"）
            var refPattern = new System.Text.RegularExpressions.Regex(@"#(\d+)\s");

            // 遍历所有新创建的卡片的字符串属性
            foreach (var card in stepToCard.Values)
            {
                var cardType = card.GetType();
                foreach (var prop in cardType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    if (prop.PropertyType != typeof(string) || !prop.CanWrite) continue;
                    if (prop.GetCustomAttributes(typeof(Newtonsoft.Json.JsonIgnoreAttribute), true).Length > 0) continue;

                    try
                    {
                        var val = prop.GetValue(card) as string;
                        if (string.IsNullOrEmpty(val) || !val.Contains('#')) continue;

                        var newVal = refPattern.Replace(val, match =>
                        {
                            if (int.TryParse(match.Groups[1].Value, out int stepNum) && stepToOrder.ContainsKey(stepNum))
                            {
                                return $"#{stepToOrder[stepNum]} ";
                            }
                            return match.Value;
                        });

                        if (newVal != val)
                        {
                            prop.SetValue(card, newVal);
                            AiFlowLogger.Info($"重映射 #{card.Order} {card.Name}.{prop.Name}: {val} → {newVal}");
                        }
                    }
                    catch { /* 忽略反射异常 */ }
                }
            }
        }
        /// <summary>
        /// 递归修复 CallSubFlow 步骤中错误的 targetSubFlowId。
        /// AI 可能在同一轮中同时创建子流程和 CallSubFlow 卡片，但请求时子流程尚不存在，
        /// AI 只能看到主流程的 ID，可能错误地用主流程 ID 作为 targetSubFlowId。
        /// </summary>
        private void FixCallSubFlowId(AiFlowPlanStep step, List<AiFlowNewTab>? createdFlows, string mainFlowId)
        {
            if (step.TaskType == "CallSubFlow")
            {
                bool hasTargetId = step.Properties.TryGetValue("targetSubFlowId", out var id) && !string.IsNullOrWhiteSpace(id);
                bool needsFix = false;
                
                if (!hasTargetId)
                {
                    // 情况 1：AI 完全没传 targetSubFlowId（最常见）
                    needsFix = true;
                }
                else if (id == mainFlowId)
                {
                    // 情况 2：AI 错误使用了主流程 ID
                    needsFix = true;
                }
                else if (!Guid.TryParse(id, out _))
                {
                    // 情况 3：AI 传了名称而非 GUID（如 "SUB_密码加密"）
                    needsFix = true;
                }

                if (needsFix)
                {
                    WorkflowTab? matchedTab = null;

                    // 策略 1：如果 AI 传的是子流程名称字符串，按名称直接匹配
                    if (hasTargetId && !Guid.TryParse(id, out _))
                    {
                        var nameToFind = id!;
                        matchedTab = _mainViewModel.Tabs.FirstOrDefault(t =>
                            t.Type == FlowType.SubFlow &&
                            (t.Name.Equals(nameToFind, StringComparison.OrdinalIgnoreCase) ||
                             t.Name.Equals("SUB_" + nameToFind, StringComparison.OrdinalIgnoreCase)));
                    }

                    // 策略 2：从新创建的子流程中查找
                    if (matchedTab == null && createdFlows != null && createdFlows.Count > 0)
                    {
                        if (createdFlows.Count == 1)
                        {
                            var flowName = createdFlows[0].Name;
                            var fullName = flowName.StartsWith("SUB_", StringComparison.OrdinalIgnoreCase) ? flowName : "SUB_" + flowName;
                            matchedTab = _mainViewModel.Tabs.FirstOrDefault(t => t.Name == fullName);
                        }
                        else
                        {
                            foreach (var flow in createdFlows)
                            {
                                var fullName = flow.Name.StartsWith("SUB_", StringComparison.OrdinalIgnoreCase) ? flow.Name : "SUB_" + flow.Name;
                                var tab = _mainViewModel.Tabs.FirstOrDefault(t => t.Name == fullName);
                                if (tab != null && step.Name.Contains(flow.Name, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchedTab = tab;
                                    break;
                                }
                            }
                        }
                    }

                    // 策略 3：从所有已有子流程 Tabs 中推断（自主循环第 2 轮时 createFlows 为空）
                    if (matchedTab == null)
                    {
                        var subFlowTabs = _mainViewModel.Tabs
                            .Where(t => t.Type == FlowType.SubFlow && t.TaskCards.Count > 0)
                            .ToList();

                        if (subFlowTabs.Count == 1)
                        {
                            // 只有一个子流程，直接匹配
                            matchedTab = subFlowTabs[0];
                        }
                        else if (subFlowTabs.Count > 1)
                        {
                            // 多个子流程，按步骤名称模糊匹配
                            foreach (var tab in subFlowTabs)
                            {
                                var cleanName = tab.Name.StartsWith("SUB_") ? tab.Name.Substring(4) : tab.Name;
                                if (step.Name.Contains(cleanName, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchedTab = tab;
                                    break;
                                }
                            }
                        }
                    }

                    if (matchedTab != null)
                    {
                        var newId = matchedTab.Id.ToString();
                        step.Properties["targetSubFlowId"] = newId;
                        AiFlowLogger.Info($"[自动修复] CallSubFlow \"{step.Name}\" 的 targetSubFlowId 设置为子流程 {matchedTab.Name} (ID: {newId})");
                    }
                    else
                    {
                        AiFlowLogger.Warn($"[修复失败] CallSubFlow \"{step.Name}\" 无法推断目标子流程 ID");
                    }
                }
            }

            // 递归处理嵌套的 IfBody/ElseBody/LoopBody
            if (step.IfBody != null) foreach (var s in step.IfBody) FixCallSubFlowId(s, createdFlows, mainFlowId);
            if (step.ElseBody != null) foreach (var s in step.ElseBody) FixCallSubFlowId(s, createdFlows, mainFlowId);
            if (step.LoopBody != null) foreach (var s in step.LoopBody) FixCallSubFlowId(s, createdFlows, mainFlowId);
        }
    }
}
