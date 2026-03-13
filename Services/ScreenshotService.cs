using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenCvSharp;
using Bitmap = System.Drawing.Bitmap;
using Graphics = System.Drawing.Graphics;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace TaskFlow.Services
{
    public interface IScreenshotService
    {
        Task<(bool Success, Mat? Image, string? Error)> CaptureWindowAsync(string processName, bool includeTitleBar = true, int cropTopHeight = 0);
    }

    public class ScreenshotService : IScreenshotService
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const int PW_RENDERFULLCONTENT = 2;

        #endregion

        public async Task<(bool Success, Mat? Image, string? Error)> CaptureWindowAsync(string processName, bool includeTitleBar = true, int cropTopHeight = 0)
        {
            return await Task.Run<(bool Success, Mat? Image, string? Error)>(() =>
            {
                try
                {
                    // 查找进程
                    var processes = Process.GetProcessesByName(processName);
                    if (processes.Length == 0)
                    {
                        return (false, null, $"找不到进程: {processName}");
                    }

                    var process = processes[0];
                    var hWnd = process.MainWindowHandle;

                    // 释放所有 Process 对象（避免句柄泄漏）
                    foreach (var p in processes) p.Dispose();

                    if (hWnd == IntPtr.Zero)
                    {
                        return (false, null, $"无法获取窗口句柄: {processName}");
                    }

                    // 检查窗口是否最小化
                    if (IsIconic(hWnd))
                    {
                        return (false, null, $"窗口已最小化: {processName}");
                    }

                    // 获取窗口尺寸
                    if (!GetWindowRect(hWnd, out RECT rect))
                    {
                        return (false, null, "无法获取窗口尺寸");
                    }

                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;

                    if (width <= 0 || height <= 0)
                    {
                        return (false, null, "窗口尺寸无效");
                    }

                    // 使用PrintWindow截图（可以截取被遮挡的窗口）
                    using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    using var graphics = Graphics.FromImage(bitmap);

                    var hdc = graphics.GetHdc();
                    bool success = PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT);
                    graphics.ReleaseHdc(hdc);

                    if (!success)
                    {
                        // 回退到传统截图方式
                        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height));
                    }

                    // 直接从 Bitmap 像素数据创建 Mat，避免 PNG 编解码开销
                    var bmpData = bitmap.LockBits(
                        new System.Drawing.Rectangle(0, 0, width, height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb);
                    Mat mat;
                    try
                    {
                        using var bgraMat = Mat.FromPixelData(height, width, MatType.CV_8UC4, bmpData.Scan0);
                        mat = new Mat();
                        Cv2.CvtColor(bgraMat, mat, ColorConversionCodes.BGRA2BGR);
                    }
                    finally
                    {
                        bitmap.UnlockBits(bmpData);
                    }

                    if (mat.Empty())
                    {
                        return (false, null, "图像转换失败");
                    }

                    // 如果不包含标题栏，裁剪出客户区域
                    if (!includeTitleBar)
                    {
                        if (GetClientRect(hWnd, out RECT clientRect))
                        {
                            // 获取客户区左上角在屏幕上的坐标
                            var clientOrigin = new POINT { X = 0, Y = 0 };
                            ClientToScreen(hWnd, ref clientOrigin);

                            // 计算客户区在截图中的偏移（相对于窗口左上角）
                            int offsetX = clientOrigin.X - rect.Left;
                            int offsetY = clientOrigin.Y - rect.Top;
                            int clientWidth = clientRect.Right - clientRect.Left;
                            int clientHeight = clientRect.Bottom - clientRect.Top;

                            // 确保裁剪区域有效
                            if (offsetX >= 0 && offsetY >= 0 &&
                                offsetX + clientWidth <= mat.Width &&
                                offsetY + clientHeight <= mat.Height &&
                                clientWidth > 0 && clientHeight > 0)
                            {
                                var roi = new Rect(offsetX, offsetY, clientWidth, clientHeight);
                                var cropped = new Mat(mat, roi).Clone(); // Clone 获得独立内存，避免悬空引用
                                mat.Dispose();
                                mat = cropped;
                            }
                        }
                    }

                    // 如果指定了额外的顶部裁剪高度
                    if (cropTopHeight > 0 && cropTopHeight < mat.Height)
                    {
                        var roi = new Rect(0, cropTopHeight, mat.Width, mat.Height - cropTopHeight);
                        var cropped = new Mat(mat, roi).Clone(); // Clone 获得独立内存，避免悬空引用
                        mat.Dispose();
                        mat = cropped;
                    }

                    return (true, mat, (string?)null);
                }
                catch (Exception ex)
                {
                    return (false, null, ex.Message);
                }
            });
        }
    }
}
