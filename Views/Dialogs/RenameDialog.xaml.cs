using System.Linq;
using System.Windows;
using TaskFlow.Resources;

namespace TaskFlow.Views.Dialogs
{
    public partial class RenameDialog : Window
    {
        public string NewName { get; private set; }

        public RenameDialog(string currentName, string? dialogTitle = null, string? inputLabel = null)
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) this.DragMove(); };
            ApplyLocalization(dialogTitle, inputLabel);



            NameTextBox.Text = currentName;
            NewName = currentName;

            NameTextBox.SelectAll();
            NameTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                string rawName = NameTextBox.Text;
                string validName = new string(rawName.Where(c => !char.IsPunctuation(c) && !char.IsSymbol(c)).ToArray());
                
                if (string.IsNullOrWhiteSpace(validName))
                {
                    AnthropicMessageDialog.ShowWarning(Strings.Dlg_NameEmpty, Strings.Dlg_EnterValidName, this);
                    return;
                }
                
                NewName = validName.Trim();
                DialogResult = true;
                Close();
            }
            else
            {
                AnthropicMessageDialog.ShowWarning(Strings.Dlg_NameEmpty, Strings.Dlg_EnterValidName, this);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ApplyLocalization(string dialogTitle, string inputLabel)
        {
            Title = dialogTitle ?? Strings.UI_RenameTitle;
            DialogTitleText.Text = dialogTitle ?? Strings.UI_RenameTitle;
            TxtNewNameLabel.Text = inputLabel ?? Strings.UI_NewName;
            BtnOK.Content = Strings.UI_OK;
        }
    }
}
