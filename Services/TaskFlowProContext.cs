using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenCvSharp;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Services
{
    /// <summary>
    /// 自定义脚本沙箱的桥接上下文对象
    /// 用户在脚本中通过 TaskFlowPro.GetTool() 等方法与主流程交互
    /// </summary>
    public class TaskFlowProContext : IDisposable
    {
        private readonly IList<TaskCardBase> _allTasks;
        private readonly VariableStore _variableStore;
        private readonly CustomScriptTaskCard _selfCard;
        private readonly StringBuilder _logBuilder = new();
        /// <summary>追踪所有分发给用户脚本的可释放对象，脚本结束后统一清理</summary>
        private readonly List<IDisposable> _trackedDisposables = new();

        public TaskFlowProContext(IList<TaskCardBase> allTasks, VariableStore variableStore, CustomScriptTaskCard selfCard)
        {
            _allTasks = allTasks;
            _variableStore = variableStore;
            _selfCard = selfCard;
        }

        // ============ 4 个核心方法 ============

        /// <summary>
        /// 获取任务卡片的输出值
        /// 用法: TaskFlowPro.GetTool("#1 图像裁剪.输出文本")
        /// </summary>
        public object? GetTool(string reference)
        {
            var (task, property) = ParseTaskReference(reference);

            return property switch
            {
                "输出文本" or "文本" => task.OutputText ?? "",
                "X" or "x" => task.OutputX ?? 0,
                "Y" or "y" => task.OutputY ?? 0,
                "执行结果" => task.OutputResult ?? (task.Status == TaskFlow.Models.TaskCards.TaskStatus.Success),
                "循环索引" => task is ForLoopTaskCard forCard ? forCard.CurrentLoopIndex : (task.OutputLoopIndex ?? 0),
                "匹配率" or "当前匹配阈值" => task switch
                {
                    ImgColorDetectTaskCard colorCard => colorCard.OutputMatchRatio,
                    ImgTemplateMatchTaskCard tmCard => tmCard.OutputMatchScore,
                    _ => 0.0
                },
                "转换结果" or "整数值" => task is TypeConvertTaskCard tcCard ? tcCard.OutputIntValue
                    : task is ArrayParseTaskCard apCard ? apCard.OutputIntValue : 0,
                "当前时间" or "时间戳" => task is GetTimestampTaskCard tsCard ? tsCard.OutputTimestamp : 0L,
                "匹配数量" => task is ImgTemplateMatchTaskCard tmCard2 ? tmCard2.OutputMatchCount : 0,
                "Blob数量" => task is ImgBlobAnalysisTaskCard blobCard ? blobCard.OutputBlobCount : 0,
                "最佳匹配分数" => task is ImgTemplateMatchTaskCard tmCard3 
                    ? (tmCard3.OutputMatchResults.Count > 0 ? tmCard3.OutputMatchResults.OrderByDescending(m => m.Score).First().Score : 0.0) : 0.0,
                "宽度缩放倍率" => task is ImgResizeTaskCard resizeCard ? resizeCard.OutputWidthScale : 0.0,
                "高度缩放倍率" => task is ImgResizeTaskCard resizeCard2 ? resizeCard2.OutputHeightScale : 0.0,
                "图像分辨率" => task is WinScreenshotTaskCard ssCard ? ssCard.OutputResolution : "",
                "宽度分辨率" => task is WinScreenshotTaskCard ssCard2 ? ssCard2.OutputWidth : 0,
                "高度分辨率" => task is WinScreenshotTaskCard ssCard3 ? ssCard3.OutputHeight : 0,
                "数组当前容量" => task is ArrayBuilderTaskCard abCard ? abCard.OutputArrayCount : 0,
                "保存文件路径" => task is ArrayBuilderTaskCard abCard2 ? abCard2.OutputSavePath ?? "" : "",
                "已翻译文件路径" => task is LlmFileTranslateTaskCard ftCard ? ftCard.OutputTranslatedFilePath ?? "" : "",
                "数组元素数量" => task is FileReadTaskCard frCard ? frCard.OutputArrayCount : 0,
                "匹配索引" => task is ArraySearchTaskCard asCard ? asCard.OutputMatchIndex : -1,
                "匹配值" => task is ArraySearchTaskCard asCard2 ? asCard2.OutputMatchValue ?? "" : "",
                "查找路径" or "outputFilePath" or "filePath" => task is WinFindFileTaskCard ffCard ? ffCard.OutputFilePath ?? "" : "",
                "测量边距" => task is ImgCaliperMeasureTaskCard caliperCard ? caliperCard.OutputDistance : 0.0,
                "解析结果" => task is ArrayParseTaskCard apCard2 ? apCard2.ArrayDataType switch
                {
                    ArrayDataType.Int => (object)apCard2.OutputIntValue,
                    ArrayDataType.String => apCard2.OutputStringValue ?? "",
                    ArrayDataType.Coordinate => apCard2.OutputX ?? 0,
                    ArrayDataType.Double => apCard2.OutputDoubleValue,
                    _ => 0
                } : 0,
                _ => throw new InvalidOperationException($"不支持的输出属性: {property}")
            };
        }

        /// <summary>
        /// 给任务卡片的可写属性赋值
        /// 用法: TaskFlowPro.SetTool("#1 表达式.表达式", "新表达式")
        /// </summary>
        public void SetTool(string reference, object value)
        {
            var (task, property) = ParseTaskReference(reference);
            string strVal = value?.ToString() ?? "";

            // 通过反射设置配置属性（ObservableProperty 生成的公共属性）
            var propInfo = task.GetType().GetProperty(property, 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (propInfo != null && propInfo.CanWrite)
            {
                if (propInfo.PropertyType.IsEnum)
                {
                    try
                    {
                        var enumVal = Enum.Parse(propInfo.PropertyType, strVal, true);
                        propInfo.SetValue(task, enumVal);
                        return;
                    }
                    catch { /* Enum 解析失败 */ }
                }
                else
                {
                    try
                    {
                        var converted = Convert.ChangeType(value, propInfo.PropertyType);
                        propInfo.SetValue(task, converted);
                        return;
                    }
                    catch { /* 如果类型转换失败，尝试字符串赋值 */ }
                }

                if (propInfo.PropertyType == typeof(string))
                {
                    propInfo.SetValue(task, strVal);
                    return;
                }
            }

            throw new InvalidOperationException($"无法设置任务卡片 #{task.Order} 的属性 \"{property}\"");
        }

        /// <summary>
        /// 获取全局变量值
        /// 用法: TaskFlowPro.GetVar("@变量名")
        /// </summary>
        public object? GetVar(string reference)
        {
            string varName = ParseVarName(reference);
            var variable = _variableStore.Variables.FirstOrDefault(v => v.Name == varName);
            if (variable == null)
                throw new InvalidOperationException($"变量 @{varName} 不存在");

            return variable.Type switch
            {
                VariableType.Int => variable.GetIntValue(),
                VariableType.Double => variable.GetDoubleValue(),
                VariableType.Bool => variable.GetBoolValue(),
                VariableType.String => variable.GetStringValue(),
                _ => variable.Value
            };
        }

        /// <summary>
        /// 设置全局变量值
        /// 用法: TaskFlowPro.SetVar("@变量名", 100)
        /// </summary>
        public void SetVar(string reference, object value)
        {
            string varName = ParseVarName(reference);
            string strVal = value?.ToString() ?? "";
            if (!_variableStore.SetValue(varName, strVal))
                throw new InvalidOperationException($"变量 @{varName} 不存在，请先在变量管理器中添加");
        }

        // ============ 图像专用方法 ============

        /// <summary>
        /// 获取任务卡片的输出图像（返回 Clone 副本，用户需自行管理生命周期）
        /// 用法: var img = TaskFlowPro.GetImage("#1 图像裁剪")
        /// </summary>
        public Mat? GetImage(string reference)
        {
            // 解析 "#N 名称" 格式（不需要属性后缀）
            var match = Regex.Match(reference.Trim(), @"#(\d+)\s+(.+)");
            if (!match.Success)
                throw new InvalidOperationException($"图像引用格式错误: \"{reference}\"，正确格式: \"#1 任务名\"");

            int order = int.Parse(match.Groups[1].Value);
            var task = _allTasks.FirstOrDefault(t => t.Order == order);
            if (task == null)
                throw new InvalidOperationException($"找不到序号为 {order} 的任务卡片");

            // 返回克隆副本，避免用户 Dispose 影响原始数据
            var clone = task.OutputImage?.Clone();
            if (clone != null) _trackedDisposables.Add(clone);
            return clone;
        }

        /// <summary>
        /// 设置自身卡片的输出图像（内部会 Clone，用户可安全 Dispose 原始对象）
        /// </summary>
        public void SetOutputImage(Mat image)
        {
            _selfCard.OutputImage?.Dispose();
            _selfCard.OutputImage = image?.Clone();
        }

        /// <summary>设置自身卡片的输出文本</summary>
        public void SetOutputText(string text)
        {
            _selfCard.OutputText = text;
        }

        /// <summary>设置自身卡片的输出坐标</summary>
        public void SetOutputXY(int x, int y)
        {
            _selfCard.OutputX = x;
            _selfCard.OutputY = y;
        }

        /// <summary>设置自身卡片的执行结果</summary>
        public void SetOutputResult(bool result)
        {
            _selfCard.OutputResult = result;
        }

        // ============ 日志方法 ============

        /// <summary>向输出面板打印日志</summary>
        public void Log(string message)
        {
            _logBuilder.AppendLine(message);
        }

        /// <summary>获取累积的日志文本</summary>
        internal string GetLog() => _logBuilder.ToString();

        // ============ 资源清理 ============

        /// <summary>
        /// 释放所有分发给用户脚本的可释放对象。
        /// 在脚本执行完成后由引擎自动调用，用户无需手动管理。
        /// </summary>
        public void Dispose()
        {
            foreach (var obj in _trackedDisposables)
            {
                try { obj.Dispose(); } catch { /* 忽略单个对象释放时的异常 */ }
            }
            _trackedDisposables.Clear();
        }

        // ============ 内部解析辅助 ============

        /// <summary>
        /// 解析 "#N 名称.属性" 格式的任务引用
        /// </summary>
        private (TaskCardBase task, string property) ParseTaskReference(string reference)
        {
            var match = Regex.Match(reference.Trim(), @"#(\d+)\s+([^.]+)\.(.+)");
            if (!match.Success)
                throw new InvalidOperationException(
                    $"任务引用格式错误: \"{reference}\"，正确格式: \"#1 任务名.属性名\"");

            int order = int.Parse(match.Groups[1].Value);
            string property = match.Groups[3].Value.Trim();

            var task = _allTasks.FirstOrDefault(t => t.Order == order);
            if (task == null)
                throw new InvalidOperationException($"找不到序号为 {order} 的任务卡片");

            return (task, property);
        }

        /// <summary>
        /// 解析 "@变量名" 格式的变量引用
        /// </summary>
        private static string ParseVarName(string reference)
        {
            var match = Regex.Match(reference.Trim(), @"^@([\w\u4e00-\u9fff]+)$");
            if (!match.Success)
                throw new InvalidOperationException(
                    $"变量引用格式错误: \"{reference}\"，正确格式: \"@变量名\"");
            return match.Groups[1].Value;
        }
    }

    /// <summary>
    /// Roslyn 脚本全局变量容器，用户代码中可直接访问 TaskFlowPro
    /// </summary>
    public class ScriptGlobals
    {
        public TaskFlowProContext TaskFlowPro { get; set; } = null!;
    }
}
