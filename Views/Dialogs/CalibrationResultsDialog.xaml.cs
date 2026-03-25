using System.Windows;
using TaskFlow.Models.AiFlow;
using TaskFlow.Services;

namespace TaskFlow.Views.Dialogs
{
    public partial class CalibrationResultsDialog : Window
    {
        public CalibrationResultsDialog()
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) this.DragMove(); };
            LoadData();
        }

        private void LoadData()
        {
            CalibrationGrid.ItemsSource = CalibrationService.LoadAll();
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (CalibrationGrid.SelectedItem is CalibrationData selected)
            {
                CalibrationService.DeleteCalibration(selected.Key);
                LoadData();
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定清除所有标定数据？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                CalibrationService.ClearAll();
                LoadData();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
