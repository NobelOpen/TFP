using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TaskFlow.Models;
using TaskFlow.Resources;
using TaskFlow.Helpers;
using System.Text.RegularExpressions;

namespace TaskFlow.Views.Dialogs
{
    public partial class ModelEditDialog : Window
    {
        private readonly LlmModelConfig _modelConfig;
        private readonly string? _editingModelId;
        private bool _isUpdatingUi = false;
        
        // 用于判断用户是否真正点击了保存
        public bool IsSaved { get; private set; } = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="owner">父窗口</param>
        /// <param name="modelConfig">模型配置对象</param>
        /// <param name="titlePrefix">标题前缀</param>
        /// <param name="editingModelId">编辑模式时传入当前模型的 Id，用于重复检查时排除自身；新建模式传 null</param>
        public ModelEditDialog(Window owner, LlmModelConfig modelConfig, string titlePrefix = "", string? editingModelId = null)
        {
            Owner = owner;
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) this.DragMove(); };
            ApplyLocalization();
            
            _modelConfig = modelConfig;
            _editingModelId = editingModelId;
            Title = string.Format(Strings.Dlg_ModelTitle, titlePrefix);

            LoadModelToUi();
            BtnSave.IsEnabled = false;
        }

        private void LoadModelToUi()
        {
            _isUpdatingUi = true;
            try
            {
                TxtDisplayName.Text = _modelConfig.DisplayName;
                TxtApiEndpoint.Text = _modelConfig.ApiEndpoint;
                TxtApiKey.Text = _modelConfig.ApiKey;
                TxtModelName.Text = _modelConfig.ModelName;
                TxtTimeout.Text = _modelConfig.TimeoutSeconds.ToString();
                TxtCustomHeaders.Text = _modelConfig.CustomHeaders ?? "";
                ChkUseProxy.IsChecked = _modelConfig.UseProxy;
                TxtProxyTargetHost.Text = _modelConfig.ProxyTargetHost ?? "";
                PanelProxyConfig.Visibility = _modelConfig.UseProxy ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                UpdateProxyStatus();
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private void Field_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            BtnSave.IsEnabled = true;
        }

        private void TxtQuickImport_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi) return;

            string code = TxtQuickImport.Text;
            if (string.IsNullOrWhiteSpace(code)) return;

            bool updated = false;
            _isUpdatingUi = true; // Prevent triggering Field_TextChanged circularly if we modify upper fields

            try
            {
                // Try to detect the provider/SDK type based on keywords in the code
                bool isOpenAi = code.Contains("OpenAI(", StringComparison.OrdinalIgnoreCase) || code.Contains("from openai", StringComparison.OrdinalIgnoreCase);
                bool isAnthropic = code.Contains("Anthropic(", StringComparison.OrdinalIgnoreCase) || code.Contains("from anthropic", StringComparison.OrdinalIgnoreCase) || code.Contains("messages.create", StringComparison.OrdinalIgnoreCase);
                bool isGemini = code.Contains("GoogleGenAI", StringComparison.OrdinalIgnoreCase) || code.Contains("from google", StringComparison.OrdinalIgnoreCase) || code.Contains("gemini", StringComparison.OrdinalIgnoreCase);

                // Default fallback if we can't be sure, but model name often gives it away later
                string providerPath = "/chat/completions"; // standard OpenAI compatible path

                // Extract api_key: api_key="...", api_key='...'
                var apiKeyMatch = Regex.Match(code, @"api_key\s*=\s*([""'])(.*?)\1");
                if (apiKeyMatch.Success)
                {
                    TxtApiKey.Text = apiKeyMatch.Groups[2].Value;
                    updated = true;
                }

                // Extract model: model="...", model='...'
                var modelMatch = Regex.Match(code, @"model\s*=\s*([""'])(.*?)\1");
                if (modelMatch.Success)
                {
                    string modelName = modelMatch.Groups[2].Value;
                    TxtModelName.Text = modelName;
                    
                    // Specific refine based on model name if SDK wasn't explicit
                    if (!isOpenAi && !isAnthropic && !isGemini)
                    {
                        if (modelName.Contains("claude", StringComparison.OrdinalIgnoreCase)) isAnthropic = true;
                        else if (modelName.Contains("gemini", StringComparison.OrdinalIgnoreCase)) isGemini = true;
                        else isOpenAi = true; // assume standard openai-compatible
                    }
                    
                    // If display name is empty, auto-fill it with the model name pattern
                    if (string.IsNullOrWhiteSpace(TxtDisplayName.Text))
                    {
                        TxtDisplayName.Text = modelName;
                    }
                    updated = true;
                }

                // Determine final path to append
                if (isAnthropic)
                {
                    providerPath = "/messages";
                }
                else if (isGemini)
                {
                    // For gemini direct rest API, usually it's /v1beta/models/<model>:generateContent, 
                    // but often people use openai-compatible proxies. We'll default to the openai path 
                    // unless it's explicitly anthropic, as most 3rd party providers use OpenAI format.
                    providerPath = "/chat/completions"; 
                }

                // Extract base_url: base_url="...", base_url='...' (Do this last so we can append the path)
                var baseUrlMatch = Regex.Match(code, @"base_url\s*=\s*([""'])(.*?)\1");
                if (baseUrlMatch.Success)
                {
                    string baseUrl = baseUrlMatch.Groups[2].Value.TrimEnd('/');
                    
                    // Only append if it doesn't already contain the completion path
                    if (!baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) && 
                        !baseUrl.EndsWith("/messages", StringComparison.OrdinalIgnoreCase) &&
                        !baseUrl.EndsWith("/completions", StringComparison.OrdinalIgnoreCase))
                    {
                        baseUrl += providerPath;
                    }
                    
                    TxtApiEndpoint.Text = baseUrl;
                    updated = true;
                }

                if (updated)
                {
                    BtnSave.IsEnabled = true;
                }
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // 如果必填项为空则简单提示
            if (string.IsNullOrWhiteSpace(TxtDisplayName.Text))
            {
                MessageBox.Show(Strings.Dlg_DisplayNameEmpty, Strings.Dlg_Hint, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检查显示名称是否重复（编辑模式下排除自身）
            string newName = TxtDisplayName.Text.Trim();
            bool isDuplicate = LlmModelManager.Models.Any(m =>
                m.DisplayName.Equals(newName, System.StringComparison.OrdinalIgnoreCase)
                && m.Id != _editingModelId);
            if (isDuplicate)
            {
                MessageBox.Show(Strings.Dlg_DisplayNameDuplicate, Strings.Dlg_Hint, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            _modelConfig.DisplayName = newName;
            _modelConfig.ApiEndpoint = TxtApiEndpoint.Text.Trim();
            _modelConfig.ApiKey = TxtApiKey.Text.Trim();
            _modelConfig.ModelName = TxtModelName.Text.Trim();
            
            if (int.TryParse(TxtTimeout.Text.Trim(), out int timeout) && timeout > 0)
            {
                _modelConfig.TimeoutSeconds = timeout;
            }
            _modelConfig.CustomHeaders = TxtCustomHeaders.Text.Trim();
            _modelConfig.UseProxy = ChkUseProxy.IsChecked == true;
            _modelConfig.ProxyTargetHost = TxtProxyTargetHost.Text.Trim();

            IsSaved = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            IsSaved = false;
            Close();
        }

        private void ApplyLocalization()
        {
            Title = Strings.UI_ModelEditTitle;
            TxtDisplayNameLabel.Text = Strings.UI_DisplayName;
            TxtApiHint.Text = Strings.UI_ModelApiHint;
            TxtModelNameLabel.Text = Strings.UI_ModelName;
            TxtModelNameHint.Text = Strings.UI_ModelNameHint;
            TxtTimeoutLabel.Text = Strings.UI_TimeoutSec;
            BtnSave.Content = Strings.UI_Save;
        }

        private void ChkUseProxy_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi) return;
            PanelProxyConfig.Visibility = (ChkUseProxy.IsChecked == true)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            UpdateProxyStatus();
        }

        private void UpdateProxyStatus()
        {
            var proxy = Services.LocalProxyService.Instance;
            if (proxy.IsRunning)
                TxtProxyStatus.Text = $"● 运行中 ({proxy.ProxyBaseUrl} → {proxy.CurrentTargetHost})";
            else
                TxtProxyStatus.Text = "○ 未运行";
        }
    }
}
