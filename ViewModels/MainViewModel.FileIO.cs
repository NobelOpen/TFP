using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Models.TaskCards;
using TaskFlow.Resources;

namespace TaskFlow.ViewModels
{
    // 文件 IO 相关功能（新建 / 保存 / 加载 / 自动加载）
    public partial class MainViewModel
    {
        [RelayCommand]
        private void NewProject()
        {
            // 清空当前界面
            TaskCards.Clear();
            VariableStore.Variables.Clear();
            NextTaskNumber = 1;
            _currentFilePath = null;
            SelectedTask = null;
            DisplayImage = null;

            // 清空全部分页，重置索引
            _isSwitchingTab = true;
            try
            {
                Tabs.Clear();
                NextTabIndex = 1;
                var firstTab = new WorkflowTab { Name = string.Format(Strings.VM_FlowDefault, NextTabIndex++), IsSelected = true };
                Tabs.Add(firstTab);
                SelectedTab = firstTab;
            }
            finally
            {
                _isSwitchingTab = false;
            }

            WindowTitle = "TaskFlow 自动流程设计器";
            AddLog("========== 已新建流程 ==========");
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentFilePath))
                {
                    // 先同步当前分页状态
                    if (SelectedTab != null)
                        SaveCurrentTabState(SelectedTab);

                    // 保存全部分页
                    JsonHelper.SaveToFileWithTabs(_currentFilePath, Tabs, VariableStore.Variables);
                    AddLog(string.Format(Strings.VM_SavedTo, _currentFilePath));
                }
                else
                {
                    SaveAs();
                }
            }
            catch (Exception ex)
            {
                AddLog(string.Format(Strings.VM_SaveFailed, ex.Message));
                MessageBox.Show(string.Format(Strings.VM_SaveFailed, ex.Message), Strings.VM_Error, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void SaveAs()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = Strings.VM_JsonFilter,
                    DefaultExt = ".json",
                    FileName = !string.IsNullOrEmpty(_currentFilePath)
                        ? System.IO.Path.GetFileNameWithoutExtension(_currentFilePath)
                        : "workflow"
                };

                if (!string.IsNullOrEmpty(_currentFilePath))
                {
                    dialog.InitialDirectory = System.IO.Path.GetDirectoryName(_currentFilePath) ?? "";
                }

                if (dialog.ShowDialog() == true)
                {
                    // 先同步当前分页状态
                    if (SelectedTab != null)
                        SaveCurrentTabState(SelectedTab);

                    // 保存全部分页
                    JsonHelper.SaveToFileWithTabs(dialog.FileName, Tabs, VariableStore.Variables);
                    _currentFilePath = dialog.FileName;
                    SaveLastFilePath(_currentFilePath);

                    // 更新窗口标题
                    WindowTitle = $"TaskFlow - {System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)}";

                    AddLog(string.Format(Strings.VM_SavedTo, dialog.FileName));
                }
            }
            catch (Exception ex)
            {
                AddLog(string.Format(Strings.VM_SaveFailed, ex.Message));
                MessageBox.Show(string.Format(Strings.VM_SaveFailed, ex.Message), Strings.VM_Error, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void Load()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = Strings.VM_JsonFilter,
                    DefaultExt = ".json"
                };

                if (dialog.ShowDialog() == true)
                {
                    LoadFromPath(dialog.FileName);
                }
            }
            catch (Exception ex)
            {
                AddLog($"读取失败: {ex.Message}");
                MessageBox.Show($"读取失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 从指定路径加载流程文件（加载全部分页）
        /// </summary>
        public void LoadFromPath(string filePath)
        {
            var (loadedTabs, loadedVariables) = JsonHelper.LoadFromFileWithTabs(filePath);
            if (loadedTabs.Count == 0) return;

            // 记录当前文件路径
            _currentFilePath = filePath;
            SaveLastFilePath(filePath);

            // 恢复共享变量
            VariableStore.Variables.Clear();
            foreach (var v in loadedVariables)
            {
                VariableStore.Variables.Add(v);
            }

            // 更新窗口标题
            WindowTitle = $"TaskFlow - {System.IO.Path.GetFileNameWithoutExtension(filePath)}";

            // 恢复全部分页
            _isSwitchingTab = true;
            try
            {
                Tabs.Clear();
                NextTabIndex = loadedTabs.Count + 1;

                foreach (var tab in loadedTabs)
                {
                    tab.FilePath = filePath;
                    Tabs.Add(tab);
                }

                // 选中第一个分页
                SelectedTab = Tabs[0];
                SelectedTab.IsSelected = true;
                RestoreTabState(SelectedTab);
            }
            finally
            {
                _isSwitchingTab = false;
            }

            AddLog(string.Format(Strings.VM_Loaded, filePath));

            // 恢复 IsElseHidden 对应的 ElseStart 隐藏状态
            foreach (var card in TaskCards)
            {
                if (card is IfElseBranchTaskCard ifCard
                    && ifCard.BranchRole == BranchRole.IfStart
                    && ifCard.IsElseHidden
                    && ifCard.BranchGroupId.HasValue)
                {
                    var elseStart = TaskCards.FirstOrDefault(t =>
                        t.BranchGroupId == ifCard.BranchGroupId && t.BranchRole == BranchRole.ElseStart);
                    if (elseStart != null)
                    {
                        elseStart.IsHiddenByCollapse = true;
                    }
                }
            }

            RecalculateIndentLevels();
        }

        /// <summary>
        /// 启动时自动加载上次打开的流程文件
        /// </summary>
        public void AutoLoad()
        {
            try
            {
                var lastPath = GetLastFilePath();
                if (!string.IsNullOrEmpty(lastPath) && System.IO.File.Exists(lastPath))
                {
                    LoadFromPath(lastPath);
                    AddLog($"自动加载上次流程: {lastPath}");
                }
            }
            catch (Exception ex)
            {
                AddLog(string.Format(Strings.VM_AutoLoadFailed, ex.Message));
            }
        }

        /// <summary>
        /// 保存上次打开的文件路径到配置文件
        /// </summary>
        private void SaveLastFilePath(string filePath)
        {
            try
            {
                if (!System.IO.Directory.Exists(ConfigDir))
                    System.IO.Directory.CreateDirectory(ConfigDir);
                System.IO.File.WriteAllText(LastWorkflowConfigPath, filePath);
            }
            catch { /* 配置保存失败不影响主流程 */ }
        }

        /// <summary>
        /// 从配置文件读取上次打开的文件路径
        /// </summary>
        private string? GetLastFilePath()
        {
            try
            {
                if (System.IO.File.Exists(LastWorkflowConfigPath))
                    return System.IO.File.ReadAllText(LastWorkflowConfigPath).Trim();
            }
            catch { }
            return null;
        }
    }
}
