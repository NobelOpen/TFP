using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using TaskFlow.Models.AiFlow;

namespace TaskFlow.Models.TaskCards
{
    /// <summary>
    /// Windows启动应用任务卡片
    /// </summary>
    public partial class WinLaunchAppTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.WinLaunchApp;

        [ObservableProperty]
        private string _exePath = string.Empty;

        [ObservableProperty]
        private string _arguments = string.Empty;

        public WinLaunchAppTaskCard()
        {
            Name = "Win启动应用";
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("exePath", out var exePath) && !string.IsNullOrEmpty(exePath))
                ExePath = exePath;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "ExePath", Hint = "可执行文件路径" });

            if (props.TryGetValue("arguments", out var args))
                Arguments = args;

            return missing;
        }
    }

    /// <summary>
    /// Windows截屏任务卡片
    /// </summary>
    public partial class WinScreenshotTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.WinScreenshot;

        [ObservableProperty]
        private string _processName = string.Empty;

        /// <summary>
        /// 是否包含标题栏（默认包含）
        /// </summary>
        [ObservableProperty]
        private bool _includeTitleBar = true;

        /// <summary>
        /// 顶部裁剪高度（主要用于去除自定义标题栏）
        /// </summary>
        [ObservableProperty]
        private int _cropTopHeight = 0;

        /// <summary>
        /// 截屏后转换为灰度图像
        /// </summary>
        [ObservableProperty]
        private bool _convertToGrayscale;

        public WinScreenshotTaskCard()
        {
            Name = "Win截屏工具";
        }

        public override bool OutputsImage => true;

        /// <summary>
        /// 输出图像分辨率字符串（如 "1920x1080"）
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private string _outputResolution = string.Empty;

        /// <summary>
        /// 输出宽度分辨率
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private int _outputWidth;

        /// <summary>
        /// 输出高度分辨率
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private int _outputHeight;

        public override void Reset()
        {
            base.Reset();
            OutputResolution = string.Empty;
            OutputWidth = 0;
            OutputHeight = 0;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("processName", out var procName) && !string.IsNullOrEmpty(procName))
                ProcessName = procName;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "ProcessName", Hint = "目标进程名称" });

            return missing;
        }
    }

    /// <summary>
    /// Windows模拟点击任务卡片
    /// </summary>
    public partial class WinClickTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.WinClick;

        [ObservableProperty]
        private int _startX;

        [ObservableProperty]
        private int _startY;

        [ObservableProperty]
        private int _endX;

        [ObservableProperty]
        private int _endY;

        [ObservableProperty]
        private ClickType _clickType = ClickType.Single;

        /// <summary>
        /// 是否启用多次点击（双击模式下）
        /// </summary>
        [ObservableProperty]
        private bool _multiClickEnabled;

        /// <summary>
        /// 多次点击次数（双击模式下）
        /// </summary>
        [ObservableProperty]
        private int _multiClickCount = 2;

        /// <summary>
        /// 点击间隔（毫秒，双击模式下）
        /// </summary>
        [ObservableProperty]
        private int _clickIntervalMs = 50;

        // 可以引用其他任务卡片的坐标输出
        [ObservableProperty]
        private Guid? _sourceTaskIdForCoordinates;

        [ObservableProperty]
        private bool _useSourceTaskCoordinates;

        // 变量引用坐标
        [ObservableProperty]
        private bool _useVariableCoordinates;

        [ObservableProperty]
        private string _startXExpression = string.Empty;

        [ObservableProperty]
        private string _startYExpression = string.Empty;

        /// <summary>
        /// 滑动操作时长（毫秒）
        /// </summary>
        [ObservableProperty]
        private int _swipeDurationMs = 300;

        /// <summary>
        /// 是否启用离屏点击（通过PostMessage定向发送给指定进程）
        /// </summary>
        [ObservableProperty]
        private bool _enableOffScreenClick;

        /// <summary>
        /// 离屏点击的目标进程名
        /// </summary>
        [ObservableProperty]
        private string _processName = string.Empty;

        /// <summary>
        /// 递归网格定位编号（如 "5" 或 "B3"），非空时自动从网格布局中还原绝对坐标
        /// </summary>
        [ObservableProperty]
        private string _gridCell = string.Empty;

        public WinClickTaskCard()
        {
            Name = "Win模拟点击";
        }

        public override bool OutputsCoordinates => true;

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            // 通过表达式引用其他任务的坐标输出
            if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var sourceCard))
            {
                if (sourceCard.OutputsCoordinates)
                {
                    StartXExpression = $"#{sourceCard.Order} {sourceCard.Name}.X";
                    StartYExpression = $"#{sourceCard.Order} {sourceCard.Name}.Y";
                }
            }

            // 设置点击类型
            if (props.TryGetValue("clickType", out var clickTypeStr)
                && Enum.TryParse<ClickType>(clickTypeStr, true, out var clickType))
                ClickType = clickType;

            // 设置网格定位编号（递归网格模式）
            if (props.TryGetValue("gridCell", out var gridCellStr) && !string.IsNullOrWhiteSpace(gridCellStr))
                GridCell = gridCellStr.Trim();

            // 设置静态坐标
            if (props.TryGetValue("startX", out var sxStr) && int.TryParse(sxStr, out var sx))
                StartX = sx;
            if (props.TryGetValue("startY", out var syStr) && int.TryParse(syStr, out var sy))
                StartY = sy;

            // 设置进程名
            if (props.TryGetValue("processName", out var clickProc))
                ProcessName = clickProc;

            return missing;
        }
    }

    /// <summary>
    /// Windows关闭应用任务卡片
    /// </summary>
    public partial class WinCloseAppTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.WinCloseApp;

        [ObservableProperty]
        private string _processName = string.Empty;

        public WinCloseAppTaskCard()
        {
            Name = "Win关闭应用";
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("processName", out var closeProc) && !string.IsNullOrEmpty(closeProc))
                ProcessName = closeProc;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "ProcessName", Hint = "目标进程名称" });

            return missing;
        }
    }

    /// <summary>
    /// WinUI自动化任务卡片 - 根据进程名查找主窗口，再查找并点击指定按钮
    /// </summary>
    public partial class WinUiAutomationTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.WinUiAutomation;

        /// <summary>
        /// 目标进程名称（不含.exe）
        /// </summary>
        [ObservableProperty]
        private string _processName = string.Empty;

        /// <summary>
        /// 要点击的按钮名称
        /// </summary>
        [ObservableProperty]
        private string _buttonName = string.Empty;

        /// <summary>
        /// 元素查找依据（名称 / AutomationId）
        /// </summary>
        [ObservableProperty]
        private UiSearchBy _searchBy = UiSearchBy.Name;

        /// <summary>
        /// 名称匹配方式（精确 / 包含 / 正则）
        /// </summary>
        [ObservableProperty]
        private UiMatchMode _matchMode = UiMatchMode.Exact;

        /// <summary>
        /// 按 AutomationId 查找时使用的 ID 值
        /// </summary>
        [ObservableProperty]
        private string _automationId = string.Empty;

        public WinUiAutomationTaskCard()
        {
            Name = "WinUI自动化";
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("processName", out var uiProc) && !string.IsNullOrEmpty(uiProc))
                ProcessName = uiProc;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "ProcessName", Hint = "目标进程名称" });

            if (props.TryGetValue("buttonName", out var btnName) && !string.IsNullOrEmpty(btnName))
                ButtonName = btnName;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "ButtonName", Hint = "按钮名称" });

            return missing;
        }
    }

    /// <summary>
    /// Win字幕提示任务卡片 - 在指定进程窗口上显示毛玻璃/纯色/透明字幕叠层
    /// </summary>
    public partial class WinSubtitleTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.WinSubtitle;

        /// <summary>
        /// 是否指定窗口（不勾选时使用屏幕坐标）
        /// </summary>
        [ObservableProperty]
        private bool _useSpecifiedWindow;

        /// <summary>
        /// 目标进程名称（不含.exe）
        /// </summary>
        [ObservableProperty]
        private string _processName = string.Empty;

        /// <summary>
        /// 显示文本（支持 @变量 和 #任务引用）
        /// </summary>
        [ObservableProperty]
        private string _displayText = string.Empty;

        /// <summary>
        /// 相对目标窗口左上角的 X 偏移量
        /// </summary>
        [ObservableProperty]
        private int _offsetX;

        /// <summary>
        /// 相对目标窗口左上角的 Y 偏移量
        /// </summary>
        [ObservableProperty]
        private int _offsetY;

        /// <summary>
        /// 字幕区域宽度（0=自动适应文本宽度）
        /// </summary>
        [ObservableProperty]
        private int _subtitleWidth;

        /// <summary>
        /// 字幕区域高度（0=自动适应文本高度）
        /// </summary>
        [ObservableProperty]
        private int _subtitleHeight;

        /// <summary>
        /// 字体大小
        /// </summary>
        [ObservableProperty]
        private int _fontSize = 20;

        /// <summary>
        /// 字体颜色（十六进制，如 #000000）
        /// </summary>
        [ObservableProperty]
        private string _textColor = "#000000";

        /// <summary>
        /// 背景样式
        /// </summary>
        [ObservableProperty]
        private SubtitleBackground _background = SubtitleBackground.SolidColor;

        /// <summary>
        /// 纯色模式下的背景色（含透明度，如 #FFFFFFFF）
        /// </summary>
        [ObservableProperty]
        private string _backgroundColor = "#FFFFFFFF";

        /// <summary>
        /// 显示时长（毫秒，0=常驻直到下一次更新或隐藏）
        /// </summary>
        [ObservableProperty]
        private int _durationMs;

        /// <summary>
        /// 是否等待字幕关闭后再继续执行下一个任务
        /// </summary>
        [ObservableProperty]
        private bool _waitUntilClosed;

        /// <summary>
        /// 自动吸色模式的采样掩膜路径（白=采样，黑=排除）
        /// </summary>
        [ObservableProperty]
        private string _sampleMaskPath = string.Empty;

        public WinSubtitleTaskCard()
        {
            Name = "Win字幕提示";
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("processName", out var subProc))
                ProcessName = subProc;
            if (props.TryGetValue("displayText", out var displayText))
                DisplayText = displayText;

            return missing;
        }
    }

    /// <summary>
    /// 修饰键类型
    /// </summary>
    public enum ModifierKeyType
    {
        None,
        Ctrl,
        Shift,
        Alt,
        CtrlShift,
        CtrlAlt
    }

    /// <summary>
    /// 模拟输入动作类型
    /// </summary>
    public enum InputActionType
    {
        ScrollUp,
        ScrollDown,
        KeyPress
    }

    /// <summary>
    /// 模拟输入任务卡片：修饰键 + 动作（鼠标滚轮/键盘按键）
    /// </summary>
    public partial class WinSimulateInputTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.WinSimulateInput;

        /// <summary>
        /// 修饰键
        /// </summary>
        [ObservableProperty]
        private ModifierKeyType _modifierKey = ModifierKeyType.None;

        /// <summary>
        /// 输入动作类型
        /// </summary>
        [ObservableProperty]
        private InputActionType _actionType = InputActionType.ScrollDown;

        /// <summary>
        /// 键盘按键（VirtualKey 码，仅 KeyPress 时使用）
        /// </summary>
        [ObservableProperty]
        private string _keyName = string.Empty;

        /// <summary>
        /// 滚轮滚动量（正数）
        /// </summary>
        [ObservableProperty]
        private int _scrollAmount = 120;

        /// <summary>
        /// 重复次数
        /// </summary>
        [ObservableProperty]
        private int _repeatCount = 1;

        /// <summary>
        /// 每次重复之间的间隔（毫秒）
        /// </summary>
        [ObservableProperty]
        private int _intervalMs = 50;

        public override bool OutputsText => true;

        public WinSimulateInputTaskCard()
        {
            Name = "Win模拟输入";
        }
    }

    /// <summary>
    /// Win路径查找任务卡片 - 在指定目录或全盘搜索文件，返回第一个匹配的完整路径
    /// </summary>
    public partial class WinFindFileTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.WinFindFile;

        /// <summary>
        /// 要查找的文件名（如 photo.jpg），支持表达式引用
        /// </summary>
        [ObservableProperty]
        private string _fileName = string.Empty;

        /// <summary>
        /// 搜索根目录，留空则搜索所有逻辑驱动器
        /// </summary>
        [ObservableProperty]
        private string _searchRootPath = string.Empty;

        /// <summary>
        /// 最大搜索深度（0=不限制深度）
        /// </summary>
        [ObservableProperty]
        private int _maxDepth = 0;

        /// <summary>
        /// 是否启用通配符匹配（如 *.jpg、setup*.exe）
        /// </summary>
        [ObservableProperty]
        private bool _useWildcard = false;

        /// <summary>
        /// 输出：找到的文件完整路径
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private string _outputFilePath = string.Empty;

        public override bool OutputsBoolResult => true;

        public WinFindFileTaskCard()
        {
            Name = "Win路径查找";
        }

        public override void Reset()
        {
            base.Reset();
            OutputFilePath = string.Empty;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            if (props.TryGetValue("fileName", out var findFileName) && !string.IsNullOrEmpty(findFileName))
                FileName = findFileName;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "FileName", Hint = "要查找的文件名称" });

            if (props.TryGetValue("searchRootPath", out var searchRoot) && !string.IsNullOrEmpty(searchRoot))
                SearchRootPath = searchRoot;

            if (props.TryGetValue("maxDepth", out var maxDepthStr) && int.TryParse(maxDepthStr, out var maxDepth))
                MaxDepth = maxDepth;

            if (props.TryGetValue("useWildcard", out var useWild) && bool.TryParse(useWild, out var wildcard))
                UseWildcard = wildcard;

            return missing;
        }
    }

    /// <summary>
    /// 输入组合动作模式
    /// </summary>
    public enum InputComboMode
    {
        /// <summary>单击（按下后立即释放）</summary>
        Tap,
        /// <summary>长按（按下后不释放，卡片结束时统一释放）</summary>
        Hold
    }

    /// <summary>
    /// 单个输入组合动作
    /// </summary>
    public class InputComboAction
    {
        /// <summary>按键名称（如 W、A、D、Space、LeftClick 等）</summary>
        public string Key { get; set; } = "W";

        /// <summary>动作模式：单击或长按</summary>
        public InputComboMode Mode { get; set; } = InputComboMode.Tap;

        /// <summary>该动作执行后的等待时间（毫秒）</summary>
        public int DelayAfterMs { get; set; } = 100;
    }

    /// <summary>
    /// 输入组合任务卡片 - 支持多按键编排（长按/单击混合），非阻塞后台执行
    /// </summary>
    public partial class InputComboTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.InputCombo;

        /// <summary>
        /// 按键动作列表
        /// </summary>
        public List<InputComboAction> Actions { get; set; } = new();

        /// <summary>
        /// 动作序列重复次数（0=无限循环，配合 StopExpression 使用）
        /// </summary>
        [ObservableProperty]
        private int _repeatCount = 1;

        /// <summary>
        /// 终止条件表达式（每轮循环后求值，为 false 时停止；填 "true" 表示一直运行）
        /// </summary>
        [ObservableProperty]
        private string _stopExpression = "true";

        /// <summary>
        /// 最大执行时长（毫秒，0=不限时）
        /// </summary>
        [ObservableProperty]
        private int _totalDurationMs = 0;

        /// <summary>
        /// 后台任务取消令牌源（运行时使用，不序列化）
        /// </summary>
        [JsonIgnore]
        public CancellationTokenSource? ComboTokenSource { get; set; }

        public override bool OutputsText => true;

        public InputComboTaskCard()
        {
            Name = "Win输入组合";
        }

        public override void Reset()
        {
            base.Reset();
            ComboTokenSource?.Cancel();
            ComboTokenSource = null;
        }
    }

    /// <summary>
    /// 文本输入方式
    /// </summary>
    public enum TextInputMode
    {
        /// <summary>逐字符模拟键盘输入（SendInput + KEYEVENTF_UNICODE）</summary>
        CharByChar,
        /// <summary>通过剪贴板粘贴（Ctrl+V）</summary>
        Clipboard
    }

    /// <summary>
    /// Win文本输入任务卡片：一次性输入一段完整文本
    /// 支持逐字符模拟和剪贴板粘贴两种模式
    /// </summary>
    public partial class WinTextInputTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.WinTextInput;

        /// <summary>
        /// 输入文本（支持 @变量 和 #任务引用 表达式）
        /// </summary>
        [ObservableProperty]
        private string _inputText = "";

        /// <summary>
        /// 输入方式：逐字符模拟 或 剪贴板粘贴
        /// </summary>
        [ObservableProperty]
        private TextInputMode _inputMode = TextInputMode.CharByChar;

        /// <summary>
        /// 逐字符模式下每个字符之间的间隔（毫秒）
        /// </summary>
        [ObservableProperty]
        private int _charIntervalMs = 50;

        public WinTextInputTaskCard()
        {
            Name = "Win文本输入";
        }
    }
}
