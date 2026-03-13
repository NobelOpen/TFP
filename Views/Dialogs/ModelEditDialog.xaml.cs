using System.Windows;
using System.Windows.Controls;
using TaskFlow.Models;
using TaskFlow.Resources;

namespace TaskFlow.Views.Dialogs
{
    public partial class ModelEditDialog : Window
    {
        private readonly LlmModelConfig _modelConfig;
        private bool _isUpdatingUi = false;
        
        // 用于判断用户是否真正点击了保存
        public bool IsSaved { get; private set; } = false;

        public ModelEditDialog(Window owner, LlmModelConfig modelConfig, string titlePrefix = "")
        {
            Owner = owner;
            InitializeComponent();
            ApplyLocalization();
            
            _modelConfig = modelConfig;
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

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // 如果必填项为空则简单提示（可选）
            if (string.IsNullOrWhiteSpace(TxtDisplayName.Text))
            {
                MessageBox.Show(Strings.Dlg_DisplayNameEmpty, Strings.Dlg_Hint, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            _modelConfig.DisplayName = TxtDisplayName.Text.Trim();
            _modelConfig.ApiEndpoint = TxtApiEndpoint.Text.Trim();
            _modelConfig.ApiKey = TxtApiKey.Text.Trim();
            _modelConfig.ModelName = TxtModelName.Text.Trim();
            
            if (int.TryParse(TxtTimeout.Text.Trim(), out int timeout) && timeout > 0)
            {
                _modelConfig.TimeoutSeconds = timeout;
            }

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
            BtnCancel.Content = Strings.UI_Cancel;
        }
    }
}
