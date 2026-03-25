using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TaskFlow.Models;
using TaskFlow.Resources;
using TaskFlow.Helpers;

namespace TaskFlow.Views.Dialogs
{
    public partial class ModelManagerDialog : Window
    {
        private static readonly HttpClient _httpClient;

        static ModelManagerDialog()
        {
            // 使用 WinHttpHandler（与 PowerShell 共享 WinHTTP 栈），避免 Cloudflare TLS 指纹拦截
            var handler = new System.Net.Http.WinHttpHandler();
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        }

        public ModelManagerDialog(Window owner)
        {
            Owner = owner;
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) this.DragMove(); };
            ApplyLocalization();
            RefreshList();
        }

        private void RefreshList()
        {
            ModelGrid.ItemsSource = null;
            ModelGrid.ItemsSource = LlmModelManager.Models;
        }

        private void ModelGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = ModelGrid.SelectedItem is LlmModelConfig;
            BtnEdit.IsEnabled = hasSelection;
            BtnDelete.IsEnabled = hasSelection;
            BtnTest.IsEnabled = hasSelection;
            BtnReset.IsEnabled = hasSelection;
        }

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            var newModel = new LlmModelConfig();
            var editDialog = new ModelEditDialog(this, newModel, Strings.Dlg_Add_Title);
            editDialog.ShowDialog();

            if (editDialog.IsSaved)
            {
                LlmModelManager.Models.Add(newModel);
                LlmModelManager.NotifyModelsChanged();
                TriggerProjectSave();
                RefreshList();
                ModelGrid.SelectedItem = newModel;
            }
        }

        private void EditBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ModelGrid.SelectedItem is LlmModelConfig model)
            {
                // 使用克隆的对象进行编辑，防止取消保存时修改了原引用
                var editingClone = model.Clone();
                var editDialog = new ModelEditDialog(this, editingClone, Strings.Dlg_Edit_Title, model.Id);
                editDialog.ShowDialog();

                if (editDialog.IsSaved)
                {
                    // 将修改同步回原对象
                    model.DisplayName = editingClone.DisplayName;
                    model.ApiEndpoint = editingClone.ApiEndpoint;
                    model.ApiKey = editingClone.ApiKey;
                    model.ModelName = editingClone.ModelName;
                    model.TimeoutSeconds = editingClone.TimeoutSeconds;
                    model.CustomHeaders = editingClone.CustomHeaders;
                    model.UseProxy = editingClone.UseProxy;
                    model.ProxyTargetHost = editingClone.ProxyTargetHost;

                    LlmModelManager.NotifyModelsChanged();
                    TriggerProjectSave();
                    
                    int selectedIndex = ModelGrid.SelectedIndex;
                    RefreshList();
                    ModelGrid.SelectedIndex = selectedIndex;
                }
            }
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ModelGrid.SelectedItem is LlmModelConfig model)
            {
                var confirmed = AnthropicMessageDialog.ShowConfirm(Strings.Dlg_DeleteConfirm, string.Format(Strings.Dlg_ConfirmDeleteModel, model.DisplayName),
                    this);
                if (confirmed)
                {
                    LlmModelManager.Models.Remove(model);
                    LlmModelManager.NotifyModelsChanged();
                    TriggerProjectSave();
                    RefreshList();
                }
            }
        }

        private void ResetStatsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ModelGrid.SelectedItem is LlmModelConfig model)
            {
                var confirmed = AnthropicMessageDialog.ShowConfirm(Strings.Dlg_ResetConfirm, string.Format(Strings.Dlg_ConfirmResetStats, model.DisplayName), this);
                if (confirmed)
                {
                    model.TotalInputTokens = 0;
                    model.TotalOutputTokens = 0;
                    TriggerProjectSave();
                    
                    int selectedIndex = ModelGrid.SelectedIndex;
                    RefreshList();
                    ModelGrid.SelectedIndex = selectedIndex;
                }
            }
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
        /// 为 Gemini API 构建完整的 URL：{baseUrl}/models/{modelName}:generateContent
        /// </summary>
        private static string BuildGeminiUrl(string baseUrl, string modelName)
        {
            // 去掉模型名中可能多余的 :generateContent 后缀
            var cleanModel = modelName.Replace(":generateContent", "").Trim();
            var cleanBase = baseUrl.TrimEnd('/');
            return $"{cleanBase}/models/{cleanModel}:generateContent";
        }

        private async void TestBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!(ModelGrid.SelectedItem is LlmModelConfig model)) return;
            
            string url = model.ApiEndpoint;
            string key = model.ApiKey;
            string modelName = model.ModelName;
            
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(modelName))
            {
                AnthropicMessageDialog.ShowWarning(Strings.Dlg_TestFailed, Strings.Dlg_ApiEmptyError, this);
                return;
            }

            var btn = sender as Button;
            string oldContent = btn.Content.ToString();
            btn.Content = Strings.Dlg_Testing;

            // 测试中禁用窗口所有交互
            this.IsEnabled = false;

            try
            {
                string actualUrl;
                string jsonContent;
                bool isGemini = IsGeminiApi(url);
                bool isResponsesApi = url.Contains("/v1/responses", StringComparison.OrdinalIgnoreCase);

                if (isGemini)
                {
                    // Gemini API：使用 contents/parts 格式
                    actualUrl = BuildGeminiUrl(url, modelName);
                    var requestBody = new
                    {
                        contents = new[] { new { parts = new[] { new { text = "Hello! Please reply with exactly one word: OK." } } } }
                    };
                    jsonContent = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
                }
                else if (isResponsesApi)
                {
                    // OpenAI Responses API（流式测试，某些模型仅支持流式）
                    actualUrl = url;
                    var requestBody = new
                    {
                        model = modelName,
                        input = new[] { new { role = "user", content = "Hello! Please reply with exactly one word: OK." } },
                        store = false,
                        stream = true
                    };
                    jsonContent = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
                }
                else
                {
                    // OpenAI Chat Completions API（非流式测试）
                    actualUrl = url;
                    var requestBody = new
                    {
                        model = modelName,
                        messages = new[] { new { role = "user", content = "Hello! Please reply with exactly one word: OK." } },
                        max_tokens = 5
                    };
                    jsonContent = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
                }

                // 如果启用了代理，启动代理并替换 URL
                if (model.UseProxy && !string.IsNullOrEmpty(model.ProxyTargetHost))
                {
                    var (ok, msg) = Services.LocalProxyService.Instance.EnsureRunning(model.ProxyTargetHost);
                    if (ok)
                        actualUrl = Services.LocalProxyService.Instance.GetProxiedUrl(actualUrl);
                    else
                    {
                        AnthropicMessageDialog.ShowError(Strings.Dlg_TestFailed, $"代理启动失败: {msg}", this);
                        return;
                    }
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, actualUrl);
                request.Version = System.Net.HttpVersion.Version11; // 强制 HTTP/1.1，部分 CDN 对 HTTP/2 返回 502
                
                if (!string.IsNullOrEmpty(key))
                {
                    if (isGemini)
                    {
                        request.Headers.Add("X-goog-api-key", key);
                    }
                    else
                    {
                        request.Headers.Add("Authorization", $"Bearer {key}");
                    }
                }

                // 注入自定义请求头
                if (!string.IsNullOrEmpty(model.CustomHeaders))
                {
                    foreach (var line in model.CustomHeaders.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var idx = line.IndexOf(':');
                        if (idx > 0)
                        {
                            var headerKey = line.Substring(0, idx).Trim();
                            var headerVal = line.Substring(idx + 1).Trim();
                            if (!string.IsNullOrEmpty(headerKey))
                            {
                                // Host 头需要特殊处理
                                if (headerKey.Equals("Host", StringComparison.OrdinalIgnoreCase))
                                    request.Headers.Host = headerVal;
                                else
                                    request.Headers.TryAddWithoutValidation(headerKey, headerVal);
                            }
                        }
                    }
                }
                
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                using var cts = new System.Threading.CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(30)); 

                // ===== 临时诊断日志 =====
                var logBuilder = new StringBuilder();
                logBuilder.AppendLine($"===== 测试连接诊断 {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
                logBuilder.AppendLine($"URL: {actualUrl}");
                logBuilder.AppendLine($"Method: POST");
                logBuilder.AppendLine($"isGemini: {isGemini}, isResponsesApi: {isResponsesApi}");
                logBuilder.AppendLine($"--- 请求头 ---");
                foreach (var h in request.Headers)
                    logBuilder.AppendLine($"  {h.Key}: {string.Join(", ", h.Value)}");
                foreach (var h in request.Content.Headers)
                    logBuilder.AppendLine($"  {h.Key}: {string.Join(", ", h.Value)}");
                logBuilder.AppendLine($"--- 请求体 ---");
                logBuilder.AppendLine(jsonContent);

                // Responses API 使用 ResponseHeadersRead（流式），其他用默认模式
                var completionOption = isResponsesApi
                    ? System.Net.Http.HttpCompletionOption.ResponseHeadersRead
                    : System.Net.Http.HttpCompletionOption.ResponseContentRead;
                var response = await _httpClient.SendAsync(request, completionOption, cts.Token);

                logBuilder.AppendLine($"--- 响应状态 ---");
                logBuilder.AppendLine($"StatusCode: {(int)response.StatusCode} {response.ReasonPhrase}");
                logBuilder.AppendLine($"--- 响应头 ---");
                foreach (var h in response.Headers)
                    logBuilder.AppendLine($"  {h.Key}: {string.Join(", ", h.Value)}");
                foreach (var h in response.Content.Headers)
                    logBuilder.AppendLine($"  {h.Key}: {string.Join(", ", h.Value)}");

                // 非成功时读取响应体写入日志
                if (!response.IsSuccessStatusCode)
                {
                    string errBody = await response.Content.ReadAsStringAsync();
                    logBuilder.AppendLine($"--- 响应体 ---");
                    logBuilder.AppendLine(errBody.Length > 500 ? errBody.Substring(0, 500) : errBody);
                }
                // HttpClient 默认请求头
                logBuilder.AppendLine($"--- HttpClient 默认头 ---");
                foreach (var h in _httpClient.DefaultRequestHeaders)
                    logBuilder.AppendLine($"  {h.Key}: {string.Join(", ", h.Value)}");

                logBuilder.AppendLine("=============================");
                
                // 写入日志文件
                try
                {
                    var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_connection.log");
                    System.IO.File.AppendAllText(logPath, logBuilder.ToString() + "\n");
                }
                catch { }

                if (response.IsSuccessStatusCode)
                {
                    if (isResponsesApi)
                    {
                        // Responses API 流式：逐行读取 SSE 数据，提取文本和 usage
                        var textBuilder = new StringBuilder();
                        int testInputTokens = 0, testOutputTokens = 0;
                        using var stream = await response.Content.ReadAsStreamAsync();
                        using var reader = new System.IO.StreamReader(stream);
                        while (!reader.EndOfStream && !cts.Token.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync();
                            if (line == null) break;
                            line = line.Trim();
                            if (!line.StartsWith("data:")) continue;
                            var jsonPart = line.Substring(5).Trim();
                            if (jsonPart == "[DONE]") break;
                            if (string.IsNullOrEmpty(jsonPart)) continue;
                            try
                            {
                                var evt = Newtonsoft.Json.Linq.JObject.Parse(jsonPart);
                                var evtType = evt["type"]?.ToString();
                                if (evtType == "response.output_text.delta")
                                    textBuilder.Append(evt["delta"]?.ToString());
                                else if (evtType == "response.completed")
                                {
                                    var usage = evt["response"]?["usage"];
                                    if (usage != null)
                                    {
                                        testInputTokens = (int?)usage["input_tokens"] ?? 0;
                                        testOutputTokens = (int?)usage["output_tokens"] ?? 0;
                                    }
                                    break; // 收到 completed 事件即可停止
                                }
                            }
                            catch { }
                        }
                        string preview = textBuilder.Length > 0
                            ? textBuilder.ToString()
                            : "(流式连接成功，未提取到文本)";
                        if (preview.Length > 150) preview = preview.Substring(0, 150);
                        AnthropicMessageDialog.ShowInfo(Strings.Dlg_TestSuccess,
                            string.Format(Strings.Dlg_ConnSuccess, (int)response.StatusCode, preview),
                            this);
                    }
                    else
                    {
                        // 非流式响应：直接读取完整 JSON
                        string responseText = await response.Content.ReadAsStringAsync();
                        var trimmed = responseText.TrimStart();
                        if (trimmed.StartsWith("<!") || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                        {
                            AnthropicMessageDialog.ShowError(Strings.Dlg_TestFailed,
                                $"服务器返回了 HTML 网页而非 API 响应，请检查 API 地址是否正确。\n常见原因：缺少 /v1 前缀（如应使用 .../v1/chat/completions）",
                                this);
                        }
                        else
                        {
                            string preview = "";
                            try
                            {
                                var json = Newtonsoft.Json.Linq.JObject.Parse(responseText);
                                if (isGemini)
                                    preview = json["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString() ?? "";
                                else
                                    preview = json["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
                            }
                            catch { }
                            if (string.IsNullOrEmpty(preview))
                                preview = responseText.Substring(0, Math.Min(responseText.Length, 150));
                            if (preview.Length > 150) preview = preview.Substring(0, 150);
                            AnthropicMessageDialog.ShowInfo(Strings.Dlg_TestSuccess, string.Format(Strings.Dlg_ConnSuccess, (int)response.StatusCode, preview), this);
                        }
                    }
                }
                else
                {
                    string responseText = await response.Content.ReadAsStringAsync();
                    AnthropicMessageDialog.ShowError(Strings.Dlg_TestFailed, string.Format(Strings.Dlg_ConnFailed, (int)response.StatusCode, responseText), this);
                }
            }
            catch (OperationCanceledException)
            {
                AnthropicMessageDialog.ShowError(Strings.Dlg_TestFailed, string.Format(Strings.Dlg_ConnException, "连接超时（30秒），请检查网络连接或 API 地址是否正确。"), this);
            }
            catch (HttpRequestException httpEx)
            {
                AnthropicMessageDialog.ShowError(Strings.Dlg_TestFailed, string.Format(Strings.Dlg_ConnException, $"网络请求失败：{httpEx.Message}"), this);
            }
            catch (Exception ex)
            {
                AnthropicMessageDialog.ShowError(Strings.Dlg_TestFailed, string.Format(Strings.Dlg_ConnException, ex.Message), this);
            }
            finally
            {
                // 恢复窗口交互
                this.IsEnabled = true;
                btn.Content = oldContent;
                // 恢复按钮状态（根据当前选择）
                ModelGrid_SelectionChanged(ModelGrid, null!);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TriggerProjectSave()
        {
            if (Application.Current.MainWindow?.DataContext is TaskFlow.ViewModels.MainViewModel mainVM)
            {
                // 如果当前是一个新创建但未保存的项目 (路径为空)，
                // 不要自动触发 SaveCommand，否则会弹出“另存为”对话框打断用户。
                if (!string.IsNullOrEmpty(mainVM.CurrentFilePath) && mainVM.SaveCommand != null && mainVM.SaveCommand.CanExecute(null))
                {
                    mainVM.SaveCommand.Execute(null);
                }
            }
        }

        private void ApplyLocalization()
        {
            Title = Strings.UI_ModelManager;
            TxtModelsTitle.Text = Strings.UI_AllModels;
            ColDisplayName.Header = Strings.UI_ModelDisplayName;
            ColTokens.Header = Strings.UI_ModelTokenStats;
            BtnAdd.Content = Strings.UI_AddModel;
            BtnEdit.Content = Strings.UI_EditModel;
            BtnDelete.Content = Strings.UI_DeleteModel;
            BtnTest.Content = Strings.UI_TestModel;
            BtnReset.Content = Strings.UI_ResetTokens;
            BtnClose.Content = Strings.Dlg_OK;
        }
    }
}
