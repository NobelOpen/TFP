using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskFlow.Models.TaskCards;
using TaskFlow.ViewModels;
using TaskFlow.Resources;

namespace TaskFlow.Views.Dialogs
{
    /// <summary>
    /// 查找任务对话框 - 输入编号或名称定位到指定任务卡片
    /// 支持实时补全建议
    /// </summary>
    public partial class SearchTaskDialog : Window
    {
        private readonly MainViewModel _viewModel;

        /// <summary>
        /// 查找到的任务卡片
        /// </summary>
        public TaskCardBase? FoundTask { get; private set; }

        public SearchTaskDialog(MainViewModel viewModel)
        {
            InitializeComponent();
            ApplyLocalization();
            _viewModel = viewModel;

            // 加载后聚焦输入框
            Loaded += (s, e) => SearchTextBox.Focus();
        }

        /// <summary>
        /// 输入框文本变化时更新建议列表
        /// </summary>
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string keyword = SearchTextBox.Text.Trim();

            if (string.IsNullOrEmpty(keyword))
            {
                SuggestionList.Visibility = Visibility.Collapsed;
                return;
            }

            // 去除 # 前缀
            string searchKey = keyword.StartsWith("#") ? keyword.Substring(1) : keyword;

            var matches = _viewModel.TaskCards
                .Where(t =>
                {
                    string orderStr = t.Order.ToString();
                    return orderStr.Contains(searchKey, StringComparison.OrdinalIgnoreCase) ||
                           t.Name.Contains(searchKey, StringComparison.OrdinalIgnoreCase);
                })
                .Take(10)
                .ToList();

            SuggestionList.Items.Clear();

            if (matches.Count > 0)
            {
                foreach (var task in matches)
                {
                    SuggestionList.Items.Add(new SuggestionEntry
                    {
                        Task = task,
                        DisplayText = $"#{task.Order} {task.Name}  ({task.TaskTypeName})"
                    });
                }
                SuggestionList.SelectedIndex = 0;
                SuggestionList.Visibility = Visibility.Visible;
            }
            else
            {
                SuggestionList.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 键盘导航：↑↓选择、Enter确认、Escape关闭
        /// </summary>
        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (SuggestionList.Visibility == Visibility.Visible && SuggestionList.Items.Count > 0)
            {
                switch (e.Key)
                {
                    case Key.Down:
                        if (SuggestionList.SelectedIndex < SuggestionList.Items.Count - 1)
                            SuggestionList.SelectedIndex++;
                        e.Handled = true;
                        return;

                    case Key.Up:
                        if (SuggestionList.SelectedIndex > 0)
                            SuggestionList.SelectedIndex--;
                        e.Handled = true;
                        return;

                    case Key.Enter:
                        if (SuggestionList.SelectedItem is SuggestionEntry entry)
                        {
                            SelectTask(entry.Task);
                            e.Handled = true;
                            return;
                        }
                        break;
                }
            }

            if (e.Key == Key.Enter)
                DoSearch();
            else if (e.Key == Key.Escape)
                Close();
        }

        /// <summary>
        /// 鼠标点击建议项
        /// </summary>
        private void SuggestionList_MouseClick(object sender, MouseButtonEventArgs e)
        {
            if (SuggestionList.SelectedItem is SuggestionEntry entry)
            {
                SelectTask(entry.Task);
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // 优先使用建议列表选中项
            if (SuggestionList.Visibility == Visibility.Visible &&
                SuggestionList.SelectedItem is SuggestionEntry entry)
            {
                SelectTask(entry.Task);
                return;
            }
            DoSearch();
        }

        private void SelectTask(TaskCardBase task)
        {
            FoundTask = task;
            DialogResult = true;
            Close();
        }

        private void DoSearch()
        {
            string keyword = SearchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                AnthropicMessageDialog.ShowWarning(Strings.Dlg_SearchTask, Strings.Dlg_EnterIdOrNamePrompt, this);
                return;
            }

            string searchKey = keyword.StartsWith("#") ? keyword.Substring(1) : keyword;

            // 按编号精确匹配
            if (int.TryParse(searchKey, out int order))
            {
                FoundTask = _viewModel.TaskCards.FirstOrDefault(t => t.Order == order);
            }

            // 按名称模糊匹配
            if (FoundTask == null)
            {
                FoundTask = _viewModel.TaskCards.FirstOrDefault(t =>
                    t.Name.Contains(searchKey, StringComparison.OrdinalIgnoreCase));
            }

            if (FoundTask != null)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                AnthropicMessageDialog.ShowWarning(Strings.Dlg_SearchTask, string.Format(Strings.Dlg_NotFound, keyword), this);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 建议项数据
        /// </summary>
        private class SuggestionEntry
        {
            public TaskCardBase Task { get; set; } = null!;
            public string DisplayText { get; set; } = "";
            public override string ToString() => DisplayText;
        }

        private void ApplyLocalization()
        {
            Title = Strings.UI_SearchTask;
            TxtSearchLabel.Text = Strings.UI_SearchKeyword;
            BtnSearch.Content = Strings.UI_Search;
            BtnCancel.Content = Strings.UI_Cancel;
        }
    }
}
