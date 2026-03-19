using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using TaskFlow.Models.AiFlow;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Services
{
    /// <summary>
    /// AI 流程序列化器：负责将当前画布状态、卡片结果、方案文本等
    /// 序列化为 AI 可理解的格式。从 AiFlowViewModel 解耦而来。
    /// </summary>
    public class AiFlowSerializer
    {
        private readonly ViewModels.MainViewModel _mainViewModel;

        public AiFlowSerializer(ViewModels.MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
        }

        /// <summary>
        /// 将 AI 方案格式化为可读文本（支持嵌套缩进）
        /// </summary>
        public string FormatPlanAsText(AiFlowPlanResponse plan)
        {
            var sb = new StringBuilder();
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
        /// 递归格式化步骤列表（带缩进）
        /// </summary>
        public void FormatSteps(StringBuilder sb, List<AiFlowPlanStep> steps, string indent)
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
        public string FormatReportAsText(int createdCount, List<AiFlowReportItem> reports)
        {
            if (reports.Count == 0)
                return "";
            return $"⚠️ {reports.Count} 项需要手动配置：";
        }

        /// <summary>
        /// 序列化指定卡片的运行结果为 AI 可理解的文本
        /// </summary>
        public string SerializeCardResults(List<int> orders)
        {
            var sb = new StringBuilder();

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

                var matchResultProp = card.GetType().GetProperty("MatchResult");
                if (matchResultProp != null)
                {
                    var matchVal = matchResultProp.GetValue(card);
                    if (matchVal != null)
                        sb.AppendLine($"  匹配结果: {matchVal}");
                }

                if (card.OutputImage != null && !card.OutputImage.Empty())
                    sb.AppendLine($"  图像分辨率: {card.OutputImage.Width}x{card.OutputImage.Height}");

                foreach (var propName in new[] { "OutputPath", "OutputFilePath", "OutputSavePath", "OutputTranslatedFilePath" })
                {
                    var pathProp = card.GetType().GetProperty(propName);
                    if (pathProp != null)
                    {
                        var pathVal = pathProp.GetValue(card) as string;
                        if (!string.IsNullOrEmpty(pathVal))
                        {
                            sb.AppendLine($"  路径输出: {pathVal}");
                            break;
                        }
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 从消息列表中提取最近的 User + Assistant 对话历史
        /// </summary>
        public List<(string Role, string Content)> BuildConversationHistory(ObservableCollection<AiChatMessage> messages)
        {
            var history = new List<(string Role, string Content)>();

            var relevantMessages = messages
                .Where(m => m.Role != AiChatRole.System)
                .ToList();

            var recent = relevantMessages.Skip(Math.Max(0, relevantMessages.Count - 20)).ToList();

            foreach (var msg in recent)
            {
                var role = msg.Role == AiChatRole.User ? "user" : "assistant";
                string content;
                if (msg.Role == AiChatRole.User)
                {
                    content = msg.Content;
                }
                else
                {
                    content = msg.Content.Length > 300
                        ? msg.Content[..300] + "..."
                        : msg.Content;
                }
                history.Add((role, content));
            }

            return history;
        }

        /// <summary>
        /// 将当前画布上的任务卡片序列化为 AI 可理解的文本摘要
        /// </summary>
        public string SerializeCurrentFlow()
        {
            var cards = _mainViewModel.TaskCards;
            var variables = _mainViewModel.VariableStore.Variables;
            bool hasCards = cards != null && cards.Count > 0;
            bool hasVars = variables != null && variables.Count > 0;

            var sb = new StringBuilder();

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

            // 序列化当前流程卡片
            if (hasCards)
            {
                var currentTabName = _mainViewModel.SelectedTab?.Name ?? "当前流程";
                sb.AppendLine($"当前流程「{currentTabName}」已有 {cards!.Count} 个任务卡片：");

                var excludeProps = new HashSet<string> { "Id", "Name", "Order", "Status", "ErrorMessage",
                    "IndentLevel", "BranchRole", "BranchGroupId", "IsCollapsed", "IsHiddenByCollapse",
                    "TaskType", "OutputsImage", "OutputsText", "OutputsCoordinates", "OutputsBoolResult" };

                foreach (var card in cards)
                {
                    var indent = new string(' ', card.IndentLevel * 2);
                    var typeName = card.GetType().Name.Replace("TaskCard", "");

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

                    // 序列化关键属性
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
