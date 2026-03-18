namespace TaskFlow.Models.AiFlow
{
    /// <summary>
    /// AI 助手工作模式
    /// </summary>
    public enum AiAssistantMode
    {
        /// <summary>设计模式：侧重蓝图生成，支持建议和强制命令</summary>
        Design = 0,

        /// <summary>自主模式：主动运行卡片、分析结果、多轮循环</summary>
        Autonomous = 1
    }
}
