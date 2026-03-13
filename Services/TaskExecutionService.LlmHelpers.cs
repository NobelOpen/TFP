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

            TaskFlow.Helpers.LlmModelManager.Load();
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

            TaskFlow.Helpers.LlmModelManager.Load();
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

            // 解析引用表达式（格式: #N 任务名.属性名）
            if (string.IsNullOrWhiteSpace(task.SourceExpression))
            {
                task.ErrorMessage = "未设置数组引用表达式";
                return false;
            }

            var pattern = @"^#(\d+)\s+([^.]+)\.(.+)$";
            var match = System.Text.RegularExpressions.Regex.Match(task.SourceExpression.Trim(), pattern);
            if (!match.Success)
            {
                task.ErrorMessage = $"引用表达式格式错误: '{task.SourceExpression}'，正确格式: #N 任务名.属性名";
                return false;
            }

            int order = int.Parse(match.Groups[1].Value);
            string taskName = match.Groups[2].Value.Trim();
            string arrayProperty = match.Groups[3].Value.Trim();

            var sourceTask = allTasks.FirstOrDefault(t => t.Order == order);
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

