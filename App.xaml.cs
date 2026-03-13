using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskFlow
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // DWM API 用于自定义窗口标题栏颜色
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 根据用户设置切换界面语言（必须在窗口创建之前）
            var settings = TaskFlow.Models.AppSettings.Load();
            var langCode = string.IsNullOrEmpty(settings.Language) ? "en" : settings.Language;
            var culture = new System.Globalization.CultureInfo(langCode);
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;

            TaskFlow.Helpers.LlmModelManager.Load();

            // 为所有窗口自动应用标题栏颜色
            EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                ApplyTitleBarColor(window);
            }
        }

        /// <summary>
        /// 应用 Anthropic 浅色标题栏（#faf9f5 暖白色）
        /// </summary>
        private static void ApplyTitleBarColor(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                // 关闭深色模式
                int darkMode = 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                // 设置标题栏颜色为 #faf9f5 (Anthropic 暖白)
                // COLORREF 格式: 0x00BBGGRR
                int captionColor = 0x00F5F9FA; // BGR: F5=B, F9=G, FA=R
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            }
            catch
            {
                // Windows 10 早期版本可能不支持 DWMWA_CAPTION_COLOR，忽略
            }
        }
    }
}
