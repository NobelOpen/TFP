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
        internal static Dictionary<int, (int X, int Y)>? _desktopMarkMappings;

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

        private async Task<bool> ExecuteWinLaunchAppAsync(WinLaunchAppTaskCard task, IList<TaskCardBase> allTasks)
        {
            try
            {
                string exePath = _variableStore.ResolveVariableReferences(task.ExePath);
                exePath = ExpressionEvaluator.ResolveExpression(exePath, allTasks, _variableStore);
                exePath = exePath.Trim().Trim('"');

                string args = _variableStore.ResolveVariableReferences(task.Arguments);
                args = ExpressionEvaluator.ResolveExpression(args, allTasks, _variableStore);
                args = Environment.ExpandEnvironmentVariables(args ?? "");

                var result = await Win32Helper.LaunchApplicationAsync(exePath, args);
                if (!result.Success)
                {
                    task.ErrorMessage = result.Message;
                }
                return result.Success;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"解析异常: {ex.Message}";
                return false;
            }
        }

        private async Task<bool> ExecuteWinScreenshotAsync(WinScreenshotTaskCard task)
        {
            var result = await _screenshotService.CaptureWindowAsync(task.ProcessName, task.IncludeTitleBar, task.CropTopHeight);
            if (result.Success && result.Image != null)
            {
                task.OutputImage?.Dispose();
                task.OutputImage = ApplyGrayscaleIfNeeded(result.Image, task.ConvertToGrayscale);
                // 输出分辨率信息
                task.OutputWidth = task.OutputImage.Width;
                task.OutputHeight = task.OutputImage.Height;
                task.OutputResolution = $"{task.OutputWidth}x{task.OutputHeight}";
                return true;
            }
            task.ErrorMessage = result.Error ?? "截屏失败";
            return false;
        }

        private async Task<bool> ExecuteWinClickAsync(WinClickTaskCard task, IList<TaskCardBase> allTasks)
        {
            int x = task.StartX;
            int y = task.StartY;

            // 优先使用 MarkId 从桌面标注映射表查坐标（SoM 模式）
            if (task.MarkId > 0 && _desktopMarkMappings != null && _desktopMarkMappings.TryGetValue(task.MarkId, out var markPos))
            {
                x = markPos.X;
                y = markPos.Y;
                // 反写回 UI，避免在界面上始终显示为 0
                task.StartX = x;
                task.StartY = y;
                Log($"[{DateTime.Now:HH:mm:ss}] [SoM] MarkId={task.MarkId} → 查表得精确桌面坐标: ({x}, {y})");
            }
            else if (task.MarkId > 0)
            {
                task.ErrorMessage = $"MarkId={task.MarkId} 不在桌面标注映射表中（映射表{(_desktopMarkMappings == null ? "为空" : $"有 {_desktopMarkMappings.Count} 项")}）。请重新执行带有 annotate=true 的截屏。";
                return false;
            }

            // 解析 X/Y 坐标表达式
            if (task.UseVariableCoordinates)
            {
                if (!ResolveCoordinateExpression(task.StartXExpression, "X", ref x, task, allTasks)) return false;
                if (!ResolveCoordinateExpression(task.StartYExpression, "Y", ref y, task, allTasks)) return false;
            }

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
                    // 获取有效的主窗口句柄
                    var validProcess = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
                    if (validProcess != null)
                    {
                        targetHwnd = validProcess.MainWindowHandle;
                    }
                    else
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

                proc = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
                if (proc == null)
                {
                    task.ErrorMessage = $"进程 {task.ProcessName} 没有主窗口";
                    return Task.FromResult(false);
                }

                var mainWindowHandle = proc.MainWindowHandle;

                var windowElement = System.Windows.Automation.AutomationElement.FromHandle(mainWindowHandle);
                System.Windows.Automation.AutomationElement? buttonElement = null;

                if (task.SearchBy == UiSearchBy.AutomationId)
                {
                    // 按 AutomationId 精确查找
                    var condition = new System.Windows.Automation.PropertyCondition(
                            System.Windows.Automation.AutomationElement.AutomationIdProperty, task.AutomationId);
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

                // 尝试使用各种 Pattern 操作控件
                if (buttonElement.TryGetCurrentPattern(
                    System.Windows.Automation.InvokePattern.Pattern, out object? invokePattern))
                {
                    ((System.Windows.Automation.InvokePattern)invokePattern).Invoke();
                }
                else if (buttonElement.TryGetCurrentPattern(
                    System.Windows.Automation.SelectionItemPattern.Pattern, out object? selectionPattern))
                {
                    ((System.Windows.Automation.SelectionItemPattern)selectionPattern).Select();
                }
                else if (buttonElement.TryGetCurrentPattern(
                    System.Windows.Automation.TogglePattern.Pattern, out object? togglePattern))
                {
                    ((System.Windows.Automation.TogglePattern)togglePattern).Toggle();
                }
                else
                {
                    task.ErrorMessage = $"控件(ControlType: {buttonElement.Current.ControlType.ProgrammaticName})不支持点击、选中或切换操作";
                    return Task.FromResult(false);
                }

                string actualName = buttonElement.Current.Name ?? "";
                string identifier = task.SearchBy == UiSearchBy.AutomationId
                    ? $"AutomationId='{task.AutomationId}'"
                    : $"名称='{actualName}'";
                Log($"[{DateTime.Now:HH:mm:ss}] WinUI自动化: 已操作控件 {identifier} ({task.ProcessName})");
                return Task.FromResult(true);
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
                var condition = new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.NameProperty, buttonName);
                return window.FindFirst(System.Windows.Automation.TreeScope.Descendants, condition);
            }

            // 包含 / 正则：遍历所有控件
            // 注意：遍历所有控件可能会较慢，但为了支持所有类型控件必须这么做
            var allButtons = window.FindAll(
                System.Windows.Automation.TreeScope.Descendants,
                System.Windows.Automation.Condition.TrueCondition);

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

        #region WinTextInput 文本输入

        /// <summary>
        /// 执行 Win文本输入：一次性输入一段完整文本
        /// </summary>
        private async Task<bool> ExecuteWinTextInputAsync(
            WinTextInputTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                // 解析文本中的变量和表达式引用
                string text = task.InputText;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    text = _variableStore.ResolveVariableReferences(text);
                    text = ExpressionEvaluator.ResolveExpression(text, allTasks, _variableStore);
                    text = text.Trim('"');
                }

                if (string.IsNullOrEmpty(text))
                {
                    task.ErrorMessage = "输入文本为空";
                    return false;
                }

                if (task.InputMode == TextInputMode.Clipboard)
                {
                    // 剪贴板模式：复制到剪贴板后模拟 Ctrl+V
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        System.Windows.Clipboard.SetText(text);
                    });

                    await Task.Delay(50, ct);

                    // 模拟 Ctrl+V
                    Win32Helper.KeyDown(0xA2); // VK_LCONTROL
                    await Task.Delay(30, ct);
                    Win32Helper.KeyDown(0x56); // V
                    await Task.Delay(30, ct);
                    Win32Helper.KeyUp(0x56);
                    Win32Helper.KeyUp(0xA2);

                    Log($"[{DateTime.Now:HH:mm:ss}] Win文本输入: 剪贴板粘贴 {text.Length} 个字符");
                }
                else
                {
                    // 逐字符模式：使用 SendInput + KEYEVENTF_UNICODE
                    foreach (char c in text)
                    {
                        ct.ThrowIfCancellationRequested();
                        Win32Helper.SendUnicodeChar(c);

                        if (task.CharIntervalMs > 0)
                            await Task.Delay(task.CharIntervalMs, ct);
                    }

                    Log($"[{DateTime.Now:HH:mm:ss}] Win文本输入: 逐字符输入 {text.Length} 个字符 (间隔: {task.CharIntervalMs}ms)");
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                task.ErrorMessage = "任务已取消";
                return false;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }

        #endregion

        #region InputCombo 输入组合

        /// <summary>
        /// 启动 InputCombo 后台任务（fire-and-forget），立即返回 true 不阻塞流程
        /// </summary>
        private bool StartInputComboFireAndForget(
            InputComboTaskCard task, IList<TaskCardBase> allTasks, CancellationToken flowToken)
        {
            if (task.Actions.Count == 0)
            {
                task.ErrorMessage = "按键动作列表为空";
                return false;
            }

            // 创建独立的取消令牌（流程取消或卡片自身取消时停止）
            var cts = CancellationTokenSource.CreateLinkedTokenSource(flowToken);
            task.ComboTokenSource = cts;

            var comboAllTasks = allTasks; // 捕获引用

            // 计数 +1
            Interlocked.Increment(ref _activeComboCount);

            // 后台 fire-and-forget
            _ = Task.Run(async () =>
            {
                var heldKeys = new List<byte>(); // 记录当前被 Hold 的键
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    int iteration = 0;
                    bool infinite = task.RepeatCount == 0;

                    while (!cts.Token.IsCancellationRequested)
                    {
                        // 检查重复次数限制
                        if (!infinite && iteration >= task.RepeatCount) break;

                        // 检查最大时长限制
                        if (task.TotalDurationMs > 0 && stopwatch.ElapsedMilliseconds >= task.TotalDurationMs) break;

                        // 执行一轮动作序列
                        foreach (var action in task.Actions)
                        {
                            if (cts.Token.IsCancellationRequested) break;

                            // 检查是否是鼠标操作
                            if (action.Key.Equals("LeftClick", StringComparison.OrdinalIgnoreCase))
                            {
                                if (action.Mode == InputComboMode.Hold)
                                {
                                    // 鼠标长按
                                    Win32Helper.MouseLeftDown();
                                }
                                else
                                {
                                    // 鼠标单击
                                    Win32Helper.MouseLeftDown();
                                    await Task.Delay(30, cts.Token);
                                    Win32Helper.MouseLeftUp();
                                }
                            }
                            else
                            {
                                // 键盘操作
                                byte vk = Win32Helper.ParseVirtualKeyPublic(action.Key);
                                if (vk == 0) continue;

                                if (action.Mode == InputComboMode.Hold)
                                {
                                    // 长按：每轮都重复发送 KeyDown 事件
                                    // keybd_event 注入的按键不会触发系统的按键重复机制，
                                    // 所以需要每轮循环都发送 KeyDown 来模拟持续输入
                                    Win32Helper.KeyDown(vk);
                                    heldKeys.Add(vk); // 记录待释放（HashSet 自动去重）
                                }
                                else
                                {
                                    // 单击：按下+短延迟+释放
                                    Win32Helper.KeyDown(vk);
                                    await Task.Delay(30, cts.Token);
                                    Win32Helper.KeyUp(vk);
                                }
                            }

                            // 动作之间的间隔
                            if (action.DelayAfterMs > 0)
                            {
                                await Task.Delay(action.DelayAfterMs, cts.Token);
                            }
                        }

                        iteration++;

                        // 每轮结束后检查终止条件表达式
                        if (!string.IsNullOrWhiteSpace(task.StopExpression) &&
                            !task.StopExpression.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                string resolved = _variableStore.ResolveVariableReferences(task.StopExpression);
                                resolved = ExpressionEvaluator.ResolveExpression(resolved, comboAllTasks, _variableStore);
                                // 如果表达式求值结果为 false/0，停止
                                if (resolved.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) ||
                                    resolved.Trim() == "0")
                                {
                                    break;
                                }
                            }
                            catch
                            {
                                // 表达式求值失败时继续运行
                            }
                        }
                    }

                    task.OutputText = $"输入组合完成: {iteration} 轮";
                    Log($"[{DateTime.Now:HH:mm:ss}] 输入组合完成: {iteration} 轮, 耗时 {stopwatch.ElapsedMilliseconds}ms");
                }
                catch (OperationCanceledException)
                {
                    task.OutputText = "输入组合已取消";
                    Log($"[{DateTime.Now:HH:mm:ss}] 输入组合已取消");
                }
                catch (Exception ex)
                {
                    task.ErrorMessage = ex.Message;
                    task.OutputText = $"输入组合异常: {ex.Message}";
                    Log($"[{DateTime.Now:HH:mm:ss}] 输入组合异常: {ex.Message}");
                }
                finally
                {
                    // 安全网：释放所有被 Hold 的键
                    foreach (var vk in heldKeys)
                    {
                        Win32Helper.KeyUp(vk);
                    }
                    // 释放鼠标（如果有 Hold 的鼠标按键）
                    Win32Helper.MouseLeftUp();

                    heldKeys.Clear();
                    task.ComboTokenSource = null;
                    stopwatch.Stop();

                    // 计数 -1，最后一个结束时触发事件
                    if (Interlocked.Decrement(ref _activeComboCount) <= 0)
                    {
                        InputCombosAllDone?.Invoke(this, EventArgs.Empty);
                    }
                }
            }, cts.Token);

            // 构建描述信息
            var actionDesc = string.Join(", ", task.Actions.Select(a => $"{a.Key}={a.Mode}"));
            task.OutputText = $"输入组合运行中: {actionDesc}";
            Log($"[{DateTime.Now:HH:mm:ss}] 输入组合已启动(非阻塞): {actionDesc}");

            return true;
        }

        /// <summary>
        /// 取消所有正在运行的 InputCombo 后台任务（流程停止时调用）
        /// </summary>
        private void CancelAllInputCombos()
        {
            // 该方法由 Stop() 调用，通过 CancellationToken 链接取消机制
            // 由于 ComboTokenSource 是 LinkedTokenSource，_cts.Cancel() 已经会触发取消
            // 但为安全起见，显式取消所有卡片的令牌
        }

        #endregion


    }
}

