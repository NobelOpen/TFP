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
    // 图像预览 + 日志右键菜单
    public partial class MainWindow
    {
        #region Preview Image

        private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
        {

            if (sender is Image image && image.Source != null)
            {
                var pos = e.GetPosition(image);
                var source = image.Source;

                // Stretch="None" + LayoutTransform 模式下，
                // GetPosition 返回的是 transform 前的本地坐标，直接对应图像像素坐标
                int pixelX = (int)pos.X;
                int pixelY = (int)pos.Y;

                if (pixelX >= 0 && pixelX < (int)source.Width &&
                    pixelY >= 0 && pixelY < (int)source.Height)
                {
                    ImageCoordinateText.Text = $"X: {pixelX}  Y: {pixelY}";
                }
                else
                {
                    ImageCoordinateText.Text = "";
                }
            }
        }

        private void PreviewImage_MouseLeave(object sender, MouseEventArgs e)
        {
            ImageCoordinateText.Text = "";
        }

        /// <summary>
        /// 无图片时阻止右键菜单弹出
        /// </summary>
        private void PreviewContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            if (ViewModel.DisplayImage == null || ViewModel.DisplayImage.IsDisposed)
            {
                e.Handled = true; // 取消菜单弹出
            }
        }

        private void SavePreviewImage_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.DisplayImage == null || ViewModel.DisplayImage.IsDisposed)
            {
                MessageBox.Show(TaskFlow.Resources.Strings.Msg_NoImageToSave, TaskFlow.Resources.Strings.Common_Tip, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = $"{TaskFlow.Resources.Strings.Filter_PngImage}|{TaskFlow.Resources.Strings.Filter_JpegImage}|{TaskFlow.Resources.Strings.Filter_BmpImage}",
                DefaultExt = ".png",
                FileName = $"TaskFlow_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    OpenCvSharp.Cv2.ImWrite(dialog.FileName, ViewModel.DisplayImage);
                    ViewModel.AddLog(string.Format(TaskFlow.Resources.Strings.Msg_ImageSaved, dialog.FileName));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(TaskFlow.Resources.Strings.Msg_SaveFailed, ex.Message), TaskFlow.Resources.Strings.Common_Error, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void PreviewImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 无图片时禁止缩放
            if (ViewModel.DisplayImage == null || ViewModel.DisplayImage.IsDisposed) return;

            // 获取鼠标在 ScrollViewer 视口中的位置
            var mousePos = e.GetPosition(ImageScrollViewer);

            // 计算鼠标对应的内容坐标（考虑滚动偏移和当前缩放）
            double currentScale = ImageScaleTransform.ScaleX;
            double contentX = (ImageScrollViewer.HorizontalOffset + mousePos.X) / currentScale;
            double contentY = (ImageScrollViewer.VerticalOffset + mousePos.Y) / currentScale;

            // 计算新缩放比例
            double step = Math.Max(0.01, currentScale * 0.1);
            double newScale = e.Delta > 0 ? currentScale + step : currentScale - step;

            // 限制缩放范围
            double minScale = Math.Max(0.01, GetFitScale() * 0.1);
            newScale = Math.Max(minScale, Math.Min(10.0, newScale));

            // 应用新缩放
            ImageScaleTransform.ScaleX = newScale;
            ImageScaleTransform.ScaleY = newScale;

            // 强制布局更新，确保 ScrollViewer 内容大小已更新
            ImageScrollViewer.UpdateLayout();

            // 调整滚动偏移，使鼠标下方的内容点保持不变
            double newOffsetX = contentX * newScale - mousePos.X;
            double newOffsetY = contentY * newScale - mousePos.Y;
            ImageScrollViewer.ScrollToHorizontalOffset(Math.Max(0, newOffsetX));
            ImageScrollViewer.ScrollToVerticalOffset(Math.Max(0, newOffsetY));

            e.Handled = true;
        }

        private void ResetImageSize_Click(object sender, RoutedEventArgs e)
        {
            // 还原到自适应最小比例
            FitImageToViewport();
        }

        private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 视口大小变化时，如果当前是自适应状态则重新适配
            FitImageToViewport();
        }

        /// <summary>
        /// 计算图像适配到视口的缩放比例
        /// </summary>
        private double GetFitScale()
        {
            if (PreviewImage.Source == null) return 1;

            double imageWidth = PreviewImage.Source.Width;
            double imageHeight = PreviewImage.Source.Height;
            double viewportWidth = ImageScrollViewer.ViewportWidth;
            double viewportHeight = ImageScrollViewer.ViewportHeight;

            if (imageWidth <= 0 || imageHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
                return 1;

            return Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
        }

        /// <summary>
        /// 将图像缩放到适配视口大小
        /// </summary>
        private void FitImageToViewport()
        {
            double fitScale = GetFitScale();
            ImageScaleTransform.ScaleX = fitScale;
            ImageScaleTransform.ScaleY = fitScale;
        }

        #endregion

        #region Log Context Menu

        /// <summary>
        /// 清空日志
        /// </summary>
        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearLog();
        }

        /// <summary>
        /// 导出日志到 txt 文件
        /// </summary>
        private void ExportLog_Click(object sender, RoutedEventArgs e)
        {
            var logText = ViewModel.LogText;
            if (string.IsNullOrEmpty(logText))
            {
                MessageBox.Show(TaskFlow.Resources.Strings.Msg_NoLogToExport, TaskFlow.Resources.Strings.Common_Tip, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = TaskFlow.Resources.Strings.Filter_TextFile,
                DefaultExt = ".txt",
                FileName = $"TaskFlow_Log_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    System.IO.File.WriteAllText(dialog.FileName, logText, System.Text.Encoding.UTF8);
                    ViewModel.AddLog(string.Format(TaskFlow.Resources.Strings.Msg_LogExported, dialog.FileName));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(TaskFlow.Resources.Strings.Msg_ExportFailed, ex.Message), TaskFlow.Resources.Strings.Common_Error, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}

