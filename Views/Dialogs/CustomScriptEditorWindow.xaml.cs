using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;
using TaskFlow.Models.TaskCards;
using TaskFlow.Services;

namespace TaskFlow.Views.Dialogs
{
    /// <summary>
    /// 自定义脚本编辑器窗口
    /// 四层布局：工具栏 / 代码编辑器(AvalonEdit) / 分隔条 / 输出面板
    /// </summary>
    public partial class CustomScriptEditorWindow : Window
    {
        private readonly CustomScriptTaskCard _card;
        private readonly ITaskExecutionService? _executionService;
        private readonly System.Collections.Generic.IList<TaskCardBase>? _allTasks;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="card">绑定的自定义脚本卡片</param>
        /// <param name="executionService">执行服务（用于运行按钮）</param>
        /// <param name="allTasks">当前流程的所有任务卡片（用于运行按钮）</param>
        public CustomScriptEditorWindow(
            CustomScriptTaskCard card,
            ITaskExecutionService? executionService = null,
            System.Collections.Generic.IList<TaskCardBase>? allTasks = null)
        {
            _card = card;
            _executionService = executionService;
            _allTasks = allTasks;

            InitializeComponent();

            // 加载暗色主题高亮配置
            LoadDarkThemeHighlighting();

            // 将卡片中的脚本代码加载到编辑器
            CodeEditor.Text = _card.ScriptCode ?? "";

            // 窗口关闭时自动保存代码回卡片
            Closing += (s, e) =>
            {
                _card.ScriptCode = CodeEditor.Text;
            };
        }

        /// <summary>加载 AvalonEdit C# 暗黑主题</summary>
        private void LoadDarkThemeHighlighting()
        {
            try
            {
                // 获取编译进程序集的 xshd 资源
                using var stream = typeof(CustomScriptEditorWindow).Assembly.GetManifestResourceStream("TaskFlow.Resources.AvalonEdit_CSharpDark.xshd");
                if (stream != null)
                {
                    using var reader = new XmlTextReader(stream);
                    CodeEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }
            catch
            {
                // 加载失败时静默反馈，保留 XAML 默认配置
            }
        }

        /// <summary>保存：将编辑器文本写回卡片</summary>
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _card.ScriptCode = CodeEditor.Text;
            OutputPanel.Text = "[保存成功] 代码已保存到卡片。";
        }

        /// <summary>另存为：导出为 .cs 文件</summary>
        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "C# 脚本 (*.cs)|*.cs|所有文件 (*.*)|*.*",
                DefaultExt = ".cs",
                FileName = $"{_card.Name}.cs"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    System.IO.File.WriteAllText(dialog.FileName, CodeEditor.Text);
                    OutputPanel.Text = $"[导出成功] {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    OutputPanel.Text = $"[导出失败] {ex.Message}";
                }
            }
        }

        /// <summary>读取：从 .cs 文件导入代码</summary>
        private void Load_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "C# 脚本 (*.cs)|*.cs|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                DefaultExt = ".cs"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    CodeEditor.Text = System.IO.File.ReadAllText(dialog.FileName);
                    OutputPanel.Text = $"[读取成功] {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    OutputPanel.Text = $"[读取失败] {ex.Message}";
                }
            }
        }

        /// <summary>注释按钮</summary>
        private void Comment_Click(object sender, RoutedEventArgs e)
        {
            ToggleComment(true);
        }

        /// <summary>取消注释按钮</summary>
        private void Uncomment_Click(object sender, RoutedEventArgs e)
        {
            ToggleComment(false);
        }

        /// <summary>帮助按钮</summary>
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var helpPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "docs",
                    System.Threading.Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh") ? "help_zh.html" : "help_en.html");

                if (!System.IO.File.Exists(helpPath))
                {
                    MessageBox.Show("未找到帮助文档文件。", "帮助", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var targetUrl = new Uri(helpPath).AbsoluteUri + "#CustomScript";
                var redirectHtml = $"<!DOCTYPE html><html><head><meta charset=\"UTF-8\"><script>window.location.replace(\"{targetUrl}\");</script></head><body></body></html>";
                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "taskflow_customscript_help.html");
                System.IO.File.WriteAllText(tempPath, redirectHtml);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tempPath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开帮助文档：{ex.Message}", "帮助", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>处理快捷键</summary>
        private void CodeEditor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.OemQuestion && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                // Ctrl + / 切换注释
                ToggleComment(null); 
                e.Handled = true;
            }
        }

        /// <summary>
        /// 注释/取消注释选定行
        /// </summary>
        /// <param name="forceComment">true为注释，false为取消，null根据块的第一行智能切换</param>
        private void ToggleComment(bool? forceComment)
        {
            var document = CodeEditor.Document;
            if (document == null) return;

            int startLineNumber = document.GetLineByOffset(CodeEditor.SelectionStart).LineNumber;
            int endLocation = CodeEditor.SelectionStart + CodeEditor.SelectionLength;
            int endLineNumber = document.GetLineByOffset(endLocation).LineNumber;

            // 避免空选中下一行首被包含（如果光标恰好在下一行开始位置）
            if (CodeEditor.SelectionLength > 0 && endLocation == document.GetLineByNumber(endLineNumber).Offset && endLineNumber > startLineNumber)
            {
                endLineNumber--;
            }

            // 判断是否需要注释
            bool shouldComment = forceComment ?? true;
            if (forceComment == null)
            {
                // 根据第一行状态决定是全块注释还是全块取消
                var firstLine = document.GetLineByNumber(startLineNumber);
                string firstLineText = document.GetText(firstLine.Offset, firstLine.Length).TrimStart();
                shouldComment = !firstLineText.StartsWith("//");
            }

            using (document.RunUpdate())
            {
                for (int i = startLineNumber; i <= endLineNumber; i++)
                {
                    var line = document.GetLineByNumber(i);
                    string text = document.GetText(line.Offset, line.Length);
                    string trimmedText = text.TrimStart();
                    int leadingSpaces = text.Length - trimmedText.Length;

                    if (shouldComment)
                    {
                        if (!trimmedText.StartsWith("//"))
                        {
                            document.Insert(line.Offset + leadingSpaces, "// ");
                        }
                    }
                    else // 取消注释
                    {
                        if (trimmedText.StartsWith("//"))
                        {
                            int removeLen = trimmedText.StartsWith("// ") ? 3 : 2;
                            document.Remove(line.Offset + leadingSpaces, removeLen);
                        }
                    }
                }
            }
        }

        /// <summary>运行：单独执行此脚本卡片</summary>
        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_executionService == null || _allTasks == null)
            {
                OutputPanel.Text = "[错误] 执行服务未就绪，请通过主流程运行。";
                return;
            }

            // 先保存当前编辑器内容到卡片
            _card.ScriptCode = CodeEditor.Text;
            OutputPanel.Text = "[运行中...]\n";

            try
            {
                using var cts = new CancellationTokenSource();
                await _executionService.ExecuteTaskAsync(_card, _allTasks, cts.Token);

                // 合并输出日志和状态
                var output = new System.Text.StringBuilder();

                if (!string.IsNullOrWhiteSpace(_card.OutputLog))
                    output.AppendLine(_card.OutputLog);

                if (_card.Status == TaskFlow.Models.TaskCards.TaskStatus.Success)
                {
                    output.AppendLine("[执行成功]");
                    if (!string.IsNullOrEmpty(_card.OutputText))
                        output.AppendLine($"  输出文本: {_card.OutputText}");
                    if (_card.OutputX.HasValue || _card.OutputY.HasValue)
                        output.AppendLine($"  输出坐标: ({_card.OutputX ?? 0}, {_card.OutputY ?? 0})");
                    if (_card.OutputResult.HasValue)
                        output.AppendLine($"  执行结果: {_card.OutputResult.Value}");
                    if (_card.OutputImage != null)
                        output.AppendLine($"  输出图像: {_card.OutputImage.Width}x{_card.OutputImage.Height}");
                }
                else
                {
                    output.AppendLine($"[执行失败] {_card.ErrorMessage}");
                }

                OutputPanel.Text = output.ToString();
            }
            catch (Exception ex)
            {
                OutputPanel.Text = $"[异常] {ex.Message}";
            }
        }
    }
}
