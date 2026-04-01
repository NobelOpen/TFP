using System;
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
        private static readonly object _lock = new();

        /// <summary>
        /// 获取（或懒加载创建）CDP 附着的 Browser 实例。
        /// </summary>
        /// <param name="cdpPort">远程调试端口，默认 9222</param>
        public static async Task<IBrowser> GetBrowserAsync(int cdpPort = 9222)
        {
            // 双重检查加锁（先快速路径，再加锁慢速路径）
            if (_browser != null && _browser.IsConnected)
                return _browser;

            _playwright ??= await Playwright.CreateAsync();

            // 创建 CDP 连接，附着到用户已有的 Chrome 实例
            var endpoint = $"http://localhost:{cdpPort}";
            var browser = await _playwright.Chromium.ConnectOverCDPAsync(endpoint);

            lock (_lock)
            {
                if (_browser == null || !_browser.IsConnected)
                {
                    _browser?.DisposeAsync();
                    _browser = browser;
                }
            }

            return _browser;
        }

        /// <summary>获取当前活跃页面（第 1 个上下文的第 1 个页面）</summary>
        public static async Task<IPage> GetActivePageAsync(int cdpPort = 9222)
        {
            var browser = await GetBrowserAsync(cdpPort);

            // ConnectOverCDP 下，已有上下文会被映射进来
            var contexts = browser.Contexts;
            if (contexts.Count == 0)
                throw new InvalidOperationException("Chrome 没有打开的标签页，请先在 Chrome 中打开一个页面。");

            var pages = contexts[0].Pages;
            if (pages.Count == 0)
                throw new InvalidOperationException("Chrome 没有可用的页面。");

            return pages[0];
        }

        /// <summary>断开当前会话（在流程停止或出错时调用）</summary>
        public static void Disconnect()
        {
            _browser = null;
        }
    }
}
