using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using TaskFlow.Models;
using TaskFlow.Models.TaskCards;
using TaskFlow.ViewModels;
using TaskFlow.Views.Dialogs;
using TaskFlow.Resources;

namespace TaskFlow
{
    // 右键菜单事件处理
    public partial class MainWindow
    {
        #region Context Menu Events

        /// <summary>
        /// 从MenuItem向上查找关联的TaskCardBase
        /// </summary>
        private TaskCardBase? FindTaskFromMenuItem(MenuItem menuItem)
        {
            // 向上遍历逻辑树找到ContextMenu
            DependencyObject? current = menuItem;
            ContextMenu? contextMenu = null;
            while (current != null)
            {
                if (current is ContextMenu cm)
                {
                    contextMenu = cm;
                    break;
                }
                current = LogicalTreeHelper.GetParent(current);
            }

            if (contextMenu?.PlacementTarget is FrameworkElement fe && fe.DataContext is TaskCardBase task)
            {
                return task;
            }
            return null;
        }

        private void AddTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string tagStr)
            {
                if (Enum.TryParse<TaskType>(tagStr, out var taskType))
                {
                    ViewModel.AddTaskCommand.Execute(taskType);
                }
            }
        }

        private void AddTaskBelow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string tagStr)
            {
                // 先找到右键点击的卡片并选中它，确保新任务插入到正确位置
                var task = FindTaskFromMenuItem(menuItem);
                if (task != null)
                {
                    ViewModel.SelectTaskCommand.Execute(task);
                }

                if (Enum.TryParse<TaskType>(tagStr, out var taskType))
                {
                    ViewModel.AddTaskCommand.Execute(taskType);
                }
            }
        }

        private void AddBranch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string tagStr)
            {
                if (tagStr == "IfElse")
                {
                    ViewModel.AddIfElseBranchCommand.Execute(null);
                }
                else if (tagStr == "ForLoop")
                {
                    ViewModel.AddForLoopCommand.Execute(null);
                }
            }
        }

        private void AddBranchBelow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                // 先找到右键点击的卡片并选中它
                var task = FindTaskFromMenuItem(menuItem);
                if (task != null)
                {
                    ViewModel.SelectTaskCommand.Execute(task);
                }
            }
            AddBranch_Click(sender, e);
        }

        private void AddElifBranch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                var task = FindTaskFromMenuItem(menuItem);
                if (task != null && task.BranchRole == BranchRole.IfStart)
                {
                    ViewModel.AddElifBranchCommand.Execute(task);
                }
            }
        }

        private void ToggleElseVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                var task = FindTaskFromMenuItem(menuItem);
                if (task is IfElseBranchTaskCard ifCard && ifCard.BranchRole == BranchRole.IfStart && ifCard.BranchGroupId.HasValue)
                {
                    ifCard.IsElseHidden = !ifCard.IsElseHidden;

                    // 找到同组的 ElseStart 和 ElseEnd，切换它们之间所有卡片的可见性
                    var groupId = ifCard.BranchGroupId.Value;
                    int elseStartIdx = -1;
                    int elseEndIdx = -1;
                    for (int i = 0; i < ViewModel.TaskCards.Count; i++)
                    {
                        var c = ViewModel.TaskCards[i];
                        if (c.BranchGroupId == groupId && c.BranchRole == BranchRole.ElseStart)
                            elseStartIdx = i;
                        if (c.BranchGroupId == groupId && c.BranchRole == BranchRole.ElseEnd)
                            elseEndIdx = i;
                    }
                    if (elseStartIdx >= 0 && elseEndIdx > elseStartIdx)
                    {
                        // 隐藏/显示 ElseStart 到 ElseEnd 之间的所有卡片（不含 ElseEnd 本身）
                        for (int i = elseStartIdx; i < elseEndIdx; i++)
                        {
                            ViewModel.TaskCards[i].IsHiddenByCollapse = ifCard.IsElseHidden;
                        }
                        ViewModel.RefreshTaskCardsView();
                    }
                }
            }
        }

        private void SearchTask_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SearchTaskDialog(ViewModel) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.FoundTask != null)
            {
                var foundTask = dialog.FoundTask;

                // 如果目标卡片被折叠隐藏，自动展开其所有父级分支/循环
                if (foundTask.IsHiddenByCollapse)
                {
                    ExpandAncestorsOf(foundTask);
                }

                // 取消所有选中，选中目标卡片
                ViewModel.DeselectAllCommand.Execute(null);
                ViewModel.SelectTaskCommand.Execute(foundTask);

                // 滚动到目标卡片位置
                TaskCanvas.ScrollIntoView(foundTask);
            }
        }

        /// <summary>
        /// 展开目标卡片的所有被折叠的祖先分支/循环，使其在 UI 上可见
        /// </summary>
        private void ExpandAncestorsOf(TaskCardBase target)
        {
            var taskIndex = ViewModel.TaskCards.IndexOf(target);
            if (taskIndex < 0) return;

            // 从目标卡片向前搜索所有折叠的分支/循环头，收集需要展开的分支头
            var collapsedHeads = new System.Collections.Generic.List<TaskCardBase>();
            for (int i = taskIndex - 1; i >= 0; i--)
            {
                var card = ViewModel.TaskCards[i];
                if (card.IsCollapsed &&
                    card.BranchGroupId.HasValue &&
                    (card.BranchRole == BranchRole.IfStart || card.BranchRole == BranchRole.ForLoopStart))
                {
                    // 检查目标卡片是否在该分支范围内
                    if (IsTaskInsideBranch(card, target))
                    {
                        collapsedHeads.Add(card);
                    }
                }
            }

            // 从外层到内层依次展开（反转列表，因为是从后往前找的）
            collapsedHeads.Reverse();
            foreach (var head in collapsedHeads)
            {
                ViewModel.ToggleBranchCollapseCommand.Execute(head);
            }
        }

        /// <summary>
        /// 判断目标卡片是否在指定分支/循环头的范围内
        /// </summary>
        private bool IsTaskInsideBranch(TaskCardBase branchHead, TaskCardBase target)
        {
            var headIndex = ViewModel.TaskCards.IndexOf(branchHead);
            var targetIndex = ViewModel.TaskCards.IndexOf(target);
            if (headIndex < 0 || targetIndex < 0 || targetIndex <= headIndex) return false;

            // 从分支头向后找到对应的结束卡片
            var groupId = branchHead.BranchGroupId;
            for (int i = headIndex + 1; i < ViewModel.TaskCards.Count; i++)
            {
                var card = ViewModel.TaskCards[i];
                if (card.BranchGroupId == groupId)
                {
                    if ((branchHead.BranchRole == BranchRole.IfStart && card.BranchRole == BranchRole.ElseEnd) ||
                        (branchHead.BranchRole == BranchRole.ForLoopStart && card.BranchRole == BranchRole.ForLoopEnd))
                    {
                        // 目标在分支头和结束卡片之间则属于该分支
                        return targetIndex <= i;
                    }
                }
            }
            return false;
        }

        private void EditTaskProperty_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                var task = FindTaskFromMenuItem(menuItem);
                if (task == null) return;

                // 非起始分支卡片不打开属性对话框（ElifStart可以编辑）
                if (task.BranchRole != BranchRole.None &&
                    task.BranchRole != BranchRole.IfStart &&
                    task.BranchRole != BranchRole.ElifStart &&
                    task.BranchRole != BranchRole.ForLoopStart)
                {
                    return;
                }

                try
                {
                    // 自定义脚本卡片使用独立的编辑器窗口
                    if (task is CustomScriptTaskCard scriptCard)
                    {
                        var editor = new CustomScriptEditorWindow(
                            scriptCard,
                            ViewModel.ExecutionService,
                            ViewModel.TaskCards.ToList());
                        editor.Owner = this;
                        editor.ShowDialog();
                        return;
                    }

                    var dialog = new TaskPropertyDialog(task, ViewModel);
                    dialog.Owner = this;
                    if (dialog.ShowDialog() == true)
                    {
                        // 属性已保存
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"打开属性对话框失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopyTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                var task = FindTaskFromMenuItem(menuItem);
                if (task == null) return;
                ViewModel.CopyTaskCommand.Execute(task);
            }
        }

        private void PasteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                // 先选中右键点击的卡片，粘贴在其下方
                var task = FindTaskFromMenuItem(menuItem);
                if (task != null)
                {
                    ViewModel.SelectTaskCommand.Execute(task);
                }
                ViewModel.PasteTaskCommand.Execute(null);
            }
        }

        private void RenameTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                var task = FindTaskFromMenuItem(menuItem);
                if (task == null) return;

                var dialog = new RenameDialog(task.Name);
                dialog.Owner = this;
                if (dialog.ShowDialog() == true)
                {
                    task.Name = dialog.NewName;
                }
            }
        }

        private void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                var task = FindTaskFromMenuItem(menuItem);
                if (task == null) return;

                string confirmMessage;
                if (task.BranchRole == BranchRole.ElifStart)
                {
                    confirmMessage = TaskFlow.Resources.Strings.Msg_ConfirmDeleteElifBranch;
                }
                else if (task.BranchGroupId.HasValue)
                {
                    confirmMessage = TaskFlow.Resources.Strings.Msg_ConfirmDeleteBranchGroup;
                }
                else
                {
                    confirmMessage = string.Format(TaskFlow.Resources.Strings.Msg_ConfirmDeleteTask, task.Name);
                }

                if (AnthropicMessageDialog.ShowConfirm(TaskFlow.Resources.Strings.Common_Confirm, confirmMessage, this))
                {
                    ViewModel.DeleteTaskCommand.Execute(task);
                }
            }
        }

        #endregion
    }
}

