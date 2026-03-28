using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        /// <summary>AI 请求截取当前屏幕（自主模式，按需使用）</summary>
        public bool NeedsScreenshot { get; set; }

        /// <summary>截图目标进程名（如 msedge、notepad），为空时截全屏</summary>
        public string? ScreenshotTarget { get; set; }

        /// <summary>AI 请求查看指定流程的详细卡片结构（按需加载）</summary>
        public string? AnalyzeFlow { get; set; }

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

        /// <summary>
        /// plan 步骤的目标流程名（可选）。
        /// 指定后，plan 中的卡片将被直接创建在该流程中，无需先 switchFlow。
        /// 留空表示创建在当前活动流程。
        /// </summary>
        public string? TargetFlow { get; set; }

        /// <summary>切换 UI 显示到的目标流程名（可选，纯 UI 操作，不影响卡片创建目标）</summary>
        public string? SwitchFlow { get; set; }

        // ===== PowerShell 后台能力（自主模式） =====

        /// <summary>需要执行的 PowerShell 命令列表（可选，仅自主模式）</summary>
        public List<AiShellCommand>? ShellCommands { get; set; }
    }

    /// <summary>
    /// AI 请求执行的 PowerShell 命令
    /// </summary>
    public class AiShellCommand
    {
        /// <summary>要执行的 PowerShell 命令</summary>
        public string Command { get; set; } = "";

        /// <summary>命令用途说明</summary>
        public string Description { get; set; } = "";

        /// <summary>超时时间（秒），默认 10，最大 30</summary>
        public int Timeout { get; set; } = 10;
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
    /// <summary>
    /// 兼容 AI 模型返回字符串或对象两种格式：
    /// 字符串格式: ["流程名"] → 自动转为 [{"Name":"流程名"}]
    /// 对象格式:   [{"name":"流程名"}] → 正常解析
    /// </summary>
    [JsonConverter(typeof(AiFlowNewTabConverter))]
    public class AiFlowNewTab
    {
        /// <summary>流程名称</summary>
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 自定义反序列化器：支持字符串和对象两种 createFlows 格式
    /// </summary>
    internal class AiFlowNewTabConverter : JsonConverter<AiFlowNewTab>
    {
        public override AiFlowNewTab ReadJson(JsonReader reader, Type objectType, AiFlowNewTab? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (token.Type == JTokenType.String)
            {
                // AI 直接返回字符串，如 "自动登录"
                return new AiFlowNewTab { Name = token.Value<string>() ?? "" };
            }
            else if (token.Type == JTokenType.Object)
            {
                // 正常对象格式 {"name": "自动登录"}
                var name = token["name"]?.Value<string>()
                        ?? token["Name"]?.Value<string>()
                        ?? "";
                return new AiFlowNewTab { Name = name };
            }
            return new AiFlowNewTab();
        }

        public override void WriteJson(JsonWriter writer, AiFlowNewTab? value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("name");
            writer.WriteValue(value?.Name ?? "");
            writer.WriteEndObject();
        }
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

        /// <summary>AI 思考/推理过程文本（可折叠显示）</summary>
        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private string? _thinkingContent;

        /// <summary>
        /// 该消息是否已通过 StreamingDelta 事件流式渲染到 WebView2。
        /// 若为 true，CollectionChanged 处理时应跳过 addMessage，避免重复显示。
        /// </summary>
        public bool IsStreamedToWebView { get; set; }
    }
}
