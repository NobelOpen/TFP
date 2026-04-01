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
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings = TaskFlow.Models.AppSettings.Load();
            
            // Apply Theme before window creation
            TaskFlow.Helpers.ThemeManager.ApplyTheme(settings.Theme);

            var langCode = string.IsNullOrEmpty(settings.Language) ? "en" : settings.Language;
            var culture = new System.Globalization.CultureInfo(langCode);
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;



            EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                TaskFlow.Helpers.ThemeManager.SetTitleBarDarkMode(window, TaskFlow.Helpers.ThemeManager.CurrentIsDark);
            }
        }
    }
}
