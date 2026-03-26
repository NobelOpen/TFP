using System;
using System.Diagnostics;
using System.IO;

namespace TaskFlow.Services
{
    /// <summary>
    /// 本地 API 代理服务：启动 Node.js 进程作为反向代理，绕过 Cloudflare TLS 指纹检测
    /// </summary>
    public class LocalProxyService : IDisposable
    {
        private static LocalProxyService? _instance;
        public static LocalProxyService Instance => _instance ??= new LocalProxyService();

        private Process? _proxyProcess;
        private string _currentTargetHost = "";
        private int _port = 9876;

        /// <summary>代理是否正在运行</summary>
        public bool IsRunning => _proxyProcess != null && !_proxyProcess.HasExited;

        /// <summary>当前代理监听端口</summary>
        public int Port => _port;

        /// <summary>获取代理的本地基础 URL</summary>
        public string ProxyBaseUrl => $"http://127.0.0.1:{_port}";

        /// <summary>当前代理的目标域名</summary>
        public string CurrentTargetHost => _currentTargetHost;

        /// <summary>
        /// 确保代理正在运行（如果目标域名变更会重启）
        /// </summary>
        public (bool Success, string Message) EnsureRunning(string targetHost)
        {
            if (string.IsNullOrWhiteSpace(targetHost))
                return (false, "代理目标域名不能为空");

            // 检查 Node.js 是否可用
            if (!IsNodeAvailable())
                return (false, "未检测到 Node.js，请先安装 Node.js (https://nodejs.org)");

            // 如果已在运行且目标相同，直接返回
            if (IsRunning && _currentTargetHost == targetHost)
                return (true, $"代理已在运行 ({ProxyBaseUrl} → {targetHost})");

            // 需要重启（目标变更或未运行）
            Stop();

            var scriptPath = GetProxyScriptPath();
            if (!File.Exists(scriptPath))
                return (false, $"代理脚本不存在: {scriptPath}");

            try
            {
                // 设置环境变量传递目标域名，并传入当前进程PID给代理用于存活监控（自动殉葬）
                var parentPid = Process.GetCurrentProcess().Id;
                var startInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"\"{scriptPath}\" {_port} {targetHost} {parentPid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _proxyProcess = Process.Start(startInfo);
                _currentTargetHost = targetHost;

                // 等待启动（最多 3 秒）
                System.Threading.Thread.Sleep(1000);

                if (_proxyProcess == null || _proxyProcess.HasExited)
                {
                    var error = _proxyProcess?.StandardError.ReadToEnd() ?? "未知错误";
                    return (false, $"代理启动失败: {error}");
                }

                return (true, $"代理已启动 ({ProxyBaseUrl} → {targetHost})");
            }
            catch (Exception ex)
            {
                return (false, $"启动代理异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 将原始 API URL 转换为通过代理的 URL
        /// </summary>
        public string GetProxiedUrl(string originalUrl)
        {
            if (!IsRunning) return originalUrl;

            try
            {
                var uri = new Uri(originalUrl);
                // 保留路径部分，替换 scheme+host 为本地代理
                return $"{ProxyBaseUrl}{uri.PathAndQuery}";
            }
            catch
            {
                return originalUrl;
            }
        }

        /// <summary>停止代理进程</summary>
        public void Stop()
        {
            if (_proxyProcess != null)
            {
                try
                {
                    if (!_proxyProcess.HasExited)
                    {
                        _proxyProcess.Kill(entireProcessTree: true);
                        _proxyProcess.WaitForExit(3000);
                    }
                }
                catch { }
                finally
                {
                    _proxyProcess?.Dispose();
                    _proxyProcess = null;
                    _currentTargetHost = "";
                }
            }
        }

        /// <summary>检查 Node.js 是否已安装</summary>
        private bool IsNodeAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
                return p?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>获取代理脚本路径</summary>
        private string GetProxyScriptPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local_proxy.mjs");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
