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
    // 标签页切换 + 输出面板 + 标签指示器动画
    public partial class MainWindow
    {
        #region Tab Switching and Output Panel

        private void TabButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton radio) return;
            if (LogPanel == null || OutputPanel == null) return;

            var tag = radio.Tag?.ToString();
            if (tag == "Log")
            {
                LogPanel.Visibility = Visibility.Visible;
                OutputPanel.Visibility = Visibility.Collapsed;
            }
            else if (tag == "Output")
            {
                LogPanel.Visibility = Visibility.Collapsed;
                OutputPanel.Visibility = Visibility.Visible;
                UpdateOutputPanel();
            }

            // 触发下方标签滑动指示条动画
            if (BottomTabIndicator != null && BottomTabContainer != null)
            {
                AnimateIndicator(BottomTabIndicator, BottomTabIndicatorTransform, radio, BottomTabContainer);
            }
        }

        private TaskCardBase? _subscribedTask;

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // 初始化右侧标签指示条位置（无动画）
            Dispatcher.BeginInvoke(() =>
            {
                if (PreviewTabButton != null && TopTabIndicator != null)
                    SetIndicatorImmediate(TopTabIndicator, TopTabIndicatorTransform, PreviewTabButton, TopTabContainer);
                if (LogTabButton != null && BottomTabIndicator != null)
                    SetIndicatorImmediate(BottomTabIndicator, BottomTabIndicatorTransform, LogTabButton, BottomTabContainer);
                InitFlowTabIndicator();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
            // 监听SelectedTask变化以更新输出面板
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.SelectedTask))
                    {
                        SubscribeToSelectedTask(vm.SelectedTask);
                        UpdateOutputPanel();
                    }
                };

                // 应用设置（日志行数等）
                vm.ApplySettings(vm.Settings);

                // 启动时自动加载上次项目
                if (vm.Settings.AutoLoadLastProject)
                {
                    _ = vm.AutoLoad();
                }

                // 启动后立即隐藏窗口（优先处理）
                if (vm.Settings.HideOnStartup)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        HideToTray_Click(this, new RoutedEventArgs());

                        // 隐藏后再根据设置决定是否运行
                        if (vm.Settings.RunAllOnStartup && vm.TaskCards.Count > 0)
                        {
                            Dispatcher.BeginInvoke(async () =>
                            {
                                await Task.Delay(500);
                                if (vm.RunAllCommand.CanExecute(null))
                                    vm.RunAllCommand.Execute(null);
                            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        }
                    }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
                // 不隐藏时，仅检查是否需要自动运行
                else if (vm.Settings.RunAllOnStartup && vm.TaskCards.Count > 0)
                {
                    Dispatcher.BeginInvoke(async () =>
                    {
                        await Task.Delay(500); // 等待 UI 完全加载
                        if (vm.RunAllCommand.CanExecute(null))
                            vm.RunAllCommand.Execute(null);
                    }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
            }
        }

        private void SubscribeToSelectedTask(TaskCardBase? task)
        {
            // 取消订阅旧任务
            if (_subscribedTask != null)
            {
                _subscribedTask.PropertyChanged -= SelectedTask_PropertyChanged;
            }

            _subscribedTask = task;

            // 订阅新任务的属性变化（执行后输出会更新）
            if (_subscribedTask != null)
            {
                _subscribedTask.PropertyChanged += SelectedTask_PropertyChanged;
            }
        }

        private void SelectedTask_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 当选中任务的输出相关属性变化时，防抖刷新面板
            if (e.PropertyName is nameof(TaskCardBase.OutputResult) or
                nameof(TaskCardBase.OutputText) or
                nameof(TaskCardBase.OutputX) or
                nameof(TaskCardBase.OutputY) or
                nameof(TaskCardBase.OutputLoopIndex) or
                nameof(TaskCardBase.ExecutionDuration) or
                nameof(TaskCardBase.Status) or
                nameof(TaskCardBase.ErrorMessage) or
                nameof(ArrayBuilderTaskCard.OutputArrayCount) or
                nameof(ArrayBuilderTaskCard.OutputSavePath) or
                nameof(LlmFileTranslateTaskCard.OutputTranslatedFilePath) or
                nameof(FileReadTaskCard.OutputArrayCount) or
                nameof(ArraySearchTaskCard.OutputMatchIndex) or
                nameof(ArraySearchTaskCard.OutputMatchValue) or
                nameof(WinFindFileTaskCard.OutputFilePath) or
                nameof(ImgResizeTaskCard.OutputWidthScale) or
                nameof(ImgResizeTaskCard.OutputHeightScale) or
                nameof(WinScreenshotTaskCard.OutputResolution) or
                nameof(WinScreenshotTaskCard.OutputWidth) or
                nameof(WinScreenshotTaskCard.OutputHeight) or
                nameof(CustomScriptTaskCard.OutputLog))
            {
                Dispatcher.BeginInvoke(() => ScheduleOutputPanelUpdate());
            }
        }

        /// <summary>
        /// 输出面板防抖更新（100ms 内多次属性变化合并为一次刷新）
        /// </summary>
        private void ScheduleOutputPanelUpdate()
        {
            if (_outputPanelDebounceTimer == null)
            {
                _outputPanelDebounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _outputPanelDebounceTimer.Tick += (s, e) =>
                {
                    _outputPanelDebounceTimer.Stop();
                    UpdateOutputPanel();
                };
            }
            _outputPanelDebounceTimer.Stop();
            _outputPanelDebounceTimer.Start();
        }

        private void UpdateOutputPanel()
        {
            if (OutputInfoPanel == null) return;
            if (OutputPanel.Visibility != Visibility.Visible) return;

            OutputInfoPanel.Children.Clear();

            var task = ViewModel.SelectedTask;
            if (task == null)
            {
                // 显示居中提示，隐藏空的输出面板
                if (OutputEmptyHint != null) OutputEmptyHint.Visibility = Visibility.Visible;
                return;
            }

            // 有数据时隐藏空状态提示
            if (OutputEmptyHint != null) OutputEmptyHint.Visibility = Visibility.Collapsed;

            // 卡片名称
            AddOutputRow(TaskFlow.Resources.Strings.Output_CardName, $"#{task.Order} {task.Name}");

            // 卡片类型
            AddOutputRow(TaskFlow.Resources.Strings.Output_CardType, TaskCardBase.GetTaskTypeName(task.TaskType));

            // 执行状态
            string statusText = task.Status switch
            {
                Models.TaskCards.TaskStatus.Idle => TaskFlow.Resources.Strings.Output_StatusIdle,
                Models.TaskCards.TaskStatus.Running => TaskFlow.Resources.Strings.Output_StatusRunning,
                Models.TaskCards.TaskStatus.Success => TaskFlow.Resources.Strings.Output_StatusSuccess,
                Models.TaskCards.TaskStatus.Failed => TaskFlow.Resources.Strings.Output_StatusFailed,
                _ => task.Status.ToString()
            };
            AddOutputRow(TaskFlow.Resources.Strings.Output_Status, statusText);

            // 运行耗时
            if (task.ExecutionDuration.HasValue)
            {
                var dur = task.ExecutionDuration.Value;
                string durText = dur.TotalSeconds >= 1
                    ? string.Format(TaskFlow.Resources.Strings.Output_Seconds, dur.TotalSeconds.ToString("F2"))
                    : string.Format(TaskFlow.Resources.Strings.Output_Milliseconds, dur.TotalMilliseconds.ToString("F0"));
                AddOutputRow(TaskFlow.Resources.Strings.Output_Duration, durText);
            }

            // 分隔线
            OutputInfoPanel.Children.Add(new Separator
            {
                Margin = new Thickness(0, 8, 0, 8),
                Background = SeparatorBrush
            });

            // 输出数据标题
            OutputInfoPanel.Children.Add(new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Output_Data,
                Foreground = LabelBrush,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontTitle"),
                Margin = new Thickness(0, 0, 0, 6)
            });

            // OutputResult
            if (task.OutputResult.HasValue)
                AddOutputRow(TaskFlow.Resources.Strings.Output_Result, task.OutputResult.Value ? "True" : "False");

            // OutputText（模板匹配卡片跳过）
            if (!string.IsNullOrEmpty(task.OutputText) && task is not ImgTemplateMatchTaskCard)
                AddOutputRow(TaskFlow.Resources.Strings.Output_Text, task.OutputText);

            // OutputX / OutputY（模板匹配和数组解析卡片跳过）
            if ((task.OutputX.HasValue || task.OutputY.HasValue) && task is not ImgTemplateMatchTaskCard && task is not ArrayParseTaskCard)
                AddOutputRow(TaskFlow.Resources.Strings.Output_Coords, $"({task.OutputX ?? 0}, {task.OutputY ?? 0})");

            // OutputLoopIndex
            if (task.OutputLoopIndex.HasValue)
                AddOutputRow(TaskFlow.Resources.Strings.Output_LoopIndex, task.OutputLoopIndex.Value.ToString());

            // ForLoop specific: CurrentLoopIndex
            if (task is ForLoopTaskCard forCard)
            {
                AddOutputRow(TaskFlow.Resources.Strings.Output_CurrentLoop, $"{forCard.CurrentLoopIndex} / {forCard.LoopCount}");
            }

            // ImgColorDetect specific
            if (task is ImgColorDetectTaskCard colorCard)
            {
                AddOutputRow(TaskFlow.Resources.Strings.Output_MatchRatio, $"{colorCard.OutputMatchRatio:P2}");
            }

            // ImgTemplateMatch specific: 显示匹配数量和数组
            if (task is ImgTemplateMatchTaskCard tmCard)
            {
                AddOutputRow(TaskFlow.Resources.Strings.Output_MatchCount, tmCard.OutputMatchCount.ToString());

                if (tmCard.OutputMatchResults.Count > 0)
                {
                    // 最佳匹配分数
                    var best = tmCard.OutputMatchResults.OrderByDescending(m => m.Score).First();
                    AddOutputRow(TaskFlow.Resources.Strings.Output_BestMatchScore, best.Score.ToString("F4"));

                    // 最佳匹配坐标
                    AddOutputRow(TaskFlow.Resources.Strings.Output_BestMatchCoords, $"({best.X},{best.Y})");

                    // 结果分数数组
                    var scores = string.Join(", ", tmCard.OutputMatchResults.Select(m => m.Score.ToString("F4")));
                    AddOutputRow(TaskFlow.Resources.Strings.Output_ScoreArray, $"[{scores}]");

                    // 匹配坐标数组
                    var coords = string.Join(", ", tmCard.OutputMatchResults.Select(m => $"({m.X},{m.Y})"));
                    AddOutputRow(TaskFlow.Resources.Strings.Output_CoordsArray, $"[{coords}]");
                }
            }

            // ImgBlobAnalysis specific: 显示Blob分析详细结果
            if (task is ImgBlobAnalysisTaskCard blobCard)
            {
                AddOutputRow(TaskFlow.Resources.Strings.Output_BlobCount, blobCard.OutputBlobCount.ToString());

                if (blobCard.OutputBlobResults.Count > 0)
                {
                    // 第一个 Blob 的中心坐标
                    var first = blobCard.OutputBlobResults[0];
                    AddOutputRow(TaskFlow.Resources.Strings.Output_FirstBlobCoords, $"({first.X},{first.Y})");

                    // 坐标数组
                    var coords = string.Join(", ", blobCard.OutputBlobResults.Select(b => $"({b.X},{b.Y})"));
                    AddOutputRow(TaskFlow.Resources.Strings.Output_BlobCoordsArray, $"[{coords}]");

                    // 尺寸数组
                    var sizes = string.Join(", ", blobCard.OutputBlobResults.Select(b => $"({b.Width}x{b.Height})"));
                    AddOutputRow(TaskFlow.Resources.Strings.Output_BlobSizeArray, $"[{sizes}]");

                    // 面积数组
                    var areas = string.Join(", ", blobCard.OutputBlobResults.Select(b => b.Area.ToString()));
                    AddOutputRow(TaskFlow.Resources.Strings.Output_BlobAreaArray, $"[{areas}]");
                }
            }

            // ImgResize specific: 显示宽度和高度缩放倍率
            if (task is ImgResizeTaskCard resizeCard && resizeCard.OutputImage != null)
            {
                AddOutputRow(TaskFlow.Resources.Strings.Output_WidthScale, resizeCard.OutputWidthScale.ToString("F4"));
                AddOutputRow(TaskFlow.Resources.Strings.Output_HeightScale, resizeCard.OutputHeightScale.ToString("F4"));
            }

            // WinScreenshot specific: 显示分辨率信息
            if (task is WinScreenshotTaskCard ssCard && !string.IsNullOrEmpty(ssCard.OutputResolution))
            {
                AddOutputRow(TaskFlow.Resources.Strings.Output_Resolution, ssCard.OutputResolution);
                AddOutputRow(TaskFlow.Resources.Strings.Output_ImgWidth, ssCard.OutputWidth.ToString());
                AddOutputRow(TaskFlow.Resources.Strings.Output_ImgHeight, ssCard.OutputHeight.ToString());
            }

            // TypeConvert specific: 转换结果
            if (task is TypeConvertTaskCard tcCard)
            {
                AddOutputRow(TaskFlow.Resources.Strings.Output_ConvertResult, tcCard.OutputIntValue.ToString());
            }

            // ArrayParse specific: 解析结果
            if (task is ArrayParseTaskCard apCard)
            {
                switch (apCard.ArrayDataType)
                {
                    case ArrayDataType.Int:
                        AddOutputRow(TaskFlow.Resources.Strings.Output_ParseResultInt, apCard.OutputIntValue.ToString());
                        break;
                    case ArrayDataType.String:
                        AddOutputRow(TaskFlow.Resources.Strings.Output_ParseResultString, apCard.OutputStringValue);
                        break;
                    case ArrayDataType.Coordinate:
                        AddOutputRow(TaskFlow.Resources.Strings.Output_ParseResultCoords, $"({apCard.OutputX ?? 0}, {apCard.OutputY ?? 0})");
                        break;
                    case ArrayDataType.Double:
                        AddOutputRow(TaskFlow.Resources.Strings.Output_ParseResultDouble, apCard.OutputDoubleValue.ToString("F4"));
                        break;
                }
            }

            // GetTimestamp specific: 当前时间
            if (task is GetTimestampTaskCard tsCard)
            {
                AddOutputRow(TaskFlow.Resources.Strings.Output_CurrentTime, tsCard.OutputTimestamp.ToString());
            }

            // ArrayBuilder specific: 数组当前容量、保存文件路径
            if (task is ArrayBuilderTaskCard abCard)
            {
                AddOutputRow(TaskFlow.Resources.Strings.AC_ArrayCapacity, abCard.OutputArrayCount.ToString());
                if (!string.IsNullOrEmpty(abCard.OutputSavePath))
                    AddOutputRow(TaskFlow.Resources.Strings.AC_SaveFilePath, abCard.OutputSavePath);
            }

            // LlmFileTranslate specific: 已翻译文件路径
            if (task is LlmFileTranslateTaskCard ftCard)
            {
                if (!string.IsNullOrEmpty(ftCard.OutputTranslatedFilePath))
                    AddOutputRow(TaskFlow.Resources.Strings.AC_TranslatedFilePath, ftCard.OutputTranslatedFilePath);
            }

            // FileRead specific: 数组元素数量
            if (task is FileReadTaskCard frCard)
            {
                AddOutputRow(TaskFlow.Resources.Strings.AC_FileReadArrayCount, frCard.OutputArrayCount.ToString());
            }

            // ArraySearch specific: 匹配索引和匹配值
            if (task is ArraySearchTaskCard asCard)
            {
                AddOutputRow(TaskFlow.Resources.Strings.AC_MatchIndex, asCard.OutputMatchIndex.ToString());
                if (!string.IsNullOrEmpty(asCard.OutputMatchValue))
                    AddOutputRow(TaskFlow.Resources.Strings.AC_MatchValue, asCard.OutputMatchValue);
            }

            // WinFindFile specific
            if (task is WinFindFileTaskCard findFileCard)
            {
                AddOutputRow(TaskFlow.Resources.Strings.AC_FilePath, findFileCard.OutputFilePath ?? "");
            }

            // CustomScript specific: 脚本输出日志
            if (task is CustomScriptTaskCard scriptCard)
            {
                if (!string.IsNullOrEmpty(scriptCard.OutputLog))
                    AddOutputRow(TaskFlow.Resources.Strings.TaskType_CustomScript, scriptCard.OutputLog);
            }

            // BrowserGetText / BrowserExecuteJs：取文本结果已通过通用 OutputText 行显示，无需额外行
            // BrowserWaitForElement：等待结果已通过通用 OutputResult 行显示，无需额外行

            // ErrorMessage
            if (!string.IsNullOrEmpty(task.ErrorMessage))
            {
                AddOutputRow(TaskFlow.Resources.Strings.Output_ErrorMessage, task.ErrorMessage, isError: true);
            }

            // 只要执行过成功，或者有任何输出字段，就算有输出（不再维护易遗漏的类型列表）
            bool hasOutput = task.Status == Models.TaskCards.TaskStatus.Success ||
                            task.Status == Models.TaskCards.TaskStatus.Failed ||
                            task.OutputResult.HasValue ||
                            !string.IsNullOrEmpty(task.OutputText) ||
                            task.OutputX.HasValue || task.OutputY.HasValue ||
                            task.OutputLoopIndex.HasValue ||
                            task is ArrayBuilderTaskCard || task is LlmFileTranslateTaskCard ||
                            task is FileReadTaskCard || task is ArraySearchTaskCard ||
                            task is WinScreenshotTaskCard || task is ImgResizeTaskCard ||
                            task is CustomScriptTaskCard;

            if (!hasOutput)
            {
                OutputInfoPanel.Children.Add(new TextBlock
                {
                    Text = TaskFlow.Resources.Strings.Output_NoData,
                    Foreground = HintBrush,
                    FontStyle = FontStyles.Italic,
                    FontSize = 11,
                    Margin = new Thickness(4, 4, 0, 0)
                });
            }
        }

        private void AddOutputRow(string label, string value, bool isError = false)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = LabelBrush,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(labelBlock, 0);

            var valueBox = new TextBox
            {
                Text = value,
                
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
                IsReadOnly = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0)
            };
            valueBox.SetResourceReference(TextBox.ForegroundProperty, isError ? "DangerButtonBgBrush" : "TextPrimaryBrush");
            Grid.SetColumn(valueBox, 1);

            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBox);
            OutputInfoPanel.Children.Add(grid);
        }

        #endregion

        #region 图像预览/实时显示 标签切换

        /// <summary>
        /// 图像预览 / 实时显示 标签切换
        /// </summary>
        private void ImageTabButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton radio) return;
            if (ImagePreviewPanel == null || LivePreviewPanel == null) return;

            var tag = radio.Tag?.ToString();
            if (tag == "Preview")
            {
                ImagePreviewPanel.Visibility = Visibility.Visible;
                LivePreviewPanel.Visibility = Visibility.Collapsed;
                // 切换到图像预览时暂停实时显示定时器
                _livePreviewTimer?.Stop();
            }
            else if (tag == "Live")
            {
                ImagePreviewPanel.Visibility = Visibility.Collapsed;
                LivePreviewPanel.Visibility = Visibility.Visible;
                // 切换回实时显示时恢复定时器
                _livePreviewTimer?.Start();
            }

            // 触发上方标签滑动指示条动画
            if (TopTabIndicator != null && TopTabContainer != null)
            {
                AnimateIndicator(TopTabIndicator, TopTabIndicatorTransform, radio, TopTabContainer);
            }
        }

        #endregion

        #region 标签滑动指示条动画

        /// <summary>
        /// 通用滑动指示条动画：将指示条平滑移动到目标控件位置
        /// </summary>
        private void AnimateIndicator(Border indicator, System.Windows.Media.TranslateTransform transform, UIElement target, UIElement container)
        {
            try
            {
                var pos = target.TranslatePoint(new Point(0, 0), container);
                double targetX = pos.X;
                double targetWidth = ((FrameworkElement)target).ActualWidth;

                if (targetWidth <= 0) return;

                var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
                var duration = TimeSpan.FromMilliseconds(130);

                // 动画化 X 位移
                var xAnim = new DoubleAnimation(targetX, duration) { EasingFunction = ease };
                transform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, xAnim);

                // 动画化宽度
                var wAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
                indicator.BeginAnimation(FrameworkElement.WidthProperty, wAnim);
            }
            catch { /* 忽略布局未完成时的异常 */ }
        }

        /// <summary>
        /// 无动画直接设置指示条位置（用于初始化）
        /// </summary>
        private void SetIndicatorImmediate(Border indicator, System.Windows.Media.TranslateTransform transform, UIElement target, UIElement container)
        {
            try
            {
                var pos = target.TranslatePoint(new Point(0, 0), container);
                transform.X = pos.X;
                indicator.Width = ((FrameworkElement)target).ActualWidth;
            }
            catch { /* 忽略布局未完成时的异常 */ }
        }

        /// <summary>
        /// 初始化左侧流程标签指示条到当前选中标签
        /// </summary>
        private void InitFlowTabIndicator()
        {
            if (FlowTabIndicator == null || FlowTabItemsControl == null) return;

            // 查找当前选中标签对应的 Button
            for (int i = 0; i < FlowTabItemsControl.Items.Count; i++)
            {
                var container = FlowTabItemsControl.ItemContainerGenerator.ContainerFromIndex(i);
                if (container == null) continue;

                var button = FindVisualChild<System.Windows.Controls.Button>(container);
                if (button?.Tag is WorkflowTab tab && tab.IsSelected)
                {
                    SetIndicatorImmediate(FlowTabIndicator, FlowTabIndicatorTransform, button, FlowTabItemsControl);
                    break;
                }
            }
        }

        #endregion
    }
}



