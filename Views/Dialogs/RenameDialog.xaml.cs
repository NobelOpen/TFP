using System.Linq;
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
