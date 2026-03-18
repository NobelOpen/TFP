using System.Linq;
using System.Windows;
using TaskFlow.Models;
using TaskFlow.Resources;

namespace TaskFlow.Views.Dialogs
{
    public partial class AddVariableDialog : Window
    {
        public string VariableName { get; private set; } = string.Empty;
        public VariableType VariableType { get; private set; } = VariableType.Int;
        public string InitialValue { get; private set; } = string.Empty;

        public AddVariableDialog()
        {
            InitializeComponent();
            ApplyLocalization();

            NameTextBox.TextChanged += (s, e) =>
            {
                if (NameTextBox.Text.Any(c => char.IsPunctuation(c) || char.IsSymbol(c)))
                {
                    NameTextBox.Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        string currentText = NameTextBox.Text;
                        string newText = new string(currentText.Where(c => !char.IsPunctuation(c) && !char.IsSymbol(c)).ToArray());
                        if (currentText != newText)
                        {
                            int caret = NameTextBox.CaretIndex;
                            NameTextBox.Text = newText;
                            NameTextBox.CaretIndex = System.Math.Max(0, caret - (currentText.Length - newText.Length));
                        }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            };

            NameTextBox.Focus();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AnthropicMessageDialog.ShowWarning(Strings.Dlg_NameEmpty, Strings.Dlg_EnterVarName, this);
                return;
            }

            VariableName = name;
            VariableType = TypeComboBox.SelectedIndex switch
            {
                0 => VariableType.Int,
                1 => VariableType.String,
                2 => VariableType.Bool,
                3 => VariableType.Double,
                _ => VariableType.Int
            };

            // 验证初始值格式
            var initVal = InitialValueTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(initVal))
            {
                switch (VariableType)
                {
                    case VariableType.Int:
                        if (!int.TryParse(initVal, out _))
                        {
                            AnthropicMessageDialog.ShowError(Strings.Dlg_FormatError, Strings.Dlg_IntFormatError, this);
                            return;
                        }
                        break;
                    case VariableType.Double:
                        if (!double.TryParse(initVal, out _))
                        {
                            AnthropicMessageDialog.ShowError(Strings.Dlg_FormatError, Strings.Dlg_DoubleFormatError, this);
                            return;
                        }
                        break;
                    case VariableType.Bool:
                        if (!bool.TryParse(initVal, out _))
                        {
                            AnthropicMessageDialog.ShowError(Strings.Dlg_FormatError, Strings.Dlg_BoolFormatError, this);
                            return;
                        }
                        break;
                }
            }

            InitialValue = initVal;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ApplyLocalization()
        {
            Title = Strings.UI_AddVariable;
            TxtVarNameLabel.Text = Strings.UI_VarName;
            TxtVarTypeLabel.Text = Strings.UI_VarType;
            TxtVarInitLabel.Text = Strings.UI_VarInitValue;
            BtnOK.Content = Strings.UI_OK;
            BtnCancel.Content = Strings.UI_Cancel;
        }
    }
}
