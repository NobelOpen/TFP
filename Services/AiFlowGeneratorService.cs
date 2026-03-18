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
        private static readonly HttpClient _httpClient = new();
        private List<CardDescriptionDef>? _cardDescriptions;

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

            return _cardDescriptions;
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
        /// 阶段1：确定需要的卡片类别
        /// </summary>
        public async Task<(List<string> Categories, int InputTokens, int OutputTokens)> DetermineCategoriesAsync(
            string userPrompt, string modelId, CancellationToken cancellationToken)
        {
            var modelConfig = LlmModelManager.GetModelById(modelId);
            if (modelConfig == null)
                throw new InvalidOperationException("未找到指定的模型配置");

            var allCategories = GetAllCategories();
            var categoryList = string.Join("、", allCategories);

            var systemPrompt = $@"你是 TaskFlow 自动化流程设计助手。用户会描述他想实现的自动化功能。
你的任务是判断这个需求涉及哪些卡片类别。

可用的类别有：{categoryList}

请只返回一个 JSON 数组，包含需要的类别名称。例如：[""Windows操作"", ""图像处理""]
不要返回任何其他文字。";

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
            AiFlowLogger.LogLlmRequest("阶段1-类别判断", modelId, modelConfig.ApiEndpoint, requestJson);

            var (responseText, inputTokens, outputTokens) = await CallLlmAsync(modelConfig, requestBody, cancellationToken);

            AiFlowLogger.LogLlmResponse("阶段1-类别判断", responseText, inputTokens, outputTokens);

            // 解析返回的类别列表
            var categories = new List<string>();
            try
            {
                // 提取 JSON 数组部分
                var jsonMatch = System.Text.RegularExpressions.Regex.Match(responseText, @"\[.*\]", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (jsonMatch.Success)
                {
                    categories = JsonConvert.DeserializeObject<List<string>>(jsonMatch.Value) ?? new();
                }
            }
            catch (Exception ex)
            {
                // 解析失败时，使用所有类别
                AiFlowLogger.Warn($"阶段1类别解析失败，回退到全部类别: {ex.Message}");
                categories = allCategories;
            }

            // 确保至少有一个类别
            if (categories.Count == 0)
                categories = allCategories;

            AiFlowLogger.Info($"阶段1结果: 确定类别 [{string.Join(", ", categories)}]");
            return (categories, inputTokens, outputTokens);
        }

        /// <summary>
        /// 阶段2：生成详细的流程方案
        /// </summary>
        public async Task<(AiFlowPlanResponse Plan, int InputTokens, int OutputTokens)> GeneratePlanAsync(
            string userPrompt, List<string> categories, string modelId, CancellationToken cancellationToken,
            string currentFlowContext = "", List<(string Role, string Content)>? conversationHistory = null,
            AiAssistantMode mode = AiAssistantMode.Design,
            List<string>? imageBase64List = null)
        {
            var modelConfig = LlmModelManager.GetModelById(modelId);
            if (modelConfig == null)
                throw new InvalidOperationException("未找到指定的模型配置");

            var detailedCards = BuildDetailedPrompt(categories);

            // 构建当前流程上下文段落
            var flowContextSection = string.IsNullOrEmpty(currentFlowContext)
                ? ""
                : $@"

用户当前画布上已有的流程如下（供你参考和分析）：
{currentFlowContext}
如果用户在询问关于已有流程的问题（如分析、审查、解释），请在 summary 中给出回答，plan 可以为空数组。
如果用户想在已有流程基础上追加新步骤，请只生成新增的步骤（不要重复已有步骤），step 编号从已有流程之后继续。
如果用户要求删除变量，请在 deleteVariables 数组中列出要删除的变量名（不带 @ 前缀）。
如果用户要求修改变量的值，请在 modifyVariables 数组中列出要修改的变量。
如果用户要求修改已有卡片属性，请在 modifyCards 数组中指定卡片序号和要修改的属性，例如: ""modifyCards"": [{{ ""order"": 3, ""properties"": {{ ""Delay"": ""2000"" }} }}]。
如果用户要求删除已有卡片，请在 deleteCards 数组中列出要删除的卡片序号。";

            // 自主模式专属指令
            var autonomousSection = mode == AiAssistantMode.Autonomous ? @"

自主执行模式（当前已启用）：
你处于自主模式，应优先考虑直接运行已有卡片来完成用户任务：
- 在 runCards 数组中指定要运行的已有卡片序号（即 order 值）
- 系统会运行这些卡片并将结果（状态、路径输出、文本输出等）反馈给你
- 收到运行结果后，如果还有后续卡片需要运行，必须在下一轮返回 runCards 继续运行它们
- 绝对不要在还有后续步骤需要执行时就设置 done 为 true
- 只有当所有需要的操作真正全部完成后，才设置 done 为 true
- 每次 runCards 只放一个批次，不要把所有步骤放在一次中
- 可以混合使用 modifyCards 和 runCards（如先修改卡片属性再运行）
- 如果需要创建新卡片后再运行，先返回 plan 创建，下一轮再用 runCards 运行
- 当卡片运行失败时，必须指定 failureStrategy（retry/fallback/abort）
- 可使用 deleteCards 删除失败卡片，再用 plan 或 fallbackPlan 创建替代方案

视觉点击策略（重要）：
当用户要求点击屏幕上的某个视觉元素（按钮、图标等）时，你必须使用以下流程：
1. 第一步：创建或运行 WinScreenshot 截图卡片（processName 留空截全屏），通过 runCards 执行
2. 第二步：在收到截图结果后，你能直接看到截图图像，结合图像分辨率估算目标元素的坐标
3. 第三步：创建 WinClick 卡片，在 startX/startY 中设置估算的坐标，设置 clickType
不要使用 WinUiAutomation 来点击桌面图标或视觉元素，它对桌面图标和很多应用不可靠。" : @"

设计模式（当前已启用）：
你处于设计模式，主要职责是帮用户设计和优化流程蓝图：
- 优先生成 plan（创建卡片蓝图），而不是直接运行
- 不要主动使用 runCards，除非用户明确要求执行/运行";

            flowContextSection += autonomousSection;

            var systemPrompt = $@"你是 TaskFlow 自动化流程设计助手。你需要根据用户的需求，使用下列可用的任务卡片来设计一个自动化流程。

{detailedCards}

请以纯 JSON 格式输出你的方案，格式如下：
{{
  ""summary"": ""方案摘要（一句话描述整体流程）"",
  ""variables"": [
    {{
      ""name"": ""变量名（不带@前缀）"",
      ""type"": ""Int"",
      ""value"": ""0"",
      ""description"": ""用途说明""
    }}
  ],
  ""plan"": [
    {{
      ""step"": 1,
      ""taskType"": ""卡片类型枚举名（必须与上面的 TaskType 完全一致）"",
      ""name"": ""步骤名称"",
      ""description"": ""为什么需要这一步"",
      ""properties"": {{ ""属性名"": ""值"" }},
      ""sourceStep"": null,
      ""templateSourceStep"": null
    }}
  ],
  ""insertCards"": [
    {{
      ""targetBlockOrder"": 4,
      ""branch"": ""if"",
      ""cards"": [
        {{ ""step"": 10, ""taskType"": ""WinClick"", ""name"": ""点击目标"", ""properties"": {{}} }}
      ]
    }}
  ]
}}

变量系统：
- variables 数组用于声明流程需要的变量，type 可选值：Int、String、Bool、Double
- 当流程需要计数器、状态标记、循环条件等场景时，应声明变量
- 在卡片属性中可使用 @变量名 引用变量，如 @retryCount
- 使用 ExpressionEval 卡片可以对变量赋值，格式：@变量名 = 表达式
- 如果流程不需要变量，variables 可以为空数组 []

输出引用语法（在 properties 中使用）：
引用格式为 #N 卡片名.输出属性（N 是步骤编号），例如：
- #3 查找 MAA 程序.查找路径 — 引用第 3 步的查找路径输出
- #1 Win截图.X — 引用第 1 步的 X 坐标输出
可用的输出属性有：
  输出文本（或 文本）、X、Y、执行结果、循环索引、匹配率、
  转换结果、当前时间、匹配数量、解析结果、查找路径、
  匹配索引、匹配值、保存文件路径、已翻译文件路径、数组元素数量
- 在 properties 中直接使用该引用格式（不需要花括号包裹）
- 支持在条件表达式中使用，如 #3 颜色识别.匹配率>0.5

控制流支持（IfElseBlock 和 ForLoopBlock）：
- 当需要条件分支时，使用 taskType=""IfElseBlock""，并在 ifBody 和 elseBody（可选）中嵌套子步骤：
  {{
    ""step"": 2, ""taskType"": ""IfElseBlock"", ""name"": ""判断匹配结果"",
    ""description"": ""根据模板匹配是否成功决定下一步"",
    ""properties"": {{ ""conditionExpression"": ""#1 模板匹配.匹配结果==True"" }},
    ""ifBody"": [ {{ ""step"": 3, ""taskType"": ""WinClick"", ... }} ],
    ""elseBody"": [ {{ ""step"": 4, ""taskType"": ""PauseTask"", ... }} ]
  }}
- 当需要循环时，使用 taskType=""ForLoopBlock""，并在 loopBody 中嵌套子步骤：
  {{
    ""step"": 5, ""taskType"": ""ForLoopBlock"", ""name"": ""重复检测"",
    ""description"": ""循环截图检测"",
    ""properties"": {{ ""loopCount"": ""5"" }},
    ""loopBody"": [ {{ ""step"": 6, ""taskType"": ""WinScreenshot"", ... }} ]
  }}
- 嵌套体内的步骤格式与顶层步骤完全一致，可以多层嵌套。
{flowContextSection}
重要规则：
1. taskType 的值必须是上面列出的 TaskType 名称之一，不能自创。
2. sourceStep 用于建立步骤间的数据传递关系：
   - 当某步骤需要使用前面步骤输出的图像时，必须设置 sourceStep 为输出图像的步骤编号。
   - 以下图像处理类卡片必须通过 sourceStep 引用图像来源（如 WinScreenshot 步骤）才能工作：
      ImgOcr、ImgTemplateMatch、ImgCrop、ImgColorDetect、ImgColorSegment、ImgPreprocess、ImgBlobAnalysis、ImgResize、LlmVision
   - templateSourceStep 仅用于 ImgTemplateMatch，指定模板图来源步骤（如 ImgCrop 裁剪出的区域）。
   - ImgCrop 支持通过 properties 设置裁剪区域：roiX、roiY、roiWidth、roiHeight。
3. properties 中只填写你能确定的值，不确定的属性不要填写。
4. 在 properties 中引用其他步骤输出时，使用 #N 步骤名.输出属性 格式（如 #3 查找MAA.查找路径），不要使用花括号。
5. 当用户要求删除变量时，使用 deleteVariables 数组。
6. 当用户要求修改变量值时，使用 modifyVariables 数组。
7. 当用户要求修改已有卡片属性时，使用 modifyCards 数组。
8. 当用户要求删除已有卡片时，使用 deleteCards 数组。
9. 当用户要求在已有的 IfElse 分支或 ForLoop 循环中插入卡片时，使用 insertCards 数组，不要删除重建整个 block。targetBlockOrder 是 block 起始卡片的序号，branch 可选 if/else/loop。
10. 使用 runCards 指定要运行的卡片序号。每轮只运行一批，运行后分析结果再决定下一批。所有卡片都运行完毕后才设置 done: true。
11. 任务步骤名称和变量名称中严禁使用任何标点符号（如 . 等特殊字符），只能包含中文、字母和数字，以防止引用解析失败。
12. 流程管理：用户可拥有多个流程（Tab），每个流程包含独立的卡片集合。创建新流程用 createFlows 数组（格式 [{{""name"":""流程名""}}]），删除流程用 deleteFlows 数组（格式 [""流程名""]），切换流程用 switchFlow 字符串。系统先处理 switchFlow 再添加 plan 步骤。
13. 点击界面元素时，结合图像分辨率信息直接估算坐标，在 WinClick 的 startX/startY 中设置。无需创建额外的裁剪或模板匹配步骤。
14. 你已经能直接看到用户的屏幕截图（系统自动附加），无需创建 WinScreenshot 卡片来获取屏幕信息。直接根据看到的截图内容进行分析和决策。
15. 只返回 JSON，不要返回其他任何文字。";

            // 构建消息数组（含历史对话）
            var messages = new List<object> { new { role = "system", content = systemPrompt } };

            // 注入最近对话历史（提供上下文连贯性）
            if (conversationHistory != null)
            {
                foreach (var (role, content) in conversationHistory)
                    messages.Add(new { role, content });
            }

            // 添加当前用户消息（支持多模态：文本 + 图片）
            if (imageBase64List != null && imageBase64List.Count > 0)
            {
                // OpenAI Vision 格式：content 为数组
                var contentParts = new List<object>
                {
                    new { type = "text", text = userPrompt }
                };
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
            else
            {
                messages.Add(new { role = "user", content = (object)userPrompt });
            }

            var requestBody = new
            {
                model = modelConfig.ModelName,
                messages,
                temperature = 0.3
            };

            var requestJson = JsonConvert.SerializeObject(requestBody, Formatting.Indented);
            AiFlowLogger.LogLlmRequest("阶段2-方案生成", modelId, modelConfig.ApiEndpoint, requestJson);

            var (responseText, inputTokens, outputTokens) = await CallLlmAsync(modelConfig, requestBody, cancellationToken);

            AiFlowLogger.LogLlmResponse("阶段2-方案生成", responseText, inputTokens, outputTokens);

            // 解析方案
            AiFlowPlanResponse plan;
            try
            {
                // 提取 JSON 对象部分
                var jsonMatch = System.Text.RegularExpressions.Regex.Match(responseText, @"\{.*\}", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (jsonMatch.Success)
                {
                    plan = JsonConvert.DeserializeObject<AiFlowPlanResponse>(jsonMatch.Value) ?? new();
                    AiFlowLogger.Info($"阶段2解析成功: {plan.Plan.Count} 个步骤");

                    // 如果解析后所有字段为空（AI 返回了非标准格式的 JSON），把原始响应当作文本回复
                    bool isEmpty = plan.Plan.Count == 0
                        && string.IsNullOrEmpty(plan.Summary)
                        && (plan.Variables == null || plan.Variables.Count == 0)
                        && (plan.DeleteVariables == null || plan.DeleteVariables.Count == 0)
                        && (plan.ModifyCards == null || plan.ModifyCards.Count == 0)
                        && (plan.DeleteCards == null || plan.DeleteCards.Count == 0)
                        && (plan.InsertCards == null || plan.InsertCards.Count == 0)
                        && (plan.RunCards == null || plan.RunCards.Count == 0);
                    if (isEmpty)
                    {
                        plan.Summary = responseText.Trim();
                        AiFlowLogger.Info("阶段2: AI 返回了非标准 JSON，作为文本回复处理");
                    }
                }
                else
                {
                    // AI 返回了纯文本回答（如知识性问题），将原文作为分析结果
                    plan = new AiFlowPlanResponse { Summary = responseText.Trim() };
                    AiFlowLogger.Info("阶段2: AI 返回了文本回答（非 JSON），作为分析结果处理");
                }
            }
            catch (Exception ex)
            {
                // JSON 解析异常，也用原文回退
                plan = new AiFlowPlanResponse { Summary = responseText.Trim() };
                AiFlowLogger.Warn($"阶段2 JSON 解析失败, 回退为文本: {ex.Message}");
            }

            return (plan, inputTokens, outputTokens);
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
        /// 调用 LLM API 的通用方法（兼容 OpenAI 和 Gemini）
        /// </summary>
        private async Task<(string ResponseText, int InputTokens, int OutputTokens)> CallLlmAsync(
            LlmModelConfig modelConfig, object requestBody, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = modelConfig.TimeoutSeconds > 0 ? modelConfig.TimeoutSeconds : 60;
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            bool isGemini = IsGeminiApi(modelConfig.ApiEndpoint);
            string actualUrl;
            object actualBody;

            if (isGemini)
            {
                actualUrl = BuildGeminiUrl(modelConfig.ApiEndpoint, modelConfig.ModelName);
                actualBody = BuildGeminiRequestBody(requestBody);
            }
            else
            {
                actualUrl = modelConfig.ApiEndpoint;
                actualBody = requestBody;
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

            var response = await _httpClient.SendAsync(requestMessage, cts.Token);
            var responseString = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"API 请求失败: {response.StatusCode} - {responseString}");
            }

            var jsonResponse = JObject.Parse(responseString);

            // 提取回复文本（兼容 OpenAI 和 Gemini 格式）
            var replyText = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString()          // OpenAI Chat
                            ?? jsonResponse["choices"]?[0]?["text"]?.ToString()                      // OpenAI Legacy
                            ?? jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString()  // Gemini
                            ?? jsonResponse["text"]?.ToString()
                            ?? responseString;

            // 提取 Token 使用量（兼容 OpenAI 和 Gemini 格式）
            int inputTokens = 0, outputTokens = 0;
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

            // 更新模型统计
            if (inputTokens > 0 || outputTokens > 0)
            {
                modelConfig.TotalInputTokens += inputTokens;
                modelConfig.TotalOutputTokens += outputTokens;
            }

            return (replyText, inputTokens, outputTokens);
        }
    }
}
