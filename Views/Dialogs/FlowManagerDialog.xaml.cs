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
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) this.DragMove(); };
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
                FlowListBox.Items.Add(tab.Name.ToUpper());
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

        private void AddSubFlow_Click(object sender, RoutedEventArgs e)
        {
            var newTab = new WorkflowTab { 
                Name = "SUB_" + string.Format(Strings.Dlg_FlowPrefix, _nextTabIndex++),
                Type = FlowType.SubFlow
            };
            
            // 为子流程强制新增输入卡片
            newTab.TaskCards.Add(new TaskFlow.Models.TaskCards.SubFlowInputTaskCard { Order = 1 });

            _tabs.Add(newTab);
            _selectTabAction(newTab);
            RefreshList();
            FlowListBox.SelectedIndex = FlowListBox.Items.Count - 1;
        }

        private void RenameFlow_Click(object sender, RoutedEventArgs e)
        {
            if (FlowListBox.SelectedIndex < 0) return;

            var tab = _tabs[FlowListBox.SelectedIndex];
            var dialog = new RenameDialog(tab.Name, Strings.Dlg_RenameFlow, Strings.Dlg_EnterNewName);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
            {
                var newName = dialog.NewName.Trim();
                
                // 子流程强制添加 SUB_ 前缀
                if (tab.Type == FlowType.SubFlow && !newName.StartsWith("SUB_", StringComparison.OrdinalIgnoreCase))
                {
                    newName = "SUB_" + newName;
                }
                
                tab.Name = newName;
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
            BtnClose.Content = Strings.Dlg_OK;
        }
    }


}
