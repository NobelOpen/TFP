using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TaskFlow.Helpers;
using TaskFlow.Models.AiFlow;

namespace TaskFlow.Services
{
    /// <summary>
    /// PowerShell 后台执行服务：安全地执行 AI 请求的 PowerShell 命令。
    /// 包含白名单自动执行、危险命令拦截、超时控制和输出截断。
    /// </summary>
    public class PowerShellExecutorService
    {
        /// <summary>
        /// 单条命令输出最大字符数
        /// </summary>
        private const int MaxOutputLength = 20000;

        /// <summary>
        /// 命令最大长度限制（防止注入超长脚本）
        /// 考虑到高阶模型常拼接复杂长命令，放宽限制
        /// </summary>
        private const int MaxCommandLength = 8000;

        /// <summary>
        /// 最大超时秒数
        /// </summary>
        private const int MaxTimeoutSeconds = 30;

        /// <summary>
        /// 只读/查询类白名单命令前缀（自动执行，无需用户批准）
        /// </summary>
        private static readonly string[] SafeCommandPrefixes =
        {
            "get-", "select-", "where-", "sort-", "measure-",
            "format-", "out-string", "convertto-", "convertfrom-",
            "test-path", "test-connection", "resolve-path",
            "split-path", "join-path",
            "[system.", "[math]", "[datetime]", "[environment]",
            "write-output", "echo",
            "$env:", "$psversiontable",
        };

        /// <summary>
        /// 绝对禁止的危险命令模式 — 正则匹配（含通配符的复杂模式）
        /// </summary>
        private static readonly string[] ForbiddenRegexPatterns =
        {
            @"remove-item.*-recurse.*-force",
            @"add-type.*-language.*csharp",
        };

        /// <summary>
        /// 绝对禁止的危险命令模式 — 字面量匹配（简单字符串包含检查）
        /// </summary>
        private static readonly string[] ForbiddenLiteralPatterns =
        {
            "format-volume", "clear-disk", "initialize-disk",
            "rm -rf", "del /s /q",
            "invoke-expression", "iex ",
            "start-process powershell", "start-process cmd",
            "set-executionpolicy",
            "[reflection.assembly]",
        };

        /// <summary>
        /// 执行结果
        /// </summary>
        public class ShellResult
        {
            /// <summary>是否成功执行</summary>
            public bool Success { get; set; }

            /// <summary>标准输出</summary>
            public string Output { get; set; } = "";

            /// <summary>错误输出</summary>
            public string Error { get; set; } = "";

            /// <summary>是否需要用户批准</summary>
            public bool NeedsApproval { get; set; }

            /// <summary>被拦截的原因（如果被拦截）</summary>
            public string? BlockReason { get; set; }

            /// <summary>退出码</summary>
            public int ExitCode { get; set; }

            /// <summary>执行耗时（毫秒）</summary>
            public long ElapsedMs { get; set; }
        }

        /// <summary>
        /// 检查命令安全级别
        /// </summary>
        public ShellResult CheckCommandSafety(AiShellCommand cmd)
        {
            // 检查命令长度
            if (cmd.Command.Length > MaxCommandLength)
            {
                return new ShellResult
                {
                    Success = false,
                    BlockReason = $"命令长度超限（{cmd.Command.Length} > {MaxCommandLength}）"
                };
            }

            // 检查空命令
            if (string.IsNullOrWhiteSpace(cmd.Command))
            {
                return new ShellResult
                {
                    Success = false,
                    BlockReason = "命令为空"
                };
            }

            var cmdLower = cmd.Command.ToLower().Trim();

            // 检查字面量禁止模式（简单字符串包含）
            foreach (var pattern in ForbiddenLiteralPatterns)
            {
                if (cmdLower.Contains(pattern))
                {
                    return new ShellResult
                    {
                        Success = false,
                        BlockReason = $"危险命令已拦截（匹配规则: {pattern}）"
                    };
                }
            }

            // 检查正则禁止模式（含通配符的复杂规则）
            foreach (var pattern in ForbiddenRegexPatterns)
            {
                if (Regex.IsMatch(cmdLower, pattern, RegexOptions.IgnoreCase))
                {
                    return new ShellResult
                    {
                        Success = false,
                        BlockReason = $"危险命令已拦截（匹配规则: {pattern}）"
                    };
                }
            }

            // 检查是否是白名单命令（只读查询类）
            bool isSafe = SafeCommandPrefixes.Any(prefix => cmdLower.StartsWith(prefix));

            // 管道链：检查管道后的命令是否也安全
            if (isSafe && cmdLower.Contains("|"))
            {
                var pipeSegments = cmdLower.Split('|');
                foreach (var segment in pipeSegments.Skip(1))
                {
                    var trimmed = segment.Trim();
                    bool segmentSafe = SafeCommandPrefixes.Any(prefix => trimmed.StartsWith(prefix));
                    if (!segmentSafe)
                    {
                        isSafe = false;
                        break;
                    }
                }
            }

            if (!isSafe)
            {
                return new ShellResult { NeedsApproval = true };
            }

            return new ShellResult { Success = true }; // 安全，可自动执行
        }

        /// <summary>
        /// 执行 PowerShell 命令
        /// </summary>
        public async Task<ShellResult> ExecuteAsync(AiShellCommand cmd, CancellationToken ct = default)
        {
            var timeout = Math.Min(Math.Max(cmd.Timeout, 1), MaxTimeoutSeconds);
            var sw = Stopwatch.StartNew();

            AiFlowLogger.Info($"[PowerShell] 执行: {cmd.Command}");
            AiFlowLogger.Info($"[PowerShell] 用途: {cmd.Description}");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{EscapeCommand(cmd.Command)}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using var process = new Process { StartInfo = psi };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null && outputBuilder.Length < MaxOutputLength)
                        outputBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null && errorBuilder.Length < MaxOutputLength)
                        errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 使用超时等待
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }

                    sw.Stop();
                    var reason = ct.IsCancellationRequested ? "用户取消" : $"执行超时（{timeout}秒）";
                    AiFlowLogger.Warn($"[PowerShell] {reason}");

                    return new ShellResult
                    {
                        Success = false,
                        Output = TruncateOutput(outputBuilder.ToString()),
                        Error = reason,
                        ExitCode = -1,
                        ElapsedMs = sw.ElapsedMilliseconds
                    };
                }

                sw.Stop();
                var output = TruncateOutput(outputBuilder.ToString());
                var error = TruncateOutput(errorBuilder.ToString());

                AiFlowLogger.Info($"[PowerShell] 退出码: {process.ExitCode}, 耗时: {sw.ElapsedMilliseconds}ms");
                if (!string.IsNullOrWhiteSpace(output))
                    AiFlowLogger.Info($"[PowerShell] 输出:\n{output}");
                if (!string.IsNullOrWhiteSpace(error))
                    AiFlowLogger.Warn($"[PowerShell] 错误:\n{error}");

                return new ShellResult
                {
                    Success = process.ExitCode == 0,
                    Output = output,
                    Error = error,
                    ExitCode = process.ExitCode,
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                AiFlowLogger.Warn($"[PowerShell] 执行异常: {ex.Message}");
                return new ShellResult
                {
                    Success = false,
                    Error = $"执行异常: {ex.Message}",
                    ExitCode = -1,
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }
        }

        /// <summary>
        /// 将多条命令的执行结果序列化为 AI 可理解的文本
        /// </summary>
        public static string SerializeResults(List<(AiShellCommand Cmd, ShellResult Result)> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== PowerShell 执行结果 ===");

            foreach (var (cmd, result) in results)
            {
                sb.AppendLine($"命令: {cmd.Command}");
                sb.AppendLine($"用途: {cmd.Description}");
                sb.AppendLine($"状态: {(result.Success ? "成功" : "失败")} (退出码: {result.ExitCode}, 耗时: {result.ElapsedMs}ms)");

                if (!string.IsNullOrWhiteSpace(result.Output))
                    sb.AppendLine($"输出:\n{result.Output}");
                if (!string.IsNullOrWhiteSpace(result.Error))
                    sb.AppendLine($"错误:\n{result.Error}");
                if (!string.IsNullOrEmpty(result.BlockReason))
                    sb.AppendLine($"拦截原因: {result.BlockReason}");

                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 转义命令中的双引号
        /// </summary>
        private static string EscapeCommand(string command)
        {
            return command.Replace("\"", "\\\"");
        }

        /// <summary>
        /// 双极截断：保留头部上下文 + 尾部报错堆栈，中间省略。
        /// 确保 AI 既能看到命令输出开头的环境信息，也能看到末尾的异常堆栈。
        /// </summary>
        private const int TruncateHeadLength = 8000;
        private const int TruncateTailLength = 8000;

        private static string TruncateOutput(string output)
        {
            if (string.IsNullOrEmpty(output)) return output;
            output = output.TrimEnd();
            if (output.Length <= MaxOutputLength) return output;

            var head = output[..TruncateHeadLength];
            var tail = output[^TruncateTailLength..];
            var hiddenCount = output.Length - TruncateHeadLength - TruncateTailLength;
            return $"{head}\n\n... [已省略中间 {hiddenCount} 字符，共 {output.Length} 字符] ...\n\n{tail}";
        }
    }
}
