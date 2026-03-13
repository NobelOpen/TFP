using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenCvSharp;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Resources;
using TaskFlow.Models.TaskCards;
using TaskStatus = TaskFlow.Models.TaskCards.TaskStatus;

namespace TaskFlow.Services
{
    // Windows 平台任务执行器（WinLaunchApp, WinClick, WinScreenshot 等）
    public partial class TaskExecutionService
    {
        private bool ExecuteGetTimestamp(GetTimestampTaskCard task)
        {
            var now = DateTime.Now;
            string format = task.TimestampFormat switch
            {
                TimestampFormat.HourMinuteSecond => now.ToString("HHmmss"),
                TimestampFormat.DayHourMinuteSecond => now.ToString("ddHHmmss"),
                TimestampFormat.MonthDayHourMinuteSecond => now.ToString("MMddHHmmss"),
                TimestampFormat.YearMonthDayHourMinuteSecond => now.ToString("yyyyMMddHHmmss"),
                _ => now.ToString("HHmmss")
            };

            if (long.TryParse(format, out long timestamp))
            {
                task.OutputTimestamp = timestamp;
                Log($"[{DateTime.Now:HH:mm:ss}] 获取当前时间: {timestamp}");
                return true;
            }

            task.ErrorMessage = "时间戳解析失败";
            return false;
        }

        private async Task<bool> ExecuteWinLaunchAppAsync(WinLaunchAppTaskCard task)
        {
            var result = await Win32Helper.LaunchApplicationAsync(task.ExePath, task.Arguments);
            if (!result.Success)
            {
                task.ErrorMessage = result.Message;
            }
            return result.Success;
        }

        private async Task<bool> ExecuteWinScreenshotAsync(WinScreenshotTaskCard task)
        {
            var result = await _screenshotService.CaptureWindowAsync(task.ProcessName, task.IncludeTitleBar, task.CropTopHeight);
            if (result.Success && result.Image != null)
            {
                task.OutputImage?.Dispose();
                task.OutputImage = ApplyGrayscaleIfNeeded(result.Image, task.ConvertToGrayscale);
                return true;
            }
            task.ErrorMessage = result.Error ?? "截屏失败";
            return false;
        }

        private async Task<bool> ExecuteWinClickAsync(WinClickTaskCard task, IList<TaskCardBase> allTasks)
        {
            int x = task.StartX;
            int y = task.StartY;

            // 解析 X/Y 坐标表达式
            if (!ResolveCoordinateExpression(task.StartXExpression, "X", ref x, task, allTasks)) return false;
            if (!ResolveCoordinateExpression(task.StartYExpression, "Y", ref y, task, allTasks)) return false;

            if (task.UseSourceTaskCoordinates && task.SourceTaskIdForCoordinates.HasValue)
            {
                var sourceTask = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForCoordinates.Value);
                if (sourceTask?.OutputX != null && sourceTask?.OutputY != null)
                {
                    x = sourceTask.OutputX.Value;
                    y = sourceTask.OutputY.Value;
                }
            }

            IntPtr? targetHwnd = null;
            if (task.EnableOffScreenClick && !string.IsNullOrWhiteSpace(task.ProcessName))
            {
                var processes = Process.GetProcessesByName(task.ProcessName);
                try
                {
                if (processes.Length > 0)
                {
                    // 获取主窗口句柄
                    targetHwnd = processes.OrderByDescending(p => p.MainWindowHandle.ToInt64()).First().MainWindowHandle;
                    if (targetHwnd == IntPtr.Zero)
                    {
                        task.ErrorMessage = $"进程 '{task.ProcessName}' 没有主窗口";
                        return false;
                    }
                }
                else
                {
                    task.ErrorMessage = $"未找到进程: {task.ProcessName}";
                    return false;
                }
                }
                finally
                {
                    foreach (var p in processes) p.Dispose();
                }
            }

            bool success = false;
            if (targetHwnd.HasValue && targetHwnd.Value != IntPtr.Zero)
            {
                success = task.ClickType switch
                {
                    ClickType.Single => await Win32Helper.PostMessageClickAsync(targetHwnd.Value, x, y),
                    ClickType.Double => await ExecuteWinMultiClickOffScreenAsync(task, targetHwnd.Value, x, y),
                    ClickType.Swipe => await Win32Helper.PostMessageSwipeAsync(targetHwnd.Value, x, y, task.EndX, task.EndY),
                    _ => false
                };
            }
            else
            {
                success = task.ClickType switch
                {
                    ClickType.Single => await Win32Helper.ClickAsync(x, y),
                    ClickType.Double => await ExecuteWinMultiClickAsync(task, x, y),
                    ClickType.Swipe => await Win32Helper.SwipeAsync(x, y, task.EndX, task.EndY),
                    _ => false
                };
            }

            if (success)
            {
                task.OutputX = x;
                task.OutputY = y;
                task.OutputText = $"已点击坐标: ({x}, {y})";
                Log($"[{DateTime.Now:HH:mm:ss}] Win点击成功: ({x}, {y})");
            }

            return success;
        }

        /// <summary>
        /// Win双击/多次点击处理
        /// </summary>
        private async Task<bool> ExecuteWinMultiClickAsync(WinClickTaskCard task, int x, int y)
        {
            if (task.MultiClickEnabled && task.MultiClickCount > 1)
            {
                // 多次点击模式
                for (int i = 0; i < task.MultiClickCount; i++)
                {
                    bool ok = await Win32Helper.ClickAsync(x, y);
                    if (!ok) return false;
                    if (i < task.MultiClickCount - 1)
                        await Task.Delay(task.ClickIntervalMs); // 点击间隔
                }
                Log($"[{DateTime.Now:HH:mm:ss}] Win多次点击: ({x}, {y}) x{task.MultiClickCount}");
                return true;
            }
            else
            {
                // 标准双击
                return await Win32Helper.DoubleClickAsync(x, y);
            }
        }

        /// <summary>
        /// 离屏双击/多次点击处理
        /// </summary>
        private async Task<bool> ExecuteWinMultiClickOffScreenAsync(WinClickTaskCard task, IntPtr hwnd, int x, int y)
        {
            if (task.MultiClickEnabled && task.MultiClickCount > 1)
            {
                // 多次点击模式
                for (int i = 0; i < task.MultiClickCount; i++)
                {
                    bool ok = await Win32Helper.PostMessageClickAsync(hwnd, x, y);
                    if (!ok) return false;
                    if (i < task.MultiClickCount - 1)
                        await Task.Delay(task.ClickIntervalMs); // 点击间隔
                }
                Log($"[{DateTime.Now:HH:mm:ss}] 离屏多次点击: ({x}, {y}) x{task.MultiClickCount}");
                return true;
            }
            else
            {
                // 标准双击
                return await Win32Helper.PostMessageDoubleClickAsync(hwnd, x, y);
            }
        }

        /// <summary>
        /// ADB双击/多次点击处理
        /// </summary>
        private async Task<(bool success, string message)> ExecuteAdbMultiClickAsync(AdbClickTaskCard task, int x, int y)
        {
            if (task.MultiClickEnabled && task.MultiClickCount > 1)
            {
                // 多次点击模式
                for (int i = 0; i < task.MultiClickCount; i++)
                {
                    var r = await _adbService.ClickAsync(task.DeviceSerial, x, y);
                    if (!r.Success) return r;
                    if (i < task.MultiClickCount - 1)
                        await Task.Delay(task.ClickIntervalMs); // 点击间隔
                }
                Log($"[{DateTime.Now:HH:mm:ss}] ADB多次点击: ({x}, {y}) x{task.MultiClickCount}");
                return (true, $"多次点击成功: {task.MultiClickCount}次");
            }
            else
            {
                // 标准双击
                return await _adbService.DoubleClickAsync(task.DeviceSerial, x, y);
            }
        }

        private bool ExecuteWinCloseApp(WinCloseAppTaskCard task)
        {
            if (string.IsNullOrEmpty(task.ProcessName))
            {
                task.ErrorMessage = "进程名称为空";
                return false;
            }

            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(task.ProcessName);
                if (processes.Length == 0)
                {
                    task.ErrorMessage = $"未找到进程: {task.ProcessName}";
                    return false;
                }

                int killed = 0;
                foreach (var proc in processes)
                {
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                        killed++;
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                Log($"[{DateTime.Now:HH:mm:ss}] Win关闭应用: {task.ProcessName}, 已关闭 {killed} 个进程");
                task.OutputText = $"已关闭 {killed} 个 {task.ProcessName} 进程";
                return killed > 0;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"关闭应用失败: {ex.Message}";
                return false;
            }
        }

        private async Task<bool> ExecuteAdbConnectAsync(AdbConnectTaskCard task)
        {
            var result = await _adbService.ConnectAsync(task.DeviceIp, task.DevicePort);
            if (!result.Success)
            {
                task.ErrorMessage = result.Message;
            }
            return result.Success;
        }

        private Task<bool> ExecuteWinUiAutomationAsync(WinUiAutomationTaskCard task)
        {
            if (string.IsNullOrEmpty(task.ProcessName))
            {
                task.ErrorMessage = "进程名称为空";
                return Task.FromResult(false);
            }

            // 根据查找方式验证输入
            if (task.SearchBy == UiSearchBy.Name && string.IsNullOrEmpty(task.ButtonName))
            {
                task.ErrorMessage = "按钮名称为空";
                return Task.FromResult(false);
            }
            if (task.SearchBy == UiSearchBy.AutomationId && string.IsNullOrEmpty(task.AutomationId))
            {
                task.ErrorMessage = "AutomationId为空";
                return Task.FromResult(false);
            }

            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(task.ProcessName);
                Process? proc = null;
                try
                {
                if (processes.Length == 0)
                {
                    task.ErrorMessage = $"未找到进程: {task.ProcessName}";
                    return Task.FromResult(false);
                }

                proc = processes[0];
                var mainWindowHandle = proc.MainWindowHandle;

                if (mainWindowHandle == IntPtr.Zero)
                {
                    task.ErrorMessage = $"进程 {task.ProcessName} 没有主窗口";
                    return Task.FromResult(false);
                }

                var windowElement = System.Windows.Automation.AutomationElement.FromHandle(mainWindowHandle);
                System.Windows.Automation.AutomationElement? buttonElement = null;

                if (task.SearchBy == UiSearchBy.AutomationId)
                {
                    // 按 AutomationId 精确查找
                    var condition = new System.Windows.Automation.AndCondition(
                        new System.Windows.Automation.PropertyCondition(
                            System.Windows.Automation.AutomationElement.AutomationIdProperty, task.AutomationId),
                        new System.Windows.Automation.PropertyCondition(
                            System.Windows.Automation.AutomationElement.ControlTypeProperty,
                            System.Windows.Automation.ControlType.Button)
                    );
                    buttonElement = windowElement.FindFirst(
                        System.Windows.Automation.TreeScope.Descendants, condition);

                    if (buttonElement == null)
                    {
                        task.ErrorMessage = $"在窗口中未找到 AutomationId 为 '{task.AutomationId}' 的按钮";
                        return Task.FromResult(false);
                    }
                }
                else
                {
                    // 按名称查找
                    buttonElement = FindButtonByName(windowElement, task.ButtonName, task.MatchMode);

                    if (buttonElement == null)
                    {
                        task.ErrorMessage = $"在窗口中未找到按钮: {task.ButtonName} (匹配方式: {task.MatchMode})";
                        return Task.FromResult(false);
                    }
                }

                // 尝试使用 InvokePattern 点击按钮
                if (buttonElement.TryGetCurrentPattern(
                    System.Windows.Automation.InvokePattern.Pattern, out object? pattern))
                {
                    ((System.Windows.Automation.InvokePattern)pattern).Invoke();
                    string actualName = buttonElement.Current.Name ?? "";
                    string identifier = task.SearchBy == UiSearchBy.AutomationId
                        ? $"AutomationId='{task.AutomationId}'"
                        : $"名称='{actualName}'";
                    Log($"[{DateTime.Now:HH:mm:ss}] WinUI自动化: 已点击按钮 {identifier} ({task.ProcessName})");
                    return Task.FromResult(true);
                }

                task.ErrorMessage = $"按钮不支持点击操作";
                return Task.FromResult(false);
                }
                finally
                {
                    foreach (var p in processes) p.Dispose();
                }
            }
            catch (Exception ex)
            {
                task.ErrorMessage = string.Format(Strings.Svc_UIAutoFailed, ex.Message);
                return Task.FromResult(false);
            }
        }

        private async Task<bool> ExecuteWinSimulateInputAsync(WinSimulateInputTaskCard task)
        {
            try
            {
                var success = await Win32Helper.SimulateInputAsync(
                    task.ModifierKey, task.ActionType,
                    task.KeyName, task.ScrollAmount,
                    task.RepeatCount, task.IntervalMs);

                if (success)
                {
                    string modStr = task.ModifierKey != ModifierKeyType.None ? $"{task.ModifierKey} + " : "";
                    string actionStr = task.ActionType switch
                    {
                        InputActionType.ScrollUp => $"滚轮向上({task.ScrollAmount})",
                        InputActionType.ScrollDown => $"滚轮向下({task.ScrollAmount})",
                        InputActionType.KeyPress => string.Format(Strings.Svc_KeyPress, task.KeyName),
                        _ => Strings.Svc_Unknown
                    };
                    task.OutputText = $"{modStr}{actionStr} x{task.RepeatCount}";
                    Log($"[{DateTime.Now:HH:mm:ss}] Win模拟输入: {task.OutputText}");
                    return true;
                }

                task.ErrorMessage = "Win模拟输入失败";
                return false;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 按名称查找按钮，支持精确/包含/正则三种匹配方式
        /// </summary>
        private static System.Windows.Automation.AutomationElement? FindButtonByName(
            System.Windows.Automation.AutomationElement window, string buttonName, UiMatchMode matchMode)
        {
            if (matchMode == UiMatchMode.Exact)
            {
                // 精确匹配：直接使用 PropertyCondition
                var condition = new System.Windows.Automation.AndCondition(
                    new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.NameProperty, buttonName),
                    new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.ControlTypeProperty,
                        System.Windows.Automation.ControlType.Button)
                );
                return window.FindFirst(System.Windows.Automation.TreeScope.Descendants, condition);
            }

            // 包含 / 正则：遍历所有按钮
            var allButtons = window.FindAll(
                System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Button));

            foreach (System.Windows.Automation.AutomationElement btn in allButtons)
            {
                string btnName = btn.Current.Name ?? "";
                bool isMatch = matchMode switch
                {
                    UiMatchMode.Contains => btnName.Contains(buttonName, StringComparison.OrdinalIgnoreCase),
                    UiMatchMode.Regex => System.Text.RegularExpressions.Regex.IsMatch(btnName, buttonName, 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                    _ => false
                };

                if (isMatch) return btn;
            }

            return null;
        }

        /// <summary>
        /// 执行 Win 字幕提示任务
        /// </summary>
        private async Task<bool> ExecuteWinSubtitleAsync(WinSubtitleTaskCard task, IList<TaskCardBase> allTasks)
        {
            if (string.IsNullOrEmpty(task.DisplayText))
            {
                task.ErrorMessage = "显示文本为空";
                return false;
            }

            try
            {
                // 解析表达式：@变量引用 和 #任务引用
                string resolvedText = task.DisplayText;
                try
                {
                    resolvedText = _variableStore.ResolveVariableReferences(resolvedText);
                    resolvedText = ExpressionEvaluator.ResolveExpression(resolvedText, allTasks, _variableStore);
                }
                catch
                {
                    // 解析失败时使用原始文本
                }

                // 调用字幕服务显示字幕
                await _subtitleService.ShowSubtitleWithDurationAsync(
                    task.Id.ToString(),
                    task.ProcessName,
                    resolvedText,
                    task.OffsetX, task.OffsetY,
                    task.SubtitleWidth, task.SubtitleHeight,
                    task.FontSize, task.TextColor,
                    task.Background, task.BackgroundColor,
                    task.DurationMs, task.WaitUntilClosed,
                    task.SampleMaskPath);

                Log($"[{DateTime.Now:HH:mm:ss}] Win字幕提示: \"{resolvedText}\" (进程: {task.ProcessName})");
                return true;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"字幕显示失败: {ex.Message}";
                return false;
            }
        }


    }
}

