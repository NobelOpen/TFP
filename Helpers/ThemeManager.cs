using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskFlow.Helpers
{
    public static class ThemeManager
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private const int DWMWA_CAPTION_COLOR = 35;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static bool IsWindows10OrGreater(int build = -1)
        {
            return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= build;
        }

        public static bool CurrentIsDark { get; private set; } = false;

        public static void ApplyTheme(string themeName)
        {
            if (themeName != "Light" && themeName != "Dark")
            {
                themeName = "Light";
            }

            CurrentIsDark = themeName == "Dark";

            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var themeDict = dictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Themes/"));

            Uri newThemeUri = new Uri($"pack://application:,,,/Themes/{themeName}.xaml");

            if (themeDict != null)
            {
                themeDict.Source = newThemeUri;
            }
            else
            {
                dictionaries.Add(new ResourceDictionary { Source = newThemeUri });
            }

            foreach (Window window in Application.Current.Windows)
            {
                SetTitleBarDarkMode(window, CurrentIsDark);
            }
        }

        public static void SetTitleBarDarkMode(Window window, bool isDark)
        {
            if (!IsWindows10OrGreater(17763)) return;

            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                // Immersion Mode
                int trueValue = isDark ? 1 : 0;
                int attribute = IsWindows10OrGreater(18985) ? DWMWA_USE_IMMERSIVE_DARK_MODE : DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
                DwmSetWindowAttribute(hwnd, attribute, ref trueValue, Marshal.SizeOf(typeof(int)));

                // Caption Color (matches AppBackgroundBrush: Light #f0eee6, Dark #0d0d0c)
                // BGR format: 0x00BBGGRR
                int captionColor = isDark ? 0x000C0D0D : 0x00E6EEF0;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, Marshal.SizeOf(typeof(int)));
            }
            catch
            {
                // Ignore runtime DWM errors
            }
        }
    }
}
