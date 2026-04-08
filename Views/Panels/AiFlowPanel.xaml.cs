using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Models.AiFlow;
using TaskFlow.ViewModels;

// 消除 UserControl 命名空间歧义
using UserControl = System.Windows.Controls.UserControl;

namespace TaskFlow.Views.Panels
{
    /// <summary>
    /// AI 流程助手面板 (code-behind)
    /// </summary>
    public partial class AiFlowPanel : UserControl
    {
        private AiFlowViewModel? _vm;

        /// <summary>
        /// 请求关闭 Orchid 面板的事件，由 MainWindow 订阅
        /// </summary>
        public event Action? ClosePanelRequested;

        // WebView2 是否已就绪
        private bool _webViewReady;
        // 缓存待发消息（WebView2 尚未就绪时）
        private readonly System.Collections.Generic.List<string> _pendingMessages = new();

        public AiFlowPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 初始化 ViewModel 绑定
        /// </summary>
        public void Initialize(AiFlowViewModel viewModel)
        {
            _vm = viewModel;
            DataContext = _vm;

            // 绑定报告列表
            ReportList.ItemsSource = _vm.ReportItems;

            // 监听方案变化，控制按钮区显示
            _vm.PropertyChanged += ViewModel_PropertyChanged;

            // WebView2 流式事件桥接
            _vm.StreamingStarted += () => PostWebMessage(new { action = "startStreaming" });
            _vm.StreamingDelta += text => PostWebMessage(new { action = "appendDelta", text });
            _vm.StreamingThinking += text => PostWebMessage(new { action = "appendThinking", text });
            _vm.StreamingEnded += () => PostWebMessage(new { action = "endStreaming" });
            _vm.MessagesUpdated += () =>
            {
                var msgs = _vm.Messages.Select(m => new
                {
                    role = m.Role.ToString().ToLower(),
                    content = m.Content ?? "",
                    thinking = m.ThinkingContent
                }).ToArray();
                PostWebMessage(new { action = "clearMessages" });
                PostWebMessage(new { action = "loadHistory", messages = msgs });
            };

            // 消息集合变化时同步非流式消息（系统消息、用户消息等）到 WebView2
            _vm.Messages.CollectionChanged += (s, e) =>
            {
                if (_isSwitchingSession) return;
                
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    foreach (AiChatMessage msg in e.NewItems)
                    {
                        // 跳过已通过 StreamingDelta 流式渲染的消息，避免重复显示
                        if (msg.IsStreamedToWebView) continue;

                        PostWebMessage(new
                        {
                            action = "addMessage",
                            role = msg.Role.ToString().ToLower(),
                            content = msg.Content ?? "",
                            thinking = msg.ThinkingContent
                        });
                    }
                }
                else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    PostWebMessage(new { action = "clearMessages" });
                }
            };

            // 初始化 WebView2
            InitWebView2Async();

            // 初始化模型列表
            RefreshModelList();

            // 订阅模型变化事件以动态更新下拉框
            LlmModelManager.ModelsChanged -= OnModelsChanged;
            LlmModelManager.ModelsChanged += OnModelsChanged;

            // 订阅主程序主题变化事件
            TaskFlow.Helpers.ThemeManager.ThemeChanged -= OnAppThemeChanged;
            TaskFlow.Helpers.ThemeManager.ThemeChanged += OnAppThemeChanged;

            // 从设置恢复模式选择
            RestoreModeFromSettings();
        }

        private void OnAppThemeChanged(string themeName)
        {
            PostWebMessage(new { action = "setTheme", theme = themeName });
        }

        /// <summary>
        /// 初始化 WebView2 控件
        /// </summary>
        private async void InitWebView2Async()
        {
            try
            {
                AiFlowLogger.Info("[WebView2] 开始初始化...");
                
                // 指定 WebView2 数据目录（避免默认使用可执行目录导致的 UnauthorizedAccessException）
                string userDataFolder = Path.Combine(Path.GetTempPath(), "TaskFlow_WebView2");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder);
                
                await ChatWebView.EnsureCoreWebView2Async(env);
                AiFlowLogger.Info("[WebView2] CoreWebView2 就绪");

                // 将 Assets 文件夹映射为虚拟主机，解决本地资源加载问题
                var assetsPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Assets");
                AiFlowLogger.Info($"[WebView2] Assets 路径: {assetsPath}, 存在: {Directory.Exists(assetsPath)}");

                ChatWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "orchid.local", assetsPath,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

                // 监听导航完成
                ChatWebView.CoreWebView2.NavigationCompleted += (s, args) =>
                {
                    AiFlowLogger.Info($"[WebView2] 导航完成, 成功: {args.IsSuccess}, 状态码: {args.HttpStatusCode}");
                };

                // 拦截外部链接点击，阻止在组件内跳转，改用系统默认浏览器打开
                ChatWebView.CoreWebView2.NavigationStarting += (s, e) =>
                {
                    if (e.Uri != null && e.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !e.Uri.Contains("orchid.local"))
                    {
                        e.Cancel = true;
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = e.Uri,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            AiFlowLogger.Error($"[WebView2] 打开外部链接失败: {ex.Message}");
                        }
                    }
                };

                // 关闭原生的浏览器右键菜单，使其更具原生应用感
                ChatWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                ChatWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

                // 监听 JS 回调
                ChatWebView.CoreWebView2.WebMessageReceived += (s, e) =>
                {
                    try
                    {
                        AiFlowLogger.Info($"[WebView2] 收到 JS 消息: {e.WebMessageAsJson}");
                        var msg = JsonConvert.DeserializeObject<dynamic>(e.WebMessageAsJson);
                        string? type = msg?.type;
                        if (type == "ready")
                        {
                            _webViewReady = true;
                            ChatWebView.Visibility = Visibility.Visible;
                            WebViewLoading.Visibility = Visibility.Collapsed;
                            InputAreaBorder.Visibility = Visibility.Visible;
                            AiFlowLogger.Info("[WebView2] 页面就绪，已播放出场动画");

                            // 执行优雅出场动画
                            var sb = new System.Windows.Media.Animation.Storyboard();
                            
                            // 输入框淡入与上浮
                            var easeOut = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                            var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { EasingFunction = easeOut };
                            System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnim, InputAreaBorder);
                            System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));

                            var translateAnim = new System.Windows.Media.Animation.DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(400)) { EasingFunction = easeOut };
                            System.Windows.Media.Animation.Storyboard.SetTarget(translateAnim, InputAreaBorder);
                            System.Windows.Media.Animation.Storyboard.SetTargetProperty(translateAnim, new PropertyPath("RenderTransform.Y"));

                            sb.Children.Add(opacityAnim);
                            sb.Children.Add(translateAnim);
                            sb.Begin();

                            // 发送初始主题和本地化文本
                            PostWebMessage(new { action = "setTheme", theme = TaskFlow.Helpers.ThemeManager.CurrentIsDark ? "Dark" : "Light" });
                            PostWebMessage(new { action = "setLocalization", disclaimer = TaskFlow.Resources.Strings.Main_OrchidDisclaimer });

                            // 发送缓存的消息
                            foreach (var pending in _pendingMessages)
                                ChatWebView.CoreWebView2.PostWebMessageAsJson(pending);
                            _pendingMessages.Clear();

                            // 加载已有的历史消息
                            if (_vm != null && _vm.Messages.Count > 0)
                            {
                                var msgs = _vm.Messages.Select(m => new
                                {
                                    role = m.Role.ToString().ToLower(),
                                    content = m.Content ?? "",
                                    thinking = m.ThinkingContent
                                }).ToArray();
                                
                                PostWebMessage(new { action = "loadHistory", messages = msgs });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AiFlowLogger.Error("[WebView2] 消息处理异常", ex);
                    }
                };

                // 导航到虚拟主机页面
                AiFlowLogger.Info("[WebView2] 开始导航到 orchid_chat.html");
                ChatWebView.Source = new Uri("https://orchid.local/orchid_chat.html");
            }
            catch (Exception ex)
            {
                AiFlowLogger.Error("WebView2 初始化失败", ex);
            }
        }

        /// <summary>
        /// 向 WebView2 发送 JSON 消息
        /// </summary>
        private void PostWebMessage(object msg)
        {
            var json = JsonConvert.SerializeObject(msg);
            // 必须在 UI 线程访问 ChatWebView 属性，否则在后台流式线程中会抛出 InvalidOperationException
            Dispatcher.BeginInvoke(() =>
            {
                if (_webViewReady && ChatWebView.CoreWebView2 != null)
                {
                    ChatWebView.CoreWebView2.PostWebMessageAsJson(json);
                }
                else
                {
                    _pendingMessages.Add(json);
                }
            });
        }

        private void OnModelsChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => RefreshModelList());
        }

        /// <summary>
        /// 统一模式：始终设置为 Autonomous（保留方法仅为向后兼容）
        /// </summary>
        private void RestoreModeFromSettings()
        {
            ModeComboBox.SelectedIndex = 1; // Autonomous
            if (_vm != null)
                _vm.CurrentMode = AiAssistantMode.Autonomous;
        }

        /// <summary>
        /// 刷新模型下拉列表（保留已选中的模型）
        /// </summary>
        public void RefreshModelList()
        {
            if (_vm == null) return;

            var previousSelectedId = _vm.SelectedModelId;
            ModelComboBox.Items.Clear();

            int selectedIndex = -1;
            int index = 0;
            foreach (var model in LlmModelManager.Models)
            {
                var item = new ComboBoxItem
                {
                    Content = model.DisplayName ?? model.ModelName,
                    Tag = model.Id
                };
                ModelComboBox.Items.Add(item);

                // 恢复之前的选中状态
                if (model.Id == previousSelectedId)
                    selectedIndex = index;

                index++;
            }

            // 如果之前选中的模型仍存在则恢复选中，否则自动选第一个
            if (selectedIndex >= 0)
            {
                ModelComboBox.SelectedIndex = selectedIndex;
            }
            else if (ModelComboBox.Items.Count > 0)
            {
                ModelComboBox.SelectedIndex = 0;
                if (ModelComboBox.Items[0] is ComboBoxItem firstItem && firstItem.Tag is string firstId)
                    _vm.SelectedModelId = firstId;
            }
        }

        /// <summary>
        /// 模型选择变化
        /// </summary>
        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            if (ModelComboBox.SelectedItem is ComboBoxItem item && item.Tag is string modelId)
            {
                _vm.SelectedModelId = modelId;
            }
        }

        /// <summary>
        /// 模式选择变化（统一模式下已禁用 UI 切换，保留方法避免 XAML 绑定报错）
        /// </summary>
        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 统一模式：忽略 UI 切换，始终保持 Autonomous
        }

        /// <summary>
        /// 附件按钮点击 — 选择图片或文件
        /// </summary>
        private void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择图片或文件",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                if (_vm != null)
                    _vm.AttachedFilePath = dialog.FileName;

                // 显示附件预览
                AttachmentPreview.Visibility = Visibility.Visible;
                AttachmentFileName.Text = Path.GetFileName(dialog.FileName);
            }
        }

        /// <summary>
        /// 移除附件
        /// </summary>
        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
                _vm.AttachedFilePath = null;
            AttachmentPreview.Visibility = Visibility.Collapsed;
            AttachmentFileName.Text = "";
        }

        /// <summary>
        /// ViewModel 属性变化处理
        /// </summary>
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AiFlowViewModel.PendingPlan))
            {
                Dispatcher.Invoke(() =>
                {
                    ActionButtonPanel.Visibility = _vm?.PendingPlan != null
                        ? Visibility.Visible : Visibility.Collapsed;
                });
            }
            else if (e.PropertyName == nameof(AiFlowViewModel.ReportItems))
            {
                Dispatcher.Invoke(() =>
                {
                    ReportPanel.Visibility = _vm?.ReportItems?.Count > 0
                        ? Visibility.Visible : Visibility.Collapsed;
                });
            }
            else if (e.PropertyName == nameof(AiFlowViewModel.IsGenerating) ||
                     e.PropertyName == nameof(AiFlowViewModel.IsAiExecuting))
            {
                Dispatcher.Invoke(() =>
                {
                    bool isBusy = (_vm?.IsGenerating ?? false) || (_vm?.IsAiExecuting ?? false);
                    // 输入框在忙碌时禁用，但发送按钮保持可用（用作中断）
                    InputTextBox.IsEnabled = !isBusy;
                });
            }
            else if (e.PropertyName == nameof(AiFlowViewModel.AwaitingApproval))
            {
                Dispatcher.Invoke(() =>
                {
                    bool awaiting = _vm?.AwaitingApproval ?? false;
                    ApprovalPanel.Visibility = awaiting ? Visibility.Visible : Visibility.Collapsed;
                    if (awaiting)
                    {
                        ApprovalText.Text = _vm?.ApprovalDescription ?? "";
                    }
                });
            }
            else if (e.PropertyName == nameof(AiFlowViewModel.ShowRetryButton))
            {
                Dispatcher.Invoke(() =>
                {
                    RetryCard.Visibility = (_vm?.ShowRetryButton ?? false)
                        ? Visibility.Visible : Visibility.Collapsed;
                });
            }
            else if (e.PropertyName == nameof(AiFlowViewModel.LoadingText))
            {
                Dispatcher.Invoke(() =>
                {
                    PostWebMessage(new { action = "setLoading", text = _vm?.LoadingText });
                });
            }
        }

        /// <summary>
        /// 重试按钮点击
        /// </summary>
        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.RetryCommand.ExecuteAsync(null);
        }

        /// <summary>
        /// 取消重试对话框
        /// </summary>
        private void DismissRetry_Click(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.ShowRetryButton = false;
            }
        }

        /// <summary>
        /// 复制最后一条错误诊断信息
        /// </summary>
        private void CopyDebug_Click(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
            {
                var sysMsg = _vm.Messages.LastOrDefault(m => m.Role == Models.AiFlow.AiChatRole.System);
                if (sysMsg != null && !string.IsNullOrWhiteSpace(sysMsg.Content))
                {
                    Clipboard.SetText(sysMsg.Content);
                }
            }
        }

        /// <summary>
        /// 继续按钮点击（回复被截断时）
        /// </summary>
        private async void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.ContinueGenerationCommand.ExecuteAsync(null);
        }

        /// <summary>
        /// 取消继续按钮点击
        /// </summary>
        private void DismissContinueButton_Click(object sender, RoutedEventArgs e)
        {
            _vm?.DismissContinueCommand.Execute(null);
        }

        /// <summary>
        /// 发送/中断按钮点击：空闲时发送消息，忙碌时中断操作
        /// </summary>
        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            if (_vm.IsAiExecuting)
            {
                // 中断 AI 自主执行
                _vm.CancelGenerationCommand.Execute(null);
            }
            else if (_vm.IsGenerating)
            {
                // 取消生成
                _vm.CancelGenerationCommand.Execute(null);
            }
            else
            {
                // 流程运行中，禁止发送新消息
                if (_vm.MainVm.IsRunning)
                    return;

                // 正常发送消息
                await _vm.SendMessageCommand.ExecuteAsync(null);

                // 发送后清除附件
                if (_vm.AttachedFilePath != null)
                {
                    _vm.AttachedFilePath = null;
                    AttachmentPreview.Visibility = Visibility.Collapsed;
                    AttachmentFileName.Text = "";
                }
            }
        }

        /// <summary>
        /// 确认创建按钮
        /// </summary>
        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.ConfirmPlanCommand.ExecuteAsync(null);
        }

        /// <summary>
        /// 重新生成按钮
        /// </summary>
        private async void RegenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.RegenerateCommand.ExecuteAsync(null);
        }

        /// <summary>
        /// 输入框回车发送
        /// </summary>
        private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                if (_vm == null) return;

                // 流程运行中，禁止发送新消息
                if (_vm.MainVm.IsRunning)
                    return;

                await _vm.SendMessageCommand.ExecuteAsync(null);

                // 发送后清除附件
                if (_vm?.AttachedFilePath != null)
                {
                    _vm.AttachedFilePath = null;
                    AttachmentPreview.Visibility = Visibility.Collapsed;
                    AttachmentFileName.Text = "";
                }
            }
        }

        /// <summary>
        /// 报告项点击 — 打开对应卡片的属性对话框
        /// </summary>
        private void ReportItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AiFlowReportItem item && _vm != null)
            {
                _vm.OnReportItemClicked(item);
            }
        }

        /// <summary>
        /// 批准执行按钮（自主模式中风险操作暂停时）
        /// </summary>
        private void ApproveButton_Click(object sender, RoutedEventArgs e)
        {
            _vm?.ApproveExecution();
        }

        /// <summary>
        /// 中止执行按钮（自主模式中风险操作暂停时）
        /// </summary>
        private void AbortButton_Click(object sender, RoutedEventArgs e)
        {
            _vm?.AbortExecution();
        }

        /// <summary>
        /// 新建对话
        /// </summary>
        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            _vm?.NewChatCommand.Execute(null);
            // 清空 WebView2 消息
            PostWebMessage(new { action = "clearMessages" });
        }

        /// <summary>
        /// 切换历史对话面板显隐
        /// </summary>
        private void ToggleHistory_Click(object sender, RoutedEventArgs e)
        {
            _vm?.ToggleHistoryCommand.Execute(null);
        }

        /// <summary>
        /// 关闭面板
        /// </summary>
        private void ClosePanel_Click(object sender, RoutedEventArgs e)
        {
            ClosePanelRequested?.Invoke();
        }

        private bool _isSwitchingSession = false;

        /// <summary>
        /// 点击历史对话项，切换会话
        /// </summary>
        private void HistoryItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is AiChatSession session && _vm != null)
            {
                _isSwitchingSession = true;
                
                _vm.SwitchSessionCommand.Execute(session);
                
                // 序列化后单次大包传输，彻底防止前端竞态
                var msgs = _vm.Messages.Select(m => new
                {
                    role = m.Role.ToString().ToLower(),
                    content = m.Content ?? "",
                    thinking = m.ThinkingContent
                }).ToArray();

                PostWebMessage(new { action = "clearMessages" });
                PostWebMessage(new { action = "loadHistory", messages = msgs });
                
                _isSwitchingSession = false;
            }
        }

        /// <summary>
        /// 删除历史对话项
        /// </summary>
        private void DeleteHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is AiChatSession session && _vm != null)
            {
                _vm.DeleteSessionCommand.Execute(session);
            }
        }
    }
}
