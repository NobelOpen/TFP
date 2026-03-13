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
        /// 当选中分页变化时，保存旧分页数据并恢复新分页数据
        /// </summary>
        partial void OnSelectedTabChanged(WorkflowTab? oldValue, WorkflowTab? newValue)
        {
            if (_isSwitchingTab) return;
            _isSwitchingTab = true;

            try
            {
                // 保存旧分页数据
                if (oldValue != null)
                {
                    SaveCurrentTabState(oldValue);
                    oldValue.IsSelected = false;
                }

                // 恢复新分页数据
                if (newValue != null)
                {
                    RestoreTabState(newValue);
                    newValue.IsSelected = true;
                }
            }
            finally
            {
                _isSwitchingTab = false;
            }
        }

        /// <summary>
        /// 将当前 ViewModel 数据保存到指定分页
        /// </summary>
        internal void SaveCurrentTabState(WorkflowTab tab)
        {
            tab.TaskCards = new ObservableCollection<TaskCardBase>(TaskCards);
            tab.NextTaskNumber = NextTaskNumber;
            tab.FilePath = _currentFilePath;
        }

        /// <summary>
        /// 从指定分页恢复数据到当前 ViewModel
        /// </summary>
        private void RestoreTabState(WorkflowTab tab)
        {
            TaskCards = new ObservableCollection<TaskCardBase>(tab.TaskCards);
            NextTaskNumber = tab.NextTaskNumber;
            // 仅当分页有关联路径时才更新全局路径，避免新分页的 null 覆盖已有路径
            if (!string.IsNullOrEmpty(tab.FilePath))
            {
                _currentFilePath = tab.FilePath;
            }
            SelectedTask = null;
            DisplayImage = null;
            RecalculateIndentLevels();
            RecalculateCollapseStates();
        }

        /// <summary>
        /// 根据 IsCollapsed 状态重新计算所有子卡片的 IsHiddenByCollapse
        /// 用于文件加载后恢复折叠显示状态
        /// </summary>
        private void RecalculateCollapseStates()
        {
            // 找到所有已折叠的分支头（IfStart / ForLoopStart）
            var collapsedHeads = TaskCards
                .Where(t => t.IsCollapsed &&
                            t.BranchGroupId.HasValue &&
                            (t.BranchRole == BranchRole.IfStart || t.BranchRole == BranchRole.ForLoopStart))
                .ToList();

            foreach (var head in collapsedHeads)
            {
                var headIndex = TaskCards.IndexOf(head);
                if (headIndex < 0) continue;

                // 找到对应的结束卡片
                int endIndex = -1;
                var branchCards = TaskCards
                    .Where(t => t.BranchGroupId == head.BranchGroupId && t != head)
                    .ToList();

                if (head.BranchRole == BranchRole.IfStart)
                {
                    var elseEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ElseEnd);
                    if (elseEnd != null) endIndex = TaskCards.IndexOf(elseEnd);
                }
                else if (head.BranchRole == BranchRole.ForLoopStart)
                {
                    var loopEnd = branchCards.FirstOrDefault(t => t.BranchRole == BranchRole.ForLoopEnd);
                    if (loopEnd != null) endIndex = TaskCards.IndexOf(loopEnd);
                }

                if (endIndex > headIndex)
                {
                    for (int i = headIndex + 1; i <= endIndex; i++)
                    {
                        TaskCards[i].IsHiddenByCollapse = true;
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
