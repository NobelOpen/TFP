using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

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

        public WinClickTaskCard()
        {
            Name = "Win模拟点击";
        }

        public override bool OutputsCoordinates => true;
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
    }
}
