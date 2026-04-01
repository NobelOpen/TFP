using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenCvSharp;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Resources;
using TaskFlow.Models.TaskCards;
using TaskStatus = TaskFlow.Models.TaskCards.TaskStatus;

namespace TaskFlow.Services
{
    // LLM AI 执行器 + 通用辅助方法（ArrayParse, ResolveCoordinate, ApplyGrayscale）
    public partial class TaskExecutionService
    {
        #region AI Processing

        private async Task<bool> ExecuteLlmTranslateAsync(LlmTranslateTaskCard task, IList<TaskCardBase> allTasks, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(task.ModelId))
            {
                task.ErrorMessage = Strings.Svc_ModelNotSelected;
                return false;
            }


            var modelConfig = TaskFlow.Helpers.LlmModelManager.Models.FirstOrDefault(m => m.Id == task.ModelId);
            if (modelConfig == null)
            {
                task.ErrorMessage = Strings.Svc_ModelDeleted;
                return false;
            }

            // 解析待翻译文本
            string sourceText = task.SourceTextExpression;
            if (!string.IsNullOrWhiteSpace(sourceText))
            {
                try
                {
                    sourceText = _variableStore.ResolveVariableReferences(sourceText);
                    sourceText = ExpressionEvaluator.ResolveExpression(sourceText, allTasks, _variableStore);
                }
                catch (Exception ex)
                {
                    task.ErrorMessage = $"待翻译文本表达式解析异常: {ex.Message}";
                    return false;
                }
            }
            
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                task.ErrorMessage = "待翻译文本为空。";
                return false;
            }

            // 构建 System Prompt
            string systemPrompt = task.SystemPrompt.Replace("{目标语言}", task.TargetLanguage ?? "");
            
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (modelConfig.TimeoutSeconds > 0)
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(modelConfig.TimeoutSeconds));
                }
                else
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(60));
                }
                
                object requestBody;
                
                // 兼容特定的 responses 接口 (如 ChatGPT+Codex 中转)
                if (modelConfig.ApiEndpoint.Contains("/v1/responses", StringComparison.OrdinalIgnoreCase))
                {
                    requestBody = new
                    {
                        model = modelConfig.ModelName,
                        input = new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = sourceText }
                        },
                        store = false,
                        stream = false
                    };
                }
                else
                {
                    requestBody = new
                    {
                        model = modelConfig.ModelName,
                        messages = new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = sourceText }
                        },
                        temperature = 0.3
                    };
                }

                // 使用 HttpRequestMessage 设置 per-request 的 Authorization 头，
                // 避免在共享 HttpClient 上设置 DefaultRequestHeaders 导致线程安全问题
                using var requestMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, modelConfig.ApiEndpoint);
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", modelConfig.ApiKey);
                requestMessage.Content = new System.Net.Http.StringContent(
                    Newtonsoft.Json.JsonConvert.SerializeObject(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json");

                Log($"[{DateTime.Now:HH:mm:ss}] 准备调用模型 {modelConfig.DisplayName} 进行翻译...");
                
                var response = await _sharedHttpClient.SendAsync(requestMessage, cts.Token);
                string responseString = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    task.ErrorMessage = $"API 请求失败: {response.StatusCode} - {responseString}";
                    return false;
                }

                var jsonResponse = Newtonsoft.Json.Linq.JObject.Parse(responseString);
                
                // 兼容两种返回结构
                var translatedText = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString() 
                                     ?? jsonResponse["choices"]?[0]?["text"]?.ToString()
                                     ?? jsonResponse["text"]?.ToString();
                
                if (translatedText == null && jsonResponse["data"] != null)
                {
                    translatedText = jsonResponse["data"]?[0]?["text"]?.ToString();
                }
                
                if (string.IsNullOrWhiteSpace(translatedText))
                {
                    task.ErrorMessage = "API 返回结果为空或格式不正确。";
                    return false;
                }

                task.OutputText = translatedText.Trim();
                
                // 更新 Token 统计
                var usage = jsonResponse["usage"];
                if (usage != null)
                {
                    var promptTokens = (int?)usage["prompt_tokens"] ?? 0;
                    var completionTokens = (int?)usage["completion_tokens"] ?? 0;
                    modelConfig.TotalInputTokens += promptTokens;
                    modelConfig.TotalOutputTokens += completionTokens;
                    // Token已更新到内存，随主文件保存时一同落地
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                task.ErrorMessage = cancellationToken.IsCancellationRequested ? "任务已取消" : "API 请求超时";
                return false;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"调用 API 时发生异常: {ex.Message}";
                return false;
            }
        }

        #endregion

        #region AI Vision Processing

        /// <summary>
        /// 执行多模态识图：将图像和提示词发送给 LLM 模型，获取回复文本
        /// </summary>
        private async Task<bool> ExecuteLlmVisionAsync(LlmVisionTaskCard task, IList<TaskCardBase> allTasks, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(task.ModelId))
            {
                task.ErrorMessage = Strings.Svc_ModelNotSelected;
                return false;
            }


            var modelConfig = TaskFlow.Helpers.LlmModelManager.Models.FirstOrDefault(m => m.Id == task.ModelId);
            if (modelConfig == null)
            {
                task.ErrorMessage = Strings.Svc_ModelDeleted;
                return false;
            }

            // 获取源图像
            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);
            if (sourceImage == null || sourceImage.Empty())
            {
                task.ErrorMessage = "无法获取源图像。请设置图像来源任务或图像文件路径。";
                if (shouldDispose) sourceImage?.Dispose();
                return false;
            }

            try
            {
                // 将图像转为 base64 PNG
                byte[] pngBytes = sourceImage.ToBytes(".png");
                string base64Image = Convert.ToBase64String(pngBytes);
                string imageDataUrl = $"data:image/png;base64,{base64Image}";

                // 解析提示词表达式
                string promptText = task.PromptExpression;
                if (!string.IsNullOrWhiteSpace(promptText))
                {
                    try
                    {
                        promptText = _variableStore.ResolveVariableReferences(promptText);
                        promptText = ExpressionEvaluator.ResolveExpression(promptText, allTasks, _variableStore);
                    }
                    catch (Exception ex)
                    {
                        task.ErrorMessage = $"提示词表达式解析异常: {ex.Message}";
                        return false;
                    }
                }

                if (string.IsNullOrWhiteSpace(promptText))
                {
                    task.ErrorMessage = "提示词为空。";
                    return false;
                }

                string systemPrompt = task.SystemPrompt ?? "";

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (modelConfig.TimeoutSeconds > 0)
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(modelConfig.TimeoutSeconds));
                }
                else
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(120)); // 多模态请求默认 120s 超时
                }

                // 构造 OpenAI Vision 兼容的多模态请求体
                var requestBody = new
                {
                    model = modelConfig.ModelName,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = promptText },
                                new { type = "image_url", image_url = new { url = imageDataUrl } }
                            }
                        }
                    },
                    temperature = 0.3,
                    max_tokens = 1024
                };

                using var requestMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, modelConfig.ApiEndpoint);
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", modelConfig.ApiKey);
                requestMessage.Content = new System.Net.Http.StringContent(
                    Newtonsoft.Json.JsonConvert.SerializeObject(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json");

                Log($"[{DateTime.Now:HH:mm:ss}] 准备调用模型 {modelConfig.DisplayName} 进行多模态识图 (图像大小: {pngBytes.Length / 1024}KB)...");

                var response = await _sharedHttpClient.SendAsync(requestMessage, cts.Token);
                string responseString = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    task.ErrorMessage = $"API 请求失败: {response.StatusCode} - {responseString}";
                    return false;
                }

                var jsonResponse = Newtonsoft.Json.Linq.JObject.Parse(responseString);

                // 兼容多种返回结构
                var replyText = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString()
                                ?? jsonResponse["choices"]?[0]?["text"]?.ToString()
                                ?? jsonResponse["text"]?.ToString();

                if (replyText == null && jsonResponse["data"] != null)
                {
                    replyText = jsonResponse["data"]?[0]?["text"]?.ToString();
                }

                if (string.IsNullOrWhiteSpace(replyText))
                {
                    task.ErrorMessage = "API 返回结果为空或格式不正确。";
                    return false;
                }

                task.OutputText = replyText.Trim();
                task.OutputResult = true;

                // 更新 Token 统计
                var usage = jsonResponse["usage"];
                if (usage != null)
                {
                    var promptTokens = (int?)usage["prompt_tokens"] ?? 0;
                    var completionTokens = (int?)usage["completion_tokens"] ?? 0;
                    modelConfig.TotalInputTokens += promptTokens;
                    modelConfig.TotalOutputTokens += completionTokens;
                    Log($"[{DateTime.Now:HH:mm:ss}] 多模态识图完成: Token消耗 (输入: {promptTokens}, 输出: {completionTokens})");
                }

                Log($"[{DateTime.Now:HH:mm:ss}] 模型回复: {task.OutputText}");
                return true;
            }
            catch (OperationCanceledException)
            {
                task.ErrorMessage = cancellationToken.IsCancellationRequested ? "任务已取消" : "API 请求超时";
                return false;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"调用 API 时发生异常: {ex.Message}";
                return false;
            }
            finally
            {
                if (shouldDispose) sourceImage?.Dispose();
            }
        }

        #endregion

        #region Array Builder

        /// <summary>
        /// 执行数组生成：解析数据表达式，追加到运行时列表，输出元素数量
        /// </summary>
        private Task<bool> ExecuteArrayBuilderAsync(ArrayBuilderTaskCard task, IList<TaskCardBase> allTasks, CancellationToken cancellationToken)
        {
            try
            {
                // 获取或创建该卡片的数据列表
                var dataList = _arrayBuilderData.GetOrAdd(task.Id, _ => new List<string>());

                // 解析清空数组开关表达式
                if (!string.IsNullOrWhiteSpace(task.ClearExpression))
                {
                    try
                    {
                        string clearResolved = _variableStore.ResolveVariableReferences(task.ClearExpression);
                        clearResolved = ExpressionEvaluator.ResolveExpression(clearResolved, allTasks, _variableStore);
                        if (ExpressionEvaluator.Evaluate(clearResolved))
                        {
                            dataList.Clear();
                            Log($"[{DateTime.Now:HH:mm:ss}] 数组生成: 清空数组（表达式为 true）");
                        }
                    }
                    catch { /* 清空表达式解析失败时忽略，继续执行 */ }
                }

                // 解析数据表达式
                string inputValue = task.InputExpression;
                if (!string.IsNullOrWhiteSpace(inputValue))
                {
                    inputValue = _variableStore.ResolveVariableReferences(inputValue);
                    inputValue = ExpressionEvaluator.ResolveExpression(inputValue, allTasks, _variableStore);
                }

                // 解析插入索引
                int insertIndex = -1;
                if (!string.IsNullOrWhiteSpace(task.IndexExpression))
                {
                    string resolvedIdx = _variableStore.ResolveVariableReferences(task.IndexExpression);
                    resolvedIdx = ExpressionEvaluator.ResolveExpression(resolvedIdx, allTasks, _variableStore);
                    resolvedIdx = EvaluateArithmetic(resolvedIdx);
                    int.TryParse(resolvedIdx.Trim(), out insertIndex);
                }

                // 插入数据
                if (insertIndex < 0 || insertIndex >= dataList.Count)
                {
                    dataList.Add(inputValue ?? "");
                }
                else
                {
                    dataList.Insert(insertIndex, inputValue ?? "");
                }

                // 输出数组当前容量
                task.OutputArrayCount = dataList.Count;
                Log($"[{DateTime.Now:HH:mm:ss}] 数组生成: 第 {dataList.Count} 个元素已添加");

                // 如果设置了自动导出路径，每次追加后覆盖写入文件
                if (!string.IsNullOrWhiteSpace(task.AutoExportPath))
                {
                    string exportPath = _variableStore.ResolveVariableReferences(task.AutoExportPath);
                    exportPath = ExpressionEvaluator.ResolveExpression(exportPath, allTasks, _variableStore);
                    var dir = System.IO.Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(exportPath, string.Join("\n", dataList), System.Text.Encoding.UTF8);
                    task.OutputSavePath = exportPath;
                    Log($"[{DateTime.Now:HH:mm:ss}] 数组数据已自动导出至: {exportPath}");
                }

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"数组生成失败: {ex.Message}";
                return Task.FromResult(false);
            }
        }

        #endregion

        #region LLM File Translate

        /// <summary>
        /// 执行LLM文件翻译：读取文件→按 MaxCharsPerBatch 分段→逐段调 API→拼结果→写输出文件
        /// </summary>
        private async Task<bool> ExecuteLlmFileTranslateAsync(LlmFileTranslateTaskCard task, IList<TaskCardBase> allTasks, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(task.ModelId))
            {
                task.ErrorMessage = Strings.Svc_ModelNotSelected;
                return false;
            }


            var modelConfig = TaskFlow.Helpers.LlmModelManager.Models.FirstOrDefault(m => m.Id == task.ModelId);
            if (modelConfig == null)
            {
                task.ErrorMessage = Strings.Svc_ModelDeleted;
                return false;
            }

            // 解析输入/输出文件路径
            string inputPath = task.InputFilePath;
            string outputPath = task.OutputFilePath;
            if (!string.IsNullOrWhiteSpace(inputPath))
            {
                inputPath = _variableStore.ResolveVariableReferences(inputPath);
                inputPath = ExpressionEvaluator.ResolveExpression(inputPath, allTasks, _variableStore);
            }
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = _variableStore.ResolveVariableReferences(outputPath);
                outputPath = ExpressionEvaluator.ResolveExpression(outputPath, allTasks, _variableStore);
            }

            if (string.IsNullOrWhiteSpace(inputPath) || !System.IO.File.Exists(inputPath))
            {
                task.ErrorMessage = $"输入文件不存在: {inputPath}";
                return false;
            }
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                task.ErrorMessage = "未设置输出文件路径";
                return false;
            }

            try
            {
                // 读取源文件全部文本
                string fullText = await System.IO.File.ReadAllTextAsync(inputPath, System.Text.Encoding.UTF8, cancellationToken);
                if (string.IsNullOrWhiteSpace(fullText))
                {
                    task.ErrorMessage = "输入文件内容为空";
                    return false;
                }

                // 按行分割，然后按 MaxCharsPerBatch 分组
                int maxChars = task.MaxCharsPerBatch > 0 ? task.MaxCharsPerBatch : 8000;
                var lines = fullText.Split('\n');
                var batches = new List<string>();
                var currentBatch = new System.Text.StringBuilder();

                foreach (var line in lines)
                {
                    if (currentBatch.Length + line.Length + 1 > maxChars && currentBatch.Length > 0)
                    {
                        batches.Add(currentBatch.ToString());
                        currentBatch.Clear();
                    }
                    if (currentBatch.Length > 0) currentBatch.Append('\n');
                    currentBatch.Append(line);
                }
                if (currentBatch.Length > 0) batches.Add(currentBatch.ToString());

                Log($"[{DateTime.Now:HH:mm:ss}] LLM文件翻译: 共 {fullText.Length} 字符，分为 {batches.Count} 批次");

                // 逐批调用 API 翻译
                string systemPrompt = task.SystemPrompt.Replace("{目标语言}", task.TargetLanguage ?? "");
                var translatedParts = new List<string>();

                for (int i = 0; i < batches.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    int timeout = modelConfig.TimeoutSeconds > 0 ? modelConfig.TimeoutSeconds : 120;
                    cts.CancelAfter(TimeSpan.FromSeconds(timeout));

                    object requestBody;
                    if (modelConfig.ApiEndpoint.Contains("/v1/responses", StringComparison.OrdinalIgnoreCase))
                    {
                        requestBody = new
                        {
                            model = modelConfig.ModelName,
                            input = new[]
                            {
                                new { role = "system", content = systemPrompt },
                                new { role = "user", content = batches[i] }
                            },
                            store = false,
                            stream = false
                        };
                    }
                    else
                    {
                        requestBody = new
                        {
                            model = modelConfig.ModelName,
                            messages = new[]
                            {
                                new { role = "system", content = systemPrompt },
                                new { role = "user", content = batches[i] }
                            },
                            temperature = 0.3,
                            stream = false
                        };
                    }

                    using var requestMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, modelConfig.ApiEndpoint);
                    requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", modelConfig.ApiKey);
                    requestMessage.Content = new System.Net.Http.StringContent(
                        Newtonsoft.Json.JsonConvert.SerializeObject(requestBody),
                        System.Text.Encoding.UTF8,
                        "application/json");

                    Log($"[{DateTime.Now:HH:mm:ss}] 翻译批次 {i + 1}/{batches.Count} ({batches[i].Length} 字符)...");

                    var response = await _sharedHttpClient.SendAsync(requestMessage, cts.Token);
                    string responseString = await response.Content.ReadAsStringAsync(cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        task.ErrorMessage = $"API 请求失败 (批次 {i + 1}): {response.StatusCode} - {responseString}";
                        return false;
                    }

                    // 解析响应：支持标准 JSON 和 SSE 流式格式
                    string? translatedText = null;
                    responseString = responseString.Trim();

                    if (responseString.StartsWith("{"))
                    {
                        // 标准 JSON 响应
                        var jsonResponse = Newtonsoft.Json.Linq.JObject.Parse(responseString);
                        translatedText = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString()
                                         ?? jsonResponse["choices"]?[0]?["text"]?.ToString()
                                         ?? jsonResponse["text"]?.ToString();
                        if (translatedText == null && jsonResponse["data"] != null)
                        {
                            translatedText = jsonResponse["data"]?[0]?["text"]?.ToString();
                        }

                        // 更新 Token 统计
                        var usage = jsonResponse["usage"];
                        if (usage != null)
                        {
                            var promptTokens = (int?)usage["prompt_tokens"] ?? 0;
                            var completionTokens = (int?)usage["completion_tokens"] ?? 0;
                            modelConfig.TotalInputTokens += promptTokens;
                            modelConfig.TotalOutputTokens += completionTokens;
                        }
                    }
                    else if (responseString.StartsWith("data:"))
                    {
                        // SSE 流式响应：逐行提取 data: 中的 JSON，拼接 content 片段
                        var contentBuilder = new System.Text.StringBuilder();
                        foreach (var sseRawLine in responseString.Split('\n'))
                        {
                            var sseLine = sseRawLine.Trim();
                            if (!sseLine.StartsWith("data:")) continue;
                            var jsonPart = sseLine.Substring(5).Trim();
                            if (jsonPart == "[DONE]") break;
                            if (string.IsNullOrEmpty(jsonPart)) continue;
                            try
                            {
                                var sseJson = Newtonsoft.Json.Linq.JObject.Parse(jsonPart);
                                var delta = sseJson["choices"]?[0]?["delta"]?["content"]?.ToString();
                                if (delta != null) contentBuilder.Append(delta);
                            }
                            catch { /* 跳过无法解析的行 */ }
                        }
                        translatedText = contentBuilder.ToString();
                    }
                    else
                    {
                        // 未知格式的响应，直接作为文本使用
                        translatedText = responseString;
                    }

                    if (string.IsNullOrWhiteSpace(translatedText))
                    {
                        task.ErrorMessage = $"API 返回结果为空 (批次 {i + 1})";
                        return false;
                    }

                    translatedParts.Add(translatedText.Trim());

                    Log($"[{DateTime.Now:HH:mm:ss}] 批次 {i + 1}/{batches.Count} 翻译完成");
                }

                // 拼接全部翻译结果并写入输出文件
                string finalResult = string.Join("\n", translatedParts);
                var outDir = System.IO.Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir)) System.IO.Directory.CreateDirectory(outDir);
                await System.IO.File.WriteAllTextAsync(outputPath, finalResult, System.Text.Encoding.UTF8, cancellationToken);

                task.OutputTranslatedFilePath = outputPath;
                Log($"[{DateTime.Now:HH:mm:ss}] LLM文件翻译完成: 共 {batches.Count} 批次，结果已写入 {outputPath}");
                return true;
            }
            catch (OperationCanceledException)
            {
                task.ErrorMessage = cancellationToken.IsCancellationRequested ? "任务已取消" : "API 请求超时";
                return false;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"LLM文件翻译异常: {ex.Message}";
                return false;
            }
        }

        #endregion

        #region Array Parse

        private bool ExecuteArrayParse(ArrayParseTaskCard task, IList<TaskCardBase> allTasks)
        {
            // 解析索引
            int index = task.ParseIndex;
            if (task.UseExpressionIndex && !string.IsNullOrWhiteSpace(task.ParseIndexExpression))
            {
                try
                {
                    string resolved = _variableStore.ResolveVariableReferences(task.ParseIndexExpression);
                    resolved = ExpressionEvaluator.ResolveExpression(resolved, allTasks, _variableStore);
                    if (int.TryParse(resolved.Trim(), out int exprIndex))
                    {
                        index = exprIndex;
                    }
                    else
                    {
                        task.ErrorMessage = string.Format(Strings.Svc_ParseIndexFailed, task.ParseIndexExpression, resolved);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    task.ErrorMessage = $"索引表达式解析异常: {ex.Message}";
                    return false;
                }
            }

            // 优先通过 SourceTaskIdForArray 定位数组来源任务
            TaskCardBase? sourceTask = null;
            string arrayProperty = "";

            if (task.SourceTaskIdForArray.HasValue)
            {
                sourceTask = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForArray.Value);
                if (sourceTask == null)
                {
                    task.ErrorMessage = "引用的数组来源任务不存在";
                    return false;
                }
                arrayProperty = task.SourcePropertyForArray ?? string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(task.SourceExpression))
            {
                // 向后兼容：旧的表达式解析逻辑（格式: #N 任务名.属性名）
                var pattern = @"^#(\d+)\s+([^.]+)\.(.+)$";
                var match = System.Text.RegularExpressions.Regex.Match(task.SourceExpression.Trim(), pattern);
                if (!match.Success)
                {
                    task.ErrorMessage = $"引用表达式格式错误: '{task.SourceExpression}'，正确格式: #N 任务名.属性名";
                    return false;
                }

                int order = int.Parse(match.Groups[1].Value);
                string taskName = match.Groups[2].Value.Trim();
                arrayProperty = match.Groups[3].Value.Trim();

                sourceTask = allTasks.FirstOrDefault(t => t.Order == order);
                if (sourceTask == null)
                {
                    task.ErrorMessage = $"找不到序号为 {order} 的任务卡片";
                    return false;
                }

                if (sourceTask.Name != taskName)
                {
                    task.ErrorMessage = $"序号 {order} 的任务名称不匹配: 期望 \"{taskName}\"，实际 \"{sourceTask.Name}\"";
                    return false;
                }
            }
            else
            {
                task.ErrorMessage = "未设置数组来源任务";
                return false;
            }

            // 支持 ArrayBuilderTaskCard 作为数据源
            if (sourceTask is ArrayBuilderTaskCard)
            {
                if (!_arrayBuilderData.TryGetValue(sourceTask.Id, out var builderData) || builderData.Count == 0)
                {
                    task.ErrorMessage = "ArrayBuilder 数据为空，请先运行数组生成任务";
                    return false;
                }

                if (index < 0 || index >= builderData.Count)
                {
                    task.ErrorMessage = string.Format(Strings.Svc_IndexOutOfRange, index, builderData.Count);
                    task.OutputResult = false;
                    return false;
                }

                string value = builderData[index];
                task.OutputStringValue = value;
                task.OutputText = value;

                // 尝试转换为数值类型
                if (int.TryParse(value, out int intVal)) task.OutputIntValue = intVal;
                if (double.TryParse(value, out double dblVal)) task.OutputDoubleValue = dblVal;

                task.OutputResult = true;
                Log($"[{DateTime.Now:HH:mm:ss}] 数组解析(ArrayBuilder): [{index}] = \"{value}\"");
                return true;
            }

            // 支持 FileReadTaskCard 作为数据源
            if (sourceTask is FileReadTaskCard)
            {
                if (!_fileReadData.TryGetValue(sourceTask.Id, out var fileData) || fileData.Data.Count == 0)
                {
                    task.ErrorMessage = "FileRead 数据为空，请先运行读取文件任务";
                    return false;
                }

                if (index < 0 || index >= fileData.Data.Count)
                {
                    task.ErrorMessage = string.Format(Strings.Svc_IndexOutOfRange, index, fileData.Data.Count);
                    task.OutputResult = false;
                    return false;
                }

                string frValue = fileData.Data[index];
                task.OutputStringValue = frValue;
                task.OutputText = frValue;

                // 尝试转换为数值类型
                if (int.TryParse(frValue, out int frIntVal)) task.OutputIntValue = frIntVal;
                if (double.TryParse(frValue, out double frDblVal)) task.OutputDoubleValue = frDblVal;

                task.OutputResult = true;
                Log($"[{DateTime.Now:HH:mm:ss}] 数组解析(FileRead): [{index}] = \"{frValue}\"");
                return true;
            }

            // 目前仅支持 ImgTemplateMatchTaskCard 的数组输出
            if (sourceTask is ImgTemplateMatchTaskCard tmCard)
            {
                var results = tmCard.OutputMatchResults;

                // 检查数组属性是否有效
                if (arrayProperty != "结果分数" && arrayProperty != "匹配坐标")
                {
                    task.ErrorMessage = $"不支持的数组属性: {arrayProperty}，支持: 结果分数, 匹配坐标";
                    return false;
                }

                // 类型一致性检查（匹配坐标必须用 Coordinate 类型，结果分数必须用 Double 类型）
                if (arrayProperty == "匹配坐标" && task.ArrayDataType != ArrayDataType.Coordinate)
                {
                    task.ErrorMessage = $"类型不匹配: '{arrayProperty}' 是 Coordinate 类型，但设置的变量类型是 {task.ArrayDataType}";
                    task.OutputResult = false;
                    return false;
                }
                if (arrayProperty == "结果分数" && task.ArrayDataType != ArrayDataType.Double)
                {
                    task.ErrorMessage = $"类型不匹配: '{arrayProperty}' 是 Double 类型，但设置的变量类型是 {task.ArrayDataType}";
                    task.OutputResult = false;
                    return false;
                }

                if (index < 0 || index >= results.Count)
                {
                    task.ErrorMessage = string.Format(Strings.Svc_IndexOutOfRange, index, results.Count);
                    task.OutputResult = false;
                    return false;
                }

                var item = results[index];

                switch (arrayProperty)
                {
                    case "结果分数":
                        task.OutputDoubleValue = item.Score;
                        task.OutputIntValue = (int)(item.Score * 10000);
                        task.OutputStringValue = item.Score.ToString("F4");
                        Log($"[{DateTime.Now:HH:mm:ss}] 数组解析: 结果分数[{index}] = {item.Score:F4}");
                        break;

                    case "匹配坐标":
                        task.OutputX = item.X;
                        task.OutputY = item.Y;
                        task.OutputIntValue = item.X;
                        task.OutputStringValue = $"({item.X},{item.Y})";
                        Log($"[{DateTime.Now:HH:mm:ss}] 数组解析: 匹配坐标[{index}] = ({item.X},{item.Y})");
                        break;
                }

                task.OutputResult = true;
                return true;
            }

            task.ErrorMessage = $"来源任务类型不支持数组解析: {sourceTask.TaskTypeName}";
            return false;
        }

        #endregion

        #region Shared Helpers

        /// <summary>
        /// 解析坐标表达式（X 或 Y），将结果写入 ref 参数。
        /// 若表达式为空则保持原值不变。
        /// </summary>
        private bool ResolveCoordinateExpression(string? expression, string axis, ref int coord, TaskCardBase task, IList<TaskCardBase> allTasks)
        {
            if (string.IsNullOrWhiteSpace(expression)) return true;

            try
            {
                string resolved = _variableStore.ResolveVariableReferences(expression, throwOnMissing: true);
                resolved = ExpressionEvaluator.ResolveExpression(resolved, allTasks, _variableStore);
                resolved = EvaluateArithmetic(resolved);
                if (!int.TryParse(resolved, out coord))
                {
                    task.ErrorMessage = $"{axis}坐标表达式解析失败: {expression} => {resolved}";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"{axis}坐标表达式解析异常: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 根据开关对图像应用灰度转换。若不转换则直接返回原图。
        /// </summary>
        private static Mat ApplyGrayscaleIfNeeded(Mat image, bool convertToGrayscale)
        {
            if (!convertToGrayscale) return image;
            using var colorImg = image;
            return colorImg.CvtColor(ColorConversionCodes.BGR2GRAY);
        }

        #endregion

    }
}

