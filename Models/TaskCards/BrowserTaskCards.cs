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

        public override bool OutputsText => true;

        public BrowserGetTextTaskCard()
        {
            Name = TaskCardBase.GetTaskTypeName(TaskType);
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

        public override bool OutputsText => true;

        public BrowserExecuteJsTaskCard()
        {
            Name = TaskCardBase.GetTaskTypeName(TaskType);
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

            // 兼容 prompt 中的 scriptCode（AI 常用）和内部属性名 script
            if (props.TryGetValue("scriptCode", out var scriptCode) && !string.IsNullOrEmpty(scriptCode))
                Script = scriptCode;
            else if (props.TryGetValue("script", out var script) && !string.IsNullOrEmpty(script))
                Script = script;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "Script", Hint = "要执行的 JavaScript 代码" });

            if (props.TryGetValue("debuggingPort", out var dp) && int.TryParse(dp, out int dpVal))
                CdpPort = dpVal;
            else if (props.TryGetValue("cdpPort", out var port) && int.TryParse(port, out int p))
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
            Name = TaskCardBase.GetTaskTypeName(TaskType);
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

    // ============================================================
    //  浏览器原生点击
    // ============================================================

    public partial class BrowserNativeClickTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.BrowserNativeClick;

        [ObservableProperty]
        private BrowserSelectorType _selectorType = BrowserSelectorType.Css;

        [ObservableProperty]
        private string _selector = string.Empty;
        
        [ObservableProperty]
        private int _x = 0;

        [ObservableProperty]
        private int _y = 0;

        [ObservableProperty]
        private ClickType _clickType = ClickType.Single;

        [ObservableProperty]
        private string _endSelector = string.Empty;

        [ObservableProperty]
        private int _endX = 0;

        [ObservableProperty]
        private int _endY = 0;

        [ObservableProperty]
        private int _multiClickCount = 2;

        [ObservableProperty]
        private int _clickIntervalMs = 100;

        [ObservableProperty]
        private int _cdpPort = 9222;

        public BrowserNativeClickTaskCard()
        {
            Name = TaskCardBase.GetTaskTypeName(TaskType);
        }

        public override List<AiFlowReportItem> FillFromAiPlan(AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("selector", out var sel) && !string.IsNullOrEmpty(sel))
                Selector = sel;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "Selector", Hint = "CSS 选择器或 XPath" });

            if (props.TryGetValue("cdpPort", out var port1) && int.TryParse(port1, out int parsedPort1))
                CdpPort = parsedPort1;

            return missing;
        }
    }

    // ============================================================
    //  浏览器原生输入
    // ============================================================

    public partial class BrowserNativeInputTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.BrowserNativeInput;

        [ObservableProperty]
        private BrowserSelectorType _selectorType = BrowserSelectorType.Css;

        [ObservableProperty]
        private string _selector = string.Empty;

        [ObservableProperty]
        private string _inputText = string.Empty;

        [ObservableProperty]
        private TextInputMode _inputMode = TextInputMode.CharByChar;
        
        [ObservableProperty]
        private int _charIntervalMs = 10;

        [ObservableProperty]
        private int _cdpPort = 9222;

        public BrowserNativeInputTaskCard()
        {
            Name = TaskCardBase.GetTaskTypeName(TaskType);
        }

        public override List<AiFlowReportItem> FillFromAiPlan(AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("selector", out var sel) && !string.IsNullOrEmpty(sel))
                Selector = sel;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "Selector", Hint = "CSS 选择器或 XPath" });

            // 兼容 prompt 中的 inputText（AI 常用）和内部属性名 text
            if (props.TryGetValue("inputText", out var inputText) && !string.IsNullOrEmpty(inputText))
                InputText = inputText;
            else if (props.TryGetValue("text", out var text) && !string.IsNullOrEmpty(text))
                InputText = text;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "InputText", Hint = "要输入的文本" });

            if (props.TryGetValue("debuggingPort", out var dp) && int.TryParse(dp, out int dpVal))
                CdpPort = dpVal;
            else if (props.TryGetValue("cdpPort", out var port2) && int.TryParse(port2, out int parsedPort2))
                CdpPort = parsedPort2;

            return missing;
        }
    }

    // ============================================================
    //  浏览器模拟点击
    // ============================================================

    public partial class BrowserSimulatedClickTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.BrowserSimulatedClick;

        [ObservableProperty]
        private int _x = 0;

        [ObservableProperty]
        private int _y = 0;

        [ObservableProperty]
        private ClickType _clickType = ClickType.Single;

        [ObservableProperty]
        private int _multiClickCount = 2;

        [ObservableProperty]
        private int _clickIntervalMs = 100;

        [ObservableProperty]
        private int _cdpPort = 9222;

        /// <summary>
        /// Set-of-Mark 标注 ID。当大于 0 时，引擎从标注映射表中查询精确 CSS 坐标，
        /// 自动覆盖 X/Y 值，无需手动输入坐标。
        /// </summary>
        [ObservableProperty]
        private int _markId = 0;

        public BrowserSimulatedClickTaskCard()
        {
            Name = TaskCardBase.GetTaskTypeName(TaskType);
        }

        public override List<AiFlowReportItem> FillFromAiPlan(AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            // 优先使用 markId（指定后无需手动 X/Y）
            if (props.TryGetValue("markId", out var markStr) && int.TryParse(markStr, out int markInt) && markInt > 0)
            {
                MarkId = markInt;
                // markId 模式下 X/Y 可选（由引擎自动填充）
                if (props.TryGetValue("x", out var xStr2) && int.TryParse(xStr2, out int xInt2))
                    X = xInt2;
                if (props.TryGetValue("y", out var yStr2) && int.TryParse(yStr2, out int yInt2))
                    Y = yInt2;
            }
            else
            {
                if (props.TryGetValue("x", out var xStr) && int.TryParse(xStr, out int xInt))
                    X = xInt;
                else
                    missing.Add(new AiFlowReportItem { PropertyName = "X", Hint = "X 坐标" });

                if (props.TryGetValue("y", out var yStr) && int.TryParse(yStr, out int yInt))
                    Y = yInt;
                else
                    missing.Add(new AiFlowReportItem { PropertyName = "Y", Hint = "Y 坐标" });
            }

            if (props.TryGetValue("cdpPort", out var port3) && int.TryParse(port3, out int parsedPort3))
                CdpPort = parsedPort3;

            return missing;
        }
    }

    // ============================================================
    //  CDP 指令执行
    // ============================================================

    public partial class BrowserCdpCommandTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.BrowserCdpCommand;

        [ObservableProperty]
        private string _methodName = string.Empty;

        [ObservableProperty]
        private string _jsonArguments = "{}";

        [ObservableProperty]
        private int _cdpPort = 9222;

        [JsonIgnore]
        [ObservableProperty]
        private string? _outputText;

        public override bool OutputsText => true;

        public BrowserCdpCommandTaskCard()
        {
            Name = TaskCardBase.GetTaskTypeName(TaskType);
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

            // 兼容 prompt 中的 commandName（AI 常用）和内部属性名 methodName
            if (props.TryGetValue("commandName", out var cmdName) && !string.IsNullOrEmpty(cmdName))
                MethodName = cmdName;
            else if (props.TryGetValue("methodName", out var method) && !string.IsNullOrEmpty(method))
                MethodName = method;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "MethodName", Hint = "CDP 方法名" });

            // 兼容 prompt 中的 commandParams 和内部属性名 jsonArguments
            if (props.TryGetValue("commandParams", out var cmdParams) && !string.IsNullOrEmpty(cmdParams))
                JsonArguments = cmdParams;
            else if (props.TryGetValue("jsonArguments", out var jsonArgs) && !string.IsNullOrEmpty(jsonArgs))
                JsonArguments = jsonArgs;

            if (props.TryGetValue("debuggingPort", out var dp) && int.TryParse(dp, out int dpVal))
                CdpPort = dpVal;
            else if (props.TryGetValue("cdpPort", out var port4) && int.TryParse(port4, out int parsedPort4))
                CdpPort = parsedPort4;

            return missing;
        }
    }

    // ============================================================
    //  浏览器页面截图
    // ============================================================

    public partial class BrowserScreenshotTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.BrowserScreenshot;

        [ObservableProperty]
        private int _cdpPort = 9222;

        [ObservableProperty]
        private bool _fullPage = true;

        public BrowserScreenshotTaskCard()
        {
            Name = TaskCardBase.GetTaskTypeName(TaskType);
        }

        public override bool OutputsImage => true;

        public override List<AiFlowReportItem> FillFromAiPlan(AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("fullPage", out var fp) && bool.TryParse(fp, out bool parsedFp))
                FullPage = parsedFp;

            if (props.TryGetValue("cdpPort", out var port1) && int.TryParse(port1, out int parsedPort))
                CdpPort = parsedPort;

            return missing;
        }
    }
}

