using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
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

            // 绑定消息列表
            MessageList.ItemsSource = _vm.Messages;

            // 绑定报告列表
            ReportList.ItemsSource = _vm.ReportItems;

            // 监听方案变化，控制按钮区显示
            _vm.PropertyChanged += ViewModel_PropertyChanged;

            // 监听滚动请求
            _vm.ScrollToEndRequested += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MessageScrollViewer.ScrollToEnd();
                }), System.Windows.Threading.DispatcherPriority.Background);
            };

            // 初始化模型列表
            RefreshModelList();

            // 订阅模型变化事件以动态更新下拉框
            LlmModelManager.ModelsChanged -= OnModelsChanged;
            LlmModelManager.ModelsChanged += OnModelsChanged;

            // 从设置恢复模式选择
            RestoreModeFromSettings();
        }

        private void OnModelsChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => RefreshModelList());
        }

        /// <summary>
        /// 从 AppSettings 恢复模式选择
        /// </summary>
        private void RestoreModeFromSettings()
        {
            try
            {
                var settings = AppSettings.Load();
                var mode = (AiAssistantMode)settings.AiAssistantMode;
                ModeComboBox.SelectedIndex = (int)mode;
                if (_vm != null)
                    _vm.CurrentMode = mode;
            }
            catch
            {
                ModeComboBox.SelectedIndex = 0;
            }
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
        /// 模式选择变化
        /// </summary>
        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            if (ModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                var mode = (AiAssistantMode)int.Parse(tag);
                _vm.CurrentMode = mode;

                // 持久化到设置
                try
                {
                    var settings = AppSettings.Load();
                    settings.AiAssistantMode = (int)mode;
                    settings.Save();
                }
                catch { }
            }
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
                        // 滚动到底部让用户看到批准面板
                        MessageScrollViewer.ScrollToEnd();
                    }
                });
            }
            else if (e.PropertyName == nameof(AiFlowViewModel.ShowRetryButton))
            {
                Dispatcher.Invoke(() =>
                {
                    RetryButtonPanel.Visibility = (_vm?.ShowRetryButton ?? false)
                        ? Visibility.Visible : Visibility.Collapsed;
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

        /// <summary>
        /// 点击历史对话项，切换会话
        /// </summary>
        private void HistoryItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is AiChatSession session && _vm != null)
            {
                _vm.SwitchSessionCommand.Execute(session);
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
