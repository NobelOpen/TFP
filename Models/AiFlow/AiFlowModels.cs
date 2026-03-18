using System;
using System.Collections.Generic;

namespace TaskFlow.Models.AiFlow
{
    /// <summary>
    /// 卡片能力描述定义（从资源文件加载）
    /// </summary>
    public class CardDescriptionDef
    {
        /// <summary>TaskType 枚举名称，如 "WinLaunchApp"</summary>
        public string TaskType { get; set; } = "";

        /// <summary>所属类别，如 "Windows操作"</summary>
        public string Category { get; set; } = "";

        /// <summary>功能描述</summary>
        public string Description { get; set; } = "";

        /// <summary>适用场景说明（帮助 AI 决定何时选用此卡片）</summary>
        public string Usage { get; set; } = "";

        /// <summary>关键可配置属性列表（属性名: 说明）</summary>
        public List<string> KeyProperties { get; set; } = new();

        /// <summary>输出描述列表（属性名: 说明）</summary>
        public List<string> Outputs { get; set; } = new();
    }

    /// <summary>
    /// LLM 返回的方案中的单个步骤
    /// </summary>
    public class AiFlowPlanStep
    {
        /// <summary>步骤编号（从 1 开始）</summary>
        public int Step { get; set; }

        /// <summary>任务卡片类型枚举名称</summary>
        public string TaskType { get; set; } = "";

        /// <summary>用户可读的步骤名称</summary>
        public string Name { get; set; } = "";

        /// <summary>为什么需要这一步的简要说明</summary>
        public string Description { get; set; } = "";

        /// <summary>AI 预填的属性值（属性名 → 值）</summary>
        public Dictionary<string, string> Properties { get; set; } = new();

        /// <summary>引用的源步骤编号（如 "使用第2步的截图输出"）</summary>
        public int? SourceStep { get; set; }

        /// <summary>模板来源步骤编号（仅 ImgTemplateMatch 使用，引用裁剪步骤的输出作为模板）</summary>
        public int? TemplateSourceStep { get; set; }

        /// <summary>IfElseBlock 的 If 分支子步骤列表</summary>
        public List<AiFlowPlanStep>? IfBody { get; set; }

        /// <summary>IfElseBlock 的 Else 分支子步骤列表（可选）</summary>
        public List<AiFlowPlanStep>? ElseBody { get; set; }

        /// <summary>ForLoopBlock 的循环体子步骤列表</summary>
        public List<AiFlowPlanStep>? LoopBody { get; set; }
    }

    /// <summary>
    /// AI 方案中声明的变量定义
    /// </summary>
    public class AiFlowPlanVariable
    {
        /// <summary>变量名称（不含 @）</summary>
        public string Name { get; set; } = "";

        /// <summary>变量类型：Int / String / Bool / Double</summary>
        public string Type { get; set; } = "Int";

        /// <summary>初始值</summary>
        public string Value { get; set; } = "0";

        /// <summary>用途说明</summary>
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// LLM 返回的完整方案
    /// </summary>
    public class AiFlowPlanResponse
    {
        /// <summary>方案摘要</summary>
        public string Summary { get; set; } = "";

        /// <summary>方案需要的变量列表（可选）</summary>
        public List<AiFlowPlanVariable> Variables { get; set; } = new();

        /// <summary>需要删除的变量名列表（可选）</summary>
        public List<string> DeleteVariables { get; set; } = new();

        /// <summary>需要修改的变量列表（可选，修改名称或值）</summary>
        public List<AiFlowPlanVariable> ModifyVariables { get; set; } = new();

        /// <summary>需要修改的卡片属性列表（可选）</summary>
        public List<AiFlowCardModification> ModifyCards { get; set; } = new();

        /// <summary>需要删除的卡片序号列表（可选）</summary>
        public List<int> DeleteCards { get; set; } = new();

        /// <summary>需要运行的卡片序号列表（可选，自主模式）</summary>
        public List<int> RunCards { get; set; } = new();

        /// <summary>向已有分支/循环中插入卡片（可选）</summary>
        public List<AiFlowInsertCardsRequest>? InsertCards { get; set; }

        /// <summary>AI 标记自主任务已完成</summary>
        public bool Done { get; set; }

        /// <summary>失败回退策略：retry / fallback / abort（自主模式卡片失败时使用）</summary>
        public string? FailureStrategy { get; set; }

        /// <summary>回退备选方案（fallback 策略时，AI 提供替代卡片）</summary>
        public List<AiFlowPlanStep>? FallbackPlan { get; set; }

        /// <summary>步骤列表</summary>
        public List<AiFlowPlanStep> Plan { get; set; } = new();

        // ===== 流程（Tab）级操作 =====

        /// <summary>需要创建的新流程列表（可选）</summary>
        public List<AiFlowNewTab>? CreateFlows { get; set; }

        /// <summary>需要删除的流程名列表（可选）</summary>
        public List<string>? DeleteFlows { get; set; }

        /// <summary>创建完成后切换到的目标流程名（可选）</summary>
        public string? SwitchFlow { get; set; }
    }

    /// <summary>
    /// 向已有分支/循环中插入卡片的请求
    /// </summary>
    public class AiFlowInsertCardsRequest
    {
        /// <summary>目标 block 的起始卡片序号（如 IfStart 的 Order）</summary>
        public int TargetBlockOrder { get; set; }

        /// <summary>插入到哪个分支：if / else / loop</summary>
        public string Branch { get; set; } = "if";

        /// <summary>要插入的卡片列表</summary>
        public List<AiFlowPlanStep> Cards { get; set; } = new();
    }

    /// <summary>
    /// 新建流程的描述
    /// </summary>
    public class AiFlowNewTab
    {
        /// <summary>流程名称</summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// AI 方案中卡片属性修改定义
    /// </summary>
    public class AiFlowCardModification
    {
        /// <summary>目标卡片序号</summary>
        public int Order { get; set; }

        /// <summary>要修改的属性键值对</summary>
        public Dictionary<string, string> Properties { get; set; } = new();
    }

    /// <summary>
    /// 创建完成后的待配置报告项
    /// </summary>
    public class AiFlowReportItem
    {
        /// <summary>关联的任务卡片 ID</summary>
        public Guid TaskCardId { get; set; }

        /// <summary>卡片名称（显示用）</summary>
        public string CardName { get; set; } = "";

        /// <summary>需要用户配置的属性名称</summary>
        public string PropertyName { get; set; } = "";

        /// <summary>属性说明</summary>
        public string Hint { get; set; } = "";
    }

    /// <summary>
    /// AI 面板中的消息类型
    /// </summary>
    public enum AiChatRole
    {
        User,
        Assistant,
        System
    }

    /// <summary>
    /// AI 面板中的单条消息
    /// </summary>
    public partial class AiChatMessage : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private AiChatRole _role;

        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private string _content = "";

        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private DateTime _timestamp = DateTime.Now;

        /// <summary>如果是方案消息，存储解析后的方案</summary>
        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private AiFlowPlanResponse? _plan;

        /// <summary>如果是报告消息，存储报告项列表</summary>
        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private List<AiFlowReportItem>? _reportItems;
    }
}
