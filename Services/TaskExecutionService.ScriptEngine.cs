using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Services
{
    public partial class TaskExecutionService
    {
        /// <summary>编译缓存：Key = 代码的MD5哈希, Value = 已编译的脚本委托</summary>
        private static readonly ConcurrentDictionary<string, ScriptRunner<object>> _scriptCache = new();

        /// <summary>
        /// 执行自定义脚本任务卡片
        /// </summary>
        private async Task<bool> ExecuteCustomScriptAsync(
            CustomScriptTaskCard task,
            IList<TaskCardBase> allTasks,
            CancellationToken cancellationToken)
        {
            // 1. 构建 TaskFlowPro 上下文（脚本结束后自动释放追踪的所有可释放对象）
            TaskFlowProContext? context = null;
            try
            {
                if (string.IsNullOrWhiteSpace(task.ScriptCode))
                {
                    task.ErrorMessage = "脚本代码为空";
                    return false;
                }

                context = new TaskFlowProContext(allTasks, _variableStore, task);

                // 2. 计算代码哈希，查找编译缓存
                string codeHash = ComputeScriptHash(task.ScriptCode);
                if (!_scriptCache.TryGetValue(codeHash, out var runner))
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 正在编译脚本...");

                    // 3. Roslyn 编译（首次或代码变更时）
                    // 预分离代码中的 using 语句，提取命名空间
                    // 局部代码块里出现 using 命名空间声明会导致致命的编译错误(CS0106/CS1529)
                    var codeLines = task.ScriptCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    var cleanedCode = new StringBuilder();
                    var extractedImports = new List<string>();

                    foreach (var line in codeLines)
                    {
                        var trimmed = line.Trim();
                        // 过滤掉引入命名空间的 using 语句（末尾分号，且不包含等号即非 using别名或using var，不包含左括号即将非using(...)块）
                        if (trimmed.StartsWith("using ") && trimmed.EndsWith(";") && !trimmed.Contains("=") && !trimmed.Contains("("))
                        {
                            var ns = trimmed.Substring(6, trimmed.Length - 7).Trim();
                            extractedImports.Add(ns);
                            continue;
                        }
                        cleanedCode.AppendLine(line);
                    }

                    // 3. Roslyn 编译（首次或代码变更时）
                    var options = ScriptOptions.Default
                        .WithReferences(
                            typeof(object).Assembly,                           // System.Private.CoreLib 等
                            typeof(Enumerable).Assembly,                       // System.Linq
                            typeof(System.IO.File).Assembly,                   // System.IO
                            typeof(System.Text.Json.JsonDocument).Assembly,    // System.Text.Json
                            typeof(System.Text.RegularExpressions.Regex).Assembly, // System.Text.RegularExpressions
                            typeof(Console).Assembly,                          // System.Console
                            typeof(OpenCvSharp.Mat).Assembly,                  // OpenCvSharp
                            typeof(OpenCvSharp.Cv2).Assembly,                  // OpenCvSharp 处理方法
                            typeof(TaskFlowProContext).Assembly                // TaskFlow 本体
                        )
                        .WithImports(
                            new[] {
                                "System",
                                "System.IO",
                                "System.Linq",
                                "System.Collections.Generic",
                                "System.Text",
                                "System.Text.RegularExpressions",
                                "System.Text.Json",
                                "OpenCvSharp",
                                "TaskFlow.Services"
                            }.Union(extractedImports).Distinct()
                        )
                        .WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest);

                    // 将用户代码包裹在 { } 块中，解决 Roslyn 脚本模式下
                    // using var 被误判为命名空间导入指令的歧义问题
                    var wrappedCode = "{\n" + cleanedCode.ToString() + "\n}";

                    var script = CSharpScript.Create<object>(
                        wrappedCode,
                        options,
                        globalsType: typeof(ScriptGlobals));

                    // 编译期错误检测
                    var diagnostics = script.Compile(cancellationToken);
                    var errors = diagnostics.Where(d =>
                        d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

                    if (errors.Any())
                    {
                        var errorMsg = new StringBuilder("编译错误:\n");
                        foreach (var err in errors)
                        {
                            var lineSpan = err.Location.GetLineSpan();
                            // 减去包裹代码块 "{" 所添加的 1 行偏移
                            int userLine = lineSpan.StartLinePosition.Line; // 0-indexed, 第0行是 "{"
                            errorMsg.AppendLine($"  行 {userLine}: {err.GetMessage()}");
                        }
                        task.ErrorMessage = errorMsg.ToString();
                        return false;
                    }

                    runner = script.CreateDelegate();
                    _scriptCache[codeHash] = runner;
                    Log($"[{DateTime.Now:HH:mm:ss}] 脚本编译完成（已缓存）");
                }
                else
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] 使用缓存的已编译脚本");
                }

                // 4. 执行脚本（重定向 Console.Out 以捕获 Console.WriteLine 输出）
                var globals = new ScriptGlobals { TaskFlowPro = context };
                var originalOut = Console.Out;
                using var consoleCapture = new StringWriter();
                try
                {
                    Console.SetOut(consoleCapture);
                    await runner(globals, cancellationToken);
                }
                finally
                {
                    Console.SetOut(originalOut);
                }

                // 5. 合并 Console 输出和 TaskFlowPro.Log() 输出
                string consoleOutput = consoleCapture.ToString();
                string contextLog = context.GetLog();
                var combinedLog = new StringBuilder();
                if (!string.IsNullOrEmpty(consoleOutput))
                    combinedLog.Append(consoleOutput);
                if (!string.IsNullOrEmpty(contextLog))
                    combinedLog.Append(contextLog);
                task.OutputLog = combinedLog.ToString();

                Log($"[{DateTime.Now:HH:mm:ss}] 脚本执行完成");
                return true;
            }
            catch (CompilationErrorException compEx)
            {
                task.ErrorMessage = "编译错误:\n" + string.Join("\n", compEx.Diagnostics.Select(d => d.GetMessage()));
                return false;
            }
            catch (OperationCanceledException)
            {
                task.ErrorMessage = "脚本执行已被取消";
                return false;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"脚本运行时错误: {ex.Message}";
                task.OutputLog = (task.OutputLog ?? "") + $"\n[异常] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
                return false;
            }
            finally
            {
                // 无论成功、异常还是取消，都自动释放脚本中获取的所有可释放对象
                context?.Dispose();
            }
        }

        /// <summary>
        /// 计算脚本代码的MD5哈希（用于编译缓存的 Key）
        /// </summary>
        private static string ComputeScriptHash(string code)
        {
            var bytes = Encoding.UTF8.GetBytes(code);
            var hash = MD5.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
