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
        private static readonly HttpClient _httpClient = new HttpClient();

        public ModelManagerDialog(Window owner)
        {
            Owner = owner;
            InitializeComponent();
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
                var editDialog = new ModelEditDialog(this, editingClone, Strings.Dlg_Edit_Title);
                editDialog.ShowDialog();

                if (editDialog.IsSaved)
                {
                    // 将修改同步回原对象
                    model.DisplayName = editingClone.DisplayName;
                    model.ApiEndpoint = editingClone.ApiEndpoint;
                    model.ApiKey = editingClone.ApiKey;
                    model.ModelName = editingClone.ModelName;
                    model.TimeoutSeconds = editingClone.TimeoutSeconds;

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
                object requestBody;
                if (url.Contains("/v1/responses", StringComparison.OrdinalIgnoreCase))
                {
                    requestBody = new
                    {
                        model = modelName,
                        input = new[] { new { role = "user", content = "Hello! Please reply with exactly one word: OK." } },
                        store = false,
                        stream = false
                    };
                }
                else
                {
                    requestBody = new
                    {
                        model = modelName,
                        messages = new[] { new { role = "user", content = "Hello! Please reply with exactly one word: OK." } },
                        max_tokens = 5
                    };
                }

                string jsonContent = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                
                if (!string.IsNullOrEmpty(key))
                {
                    request.Headers.Add("Authorization", $"Bearer {key}");
                }
                
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                using var cts = new System.Threading.CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(15)); 

                var response = await _httpClient.SendAsync(request, cts.Token);
                string responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    AnthropicMessageDialog.ShowInfo(Strings.Dlg_TestSuccess, string.Format(Strings.Dlg_ConnSuccess, (int)response.StatusCode, responseText.Substring(0, Math.Min(responseText.Length, 150))), this);
                }
                else
                {
                    AnthropicMessageDialog.ShowError(Strings.Dlg_TestFailed, string.Format(Strings.Dlg_ConnFailed, (int)response.StatusCode, responseText), this);
                }
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
            BtnClose.Content = Strings.UI_Close;
        }
    }
}
