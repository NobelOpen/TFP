using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using TaskFlow.Models;
using TaskFlow.Models.TaskCards;
using TaskFlow.ViewModels;
using TaskFlow.Views.Dialogs;
using TaskFlow.Resources;

namespace TaskFlow
{
    public partial class MainWindow : Window
    {
        private Point _dragStartPoint;
        private TaskCardBase? _draggedTask;
        private DateTime _dragStartTime;

        // 拖拽反馈相关
        private System.Windows.Threading.DispatcherTimer? _longPressTimer;
        private Border? _currentDragBorder;

        // 输出面板防抖定时器
        private System.Windows.Threading.DispatcherTimer? _outputPanelDebounceTimer;

        // 系统托盘图标
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private TaskOverlayWindow? _overlayWindow;

        // 全局共享的任务卡片右键菜单（避免为每个卡片创建独立实例）
        private ContextMenu? _sharedTaskCardContextMenu;

        // 滚动节流相关
        private ScrollViewer? _taskCanvasScrollViewer;

        // 多 ListBox 缓存：每个 WorkflowTab 拥有独立的 ListBox，切换时翻转 Visibility
        private readonly Dictionary<WorkflowTab, ListBox> _flowListBoxes = new();
        private ListBox? _activeFlowListBox;

        /// <summary>
        /// 兼容属性：返回当前活跃的 ListBox（替代原 XAML 中的 x:Name="TaskCanvas"）
        /// </summary>
        private ListBox TaskCanvas => _activeFlowListBox!;

        // 静态冻结 Brush 缓存（避免 AddOutputRow 每次创建新对象）
        private static readonly System.Windows.Media.SolidColorBrush LabelBrush = CreateFrozenBrush(0x6B, 0x6A, 0x65);
        private static readonly System.Windows.Media.SolidColorBrush ValueBrush = CreateFrozenBrush(0x14, 0x14, 0x13);
        private static readonly System.Windows.Media.SolidColorBrush ErrorBrush = CreateFrozenBrush(0xC4, 0x5B, 0x4A);
        private static readonly System.Windows.Media.SolidColorBrush HintBrush = CreateFrozenBrush(0xB0, 0xAE, 0xA5);
        private static readonly System.Windows.Media.SolidColorBrush SeparatorBrush = CreateFrozenBrush(0xE8, 0xE6, 0xDC);

        private static System.Windows.Media.SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        // Win32 API：设置深色标题栏
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // Win32 常量
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTMAXBUTTON = 9;

        public MainWindow()
        {
            InitializeComponent();

            // 设置深色标题栏 + 挂接 WndProc 钩子
            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var settings = TaskFlow.Models.AppSettings.Load();
                int useImmersiveDarkMode = settings.Theme == "Dark" ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));

                // 初始化 ThemeIconText
                ThemeIconText.Text = settings.Theme == "Dark" ? "\uE708" : "\uE706";

                // 挂接 WndProc 以支持 Snap Layout
                var source = HwndSource.FromHwnd(hwnd);
                source?.AddHook(WndProc);
            };

            // 初始化长按计时器
            _longPressTimer = new System.Windows.Threading.DispatcherTimer();
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(20);
            _longPressTimer.Tick += LongPressTimer_Tick;

            this.WindowState = WindowState.Normal;

            // 连接日志滚动事件
            if (DataContext is MainViewModel vm)
            {
                vm.LogScrollToEndRequested += LogScrollToEnd;
                vm.PropertyChanged += ViewModel_PropertyChanged;
                vm.FlowListBoxResetRequested += (_, __) => ClearAllFlowListBoxes();

                // 为初始 Tab 创建 ListBox
                if (vm.SelectedTab != null)
                {
                    Loaded += (_, __) => EnsureFlowListBox(vm.SelectedTab);
                }
            }

            // 初始化系统托盘图标
            InitializeNotifyIcon();

            // 窗口关闭时清理资源
            Closed += (s, e) =>
            {
                _overlayWindow?.Close();
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                // 停止微信 OCR 后台进程
                try { Services.WeChatOcrService.Shutdown(); } catch { }

                // 停止本地 API 代理进程
                try { Services.LocalProxyService.Instance.Stop(); } catch { }

                // 强制终止残留后台线程，确保进程完全退出
                Environment.Exit(0);
            };
        }

        /// <summary>
        /// 初始化系统托盘图标
        /// </summary>
        private void InitializeNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();

            // 使用应用程序图标，如果没有则使用系统默认
            try
            {
                _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location)
                    ?? System.Drawing.SystemIcons.Application;
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _notifyIcon.Text = "TaskFlow";
            _notifyIcon.Visible = false;

            // 双击托盘图标显示主窗口
            _notifyIcon.DoubleClick += (s, e) => ShowFromTray();

            // 右键菜单
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("显示主窗口", null, (s, e) => ShowFromTray());
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add("退出", null, (s, e) =>
            {
                _overlayWindow?.Close();
                _notifyIcon!.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
                System.Windows.Application.Current.Shutdown();
            });
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 如果宽度没有实质改变，或者正处在最小化状态，无需处理
            if (this.WindowState == WindowState.Minimized) return;

            // 解决主窗口跨度突然改变（比如还原），原生 ToolBar 的测量偶尔无法跟上的问题
            MainToolBar?.InvalidateMeasure();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Normal || this.WindowState == WindowState.Maximized)
            {
                // 等待 UI 还原动画结束，然后进行“布局抖动” (Layout Jiggling)
                // 欺骗 WPF 重新计算一次完整的布局树，以强制激发 ToolBar 溢出面板属性通知
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (this.WindowState == WindowState.Normal)
                    {
                        this.Width += 1;
                        this.Width -= 1;
                    }
                    MainToolBar?.InvalidateMeasure();
                    MainToolBar?.UpdateLayout();
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }

            // 更新自定义标题栏的最大化/还原按钮图标
            if (this.WindowState == WindowState.Maximized)
            {
                TitleBarMaxRestoreIcon.Text = "❐";
                TitleBarMaxRestoreBtn.ToolTip = "还原";
            }
            else
            {
                TitleBarMaxRestoreIcon.Text = "☐";
                TitleBarMaxRestoreBtn.ToolTip = "最大化";
            }

            // 最大化时补偿窗口溢出边框的 padding
            if (this.WindowState == WindowState.Maximized)
            {
                // 获取当前屏幕的工作区以计算正确的边距
                var thickness = SystemParameters.WindowResizeBorderThickness;
                RootBorder.Padding = new Thickness(
                    thickness.Left + 4,
                    thickness.Top + 4,
                    thickness.Right + 4,
                    thickness.Bottom + 4);
            }
            else
            {
                RootBorder.Padding = new Thickness(0);
            }
        }

        /// <summary>
        /// WndProc 钩子（预留，用于处理系统消息）
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            return IntPtr.Zero;
        }

        // ====== 自定义标题栏按钮事件 ======
        private async void TitleBar_ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.IsBusy) return;

            var settings = TaskFlow.Models.AppSettings.Load();
            bool isCurrentlyDark = settings.Theme == "Dark";
            string newTheme = isCurrentlyDark ? "Light" : "Dark";

            ThemeTransitionIcon.Text = newTheme == "Dark" ? "\uE708" : "\uE706";
            ThemeTransitionOverlay.Visibility = Visibility.Visible;
            
            await Task.Delay(50);
            
            TaskFlow.Helpers.ThemeManager.ApplyTheme(newTheme);
            settings.Theme = newTheme;
            settings.Save();
            
            ThemeIconText.Text = newTheme == "Dark" ? "\uE708" : "\uE706";

            // 动态设置窗口深色模式
            var hwnd = new WindowInteropHelper(this).Handle;
            int useImmersiveDarkMode = newTheme == "Dark" ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));

            await Task.Delay(350);

            ThemeTransitionOverlay.Visibility = Visibility.Collapsed;
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void TitleBar_MaxRestore_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void TitleBar_Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // ====== 运行模式下拉切换 ======
        private void RunModeDropdown_Click(object sender, RoutedEventArgs e)
        {
            RunModeDropdown.IsChecked = false;
            RunModeMenu.PlacementTarget = RunModeGrid;
            RunModeMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            RunModeMenu.IsOpen = true;
        }
        private void RunModeCurrent_Click(object sender, RoutedEventArgs e)
        {
            RunModeText.Text = TaskFlow.Resources.Strings.Main_RunCurrent;
            RunModeButton.Command = ViewModel.RunCurrentFlowCommand;
        }

        private void RunModeAll_Click(object sender, RoutedEventArgs e)
        {
            RunModeText.Text = TaskFlow.Resources.Strings.Main_RunAll;
            RunModeButton.Command = ViewModel.RunAllCommand;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab))
            {
                // 当 ViewModel 通过其他路径（AddTab、RemoveTab、文件加载等）改变 SelectedTab 时，
                // 自动切换对应的 ListBox Visibility
                Dispatcher.Invoke(() =>
                {
                    if (ViewModel.SelectedTab != null)
                    {
                        EnsureFlowListBox(ViewModel.SelectedTab);
                    }
                });
            }
        }

        /// <summary>
        /// 创建统一风格的对话框按钮（Width=80 Height=32, CornerRadius=6, 带 hover 效果）
        /// </summary>
        private static Button CreateDialogButton(string content,
            System.Windows.Media.Color bgColor, System.Windows.Media.Color fgColor,
            System.Windows.Media.Color hoverColor)
        {
            var btn = new Button
            {
                Content = content,
                Width = 80,
                Height = 32,
                Background = new System.Windows.Media.SolidColorBrush(bgColor),
                Foreground = new System.Windows.Media.SolidColorBrush(fgColor),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            // 构建 ControlTemplate（带圆角和 hover 效果）
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "Bd";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetBinding(Border.BorderBrushProperty,
                new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetBinding(Border.BorderThicknessProperty,
                new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenter);
            template.VisualTree = borderFactory;

            // hover 触发器
            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new System.Windows.Media.SolidColorBrush(hoverColor), "Bd"));
            template.Triggers.Add(hoverTrigger);

            btn.Template = template;
            return btn;
        }

        /// <summary>
        /// 隐藏到系统托盘并显示悬浮窗
        /// </summary>

        /// <summary>
        /// 隐藏到系统托盘并显示悬浮窗
        /// </summary>
        private void HideToTray_Click(object sender, RoutedEventArgs e)
        {

            // 隐藏主窗口
            Hide();

            // 显示托盘图标
            if (_notifyIcon != null)
                _notifyIcon.Visible = true;

            // 打开悬浮窗
            _overlayWindow?.Close();
            _overlayWindow = new TaskOverlayWindow(ViewModel);
            _overlayWindow.ShowMainWindowRequested += (s, args) => ShowFromTray();
            _overlayWindow.Closed += (s, args) => _overlayWindow = null;
            _overlayWindow.Show();
        }

        /// <summary>
        /// 从托盘恢复主窗口
        /// </summary>
        private void ShowFromTray()
        {
            Dispatcher.Invoke(() =>
            {
                // 关闭悬浮窗
                _overlayWindow?.Close();
                _overlayWindow = null;

                // 隐藏托盘图标
                if (_notifyIcon != null)
                    _notifyIcon.Visible = false;

                // 显示主窗口
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
        }

        private MainViewModel ViewModel => (MainViewModel)DataContext;

        #region 流程 ListBox 动态管理

        /// <summary>
        /// 为指定 Tab 创建 ListBox（复制原 XAML TaskCanvas 的所有属性和事件）
        /// </summary>
        private ListBox CreateFlowListBox(WorkflowTab tab)
        {
            var listBox = new ListBox
            {
                ItemsSource = tab.VisibleTaskCards,
                ItemTemplate = (DataTemplate)FindResource("TaskCardTemplate"),
                AllowDrop = true,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8),
                SelectionMode = System.Windows.Controls.SelectionMode.Single,
                Visibility = Visibility.Collapsed
            };

            // 物理平滑滚动设置 (关闭虚拟化并使用像素滚动)
            VirtualizingPanel.SetIsVirtualizing(listBox, false);
            ScrollViewer.SetCanContentScroll(listBox, false);
            ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);

            // 事件绑定
            listBox.Drop += Canvas_Drop;
            listBox.DragOver += Canvas_DragOver;
            listBox.PreviewMouseLeftButtonDown += Canvas_PreviewMouseLeftButtonDown;
            listBox.ContextMenuOpening += TaskList_ContextMenuOpening;
            listBox.PreviewMouseWheel += Canvas_SmoothMouseWheel;

            // 共享画布右键菜单
            listBox.ContextMenu = (ContextMenu)FlowCanvasHost.FindResource("CanvasContextMenu");

            return listBox;
        }

        /// <summary>
        /// 确保指定 Tab 有对应的 ListBox，并切换为可见
        /// </summary>
        internal void EnsureFlowListBox(WorkflowTab tab)
        {
            if (!_flowListBoxes.TryGetValue(tab, out var listBox))
            {
                // 首次访问：创建并加入容器
                listBox = CreateFlowListBox(tab);
                _flowListBoxes[tab] = listBox;
                FlowCanvasHost.Children.Add(listBox);
            }

            // 隐藏当前活跃 ListBox
            if (_activeFlowListBox != null && _activeFlowListBox != listBox)
            {
                _activeFlowListBox.Visibility = Visibility.Collapsed;
            }

            // 显示目标 ListBox
            listBox.Visibility = Visibility.Visible;
            _activeFlowListBox = listBox;

            // 更新 ScrollViewer 引用（用于滚动节流）
            _taskCanvasScrollViewer = FindVisualChild<ScrollViewer>(listBox);
        }

        /// <summary>
        /// 删除 Tab 时清理对应的 ListBox
        /// </summary>
        internal void RemoveFlowListBox(WorkflowTab tab)
        {
            if (_flowListBoxes.TryGetValue(tab, out var listBox))
            {
                FlowCanvasHost.Children.Remove(listBox);
                _flowListBoxes.Remove(tab);

                if (_activeFlowListBox == listBox)
                {
                    _activeFlowListBox = null;
                    _taskCanvasScrollViewer = null;
                }
            }
        }

        /// <summary>
        /// 新建项目时清空所有缓存的 ListBox
        /// </summary>
        internal void ClearAllFlowListBoxes()
        {
            FlowCanvasHost.Children.Clear();
            _flowListBoxes.Clear();
            _activeFlowListBox = null;
            _taskCanvasScrollViewer = null;
        }

        #endregion

    }
}

