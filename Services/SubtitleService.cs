using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TaskFlow.Models.TaskCards;
using TaskFlow.Views.Windows;

namespace TaskFlow.Services
{
    /// <summary>
    /// 字幕服务 - 管理多个字幕叠层窗口实例（按 ID 隔离）
    /// 每张 WinSubtitleTaskCard 通过唯一 ID 拥有自己的窗口
    /// </summary>
    public class SubtitleService
    {
        /// <summary>
        /// 字幕窗口 + 定时器取消令牌，按 ID 存储
        /// </summary>
        private readonly ConcurrentDictionary<string, SubtitleEntry> _entries = new();

        /// <summary>
        /// 封装单个字幕的窗口和定时器状态
        /// </summary>
        private class SubtitleEntry : IDisposable
        {
            public SubtitleOverlayWindow? Window { get; set; }
            public CancellationTokenSource? TimerCts { get; set; }

            public void CancelTimer()
            {
                TimerCts?.Cancel();
                TimerCts?.Dispose();
                TimerCts = null;
            }

            public void Dispose()
            {
                CancelTimer();
                if (Window != null)
                {
                    Window.Close();
                    Window = null;
                }
            }
        }

        #region Win32 API - 获取窗口位置

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion

        /// <summary>
        /// 显示或更新指定 ID 的字幕
        /// </summary>
        public void ShowSubtitle(string id, string processName, string text, int offsetX, int offsetY,
            int width, int height, int fontSize, string textColor,
            SubtitleBackground background, string backgroundColor,
            string sampleMaskPath = "")
        {
            // 如果是自动吸色模式，先采样颜色
            if (background == SubtitleBackground.AutoSample)
            {
                string sampledColor = SampleEdgeColor(processName, offsetX, offsetY, width, height, sampleMaskPath);
                backgroundColor = sampledColor;
                background = SubtitleBackground.SolidColor;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                var entry = _entries.GetOrAdd(id, _ => new SubtitleEntry());

                // 取消该 ID 已有的定时器
                entry.CancelTimer();

                // 确保窗口实例存在
                if (entry.Window == null || !entry.Window.IsLoaded)
                {
                    entry.Window = new SubtitleOverlayWindow();
                    entry.Window.Show();
                }

                // 更新文本和背景样式
                entry.Window.UpdateSubtitle(text, fontSize, textColor, background, backgroundColor);

                // 定位到目标窗口并设置尺寸（统一 DPI 缩放）
                PositionOverlay(entry.Window, processName, offsetX, offsetY, width, height);

                // 确保可见
                if (entry.Window.Visibility != Visibility.Visible)
                    entry.Window.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// 隐藏指定 ID 的字幕（不销毁窗口，以便复用）
        /// </summary>
        public void HideSubtitle(string id)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_entries.TryGetValue(id, out var entry) && entry.Window != null && entry.Window.IsLoaded)
                {
                    entry.CancelTimer();
                    entry.Window.Visibility = Visibility.Hidden;
                }
            });
        }

        /// <summary>
        /// 隐藏全部字幕
        /// </summary>
        public void HideAll()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var kvp in _entries)
                {
                    kvp.Value.CancelTimer();
                    if (kvp.Value.Window != null && kvp.Value.Window.IsLoaded)
                        kvp.Value.Window.Visibility = Visibility.Hidden;
                }
            });
        }

        /// <summary>
        /// 关闭并销毁指定 ID 的字幕窗口
        /// </summary>
        public void CloseSubtitle(string id)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_entries.TryRemove(id, out var entry))
                {
                    entry.Dispose();
                }
            });
        }

        /// <summary>
        /// 关闭并销毁全部字幕窗口（应用退出时调用）
        /// </summary>
        public void CloseAll()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var kvp in _entries)
                {
                    kvp.Value.Dispose();
                }
                _entries.Clear();
            });
        }

        /// <summary>
        /// 将字幕窗口定位到目标进程窗口的指定偏移位置，并设置尺寸
        /// 所有坐标/尺寸均为物理像素，此方法统一转换为 WPF DIP
        /// </summary>
        private void PositionOverlay(SubtitleOverlayWindow window, string processName,
            int offsetX, int offsetY, int width, int height)
        {
            // 获取 DPI 缩放因子（物理像素 → WPF DIP）
            var source = PresentationSource.FromVisual(window);
            double dpiScaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            double dpiScaleY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

            // 设置尺寸（物理像素 → DIP）
            if (width > 0 && height > 0)
            {
                window.SizeToContent = SizeToContent.Manual;
                window.Width = width * dpiScaleX;
                window.Height = height * dpiScaleY;
            }
            else if (width > 0)
            {
                window.SizeToContent = SizeToContent.Height;
                window.Width = width * dpiScaleX;
            }
            else if (height > 0)
            {
                window.SizeToContent = SizeToContent.Width;
                window.Height = height * dpiScaleY;
            }
            else
            {
                window.SizeToContent = SizeToContent.WidthAndHeight;
            }

            // 获取目标进程的主窗口位置
            IntPtr targetHwnd = FindProcessMainWindow(processName);
            if (targetHwnd == IntPtr.Zero)
            {
                // 找不到目标窗口时，使用屏幕绝对坐标（同样需要 DPI 缩放）
                window.Left = offsetX * dpiScaleX;
                window.Top = offsetY * dpiScaleY;
                return;
            }

            if (GetWindowRect(targetHwnd, out RECT rect))
            {
                window.Left = (rect.Left + offsetX) * dpiScaleX;
                window.Top = (rect.Top + offsetY) * dpiScaleY;
            }
        }

        /// <summary>
        /// 查找指定进程名的主窗口句柄
        /// </summary>
        private static IntPtr FindProcessMainWindow(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return IntPtr.Zero;

            var processes = Process.GetProcessesByName(processName);
            foreach (var proc in processes)
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                    return proc.MainWindowHandle;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 采样字幕覆盖区域的边缘颜色（避开中心文字区域）
        /// 仅采样 (边缘区域 ∩ 掩膜白色区域) 的像素
        /// </summary>
        private string SampleEdgeColor(string processName, int offsetX, int offsetY,
            int width, int height, string maskPath = "")
        {
            const string defaultColor = "#B3000000";
            const int edgePixels = 8;
            const byte defaultAlpha = 200;

            try
            {
                IntPtr targetHwnd = FindProcessMainWindow(processName);
                int screenX = offsetX;
                int screenY = offsetY;

                if (targetHwnd != IntPtr.Zero && GetWindowRect(targetHwnd, out RECT rect))
                {
                    screenX = rect.Left + offsetX;
                    screenY = rect.Top + offsetY;
                }

                int captureW = width > 0 ? width : 400;
                int captureH = height > 0 ? height : 80;

                if (captureW < edgePixels * 2 || captureH < edgePixels * 2)
                    return defaultColor;

                using var bitmap = new Bitmap(captureW, captureH, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(screenX, screenY, 0, 0,
                        new System.Drawing.Size(captureW, captureH),
                        CopyPixelOperation.SourceCopy);
                }

                // 加载掩膜（如果有）
                Bitmap? maskBitmap = null;
                if (!string.IsNullOrEmpty(maskPath) && System.IO.File.Exists(maskPath))
                {
                    try
                    {
                        using var fullMask = new Bitmap(maskPath);
                        if (offsetX >= 0 && offsetY >= 0 &&
                            offsetX + captureW <= fullMask.Width &&
                            offsetY + captureH <= fullMask.Height)
                        {
                            maskBitmap = fullMask.Clone(
                                new System.Drawing.Rectangle(offsetX, offsetY, captureW, captureH),
                                PixelFormat.Format32bppArgb);
                        }
                    }
                    catch { /* 掩膜加载失败则忽略 */ }
                }

                long totalR = 0, totalG = 0, totalB = 0;
                int count = 0;

                for (int y = 0; y < captureH; y++)
                {
                    for (int x = 0; x < captureW; x++)
                    {
                        bool isEdge = y < edgePixels || y >= captureH - edgePixels ||
                                      x < edgePixels || x >= captureW - edgePixels;
                        if (!isEdge) continue;

                        if (maskBitmap != null)
                        {
                            var maskPixel = maskBitmap.GetPixel(x, y);
                            if (maskPixel.R < 128 && maskPixel.G < 128 && maskPixel.B < 128)
                                continue;
                        }

                        var pixel = bitmap.GetPixel(x, y);
                        totalR += pixel.R;
                        totalG += pixel.G;
                        totalB += pixel.B;
                        count++;
                    }
                }

                maskBitmap?.Dispose();

                if (count == 0) return defaultColor;

                byte avgR = (byte)(totalR / count);
                byte avgG = (byte)(totalG / count);
                byte avgB = (byte)(totalB / count);

                return $"#{defaultAlpha:X2}{avgR:X2}{avgG:X2}{avgB:X2}";
            }
            catch
            {
                return defaultColor;
            }
        }

        /// <summary>
        /// 显示字幕并在指定时长后自动隐藏
        /// </summary>
        public async Task ShowSubtitleWithDurationAsync(string id, string processName, string text,
            int offsetX, int offsetY, int width, int height,
            int fontSize, string textColor,
            SubtitleBackground background, string backgroundColor,
            int durationMs, bool waitUntilClosed, string sampleMaskPath = "")
        {
            ShowSubtitle(id, processName, text, offsetX, offsetY, width, height,
                fontSize, textColor, background, backgroundColor, sampleMaskPath);

            if (durationMs > 0)
            {
                // 创建可取消的定时器（同一 ID 再次 ShowSubtitle 时会自动取消旧的）
                var cts = new CancellationTokenSource();
                if (_entries.TryGetValue(id, out var entry))
                {
                    entry.CancelTimer(); // 取消旧的定时器
                    entry.TimerCts = cts;
                }

                if (waitUntilClosed)
                {
                    // 阻塞等待
                    try
                    {
                        await Task.Delay(durationMs, cts.Token);
                        HideSubtitle(id);
                    }
                    catch (TaskCanceledException)
                    {
                        // 被新的 ShowSubtitle 取消，无需隐藏
                    }
                }
                else
                {
                    // 非阻塞
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(durationMs, cts.Token);
                            HideSubtitle(id);
                        }
                        catch (TaskCanceledException)
                        {
                            // 被取消，无需处理
                        }
                    });
                }
            }
        }
    }
}
