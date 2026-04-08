using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Toolkit.Uwp.Notifications;

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

            // 注册 Win11 Toast 通知的全局回调（Orchid 无感审批）
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;

            EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        }

        /// <summary>
        /// Win11 Toast 通知交互回调：处理 Orchid 操作审批的批准/拒绝
        /// </summary>
        private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
        {
            var args = ToastArguments.Parse(e.Argument);
            if (!args.TryGetValue("action", out var action))
                return;

            // 切回 UI 线程处理审批结果
            Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = Current.MainWindow;
                var mainVm = mainWindow?.DataContext as ViewModels.MainViewModel;
                var aiFlowVm = mainVm?.AiFlowVm;

                if (aiFlowVm == null || !aiFlowVm.AwaitingApproval)
                    return;

                if (action == "approve")
                {
                    aiFlowVm.ApproveExecution();
                }
                else if (action == "reject")
                {
                    aiFlowVm.AbortExecution();
                }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 清理 MCP 服务
            TaskFlow.Services.McpServerService.Instance.Stop();

            // 清理 Toast 通知注册（避免通知残留在系统通知中心）
            try { ToastNotificationManagerCompat.Uninstall(); } catch { }
            base.OnExit(e);
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
