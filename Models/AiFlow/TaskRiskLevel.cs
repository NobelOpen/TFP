using TaskFlow.Models.TaskCards;

namespace TaskFlow.Models.AiFlow
{
    /// <summary>
    /// 任务卡片操作风险等级
    /// </summary>
    public enum TaskRiskLevel
    {
        /// <summary>低风险：只读/查询操作，自主模式自动执行</summary>
        Low = 0,

        /// <summary>中风险：启动/交互操作，需暂停等待用户批准</summary>
        Medium = 1,

        /// <summary>高风险：数据变更操作，强制批准 + 警告</summary>
        High = 2
    }

    /// <summary>
    /// 根据 TaskType 分类风险等级
    /// </summary>
    public static class TaskRiskClassifier
    {
        /// <summary>
        /// 获取指定 TaskType 的风险等级
        /// </summary>
        public static TaskRiskLevel GetRiskLevel(TaskType type) => type switch
        {
            // === 🟢 低风险：只读/查询/分析 ===
            TaskType.WinFindFile => TaskRiskLevel.Low,
            TaskType.WinScreenshot => TaskRiskLevel.Low,
            TaskType.ImgCrop => TaskRiskLevel.Low,
            TaskType.ImgTemplateMatch => TaskRiskLevel.Low,
            TaskType.ImgOcr => TaskRiskLevel.Low,
            TaskType.ImgColorDetect => TaskRiskLevel.Low,
            TaskType.ImgColorSegment => TaskRiskLevel.Low,
            TaskType.ImgPreprocess => TaskRiskLevel.Low,
            TaskType.ImgBlobAnalysis => TaskRiskLevel.Low,
            TaskType.ImgResize => TaskRiskLevel.Low,
            TaskType.ExpressionEval => TaskRiskLevel.Low,
            TaskType.StringSubstring => TaskRiskLevel.Low,
            TaskType.TypeConvert => TaskRiskLevel.Low,
            TaskType.ArrayParse => TaskRiskLevel.Low,
            TaskType.ArrayBuilder => TaskRiskLevel.Low,
            TaskType.ArraySearch => TaskRiskLevel.Low,
            TaskType.FileRead => TaskRiskLevel.Low,
            TaskType.GetTimestamp => TaskRiskLevel.Low,
            TaskType.PauseTask => TaskRiskLevel.Low,
            // CustomScript 可执行任意 C# 代码，必须归为高风险
            TaskType.CustomScript => TaskRiskLevel.High,
            TaskType.AdbScreenshot => TaskRiskLevel.Low,

            // === 🟡 中风险：启动/关闭/交互操作 ===
            TaskType.WinLaunchApp => TaskRiskLevel.Medium,
            TaskType.WinClick => TaskRiskLevel.Medium,
            TaskType.WinCloseApp => TaskRiskLevel.Medium,
            TaskType.WinUiAutomation => TaskRiskLevel.Medium,
            TaskType.WinSimulateInput => TaskRiskLevel.Medium,
            TaskType.WinSubtitle => TaskRiskLevel.Medium,
            TaskType.AdbConnect => TaskRiskLevel.Medium,
            TaskType.AdbLaunchApp => TaskRiskLevel.Medium,
            TaskType.AdbClick => TaskRiskLevel.Medium,
            TaskType.AdbCloseApp => TaskRiskLevel.Medium,
            TaskType.AdbDisconnect => TaskRiskLevel.Medium,
            TaskType.ClipboardWatch => TaskRiskLevel.Low,

            TaskType.EventListener => TaskRiskLevel.Medium,
            TaskType.InputCombo => TaskRiskLevel.Medium,
            TaskType.WinTextInput => TaskRiskLevel.Medium,

            // === 🔴 高风险：AI 模型调用（消耗资源/费用） ===
            TaskType.LlmTranslate => TaskRiskLevel.High,
            TaskType.LlmVision => TaskRiskLevel.High,
            TaskType.LlmFileTranslate => TaskRiskLevel.High,

            // === 控制流：低风险（逻辑结构，不直接操作） ===
            TaskType.IfStart => TaskRiskLevel.Low,
            TaskType.IfEnd => TaskRiskLevel.Low,
            TaskType.ElifStart => TaskRiskLevel.Low,
            TaskType.ElseStart => TaskRiskLevel.Low,
            TaskType.ElseEnd => TaskRiskLevel.Low,
            TaskType.ForLoopStart => TaskRiskLevel.Low,
            TaskType.ForLoopEnd => TaskRiskLevel.Low,
            TaskType.EndTask => TaskRiskLevel.Low,
            TaskType.EndAllFlows => TaskRiskLevel.Low,
            TaskType.RestartFlow => TaskRiskLevel.Low,
            TaskType.BreakLoop => TaskRiskLevel.Low,
            // 子流程控制卡片：结构性操作，低风险
            TaskType.SubFlowInput => TaskRiskLevel.Low,
            TaskType.SubFlowOutput => TaskRiskLevel.Low,
            TaskType.CallSubFlow => TaskRiskLevel.Low,

            // 默认中风险
            _ => TaskRiskLevel.Medium
        };

        /// <summary>
        /// 获取风险等级的显示标记
        /// </summary>
        public static string GetRiskIcon(TaskRiskLevel level) => level switch
        {
            TaskRiskLevel.Low => "🟢",
            TaskRiskLevel.Medium => "🟡",
            TaskRiskLevel.High => "🔴",
            _ => "⚪"
        };

        /// <summary>
        /// 获取风险等级的中文描述
        /// </summary>
        public static string GetRiskDescription(TaskRiskLevel level) => level switch
        {
            TaskRiskLevel.Low => "低风险（自动执行）",
            TaskRiskLevel.Medium => "中风险（需批准）",
            TaskRiskLevel.High => "高风险（需批准）",
            _ => "未知"
        };
    }
}
