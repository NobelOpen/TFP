using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TaskFlow.Helpers;
using TaskFlow.Models;

namespace TaskFlow.Views.Dialogs
{
    public partial class OnnxModelManagerDialog : Window
    {
        /// <summary>当前正在编辑的模型（null 表示未编辑）</summary>
        private OnnxModelConfig? _editingModel;

        public OnnxModelManagerDialog(Window owner)
        {
            Owner = owner;
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) this.DragMove(); };
            RefreshList();
        }

        private void RefreshList()
        {
            ModelGrid.ItemsSource = null;
            ModelGrid.ItemsSource = OnnxModelManager.Models;
        }

        private void ModelGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = ModelGrid.SelectedItem is OnnxModelConfig;
            BtnEdit.IsEnabled = hasSelection;
            BtnDelete.IsEnabled = hasSelection;

            // 如果编辑面板打开且切换了选择，关闭编辑面板
            if (_editingModel != null && ModelGrid.SelectedItem is OnnxModelConfig selected && selected != _editingModel)
            {
                EditPanel.Visibility = Visibility.Collapsed;
                _editingModel = null;
            }
        }

        /// <summary>
        /// 导入 .onnx 模型文件
        /// </summary>
        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 ONNX 模型文件",
                Filter = "ONNX 模型 (*.onnx)|*.onnx",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                // 确保目标目录存在
                Directory.CreateDirectory(OnnxModelConfig.ModelsDir);

                var sourceFile = dialog.FileName;
                var originalName = Path.GetFileName(sourceFile);

                // 生成唯一文件名，避免覆盖
                var targetName = originalName;
                var targetPath = Path.Combine(OnnxModelConfig.ModelsDir, targetName);
                int counter = 1;
                while (File.Exists(targetPath))
                {
                    targetName = $"{Path.GetFileNameWithoutExtension(originalName)}_{counter}{Path.GetExtension(originalName)}";
                    targetPath = Path.Combine(OnnxModelConfig.ModelsDir, targetName);
                    counter++;
                }

                // 复制文件到统一目录
                File.Copy(sourceFile, targetPath);

                // 创建模型配置
                var newModel = new OnnxModelConfig
                {
                    DisplayName = Path.GetFileNameWithoutExtension(originalName),
                    FileName = targetName
                };

                OnnxModelManager.Models.Add(newModel);
                OnnxModelManager.NotifyModelsChanged();
                TriggerProjectSave();
                RefreshList();
                ModelGrid.SelectedItem = newModel;

                // 自动打开编辑面板让用户设置类别标签
                ShowEditPanel(newModel);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入模型失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 编辑选中的模型配置
        /// </summary>
        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (ModelGrid.SelectedItem is OnnxModelConfig model)
            {
                ShowEditPanel(model);
            }
        }

        /// <summary>
        /// 显示编辑面板并填入当前模型数据
        /// </summary>
        private void ShowEditPanel(OnnxModelConfig model)
        {
            _editingModel = model;
            TxtDisplayName.Text = model.DisplayName;
            TxtInputW.Text = model.InputWidth.ToString();
            TxtInputH.Text = model.InputHeight.ToString();
            TxtConfidence.Text = model.ConfidenceThreshold.ToString("F2");
            TxtLabels.Text = model.ClassLabels;
            EditPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 保存编辑面板的修改
        /// </summary>
        private void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_editingModel == null) return;

            _editingModel.DisplayName = TxtDisplayName.Text.Trim();

            if (int.TryParse(TxtInputW.Text.Trim(), out int w) && w > 0)
                _editingModel.InputWidth = w;
            if (int.TryParse(TxtInputH.Text.Trim(), out int h) && h > 0)
                _editingModel.InputHeight = h;
            if (double.TryParse(TxtConfidence.Text.Trim(), out double conf) && conf >= 0 && conf <= 1)
                _editingModel.ConfidenceThreshold = conf;

            _editingModel.ClassLabels = TxtLabels.Text.Trim();

            OnnxModelManager.NotifyModelsChanged();
            TriggerProjectSave();

            int selectedIndex = ModelGrid.SelectedIndex;
            RefreshList();
            ModelGrid.SelectedIndex = selectedIndex;

            EditPanel.Visibility = Visibility.Collapsed;
            _editingModel = null;
        }

        /// <summary>
        /// 删除选中的模型及其文件
        /// </summary>
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (ModelGrid.SelectedItem is not OnnxModelConfig model) return;

            var result = MessageBox.Show($"确定删除模型 \"{model.DisplayName}\"？\n模型文件也将从磁盘移除。",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            // 删除磁盘文件
            try
            {
                if (File.Exists(model.FilePath))
                    File.Delete(model.FilePath);
            }
            catch { /* 文件删除失败不影响列表移除 */ }

            OnnxModelManager.Models.Remove(model);
            OnnxModelManager.NotifyModelsChanged();
            TriggerProjectSave();
            RefreshList();

            EditPanel.Visibility = Visibility.Collapsed;
            _editingModel = null;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 触发项目自动保存（与 ModelManagerDialog 同逻辑）
        /// </summary>
        private void TriggerProjectSave()
        {
            if (Application.Current.MainWindow?.DataContext is TaskFlow.ViewModels.MainViewModel mainVM)
            {
                if (!string.IsNullOrEmpty(mainVM.CurrentFilePath) && mainVM.SaveCommand != null && mainVM.SaveCommand.CanExecute(null))
                {
                    mainVM.SaveCommand.Execute(null);
                }
            }
        }
    }
}
