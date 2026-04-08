using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaskFlow.Helpers;

namespace TaskFlow.Services
{
    public class McpServerService
    {
        public static McpServerService Instance { get; } = new McpServerService();

        public delegate Task<string> McpToolCallDelegate(string toolName, JObject arguments);
        
        /// <summary>
        /// 外部绑定：处理工具调用的逻辑。
        /// 返回字符串（会自动包装成 MCP 的 Text Content）
        /// </summary>
        public McpToolCallDelegate? OnToolCall { get; set; }

        private CancellationTokenSource? _cts;
        private Task? _serverTask;

        public void Start()
        {
            if (_serverTask != null) return;
            _cts = new CancellationTokenSource();
            _serverTask = Task.Run(() => ServerLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _serverTask = null;
        }

        private async Task ServerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipeServer = new NamedPipeServerStream(
                        "TaskFlowMcpPipe",
                        PipeDirection.InOut,
                        1, // 一次仅允许一个连接
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipeServer.WaitForConnectionAsync(token);
                    AiFlowLogger.Info("[MCP] 客户端已连接");

                    using var reader = new StreamReader(pipeServer, new UTF8Encoding(false), leaveOpen: true);
                    using var writer = new StreamWriter(pipeServer, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

                    while (!token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(token);
                        if (line == null) break;

                        try
                        {
                            var request = JObject.Parse(line);
                            var response = await HandleRequestAsync(request);
                            if (response != null)
                            {
                                await writer.WriteLineAsync(response.ToString(Formatting.None).AsMemory(), token);
                            }
                        }
                        catch (Exception ex)
                        {
                            AiFlowLogger.Warn($"[MCP] 消息解析或处理错误: {ex.Message}");
                        }
                    }

                    AiFlowLogger.Info("[MCP] 客户端断开连接");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AiFlowLogger.Warn($"[MCP] Pipe Server Error: {ex.Message}");
                    await Task.Delay(2000, token); // 避免崩溃死循环
                }
            }
        }

        private async Task<JObject?> HandleRequestAsync(JObject msg)
        {
            var method = msg["method"]?.ToString();
            var id = msg["id"];

            // 1. 初始化
            if (method == "initialize")
            {
                return new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = new JObject
                    {
                        ["protocolVersion"] = "2024-11-05",
                        ["capabilities"] = new JObject
                        {
                            ["tools"] = new JObject()
                        },
                        ["serverInfo"] = new JObject
                        {
                            ["name"] = "TaskFlow",
                            ["version"] = "1.0.0"
                        }
                    }
                };
            }
            // 2. 初始化确认（不需要响应）
            else if (method == "notifications/initialized")
            {
                return null;
            }
            // 3. 列出工具
            else if (method == "tools/list")
            {
                var tools = AiFlowGeneratorService.BuildToolDefinitions();
                var mcpTools = new JArray();
                foreach(var tool in tools)
                {
                    if (tool["type"]?.ToString() == "function")
                    {
                        var info = tool["function"] as JObject;
                        if (info != null)
                        {
                            mcpTools.Add(new JObject
                            {
                                ["name"] = info["name"],
                                ["description"] = info["description"],
                                ["inputSchema"] = info["parameters"]
                            });
                        }
                    }
                }

                return new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = new JObject
                    {
                        ["tools"] = mcpTools
                    }
                };
            }
            // 4. 调用工具
            else if (method == "tools/call")
            {
                var name = msg["params"]?["name"]?.ToString() ?? "";
                var args = msg["params"]?["arguments"] as JObject ?? new JObject();

                string content;
                bool isError = false;

                try
                {
                    if (OnToolCall != null)
                    {
                        content = await OnToolCall(name, args);
                    }
                    else
                    {
                        content = "Error: Tool executor is not bound directly on the server.";
                        isError = true;
                    }
                }
                catch(Exception ex)
                {
                    content = $"Error: {ex.Message}";
                    isError = true;
                    AiFlowLogger.Warn($"[MCP] Tool execution failed: {ex.Message}");
                }

                return new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = new JObject
                    {
                        ["content"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "text",
                                ["text"] = content
                            }
                        },
                        ["isError"] = isError
                    }
                };
            }

            // 对于未识别的请求，如果是 notification 则忽略，否则返回 MethodNotFound
            if (id != null)
            {
                return new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["error"] = new JObject
                    {
                        ["code"] = -32601,
                        ["message"] = $"Method not found: {method}"
                    }
                };
            }

            return null;
        }
    }
}
