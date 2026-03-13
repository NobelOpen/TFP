using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TaskFlow.Views.Windows
{
    /// <summary>
    /// 字幕叠层窗口 - 无边框透明、鼠标穿透、支持毛玻璃/纯色/透明背景
    /// </summary>
    public partial class SubtitleOverlayWindow : Window
    {
        #region Win32 API

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        #endregion

        public SubtitleOverlayWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 设置鼠标穿透 + 工具窗口（不显示在 Alt+Tab 中）
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        }

        /// <summary>
        /// 更新字幕文本和样式
        /// </summary>
        public void UpdateSubtitle(string text, int fontSize, string textColorHex,
            Models.TaskCards.SubtitleBackground background, string bgColorHex)
        {
            SubtitleText.Text = text;
            SubtitleText.FontSize = fontSize > 0 ? fontSize : 20;

            try
            {
                SubtitleText.Foreground = new SolidColorBrush(
                    (Color)System.Windows.Media.ColorConverter.ConvertFromString(textColorHex));
            }
            catch
            {
                SubtitleText.Foreground = Brushes.White;
            }

            // 设置背景样式
            ApplyBackground(background, bgColorHex);
        }

        /// <summary>
        /// 应用背景样式
        /// 注意：WPF 的 AllowsTransparency=True 与系统级 Acrylic API 不兼容，
        /// 因此"毛玻璃"模式使用半透明背景色模拟效果（遮挡原文 + 透出背景信息）
        /// </summary>
        private void ApplyBackground(Models.TaskCards.SubtitleBackground background, string bgColorHex)
        {
            switch (background)
            {
                case Models.TaskCards.SubtitleBackground.Acrylic:
                    // 毛玻璃效果：使用半透明背景色模拟
                    // 默认使用高透明度的深色背景，有效遮挡原文同时透出背景
                    try
                    {
                        var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(bgColorHex);
                        BackgroundBorder.Background = new SolidColorBrush(color);
                    }
                    catch
                    {
                        // 默认：70% 不透明的黑色（0xB3 = 179/255 ≈ 70%）
                        BackgroundBorder.Background = new SolidColorBrush(Color.FromArgb(179, 0, 0, 0));
                    }
                    break;

                case Models.TaskCards.SubtitleBackground.SolidColor:
                    try
                    {
                        BackgroundBorder.Background = new SolidColorBrush(
                            (Color)System.Windows.Media.ColorConverter.ConvertFromString(bgColorHex));
                    }
                    catch
                    {
                        BackgroundBorder.Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
                    }
                    break;

                case Models.TaskCards.SubtitleBackground.Transparent:
                    BackgroundBorder.Background = Brushes.Transparent;
                    break;
            }
        }
    }
}
