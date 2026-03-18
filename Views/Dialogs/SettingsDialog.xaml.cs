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
            TxtLogSection.Text = Strings.UI_LogOutput;
            TxtMaxLogLinesLabel.Text = Strings.UI_MaxLogLines + ":";
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
            BtnCancel.Content = Strings.UI_Cancel;
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

            // Orchid 设置
            ChkSingleStage.IsChecked = _settings.OrchidSingleStage;

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
                MessageBox.Show(string.Format(Strings.Dlg_AutoDetectSuccessMsg, ocrExePath, ocrDirPath),
                    Strings.Dlg_AutoDetectSuccess, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string msg = Strings.Dlg_AutoDetectFailed;
                if (ocrExePath != null) msg = string.Format(Strings.Dlg_AutoDetectPartialExe, ocrExePath);
                else if (ocrDirPath != null) msg = string.Format(Strings.Dlg_AutoDetectPartialDir, ocrDirPath);
                MessageBox.Show(msg, Strings.Dlg_AutoDetect, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 查看 Vision 坐标标定结果
        /// </summary>
        private void ViewCalibration_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CalibrationResultsDialog { Owner = this };
            dialog.ShowDialog();
        }

        private async void TestOcr_Click(object sender, RoutedEventArgs e)
        {
            string exePath = TxtOcrExePath.Text.Trim();
            string dirPath = TxtOcrDirPath.Text.Trim();

            if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(dirPath))
            {
                MessageBox.Show(Strings.Dlg_ConfigureOcrFirst, Strings.Dlg_Hint, MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    MessageBox.Show(message, Strings.Dlg_TestSuccess, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _settings.WeChatOcrVerified = false;
                    TxtOcrStatus.Text = Strings.Dlg_OcrStatusUnavailable;
                    TxtOcrStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(200, 80, 80));
                    MessageBox.Show(message, Strings.Dlg_TestFailed, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _settings.WeChatOcrVerified = false;
                TxtOcrStatus.Text = Strings.Dlg_OcrStatusError;
                TxtOcrStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(200, 80, 80));
                MessageBox.Show(string.Format(Strings.Dlg_TestError, ex.Message), Strings.Dlg_Error, MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(Strings.Dlg_IntervalMustBeNonNeg, Strings.Dlg_FormatError, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(TxtMaxLogLines.Text.Trim(), out int maxLines) || maxLines < 10)
            {
                MessageBox.Show(Strings.Dlg_LogLinesMustBe10, Strings.Dlg_FormatError, MessageBoxButton.OK, MessageBoxImage.Warning);
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

            if (!int.TryParse(TxtRepeatInterval.Text.Trim(), out int repeatInterval) || repeatInterval < 0)
            {
                MessageBox.Show(Strings.Dlg_RepeatIntervalMustBeNonNeg, Strings.Dlg_FormatError, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.RepeatIntervalMs = repeatInterval;

            // 语言设置
            string oldLang = _settings.Language;
            if (CmbLanguage.SelectedItem is System.Windows.Controls.ComboBoxItem langItem && langItem.Tag is string langTag)
                _settings.Language = langTag;
            bool languageChanged = _settings.Language != oldLang;

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

            // 应用开机自启动
            ApplyAutoStart(_settings.AutoStartWithOS);

            DialogResult = true;

            // 语言变更时提示重启（使用自定义弹窗，与整体风格一致）
            if (languageChanged)
            {
                ShowLanguageChangedDialog();
            }

            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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
        /// 显示语言变更提示弹窗（自定义样式，与整体设计风格一致）
        /// </summary>
        private void ShowLanguageChangedDialog()
        {
            var dialog = new Window
            {
                Title = Strings.UI_Language,
                Width = 380,
                Height = 190,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(250, 249, 245)), // #faf9f5
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontBody")
            };

            // 主容器（带圆角边框和阴影）
            var outerBorder = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(250, 249, 245)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(232, 230, 220)), // #e8e6dc
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromArgb(40, 0, 0, 0),
                    BlurRadius = 16,
                    ShadowDepth = 4,
                    Direction = 270
                }
            };

            var mainStack = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(24, 20, 24, 20)
            };

            // 标题行（带图标）
            var titlePanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };
            titlePanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "🌐",
                FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            titlePanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = Strings.UI_Language,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(217, 119, 87)), // #d97757
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontTitle")
            });
            mainStack.Children.Add(titlePanel);

            // 提示文本
            mainStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = Strings.Dlg_LangChangedMsg,
                FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(20, 20, 19)), // #141413
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            // 确认按钮
            var okButton = new System.Windows.Controls.Button
            {
                Content = Strings.UI_Confirm,
                Width = 90,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(217, 119, 87)), // #d97757
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // 按钮模板（圆角 + hover 效果）
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

            // Hover 触发器
            var hoverTrigger = new Trigger
            {
                Property = System.Windows.Controls.Control.IsMouseOverProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty,
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(224, 136, 104)), // #e08868
                "Bd"));
            btnTemplate.Triggers.Add(hoverTrigger);
            okButton.Template = btnTemplate;

            okButton.Click += (s, args) => dialog.Close();

            mainStack.Children.Add(okButton);
            outerBorder.Child = mainStack;
            dialog.Content = outerBorder;

            // 支持拖动
            dialog.MouseLeftButtonDown += (s, args) => { try { dialog.DragMove(); } catch { } };

            dialog.ShowDialog();
        }
    }
}
