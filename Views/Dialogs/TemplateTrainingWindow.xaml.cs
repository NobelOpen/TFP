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
    public enum RoiMode
    {
        None,
        Rectangle,
        AffineRectangle,
        Circle
    }

    public partial class TemplateTrainingWindow : System.Windows.Window
    {
        private readonly Mat _sourceImage;
        private readonly Guid _taskId;
        private readonly int _imageWidth;
        private readonly int _imageHeight;

        private RoiMode _currentMode = RoiMode.None;
        private bool _isDrawing = false;
        private Point _startPoint;

        // ROI drawing shapes
        private Shape? _currentShape;
        private readonly List<Point> _affinePoints = new();
        private readonly List<Ellipse> _affineMarkers = new();

        // ROI result (in original image coordinates)
        private OpenCvSharp.Rect _roiRect;
        private bool _roiCompleted = false;

        /// <summary>
        /// 完成后的模板图像路径
        /// </summary>
        public string? TemplatePath { get; private set; }

        public TemplateTrainingWindow(Mat sourceImage, Guid taskId)
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) this.DragMove(); };
            ApplyLocalization();
            _sourceImage = sourceImage;
            _taskId = taskId;
            _imageWidth = sourceImage.Width;
            _imageHeight = sourceImage.Height;

            Loaded += OnLoaded;
            SizeChanged += (s, e) => UpdateOverlayCanvas();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var bitmap = BitmapConverter.ToBitmap(_sourceImage);
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap.GetHbitmap(),
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmap.Dispose();

                SourceImage.Source = bitmapSource;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法加载图像: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        #region Coordinate Conversion (display ↔ original image)

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

        private void UpdateOverlayCanvas()
        {
            OverlayCanvas.Width = ImageContainer.ActualWidth;
            OverlayCanvas.Height = ImageContainer.ActualHeight;
        }

        #endregion

        #region ROI Mode Selection

        private void RoiButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton btn)
            {
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
                ImageContainer.Cursor = Cursors.Arrow;
                StatusText.Text = "请选择ROI类型，然后在图像上绘制";
            }
        }

        #endregion

        #region Canvas Mouse Events

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentMode == RoiMode.None) return;

            var pos = e.GetPosition(ImageContainer);

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

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing || _currentShape == null) return;

            var pos = e.GetPosition(ImageContainer);
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

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;
            _isDrawing = false;
            ImageContainer.ReleaseMouseCapture();

            if (_currentShape == null) return;

            if (_currentMode == RoiMode.Rectangle && _currentShape is Rectangle rect)
            {
                double dispX = Canvas.GetLeft(rect);
                double dispY = Canvas.GetTop(rect);
                var imgTopLeft = DisplayToImage(new Point(dispX, dispY));
                var imgBottomRight = DisplayToImage(new Point(dispX + rect.Width, dispY + rect.Height));

                int ix = (int)imgTopLeft.X;
                int iy = (int)imgTopLeft.Y;
                int iw = (int)(imgBottomRight.X - imgTopLeft.X);
                int ih = (int)(imgBottomRight.Y - imgTopLeft.Y);

                if (iw > 3 && ih > 3)
                {
                    _roiRect = new OpenCvSharp.Rect(ix, iy, iw, ih);
                    _roiCompleted = true;
                    rect.StrokeDashArray = null;
                    rect.Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 128));
                    StatusText.Text = $"矩形ROI 已完成: ({ix}, {iy}) {iw}×{ih} — 点击「完成」保存模板";
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
                    _roiRect = new OpenCvSharp.Rect(ix, iy, iw, ih);
                    _roiCompleted = true;
                    ellipse.StrokeDashArray = null;
                    ellipse.Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 128));
                    StatusText.Text = $"圆形ROI 已完成: ({ix}, {iy}) {iw}×{ih} — 点击「完成」保存模板";
                }
                else
                {
                    ClearCurrentRoi();
                    StatusText.Text = Strings.Win_TooSmallRedraw;
                }
            }
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentMode == RoiMode.AffineRectangle && _affinePoints.Count >= 3)
            {
                FinalizeAffineRoi();
            }
        }

        #endregion

        #region Affine Rectangle

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
                _roiRect = new OpenCvSharp.Rect(ix, iy, iw, ih);
                _roiCompleted = true;

                foreach (UIElement child in OverlayCanvas.Children)
                {
                    if (child is Line line && line.Tag as string == "AffineLine")
                    {
                        line.StrokeDashArray = null;
                        line.Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 128));
                    }
                }

                StatusText.Text = $"仿射矩形ROI 已完成: ({ix}, {iy}) {iw}×{ih} — 点击「完成」保存模板";
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

        #region Clear / Reset

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

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            OverlayCanvas.Children.Clear();
            _currentShape = null;
            _affinePoints.Clear();
            _affineMarkers.Clear();
            _roiCompleted = false;
            _isDrawing = false;

            // Delete saved template file if exists
            if (!string.IsNullOrEmpty(TemplatePath) && File.Exists(TemplatePath))
            {
                try
                {
                    File.Delete(TemplatePath);
                    TemplatePath = null;
                }
                catch { }
            }

            StatusText.Text = "已重置 — 请重新选择ROI类型并绘制";
        }

        #endregion

        #region Complete / Save

        private void CompleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_roiCompleted)
            {
                MessageBox.Show("请先在图像上绘制ROI区域", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Clamp ROI to image bounds
                int x = Math.Max(0, _roiRect.X);
                int y = Math.Max(0, _roiRect.Y);
                int w = Math.Min(_roiRect.Width, _imageWidth - x);
                int h = Math.Min(_roiRect.Height, _imageHeight - y);

                if (w <= 0 || h <= 0)
                {
                    MessageBox.Show("ROI区域无效，请重新绘制", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Crop
                var roi = new OpenCvSharp.Rect(x, y, w, h);
                using var cropped = new Mat(_sourceImage, roi);
                var template = cropped.Clone();

                // Save to templates directory
                string templatesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
                if (!Directory.Exists(templatesDir))
                {
                    Directory.CreateDirectory(templatesDir);
                }

                string fileName = $"{_taskId:N}_template.png";
                string savePath = System.IO.Path.Combine(templatesDir, fileName);

                Cv2.ImWrite(savePath, template);
                template.Dispose();

                TemplatePath = savePath;
                StatusText.Text = string.Format(Strings.Win_TemplateSaved, savePath);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Strings.Win_SaveTemplateFailed, ex.Message), Strings.VM_Error, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        private void ApplyLocalization()
        {
            Title = Strings.UI_TemplateTraining;
            RectRoiBtn.Content = Strings.UI_RectRoi;
            AffineRectRoiBtn.Content = Strings.UI_AffineRoi;
            CircleRoiBtn.Content = Strings.UI_CircleRoi;
            ResetBtn.Content = Strings.UI_Reset;
            CompleteBtn.Content = Strings.UI_Complete;
        }
    }
}
