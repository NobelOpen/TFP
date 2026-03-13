using System.Collections.ObjectModel;
using System.Windows;
using TaskFlow.Models;
using TaskFlow.Resources;

namespace TaskFlow.Views.Dialogs
{
    public partial class FlowManagerDialog : Window
    {
        private readonly ObservableCollection<WorkflowTab> _tabs;
        private readonly Action<WorkflowTab> _selectTabAction;
        private int _nextTabIndex;

        public FlowManagerDialog(ObservableCollection<WorkflowTab> tabs, Action<WorkflowTab> selectTabAction, int nextTabIndex)
        {
            InitializeComponent();
            ApplyLocalization();
            _tabs = tabs;
            _selectTabAction = selectTabAction;
            _nextTabIndex = nextTabIndex;
            RefreshList();
        }

        /// <summary>
        /// 获取下一个分页索引（供外部更新）
        /// </summary>
        public int NextTabIndex => _nextTabIndex;

        private void RefreshList()
        {
            FlowListBox.Items.Clear();
            foreach (var tab in _tabs)
            {
                FlowListBox.Items.Add(tab.Name);
            }
            if (FlowListBox.Items.Count > 0)
            {
                FlowListBox.SelectedIndex = 0;
            }
        }

        private void AddFlow_Click(object sender, RoutedEventArgs e)
        {
            var newTab = new WorkflowTab { Name = string.Format(Strings.Dlg_FlowPrefix, _nextTabIndex++) };
            _tabs.Add(newTab);
            _selectTabAction(newTab);
            RefreshList();
            FlowListBox.SelectedIndex = FlowListBox.Items.Count - 1;
        }

        private void RenameFlow_Click(object sender, RoutedEventArgs e)
        {
            if (FlowListBox.SelectedIndex < 0) return;

            var tab = _tabs[FlowListBox.SelectedIndex];
            var dialog = new InputDialog(Strings.Dlg_RenameFlow, Strings.Dlg_EnterNewName, tab.Name);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                tab.Name = dialog.InputText.Trim();
                RefreshList();
            }
        }

        private void DeleteFlow_Click(object sender, RoutedEventArgs e)
        {
            if (FlowListBox.SelectedIndex < 0) return;

            if (_tabs.Count <= 1)
            {
                AnthropicMessageDialog.ShowInfo(Strings.Dlg_CannotDelete, Strings.Dlg_KeepOneFlow, this);
                return;
            }

            var tab = _tabs[FlowListBox.SelectedIndex];
            var confirmed = AnthropicMessageDialog.ShowConfirm(
                Strings.Dlg_DeleteConfirm,
                string.Format(Strings.Dlg_ConfirmDeleteFlow, tab.Name),
                this);

            if (!confirmed) return;

            var index = FlowListBox.SelectedIndex;
            _tabs.Remove(tab);

            // 选中相邻流程
            if (_tabs.Count > 0)
            {
                _selectTabAction(_tabs[Math.Min(index, _tabs.Count - 1)]);
            }

            RefreshList();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ApplyLocalization()
        {
            Title = Strings.UI_FlowManager;
            TxtFlowTitle.Text = Strings.UI_FlowManagerTitle;
            BtnAdd.Content = Strings.UI_Add;
            BtnRename.Content = Strings.UI_Rename;
            BtnDelete.Content = Strings.UI_Delete;
            BtnClose.Content = Strings.UI_Close;
        }
    }

    /// <summary>
    /// 通用输入对话框
    /// </summary>
    public class InputDialog : Window
    {
        private readonly System.Windows.Controls.TextBox _textBox;

        public string InputText => _textBox.Text;

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            Title = title;
            Width = 350;
            Height = 160;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(250, 249, 245));
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = prompt,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(20, 20, 19)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            };
            System.Windows.Controls.Grid.SetRow(label, 0);
            grid.Children.Add(label);

            _textBox = new System.Windows.Controls.TextBox
            {
                Text = defaultValue,
                FontSize = 14,
                Padding = new Thickness(6, 4, 6, 4),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 255, 255)),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(20, 20, 19)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(232, 230, 220)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            _textBox.SelectAll();
            System.Windows.Controls.Grid.SetRow(_textBox, 1);
            grid.Children.Add(_textBox);

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            System.Windows.Controls.Grid.SetRow(btnPanel, 2);

            var okBtn = new System.Windows.Controls.Button
            {
                Content = Strings.Dlg_OK,
                Width = 70,
                Padding = new Thickness(0, 4, 0, 4),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(217, 119, 87)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            okBtn.Click += (s, e) => { DialogResult = true; };

            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = Strings.Dlg_Cancel,
                Width = 70,
                Padding = new Thickness(0, 4, 0, 4),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(240, 239, 232)),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(20, 20, 19)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; };

            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            grid.Children.Add(btnPanel);

            Content = grid;

            Loaded += (s, e) => { _textBox.Focus(); };
        }

    }
}
