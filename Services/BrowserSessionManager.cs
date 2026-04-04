using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace TaskFlow.Services
{
    /// <summary>
    /// 全局 CDP 浏览器会话管理器。
    /// 通过 Chrome DevTools Protocol 附着到已经以
    /// --remote-debugging-port=PORT 启动的 Chrome 实例，所有 Browser 卡片共享同一会话。
    /// </summary>
    public static class BrowserSessionManager
    {
        private static IPlaywright? _playwright;
        private static IBrowser?   _browser;
        private static int _lastCdpPort;
        private static readonly SemaphoreSlim _semaphore = new(1, 1);

        /// <summary>CDP 连接超时（秒）</summary>
        private const int ConnectTimeoutSeconds = 8;

        /// <summary>
        /// 获取（或懒加载创建）CDP 附着的 Browser 实例。
        /// 带超时保护，防止连接失败时无限挂起。
        /// </summary>
        public static async Task<IBrowser> GetBrowserAsync(int cdpPort = 9222)
        {
            // 快速路径：已有可用连接且端口一致
            if (_browser != null && _browser.IsConnected && _lastCdpPort == cdpPort)
                return _browser;

            await _semaphore.WaitAsync();
            try
            {
                // 二次检查
                if (_browser != null && _browser.IsConnected && _lastCdpPort == cdpPort)
                    return _browser;

                // 端口变更或连接断开时，清理旧连接
                if (_browser != null)
                {
                    try { await _browser.DisposeAsync(); } catch { }
                    _browser = null;
                }

                _playwright ??= await Playwright.CreateAsync();

                // 探测 CDP 端口是否可用，支持重试等待（Chrome 启动需要时间）
                const int maxRetries = 5;
                const int retryDelayMs = 1500;
                Exception? lastEx = null;

                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        await http.GetStringAsync($"http://localhost:{cdpPort}/json/version");
                        lastEx = null;
                        break; // 连接成功
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        if (i < maxRetries - 1)
                            await Task.Delay(retryDelayMs); // 等待后重试
                    }
                }

                if (lastEx != null)
                {
                    throw new InvalidOperationException(
                        $"无法连接到 Chrome 调试端口 {cdpPort}（已重试 {maxRetries} 次）。\n" +
                        $"请确认 Chrome 已使用 --remote-debugging-port={cdpPort} 启动，\n" +
                        $"并且必须指定 --user-data-dir 参数。\n" +
                        $"原因: {lastEx.Message}");
                }

                // 创建 CDP 连接，带超时保护
                var endpoint = $"http://localhost:{cdpPort}";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectTimeoutSeconds));

                var connectTask = _playwright.Chromium.ConnectOverCDPAsync(endpoint);
                var completedTask = await Task.WhenAny(connectTask, Task.Delay(-1, cts.Token));

                if (completedTask != connectTask)
                {
                    throw new TimeoutException(
                        $"连接 Chrome CDP 端口 {cdpPort} 超时（{ConnectTimeoutSeconds} 秒）。\n" +
                        $"可能原因：Chrome 未正确以调试模式启动，或该端口被其他应用占用。");
                }

                _browser = await connectTask;
                _lastCdpPort = cdpPort;
                return _browser;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 获取用户当前正在查看的活跃标签页。
        /// 通过 HTTP 接口查询 Chrome 的活跃页面信息。
        /// </summary>
        public static async Task<IPage> GetActivePageAsync(int cdpPort = 9222)
        {
            var browser = await GetBrowserAsync(cdpPort);

            var contexts = browser.Contexts;
            if (contexts.Count == 0)
                throw new InvalidOperationException("Chrome 没有打开的标签页，请先在 Chrome 中打开一个页面。");

            // 收集所有上下文中的所有页面
            var allPages = contexts.SelectMany(c => c.Pages).ToList();
            if (allPages.Count == 0)
                throw new InvalidOperationException("Chrome 没有可用的页面。");

            // 只有一个页面时直接返回
            if (allPages.Count == 1)
                return allPages[0];

            // 通过 Chrome DevTools HTTP API 查询活跃标签页
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var json = await http.GetStringAsync($"http://localhost:{cdpPort}/json/list");
                var targets = System.Text.Json.JsonDocument.Parse(json).RootElement;

                foreach (var target in targets.EnumerateArray())
                {
                    if (target.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "page")
                    {
                        if (target.TryGetProperty("url", out var urlEl))
                        {
                            var activeUrl = urlEl.GetString();
                            if (!string.IsNullOrEmpty(activeUrl))
                            {
                                var matchedPage = allPages.FirstOrDefault(p =>
                                    string.Equals(p.Url, activeUrl, StringComparison.OrdinalIgnoreCase));
                                if (matchedPage != null)
                                    return matchedPage;
                            }
                        }
                    }
                }
            }
            catch
            {
                // HTTP 查询失败时，回退到最后一个页面
            }

            return allPages[^1];
        }

        /// <summary>断开当前会话（在流程停止或出错时调用）</summary>
        public static void Disconnect()
        {
            _browser = null;
            _lastCdpPort = 0;
        }
    }
}
