using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using TaskFlow.Models;
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

        // ===== TaskType → 中文显示名映射 =====
        private static readonly Dictionary<string, string> _typeNames = new()
        {
            ["WinLaunchApp"]     = "启动应用",
            ["WinScreenshot"]    = "截屏",
            ["WinClick"]         = "点击",
            ["WinCloseApp"]      = "关闭应用",
            ["WinUiAutomation"]  = "UI自动化",
            ["WinSimulateInput"] = "模拟按键",
            ["WinSubtitle"]      = "字幕显示",
            ["WinFindFile"]      = "查找文件",
            ["WinTextInput"]     = "文本输入",
            ["EventListener"]    = "事件监听",
            ["InputCombo"]       = "按键组合",
            ["AdbConnect"]       = "ADB连接",
            ["AdbLaunchApp"]     = "ADB启动",
            ["AdbScreenshot"]    = "ADB截屏",
            ["AdbClick"]         = "ADB点击",
            ["AdbCloseApp"]      = "ADB关闭",
            ["AdbDisconnect"]    = "ADB断开",
            ["ImgOcr"]           = "OCR识别",
            ["ImgTemplateMatch"] = "模板匹配",
            ["ImgCrop"]          = "图像裁剪",
            ["ImgColorDetect"]   = "颜色检测",
            ["ImgColorSegment"]  = "颜色分割",
            ["ImgPreprocess"]    = "图像预处理",
            ["ImgBlobAnalysis"]  = "图像分析",
            ["ImgResize"]        = "图像缩放",
            ["ExpressionEval"]   = "表达式",
            ["StringSubstring"]  = "截取文本",
            ["FileRead"]         = "读取文件",
            ["ArrayParse"]       = "数组取值",
            ["ArrayBuilder"]     = "数组构建",
            ["ArraySearch"]      = "数组搜索",
            ["TypeConvert"]      = "类型转换",
            ["CustomScript"]     = "自定义脚本",
            ["LlmTranslate"]     = "AI翻译",
            ["LlmVision"]        = "AI视觉",
            ["LlmFileTranslate"] = "AI文件翻译",
            ["PauseTask"]        = "等待",
            ["EndTask"]          = "结束流程",
            ["EndAllFlows"]      = "结束所有流程",
            ["GetTimestamp"]     = "获取时间",
            ["RestartFlow"]      = "重启流程",
            ["BreakLoop"]        = "跳出循环",
            ["IfElseBlock"]      = "条件分支",
            ["ForLoopBlock"]     = "循环",
            ["IfStart"]          = "条件开始",
            ["ElseStart"]        = "Else开始",
            ["ForLoopStart"]     = "循环开始",
            ["CallSubFlow"]      = "调用子流程",
            ["SubFlowInput"]     = "子流程入口",
            ["SubFlowOutput"]    = "子流程返回",
        };

        /// <summary>获取 TaskType 的中文显示名称</summary>
        private static string GetDisplayName(string taskType) =>
            _typeNames.TryGetValue(taskType, out var n) ? n : taskType;

        /// <summary>截断过长文本，用于方案列表展示</summary>
        private static string TruncateText(string? text, int maxLen = 60)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var r = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", "");
            return r.Length > maxLen ? r[..(maxLen - 3)] + "…" : r;
        }

        /// <summary>
        /// 将步骤列表渲染为紧凑的 Markdown 列表，支持嵌套控制流
        /// </summary>
        private void AppendStepsList(StringBuilder sb, List<AiFlowPlanStep> steps, string indent = "")
        {
            foreach (var step in steps)
                AppendStepItem(sb, step, indent);
        }

        /// <summary>递归追加单个步骤项（含嵌套 If/Else/Loop）</summary>
        private void AppendStepItem(StringBuilder sb, AiFlowPlanStep step, string indent)
        {
            var typeName = GetDisplayName(step.TaskType);
            var name = step.Name ?? "";

            // 主行：编号 + 类型 + 名称
            sb.Append($"{indent}{step.Step}. **{typeName}** — {name}");

            // 如有说明，紧跟在后面（截断避免过长）
            if (!string.IsNullOrWhiteSpace(step.Description))
            {
                var desc = TruncateText(step.Description, 80);
                sb.Append($"：{desc}");
            }
            sb.AppendLine();

            // 关键属性用缩进子项展示（最多 4 个）
            if (step.Properties.Count > 0)
            {
                var filtered = step.Properties
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                    .Take(4);
                foreach (var kv in filtered)
                {
                    var val = TruncateText(kv.Value, 50);
                    sb.AppendLine($"{indent}   - `{kv.Key}` = `{val}`");
                }
            }

            // 嵌套分支
            if (step.IfBody != null && step.IfBody.Count > 0)
            {
                sb.AppendLine($"{indent}   **If 分支：**");
                AppendStepsList(sb, step.IfBody, indent + "   ");
            }
            if (step.ElseBody != null && step.ElseBody.Count > 0)
            {
                sb.AppendLine($"{indent}   **Else 分支：**");
                AppendStepsList(sb, step.ElseBody, indent + "   ");
            }
            if (step.LoopBody != null && step.LoopBody.Count > 0)
            {
                sb.AppendLine($"{indent}   **循环体：**");
                AppendStepsList(sb, step.LoopBody, indent + "   ");
            }
        }

        /// <summary>
        /// 将 AI 方案格式化为 Markdown，按目标流程分组展示卡片
        /// </summary>
        public string FormatPlanAsText(AiFlowPlanResponse plan)
        {
            var sb = new StringBuilder();

            // 方案摘要：不再重复渲染 Summary，因为 AI 的流式回复（即 Summary 内容）
            // 已经在 WebView2 中显示过了。重复渲染会导致用户看到两段一模一样的文字。

            // 计算本轮新建的子流程名集合
            var newFlowSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (plan.CreateFlows != null)
                foreach (var f in plan.CreateFlows)
                    if (!string.IsNullOrWhiteSpace(f.Name))
                        newFlowSet.Add(f.Name.StartsWith("SUB_", StringComparison.OrdinalIgnoreCase)
                            ? f.Name : "SUB_" + f.Name);

            // ===== 删除流程 =====
            if (plan.DeleteFlows?.Count > 0)
            {
                sb.AppendLine("### 🗑️ 将删除的流程");
                foreach (var f in plan.DeleteFlows) sb.AppendLine($"- {f}");
                sb.AppendLine();
            }

            // ===== 目标流程卡片（targetFlow 分组）=====
            if (!string.IsNullOrWhiteSpace(plan.TargetFlow) && plan.Plan.Count > 0)
            {
                var rawTarget = plan.TargetFlow!;

                // 检查 targetFlow 是否匹配画布上已有的主流程名称
                var existingTab = _mainViewModel.Tabs.FirstOrDefault(t =>
                    t.Name.Equals(rawTarget, StringComparison.OrdinalIgnoreCase));

                string targetName;
                string badge;
                string icon;

                if (existingTab != null && existingTab.Type != FlowType.SubFlow)
                {
                    // 目标是已有的主流程 → 原名显示，不加 SUB_ 前缀
                    targetName = existingTab.Name;
                    badge = "*(当前主流程)*";
                    icon = "🔷";
                }
                else if (newFlowSet.Any(fn => fn.Equals(rawTarget, StringComparison.OrdinalIgnoreCase)
                    || fn.Equals("SUB_" + rawTarget, StringComparison.OrdinalIgnoreCase)))
                {
                    // 目标是本轮新建的子流程
                    targetName = rawTarget.StartsWith("SUB_", StringComparison.OrdinalIgnoreCase)
                        ? rawTarget : "SUB_" + rawTarget;
                    badge = "*(新建子流程)*";
                    icon = "📁";
                }
                else
                {
                    // 目标是已有的子流程
                    targetName = rawTarget.StartsWith("SUB_", StringComparison.OrdinalIgnoreCase)
                        ? rawTarget : "SUB_" + rawTarget;
                    badge = "*(已有子流程)*";
                    icon = "📁";
                }

                sb.AppendLine($"### {icon} {targetName} {badge}");
                sb.AppendLine();
                AppendStepsList(sb, plan.Plan);
                sb.AppendLine();
                // 其他新建但无卡片的子流程
                foreach (var fn in newFlowSet)
                    if (!fn.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine($"### 📁 {fn} *(新建空子流程)*\n");
            }
            else
            {
                // 无 targetFlow：新建的空子流程（仅列出名称）
                foreach (var fn in newFlowSet)
                {
                    sb.AppendLine($"### 📁 {fn} *(新建子流程)*");
                    sb.AppendLine("*（空流程）*\n");
                }
                // 当前流程的卡片
                if (plan.Plan.Count > 0)
                {
                    var currentTab = _mainViewModel.SelectedTab;
                    var tabName = currentTab?.Name ?? "当前流程";
                    var tabIcon = (currentTab?.Type == FlowType.SubFlow) ? "📁" : "🔷";
                    sb.AppendLine($"### {tabIcon} {tabName}");
                    sb.AppendLine();
                    AppendStepsList(sb, plan.Plan);
                    sb.AppendLine();
                }
            }

            // ===== 其他操作（变量、修改卡片、删除卡片等）=====
            var otherSb = new StringBuilder();
            if (plan.Variables?.Count > 0)
            {
                otherSb.AppendLine("**新建变量：**");
                foreach (var v in plan.Variables)
                    otherSb.AppendLine($"- `@{v.Name}` ({v.Type}) = `{v.Value}` — {v.Description}");
                otherSb.AppendLine();
            }
            if (plan.DeleteVariables?.Count > 0)
            {
                otherSb.AppendLine("**删除变量：** " + string.Join("、", plan.DeleteVariables.Select(v => $"`@{v}`")));
                otherSb.AppendLine();
            }
            if (plan.ModifyVariables?.Count > 0)
            {
                otherSb.AppendLine("**修改变量：**");
                foreach (var v in plan.ModifyVariables)
                    otherSb.AppendLine($"- `@{v.Name}` → `{v.Value}`");
                otherSb.AppendLine();
            }
            if (plan.ModifyCards?.Count > 0)
            {
                otherSb.AppendLine("**修改卡片属性：**");
                foreach (var mod in plan.ModifyCards)
                {
                    var card = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == mod.Order);
                    var cName = card?.Name ?? $"卡片#{mod.Order}";
                    foreach (var kv in mod.Properties)
                        otherSb.AppendLine($"- `#{mod.Order} {cName}` → {kv.Key} = `{kv.Value}`");
                }
                otherSb.AppendLine();
            }
            if (plan.DeleteCards?.Count > 0)
            {
                otherSb.AppendLine("**删除卡片：** " + string.Join("、", plan.DeleteCards.Select(o =>
                {
                    var c = _mainViewModel.TaskCards.FirstOrDefault(x => x.Order == o);
                    return $"`#{o} {c?.Name ?? "未知"}`";
                })));
                otherSb.AppendLine();
            }
            if (plan.InsertCards?.Count > 0)
            {
                otherSb.AppendLine("**插入到现有分支：**");
                foreach (var ins in plan.InsertCards)
                {
                    var bl = ins.Branch?.ToLower() switch { "else" => "Else分支", "loop" => "循环体", _ => "If分支" };
                    var tgt = _mainViewModel.TaskCards.FirstOrDefault(c => c.Order == ins.TargetBlockOrder);
                    var tgtN = tgt?.Name ?? $"Block#{ins.TargetBlockOrder}";
                    otherSb.AppendLine($"- `#{ins.TargetBlockOrder} {tgtN}` 的 {bl}：" +
                        string.Join("、", ins.Cards.Select(c => $"`{GetDisplayName(c.TaskType)} {c.Name}`")));
                }
                otherSb.AppendLine();
            }
            if (otherSb.Length > 0)
            {
                sb.AppendLine("### 🔧 其他操作");
                sb.AppendLine();
                sb.Append(otherSb);
            }

            // ===== 确认按钮提示 =====
            bool isAutoMode = plan.RunCards != null && plan.RunCards.Count > 0;
            bool isFlowOp   = (plan.CreateFlows != null && plan.CreateFlows.Count > 0)
                           || (plan.DeleteFlows  != null && plan.DeleteFlows.Count  > 0)
                           || !string.IsNullOrWhiteSpace(plan.SwitchFlow);
            bool isExecMode = isAutoMode || isFlowOp || plan.Plan.Count > 0;
            var confirmText = isExecMode ? "确认执行」" : "确认创建」";
            sb.AppendLine($"\n---\n✅ 确认无误后点击「{confirmText}，或点击「重新生成」。");

            return sb.ToString();
        }


        /// <summary>
        /// 递归格式化步骤列表（保留兼容，使用中文类型名）
        /// </summary>
        public void FormatSteps(StringBuilder sb, List<AiFlowPlanStep> steps, string indent)
        {
            foreach (var step in steps)
            {
                sb.AppendLine($"{indent}{step.Step}. [{GetDisplayName(step.TaskType)}] {step.Name}");
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
        /// 从消息列表中提取最近的 User + Assistant 对话历史。
        /// 现代高阶模型上下文充足，不再执行按字符数的摘要或截断，完整保留最近 30 条消息的全文语境。
        /// </summary>
        public List<(string Role, string Content)> BuildConversationHistory(ObservableCollection<AiChatMessage> messages)
        {
            var history = new List<(string Role, string Content)>();

            var relevantMessages = messages
                .Where(m => m.Role != AiChatRole.System)
                .ToList();

            // 最多取最近 30 条（完整对白）
            var recent = relevantMessages.Skip(Math.Max(0, relevantMessages.Count - 30)).ToList();

            foreach (var msg in recent)
            {
                var role = msg.Role == AiChatRole.User ? "user" : "assistant";
                // 优先使用 HistoryContent（截断版），避免超长内容导致 AI 复读
                var content = msg.HistoryContent ?? msg.Content;
                history.Add((role, content));
            }

            // 合并连续相同角色的消息（某些模型不允许连续出现同角色消息，如 Claude）
            var merged = new List<(string Role, string Content)>();
            foreach (var item in history)
            {
                if (merged.Count > 0 && merged[^1].Role == item.Role)
                {
                    var last = merged[^1];
                    merged[^1] = (last.Role, last.Content + "\n" + item.Content);
                }
                else
                {
                    merged.Add(item);
                }
            }

            return merged;
        }

        /// <summary>
        /// 序列化流程摘要（仅流程列表 + 变量，不含卡片详情）
        /// AI 通过 analyzeFlow 按需请求某个流程的完整卡片结构
        /// </summary>
        public string SerializeCurrentFlow()
        {
            var variables = _mainViewModel.VariableStore.Variables;
            bool hasVars = variables != null && variables.Count > 0;

            var sb = new StringBuilder();

            // 序列化流程列表（名称、类型、卡片数、ID）
            var tabs = _mainViewModel.Tabs;
            sb.AppendLine($"当前共有 {tabs.Count} 个流程：");
            foreach (var tab in tabs)
            {
                var marker = tab == _mainViewModel.SelectedTab ? " ⬅ 当前" : "";
                var typeTag = tab.Type == FlowType.SubFlow ? "🔶子流程" : "🔷主流程";
                var cardCount = tab == _mainViewModel.SelectedTab
                    ? (_mainViewModel.TaskCards?.Count ?? 0)
                    : tab.TaskCards.Count;
                // 附加流程 ID，供 CallSubFlow 卡片的 targetSubFlowId 属性使用
                sb.AppendLine($"  • [{typeTag}] {tab.Name}{marker} - {cardCount} 个卡片 [ID: {tab.Id}]");
            }
            sb.AppendLine();

            // 序列化变量
            if (hasVars)
            {
                sb.AppendLine($"当前变量管理器中已有 {variables!.Count} 个变量：");
                foreach (var v in variables)
                    sb.AppendLine($"  @{v.Name} ({v.Type}) = {v.Value}");
                sb.AppendLine();
            }

            sb.AppendLine("如需查看某个流程的详细卡片结构，请在 analyzeFlow 字段中指定流程名称。");
            return sb.ToString();
        }

        /// <summary>
        /// 序列化指定流程的完整卡片结构（紧凑单行格式，供 analyze_flow 调用）
        /// 每张卡片压缩为 1 行：#序号 [类型] 名称 | 关键属性
        /// </summary>
        public string? SerializeFlowDetail(string flowName, int startOrder = 0, int maxCount = 2000)
        {
            var tabs = _mainViewModel.Tabs;
            var targetTab = tabs.FirstOrDefault(t => t.Name == flowName);
            if (targetTab == null) return null;

            var cards = targetTab == _mainViewModel.SelectedTab
                ? _mainViewModel.TaskCards
                : targetTab.TaskCards;

            if (cards == null || cards.Count == 0)
                return $"流程「{flowName}」为空，没有任何卡片。";

            // 分页过滤（仅超大流程兜底）
            var filteredCards = cards.AsEnumerable();
            if (startOrder > 0)
                filteredCards = filteredCards.Where(c => c.Order >= startOrder);
            var pagedCards = filteredCards.Take(maxCount).ToList();
            int totalCount = cards.Count;
            bool isTruncated = pagedCards.Count < filteredCards.Count();

            var sb = new StringBuilder();
            sb.AppendLine($"流程「{flowName}」共 {totalCount} 个卡片：");

            // 需要跳过的属性（元数据/UI 相关，不需要展示给 AI）
            var excludeProps = new HashSet<string> { "Id", "Name", "Order", "Status", "ErrorMessage",
                "IndentLevel", "BranchRole", "BranchGroupId", "IsCollapsed", "IsHiddenByCollapse",
                "TaskType", "OutputsImage", "OutputsText", "OutputsCoordinates", "OutputsBoolResult",
                "CanBeReferenced" };

            foreach (var card in pagedCards)
            {
                var indent = new string(' ', card.IndentLevel * 2);
                var typeName = card.GetType().Name.Replace("TaskCard", "");

                // 收集关键属性（紧凑格式）
                var props = new List<string>();

                if (card is IfElseBranchTaskCard ifCard)
                {
                    typeName = ifCard.BranchRole switch
                    {
                        BranchRole.IfStart => "If",
                        BranchRole.ElseStart => "Else",
                        BranchRole.ElseEnd => "EndIf",
                        _ => ifCard.BranchRole.ToString()
                    };
                    if (ifCard.BranchRole == BranchRole.IfStart && !string.IsNullOrEmpty(ifCard.ConditionExpression))
                        props.Add($"条件:{ifCard.ConditionExpression}");
                }
                else if (card is ForLoopTaskCard loopCard)
                {
                    typeName = loopCard.BranchRole == BranchRole.ForLoopStart ? "ForLoop" : "EndLoop";
                    if (loopCard.BranchRole == BranchRole.ForLoopStart)
                        props.Add($"次数:{loopCard.LoopCount}");
                }
                else
                {
                    // 通过反射收集非空字符串属性
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
                            {
                                // ScriptCode 特殊处理：只显示行数摘要，避免撑大多轮对话的请求体
                                if (prop.Name.Equals("ScriptCode", StringComparison.OrdinalIgnoreCase) ||
                                    prop.Name.Equals("scriptCode", StringComparison.OrdinalIgnoreCase))
                                {
                                    var lineCount = val.Split('\n').Length;
                                    var preview = val.Length > 60 ? val[..60].Replace("\n", " ").Replace("\r", "") + "…" : val.Replace("\n", " ").Replace("\r", "");
                                    props.Add($"{prop.Name}:[{lineCount}行] {preview}");
                                }
                                else
                                {
                                    // 普通属性：截断过长的值（如路径），防止单行过长
                                    if (val.Length > 120) val = val[..117] + "...";
                                    props.Add($"{prop.Name}:{val}");
                                }
                            }
                        }
                        catch { /* 忽略反射异常 */ }
                    }
                }

                // 组装单行：#序号 [类型] 名称 | 属性1 | 属性2
                var propsStr = props.Count > 0 ? " | " + string.Join(" | ", props) : "";
                sb.AppendLine($"{indent}#{card.Order} [{typeName}] {card.Name}{propsStr}");
            }

            // 超大流程截断提示
            if (isTruncated)
            {
                var lastOrder = pagedCards.Last().Order;
                sb.AppendLine($"⚠️ 已截断（显示到 #{lastOrder}），设置 start_order={lastOrder + 1} 查看后续。");
            }

            return sb.ToString();
        }
    }
}
