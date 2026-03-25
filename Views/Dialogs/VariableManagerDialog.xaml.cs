using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskFlow.Models;
using TaskFlow.Resources;

namespace TaskFlow.Views.Dialogs
{
    public partial class VariableManagerDialog : Window
    {
        private readonly VariableStore _variableStore;

        public VariableManagerDialog(VariableStore variableStore)
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) this.DragMove(); };
            ApplyLocalization();
            _variableStore = variableStore;
            VariableGrid.ItemsSource = _variableStore.Variables;
            // 单击进入编辑模式
            VariableGrid.PreviewMouseLeftButtonDown += VariableGrid_PreviewMouseLeftButtonDown;
            // 点击窗口空白区域退出编辑
            this.PreviewMouseLeftButtonDown += Window_PreviewMouseLeftButtonDown;
        }

        private void VariableGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 查找点击的 DataGridCell
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null && hit is not DataGridCell)
                hit = VisualTreeHelper.GetParent(hit);

            if (hit is DataGridCell cell && !cell.IsReadOnly && !cell.IsEditing)
            {
                if (!cell.IsFocused) cell.Focus();
                VariableGrid.BeginEdit();
                e.Handled = true;
            }
        }

        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 点击 DataGrid 以外的区域时提交编辑
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null && hit is not DataGrid && hit is not Window)
                hit = VisualTreeHelper.GetParent(hit);

            if (hit is not DataGrid)
            {
                VariableGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
        }

        private void AddVariable_Click(object sender, RoutedEventArgs e)
        {
            // 弹出添加变量对话框
            var dialog = new AddVariableDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                if (!_variableStore.AddVariable(dialog.VariableName, dialog.VariableType, dialog.InitialValue))
                {
                    AnthropicMessageDialog.ShowWarning(Strings.Dlg_NameConflict, Strings.Dlg_VarNameExists, this);
                }
            }
        }

        private void RenameVariable_Click(object sender, RoutedEventArgs e)
        {
            if (VariableGrid.SelectedItem is Variable selected)
            {
                var dialog = new RenameDialog(selected.Name);
                dialog.Owner = this;
                if (dialog.ShowDialog() == true)
                {
                    if (!_variableStore.RenameVariable(selected.Name, dialog.NewName))
                    {
                        AnthropicMessageDialog.ShowWarning(Strings.Dlg_NameConflict, Strings.Dlg_NewNameExists, this);
                    }
                }
            }
            else
            {
                AnthropicMessageDialog.ShowInfo(Strings.Dlg_NoVarSelected, Strings.Dlg_SelectVarFirst, this);
            }
        }

        private void DeleteVariable_Click(object sender, RoutedEventArgs e)
        {
            if (VariableGrid.SelectedItem is Variable selected)
            {
                var confirmed = AnthropicMessageDialog.ShowConfirm(
                    Strings.Dlg_DeleteConfirm,
                    string.Format(Strings.Dlg_ConfirmDeleteVar, selected.Name),
                    this);

                if (confirmed)
                {
                    _variableStore.RemoveVariable(selected.Name);
                }
            }
            else
            {
                AnthropicMessageDialog.ShowInfo(Strings.Dlg_NoVarSelected, Strings.Dlg_SelectVarFirst, this);
            }
        }

        private void VariableGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Cancel) return;
            if (e.Row.Item is not Variable variable) return;
            if (e.EditingElement is not TextBox textBox) return;

            var newValue = textBox.Text.Trim();
            string? errorMsg = variable.Type switch
            {
                VariableType.Int when !int.TryParse(newValue, out _) => Strings.Dlg_IntValueError,
                VariableType.Double when !double.TryParse(newValue, out _) => Strings.Dlg_DoubleValueError,
                VariableType.Bool when !bool.TryParse(newValue, out _) => Strings.Dlg_BoolValueError,
                _ => null
            };

            if (errorMsg != null)
            {
                // 还原为之前的值（让提交使用原值）
                textBox.Text = variable.Value;
                AnthropicMessageDialog.ShowError(Strings.Dlg_FormatError, errorMsg, this);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void ApplyLocalization()
        {
            Title = Strings.UI_VariableManager;
            TxtVarsTitle.Text = Strings.UI_GlobalVars;
            ColName.Header = Strings.UI_VarColumnName;
            ColType.Header = Strings.UI_VarColumnType;
            ColValue.Header = Strings.UI_VarColumnValue;
            BtnAdd.Content = Strings.UI_Add;
            BtnRename.Content = Strings.UI_Rename;
            BtnDelete.Content = Strings.UI_Delete;
            BtnClose.Content = Strings.Dlg_OK;
        }
    }
}
