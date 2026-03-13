using System.Windows;
using TaskFlow.Resources;

namespace TaskFlow.Views.Dialogs
{
    public partial class RenameDialog : Window
    {
        public string NewName { get; private set; }

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            ApplyLocalization();
            NameTextBox.Text = currentName;
            NewName = currentName;

            NameTextBox.SelectAll();
            NameTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                NewName = NameTextBox.Text.Trim();
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

        private void ApplyLocalization()
        {
            Title = Strings.UI_RenameTitle;
            TxtNewNameLabel.Text = Strings.UI_NewName;
            BtnOK.Content = Strings.UI_OK;
            BtnCancel.Content = Strings.UI_Cancel;
        }
    }
}
