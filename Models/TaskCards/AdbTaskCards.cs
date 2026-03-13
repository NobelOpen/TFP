using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskFlow.Models.TaskCards
{
    /// <summary>
    /// ADB连接设备任务卡片
    /// </summary>
    public partial class AdbConnectTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.AdbConnect;

        [ObservableProperty]
        private string _deviceIp = "127.0.0.1";

        [ObservableProperty]
        private int _devicePort = 5555;

        public AdbConnectTaskCard()
        {
            Name = "ADB连接设备";
        }
    }

    /// <summary>
    /// ADB启动应用任务卡片
    /// </summary>
    public partial class AdbLaunchAppTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.AdbLaunchApp;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

        [ObservableProperty]
        private string _packageName = string.Empty;

        [ObservableProperty]
        private string _activityName = string.Empty;

        public AdbLaunchAppTaskCard()
        {
            Name = "ADB启动应用";
        }
    }

    /// <summary>
    /// ADB截屏任务卡片
    /// </summary>
    public partial class AdbScreenshotTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.AdbScreenshot;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

        /// <summary>
        /// 截屏后转换为灰度图像
        /// </summary>
        [ObservableProperty]
        private bool _convertToGrayscale;

        public AdbScreenshotTaskCard()
        {
            Name = "ADB截屏工具";
        }

        public override bool OutputsImage => true;
    }

    /// <summary>
    /// ADB模拟点击任务卡片
    /// </summary>
    public partial class AdbClickTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.AdbClick;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

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
        private int _clickIntervalMs = 100;

        [ObservableProperty]
        private int _swipeDurationMs = 300;

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

        public AdbClickTaskCard()
        {
            Name = "ADB模拟点击";
        }

        public override bool OutputsCoordinates => true;
    }

    /// <summary>
    /// ADB关闭应用任务卡片
    /// </summary>
    public partial class AdbCloseAppTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.AdbCloseApp;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

        [ObservableProperty]
        private string _packageName = string.Empty;

        public AdbCloseAppTaskCard()
        {
            Name = "ADB关闭应用";
        }
    }

    /// <summary>
    /// ADB断开设备任务卡片
    /// </summary>
    public partial class AdbDisconnectTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.AdbDisconnect;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

        public AdbDisconnectTaskCard()
        {
            Name = "ADB断开设备";
        }
    }
}
