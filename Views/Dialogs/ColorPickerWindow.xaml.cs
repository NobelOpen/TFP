using System;
using TaskFlow.Resources;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace TaskFlow.Views.Dialogs
{
    /// <summary>
    /// 颜色吸笔窗口：从图像上拾取像素的 HSV 值
    /// </summary>
    public partial class ColorPickerWindow : System.Windows.Window
    {
        private readonly Mat _sourceImage;
        private readonly Mat _hsvImage;
        private bool _picked = false;

        /// <summary>拾取的 H 值 (0-180)</summary>
        public int PickedH { get; private set; }
        /// <summary>拾取的 S 值 (0-255)</summary>
        public int PickedS { get; private set; }
        /// <summary>拾取的 V 值 (0-255)</summary>
        public int PickedV { get; private set; }

        public ColorPickerWindow(Mat sourceImage)
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) this.DragMove(); };
            ApplyLocalization();
            _sourceImage = sourceImage;

            // 转换为 HSV 备用
            _hsvImage = new Mat();
            if (sourceImage.Channels() == 1)
            {
                using var bgr = new Mat();
                Cv2.CvtColor(sourceImage, bgr, ColorConversionCodes.GRAY2BGR);
                Cv2.CvtColor(bgr, _hsvImage, ColorConversionCodes.BGR2HSV);
            }
            else
            {
                Cv2.CvtColor(sourceImage, _hsvImage, ColorConversionCodes.BGR2HSV);
            }

            // 显示图像
            var bmp = BitmapSourceConverter.ToBitmapSource(_sourceImage);
            SourceImage.Source = bmp;
            SourceImage.Width = _sourceImage.Width;
            SourceImage.Height = _sourceImage.Height;
            ImageCanvas.Width = _sourceImage.Width;
            ImageCanvas.Height = _sourceImage.Height;
        }

        private void ImageCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(SourceImage);
            PickColorAt((int)pos.X, (int)pos.Y);
        }

        private void ImageCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            // 实时预览鼠标位置的颜色
            var pos = e.GetPosition(SourceImage);
            int x = (int)pos.X, y = (int)pos.Y;
            if (x >= 0 && x < _sourceImage.Width && y >= 0 && y < _sourceImage.Height)
            {
                var hsv = _hsvImage.At<Vec3b>(y, x);
                var bgr = _sourceImage.Channels() >= 3 
                    ? _sourceImage.At<Vec3b>(y, x) 
                    : new Vec3b(_sourceImage.At<byte>(y, x), _sourceImage.At<byte>(y, x), _sourceImage.At<byte>(y, x));
                
                ColorPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(bgr[2], bgr[1], bgr[0]));
                InfoText.Text = string.Format(Strings.Win_Position, x, y, hsv[0], hsv[1], hsv[2]);
            }
        }

        private void PickColorAt(int x, int y)
        {
            if (x < 0 || x >= _sourceImage.Width || y < 0 || y >= _sourceImage.Height) return;

            var hsv = _hsvImage.At<Vec3b>(y, x);
            PickedH = hsv[0];
            PickedS = hsv[1];
            PickedV = hsv[2];
            _picked = true;

            // 更新显示
            var bgr = _sourceImage.Channels() >= 3
                ? _sourceImage.At<Vec3b>(y, x)
                : new Vec3b(_sourceImage.At<byte>(y, x), _sourceImage.At<byte>(y, x), _sourceImage.At<byte>(y, x));
            ColorPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(bgr[2], bgr[1], bgr[0]));
            InfoText.Text = string.Format(Strings.Win_ColorPicked, PickedH, PickedS, PickedV, x, y);
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!_picked)
            {
                MessageBox.Show(Strings.Win_PickColorFirst, Strings.Dlg_Hint, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _hsvImage?.Dispose();
            base.OnClosed(e);
        }

        private void ApplyLocalization()
        {
            Title = Strings.UI_ColorPicker;
            TxtColorHint.Text = Strings.UI_ColorPickerHint;
            InfoText.Text = Strings.UI_ClickToPickColor;
            BtnOK.Content = Strings.UI_OK;
            BtnCancel.Content = Strings.UI_Cancel;
        }
    }
}
