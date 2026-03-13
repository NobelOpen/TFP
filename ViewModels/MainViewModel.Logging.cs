using System;
using System.Windows;
using System.Windows.Threading;
using TaskFlow.Models;

namespace TaskFlow.ViewModels
{
    // 日志管理相关功能
    public partial class MainViewModel
    {
        /// <summary>
        /// 日志最大字符数，超过时截断旧日志
        /// </summary>
        private int _maxLogLength = 10000;
        private readonly System.Text.StringBuilder _logBuilder = new();
        private bool _isLogUpdatePending = false;
        private readonly DispatcherTimer _logThrottleTimer;

        /// <summary>
        /// 清空所有日志内容
        /// </summary>
        public void ClearLog()
        {
            lock (_logBuilder)
            {
                _logBuilder.Clear();
            }
            LogText = string.Empty;
        }

        public void AddLog(string message)
        {
            lock (_logBuilder)
            {
                _logBuilder.AppendLine(message);
                if (_logBuilder.Length > _maxLogLength)
                {
                    _logBuilder.Remove(0, _logBuilder.Length - _maxLogLength / 2); // 截断一半以避免频繁分配
                }

                if (!_isLogUpdatePending)
                {
                    _isLogUpdatePending = true;
                    // 使用节流定时器，200ms 内最多刷新一次日志 UI
                    Application.Current.Dispatcher.BeginInvoke(() => _logThrottleTimer.Start(), DispatcherPriority.Background);
                }
            }

            // 自动保存日志到文件
            if (Settings.AutoSaveLogToFile)
            {
                try
                {
                    var logDir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskFlow", "logs");
                    System.IO.Directory.CreateDirectory(logDir);
                    var logFile = System.IO.Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
                    System.IO.File.AppendAllText(logFile, message + Environment.NewLine);
                }
                catch { }
            }
        }

        /// <summary>
        /// 应用设置更新
        /// </summary>
        public void ApplySettings(AppSettings settings)
        {
            Settings = settings;
            // 更新日志最大长度（行数 * 平均每行50字符）
            _maxLogLength = settings.MaxLogLines * 50;
        }
    }
}
