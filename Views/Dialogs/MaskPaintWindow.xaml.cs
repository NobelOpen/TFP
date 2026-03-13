using System;
using TaskFlow.Resources;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OpenCvSharp;
using OpenCvSharp.Extensions;


namespace TaskFlow.Views.Dialogs
{
    public partial class MaskPaintWindow : System.Windows.Window
    {
        private readonly Mat _sourceImage;
        private readonly int _imageWidth;
        private readonly int _imageHeight;

        // 掩膜数据：白色(255)=保留，黑色(0)=遮蔽
        private Mat _maskMat;

        // 绘制状态
        private bool _isPainting;
        private bool _isErasing; // false=画笔（涂黑遮蔽），true=橡皮擦（擦除恢复）
        private int _brushSize = 20;

        // 已保存的掩膜路径
        public string? MaskPath { get; private set; }

        // 可选：加载已有掩膜
        private readonly string? _existingMaskPath;

        public MaskPaintWindow(Mat sourceImage, string? existingMaskPath = null)
        {
            InitializeComponent();
            ApplyLocalization();
            _sourceImage = sourceImage;
            _imageWidth = sourceImage.Width;
            _imageHeight = sourceImage.Height;
            _existingMaskPath = existingMaskPath;

            // 初始化掩膜（全白=全部保留）
            _maskMat = new Mat(_imageHeight, _imageWidth, MatType.CV_8UC1, new Scalar(255));

            // 如果有已有掩膜，加载它
            if (!string.IsNullOrEmpty(existingMaskPath) && File.Exists(existingMaskPath))
            {
                try
                {
                    var loaded = Cv2.ImRead(existingMaskPath, ImreadModes.Grayscale);
                    if (loaded != null && !loaded.Empty() &&
                        loaded.Width == _imageWidth && loaded.Height == _imageHeight)
                    {
                        _maskMat.Dispose();
                        _maskMat = loaded;
                    }
                    else
                    {
                        loaded?.Dispose();
                    }
                }
                catch { }
            }

            this.Loaded += OnLoaded;
            this.SizeChanged += (s, e) => RefreshDisplay();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshDisplay();
        }

        /// <summary>
        /// 将 Mat 转换为 WPF BitmapSource
        /// </summary>
        private static BitmapSource MatToBitmapSource(Mat mat)
        {
            var bitmap = BitmapConverter.ToBitmap(mat);
            var handle = bitmap.GetHbitmap();
            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    handle, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(handle);
                bitmap.Dispose();
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// 刷新显示：将源图与掩膜叠加显示
        /// </summary>
        private void RefreshDisplay()
        {
            // 创建叠加图像：遮蔽区域显示为半透明红色
            using var display = _sourceImage.Clone();

            // 创建红色覆盖层
            using var redOverlay = new Mat(display.Size(), display.Type(), new Scalar(0, 0, 180));

            // 反转掩膜得到遮蔽区域
            using var invertedMask = new Mat();
            Cv2.BitwiseNot(_maskMat, invertedMask);

            // 在源图上叠加红色遮蔽
            using var maskedRed = new Mat();
            redOverlay.CopyTo(maskedRed, invertedMask);

            Cv2.AddWeighted(display, 0.7, maskedRed, 0.3, 0, display);

            // 转为 BitmapSource 显示
            SourceImage.Source = MatToBitmapSource(display);
        }

        /// <summary>
        /// 获取图像在容器中的实际渲染区域
        /// </summary>
        private System.Windows.Rect GetImageRect()
        {
            double containerW = ImageContainer.ActualWidth;
            double containerH = ImageContainer.ActualHeight;

            if (containerW <= 0 || containerH <= 0) return System.Windows.Rect.Empty;

            double scaleX = containerW / _imageWidth;
            double scaleY = containerH / _imageHeight;
            double scale = Math.Min(scaleX, scaleY);

            double renderW = _imageWidth * scale;
            double renderH = _imageHeight * scale;
            double offsetX = (containerW - renderW) / 2;
            double offsetY = (containerH - renderH) / 2;

            return new System.Windows.Rect(offsetX, offsetY, renderW, renderH);
        }

        /// <summary>
        /// 将容器坐标转换为原始图像坐标
        /// </summary>
        private (int X, int Y) DisplayToImage(Point displayPoint)
        {
            var imageRect = GetImageRect();
            if (imageRect.IsEmpty) return (-1, -1);

            double relX = (displayPoint.X - imageRect.X) / imageRect.Width;
            double relY = (displayPoint.Y - imageRect.Y) / imageRect.Height;

            int imgX = (int)(relX * _imageWidth);
            int imgY = (int)(relY * _imageHeight);

            return (imgX, imgY);
        }

        /// <summary>
        /// 获取缩放后的画笔大小（屏幕空间 → 图像空间）
        /// </summary>
        private int GetScaledBrushSize()
        {
            var imageRect = GetImageRect();
            if (imageRect.IsEmpty || imageRect.Width <= 0) return _brushSize;

            double scale = _imageWidth / imageRect.Width;
            return Math.Max(1, (int)(_brushSize * scale));
        }

        private void PaintAt(Point displayPoint)
        {
            var (imgX, imgY) = DisplayToImage(displayPoint);
            if (imgX < 0 || imgY < 0 || imgX >= _imageWidth || imgY >= _imageHeight) return;

            int scaledBrush = GetScaledBrushSize();
            var center = new OpenCvSharp.Point(imgX, imgY);

            // 画笔=涂黑（遮蔽），橡皮擦=涂白（恢复）
            var color = _isErasing ? new Scalar(255) : new Scalar(0);
            Cv2.Circle(_maskMat, center, scaledBrush / 2, color, -1);

            RefreshDisplay();
        }

        #region 鼠标事件

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isPainting = true;
            ImageContainer.CaptureMouse();
            PaintAt(e.GetPosition(ImageContainer));
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPainting) return;
            PaintAt(e.GetPosition(ImageContainer));
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isPainting = false;
            ImageContainer.ReleaseMouseCapture();
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 右键切换画笔/橡皮擦
            _isErasing = !_isErasing;
            BrushBtn.IsChecked = !_isErasing;
            EraserBtn.IsChecked = _isErasing;
        }

        #endregion

        #region 工具栏事件

        private void BrushBtn_Checked(object sender, RoutedEventArgs e)
        {
            _isErasing = false;
            if (EraserBtn != null) EraserBtn.IsChecked = false;
        }

        private void EraserBtn_Checked(object sender, RoutedEventArgs e)
        {
            _isErasing = true;
            if (BrushBtn != null) BrushBtn.IsChecked = false;
        }

        private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _brushSize = (int)e.NewValue;
            if (BrushSizeText != null) BrushSizeText.Text = _brushSize.ToString();
        }

        private void MaskAll_Click(object sender, RoutedEventArgs e)
        {
            // 全部遮蔽（全黑）
            _maskMat.SetTo(new Scalar(0));
            RefreshDisplay();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            // 重置为全白（不遮蔽）
            _maskMat.SetTo(new Scalar(255));
            RefreshDisplay();
        }

        #endregion

        #region 底部按钮

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _maskMat.SetTo(new Scalar(255));
            RefreshDisplay();
        }

        private void CompleteButton_Click(object sender, RoutedEventArgs e)
        {
            // 检查是否有实际的遮蔽区域
            int blackPixels = _imageWidth * _imageHeight - Cv2.CountNonZero(_maskMat);
            if (blackPixels == 0)
            {
                // 没有遮蔽区域，清除掩膜路径
                MaskPath = null;
                DialogResult = true;
                Close();
                return;
            }

            try
            {
                // 保存掩膜到 masks 目录
                var masksDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "masks");
                if (!Directory.Exists(masksDir))
                {
                    Directory.CreateDirectory(masksDir);
                }

                // 如果有已有路径就覆盖，否则创建新路径
                if (!string.IsNullOrEmpty(_existingMaskPath) && _existingMaskPath.StartsWith(masksDir))
                {
                    MaskPath = _existingMaskPath;
                }
                else
                {
                    MaskPath = System.IO.Path.Combine(masksDir, $"mask_{Guid.NewGuid():N}.png");
                }

                Cv2.ImWrite(MaskPath, _maskMat);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Strings.Win_SaveMaskFailed, ex.Message), Strings.VM_Error, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _maskMat?.Dispose();
            base.OnClosed(e);
        }

        private void ApplyLocalization()
        {
            Title = Strings.UI_MaskPaint;
            BrushBtn.Content = Strings.UI_Brush;
            EraserBtn.Content = Strings.UI_Eraser;
            TxtBrushSizeLabel.Text = Strings.UI_BrushSize;
            BtnMaskAll.Content = Strings.UI_MaskAll;
            BtnClearAll.Content = Strings.UI_ClearAll;
            TxtMaskHint.Text = Strings.UI_MaskHint;
            BtnReset.Content = Strings.UI_Reset;
            BtnComplete.Content = Strings.UI_Complete;
        }
    }
}
