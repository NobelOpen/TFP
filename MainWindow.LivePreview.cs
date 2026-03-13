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
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFA, 0xF9, 0xF5))
            };

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标签
            var label = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Live_ProcessName,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x6B, 0x6A, 0x65)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            // 输入框（圆角与整体风格一致）
            var inputBox = new TextBox
            {
                Text = _liveProcessName,
                FontSize = 13,
                Padding = new Thickness(8, 6, 8, 6),
                Background = System.Windows.Media.Brushes.White,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x13)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE8, 0xE6, 0xDC)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 12)
            };
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
            Grid.SetRow(inputBox, 1);
            grid.Children.Add(inputBox);

            // 提示文本
            var hint = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Live_ProcessHint,
                Foreground = HintBrush,
                FontSize = 11
            };
            Grid.SetRow(hint, 2);
            grid.Children.Add(hint);

            // 按钮区（确定 + 取消）
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(btnPanel, 4);

            // 确定按钮（与 AddVariableDialog 一致的样式）
            var btnOk = CreateDialogButton(TaskFlow.Resources.Strings.Common_OK,
                System.Windows.Media.Color.FromRgb(0xD9, 0x77, 0x57),
                System.Windows.Media.Colors.White,
                System.Windows.Media.Color.FromRgb(0xE0, 0x88, 0x68));
            btnOk.Margin = new Thickness(0, 0, 8, 0);
            btnOk.IsDefault = true;
            btnOk.Click += (s, args) => { dialog.DialogResult = true; };
            btnPanel.Children.Add(btnOk);

            // 取消按钮（深色，与 AddVariableDialog 一致）
            var btnCancel = CreateDialogButton(TaskFlow.Resources.Strings.Common_Cancel,
                System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x13),
                System.Windows.Media.Color.FromRgb(0xFA, 0xF9, 0xF5),
                System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x28));
            btnCancel.IsCancel = true;
            btnCancel.Click += (s, args) => { dialog.DialogResult = false; };
            btnPanel.Children.Add(btnCancel);

            grid.Children.Add(btnPanel);
            dialog.Content = grid;

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

