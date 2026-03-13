using System;
using TaskFlow.Resources;
using System.Collections.Generic;
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
    public partial class RoiSelectionWindow : System.Windows.Window
    {
        private readonly Mat _sourceImage;
        private readonly int _imageWidth;
        private readonly int _imageHeight;

        // ROI 相关
        private RoiMode _currentMode = RoiMode.None;
        private bool _isDrawing = false;
        private Point _startPoint;
        private Shape? _currentShape;
        private readonly List<Point> _affinePoints = new();
        private readonly List<Ellipse> _affineMarkers = new();
        private bool _roiCompleted = false;

        /// <summary>
        /// ROI 结果（原始图像坐标）
        /// </summary>
        public int RoiX { get; private set; }
        public int RoiY { get; private set; }
        public int RoiWidth { get; private set; }
        public int RoiHeight { get; private set; }

        // 初始ROI（用于显示已有的ROI区域）
        private readonly int _initRoiX, _initRoiY, _initRoiW, _initRoiH;

        // ========== 掩膜相关 ==========
        private Mat _maskMat;
        private bool _isMaskMode = false;   // 当前是否在掩膜绘制模式
        private bool _isPainting = false;
        private bool _isErasing = false;    // false=画笔（涂黑遮蔽），true=橡皮擦（涂白恢复）
        private int _brushSize = 20;
        private readonly string? _existingMaskPath;
        private bool _maskModified = false; // 掩膜是否被修改过

        /// <summary>
        /// 完成后的掩膜路径（null 表示无掩膜）
        /// </summary>
        public string? MaskPath { get; private set; }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public RoiSelectionWindow(Mat sourceImage) : this(sourceImage, 0, 0, 0, 0, null) { }

        public RoiSelectionWindow(Mat sourceImage, int initRoiX, int initRoiY, int initRoiW, int initRoiH,
            string? existingMaskPath = null)
        {
            InitializeComponent();
            ApplyLocalization();
            _sourceImage = sourceImage;
            _imageWidth = sourceImage.Width;
            _imageHeight = sourceImage.Height;
            _initRoiX = initRoiX;
            _initRoiY = initRoiY;
            _initRoiW = initRoiW;
            _initRoiH = initRoiH;
            _existingMaskPath = existingMaskPath;

            // 初始化掩膜（全白=全部保留）
            _maskMat = new Mat(_imageHeight, _imageWidth, MatType.CV_8UC1, new Scalar(255));

            // 加载已有掩膜
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

            Loaded += OnLoaded;
            SizeChanged += (s, e) => UpdateOverlayCanvas();

            // 使用 ContentRendered 确保布局完成后再绘制已有 ROI
            if (_initRoiW > 0 && _initRoiH > 0)
            {
                ContentRendered += (s, e) => ShowInitialRoi();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshDisplay();
            UpdateMaskStatus();
        }

        #region 图像显示

        /// <summary>
        /// 刷新显示：源图叠加掩膜半透明遮蔽效果
        /// </summary>
        private void RefreshDisplay()
        {
            try
            {
                // 检查掩膜是否有遮蔽区域
                int blackPixels = _imageWidth * _imageHeight - Cv2.CountNonZero(_maskMat);

                Mat displayMat;
                if (blackPixels > 0)
                {
                    // 有遮蔽区域，叠加红色半透明显示
                    // 灰度图需先转为BGR才能叠加彩色遮罩
                    if (_sourceImage.Channels() == 1)
                        displayMat = new Mat();
                    else
                        displayMat = _sourceImage.Clone();

                    if (_sourceImage.Channels() == 1)
                        Cv2.CvtColor(_sourceImage, displayMat, ColorConversionCodes.GRAY2BGR);

                    using var redOverlay = new Mat(displayMat.Size(), displayMat.Type(), new Scalar(0, 0, 180));
                    using var invertedMask = new Mat();
                    Cv2.BitwiseNot(_maskMat, invertedMask);
                    using var maskedRed = new Mat();
                    redOverlay.CopyTo(maskedRed, invertedMask);
                    Cv2.AddWeighted(displayMat, 0.7, maskedRed, 0.3, 0, displayMat);
                }
                else
                {
                    displayMat = _sourceImage.Clone();
                }

                SourceImage.Source = MatToBitmapSource(displayMat);
                displayMat.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法加载图像: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

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

        #endregion

        #region 初始ROI显示

        /// <summary>
        /// 显示已有的ROI区域
        /// </summary>
        private void ShowInitialRoi()
        {
            UpdateOverlayCanvas();

            _currentMode = RoiMode.Rectangle;
            RectRoiBtn.IsChecked = true;

            var topLeft = ImageToDisplay(new Point(_initRoiX, _initRoiY));
            var bottomRight = ImageToDisplay(new Point(_initRoiX + _initRoiW, _initRoiY + _initRoiH));

            double dispX = topLeft.X;
            double dispY = topLeft.Y;
            double dispW = bottomRight.X - topLeft.X;
            double dispH = bottomRight.Y - topLeft.Y;

            var rect = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 128)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 6, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(30, 0, 200, 255)),
                Width = dispW,
                Height = dispH
            };
            Canvas.SetLeft(rect, dispX);
            Canvas.SetTop(rect, dispY);
            OverlayCanvas.Children.Add(rect);
            _currentShape = rect;

            SetRoiResult(_initRoiX, _initRoiY, _initRoiW, _initRoiH);
            StatusText.Text = $"已有ROI区域: ({_initRoiX}, {_initRoiY}) {_initRoiW}×{_initRoiH} — 可直接完成或重新绘制";
        }

        #endregion

        #region 坐标转换

        private System.Windows.Rect GetImageRect()
        {
            if (SourceImage.Source == null) return new System.Windows.Rect();

            double containerW = ImageContainer.ActualWidth;
            double containerH = ImageContainer.ActualHeight;

            double scaleX = containerW / _imageWidth;
            double scaleY = containerH / _imageHeight;
            double scale = Math.Min(scaleX, scaleY);

            double renderW = _imageWidth * scale;
            double renderH = _imageHeight * scale;

            double offsetX = (containerW - renderW) / 2;
            double offsetY = (containerH - renderH) / 2;

            return new System.Windows.Rect(offsetX, offsetY, renderW, renderH);
        }

        private Point DisplayToImage(Point displayPoint)
        {
            var rect = GetImageRect();
            if (rect.Width <= 0 || rect.Height <= 0) return new Point(0, 0);

            double imgX = (displayPoint.X - rect.X) / rect.Width * _imageWidth;
            double imgY = (displayPoint.Y - rect.Y) / rect.Height * _imageHeight;

            return new Point(
                Math.Max(0, Math.Min(imgX, _imageWidth)),
                Math.Max(0, Math.Min(imgY, _imageHeight))
            );
        }

        private Point ImageToDisplay(Point imagePoint)
        {
            var rect = GetImageRect();
            double dispX = imagePoint.X / _imageWidth * rect.Width + rect.X;
            double dispY = imagePoint.Y / _imageHeight * rect.Height + rect.Y;
            return new Point(dispX, dispY);
        }

        private void UpdateOverlayCanvas()
        {
            OverlayCanvas.Width = ImageContainer.ActualWidth;
            OverlayCanvas.Height = ImageContainer.ActualHeight;
        }

        #endregion

        #region ROI 模式选择

        private void RoiButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton btn)
            {
                // 退出掩膜模式
                ExitMaskMode();

                if (btn != RectRoiBtn) RectRoiBtn.IsChecked = false;
                if (btn != AffineRectRoiBtn) AffineRectRoiBtn.IsChecked = false;
                if (btn != CircleRoiBtn) CircleRoiBtn.IsChecked = false;

                if (btn == RectRoiBtn)
                {
                    _currentMode = RoiMode.Rectangle;
                    StatusText.Text = "矩形ROI：在图像上按住左键拖拽绘制矩形区域";
                }
                else if (btn == AffineRectRoiBtn)
                {
                    _currentMode = RoiMode.AffineRectangle;
                    StatusText.Text = "仿射矩形ROI：在图像上依次左键点击4个角点，右键完成";
                    _affinePoints.Clear();
                    ClearAffineMarkers();
                }
                else if (btn == CircleRoiBtn)
                {
                    _currentMode = RoiMode.Circle;
                    StatusText.Text = "圆形ROI：在图像上按住左键从中心拖拽绘制圆形区域";
                }

                ImageContainer.Cursor = Cursors.Cross;
                ClearCurrentRoi();
            }
        }

        private void RoiButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (RectRoiBtn.IsChecked != true && AffineRectRoiBtn.IsChecked != true && CircleRoiBtn.IsChecked != true)
            {
                _currentMode = RoiMode.None;
                if (!_isMaskMode)
                    ImageContainer.Cursor = Cursors.Arrow;
                StatusText.Text = "请选择ROI类型绘制，或使用掩膜画笔";
            }
        }

        #endregion

        #region 掩膜模式选择

        private void MaskBrushBtn_Checked(object sender, RoutedEventArgs e)
        {
            EnterMaskMode(false);
        }

        private void MaskEraserBtn_Checked(object sender, RoutedEventArgs e)
        {
            EnterMaskMode(true);
        }

        /// <summary>
        /// 进入掩膜绘制模式
        /// </summary>
        private void EnterMaskMode(bool erasing)
        {
            _isMaskMode = true;
            _isErasing = erasing;
            _currentMode = RoiMode.None;

            // 取消ROI工具按钮
            RectRoiBtn.IsChecked = false;
            AffineRectRoiBtn.IsChecked = false;
            CircleRoiBtn.IsChecked = false;

            // 设置掩膜工具按钮状态
            if (erasing)
            {
                MaskEraserBtn.IsChecked = true;
                MaskBrushBtn.IsChecked = false;
            }
            else
            {
                MaskBrushBtn.IsChecked = true;
                MaskEraserBtn.IsChecked = false;
            }

            ImageContainer.Cursor = Cursors.Cross;
            StatusText.Text = erasing ? "橡皮擦模式：左键擦除恢复，右键切换画笔" : "画笔模式：左键涂抹遮蔽，右键切换橡皮擦";
        }

        /// <summary>
        /// 退出掩膜绘制模式
        /// </summary>
        private void ExitMaskMode()
        {
            _isMaskMode = false;
            _isPainting = false;
            MaskBrushBtn.IsChecked = false;
            MaskEraserBtn.IsChecked = false;
        }

        private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _brushSize = (int)e.NewValue;
            if (BrushSizeText != null) BrushSizeText.Text = _brushSize.ToString();
        }

        private void ClearRoi_Click(object sender, RoutedEventArgs e)
        {
            // 仅清除ROI，保留掩膜
            OverlayCanvas.Children.Clear();
            _currentShape = null;
            _affinePoints.Clear();
            _affineMarkers.Clear();
            _roiCompleted = false;
            _isDrawing = false;
            RoiX = 0; RoiY = 0; RoiWidth = 0; RoiHeight = 0;
            StatusText.Text = "已清除ROI区域";
        }

        private void MaskAll_Click(object sender, RoutedEventArgs e)
        {
            // 全部遮蔽（全黑）
            _maskMat.SetTo(new Scalar(0));
            _maskModified = true;
            RefreshDisplay();
            UpdateMaskStatus();
        }

        private void ClearMask_Click(object sender, RoutedEventArgs e)
        {
            // 清除掩膜（全白=不遮蔽）
            _maskMat.SetTo(new Scalar(255));
            _maskModified = true;
            RefreshDisplay();
            UpdateMaskStatus();
        }

        /// <summary>
        /// 更新掩膜状态文本
        /// </summary>
        private void UpdateMaskStatus()
        {
            if (MaskStatusText == null) return;
            int blackPixels = _imageWidth * _imageHeight - Cv2.CountNonZero(_maskMat);
            if (blackPixels > 0)
            {
                MaskStatusText.Text = "✔ 已设置掩膜";
                MaskStatusText.Foreground = new SolidColorBrush(Color.FromRgb(120, 140, 93)); // Anthropic 绿
            }
            else
            {
                MaskStatusText.Text = "未设置掩膜";
                MaskStatusText.Foreground = new SolidColorBrush(Color.FromRgb(176, 174, 165)); // Anthropic 中灰
            }
        }

        #endregion

        #region 掩膜绘制

        /// <summary>
        /// 获取缩放后的画笔大小（屏幕空间 → 图像空间）
        /// </summary>
        private int GetScaledBrushSize()
        {
            var imageRect = GetImageRect();
            if (imageRect.Width <= 0) return _brushSize;
            double scale = _imageWidth / imageRect.Width;
            return Math.Max(1, (int)(_brushSize * scale));
        }

        private void PaintMaskAt(Point displayPoint)
        {
            var imgPt = DisplayToImage(displayPoint);
            int imgX = (int)imgPt.X;
            int imgY = (int)imgPt.Y;
            if (imgX < 0 || imgY < 0 || imgX >= _imageWidth || imgY >= _imageHeight) return;

            int scaledBrush = GetScaledBrushSize();
            var center = new OpenCvSharp.Point(imgX, imgY);

            // 画笔=涂黑（遮蔽），橡皮擦=涂白（恢复）
            var color = _isErasing ? new Scalar(255) : new Scalar(0);
            Cv2.Circle(_maskMat, center, scaledBrush / 2, color, -1);

            _maskModified = true;
            RefreshDisplay();
        }

        #endregion

        #region 鼠标事件

        private void Container_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(ImageContainer);

            // 掩膜绘制模式
            if (_isMaskMode)
            {
                _isPainting = true;
                ImageContainer.CaptureMouse();
                PaintMaskAt(pos);
                return;
            }

            // ROI 模式
            if (_currentMode == RoiMode.None) return;

            if (_currentMode == RoiMode.AffineRectangle)
            {
                HandleAffinePointClick(pos);
                return;
            }

            _isDrawing = true;
            _startPoint = pos;
            _roiCompleted = false;
            ClearCurrentRoi();

            if (_currentMode == RoiMode.Rectangle)
            {
                var rect = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(40, 0, 200, 255))
                };
                Canvas.SetLeft(rect, pos.X);
                Canvas.SetTop(rect, pos.Y);
                OverlayCanvas.Children.Add(rect);
                _currentShape = rect;
            }
            else if (_currentMode == RoiMode.Circle)
            {
                var ellipse = new Ellipse
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(40, 255, 165, 0))
                };
                OverlayCanvas.Children.Add(ellipse);
                _currentShape = ellipse;
            }

            ImageContainer.CaptureMouse();
        }

        private void Container_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(ImageContainer);

            // 掩膜绘制
            if (_isMaskMode && _isPainting)
            {
                PaintMaskAt(pos);
                return;
            }

            // ROI 绘制
            if (!_isDrawing || _currentShape == null) return;

            pos.X = Math.Max(0, Math.Min(pos.X, ImageContainer.ActualWidth));
            pos.Y = Math.Max(0, Math.Min(pos.Y, ImageContainer.ActualHeight));

            if (_currentMode == RoiMode.Rectangle && _currentShape is Rectangle rect)
            {
                double x = Math.Min(_startPoint.X, pos.X);
                double y = Math.Min(_startPoint.Y, pos.Y);
                double w = Math.Abs(pos.X - _startPoint.X);
                double h = Math.Abs(pos.Y - _startPoint.Y);

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                rect.Width = w;
                rect.Height = h;

                var imgStart = DisplayToImage(new Point(x, y));
                var imgEnd = DisplayToImage(new Point(x + w, y + h));
                StatusText.Text = string.Format(Strings.Win_RectRoiDrawing, (int)imgStart.X, (int)imgStart.Y, (int)(imgEnd.X - imgStart.X), (int)(imgEnd.Y - imgStart.Y));
            }
            else if (_currentMode == RoiMode.Circle && _currentShape is Ellipse ellipse)
            {
                double radius = Math.Sqrt(Math.Pow(pos.X - _startPoint.X, 2) + Math.Pow(pos.Y - _startPoint.Y, 2));

                Canvas.SetLeft(ellipse, _startPoint.X - radius);
                Canvas.SetTop(ellipse, _startPoint.Y - radius);
                ellipse.Width = radius * 2;
                ellipse.Height = radius * 2;

                var imgCenter = DisplayToImage(_startPoint);
                var imgEdge = DisplayToImage(new Point(_startPoint.X + radius, _startPoint.Y));
                double imgRadius = Math.Abs(imgEdge.X - imgCenter.X);
                StatusText.Text = $"圆形ROI: 中心({(int)imgCenter.X}, {(int)imgCenter.Y}) 半径{(int)imgRadius}";
            }
        }

        private void Container_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 掩膜绘制
            if (_isMaskMode && _isPainting)
            {
                _isPainting = false;
                ImageContainer.ReleaseMouseCapture();
                UpdateMaskStatus();
                return;
            }

            // ROI 绘制
            if (!_isDrawing) return;
            _isDrawing = false;
            ImageContainer.ReleaseMouseCapture();

            if (_currentShape == null) return;

            if (_currentMode == RoiMode.Rectangle && _currentShape is Rectangle rect)
            {
                double dispX = Canvas.GetLeft(rect);
                double dispY = Canvas.GetTop(rect);
                double dispW = rect.Width;
                double dispH = rect.Height;

                var imgTopLeft = DisplayToImage(new Point(dispX, dispY));
                var imgBottomRight = DisplayToImage(new Point(dispX + dispW, dispY + dispH));

                int ix = (int)imgTopLeft.X;
                int iy = (int)imgTopLeft.Y;
                int iw = (int)(imgBottomRight.X - imgTopLeft.X);
                int ih = (int)(imgBottomRight.Y - imgTopLeft.Y);

                if (iw > 3 && ih > 3)
                {
                    SetRoiResult(ix, iy, iw, ih);
                    rect.StrokeDashArray = null;
                    rect.Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 128));
                    StatusText.Text = $"矩形ROI 已完成: ({ix}, {iy}) {iw}×{ih} — 点击「完成」保存";
                }
                else
                {
                    ClearCurrentRoi();
                    StatusText.Text = Strings.Win_TooSmallRedraw;
                }
            }
            else if (_currentMode == RoiMode.Circle && _currentShape is Ellipse ellipse)
            {
                double radius = ellipse.Width / 2;
                var imgCenter = DisplayToImage(_startPoint);
                var imgEdge = DisplayToImage(new Point(_startPoint.X + radius, _startPoint.Y));
                double imgRadius = Math.Abs(imgEdge.X - imgCenter.X);

                int ix = Math.Max(0, (int)(imgCenter.X - imgRadius));
                int iy = Math.Max(0, (int)(imgCenter.Y - imgRadius));
                int iw = (int)(imgRadius * 2);
                int ih = (int)(imgRadius * 2);

                iw = Math.Min(iw, _imageWidth - ix);
                ih = Math.Min(ih, _imageHeight - iy);

                if (iw > 3 && ih > 3)
                {
                    SetRoiResult(ix, iy, iw, ih);
                    ellipse.StrokeDashArray = null;
                    ellipse.Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 128));
                    StatusText.Text = $"圆形ROI 已完成: ({ix}, {iy}) {iw}×{ih} — 点击「完成」保存";
                }
                else
                {
                    ClearCurrentRoi();
                    StatusText.Text = Strings.Win_TooSmallRedraw;
                }
            }
        }

        private void Container_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 掩膜模式下右键切换画笔/橡皮擦
            if (_isMaskMode)
            {
                _isErasing = !_isErasing;
                MaskBrushBtn.IsChecked = !_isErasing;
                MaskEraserBtn.IsChecked = _isErasing;
                StatusText.Text = _isErasing ? "橡皮擦模式：左键擦除恢复" : "画笔模式：左键涂抹遮蔽";
                return;
            }

            // 仿射矩形模式
            if (_currentMode == RoiMode.AffineRectangle && _affinePoints.Count >= 3)
            {
                FinalizeAffineRoi();
            }
        }

        #endregion

        #region 仿射矩形

        private void HandleAffinePointClick(Point pos)
        {
            if (_affinePoints.Count >= 4) return;

            _affinePoints.Add(pos);

            var marker = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(255, 80, 80)),
                Stroke = Brushes.White,
                StrokeThickness = 1
            };
            Canvas.SetLeft(marker, pos.X - 4);
            Canvas.SetTop(marker, pos.Y - 4);
            OverlayCanvas.Children.Add(marker);
            _affineMarkers.Add(marker);

            StatusText.Text = $"仿射矩形ROI: 已标记 {_affinePoints.Count}/4 个角点";

            if (_affinePoints.Count > 1)
            {
                var prevPoint = _affinePoints[_affinePoints.Count - 2];
                var line = new Line
                {
                    X1 = prevPoint.X,
                    Y1 = prevPoint.Y,
                    X2 = pos.X,
                    Y2 = pos.Y,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 200, 0)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Tag = "AffineLine"
                };
                OverlayCanvas.Children.Add(line);
            }

            if (_affinePoints.Count == 4)
            {
                var closingLine = new Line
                {
                    X1 = _affinePoints[3].X,
                    Y1 = _affinePoints[3].Y,
                    X2 = _affinePoints[0].X,
                    Y2 = _affinePoints[0].Y,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 200, 0)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Tag = "AffineLine"
                };
                OverlayCanvas.Children.Add(closingLine);
                FinalizeAffineRoi();
            }
        }

        private void FinalizeAffineRoi()
        {
            if (_affinePoints.Count < 3) return;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var p in _affinePoints)
            {
                var imgP = DisplayToImage(p);
                minX = Math.Min(minX, imgP.X);
                minY = Math.Min(minY, imgP.Y);
                maxX = Math.Max(maxX, imgP.X);
                maxY = Math.Max(maxY, imgP.Y);
            }

            int ix = Math.Max(0, (int)minX);
            int iy = Math.Max(0, (int)minY);
            int iw = Math.Min((int)(maxX - minX), _imageWidth - ix);
            int ih = Math.Min((int)(maxY - minY), _imageHeight - iy);

            if (iw > 3 && ih > 3)
            {
                SetRoiResult(ix, iy, iw, ih);

                foreach (UIElement child in OverlayCanvas.Children)
                {
                    if (child is Line line && line.Tag as string == "AffineLine")
                    {
                        line.StrokeDashArray = null;
                        line.Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 128));
                    }
                }

                StatusText.Text = $"仿射矩形ROI 已完成: ({ix}, {iy}) {iw}×{ih} — 点击「完成」保存";
            }
            else
            {
                StatusText.Text = Strings.Win_TooSmallRedraw;
            }
        }

        private void ClearAffineMarkers()
        {
            foreach (var marker in _affineMarkers)
                OverlayCanvas.Children.Remove(marker);
            _affineMarkers.Clear();

            var toRemove = new List<UIElement>();
            foreach (UIElement child in OverlayCanvas.Children)
            {
                if (child is Line line && line.Tag as string == "AffineLine")
                    toRemove.Add(line);
            }
            foreach (var item in toRemove)
                OverlayCanvas.Children.Remove(item);
        }

        #endregion

        #region 辅助方法

        private void SetRoiResult(int x, int y, int w, int h)
        {
            RoiX = x;
            RoiY = y;
            RoiWidth = w;
            RoiHeight = h;
            _roiCompleted = true;
        }

        private void ClearCurrentRoi()
        {
            if (_currentShape != null)
            {
                OverlayCanvas.Children.Remove(_currentShape);
                _currentShape = null;
            }
            ClearAffineMarkers();
            _affinePoints.Clear();
            _roiCompleted = false;
        }

        #endregion

        #region 底部按钮

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            // 重置ROI
            OverlayCanvas.Children.Clear();
            _currentShape = null;
            _affinePoints.Clear();
            _affineMarkers.Clear();
            _roiCompleted = false;
            _isDrawing = false;
            RoiX = 0; RoiY = 0; RoiWidth = 0; RoiHeight = 0;

            // 重置掩膜
            _maskMat.SetTo(new Scalar(255));
            _maskModified = true;

            RefreshDisplay();
            UpdateMaskStatus();
            StatusText.Text = "已重置 — 请重新选择ROI类型绘制，或使用掩膜画笔";
        }

        private void CompleteButton_Click(object sender, RoutedEventArgs e)
        {
            // 保存掩膜
            int blackPixels = _imageWidth * _imageHeight - Cv2.CountNonZero(_maskMat);
            if (blackPixels > 0)
            {
                try
                {
                    var masksDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "masks");
                    if (!Directory.Exists(masksDir))
                        Directory.CreateDirectory(masksDir);

                    // 复用已有路径或新建
                    if (!string.IsNullOrEmpty(_existingMaskPath) && _existingMaskPath.StartsWith(masksDir))
                        MaskPath = _existingMaskPath;
                    else
                        MaskPath = System.IO.Path.Combine(masksDir, $"mask_{Guid.NewGuid():N}.png");

                    Cv2.ImWrite(MaskPath, _maskMat);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(Strings.Win_SaveMaskFailed, ex.Message), Strings.VM_Error, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                MaskPath = null;
            }

            DialogResult = true;
            Close();
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _maskMat?.Dispose();
            base.OnClosed(e);
        }

        private void ApplyLocalization()
        {
            Title = Strings.UI_RoiSelection;
            RectRoiBtn.Content = Strings.UI_RectRoi;
            AffineRectRoiBtn.Content = Strings.UI_AffineRoi;
            CircleRoiBtn.Content = Strings.UI_CircleRoi;
            BtnClearRoi.Content = Strings.UI_ClearRoi;
            TxtMaskLabel.Text = Strings.UI_MaskLabel;
            MaskBrushBtn.Content = Strings.UI_Brush;
            MaskEraserBtn.Content = Strings.UI_Eraser;
            ResetBtn.Content = Strings.UI_Reset;
            CompleteBtn.Content = Strings.UI_Complete;
        }
    }
}
