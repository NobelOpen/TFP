using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Models.AiFlow;

namespace TaskFlow.Services
{
    /// <summary>
    /// AI 流程生成服务：两阶段 LLM 调用，生成任务卡片方案
    /// </summary>
    public class AiFlowGeneratorService
    {
        private static readonly HttpClient _httpClient;

        static AiFlowGeneratorService()
        {
            // 使用 WinHttpHandler（与 PowerShell 共享 WinHTTP 栈），避免 Cloudflare TLS 指纹拦截
            var handler = new System.Net.Http.WinHttpHandler();
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        }
        private List<CardDescriptionDef>? _cardDescriptions;

        // ===== Prompt 模板缓存系统 =====
        // 缓存已加载的模板内容和文件最后修改时间
        private static readonly Dictionary<string, (string Content, DateTime LastModified)> _promptCache = new();
        private static readonly object _promptCacheLock = new();

        /// <summary>
        /// 加载 Prompt 模板文件（带文件时间戳缓存，文件修改后自动刷新）
        /// </summary>
        private static string LoadPromptTemplate(string fileName)
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            var filePath = Path.Combine(assemblyDir, "Resources", "Prompts", fileName);

            if (!File.Exists(filePath))
            {
                AiFlowLogger.Warn($"Prompt 模板文件不存在: {filePath}");
                return "";
            }

            var lastWrite = File.GetLastWriteTimeUtc(filePath);

            lock (_promptCacheLock)
            {
                if (_promptCache.TryGetValue(fileName, out var cached) && cached.LastModified == lastWrite)
                {
                    return cached.Content;
                }
            }

            // 读取文件（在锁外执行 I/O）
            var content = File.ReadAllText(filePath, Encoding.UTF8);

            lock (_promptCacheLock)
            {
                _promptCache[fileName] = (content, lastWrite);
            }

            AiFlowLogger.Info($"已加载 Prompt 模板: {fileName}");
            return content;
        }

        /// <summary>
        /// 渲染模板：将 {{占位符}} 替换为实际值
        /// </summary>
        private static string RenderTemplate(string template, Dictionary<string, string> variables)
        {
            var result = template;
            foreach (var (key, value) in variables)
            {
                result = result.Replace($"{{{{{key}}}}}", value ?? "");
            }
            return result;
        }

        /// <summary>
        /// 预热：提前加载卡片描述 + 语义向量（供后台线程在 Orchid 面板打开时调用）
        /// </summary>
        public void WarmupCardDescriptions()
        {
            LoadCardDescriptions(); // 内部有缓存，只首次执行实际加载
        }

        /// <summary>
        /// 加载卡片能力描述资源
        /// </summary>
        private List<CardDescriptionDef> LoadCardDescriptions()
        {
            if (_cardDescriptions != null) return _cardDescriptions;

            try
            {
                // 从嵌入资源或文件加载
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                var jsonPath = Path.Combine(assemblyDir, "Resources", "AiFlowDescriptions.json");

                if (File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath, Encoding.UTF8);
                    _cardDescriptions = JsonConvert.DeserializeObject<List<CardDescriptionDef>>(json) ?? new();
                }
                else
                {
                    _cardDescriptions = new();
                }
            }
            catch
            {
                _cardDescriptions = new();
            }

            // 加载完成后，为语义路由预计算类别向量（按类别聚合描述）
            TryPrecomputeSemanticVectors(_cardDescriptions);

            return _cardDescriptions;
        }

        /// <summary>
        /// 将各卡片的描述提交给语义路由器预计算向量
        /// </summary>
        private static void TryPrecomputeSemanticVectors(List<CardDescriptionDef> cards)
        {
            try
            {
                var router = SemanticRouter.GetInstance();
                if (!router.IsReady) return;

                // 直接传入卡片级别的数据，避免类别合并后文字过长被 ONNX (512 tokens) 截断，同时也避免语义被同类的其他卡片稀释
                var cardDefs = cards.Select(c => (
                    Category: c.Category,
                    TaskType: c.TaskType,
                    Description: c.Description,
                    Usage: c.Usage
                ));

                router.PrecomputeCardVectors(cardDefs);
            }
            catch (Exception ex)
            {
                AiFlowLogger.Warn($"卡片向量预计算失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取所有可用类别
        /// </summary>
        private List<string> GetAllCategories()
        {
            return LoadCardDescriptions()
                .Select(c => c.Category)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// 根据类别列表获取对应卡片的详细描述文本
        /// </summary>
        private string BuildDetailedPrompt(List<string> categories)
        {
            // 如果 categories 为空（如自主模式），使用所有类别
            var effectiveCategories = categories.Count > 0 ? categories : GetAllCategories();
            var cards = LoadCardDescriptions()
                .Where(c => effectiveCategories.Contains(c.Category))
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("以下是你可以使用的任务卡片类型：\n");

            var grouped = cards.GroupBy(c => c.Category);
            foreach (var group in grouped)
            {
                sb.AppendLine($"## {group.Key}");
                foreach (var card in group)
                {
                    sb.AppendLine($"### {card.TaskType}");
                    sb.AppendLine($"- 功能：{card.Description}");
                    sb.AppendLine($"- 适用场景：{card.Usage}");
                    if (card.KeyProperties.Count > 0)
                    {
                        sb.AppendLine($"- 可配置属性：");
                        foreach (var prop in card.KeyProperties)
                            sb.AppendLine($"  - {prop}");
                    }
                    if (card.Outputs.Count > 0)
                    {
                        sb.AppendLine($"- 输出：");
                        foreach (var output in card.Outputs)
                            sb.AppendLine($"  - {output}");
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 阶段1：确定需要的卡片类别。
        /// 优先使用本地语义路由（零 Token 消耗），若模型不可用则回退到 LLM 分类。
        /// </summary>
        public async Task<(List<string> Categories, int InputTokens, int OutputTokens)> DetermineCategoriesAsync(
            string userPrompt, string modelId, CancellationToken cancellationToken)
        {
            var allCategories = GetAllCategories();

            // ===== 优先路径：本地语义路由（零 Token 消耗）=====
            var router = SemanticRouter.GetInstance();
            if (router.IsReady)
            {
                var categories = router.Route(userPrompt, threshold: 0.30f, minCategories: 2);

                // 验证结果：确保返回的类别都是已知的
                categories = categories.Where(c => allCategories.Contains(c)).ToList();
                if (categories.Count == 0)
                    categories = allCategories;

                AiFlowLogger.Info($"[语义路由] 类别匹配结果: [{string.Join(", ", categories)}]（0 Token 消耗）");
                return (categories, 0, 0);
            }

            // ===== 兜底路径：LLM 分类（模型文件不存在时使用）=====
            AiFlowLogger.Info("语义路由不可用，回退到 LLM 分类模式...");

            var modelConfig = LlmModelManager.GetModelById(modelId);
            if (modelConfig == null)
                throw new InvalidOperationException("未找到指定的模型配置");

            var categoryList = string.Join("、", allCategories);
            var categoryTemplate = LoadPromptTemplate("CategoryJudge.md");
            var systemPrompt = RenderTemplate(categoryTemplate, new Dictionary<string, string>
            {
                ["类别列表"] = categoryList
            });

            var requestBody = new
            {
                model = modelConfig.ModelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.1
            };

            var requestJson = JsonConvert.SerializeObject(requestBody, Formatting.Indented);
            AiFlowLogger.LogLlmRequest("阶段1-LLM类别判断", modelId, modelConfig.ApiEndpoint, requestJson);

            var (responseText, inputTokens, outputTokens, _) = await CallLlmAsync(modelConfig, requestBody, cancellationToken);
            AiFlowLogger.LogLlmResponse("阶段1-LLM类别判断", responseText, inputTokens, outputTokens);

            var llmCategories = new List<string>();
            try
            {
                var jsonMatch = System.Text.RegularExpressions.Regex.Match(responseText, @"\[.*\]", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (jsonMatch.Success)
                    llmCategories = JsonConvert.DeserializeObject<List<string>>(jsonMatch.Value) ?? new();
            }
            catch (Exception ex)
            {
                AiFlowLogger.Warn($"LLM类别解析失败，回退到全部类别: {ex.Message}");
                llmCategories = allCategories;
            }

            if (llmCategories.Count == 0) llmCategories = allCategories;

            AiFlowLogger.Info($"[LLM路由] 类别判断结果: [{string.Join(", ", llmCategories)}]");
            return (llmCategories, inputTokens, outputTokens);
        }

        /// <summary>
        /// 构建 Tool Use 工具定义（统一模式：所有工具始终可用，安全由 TaskRiskClassifier 运行时把关）
        /// </summary>
        private static JArray BuildToolDefinitions()
        {
            var tools = new JArray();

            // 工具1: analyze_flow —— 查看流程详情（支持分页）
            tools.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = "analyze_flow",
                    ["description"] = "查看指定流程的详细卡片结构。支持分页：默认返回前 80 个卡片，可通过 start_order 和 count 获取后续卡片。当返回结果提示'已截断'时，使用 start_order 继续查看。",
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["flow_name"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "要查看的流程名称"
                            },
                            ["start_order"] = new JObject
                            {
                                ["type"] = "integer",
                                ["description"] = "从第几号卡片开始查看（默认从头开始）"
                            },
                            ["count"] = new JObject
                            {
                                ["type"] = "integer",
                                ["description"] = "最多返回多少个卡片（默认 2000，用于超大流程分页）"
                            },
                            ["thought"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "调用此工具前的思考过程（必须提供）"
                            }
                        },
                        ["required"] = new JArray("flow_name", "thought")
                    }
                }
            });

            // 工具2: submit_plan —— 提交操作方案
            var planProps = new JObject
            {
                ["plan"] = new JObject { ["type"] = "array", ["description"] = "新建任务卡片列表。每个元素: {step, taskType, name, description, properties, sourceStep?, templateSourceStep?, ifBody?, elseBody?, loopBody?}", ["items"] = new JObject { ["type"] = "object" } },
                ["variables"] = new JObject { ["type"] = "array", ["description"] = "需要声明的变量。每个元素: {name, type(Int/String/Bool/Double), value, description}", ["items"] = new JObject { ["type"] = "object" } },
                ["deleteVariables"] = new JObject { ["type"] = "array", ["description"] = "要删除的变量名列表（不带@前缀）", ["items"] = new JObject { ["type"] = "string" } },
                ["modifyVariables"] = new JObject { ["type"] = "array", ["description"] = "要修改的变量", ["items"] = new JObject { ["type"] = "object" } },
                ["modifyCards"] = new JObject { ["type"] = "array", ["description"] = "修改已有卡片属性: [{order, properties:{key:val}}]", ["items"] = new JObject { ["type"] = "object" } },
                ["deleteCards"] = new JObject { ["type"] = "array", ["description"] = "要删除的卡片序号列表", ["items"] = new JObject { ["type"] = "integer" } },
                ["insertCards"] = new JObject { ["type"] = "array", ["description"] = "向已有分支/循环插入卡片: [{targetBlockOrder, branch(if/else/loop), cards:[...]}]", ["items"] = new JObject { ["type"] = "object" } },
                ["createFlows"] = new JObject { ["type"] = "array", ["description"] = "创建新流程: [{name}]，系统会自动添加 SUB_ 前缀并标记为子流程类型", ["items"] = new JObject { ["type"] = "object" } },
                ["deleteFlows"] = new JObject { ["type"] = "array", ["description"] = "删除的流程名列表", ["items"] = new JObject { ["type"] = "string" } },
                ["targetFlow"] = new JObject { ["type"] = "string", ["description"] = "plan 步骤的目标流程名。指定后，plan 中的卡片将直接创建在该流程中，无需切换 UI 标签页。留空则创建在当前流程。" },
                ["switchFlow"] = new JObject { ["type"] = "string", ["description"] = "[可选] 切换 UI 显示到的目标流程名，纯 UI 操作，不影响卡片创建位置。通常在工作完成后设置以让用户看到结果。" },
                ["thought"] = new JObject { ["type"] = "string", ["description"] = "设计或执行前的思考决策过程（必须提供）" }
            };

            // 执行控制参数（统一内含，AI 视需要使用）
            planProps["runCards"] = new JObject { ["type"] = "array", ["description"] = "要运行的卡片序号", ["items"] = new JObject { ["type"] = "integer" } };
            planProps["done"] = new JObject { ["type"] = "boolean", ["description"] = "自主任务是否全部完成" };
            planProps["failureStrategy"] = new JObject { ["type"] = "string", ["description"] = "失败策略: retry/fallback/abort" };
            planProps["fallbackPlan"] = new JObject { ["type"] = "array", ["description"] = "回退备选方案", ["items"] = new JObject { ["type"] = "object" } };

            tools.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = "submit_plan",
                    ["description"] = "提交流程操作方案。包括创建/修改/删除卡片和变量、管理流程等。纯对话回复不需要调用此工具。",
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = planProps
                    }
                }
            });

            // 工具: execute_shell —— 执行 PowerShell 命令
            tools.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = "execute_shell",
                    ["description"] = "执行 PowerShell 命令。当任务卡片的功能无法满足需求时使用（如查询系统信息、文件操作、安装依赖等）。优先使用任务卡片，只有卡片无法实现时才调用此工具。每次最多 3 条命令。",
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["commands"] = new JObject
                            {
                                ["type"] = "array",
                                ["description"] = "要执行的命令列表",
                                ["items"] = new JObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JObject
                                    {
                                        ["command"] = new JObject { ["type"] = "string", ["description"] = "PowerShell 命令" },
                                        ["description"] = new JObject { ["type"] = "string", ["description"] = "命令用途说明" },
                                        ["timeout"] = new JObject { ["type"] = "integer", ["description"] = "超时时间（秒），默认 10，最大 30" }
                                    },
                                    ["required"] = new JArray("command", "description")
                                }
                            },
                            ["thought"] = new JObject { ["type"] = "string", ["description"] = "决定执行此 Shell 脚本的思考过程（必须提供）" }
                        },
                        ["required"] = new JArray("commands", "thought")
                    }
                }
            });

            // 工具: request_screenshot —— 请求截取屏幕
            tools.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = "request_screenshot",
                    ["description"] = "请求截取屏幕或指定窗口的截图。只在需要查看屏幕内容时使用（如估算坐标、分析界面状态）。不需要视觉信息时不要调用。",
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["target"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "截图目标进程名（如 msedge、notepad），留空或省略则截全屏"
                            },
                            ["thought"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "为什么以及需要截图的哪个区域（思考过程必须提供）"
                            }
                        },
                        ["required"] = new JArray("thought")
                    }
                }
            });

            // ===== 以下工具在所有模式下均可用（只读零风险操作） =====

            // 工具: read_file —— 读取文件内容（支持分页）
            tools.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = "read_file",
                    ["description"] = "读取指定文件的内容。支持分页：默认返回前 200 行，可通过 start_line 和 count 参数获取后续内容。适用于查看代码、配置文件、日志等文本文件。",
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["file_path"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "文件绝对路径"
                            },
                            ["start_line"] = new JObject
                            {
                                ["type"] = "integer",
                                ["description"] = "起始行号（从 1 开始，默认 1）"
                            },
                            ["count"] = new JObject
                            {
                                ["type"] = "integer",
                                ["description"] = "最多返回行数（默认 200，最大 500）"
                            },
                            ["thought"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "为什么需要读取此文件（思考过程必须提供）"
                            }
                        },
                        ["required"] = new JArray("file_path", "thought")
                    }
                }
            });

            // 工具: list_directory —— 列出目录内容
            tools.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = "list_directory",
                    ["description"] = "列出指定目录下的文件和子目录。返回每个条目的名称、类型和大小。适用于了解项目结构、查找文件位置。",
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["path"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "目录绝对路径"
                            },
                            ["recursive"] = new JObject
                            {
                                ["type"] = "boolean",
                                ["description"] = "是否递归列出子目录（默认 false，最深 3 层）"
                            },
                            ["thought"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "为什么需要列出此目录（思考过程必须提供）"
                            }
                        },
                        ["required"] = new JArray("path", "thought")
                    }
                }
            });

            // 工具: search_text —— 在文件中搜索关键词
            tools.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = "search_text",
                    ["description"] = "在指定路径中搜索包含关键词的文件。返回匹配的文件名、行号和行内容。最多返回 50 处匹配。",
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["path"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "搜索起始路径（文件或目录的绝对路径）"
                            },
                            ["query"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "搜索关键词或正则表达式"
                            },
                            ["is_regex"] = new JObject
                            {
                                ["type"] = "boolean",
                                ["description"] = "是否为正则表达式（默认 false）"
                            },
                            ["includes"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "文件名过滤模式（如 *.cs, *.json），默认搜索所有文本文件"
                            },
                            ["thought"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "搜索目的的思考过程（必须提供）"
                            }
                        },
                        ["required"] = new JArray("path", "query", "thought")
                    }
                }
            });

            return tools;
        }

        /// <summary>
        /// 阶段2：生成详细的流程方案（Tool Use 模式）
        /// </summary>
        public async Task<(AiFlowPlanResponse Plan, int InputTokens, int OutputTokens, bool IsTruncated)> GeneratePlanAsync(
            string userPrompt, List<string> categories, string modelId, CancellationToken cancellationToken,
            string currentFlowContext = "", List<(string Role, string Content)>? conversationHistory = null,
            AiAssistantMode mode = AiAssistantMode.Design,
            List<string>? imageBase64List = null,
            Action<string>? onDelta = null,
            Action<string>? onThinking = null,
            Action<string?>? onStatus = null,
            Func<string, int, int, string?>? getFlowDetail = null,
            Func<string, Task<(string? Base64, int Width, int Height)>>? captureScreenshot = null,
            string? prefillAssistantMessage = null)
        {
            var modelConfig = LlmModelManager.GetModelById(modelId);
            if (modelConfig == null)
                throw new InvalidOperationException("未找到指定的模型配置");

            var detailedCards = BuildDetailedPrompt(categories);

            // 构建当前流程上下文段落
            var flowContextSection = string.IsNullOrEmpty(currentFlowContext)
                ? ""
                : $@"
用户当前画布上的流程摘要如下：
{currentFlowContext}
注意：以上仅为流程列表摘要（流程名和卡片数），不包含卡片属性详情。
如果需要查看某个流程的详细卡片结构（例如要修改、追加、分析卡片），必须先调用 analyze_flow 工具获取详情。
收到详情后，你才能正确生成操作方案（通过 submit_plan 工具提交）。
如果用户只是普通对话（如问候、提问），直接用自然语言回答即可。
如果用户在询问关于已有流程的问题（如分析、审查、解释），先调用 analyze_flow 获取详情再回答。
如果用户想在已有流程基础上追加新步骤，请只生成新增的步骤（不要重复已有步骤），step 编号从已有流程之后继续。";

            // 从模板文件加载系统 Prompt（XML 标签化结构），填充占位符
            var baseTemplate = LoadPromptTemplate("SystemBase.md");
            var systemPrompt = RenderTemplate(baseTemplate, new Dictionary<string, string>
            {
                ["卡片描述"] = detailedCards,
                ["流程上下文"] = flowContextSection
            });

            // 构建消息数组（含历史对话）
            var messages = new List<object> { new { role = "system", content = systemPrompt } };

            // 注入最近对话历史（提供上下文连贯性）
            if (conversationHistory != null)
            {
                foreach (var (role, content) in conversationHistory)
                    messages.Add(new { role, content });
            }

            // 添加当前用户消息（支持多模态：文本 + 图片）
            // 检查 history 最后一条是否已经是当前用户输入（避免重复）
            bool lastHistoryIsCurrentUser = conversationHistory != null
                && conversationHistory.Count > 0
                && conversationHistory[^1].Role == "user"
                && conversationHistory[^1].Content == userPrompt;

            if (imageBase64List != null && imageBase64List.Count > 0)
            {
                // 有图像时：若历史末尾已含用户纯文本消息，先移除以避免重复
                if (lastHistoryIsCurrentUser)
                    messages.RemoveAt(messages.Count - 1);

                // OpenAI Vision 格式：content 为数组
                var contentParts = new List<object>();
                if (!string.IsNullOrEmpty(userPrompt))
                {
                    contentParts.Add(new { type = "text", text = userPrompt });
                }
                foreach (var imgB64 in imageBase64List)
                {
                    // 根据 base64 头自动检测格式（PNG 以 iVBOR 开头）
                    var mime = imgB64.StartsWith("iVBOR") ? "image/png" : "image/jpeg";
                    contentParts.Add(new
                    {
                        type = "image_url",
                        image_url = new { url = $"data:{mime};base64,{imgB64}", detail = "high" }
                    });
                }
                messages.Add(new { role = "user", content = (object)contentParts });
            }
            else if (!lastHistoryIsCurrentUser && !string.IsNullOrEmpty(userPrompt))
            {
                messages.Add(new { role = "user", content = (object)userPrompt });
            }

            // 【无缝续写支持】如果在消息末尾追加了中断的 assistant 预填充内容
            if (!string.IsNullOrEmpty(prefillAssistantMessage))
            {
                messages.Add(new { role = "assistant", content = (object)prefillAssistantMessage });
                AiFlowLogger.Info("已附加 Assistant Prefill 断点续写内容进行引导");
            }

            // 构建工具定义（统一模式，所有工具始终可用）
            var tools = BuildToolDefinitions();

            // 使用 JObject 构建请求体以便注入 tools
            var requestObj = new JObject
            {
                ["model"] = modelConfig.ModelName,
                ["messages"] = JArray.FromObject(messages),
                ["temperature"] = 0.3,
                ["tools"] = tools
            };

            var requestJson = requestObj.ToString(Formatting.Indented);
            AiFlowLogger.LogLlmRequest("阶段2-方案生成", modelId, modelConfig.ApiEndpoint, requestJson);

            // 调用 LLM（流式或非流式）
            string responseText;
            int inputTokens, outputTokens;
            List<(string Id, string Name, string Arguments)>? toolCalls;
            bool isTruncated = false;

            if (onDelta != null)
            {
                (responseText, inputTokens, outputTokens, toolCalls, isTruncated) =
                    await CallLlmStreamAsync(modelConfig, requestObj, onDelta, cancellationToken, onThinking);
            }
            else
            {
                (responseText, inputTokens, outputTokens, toolCalls) =
                    await CallLlmAsync(modelConfig, requestObj, cancellationToken);
            }

            AiFlowLogger.LogLlmResponse("阶段2-方案生成", responseText, inputTokens, outputTokens);

            // ======= 处理 Tool Calls =======
            AiFlowPlanResponse plan = new();

            if (toolCalls != null && toolCalls.Count > 0)
            {
                // ---- analyze_flow 多轮循环 ----
                // AI 可能多次调用 analyze_flow（分页浏览大流程），循环处理直到 AI 不再调用 analyze_flow
                var currentToolCalls = toolCalls;
                var currentResponseText = responseText;
                var messagesJson = requestObj["messages"] as JArray ?? new JArray();
                const int maxAnalyzeRounds = 10; // 防止死循环
                int analyzeRound = 0;

                // 多轮对话前清理：移除预填充的末尾续写，因为多轮工具调用不支持首轮带有预填充
                if (!string.IsNullOrEmpty(prefillAssistantMessage))
                {
                    // 由于目前无法合并断点续写与 ToolCall，如果遇到预填充我们就假定模型是在补全 JSON，不会有多轮工具调用
                    AiFlowLogger.Info("注意：已启用断点续写预填充，本轮 Tool Call 循环将被视为常规回复并合并。");
                    responseText = prefillAssistantMessage + responseText; // 拼装后半截
                }

                // 多轮对话前清理：移除用户消息中的 base64 图片数据，避免每轮重复传输
                foreach (var msg in messagesJson)
                {
                    if (msg["role"]?.ToString() != "user") continue;
                    var content = msg["content"];
                    if (content is JArray contentArray)
                    {
                        // 多模态消息：将 image_url 替换为文本占位符
                        var toRemove = new List<JToken>();
                        foreach (var item in contentArray)
                        {
                            if (item["type"]?.ToString() == "image_url")
                                toRemove.Add(item);
                        }
                        if (toRemove.Count > 0)
                        {
                            foreach (var item in toRemove)
                                item.Remove();
                            contentArray.Add(JObject.FromObject(new { type = "text", text = $"[已附带 {toRemove.Count} 张截图，此处省略]" }));
                        }
                    }
                }

                while (analyzeRound < maxAnalyzeRounds)
                {
                    // 找到本轮中所有需要多轮处理的工具调用
                    var analyzeCalls = currentToolCalls?.Where(t => t.Name == "analyze_flow").ToList();
                    var screenshotCall = currentToolCalls?.FirstOrDefault(t => t.Name == "request_screenshot");
                    var fileToolCalls = currentToolCalls?.Where(t => t.Name is "read_file" or "list_directory" or "search_text").ToList();

                    // 优先处理 analyze_flow（支持一次返回多个），其次 request_screenshot，再次文件工具
                    if (analyzeCalls != null && analyzeCalls.Count > 0)
                    {
                        // ---- 处理所有 analyze_flow 调用 ----

                        analyzeRound++;

                        try
                        {
                            // 构建 assistant 消息中的 tool_calls 数组和对应的 tool result 消息
                            var toolCallsArray = new JArray();
                            var toolResultMessages = new List<JObject>();

                            foreach (var analyzeCall in analyzeCalls)
                            {
                                var (afId, afName, afArgs) = analyzeCall;

                                JObject flowArg;
                                try { flowArg = JObject.Parse(afArgs); }
                                catch { continue; }

                                var flowName = flowArg["flow_name"]?.ToString();
                                var startOrder = (int?)flowArg["start_order"] ?? 0;
                                var count = (int?)flowArg["count"] ?? 2000;

                                // 构建 tool_call 条目
                                toolCallsArray.Add(new JObject
                                {
                                    ["id"] = afId,
                                    ["type"] = "function",
                                    ["function"] = new JObject
                                    {
                                        ["name"] = "analyze_flow",
                                        ["arguments"] = afArgs
                                    }
                                });

                                if (string.IsNullOrEmpty(flowName) || getFlowDetail == null)
                                {
                                    toolResultMessages.Add(new JObject
                                    {
                                        ["role"] = "tool",
                                        ["tool_call_id"] = afId,
                                        ["content"] = "错误：未指定流程名"
                                    });
                                    continue;
                                }

                                var flowDetail = getFlowDetail(flowName, startOrder, count);
                                if (flowDetail == null)
                                {
                                    AiFlowLogger.Warn($"[ToolUse] 流程「{flowName}」不存在");
                                    toolResultMessages.Add(new JObject
                                    {
                                        ["role"] = "tool",
                                        ["tool_call_id"] = afId,
                                        ["content"] = $"流程「{flowName}」不存在"
                                    });
                                    continue;
                                }

                                AiFlowLogger.Info($"[ToolUse] analyze_flow(\"{flowName}\", start={startOrder}) → 第 {analyzeRound} 轮对话...");

                                // 超大流程详情二次截断：防止请求体过大触发 502
                                const int MaxFlowDetailLength = 6000;
                                const int FlowDetailHead = 2000;
                                const int FlowDetailTail = 2000;
                                if (flowDetail.Length > MaxFlowDetailLength)
                                {
                                    var fdHead = flowDetail[..FlowDetailHead];
                                    var fdTail = flowDetail[^FlowDetailTail..];
                                    var fdHidden = flowDetail.Length - FlowDetailHead - FlowDetailTail;
                                    flowDetail = $"{fdHead}\n\n... [已省略中间 {fdHidden} 字符，流程详情过长。可通过 start_order 参数分页查看] ...\n\n{fdTail}";
                                    AiFlowLogger.Info($"[ToolUse] 流程详情过长（{flowDetail.Length} 字符），已二次截断");
                                }

                                toolResultMessages.Add(new JObject
                                {
                                    ["role"] = "tool",
                                    ["tool_call_id"] = afId,
                                    ["content"] = flowDetail
                                });
                            }

                            if (toolCallsArray.Count == 0 || toolResultMessages.Count == 0)
                                break;

                            // 构建 assistant 消息（包含所有 tool_calls）
                            var assistantMsg = new JObject
                            {
                                ["role"] = "assistant",
                                ["content"] = currentResponseText ?? ""
                            };
                            assistantMsg["tool_calls"] = toolCallsArray;

                            // 构建精简的多轮请求：只保留 system + 原始 user + 本轮 assistant + 所有 tool results
                            // 避免多轮历史累积导致请求体以 15KB 的倍数增长，触发 502 Bad Gateway
                            var sysMsg = messagesJson.FirstOrDefault(m => m["role"]?.ToString() == "system");
                            var originalUserMsg = messagesJson.LastOrDefault(m => m["role"]?.ToString() == "user");
                            var trimmedMessages = new JArray();
                            if (sysMsg != null) trimmedMessages.Add(sysMsg);
                            if (originalUserMsg != null) trimmedMessages.Add(originalUserMsg);
                            trimmedMessages.Add(assistantMsg);
                            // Chat Completions 协议要求每个 tool_call 对应一条 tool 消息
                            foreach (var toolMsg in toolResultMessages)
                                trimmedMessages.Add(toolMsg);

                            var requestN = new JObject
                            {
                                ["model"] = modelConfig.ModelName,
                                ["messages"] = trimmedMessages,
                                ["temperature"] = 0.3,
                                ["tools"] = tools
                            };

                            onDelta?.Invoke($"\n\n📋 已获取流程详情（第 {analyzeRound} 批，{analyzeCalls.Count} 个流程），正在分析...\n\n");

                            string respN;
                            int inN, outN;
                            List<(string Id, string Name, string Arguments)>? tcN;

                            if (onDelta != null)
                            {
                                (respN, inN, outN, tcN, _) =
                                    await CallLlmStreamAsync(modelConfig, requestN, onDelta, cancellationToken, onThinking);
                            }
                            else
                            {
                                (respN, inN, outN, tcN) =
                                    await CallLlmAsync(modelConfig, requestN, cancellationToken);
                            }

                            AiFlowLogger.LogLlmResponse($"阶段2-第{analyzeRound + 1}轮对话", respN, inN, outN);

                            inputTokens += inN;
                            outputTokens += outN;
                            // 只保留最终轮的回复文本，中间轮的 "继续查看" 丢弃
                            responseText = respN;

                            // 更新当前轮的 tool calls，继续循环检查是否还有 analyze_flow
                            currentToolCalls = tcN;
                            currentResponseText = respN;
                        }
                        catch (Exception ex)
                        {
                            AiFlowLogger.Warn($"[ToolUse] analyze_flow 第 {analyzeRound} 轮处理失败: {ex.Message}");
                            // 根据异常类型设置友好提示，避免上层误报"未能生成有效方案"
                            var isNetworkError = ex.Message.Contains("BadGateway") || ex.Message.Contains("502")
                                || ex.Message.Contains("503") || ex.Message.Contains("429")
                                || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                                || ex is HttpRequestException;
                            plan.Summary = isNetworkError
                                ? $"❌ API 请求失败（{(ex.Message.Contains("502") || ex.Message.Contains("BadGateway") ? "502 Bad Gateway，服务器暂时不可用" : ex.Message)}），请稍后重试。"
                                : $"❌ 流程分析失败: {ex.Message}";
                            onDelta?.Invoke($"\n\n{plan.Summary}\n");
                            break;
                        }
                    } // end analyze_flow
                    else if (screenshotCall != null && screenshotCall.Value.Name == "request_screenshot" && captureScreenshot != null)
                    {
                        // ---- 处理 request_screenshot ----
                        analyzeRound++;
                        var (ssId, _, ssArgs) = screenshotCall.Value;
                        try
                        {
                            var ssArg = JObject.Parse(ssArgs);
                            var target = ssArg["target"]?.ToString() ?? "";
                            // 防御：AI 可能违反指令使用 explorer 作为截图目标，会导致 1x1 空图
                            if (target.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                                target.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                AiFlowLogger.Warn($"[ToolUse] AI 错误使用 explorer 作为截图目标，自动纠正为全屏截图");
                                target = "";
                            }
                            AiFlowLogger.Info($"[ToolUse] request_screenshot(target=\"{target}\") → 截图中...");

                            onStatus?.Invoke("正在截取屏幕...");
                            var (base64, sw, sh) = await captureScreenshot(target);

                            if (base64 == null)
                            {
                                AiFlowLogger.Warn("[ToolUse] 截图失败");
                                onStatus?.Invoke(null);
                                onDelta?.Invoke("⚠️ 截图失败\n");
                                break;
                            }

                            AiFlowLogger.Info($"[ToolUse] 截图成功 ({sw}x{sh})，注入截图发起下一轮对话...");

                            // 将截图直接嵌入 user 消息（不使用 tool_calls/tool 历史格式，
                            // 因为部分第三方 API 代理商不支持 Responses API 的 function_call input 类型）
                            var mime = base64.StartsWith("iVBOR") ? "image/png" : "image/jpeg";
                            messagesJson.Add(new JObject
                            {
                                ["role"] = "user",
                                ["content"] = new JArray
                                {
                                    new JObject { ["type"] = "text", ["text"] = $"[系统截图结果] 截图成功，分辨率 {sw}x{sh}。以下是截取的屏幕截图，请分析内容并继续完成任务。" },
                                    new JObject
                                    {
                                        ["type"] = "image_url",
                                        ["image_url"] = new JObject
                                        {
                                            ["url"] = $"data:{mime};base64,{base64}",
                                            ["detail"] = "high"
                                        }
                                    }
                                }
                            });

                            // 发起下一轮请求
                            var requestN = new JObject
                            {
                                ["model"] = modelConfig.ModelName,
                                ["messages"] = messagesJson,
                                ["temperature"] = 0.3,
                                ["tools"] = tools
                            };

                            string respN;
                            int inN, outN;
                            List<(string Id, string Name, string Arguments)>? tcN;

                            if (onDelta != null)
                            {
                                (respN, inN, outN, tcN, _) =
                                    await CallLlmStreamAsync(modelConfig, requestN, onDelta, cancellationToken, onThinking);
                            }
                            else
                            {
                                (respN, inN, outN, tcN) =
                                    await CallLlmAsync(modelConfig, requestN, cancellationToken);
                            }

                            AiFlowLogger.LogLlmResponse($"阶段2-截图分析轮", respN, inN, outN);

                            inputTokens += inN;
                            outputTokens += outN;
                            responseText = respN;
                            currentToolCalls = tcN;
                            currentResponseText = respN;
                        }
                        catch (Exception ex)
                        {
                            var errMsg = ex.Message.Contains("BadGateway") || ex.Message.Contains("502")
                                ? "❌ 截图后 API 请求被拦截（502 Bad Gateway），当前模型可能需要配置代理才能使用。请在模型设置中启用代理后重试。"
                                : $"❌ 截图分析失败: {ex.Message}";
                            AiFlowLogger.Warn($"[ToolUse] request_screenshot 处理失败: {ex.Message}");
                            onDelta?.Invoke($"\n\n{errMsg}\n");
                            // 设置 Summary 让上层不误报"未能生成有效方案"
                            plan.Summary = errMsg;
                            break;
                        }
                    }
                    else if (fileToolCalls != null && fileToolCalls.Count > 0)
                    {
                        // ---- 处理文件系统工具调用（read_file / list_directory / search_text） ----
                        analyzeRound++;

                        try
                        {
                            var fsService = new FileSystemService();
                            var toolCallsArray = new JArray();
                            var toolResultMessages = new List<JObject>();

                            foreach (var (ftId, ftName, ftArgs) in fileToolCalls)
                            {
                                JObject ftArg;
                                try { ftArg = JObject.Parse(ftArgs); }
                                catch { continue; }

                                toolCallsArray.Add(new JObject
                                {
                                    ["id"] = ftId,
                                    ["type"] = "function",
                                    ["function"] = new JObject
                                    {
                                        ["name"] = ftName,
                                        ["arguments"] = ftArgs
                                    }
                                });

                                string result;
                                switch (ftName)
                                {
                                    case "read_file":
                                        var filePath = ftArg["file_path"]?.ToString() ?? "";
                                        var startLine = (int?)ftArg["start_line"] ?? 1;
                                        var count = (int?)ftArg["count"] ?? 200;
                                        result = fsService.ReadFile(filePath, startLine, count);
                                        break;

                                    case "list_directory":
                                        var dirPath = ftArg["path"]?.ToString() ?? "";
                                        var recursive = (bool?)ftArg["recursive"] ?? false;
                                        result = fsService.ListDirectory(dirPath, recursive);
                                        break;

                                    case "search_text":
                                        var searchPath = ftArg["path"]?.ToString() ?? "";
                                        var query = ftArg["query"]?.ToString() ?? "";
                                        var isRegex = (bool?)ftArg["is_regex"] ?? false;
                                        var includes = ftArg["includes"]?.ToString();
                                        result = fsService.SearchText(searchPath, query, isRegex, includes);
                                        break;

                                    default:
                                        result = $"未知文件工具: {ftName}";
                                        break;
                                }

                                toolResultMessages.Add(new JObject
                                {
                                    ["role"] = "tool",
                                    ["tool_call_id"] = ftId,
                                    ["content"] = result
                                });
                            }

                            if (toolCallsArray.Count == 0 || toolResultMessages.Count == 0)
                                break;

                            // 构建精简的多轮请求
                            var assistantMsg = new JObject
                            {
                                ["role"] = "assistant",
                                ["content"] = currentResponseText ?? ""
                            };
                            assistantMsg["tool_calls"] = toolCallsArray;

                            var sysMsg = messagesJson.FirstOrDefault(m => m["role"]?.ToString() == "system");
                            var originalUserMsg = messagesJson.LastOrDefault(m => m["role"]?.ToString() == "user");
                            var trimmedMessages = new JArray();
                            if (sysMsg != null) trimmedMessages.Add(sysMsg);
                            if (originalUserMsg != null) trimmedMessages.Add(originalUserMsg);
                            trimmedMessages.Add(assistantMsg);
                            foreach (var toolMsg in toolResultMessages)
                                trimmedMessages.Add(toolMsg);

                            var requestN = new JObject
                            {
                                ["model"] = modelConfig.ModelName,
                                ["messages"] = trimmedMessages,
                                ["temperature"] = 0.3,
                                ["tools"] = tools
                            };

                            onDelta?.Invoke($"\n\n📂 已执行 {fileToolCalls.Count} 个文件操作，正在分析结果...\n\n");

                            string respN;
                            int inN, outN;
                            List<(string Id, string Name, string Arguments)>? tcN;

                            if (onDelta != null)
                            {
                                (respN, inN, outN, tcN, _) =
                                    await CallLlmStreamAsync(modelConfig, requestN, onDelta, cancellationToken, onThinking);
                            }
                            else
                            {
                                (respN, inN, outN, tcN) =
                                    await CallLlmAsync(modelConfig, requestN, cancellationToken);
                            }

                            AiFlowLogger.LogLlmResponse($"阶段2-文件工具第{analyzeRound}轮", respN, inN, outN);

                            inputTokens += inN;
                            outputTokens += outN;
                            responseText = respN;
                            currentToolCalls = tcN;
                            currentResponseText = respN;
                        }
                        catch (Exception ex)
                        {
                            AiFlowLogger.Warn($"[ToolUse] 文件工具处理失败: {ex.Message}");
                            plan.Summary = $"❌ 文件操作失败: {ex.Message}";
                            onDelta?.Invoke($"\n\n{plan.Summary}\n");
                            break;
                        }
                    }
                    else
                    {
                        break; // 没有需要多轮处理的工具调用，退出循环
                    }

                } // end while

                // ---- 处理最终轮的非 analyze_flow 工具调用 ----
                var finalToolCalls = currentToolCalls ?? toolCalls;
                foreach (var (tcId, tcName, tcArgs) in finalToolCalls)
                {
                    if (tcName == "analyze_flow" || tcName == "request_screenshot")
                    {
                        // 已在上面多轮循环中处理过，跳过
                        continue;
                    }
                    else if (tcName == "submit_plan")
                    {
                        // AI 提交了操作方案（可能有多个 submit_plan，合并而非覆盖）
                        try
                        {
                            var newPlan = JsonConvert.DeserializeObject<AiFlowPlanResponse>(tcArgs) ?? new();
                            plan = MergeSubmitPlans(plan, newPlan);
                            AiFlowLogger.Info($"[ToolUse] submit_plan: {newPlan.Plan.Count} 个新步骤（合并后共 {plan.Plan.Count} 个步骤, {plan.ModifyCards.Count} 个修改）");
                        }
                        catch (Exception ex)
                        {
                            AiFlowLogger.Warn($"[ToolUse] submit_plan 解析失败: {ex.Message}");
                            // 设置 Summary 防止上层误报"未能生成有效方案"
                            plan.Summary = $"❌ 方案解析失败: {ex.Message}";
                            onDelta?.Invoke($"\n\n{plan.Summary}\n");
                        }
                    }
                    else if (tcName == "execute_shell")
                    {
                        // AI 请求执行 PowerShell 命令 → 映射到 plan.ShellCommands
                        try
                        {
                            var shellArg = JObject.Parse(tcArgs);
                            var cmds = shellArg["commands"] as JArray;
                            if (cmds != null)
                            {
                                plan.ShellCommands ??= new List<Models.AiFlow.AiShellCommand>();
                                foreach (var cmd in cmds)
                                {
                                    plan.ShellCommands.Add(new Models.AiFlow.AiShellCommand
                                    {
                                        Command = cmd["command"]?.ToString() ?? "",
                                        Description = cmd["description"]?.ToString() ?? "",
                                        Timeout = (int?)cmd["timeout"] ?? 10
                                    });
                                }
                                AiFlowLogger.Info($"[ToolUse] execute_shell: {plan.ShellCommands.Count} 条命令");
                            }
                        }
                        catch (Exception ex)
                        {
                            AiFlowLogger.Warn($"[ToolUse] execute_shell 解析失败: {ex.Message}");
                        }
                    }
                    else if (tcName == "request_screenshot")
                    {
                        // AI 请求截屏 → 映射到 plan.NeedsScreenshot
                        try
                        {
                            var ssArg = JObject.Parse(tcArgs);
                            plan.NeedsScreenshot = true;
                            var target = ssArg["target"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(target))
                                plan.ScreenshotTarget = target;
                            AiFlowLogger.Info($"[ToolUse] request_screenshot: target={target ?? "全屏"}");
                        }
                        catch (Exception ex)
                        {
                            AiFlowLogger.Warn($"[ToolUse] request_screenshot 解析失败: {ex.Message}");
                        }
                    }
                }

                // 自然语言回复作为 Summary
                if (string.IsNullOrEmpty(plan.Summary) && !string.IsNullOrWhiteSpace(responseText))
                {
                    plan.Summary = responseText.Trim();
                }
            }
            else
            {
                // 没有 tool_calls —— AI 仅用自然语言回复（兼容不支持 tools 的 API / 回退模式）
                
                // 【无缝续写支持】如果是断点续写，前置原本的半截文本
                if (!string.IsNullOrEmpty(prefillAssistantMessage))
                {
                    responseText = prefillAssistantMessage + responseText;
                }

                // 尝试从 responseText 提取 JSON（兼容旧模式）
                try
                {
                    var jsonMatch = System.Text.RegularExpressions.Regex.Match(
                        responseText, @"\{.*\}", System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (jsonMatch.Success)
                    {
                        plan = JsonConvert.DeserializeObject<AiFlowPlanResponse>(jsonMatch.Value) ?? new();
                        AiFlowLogger.Info($"[Fallback] JSON 解析成功: {plan.Plan.Count} 个步骤");

                        bool isEmpty = plan.Plan.Count == 0
                            && string.IsNullOrEmpty(plan.Summary)
                            && (plan.Variables == null || plan.Variables.Count == 0)
                            && (plan.ModifyCards == null || plan.ModifyCards.Count == 0)
                            && (plan.DeleteCards == null || plan.DeleteCards.Count == 0);
                        if (isEmpty)
                        {
                            plan.Summary = responseText.Trim();
                        }
                    }
                    else
                    {
                        // 检测 AI 是否在文字中表达了需要调用工具的意图却没有实际调用
                        bool hasToolIntent = System.Text.RegularExpressions.Regex.IsMatch(
                            responseText, @"(先(读取|查看|获取|分析)|让我.{0,6}(读取|查看|获取|分析)|需要.{0,6}(读取|查看|获取|分析)|我来.{0,6}(读取|查看|获取|分析)).{0,20}(流程|卡片)");

                        if (hasToolIntent && getFlowDetail != null && onDelta != null)
                        {
                            AiFlowLogger.Info("[ToolUse] AI 纯文本回复但检测到工具调用意图，自动重试引导 AI 使用工具...");
                            onDelta.Invoke("\n\n🔄 正在重新请求...\n\n");

                            // 构建 follow-up 请求，提示 AI 直接使用工具
                            var messagesJson = requestObj["messages"] as JArray ?? new JArray();
                            messagesJson.Add(new JObject
                            {
                                ["role"] = "assistant",
                                ["content"] = responseText
                            });
                            messagesJson.Add(new JObject
                            {
                                ["role"] = "user",
                                ["content"] = "请直接调用 analyze_flow 工具来获取流程详情，不要用文字描述你的意图。"
                            });

                            var retryRequest = new JObject
                            {
                                ["model"] = modelConfig.ModelName,
                                ["messages"] = messagesJson,
                                ["temperature"] = 0.3,
                                ["tools"] = tools
                            };

                            try
                            {
                                string retryResp;
                                int retryIn, retryOut;
                                List<(string Id, string Name, string Arguments)>? retryTc;

                                (retryResp, retryIn, retryOut, retryTc, _) =
                                    await CallLlmStreamAsync(modelConfig, retryRequest, onDelta, cancellationToken, onThinking);

                                AiFlowLogger.LogLlmResponse("阶段2-工具意图重试", retryResp, retryIn, retryOut);
                                inputTokens += retryIn;
                                outputTokens += retryOut;

                                if (retryTc != null && retryTc.Count > 0)
                                {
                                    // 重试成功，AI 这次调用了工具 —— 递归回到 tool_calls 处理流程
                                    // 为简化实现，此处只处理 analyze_flow + submit_plan 的单轮场景
                                    var analyzeRetry = retryTc.FirstOrDefault(t => t.Name == "analyze_flow");
                                    if (analyzeRetry.Name == "analyze_flow" && getFlowDetail != null)
                                    {
                                        var flowArg = JObject.Parse(analyzeRetry.Arguments);
                                        var flowName = flowArg["flow_name"]?.ToString();
                                        if (!string.IsNullOrEmpty(flowName))
                                        {
                                            var flowDetail = getFlowDetail(flowName, 0, 2000);
                                            if (flowDetail != null)
                                            {
                                                AiFlowLogger.Info($"[ToolUse] 重试成功 → analyze_flow(\"{flowName}\")，发起下一轮对话...");
                                                onStatus?.Invoke($"已获取流程详情，正在分析...");

                                                // 构建 tool result 并再次请求
                                                var assistantMsg2 = new JObject
                                                {
                                                    ["role"] = "assistant",
                                                    ["content"] = retryResp ?? ""
                                                };
                                                assistantMsg2["tool_calls"] = new JArray(new JObject
                                                {
                                                    ["id"] = analyzeRetry.Id,
                                                    ["type"] = "function",
                                                    ["function"] = new JObject
                                                    {
                                                        ["name"] = "analyze_flow",
                                                        ["arguments"] = analyzeRetry.Arguments
                                                    }
                                                });
                                                messagesJson.Add(assistantMsg2);
                                                messagesJson.Add(new JObject
                                                {
                                                    ["role"] = "tool",
                                                    ["tool_call_id"] = analyzeRetry.Id,
                                                    ["content"] = flowDetail
                                                });

                                                var finalRequest = new JObject
                                                {
                                                    ["model"] = modelConfig.ModelName,
                                                    ["messages"] = messagesJson,
                                                    ["temperature"] = 0.3,
                                                    ["tools"] = tools
                                                };

                                                string finalResp;
                                                int finalIn, finalOut;
                                                List<(string Id, string Name, string Arguments)>? finalTc;

                                                (finalResp, finalIn, finalOut, finalTc, _) =
                                                    await CallLlmStreamAsync(modelConfig, finalRequest, onDelta, cancellationToken, onThinking);

                                                AiFlowLogger.LogLlmResponse("阶段2-工具意图重试最终轮", finalResp, finalIn, finalOut);
                                                inputTokens += finalIn;
                                                outputTokens += finalOut;
                                                responseText = finalResp;

                                                // 处理最终轮的 submit_plan
                                                if (finalTc != null)
                                                {
                                                    foreach (var (tcId2, tcName2, tcArgs2) in finalTc)
                                                    {
                                                        if (tcName2 == "submit_plan")
                                                        {
                                                            try
                                                            {
                                                                plan = JsonConvert.DeserializeObject<AiFlowPlanResponse>(tcArgs2) ?? new();
                                                                AiFlowLogger.Info($"[ToolUse] 重试最终轮 submit_plan 解析成功: {plan.Plan.Count} 个步骤");
                                                            }
                                                            catch (Exception ex2)
                                                            {
                                                                AiFlowLogger.Warn($"[ToolUse] 重试最终轮 submit_plan 解析失败: {ex2.Message}");
                                                            }
                                                        }
                                                    }
                                                }
                                                if (string.IsNullOrEmpty(plan.Summary) && !string.IsNullOrWhiteSpace(responseText))
                                                    plan.Summary = responseText.Trim();

                                                // 跳过后续的纯文本处理
                                                goto ToolRetryDone;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception retryEx)
                            {
                                AiFlowLogger.Warn($"[ToolUse] 工具意图重试失败: {retryEx.Message}");
                            }
                        }

                        plan = new AiFlowPlanResponse { Summary = responseText.Trim() };
                        AiFlowLogger.Info("[ToolUse] AI 纯文本回复（无工具调用）");
                    }
                }
                catch (Exception ex)
                {
                    plan = new AiFlowPlanResponse { Summary = responseText.Trim() };
                    AiFlowLogger.Warn($"[Fallback] JSON 解析失败: {ex.Message}");
                }
            }

ToolRetryDone:
            return (plan, inputTokens, outputTokens, isTruncated);
        }

        /// <summary>
        /// 判断是否为 Google Gemini API
        /// </summary>
        private static bool IsGeminiApi(string url)
        {
            return url.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase)
                || url.Contains("aiplatform.googleapis.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 为 Gemini API 构建完整的请求 URL
        /// </summary>
        private static string BuildGeminiUrl(string baseUrl, string modelName)
        {
            var cleanModel = modelName.Replace(":generateContent", "").Trim();
            var cleanBase = baseUrl.TrimEnd('/');
            return $"{cleanBase}/models/{cleanModel}:generateContent";
        }

        /// <summary>
        /// 将 OpenAI 格式的 messages 转换为 Gemini 的 contents 格式
        /// </summary>
        private static object BuildGeminiRequestBody(object originalRequestBody)
        {
            var json = JObject.FromObject(originalRequestBody);
            var messages = json["messages"] as JArray;
            if (messages == null) return originalRequestBody;

            var contents = new List<object>();
            string? systemInstruction = null;

            foreach (var msg in messages)
            {
                var role = msg["role"]?.ToString();
                var contentToken = msg["content"];

                if (role == "system")
                {
                    // Gemini 将 system message 放在 systemInstruction 中
                    systemInstruction = contentToken?.ToString() ?? "";
                }
                else
                {
                    var geminiRole = role == "assistant" ? "model" : "user";
                    var parts = new List<object>();

                    if (contentToken is JArray contentArray)
                    {
                        // 多模态内容：将 OpenAI 格式转为 Gemini 格式
                        foreach (var item in contentArray)
                        {
                            var type = item["type"]?.ToString();
                            if (type == "text")
                            {
                                parts.Add(new { text = item["text"]?.ToString() ?? "" });
                            }
                            else if (type == "image_url")
                            {
                                var url = item["image_url"]?["url"]?.ToString() ?? "";
                                // 从 data:image/png;base64,... 格式提取 base64 数据
                                if (url.StartsWith("data:"))
                                {
                                    var commaIdx = url.IndexOf(',');
                                    if (commaIdx > 0)
                                    {
                                        var mimeType = url.Substring(5, url.IndexOf(';') - 5);
                                        var b64Data = url.Substring(commaIdx + 1);
                                        parts.Add(new
                                        {
                                            inline_data = new { mime_type = mimeType, data = b64Data }
                                        });
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // 纯文本内容
                        parts.Add(new { text = contentToken?.ToString() ?? "" });
                    }

                    contents.Add(new { role = geminiRole, parts });
                }
            }

            var result = new Dictionary<string, object>();
            if (systemInstruction != null)
            {
                result["system_instruction"] = new { parts = new[] { new { text = systemInstruction } } };
            }
            result["contents"] = contents;

            // 保留 temperature 设置（如有）
            if (json["temperature"] != null)
            {
                result["generationConfig"] = new { temperature = json["temperature"]!.Value<double>() };
            }

            return result;
        }

        /// <summary>
        /// 调用 LLM API 的通用方法（兼容 OpenAI 和 Gemini），支持返回 tool_calls
        /// </summary>
        private async Task<(string ResponseText, int InputTokens, int OutputTokens, List<(string Id, string Name, string Arguments)>? ToolCalls)> CallLlmAsync(
            LlmModelConfig modelConfig, object requestBody, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = modelConfig.TimeoutSeconds > 0 ? modelConfig.TimeoutSeconds : 60;
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            bool isGemini = IsGeminiApi(modelConfig.ApiEndpoint);
            string actualUrl;
            object actualBody;

            // 将 /v1/responses 端点自动替换为 /v1/chat/completions（与流式方法保持一致）
            var endpoint = modelConfig.ApiEndpoint;
            if (endpoint.Contains("/v1/responses", StringComparison.OrdinalIgnoreCase))
                endpoint = endpoint.Replace("/v1/responses", "/v1/chat/completions");

            if (isGemini)
            {
                actualUrl = BuildGeminiUrl(endpoint, modelConfig.ModelName);
                actualBody = BuildGeminiRequestBody(requestBody);
            }
            else
            {
                actualUrl = endpoint;
                actualBody = requestBody;
            }


            // 如果启用了代理，启动代理并替换 URL
            if (modelConfig.UseProxy && !string.IsNullOrEmpty(modelConfig.ProxyTargetHost))
            {
                var (ok, msg) = LocalProxyService.Instance.EnsureRunning(modelConfig.ProxyTargetHost);
                if (ok)
                    actualUrl = LocalProxyService.Instance.GetProxiedUrl(actualUrl);
                else
                    AiFlowLogger.Warn($"[Proxy] 代理启动失败: {msg}，将直连 API");
            }

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, actualUrl);

            if (isGemini)
            {
                // Gemini 使用 X-goog-api-key 头认证
                requestMessage.Headers.Add("X-goog-api-key", modelConfig.ApiKey);
            }
            else
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", modelConfig.ApiKey);
            }

            requestMessage.Content = new StringContent(
                JsonConvert.SerializeObject(actualBody),
                Encoding.UTF8,
                "application/json");

            // 注入自定义请求头
            ApplyCustomHeaders(requestMessage, modelConfig);

            var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseContentRead, cts.Token);
            var responseString = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"API 请求失败: {response.StatusCode} - {responseString}");
            }

            // 解析响应（统一使用 Chat Completions 格式）
            string replyText;
            int inputTokens = 0, outputTokens = 0;
            List<(string Id, string Name, string Arguments)>? toolCalls = null;

            {
                var jsonResponse = JObject.Parse(responseString);

                // 提取回复文本（兼容 OpenAI 和 Gemini 格式）
                replyText = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString()          // OpenAI Chat
                            ?? jsonResponse["choices"]?[0]?["text"]?.ToString()                      // OpenAI Legacy
                            ?? jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString()  // Gemini
                            ?? jsonResponse["text"]?.ToString()
                            ?? "";

                // 提取非流式 tool_calls（OpenAI Chat 格式）
                var msgToolCalls = jsonResponse["choices"]?[0]?["message"]?["tool_calls"] as JArray;
                if (msgToolCalls != null && msgToolCalls.Count > 0)
                {
                    toolCalls = new List<(string, string, string)>();
                    foreach (var tc in msgToolCalls)
                    {
                        var id = tc["id"]?.ToString() ?? $"call_{Guid.NewGuid():N}";
                        var name = tc["function"]?["name"]?.ToString() ?? "";
                        var args = tc["function"]?["arguments"]?.ToString() ?? "{}";
                        toolCalls.Add((id, name, args));
                        AiFlowLogger.Info($"[NonStream] Tool Call: {name}({args.Substring(0, Math.Min(args.Length, 200))})");
                    }
                }

                // 提取 Token 使用量（兼容 OpenAI 和 Gemini 格式）
                var usage = jsonResponse["usage"];                    // OpenAI
                var usageMeta = jsonResponse["usageMetadata"];        // Gemini
                if (usage != null)
                {
                    inputTokens = (int?)usage["prompt_tokens"] ?? 0;
                    outputTokens = (int?)usage["completion_tokens"] ?? 0;
                }
                else if (usageMeta != null)
                {
                    inputTokens = (int?)usageMeta["promptTokenCount"] ?? 0;
                    outputTokens = (int?)usageMeta["candidatesTokenCount"] ?? 0;
                }
            }

            // 更新模型统计
            if (inputTokens > 0 || outputTokens > 0)
            {
                modelConfig.TotalInputTokens += inputTokens;
                modelConfig.TotalOutputTokens += outputTokens;
            }

            return (replyText, inputTokens, outputTokens, toolCalls);
        }


        /// <summary>
        /// 流式调用 LLM API：逐增量通过 onDelta 回调推送文本到 UI
        /// 支持 Chat Completions（delta.content / delta.tool_calls）、Responses API 和 Gemini
        /// </summary>
        private async Task<(string ResponseText, int InputTokens, int OutputTokens, List<(string Id, string Name, string Arguments)>? ToolCalls, bool IsTruncated)> CallLlmStreamAsync(
            LlmModelConfig modelConfig, object requestBody, Action<string> onDelta, CancellationToken cancellationToken,
            Action<string>? onThinking = null)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = modelConfig.TimeoutSeconds > 0 ? modelConfig.TimeoutSeconds : 120; // 流式给更多时间
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            bool isGemini = IsGeminiApi(modelConfig.ApiEndpoint);
            string actualUrl;
            object actualBody;

            // 将 /v1/responses 端点自动替换为 /v1/chat/completions
            // 原因：Responses API 的 SSE 事件中每帧都携带完整 instructions + tools 定义（约 20KB），
            // 中转站转发时容易触发 Cloudflare/WAF 的 502 Bad Gateway。
            // Chat Completions 的 SSE delta 每帧仅约 200 字节，兼容性和稳定性远优于 Responses API。
            var endpoint = modelConfig.ApiEndpoint;
            if (endpoint.Contains("/v1/responses", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = endpoint.Replace("/v1/responses", "/v1/chat/completions");
                AiFlowLogger.Info($"[协议切换] /v1/responses → /v1/chat/completions (稳定性优化)");
            }

            if (isGemini)
            {
                // Gemini 流式：使用 streamGenerateContent 端点
                var cleanModel = modelConfig.ModelName.Replace(":generateContent", "").Trim();
                var cleanBase = endpoint.TrimEnd('/');
                actualUrl = $"{cleanBase}/models/{cleanModel}:streamGenerateContent?alt=sse";
                actualBody = BuildGeminiRequestBody(requestBody);
            }
            else
            {
                // Chat Completions：注入 stream=true
                actualUrl = endpoint;
                var json = JObject.FromObject(requestBody);
                json["stream"] = true;
                actualBody = json;
            }


            // 如果启用了代理，启动代理并替换 URL
            if (modelConfig.UseProxy && !string.IsNullOrEmpty(modelConfig.ProxyTargetHost))
            {
                var (ok, msg) = LocalProxyService.Instance.EnsureRunning(modelConfig.ProxyTargetHost);
                if (ok)
                    actualUrl = LocalProxyService.Instance.GetProxiedUrl(actualUrl);
                else
                    AiFlowLogger.Warn($"[Proxy] 代理启动失败: {msg}，将直连 API");
            }

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, actualUrl);

            if (isGemini)
            {
                requestMessage.Headers.Add("X-goog-api-key", modelConfig.ApiKey);
            }
            else
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", modelConfig.ApiKey);
            }

            requestMessage.Content = new StringContent(
                JsonConvert.SerializeObject(actualBody),
                Encoding.UTF8,
                "application/json");

            // 注入自定义请求头
            ApplyCustomHeaders(requestMessage, modelConfig);

            // 使用 ResponseHeadersRead 以便逐行读取流
            var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                throw new HttpRequestException($"API 请求失败: {response.StatusCode} - {errorBody}");
            }

            var contentBuilder = new StringBuilder();
            int inputTokens = 0, outputTokens = 0;
            int sseLineCount = 0;
            int rawLineCount = 0;
            bool isTruncated = false; // API 回复是否因 token 上限被截断

            // Tool Calls 累积器（支持多个并发工具调用）
            var toolCallIds = new Dictionary<int, string>();
            var toolCallNames = new Dictionary<int, string>();
            var toolCallArgs = new Dictionary<int, StringBuilder>();

            // SSE 格式自动检测：正常走 Chat Completions，但如果中转站实际返回 Responses API 格式，自动切换解析模式
            bool isResponsesApi = false;

            // 当前活跃输出项的 phase（Responses API 支持 commentary/final_answer）
            // commentary 是模型的内心思考，不应显示给用户；只显示 final_answer 的内容
            string? currentOutputPhase = null;

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;
                line = line.Trim();

                // 记录前 5 行原始数据（不过滤），用于诊断非标准 SSE 格式
                rawLineCount++;
                if (rawLineCount <= 5 && !string.IsNullOrEmpty(line))
                    AiFlowLogger.Info($"[RAW #{rawLineCount}] {line.Substring(0, Math.Min(line.Length, 300))}");

                // SSE 格式：跳过空行和事件类型行
                if (string.IsNullOrEmpty(line) || line.StartsWith("event:")) continue;
                if (!line.StartsWith("data:")) continue;

                var jsonPart = line.Substring(5).Trim();
                if (jsonPart == "[DONE]") break;
                if (string.IsNullOrEmpty(jsonPart)) continue;

                AiFlowLogger.Info($"[SSE RAW JSON] {jsonPart}");

                sseLineCount++;

                try
                {
                    var evt = JObject.Parse(jsonPart);

                    // 动态检测：如果 data 中包含 "type":"response.xxx"，自动切换为 Responses API 模式
                    // （解决 API 代理 URL 为 /v1/chat/completions 但实际返回 Responses API 格式的情况）
                    if (!isResponsesApi && evt["type"]?.ToString() is string evtTypeCheck
                        && evtTypeCheck.StartsWith("response."))
                    {
                        isResponsesApi = true;
                        AiFlowLogger.Info($"[SSE] 自动检测到 Responses API 格式（{evtTypeCheck}）");
                    }

                    if (isResponsesApi)
                    {
                        // Responses API 流式事件
                        var evtType = evt["type"]?.ToString();

                        if (evtType == "response.output_item.added")
                        {
                            // 记录当前输出项的 phase（commentary / final_answer）
                            currentOutputPhase = evt["item"]?["phase"]?.ToString();
                        }
                        else if (evtType == "response.output_item.done")
                        {
                            // 输出项结束，重置 phase
                            currentOutputPhase = null;
                        }
                        else if (evtType == "response.output_text.delta")
                        {
                            var delta = evt["delta"]?.ToString();
                            // 只有 final_answer phase（或无 phase 字段）才显示给用户
                            // commentary phase 是模型的内心思考，不传递给 UI
                            bool isVisible = currentOutputPhase == null
                                || currentOutputPhase == "final_answer";
                            if (delta != null && isVisible)
                            {
                                contentBuilder.Append(delta);
                                onDelta(delta);
                            }
                        }
                        else if (evtType == "response.reasoning_summary_text.delta"
                              || evtType == "response.reasoning.delta")
                        {
                            // 推理/思考过程增量
                            var thinking = evt["delta"]?.ToString();
                            if (thinking != null && onThinking != null)
                            {
                                onThinking(thinking);
                            }
                        }
                        else if (evtType == "response.completed")
                        {
                            var respObj = evt["response"];
                            if (respObj is JObject)
                            {
                                // 检测回复是否被截断（status != "completed" 或 incomplete_details 存在）
                                var respStatus = respObj["status"]?.ToString();
                                if (respStatus == "incomplete")
                                {
                                    isTruncated = true;
                                    var reason = respObj["incomplete_details"]?["reason"]?.ToString() ?? "unknown";
                                    AiFlowLogger.Info($"[SSE] 回复被截断，原因: {reason}");
                                }

                                var usage = respObj["usage"];
                                if (usage is JObject)
                                {
                                    inputTokens = (int?)usage["input_tokens"] ?? (int?)usage["prompt_tokens"] ?? 0;
                                    outputTokens = (int?)usage["output_tokens"] ?? (int?)usage["completion_tokens"] ?? 0;
                                }

                                // Responses API: 从 response.completed 中提取 tool calls
                                var output = respObj["output"] as JArray;
                                if (output != null)
                                {
                                    int tcIdx = 0;
                                    foreach (var item in output)
                                    {
                                        var itemType = item["type"]?.ToString();
                                        if (itemType == "function_call")
                                        {
                                            var callId = item["call_id"]?.ToString() ?? item["id"]?.ToString() ?? $"call_{tcIdx}";
                                            var funcName = item["name"]?.ToString();
                                            var funcArgs = item["arguments"]?.ToString();
                                            if (funcName != null)
                                            {
                                                toolCallIds[tcIdx] = callId;
                                                toolCallNames[tcIdx] = funcName;
                                                if (funcArgs != null)
                                                {
                                                    toolCallArgs[tcIdx] = new StringBuilder(funcArgs);
                                                }
                                                AiFlowLogger.Info($"[SSE] Tool Call: {funcName}({funcArgs})");
                                                tcIdx++;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else if (evtType == "response.output_item.added")
                        {
                            // Responses API: 新输出项（可能是 function_call）
                            var item = evt["item"];
                            if (item?["type"]?.ToString() == "function_call")
                            {
                                var callId = item["call_id"]?.ToString() ?? item["id"]?.ToString() ?? $"call_0";
                                var funcName = item["name"]?.ToString();
                                if (funcName != null)
                                {
                                    // 使用 output_item index 或默认 0
                                    int tcIdx = toolCallNames.Count;
                                    toolCallIds[tcIdx] = callId;
                                    toolCallNames[tcIdx] = funcName;
                                    if (!toolCallArgs.ContainsKey(tcIdx))
                                        toolCallArgs[tcIdx] = new StringBuilder();
                                }
                            }
                        }
                        else if (evtType == "response.function_call_arguments.delta")
                        {
                            // Responses API: function call arguments 增量
                            var argsDelta = evt["delta"]?.ToString();
                            if (argsDelta != null)
                            {
                                // 追加到最后一个 tool call 的 args
                                int lastIdx = toolCallNames.Count > 0 ? toolCallNames.Keys.Max() : 0;
                                if (!toolCallArgs.ContainsKey(lastIdx))
                                    toolCallArgs[lastIdx] = new StringBuilder();
                                toolCallArgs[lastIdx].Append(argsDelta);
                            }
                        }
                    }
                    else if (isGemini)
                    {
                        // Gemini 流式事件
                        var text = evt["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                        if (text != null)
                        {
                            contentBuilder.Append(text);
                            onDelta(text);
                        }
                        var usageMeta = evt["usageMetadata"];
                        if (usageMeta is JObject)
                        {
                            inputTokens = (int?)usageMeta["promptTokenCount"] ?? inputTokens;
                            outputTokens = (int?)usageMeta["candidatesTokenCount"] ?? outputTokens;
                        }
                    }
                    else
                    {
                        // Chat Completions 流式事件
                        var choices = evt["choices"] as JArray;
                        var choiceDelta = choices != null && choices.Count > 0 ? choices[0]?["delta"] : null;
                        if (choiceDelta != null)
                        {
                            // 思考/推理过程（DeepSeek: reasoning_content, 部分模型: reasoning）
                            var thinking = choiceDelta["reasoning_content"]?.ToString()
                                           ?? choiceDelta["reasoning"]?.ToString();
                            if (thinking != null && onThinking != null)
                            {
                                onThinking(thinking);
                            }

                            // 正文内容
                            var delta = choiceDelta["content"]?.ToString();
                            if (delta != null)
                            {
                                contentBuilder.Append(delta);
                                onDelta(delta);
                            }

                            // Tool Calls 增量解析
                            var toolCalls = choiceDelta["tool_calls"] as JArray;
                            if (toolCalls != null)
                            {
                                foreach (var tc in toolCalls)
                                {
                                    var idx = (int?)tc["index"] ?? 0;
                                    var tcId = tc["id"]?.ToString();
                                    if (tcId != null)
                                        toolCallIds[idx] = tcId;
                                    var funcObj = tc["function"];
                                    if (funcObj != null)
                                    {
                                        var name = funcObj["name"]?.ToString();
                                        if (!string.IsNullOrEmpty(name))
                                            toolCallNames[idx] = name;

                                        var args = funcObj["arguments"]?.ToString();
                                        if (args != null)
                                        {
                                            if (!toolCallArgs.ContainsKey(idx))
                                                toolCallArgs[idx] = new StringBuilder();
                                            toolCallArgs[idx].Append(args);
                                        }
                                    }
                                }
                            }
                        }
                        // 检测 Chat Completions 的 finish_reason
                        var finishReason = choices != null && choices.Count > 0
                            ? choices[0]?["finish_reason"]?.ToString() : null;
                        if (finishReason == "length")
                        {
                            isTruncated = true;
                            AiFlowLogger.Info("[SSE] 回复被截断（finish_reason=length）");
                        }

                        // 部分提供商在最后一帧附带 usage
                        var usage = evt["usage"];
                        if (usage is JObject)
                        {
                            inputTokens = (int?)usage["prompt_tokens"] ?? inputTokens;
                            outputTokens = (int?)usage["completion_tokens"] ?? outputTokens;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AiFlowLogger.Warn($"[SSE] 解析异常: {ex.Message}");
                }
            }

            // 更新模型统计
            if (inputTokens > 0 || outputTokens > 0)
            {
                modelConfig.TotalInputTokens += inputTokens;
                modelConfig.TotalOutputTokens += outputTokens;
            }

            // 汇总 tool calls 结果
            List<(string Id, string Name, string Arguments)>? parsedToolCalls = null;
            if (toolCallNames.Count > 0)
            {
                parsedToolCalls = new List<(string, string, string)>();
                foreach (var kvp in toolCallNames.OrderBy(k => k.Key))
                {
                    var id = toolCallIds.ContainsKey(kvp.Key) ? toolCallIds[kvp.Key] : $"call_{Guid.NewGuid():N}";
                    var args = toolCallArgs.ContainsKey(kvp.Key) ? toolCallArgs[kvp.Key].ToString() : "{}";
                    parsedToolCalls.Add((id, kvp.Value, args));
                    AiFlowLogger.Info($"[SSE] Tool Call: {kvp.Value}({args.Substring(0, Math.Min(args.Length, 200))})");
                }
            }

            // 回退：流式解析未获取到任何内容且无 tool calls 时，使用非流式方式重试
            if (contentBuilder.Length == 0 && parsedToolCalls == null && rawLineCount > 0)
            {
                AiFlowLogger.Warn($"[SSE] 流式解析未获取内容（共 {rawLineCount} 行原始数据, {sseLineCount} 行 SSE 数据），回退使用非流式 API...");
                var fallback = await CallLlmAsync(modelConfig, requestBody, cancellationToken);
                return (fallback.ResponseText, fallback.InputTokens, fallback.OutputTokens, null, false);
            }

            return (contentBuilder.ToString(), inputTokens, outputTokens, parsedToolCalls, isTruncated);
        }

        /// <summary>
        /// 将模型配置中的自定义请求头注入到 HttpRequestMessage
        /// </summary>
        private static void ApplyCustomHeaders(HttpRequestMessage request, LlmModelConfig modelConfig)
        {
            if (string.IsNullOrEmpty(modelConfig.CustomHeaders)) return;

            foreach (var line in modelConfig.CustomHeaders.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;

                var headerKey = line.Substring(0, idx).Trim();
                var headerVal = line.Substring(idx + 1).Trim();
                if (string.IsNullOrEmpty(headerKey)) continue;

                // Host 头需要特殊处理
                if (headerKey.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    request.Headers.Host = headerVal;
                else
                    request.Headers.TryAddWithoutValidation(headerKey, headerVal);
            }
        }

        /// <summary>
        /// 合并多个 submit_plan 调用的结果。
        /// AI 可能在一次响应中返回多个 submit_plan 工具调用（如先修改子流程卡片，再创建主流程卡片），
        /// 需要将它们合并为一个统一的 plan 对象，而非后者覆盖前者。
        /// </summary>
        private static AiFlowPlanResponse MergeSubmitPlans(AiFlowPlanResponse existing, AiFlowPlanResponse incoming)
        {
            // 列表类字段：追加
            if (incoming.Plan.Count > 0)
            {
                // 为每个步骤标记它所属的 targetFlow（多 submit_plan 时各自独立）
                // 使用约定：空字符串 "" 表示"当前主流程"(即不覆盖)，null 表示"未设置"
                var incomingTarget = incoming.TargetFlow;
                foreach (var step in incoming.Plan)
                {
                    // 仅在 incoming 有明确的 targetFlow 时标记
                    // 如果 incoming 没有 targetFlow 且 existing 有，需要标记为 "" 以区分
                    if (!string.IsNullOrEmpty(incomingTarget))
                    {
                        step.TargetFlowOverride = incomingTarget;
                    }
                    else if (!string.IsNullOrEmpty(existing.TargetFlow))
                    {
                        // existing 已有 targetFlow，但 incoming 没有 → 步骤写入当前流程（用 "" 标记）
                        step.TargetFlowOverride = "";
                    }
                }
                existing.Plan.AddRange(incoming.Plan);
            }
            if (incoming.Variables.Count > 0)
                existing.Variables.AddRange(incoming.Variables);
            if (incoming.DeleteVariables.Count > 0)
                existing.DeleteVariables.AddRange(incoming.DeleteVariables);
            if (incoming.ModifyVariables.Count > 0)
                existing.ModifyVariables.AddRange(incoming.ModifyVariables);
            if (incoming.ModifyCards.Count > 0)
                existing.ModifyCards.AddRange(incoming.ModifyCards);
            if (incoming.DeleteCards.Count > 0)
                existing.DeleteCards.AddRange(incoming.DeleteCards);
            if (incoming.RunCards.Count > 0)
                existing.RunCards.AddRange(incoming.RunCards);
            if (incoming.InsertCards?.Count > 0)
            {
                existing.InsertCards ??= new();
                existing.InsertCards.AddRange(incoming.InsertCards);
            }
            if (incoming.CreateFlows?.Count > 0)
            {
                existing.CreateFlows ??= new();
                existing.CreateFlows.AddRange(incoming.CreateFlows);
            }
            if (incoming.DeleteFlows?.Count > 0)
            {
                existing.DeleteFlows ??= new();
                existing.DeleteFlows.AddRange(incoming.DeleteFlows);
            }
            if (incoming.ShellCommands?.Count > 0)
            {
                existing.ShellCommands ??= new();
                existing.ShellCommands.AddRange(incoming.ShellCommands);
            }
            if (incoming.FallbackPlan?.Count > 0)
            {
                existing.FallbackPlan ??= new();
                existing.FallbackPlan.AddRange(incoming.FallbackPlan);
            }

            // 标量字段：后者覆盖前者（取最后一次设置）
            if (!string.IsNullOrEmpty(incoming.TargetFlow))
                existing.TargetFlow = incoming.TargetFlow;
            if (!string.IsNullOrEmpty(incoming.SwitchFlow))
                existing.SwitchFlow = incoming.SwitchFlow;
            if (!string.IsNullOrEmpty(incoming.FailureStrategy))
                existing.FailureStrategy = incoming.FailureStrategy;
            if (!string.IsNullOrEmpty(incoming.Summary))
                existing.Summary = incoming.Summary;
            if (!string.IsNullOrEmpty(incoming.ScreenshotTarget))
                existing.ScreenshotTarget = incoming.ScreenshotTarget;

            // 布尔字段：任一为 true 则 true
            if (incoming.Done) existing.Done = true;
            if (incoming.NeedsScreenshot) existing.NeedsScreenshot = true;

            return existing;
        }
    }
}
