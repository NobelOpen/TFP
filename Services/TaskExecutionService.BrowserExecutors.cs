using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Playwright;
using TaskFlow.Helpers;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Services
{
    /// <summary>
    /// 浏览器操作任务执行器（CDP 附着模式）
    /// </summary>
    public partial class TaskExecutionService
    {
        // ----------------------------------------------------------
        // 浏览器取文本
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserGetTextAsync(
            BrowserGetTextTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                // 解析选择器表达式
                string selector = _variableStore.ResolveVariableReferences(task.Selector);
                selector = ExpressionEvaluator.ResolveExpression(selector, allTasks, _variableStore);
                selector = selector.Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(selector))
                {
                    task.ErrorMessage = "选择器为空";
                    return false;
                }

                ct.ThrowIfCancellationRequested();

                var page = await WithCancellation(BrowserSessionManager.GetActivePageAsync(task.CdpPort), ct);

                string? result;

                if (string.IsNullOrWhiteSpace(task.AttributeName))
                {
                    // 取 innerText
                    result = task.SelectorType == BrowserSelectorType.XPath
                        ? await WithCancellation(page.InnerTextAsync($"xpath={selector}"), ct)
                        : await WithCancellation(page.InnerTextAsync(selector), ct);
                }
                else
                {
                    // 取指定属性
                    result = task.SelectorType == BrowserSelectorType.XPath
                        ? await WithCancellation(page.GetAttributeAsync($"xpath={selector}", task.AttributeName), ct)
                        : await WithCancellation(page.GetAttributeAsync(selector, task.AttributeName), ct);
                }

                task.OutputText = result ?? string.Empty;
                Log($"[{DateTime.Now:HH:mm:ss}] 浏览器取文本: '{selector}' => \"{task.OutputText}\"");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }

        // ----------------------------------------------------------
        // 浏览器执行脚本
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserExecuteJsAsync(
            BrowserExecuteJsTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                // 解析脚本中的变量引用
                string script = _variableStore.ResolveVariableReferences(task.Script);
                script = ExpressionEvaluator.ResolveExpression(script, allTasks, _variableStore);

                if (string.IsNullOrWhiteSpace(script))
                {
                    task.ErrorMessage = "脚本内容为空";
                    return false;
                }

                ct.ThrowIfCancellationRequested();

                var page = await WithCancellation(BrowserSessionManager.GetActivePageAsync(task.CdpPort), ct);

                // 将用户代码包裹为匿名函数执行
                var wrappedScript = $"() => {{ {script} }}";
                var rawResult = await WithCancellation(page.EvaluateAsync<object?>(wrappedScript), ct);

                task.OutputText = rawResult?.ToString() ?? string.Empty;
                Log($"[{DateTime.Now:HH:mm:ss}] 浏览器执行脚本 => \"{task.OutputText}\"");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }

        // ----------------------------------------------------------
        // 浏览器等待元素
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserWaitForElementAsync(
            BrowserWaitForElementTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                // 解析选择器表达式
                string selector = _variableStore.ResolveVariableReferences(task.Selector);
                selector = ExpressionEvaluator.ResolveExpression(selector, allTasks, _variableStore);
                selector = selector.Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(selector))
                {
                    task.ErrorMessage = "选择器为空";
                    return false;
                }

                ct.ThrowIfCancellationRequested();

                var page = await WithCancellation(BrowserSessionManager.GetActivePageAsync(task.CdpPort), ct);

                // 构建 WaitForSelector 选项
                var state = task.WaitMode == BrowserWaitMode.Hidden
                    ? WaitForSelectorState.Hidden
                    : WaitForSelectorState.Visible;

                var opts = new PageWaitForSelectorOptions
                {
                    State   = state,
                    Timeout = task.TimeoutMs
                };

                // XPath 选择器需要加前缀
                string resolvedSelector = task.SelectorType == BrowserSelectorType.XPath
                    ? $"xpath={selector}"
                    : selector;

                await WithCancellation(page.WaitForSelectorAsync(resolvedSelector, opts), ct);

                task.OutputResult = true;
                string modeStr = task.WaitMode == BrowserWaitMode.Hidden ? "消失" : "出现";
                Log($"[{DateTime.Now:HH:mm:ss}] 浏览器等待元素{modeStr}: '{selector}'");
                return true;
            }
            catch (TimeoutException)
            {
                string modeStr = task.WaitMode == BrowserWaitMode.Hidden ? "消失" : "出现";
                task.ErrorMessage = $"等待元素{modeStr}超时（{task.TimeoutMs}ms）: {task.Selector}";
                task.OutputResult = false;
                return false;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                task.OutputResult = false;
                return false;
            }
        }

        // ----------------------------------------------------------
        // 浏览器原生点击
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserNativeClickAsync(
            BrowserNativeClickTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                string selector = _variableStore.ResolveVariableReferences(task.Selector);
                selector = ExpressionEvaluator.ResolveExpression(selector, allTasks, _variableStore);
                selector = selector.Trim().Trim('"');

                ct.ThrowIfCancellationRequested();
                var page = await WithCancellation(BrowserSessionManager.GetActivePageAsync(task.CdpPort), ct);

                // 选择器为空时，直接使用视口 CSS 坐标进行鼠标操作（快速模式，不做 DPR 转换和滚动）
                // 如需截图坐标自动转换，请使用"浏览器模拟点击"
                if (string.IsNullOrWhiteSpace(selector))
                {
                    if (task.ClickType == ClickType.Swipe)
                    {
                        await page.Mouse.MoveAsync(task.X, task.Y);
                        await page.Mouse.DownAsync();
                        await page.Mouse.MoveAsync(task.EndX, task.EndY, new MouseMoveOptions { Steps = 10 });
                        await page.Mouse.UpAsync();
                        Log($"[{DateTime.Now:HH:mm:ss}] 浏览器原生坐标拖动: ({task.X},{task.Y})->({task.EndX},{task.EndY})");
                    }
                    else
                    {
                        int clickCount = task.ClickType == ClickType.Double
                            ? (task.MultiClickCount > 0 ? task.MultiClickCount : 2)
                            : 1;

                        for (int i = 0; i < clickCount; i++)
                        {
                            await page.Mouse.ClickAsync(task.X, task.Y);
                            if (i < clickCount - 1 && task.ClickIntervalMs > 0)
                                await Task.Delay(task.ClickIntervalMs, ct);
                        }
                        Log($"[{DateTime.Now:HH:mm:ss}] 浏览器原生坐标点击: ({task.X},{task.Y}) x{clickCount}");
                    }

                    task.OutputResult = true;
                    return true;
                }

                // 有选择器时，通过定位器操作
                string locStr = task.SelectorType == BrowserSelectorType.XPath ? $"xpath={selector}" : selector;
                var locator = page.Locator(locStr).First;

                // 判断是否是拖拽
                if (task.ClickType == ClickType.Swipe)
                {
                    string endSelector = _variableStore.ResolveVariableReferences(task.EndSelector);
                    endSelector = ExpressionEvaluator.ResolveExpression(endSelector, allTasks, _variableStore);
                    endSelector = endSelector.Trim().Trim('"');

                    if (!string.IsNullOrWhiteSpace(endSelector))
                    {
                        var endLocStr = task.SelectorType == BrowserSelectorType.XPath ? $"xpath={endSelector}" : endSelector;
                        var endLocator = page.Locator(endLocStr).First;
                        await WithCancellation(locator.DragToAsync(endLocator), ct);
                    }
                    else
                    {
                        // 从元素位置拖动到终点坐标
                        var box = await locator.BoundingBoxAsync();
                        if (box != null)
                        {
                            float startX = box.X + box.Width / 2;
                            float startY = box.Y + box.Height / 2;
                            await page.Mouse.MoveAsync(startX, startY);
                            await page.Mouse.DownAsync();
                            await page.Mouse.MoveAsync(task.EndX, task.EndY, new MouseMoveOptions { Steps = 10 });
                            await page.Mouse.UpAsync();
                        }
                    }
                }
                else
                {
                    var options = new LocatorClickOptions
                    {
                        ClickCount = task.MultiClickCount > 0 ? task.MultiClickCount : 1,
                        Delay = task.ClickIntervalMs > 0 ? task.ClickIntervalMs : 0
                    };

                    // 配置相对点击位置偏移
                    if (task.X != 0 || task.Y != 0)
                        options.Position = new Position { X = task.X, Y = task.Y };

                    if (task.ClickType == ClickType.Double)
                    {
                        var dblOpts = new LocatorDblClickOptions { Delay = options.Delay, Position = options.Position };
                        await WithCancellation(locator.DblClickAsync(dblOpts), ct);
                    }
                    else
                    {
                        await WithCancellation(locator.ClickAsync(options), ct);
                    }
                }

                Log($"[{DateTime.Now:HH:mm:ss}] 浏览器原生点击: '{selector}' 成功");
                task.OutputResult = true;
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                task.OutputResult = false;
                return false;
            }
        }

        // ----------------------------------------------------------
        // 浏览器原生输入
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserNativeInputAsync(
            BrowserNativeInputTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                string selector = _variableStore.ResolveVariableReferences(task.Selector);
                selector = ExpressionEvaluator.ResolveExpression(selector, allTasks, _variableStore);
                selector = selector.Trim().Trim('"');

                string text = _variableStore.ResolveVariableReferences(task.InputText);
                text = ExpressionEvaluator.ResolveExpression(text, allTasks, _variableStore);
                text = text.Trim('"');

                if (string.IsNullOrWhiteSpace(selector))
                {
                    task.ErrorMessage = "选择器为空";
                    return false;
                }

                ct.ThrowIfCancellationRequested();
                var page = await WithCancellation(BrowserSessionManager.GetActivePageAsync(task.CdpPort), ct);

                string locStr = task.SelectorType == BrowserSelectorType.XPath ? $"xpath={selector}" : selector;
                var locator = page.Locator(locStr).First;

                if (task.InputMode == TextInputMode.Clipboard)
                {
                    // 使用 Fill (剪贴板级的瞬间填入)
                    await WithCancellation(locator.FillAsync(text), ct);
                }
                else
                {
                    // 逐字键入
                    await WithCancellation(locator.PressSequentiallyAsync(text, new LocatorPressSequentiallyOptions { Delay = Math.Max(0, task.CharIntervalMs) }), ct);
                }

                Log($"[{DateTime.Now:HH:mm:ss}] 浏览器原生输入: '{text}' 至 '{selector}'");
                task.OutputResult = true;
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                task.OutputResult = false;
                return false;
            }
        }

        // ----------------------------------------------------------
        // 浏览器模拟点击 (视觉/绝对坐标)
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserSimulatedClickAsync(
            BrowserSimulatedClickTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var page = await WithCancellation(BrowserSessionManager.GetActivePageAsync(task.CdpPort), ct);

                // -------------------------------------------------------
                // 全景截图坐标 → CSS 视口坐标转换
                // 截图是按 devicePixelRatio 倍率拍摄的，
                // 图片像素坐标 ÷ DPR = CSS 坐标
                // -------------------------------------------------------
                var dpr = await page.EvaluateAsync<double>("window.devicePixelRatio");
                if (dpr <= 0) dpr = 1.0;

                float cssX = (float)(task.X / dpr);
                float cssY = (float)(task.Y / dpr);

                var viewportSize = page.ViewportSize;
                int vpWidth = viewportSize?.Width ?? 1920;
                int vpHeight = viewportSize?.Height ?? 1080;

                // CSS 坐标 → 计算滚动位置
                int scrollX = Math.Max(0, (int)(cssX - vpWidth / 3));
                int scrollY = Math.Max(0, (int)(cssY - vpHeight / 3));

                await page.EvaluateAsync($"window.scrollTo({scrollX}, {scrollY})");
                await Task.Delay(150, ct);

                // 转换为视口内坐标
                float viewportX = Math.Max(0, Math.Min(cssX - scrollX, vpWidth - 1));
                float viewportY = Math.Max(0, Math.Min(cssY - scrollY, vpHeight - 1));

                if (task.ClickType == ClickType.Double)
                {
                    int clickCount = task.MultiClickCount > 0 ? task.MultiClickCount : 2;
                    for (int i = 0; i < clickCount; i++)
                    {
                        await page.Mouse.ClickAsync(viewportX, viewportY);
                        if (i < clickCount - 1 && task.ClickIntervalMs > 0)
                            await Task.Delay(task.ClickIntervalMs, ct);
                    }
                }
                else
                {
                    await page.Mouse.ClickAsync(viewportX, viewportY);
                }

                Log($"[{DateTime.Now:HH:mm:ss}] 浏览器模拟点击: 图片({task.X},{task.Y}) DPR={dpr} CSS({cssX:F1},{cssY:F1}) 视口({viewportX:F1},{viewportY:F1})");
                task.OutputResult = true;
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                task.OutputResult = false;
                return false;
            }
        }

        // ----------------------------------------------------------
        // CDP 指令执行
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserCdpCommandAsync(
            BrowserCdpCommandTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                string methodName = _variableStore.ResolveVariableReferences(task.MethodName);
                methodName = ExpressionEvaluator.ResolveExpression(methodName, allTasks, _variableStore);
                methodName = methodName.Trim().Trim('"');

                string argsJson = _variableStore.ResolveVariableReferences(task.JsonArguments);
                argsJson = ExpressionEvaluator.ResolveExpression(argsJson, allTasks, _variableStore);

                if (string.IsNullOrWhiteSpace(methodName))
                {
                    task.ErrorMessage = "CDP 方法名为空";
                    return false;
                }

                ct.ThrowIfCancellationRequested();
                var page = await WithCancellation(BrowserSessionManager.GetActivePageAsync(task.CdpPort), ct);
                
                var client = await WithCancellation(page.Context.NewCDPSessionAsync(page), ct);

                var argsDict = string.IsNullOrWhiteSpace(argsJson) || argsJson == "{}"
                    ? new Dictionary<string, object>()
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson);

                var result = await WithCancellation(client.SendAsync(methodName, argsDict), ct);
                task.OutputText = result?.ToString() ?? string.Empty;

                Log($"[{DateTime.Now:HH:mm:ss}] CDP 指令执行: '{methodName}' 成功");
                task.OutputResult = true;
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                task.OutputResult = false;
                return false;
            }
        }

        // ----------------------------------------------------------
        // 浏览器页面截图
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserScreenshotAsync(
            BrowserScreenshotTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var page = await WithCancellation(BrowserSessionManager.GetActivePageAsync(task.CdpPort), ct);

                byte[] bytes = await WithCancellation(page.ScreenshotAsync(new Microsoft.Playwright.PageScreenshotOptions
                {
                    FullPage = task.FullPage
                }), ct);

                if (bytes != null && bytes.Length > 0)
                {
                    // To prevent memory leak, make sure to dispose previous matrix if exists
                    task.OutputImage?.Dispose();
                    task.OutputImage = OpenCvSharp.Mat.FromImageData(bytes, OpenCvSharp.ImreadModes.Color);
                    Log($"[{DateTime.Now:HH:mm:ss}] 浏览器截图成功 (长图: {task.FullPage})");
                    return true;
                }

                task.ErrorMessage = "截图返回空数据";
                return false;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = $"截图执行失败: {ex.Message}";
                return false;
            }
        }

        // ----------------------------------------------------------
        // 辅助方法：让不支持 CancellationToken 的 Playwright 调用可被取消
        // ----------------------------------------------------------

        /// <summary>
        /// 将不接受 CancellationToken 的 Task 包装为可取消的版本。
        /// 当 CancellationToken 被触发时，立即抛出 OperationCanceledException，
        /// 不再等待原始 Task 完成。
        /// </summary>
        private static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            using var reg = ct.Register(() => tcs.TrySetResult(true));
            var completed = await Task.WhenAny(task, tcs.Task);
            if (completed == tcs.Task)
            {
                ct.ThrowIfCancellationRequested();
            }
            return await task;
        }

        /// <summary>
        /// 无返回值版本的可取消包装。
        /// </summary>
        private static async Task WithCancellation(Task task, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            using var reg = ct.Register(() => tcs.TrySetResult(true));
            var completed = await Task.WhenAny(task, tcs.Task);
            if (completed == tcs.Task)
            {
                ct.ThrowIfCancellationRequested();
            }
            await task;
        }
    }
}
