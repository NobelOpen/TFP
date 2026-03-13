using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TaskFlow.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TaskFlow.Models;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Helpers
{
    /// <summary>
    /// 表达式输入框自动补全辅助类
    /// 支持 @变量名 和 #任务编号/名称 的模糊匹配与自动补全
    /// </summary>
    public class AutoCompleteHelper
    {
        private readonly TextBox _textBox;
        private readonly Popup _popup;
        private readonly ListBox _listBox;
        private readonly ObservableCollection<Variable> _variables;
        private readonly ObservableCollection<TaskCardBase> _taskCards;

        // 当前触发符的位置和类型
        private int _triggerIndex = -1;
        private char _triggerChar;

        /// <summary>
        /// 为指定 TextBox 附加自动补全功能
        /// </summary>
        public static AutoCompleteHelper Attach(
            TextBox textBox,
            ObservableCollection<Variable> variables,
            ObservableCollection<TaskCardBase> taskCards)
        {
            return new AutoCompleteHelper(textBox, variables, taskCards);
        }

        private AutoCompleteHelper(
            TextBox textBox,
            ObservableCollection<Variable> variables,
            ObservableCollection<TaskCardBase> taskCards)
        {
            _textBox = textBox;
            _variables = variables;
            _taskCards = taskCards;

            // 创建建议列表 ListBox
            _listBox = new ListBox
            {
                MaxHeight = 200,
                MinWidth = 220,
                Background = new SolidColorBrush(Color.FromRgb(250, 249, 245)),     // #faf9f5
                Foreground = new SolidColorBrush(Color.FromRgb(20, 20, 19)),         // #141413
                BorderBrush = new SolidColorBrush(Color.FromRgb(232, 230, 220)),     // #e8e6dc
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                FontSize = 13
            };

            // 建议项样式
            _listBox.ItemContainerStyle = CreateItemStyle();

            // 创建 Popup
            _popup = new Popup
            {
                PlacementTarget = _textBox,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(250, 249, 245)),
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(216, 214, 206)),
                    BorderThickness = new Thickness(1),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 12,
                        ShadowDepth = 3,
                        Opacity = 0.15,
                        Color = Color.FromRgb(26, 26, 25)
                    },
                    Child = _listBox
                }
            };

            // 绑定事件
            _textBox.TextChanged += OnTextChanged;
            _textBox.PreviewKeyDown += OnPreviewKeyDown;
            _textBox.LostFocus += OnLostFocus;
            _listBox.PreviewMouseLeftButtonUp += OnListBoxItemClicked;
        }

        /// <summary>
        /// 文本变化时检测触发符并更新建议列表
        /// </summary>
        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var text = _textBox.Text;
            var caretIndex = _textBox.CaretIndex;

            if (string.IsNullOrEmpty(text) || caretIndex <= 0)
            {
                HidePopup();
                return;
            }

            // 从光标位置向前查找最近的 @ 或 # 触发符
            int triggerPos = -1;
            char triggerChar = '\0';

            for (int i = caretIndex - 1; i >= 0; i--)
            {
                char c = text[i];

                // 遇到空白、分号、等号、换行则停止搜索
                if (c == ' ' || c == '\t' || c == ';' || c == '=' || c == '\n' || c == '\r')
                    break;

                if (c == '@' || c == '#')
                {
                    triggerPos = i;
                    triggerChar = c;
                    break;
                }
            }

            if (triggerPos < 0)
            {
                HidePopup();
                return;
            }

            _triggerIndex = triggerPos;
            _triggerChar = triggerChar;

            // 提取搜索关键词（触发符之后到光标之间的文本）
            string keyword = text.Substring(triggerPos + 1, caretIndex - triggerPos - 1);

            // 根据触发符类型获取匹配列表
            var suggestions = triggerChar == '@'
                ? GetVariableSuggestions(keyword)
                : GetTaskSuggestions(keyword);

            if (suggestions.Count > 0)
            {
                _listBox.Items.Clear();
                foreach (var item in suggestions)
                {
                    _listBox.Items.Add(item);
                }
                _listBox.SelectedIndex = 0;
                _popup.IsOpen = true;
            }
            else
            {
                HidePopup();
            }
        }

        /// <summary>
        /// 获取匹配的变量建议
        /// </summary>
        private List<SuggestionItem> GetVariableSuggestions(string keyword)
        {
            return _variables
                .Where(v => string.IsNullOrEmpty(keyword) ||
                            v.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Select(v => new SuggestionItem
                {
                    DisplayText = $"@{v.Name}",
                    DetailText = v.Type.ToString(),
                    InsertText = $"@{v.Name}"
                })
                .Take(15)
                .ToList();
        }

        /// <summary>
        /// 获取匹配的任务建议（按输出类型展开）
        /// </summary>
        private List<SuggestionItem> GetTaskSuggestions(string keyword)
        {
            var suggestions = new List<SuggestionItem>();

            var filteredTasks = _taskCards
                .Where(t => t.BranchRole == BranchRole.None ||
                            t.BranchRole == BranchRole.IfStart ||
                            t.BranchRole == BranchRole.ForLoopStart);

            foreach (var t in filteredTasks)
            {
                // 获取该任务的所有可引用输出
                var outputs = GetTaskOutputs(t);
                if (outputs.Count == 0) continue;

                foreach (var output in outputs)
                {
                    string displayText = $"#{t.Order} {t.Name}.{output.Label}";
                    string insertText = $"#{t.Order} {t.Name}.{output.Suffix}";

                    // 用关键词匹配：编号、名称、输出类型
                    if (!string.IsNullOrEmpty(keyword))
                    {
                        string orderStr = t.Order.ToString();
                        bool match = orderStr.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                                     t.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                                     output.Label.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                        if (!match) continue;
                    }

                    suggestions.Add(new SuggestionItem
                    {
                        DisplayText = displayText,
                        DetailText = t.TaskTypeName,
                        InsertText = insertText
                    });
                }
            }

            return suggestions.Take(20).ToList();
        }

        /// <summary>
        /// 获取任务卡片的可引用输出列表
        /// </summary>
        private static List<TaskOutput> GetTaskOutputs(TaskCardBase task)
        {
            var outputs = new List<TaskOutput>();

            if (task.OutputsText)
                outputs.Add(new TaskOutput("文本", "文本"));

            if (task.OutputsCoordinates)
            {
                outputs.Add(new TaskOutput(Strings.AC_XCoord, "X"));
                outputs.Add(new TaskOutput(Strings.AC_YCoord, "Y"));
            }

            // 除循环和分支任务外，所有任务都有执行结果
            if (task.BranchRole == BranchRole.None)
                outputs.Add(new TaskOutput(Strings.AC_ExecResult, Strings.AC_ExecResult));

            // ForLoop 输出循环索引
            if (task.BranchRole == BranchRole.ForLoopStart)
                outputs.Add(new TaskOutput("循环索引", "循环索引"));

            // 时间戳任务输出当前时间
            if (task.TaskType == TaskType.GetTimestamp)
                outputs.Add(new TaskOutput(Strings.AC_CurrentTime, Strings.AC_CurrentTime));

            // 字符串截取输出文本
            if (task.TaskType == TaskType.StringSubstring)
                outputs.Add(new TaskOutput("文本", "文本"));

            // 类型转换输出文本
            if (task.TaskType == TaskType.TypeConvert)
                outputs.Add(new TaskOutput("文本", "文本"));

            // 数组解析输出文本
            if (task.TaskType == TaskType.ArrayParse)
                outputs.Add(new TaskOutput("文本", "文本"));

            // Blob分析输出Blob数量
            if (task.TaskType == TaskType.ImgBlobAnalysis)
                outputs.Add(new TaskOutput(Strings.AC_BlobCount, Strings.AC_BlobCount));

            // 图像缩放输出缩放倍率
            if (task.TaskType == TaskType.ImgResize)
                outputs.Add(new TaskOutput("缩放倍率", "缩放倍率"));

            return outputs;
        }

        /// <summary>
        /// 任务输出描述
        /// </summary>
        private record TaskOutput(string Label, string Suffix);

        /// <summary>
        /// 键盘事件：↑↓导航、Enter/Tab选中、Escape关闭
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_popup.IsOpen) return;

            switch (e.Key)
            {
                case Key.Down:
                    if (_listBox.SelectedIndex < _listBox.Items.Count - 1)
                        _listBox.SelectedIndex++;
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (_listBox.SelectedIndex > 0)
                        _listBox.SelectedIndex--;
                    e.Handled = true;
                    break;

                case Key.Enter:
                case Key.Tab:
                    ApplySelection();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    HidePopup();
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// 鼠标点击建议项
        /// </summary>
        private void OnListBoxItemClicked(object sender, MouseButtonEventArgs e)
        {
            ApplySelection();
        }

        /// <summary>
        /// 失去焦点时关闭弹窗
        /// </summary>
        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            // 延迟关闭，允许点击列表项
            _textBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_listBox.IsMouseOver)
                    HidePopup();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// 应用选中的建议，替换触发符到光标间的文本
        /// </summary>
        private void ApplySelection()
        {
            if (_listBox.SelectedItem is not SuggestionItem item) return;
            if (_triggerIndex < 0) return;

            var text = _textBox.Text;
            var caretIndex = _textBox.CaretIndex;

            // 替换从触发符位置到当前光标位置的文本
            string before = text.Substring(0, _triggerIndex);
            string after = caretIndex < text.Length ? text.Substring(caretIndex) : "";
            string newText = before + item.InsertText + after;

            _textBox.Text = newText;
            _textBox.CaretIndex = _triggerIndex + item.InsertText.Length;

            HidePopup();
            _textBox.Focus();
        }

        private void HidePopup()
        {
            _popup.IsOpen = false;
            _triggerIndex = -1;
        }

        /// <summary>
        /// 创建建议项的样式
        /// </summary>
        private Style CreateItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 5, 8, 5)));
            style.Setters.Add(new Setter(ListBoxItem.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(ListBoxItem.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(20, 20, 19))));
            style.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));

            // 选中状态
            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(245, 230, 222))));  // #f5e6de
            selectedTrigger.Setters.Add(new Setter(ListBoxItem.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(217, 119, 87))));   // #d97757
            style.Triggers.Add(selectedTrigger);

            // 悬停状态
            var hoverTrigger = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(240, 239, 232))));  // #f0efe8
            style.Triggers.Add(hoverTrigger);

            // 使用 ItemTemplate 展示 DisplayText + DetailText
            style.Setters.Add(new Setter(ListBoxItem.TemplateProperty, CreateItemTemplate()));

            return style;
        }

        /// <summary>
        /// 创建建议项的自定义模板
        /// </summary>
        private ControlTemplate CreateItemTemplate()
        {
            var template = new ControlTemplate(typeof(ListBoxItem));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFactory.SetBinding(Border.PaddingProperty,
                new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            borderFactory.AppendChild(contentPresenter);

            template.VisualTree = borderFactory;
            return template;
        }

        /// <summary>
        /// 建议项数据
        /// </summary>
        public class SuggestionItem
        {
            public string DisplayText { get; set; } = "";
            public string DetailText { get; set; } = "";
            public string InsertText { get; set; } = "";

            public override string ToString()
            {
                return string.IsNullOrEmpty(DetailText)
                    ? DisplayText
                    : $"{DisplayText}  ({DetailText})";
            }
        }
    }
}
