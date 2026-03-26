using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace TaskFlow.Helpers
{
    /// <summary>
    /// AI 流程助手专用日志记录器
    /// 按日期创建日志文件，存放在 logs/ai_flow/ 目录下
    /// </summary>
    public static class AiFlowLogger
    {
        private static readonly object _lock = new();
        private static string? _logDirectory;

        /// <summary>
        /// 获取日志目录路径（exe 同级的 logs/ai_flow/）
        /// </summary>
        private static string GetLogDirectory()
        {
            if (_logDirectory != null) return _logDirectory;

            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            _logDirectory = Path.Combine(assemblyDir, "logs", "ai_flow");

            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);

            return _logDirectory;
        }

        /// <summary>
        /// 获取当前小时的日志文件路径
        /// </summary>
        private static string GetLogFilePath()
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd_HH");
            return Path.Combine(GetLogDirectory(), $"ai_flow_{date}.log");
        }

        /// <summary>
        /// 将文本中所有 base64 图像数据替换为截断占位符，防止日志文件被图像数据污染。
        /// 例如：data:image/png;base64,iVBORw0KGgo... → data:image/png;base64,[省略 xxxxx 字节]
        /// </summary>
        internal static string TruncateBase64(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // 匹配 ;base64, 后跟连续 base64 字符的模式
            const string marker = ";base64,";
            int searchPos = 0;
            var sb = new System.Text.StringBuilder();

            while (true)
            {
                int markerIdx = text.IndexOf(marker, searchPos, StringComparison.Ordinal);
                if (markerIdx < 0) break;

                int dataStart = markerIdx + marker.Length;
                // 收集连续的 base64 字符（A-Z a-z 0-9 + / =）
                int dataEnd = dataStart;
                while (dataEnd < text.Length)
                {
                    char c = text[dataEnd];
                    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                        (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '=')
                        dataEnd++;
                    else
                        break;
                }

                int b64Length = dataEnd - dataStart;
                // 只有长度超过 64 字节才截断（避免误处理短字符串）
                if (b64Length > 64)
                {
                    sb.Append(text, searchPos, dataStart - searchPos); // 保留 ;base64, 前缀
                    sb.Append($"[省略 {b64Length} 字节的图像数据]");
                    searchPos = dataEnd;
                }
                else
                {
                    // 短 base64 保留原文
                    sb.Append(text, searchPos, dataEnd - searchPos);
                    searchPos = dataEnd;
                }
            }

            if (searchPos == 0) return text; // 没有发现 base64，直接返回原文
            sb.Append(text, searchPos, text.Length - searchPos);
            return sb.ToString();
        }

        /// <summary>
        /// 写入一条日志
        /// </summary>
        public static void Log(string level, string message)
        {
            try
            {
                // 在写入前截断 base64 图像数据，防止日志文件膨胀
                var safeMessage = TruncateBase64(message);
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var logLine = $"[{timestamp}] [{level}] {safeMessage}\n";

                lock (_lock)
                {
                    File.AppendAllText(GetLogFilePath(), logLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志写入失败不应影响主流程
            }
        }

        /// <summary>
        /// 记录信息级别日志
        /// </summary>
        public static void Info(string message) => Log("INFO", message);

        /// <summary>
        /// 记录警告级别日志
        /// </summary>
        public static void Warn(string message) => Log("WARN", message);

        /// <summary>
        /// 记录错误级别日志（含异常堆栈）
        /// </summary>
        public static void Error(string message, Exception? ex = null)
        {
            var sb = new StringBuilder(message);
            if (ex != null)
            {
                sb.AppendLine();
                sb.AppendLine($"  异常类型: {ex.GetType().FullName}");
                sb.AppendLine($"  异常消息: {ex.Message}");
                sb.AppendLine($"  堆栈跟踪:\n{ex.StackTrace}");
                if (ex.InnerException != null)
                    sb.AppendLine($"  内部异常: {ex.InnerException.Message}");
            }
            Log("ERROR", sb.ToString());
        }

        /// <summary>
        /// 记录 LLM 请求详情
        /// </summary>
        public static void LogLlmRequest(string stage, string modelId, string endpoint, string requestJson)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"===== LLM 请求 [{stage}] =====");
            sb.AppendLine($"模型ID: {modelId}");
            sb.AppendLine($"端点: {endpoint}");
            sb.AppendLine($"请求体:");
            sb.AppendLine(TruncateBase64(requestJson)); // Truncate base64 data
            sb.AppendLine("=============================");
            Info(sb.ToString());
        }

        /// <summary>
        /// 记录 LLM 响应详情
        /// </summary>
        public static void LogLlmResponse(string stage, string responseJson, int inputTokens, int outputTokens)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"===== LLM 响应 [{stage}] =====");
            sb.AppendLine($"Token 消耗: 输入={inputTokens}, 输出={outputTokens}");
            sb.AppendLine($"响应体:");
            sb.AppendLine(TruncateBase64(responseJson)); // Truncate base64 data
            sb.AppendLine("=============================");
            Info(sb.ToString());
        }

        /// <summary>
        /// 记录卡片创建详情
        /// </summary>
        public static void LogCardCreated(string taskType, string name, int order, string details = "")
        {
            var msg = $"创建卡片: [{taskType}] #{order} \"{name}\"";
            if (!string.IsNullOrEmpty(details))
                msg += $" | {details}";
            Info(msg);
        }

        /// <summary>
        /// 记录分隔线（新会话开始）
        /// </summary>
        public static void LogSessionStart(string userPrompt, string modelId)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n" + new string('=', 60));
            sb.AppendLine($"新会话开始 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"用户输入: {userPrompt}");
            sb.AppendLine($"选择模型: {modelId}");
            sb.AppendLine(new string('=', 60));
            Info(sb.ToString());
        }
    }
}
