using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskFlow.Resources;
using System.Threading;
using System.Threading.Tasks;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Helpers
{
    public static class Win32Helper
    {
        #region Win32 API Declarations

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const int MK_LBUTTON = 0x0001;

        // MakeLParam 用于构造 x, y 坐标参数
        private static IntPtr MakeLParam(int x, int y)
        {
            return (IntPtr)((y << 16) | (x & 0xFFFF));
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        // 虚拟键码
        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_MENU = 0x12; // Alt键

        #endregion

        /// <summary>
        /// 模拟鼠标单击
        /// </summary>
        public static async Task<bool> ClickAsync(int x, int y)
        {
            return await Task.Run(() =>
            {
                try
                {
                    SetCursorPos(x, y);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// 模拟鼠标双击
        /// </summary>
        public static async Task<bool> DoubleClickAsync(int x, int y)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    await ClickAsync(x, y);
                    await Task.Delay(100);
                    await ClickAsync(x, y);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// 模拟鼠标滑动
        /// </summary>
        public static async Task<bool> SwipeAsync(int x1, int y1, int x2, int y2, int steps = 20)
        {
            return await Task.Run(() =>
            {
                try
                {
                    SetCursorPos(x1, y1);
                    Thread.Sleep(100);

                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(50);

                    double deltaX = (x2 - x1) / (double)steps;
                    double deltaY = (y2 - y1) / (double)steps;

                    for (int i = 1; i <= steps; i++)
                    {
                        int currentX = x1 + (int)(deltaX * i);
                        int currentY = y1 + (int)(deltaY * i);
                        SetCursorPos(currentX, currentY);
                        Thread.Sleep(10);
                    }

                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        #region Off-Screen PostMessage Actions

        /// <summary>
        /// 离屏点击
        /// </summary>
        public static async Task<bool> PostMessageClickAsync(IntPtr hwnd, int x, int y)
        {
            return await Task.Run(() =>
            {
                try
                {
                    IntPtr lParam = MakeLParam(x, y);
                    PostMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
                    Thread.Sleep(50);
                    PostMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
                    Thread.Sleep(50);
                    PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// 离屏双击
        /// </summary>
        public static async Task<bool> PostMessageDoubleClickAsync(IntPtr hwnd, int x, int y)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    await PostMessageClickAsync(hwnd, x, y);
                    await Task.Delay(100);
                    await PostMessageClickAsync(hwnd, x, y);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// 离屏滑动
        /// </summary>
        public static async Task<bool> PostMessageSwipeAsync(IntPtr hwnd, int x1, int y1, int x2, int y2, int steps = 20)
        {
            return await Task.Run(() =>
            {
                try
                {
                    IntPtr startParam = MakeLParam(x1, y1);
                    PostMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, startParam);
                    Thread.Sleep(50);

                    PostMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, startParam);
                    Thread.Sleep(50);

                    double deltaX = (x2 - x1) / (double)steps;
                    double deltaY = (y2 - y1) / (double)steps;

                    for (int i = 1; i <= steps; i++)
                    {
                        int currentX = x1 + (int)(deltaX * i);
                        int currentY = y1 + (int)(deltaY * i);
                        IntPtr moveParam = MakeLParam(currentX, currentY);
                        PostMessage(hwnd, WM_MOUSEMOVE, (IntPtr)MK_LBUTTON, moveParam);
                        Thread.Sleep(10);
                    }

                    IntPtr endParam = MakeLParam(x2, y2);
                    PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, endParam);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        #endregion

        #region Simulate Input

        /// <summary>
        /// 模拟组合输入：修饰键 + 动作（滚轮/按键）
        /// </summary>
        public static async Task<bool> SimulateInputAsync(
            ModifierKeyType modifier, InputActionType action,
            string keyName, int scrollAmount, int repeatCount, int intervalMs)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 按下修饰键
                    PressModifiers(modifier, true);
                    Thread.Sleep(30);

                    for (int i = 0; i < repeatCount; i++)
                    {
                        switch (action)
                        {
                            case InputActionType.ScrollUp:
                                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)scrollAmount, UIntPtr.Zero);
                                break;
                            case InputActionType.ScrollDown:
                                // 向下滚动为负值
                                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)(-scrollAmount)), UIntPtr.Zero);
                                break;
                            case InputActionType.KeyPress:
                                byte vk = ParseVirtualKey(keyName);
                                if (vk != 0)
                                {
                                    keybd_event(vk, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                                    Thread.Sleep(30);
                                    keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                                }
                                break;
                        }

                        if (i < repeatCount - 1 && intervalMs > 0)
                            Thread.Sleep(intervalMs);
                    }

                    // 释放修饰键
                    Thread.Sleep(30);
                    PressModifiers(modifier, false);
                    return true;
                }
                catch
                {
                    // 确保释放修饰键
                    PressModifiers(modifier, false);
                    return false;
                }
            });
        }

        /// <summary>
        /// 按下或释放修饰键
        /// </summary>
        private static void PressModifiers(ModifierKeyType modifier, bool down)
        {
            uint flag = down ? KEYEVENTF_KEYDOWN : KEYEVENTF_KEYUP;
            switch (modifier)
            {
                case ModifierKeyType.Ctrl:
                    keybd_event(VK_CONTROL, 0, flag, UIntPtr.Zero);
                    break;
                case ModifierKeyType.Shift:
                    keybd_event(VK_SHIFT, 0, flag, UIntPtr.Zero);
                    break;
                case ModifierKeyType.Alt:
                    keybd_event(VK_MENU, 0, flag, UIntPtr.Zero);
                    break;
                case ModifierKeyType.CtrlShift:
                    keybd_event(VK_CONTROL, 0, flag, UIntPtr.Zero);
                    keybd_event(VK_SHIFT, 0, flag, UIntPtr.Zero);
                    break;
                case ModifierKeyType.CtrlAlt:
                    keybd_event(VK_CONTROL, 0, flag, UIntPtr.Zero);
                    keybd_event(VK_MENU, 0, flag, UIntPtr.Zero);
                    break;
            }
        }

        /// <summary>
        /// 将按键名称解析为虚拟键码
        /// </summary>
        private static byte ParseVirtualKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return 0;
            keyName = keyName.Trim().ToUpper();

            // 常用按键映射
            return keyName switch
            {
                "A" => 0x41, "B" => 0x42, "C" => 0x43, "D" => 0x44,
                "E" => 0x45, "F" => 0x46, "G" => 0x47, "H" => 0x48,
                "I" => 0x49, "J" => 0x4A, "K" => 0x4B, "L" => 0x4C,
                "M" => 0x4D, "N" => 0x4E, "O" => 0x4F, "P" => 0x50,
                "Q" => 0x51, "R" => 0x52, "S" => 0x53, "T" => 0x54,
                "U" => 0x55, "V" => 0x56, "W" => 0x57, "X" => 0x58,
                "Y" => 0x59, "Z" => 0x5A,
                "0" => 0x30, "1" => 0x31, "2" => 0x32, "3" => 0x33,
                "4" => 0x34, "5" => 0x35, "6" => 0x36, "7" => 0x37,
                "8" => 0x38, "9" => 0x39,
                "TAB" => 0x09, "ENTER" => 0x0D, "ESCAPE" or "ESC" => 0x1B,
                "SPACE" => 0x20, "DELETE" or "DEL" => 0x2E,
                "UP" => 0x26, "DOWN" => 0x28, "LEFT" => 0x25, "RIGHT" => 0x27,
                "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
                "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
                "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
                _ => byte.TryParse(keyName, System.Globalization.NumberStyles.HexNumber, null, out byte hex) ? hex : (byte)0
            };
        }

        #endregion

        /// <summary>
        /// 启动应用程序
        /// </summary>
        public static async Task<(bool Success, string Message)> LaunchApplicationAsync(string exePath, string arguments = "")
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = arguments,
                        UseShellExecute = true
                    };

                    var process = Process.Start(psi);
                    if (process != null)
                    {
                        return (true, $"已启动: {exePath}");
                    }
                    return (false, "启动失败");
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });
        }
    }
}
