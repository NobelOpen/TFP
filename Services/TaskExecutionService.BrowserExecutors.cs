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

        /// <summary>
        /// Set-of-Mark 标注映射表缓存（由截图管道填充）。
        /// Key = markId, Value = (CssX, CssY, IsFixed) 全页面绝对 CSS 坐标（或 fixed 元素的视口坐标）。
        /// 每次新的标注截图会覆盖旧缓存。
        /// </summary>
        internal static Dictionary<int, (float CssX, float CssY, bool IsFixed)>? _markMappings;

        private async Task<bool> ExecuteBrowserSimulatedClickAsync(
            BrowserSimulatedClickTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var page = await WithCancellation(BrowserSessionManager.GetActivePageAsync(task.CdpPort), ct);

                float cssX, cssY;
                bool isFixed = false;

                // -------------------------------------------------------
                // 优先使用 MarkId 从标注映射表查询精确 CSS 坐标
                // -------------------------------------------------------
                if (task.MarkId > 0 && _markMappings != null && _markMappings.TryGetValue(task.MarkId, out var markPos))
                {
                    cssX = markPos.CssX;
                    cssY = markPos.CssY;
                    isFixed = markPos.IsFixed;
                    // 回写到卡片属性，让用户能在属性窗口看到实际使用的坐标
                    task.X = (int)Math.Round(cssX);
                    task.Y = (int)Math.Round(cssY);
                    Log($"[{DateTime.Now:HH:mm:ss}] [SoM] MarkId={task.MarkId} → 查表得精确 CSS 坐标: ({cssX:F1}, {cssY:F1}){(isFixed ? " [Fixed定位]" : "")}");
                }
                else if (task.MarkId > 0)
                {
                    // markId 已设置但映射表中找不到（可能页面已变动）
                    task.ErrorMessage = $"MarkId={task.MarkId} 不在标注映射表中（映射表{(_markMappings == null ? "为空" : $"有 {_markMappings.Count} 项")}）。请重新执行标注截图。";
                    task.OutputResult = false;
                    return false;
                }
                else
                {
                    // -------------------------------------------------------
                    // 传统模式：全景截图坐标 → CSS 视口坐标转换
                    // 截图是按 devicePixelRatio 倍率拍摄的，
                    // 图片像素坐标 ÷ DPR = CSS 坐标
                    // -------------------------------------------------------
                    var dpr = await page.EvaluateAsync<double>("window.devicePixelRatio");
                    if (dpr <= 0) dpr = 1.0;

                    cssX = (float)(task.X / dpr);
                    cssY = (float)(task.Y / dpr);
                    Log($"[{DateTime.Now:HH:mm:ss}] 浏览器模拟点击: 图片({task.X},{task.Y}) DPR={dpr} CSS({cssX:F1},{cssY:F1})");
                }

                float viewportX, viewportY;

                if (isFixed)
                {
                    // -------------------------------------------------------
                    // Fixed 定位元素（弹窗/对话框/浮层）：
                    // 坐标已经是视口相对坐标，不需要滚动，直接点击
                    // -------------------------------------------------------
                    viewportX = cssX;
                    viewportY = cssY;
                    Log($"[{DateTime.Now:HH:mm:ss}] [SoM] Fixed元素，跳过滚动，直接使用视口坐标: ({viewportX:F1}, {viewportY:F1})");
                }
                else
                {
                    // -------------------------------------------------------
                    // 普通元素：CSSPageCoord → 滚动 → 视口内坐标
                    // -------------------------------------------------------
                    var viewportSize = page.ViewportSize;
                    int vpWidth = viewportSize?.Width ?? 1920;
                    int vpHeight = viewportSize?.Height ?? 1080;

                    int scrollX = Math.Max(0, (int)(cssX - vpWidth / 3));
                    int scrollY = Math.Max(0, (int)(cssY - vpHeight / 3));

                    await page.EvaluateAsync($"window.scrollTo({scrollX}, {scrollY})");
                    await Task.Delay(150, ct);

                    viewportX = Math.Max(0, Math.Min(cssX - scrollX, vpWidth - 1));
                    viewportY = Math.Max(0, Math.Min(cssY - scrollY, vpHeight - 1));
                }

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

                Log($"[{DateTime.Now:HH:mm:ss}] 浏览器模拟点击成功: 视口({viewportX:F1},{viewportY:F1}){(task.MarkId > 0 ? $" [SoM #{task.MarkId}]" : "")}");
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

        // ----------------------------------------------------------
        // HTTP 静默请求（无需浏览器，后台 HttpClient）
        // ----------------------------------------------------------

        private async Task<bool> ExecuteHttpRequestAsync(
            HttpRequestTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                // 1. 解析 URL 表达式
                string url = _variableStore.ResolveVariableReferences(task.UrlExpression);
                url = ExpressionEvaluator.ResolveExpression(url, allTasks, _variableStore);
                url = url.Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(url))
                {
                    task.ErrorMessage = "URL 为空";
                    return false;
                }

                ct.ThrowIfCancellationRequested();

                // 2. 构建请求
                using var request = new System.Net.Http.HttpRequestMessage(
                    task.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                        ? System.Net.Http.HttpMethod.Post
                        : System.Net.Http.HttpMethod.Get,
                    url);

                // 默认 User-Agent（防止被直接拦截）
                request.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

                // 自定义请求头（每行一个，格式 Key: Value）
                if (!string.IsNullOrWhiteSpace(task.CustomHeaders))
                {
                    var headerLines = task.CustomHeaders.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in headerLines)
                    {
                        var colonIdx = line.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            var key = line.Substring(0, colonIdx).Trim();
                            var val = line.Substring(colonIdx + 1).Trim();
                            request.Headers.TryAddWithoutValidation(key, val);
                        }
                    }
                }

                // POST 请求体
                if (task.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(task.RequestBody))
                {
                    string body = _variableStore.ResolveVariableReferences(task.RequestBody);
                    body = ExpressionEvaluator.ResolveExpression(body, allTasks, _variableStore);
                    request.Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
                }

                // 3. 发送请求（使用共享的静态 HttpClient + 超时取消令牌）
                using var timeoutCts = new CancellationTokenSource(task.TimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                Log($"[{DateTime.Now:HH:mm:ss}] HTTP {task.HttpMethod} → {url}");

                var response = await _sharedHttpClient.SendAsync(request, linkedCts.Token);
                task.OutputStatusCode = (int)response.StatusCode;

                // 4. 读取响应体
                string responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token);

                // 5. 基础 HTML 标签清理（去除 script/style/tag，保留可读文本）
                string cleanedText = StripHtmlTags(responseBody);

                task.OutputText = cleanedText;
                task.OutputResult = response.IsSuccessStatusCode;

                Log($"[{DateTime.Now:HH:mm:ss}] HTTP 响应: {task.OutputStatusCode}, 文本长度: {cleanedText.Length}");
                return true;
            }
            catch (TaskCanceledException)
            {
                task.ErrorMessage = "HTTP 请求超时";
                task.OutputResult = false;
                return false;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"HTTP 请求失败: {ex.Message}";
                task.OutputResult = false;
                return false;
            }
        }

        /// <summary>
        /// 基础 HTML 标签清理：移除 script/style 块和所有 HTML 标签，保留可读纯文本
        /// </summary>
        private static string StripHtmlTags(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            // 移除 <script>...</script> 和 <style>...</style> 块
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                html, @"<(script|style)[^>]*>[\s\S]*?</\1>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 移除所有 HTML 标签
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"<[^>]+>", "");

            // 解码常见 HTML 实体
            cleaned = System.Net.WebUtility.HtmlDecode(cleaned);

            // 压缩连续空白行为最多两个换行
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(\r?\n\s*){3,}", "\n\n");

            return cleaned.Trim();
        }
    }
}
