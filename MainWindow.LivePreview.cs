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
    // 实时预览功能
    public partial class MainWindow
    {
        #region 实时预览

        // 实时预览相关字段
        private System.Windows.Threading.DispatcherTimer? _livePreviewTimer;
        private readonly Services.ScreenshotService _liveScreenshotService = new();
        private bool _isLiveCapturing; // 防重入标志
        private string _liveProcessName = string.Empty; // 要实时显示的进程名
        private System.Windows.Media.Imaging.WriteableBitmap? _liveWriteableBitmap; // 复用内存

        /// <summary>
        /// 设置按钮：弹出对话框设置进程名
        /// </summary>
        private void LivePreviewSettings_Click(object sender, RoutedEventArgs e)
        {
            // 如果正在实时显示中，先停止
            if (_livePreviewTimer != null)
            {
                StopLivePreview();
            }

            // 创建与 AddVariableDialog 同款风格的对话框
            var dialog = new Window
            {
                Title = TaskFlow.Resources.Strings.Live_SettingsTitle,
                Width = 350,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent
            };

            var grid = new Grid { Margin = new Thickness(24) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标题层
            var titlePanel = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var titleLabel = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Live_SettingsTitle,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleLabel.SetResourceReference(TextBlock.ForegroundProperty, "DialogTitleBrush");
            Grid.SetColumn(titleLabel, 0);
            titlePanel.Children.Add(titleLabel);

            var closeBtn = new Button
            {
                Content = "✕", Width = 36, Height = 36, Background = System.Windows.Media.Brushes.Transparent, 
                BorderThickness = new Thickness(0),
                FontSize = 18, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -8, -8, 0)
            };
            closeBtn.SetResourceReference(Control.ForegroundProperty, "TextSecondaryBrush");
            closeBtn.Click += (s, args) => dialog.DialogResult = false;
            
            var closeTemplate = new ControlTemplate(typeof(Button));
            var closeBorderFact = new FrameworkElementFactory(typeof(Border));
            closeBorderFact.Name = "Bd";
            closeBorderFact.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
            closeBorderFact.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var closeContentFact = new FrameworkElementFactory(typeof(ContentPresenter));
            closeContentFact.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            closeContentFact.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            closeContentFact.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2));
            closeBorderFact.AppendChild(closeContentFact);
            closeTemplate.VisualTree = closeBorderFact;

            var closeHoverTrig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            closeHoverTrig.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SurfaceHoverBrush"), "Bd"));
            closeTemplate.Triggers.Add(closeHoverTrig);
            closeBtn.Template = closeTemplate;

            Grid.SetColumn(closeBtn, 1);
            titlePanel.Children.Add(closeBtn);
            
            Grid.SetRow(titlePanel, 0);
            grid.Children.Add(titlePanel);

            // 标签
            var label = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Live_ProcessName,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetRow(label, 1);
            grid.Children.Add(label);

            // 输入框（圆角与整体风格一致）
            var inputBox = new TextBox
            {
                Text = _liveProcessName,
                FontSize = 13,
                Padding = new Thickness(8, 6, 8, 6),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 12)
            };
            inputBox.SetResourceReference(Control.BackgroundProperty, "AppBackgroundBrush");
            inputBox.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            inputBox.SetResourceReference(Control.BorderBrushProperty, "BorderLightBrush");

            // 设置圆角模板
            var inputTemplate = new ControlTemplate(typeof(TextBox));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(TextBox.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(TextBox.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(TextBox.BorderThicknessProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(TextBox.PaddingProperty));
            var scrollFactory = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollFactory.Name = "PART_ContentHost";
            scrollFactory.SetValue(ScrollViewer.FocusableProperty, false);
            borderFactory.AppendChild(scrollFactory);
            inputTemplate.VisualTree = borderFactory;
            inputBox.Template = inputTemplate;
            Grid.SetRow(inputBox, 2);
            grid.Children.Add(inputBox);

            // 提示文本
            var hint = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Live_ProcessHint,
                FontSize = 11
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            Grid.SetRow(hint, 3);
            grid.Children.Add(hint);

            // 按钮区（确定 + 取消）
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(btnPanel, 5);

            // 确定按钮
            var btnOk = new Button
            {
                Content = TaskFlow.Resources.Strings.Common_OK,
                Padding = new Thickness(24, 8, 24, 8),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 13
            };
            btnOk.SetResourceReference(Control.BackgroundProperty, "PrimaryButtonBgBrush");
            btnOk.SetResourceReference(Control.ForegroundProperty, "PrimaryButtonTextBrush");

            var btnTemplate = new ControlTemplate(typeof(Button));
            var btnBorderFact = new FrameworkElementFactory(typeof(Border));
            btnBorderFact.Name = "Bd";
            btnBorderFact.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            btnBorderFact.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            btnBorderFact.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var btnContentFact = new FrameworkElementFactory(typeof(ContentPresenter));
            btnContentFact.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            btnContentFact.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            btnBorderFact.AppendChild(btnContentFact);
            btnTemplate.VisualTree = btnBorderFact;

            var btnHoverTrig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            btnHoverTrig.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("PrimaryButtonHoverBrush"), "Bd"));
            btnTemplate.Triggers.Add(btnHoverTrig);
            btnOk.Template = btnTemplate;

            btnOk.IsDefault = true;
            btnOk.Click += (s, args) => { dialog.DialogResult = true; };
            btnPanel.Children.Add(btnOk);

            grid.Children.Add(btnPanel);
            
            var border = new Border
            {
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(16),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Opacity = 0.18,
                    Color = System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x19) // Shadow can stay hardcoded or we can use generic shadow if any
                },
                Child = grid
            };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");


            dialog.Content = border;
            
            // 允许拖动整个窗口
            dialog.MouseLeftButtonDown += (s, args) =>
            {
                if (args.ButtonState == MouseButtonState.Pressed)
                    dialog.DragMove();
            };

            // 焦点到输入框
            dialog.Loaded += (s, args) =>
            {
                inputBox.Focus();
                inputBox.SelectAll();
            };

            if (dialog.ShowDialog() == true)
            {
                _liveProcessName = inputBox.Text.Trim();
                // 更新提示文本和标签
                if (!string.IsNullOrEmpty(_liveProcessName))
                {
                    LivePreviewHint.Text = $"{_liveProcessName}  |  ▶";
                    LiveTabButton.Content = string.Format(TaskFlow.Resources.Strings.Live_DisplayWithName, _liveProcessName);
                }
                else
                {
                    LivePreviewHint.Text = TaskFlow.Resources.Strings.Main_LiveStartHint;
                    LiveTabButton.Content = TaskFlow.Resources.Strings.Live_Display;
                }
                LivePreviewHint.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 开始实时显示
        /// </summary>
        private async void LivePreviewStart_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_liveProcessName))
            {
                AnthropicMessageDialog.ShowWarning(TaskFlow.Resources.Strings.Common_Tip, TaskFlow.Resources.Strings.Msg_LiveSetProcess, this);
                return;
            }

            // Orchid 运行时禁止启动实时显示（避免截图冲突）
            if (ViewModel.AiFlowVm.IsGenerating || ViewModel.AiFlowVm.IsAiExecuting)
            {
                AnthropicMessageDialog.ShowWarning(TaskFlow.Resources.Strings.Common_Tip,
                    "Orchid 正在运行中，请等待其完成后再启动实时显示。", this);
                return;
            }

            // 先做一次试截图，验证进程是否有效
            var testResult = await _liveScreenshotService.CaptureWindowAsync(_liveProcessName);
            if (!testResult.Success)
            {
                AnthropicMessageDialog.ShowError(TaskFlow.Resources.Strings.Live_Display, string.Format(TaskFlow.Resources.Strings.Msg_LiveCannotCapture, testResult.Error), this);
                testResult.Image?.Dispose();
                return;
            }

            // 显示第一帧，隐藏提示文本
            UpdateLivePreviewImage(testResult.Image!);
            testResult.Image?.Dispose();
            LivePreviewHint.Visibility = Visibility.Collapsed;

            // 切换按钮状态
            BtnLiveStart.Visibility = Visibility.Collapsed;
            BtnLiveStop.Visibility = Visibility.Visible;

            // 捕获进程名用于闭包
            string processName = _liveProcessName;

            // 启动定时器（500ms 间隔，降低 CPU 占用）
            _livePreviewTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _livePreviewTimer.Tick += async (s, args) =>
            {

                if (_isLiveCapturing) return; // 防重入
                _isLiveCapturing = true;

                try
                {
                    // 在后台线程执行截图和像素数据提取
                    var frameInfo = await System.Threading.Tasks.Task.Run(() =>
                    {
                        var result = _liveScreenshotService.CaptureWindowAsync(processName).Result;
                        if (!result.Success || result.Image == null)
                        {
                            result.Image?.Dispose();
                            return ((byte[]?)null, 0, 0, 0);
                        }

                        var mat = result.Image;
                        if (!mat.IsContinuous())
                        {
                            var contMat = mat.Clone();
                            mat.Dispose();
                            mat = contMat;
                        }

                        int w = mat.Cols;
                        int h = mat.Rows;
                        int ch = mat.Channels();
                        int byteCount = w * h * ch;
                        var pixels = new byte[byteCount];
                        System.Runtime.InteropServices.Marshal.Copy(mat.Data, pixels, 0, byteCount);
                        mat.Dispose();

                        return ((byte[]?)pixels, w, h, ch);
                    });

                    // 仅在 UI 线程写入像素数据
                    if (frameInfo.Item1 != null)
                    {
                        UpdateLivePreviewFromBytes(frameInfo.Item1, frameInfo.Item2, frameInfo.Item3, frameInfo.Item4);
                    }
                }
                catch { /* 忽略截图异常 */ }
                finally
                {
                    _isLiveCapturing = false;
                }
            };
            _livePreviewTimer.Start();
        }

        /// <summary>
        /// 停止实时显示
        /// </summary>
        private void LivePreviewStop_Click(object sender, RoutedEventArgs e)
        {
            StopLivePreview();
        }

        /// <summary>
        /// 停止实时预览（内部方法，支持外部调用）
        /// </summary>
        private void StopLivePreview()
        {
            if (_livePreviewTimer != null)
            {
                _livePreviewTimer.Stop();
                _livePreviewTimer = null;
            }

            _isLiveCapturing = false;
            BtnLiveStart.Visibility = Visibility.Visible;
            BtnLiveStop.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 将 OpenCV Mat 转为 BitmapSource 并设置到实时预览图
        /// </summary>
        private void UpdateLivePreviewImage(OpenCvSharp.Mat mat)
        {
            try
            {
                int width = mat.Cols;
                int height = mat.Rows;
                int channels = mat.Channels();
                int stride = width * channels;

                if (!mat.IsContinuous())
                {
                    using var contMat = mat.Clone();
                    UpdateLivePreviewImage(contMat);
                    return;
                }

                // 用于第一帧（试截图），后续帧用 UpdateLivePreviewFromBytes
                byte[] pixels = new byte[height * stride];
                System.Runtime.InteropServices.Marshal.Copy(mat.Data, pixels, 0, pixels.Length);
                UpdateLivePreviewFromBytes(pixels, width, height, channels);
            }
            catch { /* 忽略图像转换异常 */ }
        }

        /// <summary>
        /// 使用 WriteableBitmap 复用内存更新实时预览图像
        /// </summary>
        private void UpdateLivePreviewFromBytes(byte[] pixels, int width, int height, int channels)
        {
            try
            {
                int stride = width * channels;
                var pixelFormat = channels == 4
                    ? System.Windows.Media.PixelFormats.Bgra32
                    : System.Windows.Media.PixelFormats.Bgr24;

                // 检查 WriteableBitmap 是否可复用（尺寸和格式一致）
                if (_liveWriteableBitmap == null ||
                    _liveWriteableBitmap.PixelWidth != width ||
                    _liveWriteableBitmap.PixelHeight != height ||
                    _liveWriteableBitmap.Format != pixelFormat)
                {
                    _liveWriteableBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                        width, height, 96, 96, pixelFormat, null);
                    LivePreviewImage.Source = _liveWriteableBitmap;
                }

                // 直接写入像素数据，避免创建新的 BitmapSource
                _liveWriteableBitmap.WritePixels(
                    new System.Windows.Int32Rect(0, 0, width, height),
                    pixels, stride, 0);
            }
            catch { /* 忽略图像转换异常 */ }
        }

        #endregion

        #region 实时预览 - 鼠标缩放与坐标

        /// <summary>
        /// 实时预览图像 - 鼠标移动时显示坐标
        /// </summary>
        private void LivePreviewImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is Image image && image.Source != null)
            {
                var pos = e.GetPosition(image);
                int pixelX = (int)pos.X;
                int pixelY = (int)pos.Y;

                if (pixelX >= 0 && pixelX < (int)image.Source.Width &&
                    pixelY >= 0 && pixelY < (int)image.Source.Height)
                {
                    ImageCoordinateText.Text = $"X: {pixelX}  Y: {pixelY}";
                }
                else
                {
                    ImageCoordinateText.Text = "";
                }
            }
        }

        /// <summary>
        /// 实时预览图像 - 鼠标离开
        /// </summary>
        private void LivePreviewImage_MouseLeave(object sender, MouseEventArgs e)
        {
            ImageCoordinateText.Text = "";
        }

        /// <summary>
        /// 实时预览图像 - 鼠标滚轮缩放
        /// </summary>
        private void LivePreviewImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var mousePos = e.GetPosition(LiveScrollViewer);

            double currentScale = LiveImageScaleTransform.ScaleX;
            double contentX = (LiveScrollViewer.HorizontalOffset + mousePos.X) / currentScale;
            double contentY = (LiveScrollViewer.VerticalOffset + mousePos.Y) / currentScale;

            double step = Math.Max(0.01, currentScale * 0.1);
            double newScale = e.Delta > 0 ? currentScale + step : currentScale - step;

            // 限制缩放范围
            newScale = Math.Max(0.05, Math.Min(10.0, newScale));

            LiveImageScaleTransform.ScaleX = newScale;
            LiveImageScaleTransform.ScaleY = newScale;

            LiveScrollViewer.UpdateLayout();

            double newHOffset = contentX * newScale - mousePos.X;
            double newVOffset = contentY * newScale - mousePos.Y;
            LiveScrollViewer.ScrollToHorizontalOffset(Math.Max(0, newHOffset));
            LiveScrollViewer.ScrollToVerticalOffset(Math.Max(0, newVOffset));

            e.Handled = true;
        }

        #endregion

        #region 实时预览 - 右键菜单

        /// <summary>
        /// 保存实时预览图像
        /// </summary>
        private void SaveLiveImage_Click(object sender, RoutedEventArgs e)
        {
            if (LivePreviewImage.Source == null)
            {
                AnthropicMessageDialog.ShowWarning(TaskFlow.Resources.Strings.Common_Tip, TaskFlow.Resources.Strings.Msg_NoImageToSave, this);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = $"{TaskFlow.Resources.Strings.Filter_PngImage}|{TaskFlow.Resources.Strings.Filter_JpegImage}|{TaskFlow.Resources.Strings.Filter_BmpImage}",
                DefaultExt = ".png",
                FileName = $"LivePreview_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
                        (System.Windows.Media.Imaging.BitmapSource)LivePreviewImage.Source));
                    using var stream = System.IO.File.Create(dialog.FileName);
                    encoder.Save(stream);
                    ViewModel.AddLog(string.Format(TaskFlow.Resources.Strings.Msg_LiveSaved, dialog.FileName));
                }
                catch (Exception ex)
                {
                    AnthropicMessageDialog.ShowError(TaskFlow.Resources.Strings.Common_Error, string.Format(TaskFlow.Resources.Strings.Msg_SaveFailed, ex.Message), this);
                }
            }
        }

        /// <summary>
        /// 还原实时预览图像大小
        /// </summary>
        private void ResetLiveImageSize_Click(object sender, RoutedEventArgs e)
        {
            // 计算 FitScale 让图像自适应窗口
            if (LivePreviewImage.Source != null)
            {
                double viewWidth = LiveScrollViewer.ViewportWidth;
                double viewHeight = LiveScrollViewer.ViewportHeight;
                double imgWidth = LivePreviewImage.Source.Width;
                double imgHeight = LivePreviewImage.Source.Height;

                if (imgWidth > 0 && imgHeight > 0 && viewWidth > 0 && viewHeight > 0)
                {
                    double fitScale = Math.Min(viewWidth / imgWidth, viewHeight / imgHeight);
                    LiveImageScaleTransform.ScaleX = fitScale;
                    LiveImageScaleTransform.ScaleY = fitScale;
                    return;
                }
            }

            // 回退：没有图像时重置为 1:1
            LiveImageScaleTransform.ScaleX = 1;
            LiveImageScaleTransform.ScaleY = 1;
        }

        #endregion
    }
}
