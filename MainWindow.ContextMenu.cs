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

                    // 找到同组的ElseStart卡片，切换其可见性
                    var elseStart = ViewModel.TaskCards.FirstOrDefault(t =>
                        t.BranchGroupId == ifCard.BranchGroupId && t.BranchRole == BranchRole.ElseStart);
                    if (elseStart != null)
                    {
                        elseStart.IsHiddenByCollapse = ifCard.IsElseHidden;
                    }
                }
            }
        }

        private void SearchTask_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SearchTaskDialog(ViewModel) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.FoundTask != null)
            {
                var task = dialog.FoundTask;

                // 取消所有选中，选中目标卡片
                ViewModel.DeselectAllCommand.Execute(null);
                ViewModel.SelectTaskCommand.Execute(task);

                // 滚动到目标卡片位置
                TaskCanvas.ScrollIntoView(task);
            }
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

                string confirmMessage = task.BranchGroupId.HasValue
                    ? TaskFlow.Resources.Strings.Msg_ConfirmDeleteBranchGroup
                    : string.Format(TaskFlow.Resources.Strings.Msg_ConfirmDeleteTask, task.Name);

                if (AnthropicMessageDialog.ShowConfirm(TaskFlow.Resources.Strings.Common_Confirm, confirmMessage, this))
                {
                    ViewModel.DeleteTaskCommand.Execute(task);
                }
            }
        }

        #endregion
    }
}

