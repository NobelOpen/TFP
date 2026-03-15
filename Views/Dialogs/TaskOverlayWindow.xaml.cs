using System.ComponentModel;
using System.Windows;
using TaskFlow.Resources;
using System.Windows.Input;
using System.Windows.Media;
using TaskFlow.ViewModels;

namespace TaskFlow.Views.Dialogs
{
    /// <summary>
    /// 实时任务悬浮窗
    /// </summary>
    public partial class TaskOverlayWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly System.Windows.Media.Animation.DoubleAnimation _spinAnimation;

        /// <summary>
        /// 请求显示主窗口的事件
        /// </summary>
        public event EventHandler? ShowMainWindowRequested;

        public TaskOverlayWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            ApplyLocalization();
            _viewModel = viewModel;

            _spinAnimation = new System.Windows.Media.Animation.DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1.5)))
            {
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };

            // 初始位置：屏幕右上角
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 16;
            Top = workArea.Top + 16;

            this.MouseEnter += (s, e) => { TaskAreaContainer.Opacity = 1.0; };
            this.MouseLeave += (s, e) => 
            { 
                if (_viewModel.IsRunning)
                {
                    TaskAreaContainer.Opacity = 0.5;
                }
            };

            // 监听 ViewModel 属性变化
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // 初始更新
            UpdateFlowName();
            UpdateTaskDisplay();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainViewModel.CurrentRunningTask) or
                nameof(MainViewModel.CurrentTaskBreadcrumb) or
                nameof(MainViewModel.PreviousTask) or
                nameof(MainViewModel.NextTask) or
                nameof(MainViewModel.IsRunning))
            {
                Dispatcher.Invoke(UpdateTaskDisplay);
            }
            else if (e.PropertyName == nameof(MainViewModel.SelectedTab))
            {
                Dispatcher.Invoke(UpdateFlowName);
            }
        }

        private void UpdateFlowName()
        {
            FlowNameText.Text = _viewModel.SelectedTab?.Name ?? "TaskFlow";
        }

        private void UpdateTaskDisplay()
        {
            // 当前任务
            if (_viewModel.CurrentRunningTask != null)
            {
                CurrentTaskText.Text = $"#{_viewModel.CurrentRunningTask.Order} {_viewModel.CurrentRunningTask.Name}";
                
                if (!string.IsNullOrEmpty(_viewModel.CurrentTaskBreadcrumb))
                {
                    CurrentTaskBreadcrumb.Text = _viewModel.CurrentTaskBreadcrumb;
                    CurrentTaskBreadcrumb.Visibility = Visibility.Visible;
                }
                else
                {
                    CurrentTaskBreadcrumb.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                CurrentTaskText.Text = _viewModel.IsRunning ? Strings.Win_Executing : Strings.Win_WaitingExec;
                CurrentTaskBreadcrumb.Visibility = Visibility.Collapsed;
            }

            // 上一个任务
            if (_viewModel.PreviousTask != null)
            {
                PrevTaskText.Text = $"#{_viewModel.PreviousTask.Order} {_viewModel.PreviousTask.Name}";
                PrevTaskCard.Visibility = Visibility.Visible;
            }
            else
            {
                PrevTaskText.Text = "—";
                PrevTaskCard.Visibility = Visibility.Collapsed;
            }

            // 下一个任务
            if (_viewModel.NextTask != null)
            {
                NextTaskText.Text = $"#{_viewModel.NextTask.Order} {_viewModel.NextTask.Name}";
                NextTaskCard.Visibility = Visibility.Visible;
            }
            else
            {
                NextTaskText.Text = "—";
                NextTaskCard.Visibility = Visibility.Collapsed;
            }

            // 状态指示器颜色
            var indicator = (System.Windows.Shapes.Ellipse)((System.Windows.Controls.StackPanel)FlowNameText.Parent).Children[0];
            if (_viewModel.IsRunning)
            {
                indicator.Fill = new SolidColorBrush(Color.FromRgb(120, 140, 93)); // Anthropic 绿
                // 运行中：运行按钮图标变灰
                RunAllIcon.Foreground = new SolidColorBrush(Color.FromRgb(176, 174, 165));
                RunCurrentIcon.Foreground = new SolidColorBrush(Color.FromRgb(176, 174, 165));
                StopIcon.Foreground = new SolidColorBrush(Color.FromRgb(161, 38, 13));  // #a1260d

                if (!IsMouseOver)
                {
                    TaskAreaContainer.Opacity = 0.5;
                }

                SwitchFlowBtn.IsHitTestVisible = false;
                SwitchFlowRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, _spinAnimation);
            }
            else
            {
                indicator.Fill = new SolidColorBrush(Color.FromRgb(176, 174, 165)); // 灰色
                // 未运行：恢复原色
                RunAllIcon.Foreground = new SolidColorBrush(Color.FromRgb(56, 138, 52));    // #388a34
                RunCurrentIcon.Foreground = new SolidColorBrush(Color.FromRgb(42, 161, 152)); // #2aa198
                StopIcon.Foreground = new SolidColorBrush(Color.FromRgb(176, 174, 165));      // 灰色

                TaskAreaContainer.Opacity = 1.0;

                SwitchFlowBtn.IsHitTestVisible = true;
                SwitchFlowRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
                SwitchFlowRotateTransform.Angle = 0;
            }
        }

        // 拖动窗口
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击显示主窗口
                ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            DragMove();
        }

        // 运行全部
        private void RunAll_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.RunAllCommand.CanExecute(null))
                _viewModel.RunAllCommand.Execute(null);
        }

        // 运行当前流程
        private void RunCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.RunCurrentFlowCommand.CanExecute(null))
                _viewModel.RunCurrentFlowCommand.Execute(null);
        }

        // 停止执行
        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.StopExecutionCommand.CanExecute(null))
                _viewModel.StopExecutionCommand.Execute(null);
        }

        // 切换至下一个流程
        private void SwitchFlow_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.IsRunning) return; // 运行中不允许切换

            var tabs = _viewModel.Tabs;
            if (tabs == null || tabs.Count <= 1) return;

            var currentIndex = tabs.IndexOf(_viewModel.SelectedTab!);
            var nextIndex = (currentIndex + 1) % tabs.Count;
            _viewModel.SelectedTab = tabs[nextIndex];
        }

        // 显示主窗口
        private void ShowMainWindow_Click(object sender, RoutedEventArgs e)
        {
            ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            base.OnClosing(e);
        }

        private void ApplyLocalization()
        {
            TxtPrevLabel.Text = Strings.UI_Overlay_Prev;
            TxtCurrentLabel.Text = Strings.UI_Overlay_Current;
            TxtNextLabel.Text = Strings.UI_Overlay_Next;
        }
    }
}
