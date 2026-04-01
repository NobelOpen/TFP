using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using TaskFlow.Models.AiFlow;

namespace TaskFlow.Models.TaskCards
{
    /// <summary>
    /// 浏览器选择器类型
    /// </summary>
    public enum BrowserSelectorType
    {
        /// <summary>CSS 选择器</summary>
        Css,
        /// <summary>XPath 路径</summary>
        XPath
    }

    /// <summary>
    /// 浏览器等待模式
    /// </summary>
    public enum BrowserWaitMode
    {
        /// <summary>等待元素出现（可见）</summary>
        Visible,
        /// <summary>等待元素消失（隐藏/移除）</summary>
        Hidden
    }

    // ============================================================
    //  浏览器取文本
    // ============================================================

    /// <summary>
    /// 浏览器取文本任务卡片 —— 用 CSS/XPath 提取 DOM 元素的文本或属性值
    /// </summary>
    public partial class BrowserGetTextTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.BrowserGetText;

        /// <summary>选择器类型（CSS / XPath）</summary>
        [ObservableProperty]
        private BrowserSelectorType _selectorType = BrowserSelectorType.Css;

        /// <summary>选择器表达式（支持 @变量 / #引用）</summary>
        [ObservableProperty]
        private string _selector = string.Empty;

        /// <summary>
        /// 要提取的属性名称。留空时取 innerText；
        /// 填写时取对应属性，如 href、src、value 等。
        /// </summary>
        [ObservableProperty]
        private string _attributeName = string.Empty;

        /// <summary>连接端口，默认 9222</summary>
        [ObservableProperty]
        private int _cdpPort = 9222;

        // ===== 输出 =====

        [JsonIgnore]
        [ObservableProperty]
        private string? _outputText;

        public override bool OutputsText => true;

        public BrowserGetTextTaskCard()
        {
            Name = "浏览器取文本";
        }

        public override void Reset()
        {
            base.Reset();
            OutputText = null;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("selector", out var sel) && !string.IsNullOrEmpty(sel))
                Selector = sel;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "Selector", Hint = "CSS 选择器或 XPath" });

            if (props.TryGetValue("attributeName", out var attr))
                AttributeName = attr;

            if (props.TryGetValue("cdpPort", out var port) && int.TryParse(port, out int p))
                CdpPort = p;

            return missing;
        }
    }

    // ============================================================
    //  浏览器执行脚本
    // ============================================================

    /// <summary>
    /// 浏览器执行脚本任务卡片 —— 在当前页面执行任意 JavaScript，返回结果字符串
    /// </summary>
    public partial class BrowserExecuteJsTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.BrowserExecuteJs;

        /// <summary>要执行的 JavaScript 代码（return 的值作为输出）</summary>
        [ObservableProperty]
        private string _script = string.Empty;

        /// <summary>连接端口，默认 9222</summary>
        [ObservableProperty]
        private int _cdpPort = 9222;

        // ===== 输出 =====

        [JsonIgnore]
        [ObservableProperty]
        private string? _outputText;

        public override bool OutputsText => true;

        public BrowserExecuteJsTaskCard()
        {
            Name = "浏览器执行脚本";
        }

        public override void Reset()
        {
            base.Reset();
            OutputText = null;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("script", out var script) && !string.IsNullOrEmpty(script))
                Script = script;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "Script", Hint = "要执行的 JavaScript 代码" });

            if (props.TryGetValue("cdpPort", out var port) && int.TryParse(port, out int p))
                CdpPort = p;

            return missing;
        }
    }

    // ============================================================
    //  浏览器等待元素
    // ============================================================

    /// <summary>
    /// 浏览器等待元素任务卡片 —— 等待选择器对应元素出现或消失
    /// </summary>
    public partial class BrowserWaitForElementTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.BrowserWaitForElement;

        /// <summary>选择器类型（CSS / XPath）</summary>
        [ObservableProperty]
        private BrowserSelectorType _selectorType = BrowserSelectorType.Css;

        /// <summary>选择器表达式</summary>
        [ObservableProperty]
        private string _selector = string.Empty;

        /// <summary>等待模式（出现/消失）</summary>
        [ObservableProperty]
        private BrowserWaitMode _waitMode = BrowserWaitMode.Visible;

        /// <summary>超时时间（毫秒）默认 10000ms</summary>
        [ObservableProperty]
        private int _timeoutMs = 10000;

        /// <summary>连接端口，默认 9222</summary>
        [ObservableProperty]
        private int _cdpPort = 9222;

        // ===== 输出 =====

        [JsonIgnore]
        [ObservableProperty]
        private bool? _outputResult;

        public override bool OutputsBoolResult => true;

        public BrowserWaitForElementTaskCard()
        {
            Name = "浏览器等待元素";
        }

        public override void Reset()
        {
            base.Reset();
            OutputResult = null;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("selector", out var sel) && !string.IsNullOrEmpty(sel))
                Selector = sel;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "Selector", Hint = "CSS 选择器或 XPath" });

            if (props.TryGetValue("waitMode", out var mode))
                WaitMode = mode.Equals("hidden", System.StringComparison.OrdinalIgnoreCase)
                    ? BrowserWaitMode.Hidden : BrowserWaitMode.Visible;

            if (props.TryGetValue("timeoutMs", out var timeout) && int.TryParse(timeout, out int t))
                TimeoutMs = t;

            if (props.TryGetValue("cdpPort", out var port) && int.TryParse(port, out int p))
                CdpPort = p;

            return missing;
        }
    }
}
