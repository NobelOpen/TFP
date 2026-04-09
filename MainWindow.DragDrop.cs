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
    // 滚动节流 + TaskCard 拖拽事件
    public partial class MainWindow
    {
        #region 滚动节流

        /// <summary>
        /// ListBox 加载完成后，获取内部 ScrollViewer 和垂直 ScrollBar，
        /// 用于监听 Thumb 拖拽事件以实现自适应延迟滚动
        /// </summary>
        private void TaskCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            // 由 EnsureFlowListBox 在首次显示时获取 ScrollViewer
            // 此方法保留为空以防其他地方引用
        }

        /// <summary>
        /// ScrollBar 的 Scroll 事件：
        /// 拖拽 Thumb 时启用延迟滚动（内容不实时刷新，避免卡顿），
        /// 释放 Thumb 时关闭延迟滚动（一次性跳转到目标位置）。
        /// 滚轮滚动和点击轨道不受影响。
        /// </summary>
        private void TaskCanvas_ScrollBarScroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
        {
            if (_taskCanvasScrollViewer == null) return;

            switch (e.ScrollEventType)
            {
                case System.Windows.Controls.Primitives.ScrollEventType.ThumbTrack:
                    // Thumb 正在拖动，启用延迟滚动以避免卡顿
                    _taskCanvasScrollViewer.SetValue(
                        ScrollViewer.IsDeferredScrollingEnabledProperty, true);
                    break;

                case System.Windows.Controls.Primitives.ScrollEventType.EndScroll:
                    // Thumb 已释放，关闭延迟滚动，内容会一次性跳转到目标位置
                    _taskCanvasScrollViewer.SetValue(
                        ScrollViewer.IsDeferredScrollingEnabledProperty, false);
                    break;
            }
        }

        #region 平滑滚轮滚动（指数衰减插值）

        // 平滑滚动的目标偏移量
        private double _smoothScrollTarget;
        // 平滑滚动的当前虚拟偏移量（驱动 ScrollViewer）
        private double _smoothScrollCurrent;
        // 当前绑定的目标 ScrollViewer
        private ScrollViewer? _smoothScrollViewer;
        // 是否正在执行平滑滚动帧循环
        private bool _smoothScrollRunning;

        /// <summary>
        /// 拦截滚轮事件，启动基于 CompositionTarget.Rendering 的每帧指数衰减平滑滚动。
        /// 无论滚轮频率多高，只更新目标值，不叠加动画，从根本上杜绝"拖住"问题。
        /// </summary>
        private void Canvas_SmoothMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ListBox listBox) return;
            var sv = FindVisualChild<ScrollViewer>(listBox);
            if (sv == null) return;

            // 阻断 ScrollViewer 默认的瞬间跳跃
            e.Handled = true;

            // 每格滚轮对应的滚动像素量（可调）
            const double scrollStep = 120.0;
            double delta = -e.Delta / 120.0 * scrollStep;

            // 切换 ScrollViewer 时重置状态
            if (_smoothScrollViewer != sv)
            {
                _smoothScrollViewer = sv;
                _smoothScrollCurrent = sv.VerticalOffset;
                _smoothScrollTarget = sv.VerticalOffset;
            }

            // 动态追加目标（不重置，无论多快拨轮都只更新终点）
            _smoothScrollTarget = Math.Max(0,
                Math.Min(_smoothScrollTarget + delta, sv.ScrollableHeight));

            // 启动帧循环（幂等，重复启动无害）
            if (!_smoothScrollRunning)
            {
                _smoothScrollRunning = true;
                System.Windows.Media.CompositionTarget.Rendering += SmoothScroll_OnRendering;
            }
        }

        /// <summary>
        /// 每帧回调：按指数衰减系数向目标靠拢。
        /// 当距离足够小时停止帧循环，释放 CPU。
        /// </summary>
        private void SmoothScroll_OnRendering(object? sender, EventArgs e)
        {
            var sv = _smoothScrollViewer;
            if (sv == null) { StopSmoothScroll(); return; }

            // 指数衰减插值：每帧向目标靠拢 20%（相当于约 150ms 接近终点，60fps 下约 9 帧）
            _smoothScrollCurrent += (_smoothScrollTarget - _smoothScrollCurrent) * 0.18;

            sv.ScrollToVerticalOffset(_smoothScrollCurrent);

            // 距目标不足 0.5px 时停止帧循环
            if (Math.Abs(_smoothScrollTarget - _smoothScrollCurrent) < 0.5)
            {
                sv.ScrollToVerticalOffset(_smoothScrollTarget);
                _smoothScrollCurrent = _smoothScrollTarget;
                StopSmoothScroll();
            }
        }

        private void StopSmoothScroll()
        {
            if (_smoothScrollRunning)
            {
                System.Windows.Media.CompositionTarget.Rendering -= SmoothScroll_OnRendering;
                _smoothScrollRunning = false;
            }
        }

        #endregion

        /// <summary>
        /// 从父元素中查找指定类型的第一个子元素（无名称版本）
        /// </summary>
        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                var found = FindVisualChild<T>(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        #endregion

        #region Task Card Events

        private void LongPressTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentDragBorder == null)
            {
                StopLongPressTimer();
                return;
            }

            var elapsed = (DateTime.Now - _dragStartTime).TotalMilliseconds;
            // 通过 Opacity 渐变提示长按进度（1.0 → 0.6）
            _currentDragBorder.Opacity = 1.0 - (Math.Min(elapsed, 300) / 300.0) * 0.4;
        }

        private void StopLongPressTimer()
        {
            _longPressTimer?.Stop();
            if (_currentDragBorder != null)
            {
                _currentDragBorder.Opacity = 1.0;
                _currentDragBorder = null;
            }
        }

        private void TaskCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is TaskCardBase task)
            {
                // 子流程输入卡片禁止任何鼠标交互（不允许被选中）
                if (task.TaskType == TaskType.SubFlowInput) return;

                // 检查是否双击（用于折叠/展开，运行中也允许）
                if (e.ClickCount == 2)
                {
                    ViewModel.ToggleBranchCollapseCommand.Execute(task);
                    StopLongPressTimer();
                    return;
                }

                // 运行中只允许选中，禁止长按和拖拽
                if (ViewModel.IsBusy)
                {
                    ViewModel.SelectTaskCommand.Execute(task);
                    return;
                }

                // 单击选中
                ViewModel.SelectTaskCommand.Execute(task);

                // 记录拖拽开始位置和时间
                _dragStartPoint = e.GetPosition(this);
                _dragStartTime = DateTime.Now;

                // 只有可拖拽的卡片才启用长按反馈
                if (CanDrag(task))
                {
                    _currentDragBorder = border;
                    _longPressTimer?.Start();
                }
            }
        }

        private void TaskCard_MouseMove(object sender, MouseEventArgs e)
        {
            // 运行中禁止拖拽
            if (ViewModel.IsBusy) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                StopLongPressTimer();
                return;
            }

            if (sender is Border border && border.DataContext is TaskCardBase task)
            {
                var elapsed = (DateTime.Now - _dragStartTime).TotalMilliseconds;
                var currentPoint = e.GetPosition(this);
                var diff = _dragStartPoint - currentPoint;
                bool isMoveThresholdReached = Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                                              Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance;

                // 如果移动了但时间未到300ms，说明用户只是普通点击或误操作，取消进度条显示
                if (isMoveThresholdReached && elapsed < 300)
                {
                    StopLongPressTimer();
                    return;
                }

                // 长按时间足够且发生了移动 -> 触发拖拽
                if (elapsed >= 300 && isMoveThresholdReached)
                {
                    StopLongPressTimer(); // 拖拽开始前隐藏进度条

                    // 检查是否可拖拽 (双重检查)
                    if (!CanDrag(task))
                        return;

                    _draggedTask = task;

                    // 开始拖拽
                    var data = new DataObject("TaskCard", task);
                    DragDrop.DoDragDrop(border, data, DragDropEffects.Move);

                    _draggedTask = null;
                }
            }
        }

        private void TaskCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            StopLongPressTimer();
        }

        private T? FindVisualChild<T>(DependencyObject parent, string childName) where T : FrameworkElement
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == childName)
                {
                    return element;
                }

                var result = FindVisualChild<T>(child, childName);
                if (result != null)
                    return result;
            }
            return null;
        }

        private bool CanDrag(TaskCardBase task)
        {
            // 禁止拖拽子流程输入卡片
            if (task.TaskType == TaskType.SubFlowInput)
                return false;

            // 非分支卡片始终可拖拽
            if (task.BranchRole == BranchRole.None)
                return true;

            // IfStart或ForLoopStart在折叠状态下可拖拽
            if ((task.BranchRole == BranchRole.IfStart || task.BranchRole == BranchRole.ForLoopStart)
                && task.IsCollapsed)
                return true;

            return false;
        }

        private void TaskCard_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("TaskCard"))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void TaskCard_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("TaskCard"))
                return;

            // 无论后续逻辑如何，只要是 TaskCard 拖拽，就阻止冒泡
            // 防止Canvas_Drop被触发导致卡片飞到底部
            e.Handled = true;

            if (sender is Border border && border.DataContext is TaskCardBase targetTask)
            {
                var sourceTask = e.Data.GetData("TaskCard") as TaskCardBase;
                if (sourceTask == null || sourceTask == targetTask)
                    return;

                var sourceIndex = ViewModel.TaskCards.IndexOf(sourceTask);
                var targetIndex = ViewModel.TaskCards.IndexOf(targetTask);

                if (sourceIndex >= 0 && targetIndex >= 0)
                {
                    // 如果目标卡片是折叠的分支/循环头，将目标索引调整到整个分组末尾之后
                    if (targetTask.IsCollapsed && targetTask.BranchGroupId.HasValue &&
                        (targetTask.BranchRole == BranchRole.IfStart || targetTask.BranchRole == BranchRole.ForLoopStart))
                    {
                        // 找到分组结束卡片的索引
                        var branchCards = ViewModel.TaskCards
                            .Where(t => t.BranchGroupId == targetTask.BranchGroupId && t != targetTask).ToList();

                        int endIdx = targetIndex;
                        if (targetTask.BranchRole == BranchRole.IfStart)
                        {
                            var elseEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ElseEnd);
                            if (elseEnd != null) endIdx = ViewModel.TaskCards.IndexOf(elseEnd);
                        }
                        else if (targetTask.BranchRole == BranchRole.ForLoopStart)
                        {
                            var loopEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ForLoopEnd);
                            if (loopEnd != null) endIdx = ViewModel.TaskCards.IndexOf(loopEnd);
                        }

                        // 目标位置为分组末尾之后
                        targetIndex = endIdx;

                        // 源在目标上方时，不需要+1（Move语义已自动处理）
                        if (sourceIndex > targetIndex)
                        {
                            targetIndex = endIdx + 1;
                        }
                    }
                    else
                    {
                        // 普通卡片：保持原有逻辑，放在目标卡片下方
                        if (sourceIndex > targetIndex)
                        {
                            targetIndex = targetIndex + 1;
                        }
                    }

                    ViewModel.MoveTaskCommand.Execute(Tuple.Create(sourceIndex, targetIndex));
                }
            }
        }

        private void Canvas_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("TaskCard"))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void Canvas_Drop(object sender, DragEventArgs e)
        {
            // 拖放到画布末尾
            if (!e.Data.GetDataPresent("TaskCard"))
                return;

            var sourceTask = e.Data.GetData("TaskCard") as TaskCardBase;
            if (sourceTask == null)
                return;

            var sourceIndex = ViewModel.TaskCards.IndexOf(sourceTask);
            if (sourceIndex >= 0)
            {
                ViewModel.MoveTaskCommand.Execute(Tuple.Create(sourceIndex, ViewModel.TaskCards.Count - 1));
            }
        }

        private void LogScrollToEnd(object? sender, EventArgs e)
        {
            LogScrollViewer.ScrollToEnd();
        }

        /// <summary>
        /// 分页标签点击事件，切换选中分页（通过 Visibility 翻转实现瞬切）
        /// </summary>
        private void TabItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is WorkflowTab tab)
            {
                // 指示线滑动动画
                if (FlowTabIndicator != null && FlowTabItemsControl != null)
                {
                    AnimateIndicator(FlowTabIndicator, FlowTabIndicatorTransform, btn, FlowTabItemsControl);
                }

                // 切换 ListBox Visibility（瞬间完成，无需等待）
                EnsureFlowListBox(tab);

                // 更新 ViewModel 状态
                ViewModel.SelectedTab = tab;
            }
        }

        /// <summary>
        /// 分页标签栏向左滚动
        /// </summary>
        private void TabScrollLeft_Click(object sender, RoutedEventArgs e)
        {
            TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset - 80);
        }

        /// <summary>
        /// 分页标签栏向右滚动
        /// </summary>
        private void TabScrollRight_Click(object sender, RoutedEventArgs e)
        {
            TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset + 80);
        }

        private void OpenVariableManager_Click(object sender, RoutedEventArgs e)
        {
            // 忙碌状态禁止操作
            if (ViewModel.IsBusy) return;

            var dialog = new VariableManagerDialog(ViewModel.VariableStore);
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenFlowManager_Click(object sender, RoutedEventArgs e)
        {
            // 忙碌状态禁止操作
            if (ViewModel.IsBusy) return;

            var dialog = new FlowManagerDialog(
                ViewModel.Tabs,
                tab => ViewModel.SelectedTab = tab,
                ViewModel.NextTabIndex);
            dialog.Owner = this;
            dialog.ShowDialog();
            ViewModel.NextTabIndex = dialog.NextTabIndex;
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            // 忙碌状态禁止操作
            if (ViewModel.IsBusy) return;

            var settings = TaskFlow.Models.AppSettings.Load();
            var dialog = new Views.Dialogs.SettingsDialog(settings) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                // 设置已保存，通知 ViewModel 更新
                ViewModel.ApplySettings(settings);

                ThemeIconText.Text = settings.Theme == "Dark" ? "\uE708" : "\uE706";
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int useImmersiveDarkMode = settings.Theme == "Dark" ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref useImmersiveDarkMode, sizeof(int));
            }
        }

        private void OpenModelManager_Click(object sender, RoutedEventArgs e)
        {
            // 忙碌状态禁止操作
            if (ViewModel.IsBusy) return;

            var dialog = new ModelManagerDialog(this);
            dialog.ShowDialog();
        }

        private void OpenOnnxModelManager_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy) return;

            var dialog = new OnnxModelManagerDialog(this);
            dialog.ShowDialog();
        }

        /// <summary>
        /// 工具栏帮助按钮 —— 打开帮助文档首页
        /// </summary>
        private void OpenHelp_Click(object sender, RoutedEventArgs e)
        {
            OpenHelpDocument(null);
        }

        /// <summary>
        /// AI 助手按钮 - 切换左侧 AI 面板的显示/隐藏
        /// </summary>
        private void ToggleAiPanel_Click(object sender, RoutedEventArgs e)
        {
            // 忙碌状态禁止操作
            if (ViewModel.IsBusy) return;

            ViewModel.IsAiPanelOpen = !ViewModel.IsAiPanelOpen;

            if (ViewModel.IsAiPanelOpen)
            {
                InitializeAiPanel();
                // 每次打开面板时刷新模型列表（用户可能在模型管理中添加了新模型）
                AiFlowPanelControl.RefreshModelList();
                AiFlowPanelControl.Visibility = Visibility.Visible;
                AiPanelSplitter.Visibility = Visibility.Visible;
                AiPanelColumn.Width = new GridLength(320);
                AiPanelSplitterColumn.Width = new GridLength(5);
                
                // 收缩右侧面板为流程画布腾出空间，并与左侧Orchid宽度(320)保持一致
                RightPanelColumn.Width = new GridLength(320);
            }
            else
            {
                AiFlowPanelControl.Visibility = Visibility.Collapsed;
                AiPanelSplitter.Visibility = Visibility.Collapsed;
                AiPanelColumn.Width = new GridLength(0);
                AiPanelSplitterColumn.Width = new GridLength(0);
                
                // 恢复右侧面板默认宽度
                RightPanelColumn.Width = new GridLength(420);
            }
        }

        private bool _aiPanelInitialized = false;

        /// <summary>
        /// 初始化 AI 流程助手面板（仅首次调用时执行）
        /// </summary>
        private void InitializeAiPanel()
        {
            if (_aiPanelInitialized) return;
            _aiPanelInitialized = true;

            AiFlowPanelControl.Initialize(ViewModel.AiFlowVm);

            ViewModel.AiFlowVm.OpenCardPropertyRequested += (cardId) =>
            {
                var task = ViewModel.TaskCards.FirstOrDefault(t => t.Id == cardId);
                if (task != null)
                {
                    var dialog = new Views.Dialogs.TaskPropertyDialog(task, ViewModel) { Owner = this };
                    if (dialog.ShowDialog() == true)
                    {
                        ViewModel.AddLog($"[AI] 已配置卡片: {task.Name}");
                    }
                }
            };

            // 订阅面板内关闭按钮事件
            AiFlowPanelControl.ClosePanelRequested += () =>
            {
                ViewModel.IsAiPanelOpen = false;
                AiFlowPanelControl.Visibility = Visibility.Collapsed;
                AiPanelSplitter.Visibility = Visibility.Collapsed;
                AiPanelColumn.Width = new GridLength(0);
                AiPanelSplitterColumn.Width = new GridLength(0);
                RightPanelColumn.Width = new GridLength(420);
            };
        }

        /// <summary>
        /// 卡片右键菜单帮助 —— 跳转到对应任务类型的文档锚点
        /// </summary>
        private void OpenTaskHelp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi)
            {
                var task = FindTaskFromMenuItem(mi);
                if (task != null)
                {
                    // 将 TaskType 映射到帮助文档锚点 ID
                    var anchor = task.TaskType switch
                    {
                        // 控制流结构块 → 映射到对应的文档分页
                        TaskType.IfStart or TaskType.IfEnd or TaskType.ElifStart
                            or TaskType.ElseStart or TaskType.ElseEnd => "IfElse",
                        TaskType.ForLoopStart or TaskType.ForLoopEnd => "ForLoop",

                        // 其余 TaskType 名称与帮助文档 id 一致
                        _ => task.TaskType.ToString()
                    };
                    OpenHelpDocument(anchor);
                }
            }
        }

        /// <summary>
        /// 打开帮助文档（可选锚点跳转）
        /// </summary>
        private static void OpenHelpDocument(string? anchor)
        {
            try
            {
                var helpPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "docs",
                    System.Threading.Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh") ? "help_zh.html" : "help_en.html");

                if (!System.IO.File.Exists(helpPath))
                {
                    MessageBox.Show(Strings.UI_HelpNotFound, Strings.Common_Help,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 无锚点时直接打开文档；有锚点时通过临时跳转页面解决 Shell 丢弃 #fragment 问题
                string openPath;
                if (string.IsNullOrEmpty(anchor))
                {
                    openPath = helpPath;
                }
                else
                {
                    var targetUrl = new Uri(helpPath).AbsoluteUri + "#" + anchor;
                    var redirectHtml = $"<!DOCTYPE html><html><head><meta charset=\"UTF-8\"><script>window.location.replace(\"{targetUrl}\");</script></head><body></body></html>";
                    var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "taskflow_help_redirect.html");
                    System.IO.File.WriteAllText(tempPath, redirectHtml);
                    openPath = tempPath;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(openPath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开帮助文档：{ex.Message}", "帮助",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 将 TaskType 映射到帮助文档锚点 ID
        /// 控制流分支卡片统一映射到父级文档节（如 IfStart/IfEnd → IfElse）
        /// </summary>
        private static string GetHelpAnchor(TaskType type)
        {
            return type switch
            {
                TaskType.IfStart or TaskType.IfEnd or
                TaskType.ElifStart or TaskType.ElseStart or TaskType.ElseEnd
                    => "IfElse",
                TaskType.ForLoopStart or TaskType.ForLoopEnd
                    => "ForLoop",
                TaskType.CallSubFlow or TaskType.SubFlowInput or TaskType.SubFlowOutput
                    => "SubFlow",
                _ => type.ToString()
            };
        }


        private void Canvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 检查点击目标是否在任务卡片上
            if (e.OriginalSource is DependencyObject source)
            {
                // 向上查找是否在某个TaskCard的Border内
                var parent = source;
                while (parent != null)
                {
                    if (parent is Border border && border.DataContext is TaskCardBase)
                    {
                        // 点击在任务卡片上，不处理
                        return;
                    }
                    if (parent is System.Windows.Media.Visual visual)
                    {
                        parent = System.Windows.Media.VisualTreeHelper.GetParent(visual);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // 点击在空白处，取消选中并取消高亮
            ViewModel.DeselectAllCommand.Execute(null);
        }

        private void TaskCard_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 运行中或Orchid执行中禁止弹出右键菜单
            if (ViewModel.IsBusy) return;

            if (sender is Border border && border.DataContext is TaskCardBase task)
            {
                // 子流程输入卡片被视为起始定位锚，禁止任何交互和修改
                if (task.TaskType == TaskType.SubFlowInput) return;

                // 懒初始化共享 ContextMenu
                if (_sharedTaskCardContextMenu == null)
                {
                    _sharedTaskCardContextMenu = CreateTaskCardContextMenu();
                }

                // 判断是否为非起始分支卡片（包括ElifStart可编辑）
                bool isNonEditableBranch = task.BranchRole != BranchRole.None &&
                                           task.BranchRole != BranchRole.IfStart &&
                                           task.BranchRole != BranchRole.ElifStart &&
                                           task.BranchRole != BranchRole.ForLoopStart;

                bool isIfStart = task.BranchRole == BranchRole.IfStart;

                // 动态显示/隐藏菜单项
                foreach (var item in _sharedTaskCardContextMenu.Items)
                {
                    if (item is MenuItem menuItem)
                    {
                        string? tag = menuItem.Tag as string;
                        if (tag == "EditProperty" || tag == "Rename")
                        {
                            menuItem.Visibility = isNonEditableBranch ? Visibility.Collapsed : Visibility.Visible;
                        }
                        else if (tag == "Delete")
                        {
                            menuItem.Visibility = isNonEditableBranch ? Visibility.Collapsed : Visibility.Visible;
                            if (task.BranchRole == BranchRole.ElifStart)
                            {
                                menuItem.Header = TaskFlow.Resources.Strings.Menu_DeleteElifBranch;
                            }
                            else
                            {
                                menuItem.Header = TaskFlow.Resources.Strings.Common_Delete;
                            }
                        }
                        else if (tag == "AddElif")
                        {
                            // 仅对IfStart卡片显示"增加Elif分支"
                            menuItem.Visibility = isIfStart ? Visibility.Visible : Visibility.Collapsed;
                        }
                        else if (tag == "ToggleElse")
                        {
                            // 仅对IfStart卡片显示，并动态切换文本
                            menuItem.Visibility = isIfStart ? Visibility.Visible : Visibility.Collapsed;
                            if (isIfStart && task is IfElseBranchTaskCard ifCard)
                            {
                                menuItem.Header = ifCard.IsElseHidden ? Strings.Menu_ShowElse : Strings.Menu_HideElse;
                            }
                        }
                        else if (menuItem.Header is string header && header == TaskFlow.Resources.Strings.Common_Help)
                        {
                            // 动态设置帮助锚点为当前任务类型
                            menuItem.Tag = GetHelpAnchor(task.TaskType);
                        }
                        else if (tag == "Paste")
                        {
                            menuItem.IsEnabled = ViewModel.HasCopiedTask;
                        }
                    }
                    else if (item is Separator separator)
                    {
                        string? tag = separator.Tag as string;
                        if (tag == "DeleteSeparator" || tag == "EditSeparator")
                        {
                            separator.Visibility = isNonEditableBranch ? Visibility.Collapsed : Visibility.Visible;
                        }
                        else if (tag == "ElifSeparator")
                        {
                            separator.Visibility = isIfStart ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }
                }

                // 设置 PlacementTarget 并弹出菜单
                UpdateSubFlowMenuItemsVisibility(_sharedTaskCardContextMenu);
                _sharedTaskCardContextMenu.PlacementTarget = border;
                _sharedTaskCardContextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        /// <summary>
        /// 动态创建任务卡片的右键菜单（延迟加载，避免虚拟化回收时的开销）
        /// </summary>
        private ContextMenu CreateTaskCardContextMenu()
        {
            var menu = new ContextMenu();

            // 1. 编辑与重命名
            var editProp = new MenuItem { Header = TaskFlow.Resources.Strings.TaskProp_EditTitle, Tag = "EditProperty" };
            editProp.Click += EditTaskProperty_Click;
            menu.Items.Add(editProp);

            var rename = new MenuItem { Header = TaskFlow.Resources.Strings.Common_Rename, Tag = "Rename" };
            rename.Click += RenameTask_Click;
            menu.Items.Add(rename);

            menu.Items.Add(new Separator { Tag = "EditSeparator" });

            // 2. 剪贴板操作
            var copy = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_Copy, Tag = "Copy" };
            copy.Click += CopyTask_Click;
            menu.Items.Add(copy);

            var paste = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_Paste, Tag = "Paste" };
            paste.Click += PasteTask_Click;
            menu.Items.Add(paste);

            menu.Items.Add(new Separator());

            // 3. 在下方添加任务
            var addBelow = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_AddTaskBelow };

            // 通用任务
            var general = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_General };
            foreach (var (type, tag) in new[] {
                (TaskType.PauseTask, "PauseTask"), (TaskType.GetTimestamp, "GetTimestamp"),
                (TaskType.EndTask, "EndTask"), (TaskType.EndAllFlows, "EndAllFlows"), (TaskType.RestartFlow, "RestartFlow") })
            {
                var mi = new MenuItem { Header = TaskCardBase.GetTaskTypeName(type), Tag = tag };
                mi.Click += AddTaskBelow_Click;
                general.Items.Add(mi);
            }
            addBelow.Items.Add(general);

            // Win操作
            var windows = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_WinOps };
            foreach (var (type, tag) in new[] {
                (TaskType.WinLaunchApp, "WinLaunchApp"), (TaskType.WinScreenshot, "WinScreenshot"),
                (TaskType.WinClick, "WinClick"), (TaskType.WinCloseApp, "WinCloseApp"),
                (TaskType.WinUiAutomation, "WinUiAutomation"), (TaskType.WinSimulateInput, "WinSimulateInput"),
                (TaskType.WinSubtitle, "WinSubtitle"), (TaskType.WinFindFile, "WinFindFile"),
                (TaskType.WinTextInput, "WinTextInput"),
                (TaskType.InputCombo, "InputCombo"), (TaskType.EventListener, "EventListener"),
                (TaskType.ClipboardWatch, "ClipboardWatch") })
            {
                var mi = new MenuItem { Header = TaskCardBase.GetTaskTypeName(type), Tag = tag };
                mi.Click += AddTaskBelow_Click;
                windows.Items.Add(mi);
            }
            addBelow.Items.Add(windows);

            // ADB 操作
            var adb = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_AdbOps };
            foreach (var (type, tag) in new[] {
                (TaskType.AdbConnect, "AdbConnect"), (TaskType.AdbLaunchApp, "AdbLaunchApp"),
                (TaskType.AdbScreenshot, "AdbScreenshot"), (TaskType.AdbClick, "AdbClick"),
                (TaskType.AdbCloseApp, "AdbCloseApp"), (TaskType.AdbDisconnect, "AdbDisconnect") })
            {
                var mi = new MenuItem { Header = TaskCardBase.GetTaskTypeName(type), Tag = tag };
                mi.Click += AddTaskBelow_Click;
                adb.Items.Add(mi);
            }
            addBelow.Items.Add(adb);

            // 图像处理
            var imgProc = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_ImgProc };
            foreach (var (type, tag) in new[] {
                (TaskType.ImgCrop, "ImgCrop"), (TaskType.ImgTemplateMatch, "ImgTemplateMatch"),
                (TaskType.ImgOnnxDetect, "ImgOnnxDetect"), (TaskType.ImgCaliperMeasure, "ImgCaliperMeasure"),
                (TaskType.ImgOcr, "ImgOcr"), (TaskType.ImgColorDetect, "ImgColorDetect"),
                (TaskType.ImgColorSegment, "ImgColorSegment"),
                (TaskType.ImgPreprocess, "ImgPreprocess"), (TaskType.ImgBlobAnalysis, "ImgBlobAnalysis"),
                (TaskType.ImgResize, "ImgResize") })
            {
                var mi = new MenuItem { Header = TaskCardBase.GetTaskTypeName(type), Tag = tag };
                mi.Click += AddTaskBelow_Click;
                imgProc.Items.Add(mi);
            }
            addBelow.Items.Add(imgProc);

            // 数据处理
            var dataProc = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_DataProc };
            foreach (var (type, tag) in new[] {
                (TaskType.ExpressionEval, "ExpressionEval"), (TaskType.StringSubstring, "StringSubstring"),
                (TaskType.TypeConvert, "TypeConvert"), (TaskType.ArrayParse, "ArrayParse"),
                (TaskType.ArrayBuilder, "ArrayBuilder"), (TaskType.FileRead, "FileRead"),
                (TaskType.ArraySearch, "ArraySearch"), (TaskType.CustomScript, "CustomScript") })
            {
                var mi = new MenuItem { Header = TaskCardBase.GetTaskTypeName(type), Tag = tag };
                mi.Click += AddTaskBelow_Click;
                dataProc.Items.Add(mi);
            }
            addBelow.Items.Add(dataProc);

            // Web操作
            var browserOps = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_BrowserOps };
            foreach (var (type, tag) in new[] {
                (TaskType.BrowserGetText, "BrowserGetText"),
                (TaskType.BrowserExecuteJs, "BrowserExecuteJs"),
                (TaskType.BrowserWaitForElement, "BrowserWaitForElement"),
                (TaskType.BrowserNativeClick, "BrowserNativeClick"),
                (TaskType.BrowserNativeInput, "BrowserNativeInput"),
                (TaskType.BrowserSimulatedClick, "BrowserSimulatedClick"),
                (TaskType.BrowserCdpCommand, "BrowserCdpCommand"),
                (TaskType.BrowserScreenshot, "BrowserScreenshot"),
                (TaskType.HttpRequest, "HttpRequest") })
            {
                var mi = new MenuItem { Header = TaskCardBase.GetTaskTypeName(type), Tag = tag };
                mi.Click += AddTaskBelow_Click;
                browserOps.Items.Add(mi);
            }
            addBelow.Items.Add(browserOps);

            // AI 操作
            var aiOps = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_AiOps };
            foreach (var (type, tag) in new[] {
                (TaskType.LlmTranslate, "LlmTranslate"), (TaskType.LlmVision, "LlmVision"),
                (TaskType.LlmFileTranslate, "LlmFileTranslate") })
            {
                var mi = new MenuItem { Header = TaskCardBase.GetTaskTypeName(type), Tag = tag };
                mi.Click += AddTaskBelow_Click;
                aiOps.Items.Add(mi);
            }
            addBelow.Items.Add(aiOps);

            // 控制流
            var controlFlow = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_ControlFlow };
            var breakLoop = new MenuItem { Header = TaskFlow.Resources.Strings.TaskType_BreakLoop, Tag = "BreakLoop" };
            breakLoop.Click += AddTaskBelow_Click;
            controlFlow.Items.Add(breakLoop);
            
            foreach (var (type, tag) in new[] {
                (TaskType.AutoRouteTracker, "AutoRouteTracker"),
                (TaskType.OcrKeywordAnchor, "OcrKeywordAnchor"),
                (TaskType.AutoRouteAdvance, "AutoRouteAdvance") })
            {
                var mi = new MenuItem { Header = TaskCardBase.GetTaskTypeName(type), Tag = tag };
                mi.Click += AddTaskBelow_Click;
                controlFlow.Items.Add(mi);
            }
            var ifElse = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_IfElseBranch, Tag = "IfElse" };
            ifElse.Click += AddBranchBelow_Click;
            controlFlow.Items.Add(ifElse);
            var forLoop = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_ForLoop, Tag = "ForLoop" };
            forLoop.Click += AddBranchBelow_Click;
            controlFlow.Items.Add(forLoop);
            
            controlFlow.Items.Add(new Separator());
            var callSubFlow = new MenuItem { Header = TaskCardBase.GetTaskTypeName(TaskType.CallSubFlow), Tag = "CallSubFlow" };
            callSubFlow.Click += AddTaskBelow_Click;
            controlFlow.Items.Add(callSubFlow);
            
            var subFlowOutput = new MenuItem { Header = TaskCardBase.GetTaskTypeName(TaskType.SubFlowOutput), Tag = "SubFlowOutput" };
            subFlowOutput.Click += AddTaskBelow_Click;
            controlFlow.Items.Add(subFlowOutput);
            
            addBelow.Items.Add(controlFlow);

            menu.Items.Add(addBelow);

            menu.Items.Add(new Separator { Tag = "ElifSeparator" });

            var addElif = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_AddElifBranch, Tag = "AddElif" };
            addElif.Click += AddElifBranch_Click;
            menu.Items.Add(addElif);

            var toggleElse = new MenuItem { Header = TaskFlow.Resources.Strings.Menu_ShowElse, Tag = "ToggleElse" };
            toggleElse.Click += ToggleElseVisibility_Click;
            menu.Items.Add(toggleElse);

            menu.Items.Add(new Separator());

            // 4. 辅助工具
            var search = new MenuItem { Header = TaskFlow.Resources.Strings.Main_SearchTask, Tag = "Search" };
            search.Click += SearchTask_Click;
            menu.Items.Add(search);

            var help = new MenuItem { Header = TaskFlow.Resources.Strings.Common_Help, Tag = "Help" };
            help.Click += OpenTaskHelp_Click;
            menu.Items.Add(help);

            menu.Items.Add(new Separator { Tag = "DeleteSeparator" });

            // 5. 危险操作
            var delete = new MenuItem { Header = TaskFlow.Resources.Strings.Common_Delete, Tag = "Delete", Foreground = ErrorBrush };
            delete.Click += DeleteTask_Click;
            menu.Items.Add(delete);

            return menu;
        }

        private void TaskList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // 运行中或Orchid执行中禁止弹出任务列表右键菜单
            if (ViewModel.IsBusy)
            {
                e.Handled = true;
                return;
            }

            if (sender is FrameworkElement fe && fe.ContextMenu != null)
            {
                UpdateSubFlowMenuItemsVisibility(fe.ContextMenu);

                foreach (var item in fe.ContextMenu.Items)
                {
                    if (item is MenuItem menuItem && (menuItem.Tag as string) == "Paste")
                    {
                        menuItem.IsEnabled = ViewModel.HasCopiedTask;
                    }
                }
            }
        }

        private void UpdateSubFlowMenuItemsVisibility(ContextMenu menu)
        {
            var isSubFlow = ViewModel.SelectedTab?.Type == FlowType.SubFlow;
            
            // 根据逻辑寻找菜单项而不是FindName
            foreach (var item in menu.Items)
            {
                if (item is MenuItem mainMenuItem && mainMenuItem.Header is string header && 
                   (header == TaskFlow.Resources.Strings.Menu_AddTaskBelow || header == TaskFlow.Resources.Strings.Main_AddTask))
                {
                    foreach (var subItem in mainMenuItem.Items)
                    {
                        if (subItem is MenuItem controlFlow && controlFlow.Header is string ch && ch == TaskFlow.Resources.Strings.Menu_ControlFlow)
                        {
                            foreach (var controlFlowItem in controlFlow.Items)
                            {
                                if (controlFlowItem is MenuItem ci)
                                {
                                    string? tag = ci.Tag as string ?? ci.Name;
                                    if (tag == "CallSubFlow" || tag == "MenuCallSubFlow")
                                        ci.Visibility = isSubFlow ? Visibility.Collapsed : Visibility.Visible;
                                    if (tag == "SubFlowOutput" || tag == "MenuSubFlowOutput")
                                        ci.Visibility = isSubFlow ? Visibility.Visible : Visibility.Collapsed;
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion
    }
}

