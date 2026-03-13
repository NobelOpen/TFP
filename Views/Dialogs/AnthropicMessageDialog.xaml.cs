using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskFlow.Views.Dialogs
{
    /// <summary>
    /// Anthropic 风格的自定义消息对话框
    /// 支持 Info / Warning / Error / Confirm 四种类型
    /// </summary>
    public partial class AnthropicMessageDialog : Window
    {
        /// <summary>
        /// 消息类型枚举
        /// </summary>
        public enum MsgType
        {
            Info,       // 信息提示
            Warning,    // 警告提示
            Error,      // 错误提示
            Confirm     // 确认询问（是/否）
        }

        /// <summary>
        /// 用户点击的结果
        /// </summary>
        public bool IsConfirmed { get; private set; }

        public AnthropicMessageDialog(string title, string message, MsgType type = MsgType.Info)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;

            // 根据类型设置图标和样式
            ApplyTypeStyle(type);
            // 根据类型生成按钮
            CreateButtons(type);

            // 支持拖动窗口
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };

            // ESC 关闭
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    IsConfirmed = false;
                    Close();
                }
            };

            // 加载后动画效果
            Loaded += (s, e) =>
            {
                Opacity = 0;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                    TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                };
                BeginAnimation(OpacityProperty, anim);
            };
        }

        private void ApplyTypeStyle(MsgType type)
        {
            switch (type)
            {
                case MsgType.Info:
                    // 蓝灰色调信息图标
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(234, 240, 246));
                    IconText.Text = "i";
                    IconText.FontStyle = FontStyles.Italic;
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(107, 131, 155));
                    break;
                case MsgType.Warning:
                    // 暖橙色警告图标
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(254, 240, 234));
                    IconText.Text = "!";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 87));
                    break;
                case MsgType.Error:
                    // 红色错误图标
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(253, 232, 229));
                    IconText.Text = "✕";
                    IconText.FontSize = 15;
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(196, 91, 74));
                    break;
                case MsgType.Confirm:
                    // 温暖色确认图标
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(254, 240, 234));
                    IconText.Text = "?";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 87));
                    break;
            }
        }

        private void CreateButtons(MsgType type)
        {
            ButtonPanel.Children.Clear();

            if (type == MsgType.Confirm)
            {
                // 确认对话框：取消 + 确定
                var cancelBtn = new Button
                {
                    Content = "取消",
                    Style = (Style)FindResource("MsgBtnSecondary"),
                    Margin = new Thickness(0, 0, 10, 0),
                    MinWidth = 80
                };
                cancelBtn.Click += (s, e) =>
                {
                    IsConfirmed = false;
                    Close();
                };

                var confirmBtn = new Button
                {
                    Content = "确定",
                    Style = (Style)FindResource("MsgBtnDanger"),
                    MinWidth = 80
                };
                confirmBtn.Click += (s, e) =>
                {
                    IsConfirmed = true;
                    Close();
                };

                ButtonPanel.Children.Add(cancelBtn);
                ButtonPanel.Children.Add(confirmBtn);
            }
            else
            {
                // 其余类型：只有一个知道了按钮
                var okBtn = new Button
                {
                    Content = "知道了",
                    Style = (Style)FindResource("MsgBtnPrimary"),
                    MinWidth = 90
                };
                okBtn.Click += (s, e) =>
                {
                    IsConfirmed = true;
                    Close();
                };

                ButtonPanel.Children.Add(okBtn);
            }
        }

        // ========== 静态便捷方法 ==========

        /// <summary>
        /// 显示信息提示
        /// </summary>
        public static void ShowInfo(string title, string message, Window? owner = null)
        {
            var dlg = new AnthropicMessageDialog(title, message, MsgType.Info);
            if (owner != null) dlg.Owner = owner;
            dlg.ShowDialog();
        }

        /// <summary>
        /// 显示警告提示
        /// </summary>
        public static void ShowWarning(string title, string message, Window? owner = null)
        {
            var dlg = new AnthropicMessageDialog(title, message, MsgType.Warning);
            if (owner != null) dlg.Owner = owner;
            dlg.ShowDialog();
        }

        /// <summary>
        /// 显示错误提示
        /// </summary>
        public static void ShowError(string title, string message, Window? owner = null)
        {
            var dlg = new AnthropicMessageDialog(title, message, MsgType.Error);
            if (owner != null) dlg.Owner = owner;
            dlg.ShowDialog();
        }

        /// <summary>
        /// 显示确认对话框，返回用户是否确认
        /// </summary>
        public static bool ShowConfirm(string title, string message, Window? owner = null)
        {
            var dlg = new AnthropicMessageDialog(title, message, MsgType.Confirm);
            if (owner != null) dlg.Owner = owner;
            dlg.ShowDialog();
            return dlg.IsConfirmed;
        }
    }
}
