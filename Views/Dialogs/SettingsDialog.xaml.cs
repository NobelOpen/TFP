using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using TaskFlow.Models;
using TaskFlow.Resources;
using TaskFlow.Services;

namespace TaskFlow.Views.Dialogs
{
    /// <summary>
    /// 设置窗口
    /// </summary>
    public partial class SettingsDialog : Window
    {
        private readonly AppSettings _settings;

        public SettingsDialog(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            ApplyLocalization();
            LoadSettings();
        }

        private void ApplyLocalization()
        {
            Title = Strings.UI_Settings;
            TxtTitle.Text = Strings.UI_SettingsManagement;
            TxtStartupSection.Text = Strings.UI_Startup;
            ChkAutoStart.Content = Strings.UI_AutoStart;
            ChkAutoLoadLast.Content = Strings.UI_AutoLoadLast;
            ChkRunAllOnStartup.Content = Strings.UI_RunAllOnStartup;
            ChkHideOnStartup.Content = Strings.UI_HideOnStartup;
            TxtExecSection.Text = Strings.UI_Execution;
            TxtFlowIntervalLabel.Text = Strings.UI_FlowInterval + ":";
            ChkRepeatRunAll.Content = Strings.UI_RepeatExecution;
            TxtRepeatIntervalLabel.Text = Strings.UI_RepeatInterval + ":";
            ChkKeepScreenOn.Content = Strings.Settings_KeepScreenOn;
            TxtLogSection.Text = Strings.UI_LogOutput;
            TxtMaxLogLinesLabel.Text = Strings.UI_MaxLogLines + ":";
            ChkAutoSaveLog.Content = Strings.Settings_AutoSaveLog;
            TxtOrchidSection.Text = Strings.Settings_OrchidSection;
            ChkSingleStage.Content = Strings.Settings_SingleStage;
            TxtRouterModelLabel.Text = Strings.Settings_RouterModel + ":";
            TxtOcrSection.Text = Strings.UI_WeChatOcr;
            TxtOcrExeLabel.Text = Strings.UI_OcrExePath + ":";
            TxtOcrDirLabel.Text = Strings.UI_OcrDirPath + ":";
            BtnAutoDetect.Content = Strings.UI_AutoDetect;
            BtnTestOcr.Content = Strings.UI_TestOcr;
            TxtLangSection.Text = Strings.UI_Language;
            TxtLangLabel.Text = Strings.UI_LanguageLabel + ":";
            TxtLangHint.Text = "* " + Strings.UI_LangRestartHint;
            TxtAboutSection.Text = "ℹ " + Strings.UI_About;
            BtnSave.Content = Strings.UI_Save;
            
            // Theme Localization
            if (CmbTheme.Items.Count >= 2)
            {
                bool isZh = System.Threading.Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh");
                if (CmbTheme.Items[0] is ComboBoxItem itemLight)
                    itemLight.Content = isZh ? "浅色 (Light)" : "Light Theme";
                if (CmbTheme.Items[1] is ComboBoxItem itemDark)
                    itemDark.Content = isZh ? "深色 (Dark)" : "Dark Theme";
            }
        }

        private void LoadSettings()
        {
            ChkAutoStart.IsChecked = _settings.AutoStartWithOS;
            ChkAutoLoadLast.IsChecked = _settings.AutoLoadLastProject;
            ChkRunAllOnStartup.IsChecked = _settings.RunAllOnStartup;
            ChkHideOnStartup.IsChecked = _settings.HideOnStartup;
            TxtFlowInterval.Text = _settings.FlowExecutionIntervalMs.ToString();
            TxtMaxLogLines.Text = _settings.MaxLogLines.ToString();
            ChkAutoSaveLog.IsChecked = _settings.AutoSaveLogToFile;
            ChkRepeatRunAll.IsChecked = _settings.RepeatRunAll;
            TxtRepeatInterval.Text = _settings.RepeatIntervalMs.ToString();
            ChkKeepScreenOn.IsChecked = _settings.KeepScreenOn;

            // 语言
            foreach (System.Windows.Controls.ComboBoxItem item in CmbLanguage.Items)
            {
                if (item.Tag is string tag && tag == _settings.Language)
                {
                    CmbLanguage.SelectedItem = item;
                    break;
                }
            }

            // 主题
            foreach (System.Windows.Controls.ComboBoxItem item in CmbTheme.Items)
            {
                if (item.Tag is string tag && tag == _settings.Theme)
                {
                    CmbTheme.SelectedItem = item;
                    break;
                }
            }

            // Orchid 设置
            ChkSingleStage.IsChecked = _settings.OrchidSingleStage;
            
            // 路由模型
            CmbRouterModel.Items.Clear();
            var noneItem = new ComboBoxItem { Content = Strings.Settings_RouterModelNone, Tag = "" };
            CmbRouterModel.Items.Add(noneItem);
            foreach (var model in TaskFlow.Helpers.LlmModelManager.Models)
            {
                CmbRouterModel.Items.Add(new ComboBoxItem { Content = model.DisplayName, Tag = model.Id });
            }
            CmbRouterModel.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(_settings.RouterModelId))
            {
                foreach (ComboBoxItem item in CmbRouterModel.Items)
                {
                    if (item.Tag is string tag && tag == _settings.RouterModelId)
                    {
                        CmbRouterModel.SelectedItem = item;
                        break;
                    }
                }
            }

            // 微信 OCR
            TxtOcrExePath.Text = _settings.WeChatOcrExePath ?? string.Empty;
            TxtOcrDirPath.Text = _settings.WeChatOcrDirPath ?? string.Empty;
            UpdateOcrStatus();

            // 版本号
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            TxtVersion.Text = "TaskFlow Pro v1.2";
        }

        /// <summary>
        /// 更新微信 OCR 状态显示
        /// </summary>
        private void UpdateOcrStatus()
        {
            if (_settings.WeChatOcrVerified)
            {
                TxtOcrStatus.Text = Strings.Dlg_OcrStatusAvailable;
                TxtOcrStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(120, 140, 93));
            }
            else if (!string.IsNullOrEmpty(TxtOcrExePath.Text) && !string.IsNullOrEmpty(TxtOcrDirPath.Text))
            {
                TxtOcrStatus.Text = Strings.Dlg_OcrStatusConfigured;
                TxtOcrStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(217, 119, 87));
            }
            else
            {
                TxtOcrStatus.Text = Strings.Dlg_OcrStatusNotConfigured;
                TxtOcrStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(176, 174, 165));
            }
        }

        private void BrowseOcrExe_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Strings.Dlg_SelectOcrExeTitle,
                Filter = Strings.Dlg_OcrFileFilter
            };
            if (dialog.ShowDialog() == true)
            {
                TxtOcrExePath.Text = dialog.FileName;
                _settings.WeChatOcrVerified = false;
                UpdateOcrStatus();
            }
        }

        private void BrowseOcrDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = Strings.Dlg_SelectWeChatDir,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtOcrDirPath.Text = dialog.SelectedPath;
                _settings.WeChatOcrVerified = false;
                UpdateOcrStatus();
            }
        }

        private void AutoDetect_Click(object sender, RoutedEventArgs e)
        {
            var (ocrExePath, ocrDirPath) = WeChatOcrService.AutoDetectPaths();

            if (ocrExePath != null)
                TxtOcrExePath.Text = ocrExePath;
            if (ocrDirPath != null)
                TxtOcrDirPath.Text = ocrDirPath;

            if (ocrExePath != null && ocrDirPath != null)
            {
                _settings.WeChatOcrVerified = false;
                UpdateOcrStatus();
                ShowStyledMessage(Strings.Dlg_AutoDetectSuccess, 
                    string.Format(Strings.Dlg_AutoDetectSuccessMsg, ocrExePath, ocrDirPath), 
                    "✨", System.Windows.Media.Color.FromRgb(120, 140, 93), System.Windows.Media.Color.FromRgb(140, 158, 115));
            }
            else
            {
                string msg = Strings.Dlg_AutoDetectFailed;
                if (ocrExePath != null) msg = string.Format(Strings.Dlg_AutoDetectPartialExe, ocrExePath);
                else if (ocrDirPath != null) msg = string.Format(Strings.Dlg_AutoDetectPartialDir, ocrDirPath);
                ShowStyledMessage(Strings.Dlg_AutoDetect, msg, 
                    "⚠️", System.Windows.Media.Color.FromRgb(217, 119, 87), System.Windows.Media.Color.FromRgb(224, 136, 104));
            }
        }

        private async void TestOcr_Click(object sender, RoutedEventArgs e)
        {
            string exePath = TxtOcrExePath.Text.Trim();
            string dirPath = TxtOcrDirPath.Text.Trim();

            if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(dirPath))
            {
                ShowStyledMessage(Strings.Dlg_Hint, Strings.Dlg_ConfigureOcrFirst, 
                    "💡", System.Windows.Media.Color.FromRgb(217, 119, 87), System.Windows.Media.Color.FromRgb(224, 136, 104));
                return;
            }

            BtnTestOcr.IsEnabled = false;
            BtnTestOcr.Content = Strings.Dlg_Testing;
            TxtOcrStatus.Text = Strings.Dlg_OcrStatusTesting;
            TxtOcrStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(176, 174, 165));

            try
            {
                var service = new WeChatOcrService(_settings);
                var (success, message) = await service.TestAsync(exePath, dirPath);
                service.Dispose();

                if (success)
                {
                    _settings.WeChatOcrVerified = true;
                    TxtOcrStatus.Text = Strings.Dlg_OcrStatusAvailable;
                    TxtOcrStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(120, 140, 93));
                    ShowStyledMessage(Strings.Dlg_TestSuccess, message, 
                        "✅", System.Windows.Media.Color.FromRgb(120, 140, 93), System.Windows.Media.Color.FromRgb(140, 158, 115));
                }
                else
                {
                    _settings.WeChatOcrVerified = false;
                    TxtOcrStatus.Text = Strings.Dlg_OcrStatusUnavailable;
                    TxtOcrStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(200, 80, 80));
                    ShowStyledMessage(Strings.Dlg_TestFailed, message, 
                        "❌", System.Windows.Media.Color.FromRgb(200, 80, 80), System.Windows.Media.Color.FromRgb(214, 104, 104));
                }
            }
            catch (Exception ex)
            {
                _settings.WeChatOcrVerified = false;
                TxtOcrStatus.Text = Strings.Dlg_OcrStatusError;
                TxtOcrStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(200, 80, 80));
                ShowStyledMessage(Strings.Dlg_Error, string.Format(Strings.Dlg_TestError, ex.Message), 
                    "❌", System.Windows.Media.Color.FromRgb(200, 80, 80), System.Windows.Media.Color.FromRgb(214, 104, 104));
            }
            finally
            {
                BtnTestOcr.IsEnabled = true;
                BtnTestOcr.Content = Strings.Dlg_TestAvailability;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 验证数字输入
            if (!int.TryParse(TxtFlowInterval.Text.Trim(), out int interval) || interval < 0)
            {
                ShowStyledMessage(Strings.Dlg_FormatError, Strings.Dlg_IntervalMustBeNonNeg, 
                    "⚠️", System.Windows.Media.Color.FromRgb(217, 119, 87), System.Windows.Media.Color.FromRgb(224, 136, 104));
                return;
            }
            if (!int.TryParse(TxtMaxLogLines.Text.Trim(), out int maxLines) || maxLines < 10)
            {
                ShowStyledMessage(Strings.Dlg_FormatError, Strings.Dlg_LogLinesMustBe10, 
                    "⚠️", System.Windows.Media.Color.FromRgb(217, 119, 87), System.Windows.Media.Color.FromRgb(224, 136, 104));
                return;
            }

            // 保存设置
            _settings.AutoStartWithOS = ChkAutoStart.IsChecked == true;
            _settings.AutoLoadLastProject = ChkAutoLoadLast.IsChecked == true;
            _settings.RunAllOnStartup = ChkRunAllOnStartup.IsChecked == true;
            _settings.HideOnStartup = ChkHideOnStartup.IsChecked == true;
            _settings.FlowExecutionIntervalMs = interval;
            _settings.MaxLogLines = maxLines;
            _settings.AutoSaveLogToFile = ChkAutoSaveLog.IsChecked == true;
            _settings.RepeatRunAll = ChkRepeatRunAll.IsChecked == true;
            _settings.KeepScreenOn = ChkKeepScreenOn.IsChecked == true;
            _settings.OrchidSingleStage = ChkSingleStage.IsChecked == true;

            if (CmbRouterModel.SelectedItem is ComboBoxItem routerItem && routerItem.Tag is string routerTag)
            {
                _settings.RouterModelId = string.IsNullOrEmpty(routerTag) ? null : routerTag;
            }

            if (!int.TryParse(TxtRepeatInterval.Text.Trim(), out int repeatInterval) || repeatInterval < 0)
            {
                ShowStyledMessage(Strings.Dlg_FormatError, Strings.Dlg_RepeatIntervalMustBeNonNeg, 
                    "⚠️", System.Windows.Media.Color.FromRgb(217, 119, 87), System.Windows.Media.Color.FromRgb(224, 136, 104));
                return;
            }
            _settings.RepeatIntervalMs = repeatInterval;

            // 语言设置
            string oldLang = _settings.Language;
            if (CmbLanguage.SelectedItem is System.Windows.Controls.ComboBoxItem langItem && langItem.Tag is string langTag)
                _settings.Language = langTag;
            bool languageChanged = _settings.Language != oldLang;

            // 主题设置
            string oldTheme = _settings.Theme;
            if (CmbTheme.SelectedItem is System.Windows.Controls.ComboBoxItem themeItem && themeItem.Tag is string themeTag)
                _settings.Theme = themeTag;
            bool themeChanged = _settings.Theme != oldTheme;

            // 保存微信 OCR 路径
            string ocrExe = TxtOcrExePath.Text.Trim();
            string ocrDir = TxtOcrDirPath.Text.Trim();

            // 如果路径有变更，重置验证状态
            if (ocrExe != (_settings.WeChatOcrExePath ?? string.Empty)
                || ocrDir != (_settings.WeChatOcrDirPath ?? string.Empty))
            {
                _settings.WeChatOcrVerified = false;
            }

            _settings.WeChatOcrExePath = string.IsNullOrEmpty(ocrExe) ? null : ocrExe;
            _settings.WeChatOcrDirPath = string.IsNullOrEmpty(ocrDir) ? null : ocrDir;

            _settings.Save();

            if (themeChanged)
            {
                TaskFlow.Helpers.ThemeManager.ApplyTheme(_settings.Theme);
            }

            // 应用开机自启动
            ApplyAutoStart(_settings.AutoStartWithOS);

            // 语言变更时提示重启（必须在 DialogResult = true 之前，否则窗口已关闭无法作为 Owner）
            if (languageChanged)
            {
                ShowStyledMessage(Strings.UI_Language, Strings.Dlg_LangChangedMsg, 
                    "🌐", System.Windows.Media.Color.FromRgb(217, 119, 87), System.Windows.Media.Color.FromRgb(224, 136, 104));
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { }
            e.Handled = true;
        }

        /// <summary>
        /// 设置/取消开机自启动（写入注册表）
        /// </summary>
        private static void ApplyAutoStart(bool enable)
        {
            const string appName = "TaskFlow";
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (enable)
                {
                    // 优先使用入口程序集路径，避免调试模式下获取到 dotnet.exe
                    string? exePath = Environment.ProcessPath;
                    // 如果获取到的是 dotnet.exe，则使用程序集位置
                    if (string.IsNullOrEmpty(exePath) || exePath.EndsWith("dotnet.exe", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
                        if (entryAssembly != null)
                        {
                            var dllPath = entryAssembly.Location;
                            // 将 .dll 路径转换为 .exe 路径
                            exePath = System.IO.Path.ChangeExtension(dllPath, ".exe");
                        }
                    }
                    if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                        key.SetValue(appName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(appName, false);
                }
            }
            catch { }
        }

        /// <summary>
        /// 显示自定义样式的提示框
        /// </summary>
        private void ShowStyledMessage(string title, string message, string icon, System.Windows.Media.Color themeColor, System.Windows.Media.Color hoverColor)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontBody"),
                Topmost = true
            };

            var shadowBorder = new System.Windows.Controls.Border
            {
                Padding = new Thickness(16),
                Background = System.Windows.Media.Brushes.Transparent
            };

            var outerBorder = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromArgb(40, 0, 0, 0),
                    BlurRadius = 16,
                    ShadowDepth = 4,
                    Direction = 270
                }
            };
            outerBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AppBackgroundBrush");
            outerBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "BorderLightBrush");

            var mainStack = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(24, 20, 24, 20)
            };

            var titlePanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };
            titlePanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = icon,
                FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            titlePanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(themeColor),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontTitle")
            });
            mainStack.Children.Add(titlePanel);

            var messageText = new System.Windows.Controls.TextBlock
            {
                Text = message,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            };
            messageText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextPrimaryBrush");
            mainStack.Children.Add(messageText);

            var okButton = new System.Windows.Controls.Button
            {
                Content = Strings.UI_Confirm,
                Width = 90,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new System.Windows.Media.SolidColorBrush(themeColor),
                Foreground = (System.Windows.Media.Brush)FindResource("CreamyWhiteBrush"),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var btnTemplate = new ControlTemplate(typeof(System.Windows.Controls.Button));
            var btnBorder = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            btnBorder.Name = "Bd";
            btnBorder.SetValue(System.Windows.Controls.Border.BackgroundProperty,
                new TemplateBindingExtension(Control.BackgroundProperty));
            btnBorder.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
            var btnContent = new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            btnContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            btnContent.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            btnBorder.AppendChild(btnContent);
            btnTemplate.VisualTree = btnBorder;

            var hoverTrigger = new Trigger
            {
                Property = System.Windows.Controls.Control.IsMouseOverProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty,
                new System.Windows.Media.SolidColorBrush(hoverColor), "Bd"));
            btnTemplate.Triggers.Add(hoverTrigger);
            okButton.Template = btnTemplate;

            okButton.Click += (s, args) => dialog.Close();

            mainStack.Children.Add(okButton);
            outerBorder.Child = mainStack;
            shadowBorder.Child = outerBorder;
            dialog.Content = shadowBorder;

            dialog.MouseLeftButtonDown += (s, args) => { try { dialog.DragMove(); } catch { } };

            dialog.ShowDialog();
        }
    }
}
