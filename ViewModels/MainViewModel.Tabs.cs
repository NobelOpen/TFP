using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using TaskFlow.Models;
using TaskFlow.Models.TaskCards;
using TaskFlow.Resources;

namespace TaskFlow.ViewModels
{
    // 分页管理相关功能
    public partial class MainViewModel
    {
        /// <summary>
        /// 当选中分页变化时，切换轻量级状态（不再替换集合，由 ListBox Visibility 切换处理 UI）
        /// </summary>
        partial void OnSelectedTabChanged(WorkflowTab? oldValue, WorkflowTab? newValue)
        {
            if (_isSwitchingTab) return;
            _isSwitchingTab = true;

            try
            {
                if (oldValue != null)
                {
                    // 保存轻量级状态到旧分页
                    oldValue.NextTaskNumber = NextTaskNumber;
                    oldValue.FilePath = _currentFilePath;
                    oldValue.IsSelected = false;
                }

                if (newValue != null)
                {
                    // 恢复新分页状态（TaskCards 指向新分页的集合，但不触发 ListBox 重建）
                    TaskCards = newValue.TaskCards;
                    NextTaskNumber = newValue.NextTaskNumber;
                    if (!string.IsNullOrEmpty(newValue.FilePath))
                        _currentFilePath = newValue.FilePath;
                    newValue.IsSelected = true;
                    SelectedTask = null;
                    DisplayImage = null;
                }
            }
            finally
            {
                _isSwitchingTab = false;
            }
        }

        /// <summary>
        /// 将当前轻量级状态保存到指定分页（用于文件保存前同步）
        /// </summary>
        internal void SaveCurrentTabState(WorkflowTab tab)
        {
            // TaskCards 已直接由 Tab 拥有，无需拷贝
            tab.NextTaskNumber = NextTaskNumber;
            tab.FilePath = _currentFilePath;
        }

        /// <summary>
        /// 根据 IsCollapsed 状态重新计算所有子卡片的 IsHiddenByCollapse
        /// 用于文件加载后恢复折叠显示状态
        /// </summary>
        private void RecalculateCollapseStates()
        {
            RecalculateCollapseStatesFor(TaskCards);
        }

        /// <summary>
        /// 对指定集合根据 IsCollapsed 状态重新计算 IsHiddenByCollapse
        /// （用于文件加载时处理所有分页）
        /// </summary>
        internal static void RecalculateCollapseStatesFor(IList<TaskCardBase> cards)
        {
            // 找到所有已折叠的分支头（IfStart / ForLoopStart）
            var collapsedHeads = cards
                .Where(t => t.IsCollapsed &&
                            t.BranchGroupId.HasValue &&
                            (t.BranchRole == BranchRole.IfStart || t.BranchRole == BranchRole.ForLoopStart))
                .ToList();

            foreach (var head in collapsedHeads)
            {
                var headIndex = cards.IndexOf(head);
                if (headIndex < 0) continue;

                // 找到对应的结束卡片
                int endIndex = -1;
                var branchCards = cards
                    .Where(t => t.BranchGroupId == head.BranchGroupId && t != head)
                    .ToList();

                if (head.BranchRole == BranchRole.IfStart)
                {
                    var elseEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ElseEnd);
                    if (elseEnd != null) endIndex = cards.IndexOf(elseEnd);
                }
                else if (head.BranchRole == BranchRole.ForLoopStart)
                {
                    var loopEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ForLoopEnd);
                    if (loopEnd != null) endIndex = cards.IndexOf(loopEnd);
                }

                if (endIndex > headIndex)
                {
                    for (int i = headIndex + 1; i <= endIndex; i++)
                    {
                        cards[i].IsHiddenByCollapse = true;
                    }
                }
            }
        }

        [RelayCommand]
        private void AddTab()
        {
            var newTab = new WorkflowTab { Name = string.Format(Strings.VM_FlowDefault, NextTabIndex++) };
            Tabs.Add(newTab);
            SelectedTab = newTab;
            AddLog($"已添加分页: {newTab.Name}");
        }

        [RelayCommand]
        private void RemoveTab()
        {
            if (Tabs.Count <= 1)
            {
                AddLog("至少保留一个分页");
                return;
            }

            if (SelectedTab == null) return;

            var result = MessageBox.Show(
                $"确定删除分页 \"{SelectedTab.Name}\" 吗？",
                Strings.VM_DeleteConfirm,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var tabToRemove = SelectedTab;
            var index = Tabs.IndexOf(tabToRemove);

            // 切换到相邻分页
            if (index > 0)
                SelectedTab = Tabs[index - 1];
            else if (index < Tabs.Count - 1)
                SelectedTab = Tabs[index + 1];

            Tabs.Remove(tabToRemove);
            AddLog($"已删除分页: {tabToRemove.Name}");
        }
    }
}
