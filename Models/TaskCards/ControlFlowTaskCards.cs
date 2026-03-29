using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using TaskFlow.Models.AiFlow;

namespace TaskFlow.Models.TaskCards
{
    /// <summary>
    /// If-Else分支任务卡片
    /// 由3+N个卡片组成：IfStart, [ElifStart...], ElseStart, ElseEnd(分支结束)
    /// </summary>
    public partial class IfElseBranchTaskCard : TaskCardBase
    {
        private readonly TaskType _taskType;

        public override TaskType TaskType => _taskType;

        /// <summary>
        /// 条件表达式，支持表达式判断语法
        /// 示例: "true", "1==1", "#1 循环开始.循环索引>1"
        /// </summary>
        [ObservableProperty]
        private string _conditionExpression = "true";

        [JsonIgnore]
        [ObservableProperty]
        private bool? _conditionResult;

        #region JSON向后兼容（旧数据迁移）

        /// <summary>
        /// 旧属性：条件来源（仅用于反序列化旧数据）
        /// </summary>
        [JsonProperty("ConditionSource")]
        private ConditionSource? _legacyConditionSource
        {
            set
            {
                // 仅在反序列化时触发迁移逻辑
                if (value == ConditionSource.ManualInput)
                {
                    // ManualConditionValue 在后续属性中处理
                }
            }
        }

        /// <summary>
        /// 旧属性：手动条件值（仅用于反序列化旧数据）
        /// </summary>
        [JsonProperty("ManualConditionValue")]
        private bool? _legacyManualConditionValue
        {
            set
            {
                if (value.HasValue)
                {
                    // 如果是旧数据且 ConditionExpression 还是默认值，则迁移
                    ConditionExpression = value.Value ? "true" : "false";
                }
            }
        }

        /// <summary>
        /// 旧属性：引用任务ID（仅用于反序列化旧数据，忽略）
        /// </summary>
        [JsonProperty("SourceTaskId")]
        private Guid? _legacySourceTaskId { set { /* 忽略，无法在模型层迁移 */ } }

        #endregion

        /// <summary>
        /// 是否隐藏Else分支（仅对IfStart有意义）
        /// </summary>
        [ObservableProperty]
        private bool _isElseHidden = true;

        public IfElseBranchTaskCard(BranchRole role)
        {
            BranchRole = role;
            _taskType = role switch
            {
                BranchRole.IfStart => TaskType.IfStart,
                BranchRole.IfEnd => TaskType.IfEnd,
                BranchRole.ElifStart => TaskType.ElifStart,
                BranchRole.ElseStart => TaskType.ElseStart,
                BranchRole.ElseEnd => TaskType.ElseEnd,
                _ => throw new ArgumentException($"Invalid branch role for IfElse: {role}")
            };

            Name = TaskCardBase.GetTaskTypeName(_taskType);
        }

        /// <summary>
        /// If-Else分支不能被其他任务引用结果
        /// </summary>
        public override bool CanBeReferenced => false;

        public override void Reset()
        {
            base.Reset();
            ConditionResult = null;
        }
    }

    /// <summary>
    /// For循环任务卡片
    /// 由2个卡片组成：ForLoopStart, ForLoopEnd
    /// </summary>
    public partial class ForLoopTaskCard : TaskCardBase
    {
        private readonly TaskType _taskType;

        public override TaskType TaskType => _taskType;

        [ObservableProperty]
        private int _loopCount = 0;

        /// <summary>
        /// 循环次数表达式（支持引用变量如 @次数，或任务引用如 #N 名称.转换结果）
        /// 为空时使用 LoopCount
        /// </summary>
        [ObservableProperty]
        private string _loopCountExpression = string.Empty;

        /// <summary>
        /// 是否使用表达式循环次数
        /// </summary>
        [ObservableProperty]
        private bool _useExpressionLoopCount;

        [JsonIgnore]
        [ObservableProperty]
        private int _currentLoopIndex;

        public ForLoopTaskCard(BranchRole role)
        {
            BranchRole = role;
            _taskType = role switch
            {
                BranchRole.ForLoopStart => TaskType.ForLoopStart,
                BranchRole.ForLoopEnd => TaskType.ForLoopEnd,
                _ => throw new ArgumentException($"Invalid branch role for ForLoop: {role}")
            };

            Name = TaskCardBase.GetTaskTypeName(_taskType);
        }

        /// <summary>
        /// For循环不能被其他任务引用结果
        /// </summary>
        public override bool CanBeReferenced => false;

        public override void Reset()
        {
            base.Reset();
            CurrentLoopIndex = 0;
        }
    }

    /// <summary>
    /// 结束当前流程卡片
    /// </summary>
    public partial class EndTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.EndTask;

        public EndTaskCard()
        {
            Name = "结束当前流程";
        }

        public override bool CanBeReferenced => false;
    }

    /// <summary>
    /// 结束全部流程卡片，执行后立即停止全部流程执行
    /// </summary>
    public partial class EndAllFlowsTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.EndAllFlows;

        public EndAllFlowsTaskCard()
        {
            Name = "结束全部流程";
        }

        public override bool CanBeReferenced => false;
    }

    /// <summary>
    /// 重新开始当前流程卡片，执行后立即结束当前流程并从头开始重新执行
    /// </summary>
    public partial class RestartFlowTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.RestartFlow;

        public RestartFlowTaskCard()
        {
            Name = "重开当前流程";
        }

        public override bool CanBeReferenced => false;
    }

    /// <summary>
    /// 暂停任务卡片
    /// </summary>
    public partial class PauseTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.PauseTask;

        [ObservableProperty]
        private int _pauseDurationMs = 1000;

        /// <summary>
        /// 暂停时长表达式（支持引用变量，如 @延迟时间），为空时使用 PauseDurationMs
        /// </summary>
        [ObservableProperty]
        private string _pauseDurationExpression = string.Empty;

        public PauseTaskCard()
        {
            Name = "暂停全部任务";
        }

        public override bool CanBeReferenced => false;

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            if (step.Properties.TryGetValue("pauseMs", out var pauseMs) && int.TryParse(pauseMs, out var ms))
                PauseDurationMs = ms;
            return missing;
        }
    }

    /// <summary>
    /// 时间戳输出格式
    /// </summary>
    public enum TimestampFormat
    {
        /// <summary>时分秒 (HHmmss)</summary>
        HourMinuteSecond,
        /// <summary>日时分秒 (ddHHmmss)</summary>
        DayHourMinuteSecond,
        /// <summary>月日时分秒 (MMddHHmmss)</summary>
        MonthDayHourMinuteSecond,
        /// <summary>年月日时分秒 (yyyyMMddHHmmss)</summary>
        YearMonthDayHourMinuteSecond
    }

    /// <summary>
    /// 获取当前时间戳卡片
    /// </summary>
    public partial class GetTimestampTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.GetTimestamp;

        /// <summary>
        /// 时间戳输出格式
        /// </summary>
        [ObservableProperty]
        private TimestampFormat _timestampFormat = TimestampFormat.HourMinuteSecond;

        /// <summary>
        /// 输出的时间戳整数值
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private long _outputTimestamp;

        public GetTimestampTaskCard()
        {
            Name = "获取当前时间";
        }

        public override void Reset()
        {
            base.Reset();
            OutputTimestamp = 0;
        }
    }

    /// <summary>
    /// 表达式处理任务卡片 - 用于给变量动态赋值
    /// 支持多行赋值语句，以";"分隔，如 "@A = 1; @B = 2; @C = 3"
    /// </summary>
    public partial class ExpressionEvalTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ExpressionEval;

        /// <summary>
        /// 表达式字符串，支持多行赋值
        /// 格式: @变量名 = 表达式，多条语句以";"分隔
        /// 示例: "@A = 1; @B = @A + 1; @C = 3"
        /// </summary>
        [ObservableProperty]
        private string _expression = string.Empty;

        public ExpressionEvalTaskCard()
        {
            Name = "表达式赋值";
        }

    }

    /// <summary>
    /// 中止循环任务卡片 - 强制退出指定的循环结构
    /// </summary>
    public partial class BreakLoopTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.BreakLoop;

        /// <summary>
        /// 目标循环的 ForLoopStart 卡片 ID
        /// </summary>
        [ObservableProperty]
        private Guid? _targetLoopId;

        public BreakLoopTaskCard()
        {
            Name = "中止循环";
        }

        public override bool CanBeReferenced => false;
    }

    /// <summary>
    /// 起始位置模式
    /// </summary>
    public enum StartIndexMode
    {
        /// <summary>
        /// 手动指定起始位置
        /// </summary>
        Manual,

        /// <summary>
        /// 通过查找字符确定起始位置
        /// </summary>
        FindChar
    }

    /// <summary>
    /// 字符串截取任务卡片 - 从输入文本中提取子字符串
    /// 支持引用其他任务的输出文本，支持手动指定或按字符查找起始位置
    /// </summary>
    public partial class StringSubstringTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.StringSubstring;

        /// <summary>
        /// 文本来源任务ID
        /// </summary>
        [ObservableProperty]
        private Guid? _sourceTaskIdForText;

        /// <summary>
        /// 手动输入文本（不引用任务时使用）
        /// </summary>
        [ObservableProperty]
        private string _inputText = string.Empty;

        /// <summary>
        /// 起始位置模式：手动指定 / 查找字符
        /// </summary>
        [ObservableProperty]
        private StartIndexMode _startMode = StartIndexMode.Manual;

        /// <summary>
        /// 手动起始位置（0-based）
        /// </summary>
        [ObservableProperty]
        private int _manualStartIndex;

        /// <summary>
        /// 查找字符（用于确定起始位置）
        /// </summary>
        [ObservableProperty]
        private string _searchChar = string.Empty;

        /// <summary>
        /// 查找到字符后的偏移量
        /// </summary>
        [ObservableProperty]
        private int _searchCharOffset;

        /// <summary>
        /// 截取长度（-1表示截取到末尾）
        /// </summary>
        [ObservableProperty]
        private int _substringLength = -1;

        public StringSubstringTaskCard()
        {
            Name = "字符串截取";
        }

        /// <summary>
        /// 输出截取后的文本
        /// </summary>
        public override bool OutputsText => true;
    }

    /// <summary>
    /// 数据类型转换任务卡片 - 将string转换为int
    /// 支持引用其他任务的文本输出或string类型的变量
    /// </summary>
    public partial class TypeConvertTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.TypeConvert;

        /// <summary>
        /// 文本来源任务ID
        /// </summary>
        [ObservableProperty]
        private Guid? _sourceTaskIdForText;

        /// <summary>
        /// 手动输入文本（不引用任务时使用，支持变量引用如 @变量名）
        /// </summary>
        [ObservableProperty]
        private string _inputExpression = string.Empty;

        public TypeConvertTaskCard()
        {
            Name = "类型转换";
        }

        /// <summary>
        /// 转换后的整数值，方便其他任务卡片引用
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private int _outputIntValue;

        /// <summary>
        /// 输出转换后的文本（int值的字符串形式）
        /// </summary>
        public override bool OutputsText => true;

        public override void Reset()
        {
            base.Reset();
            OutputIntValue = 0;
        }
    }

    /// <summary>
    /// 数组变量类型
    /// </summary>
    public enum ArrayDataType
    {
        /// <summary>
        /// 整数值
        /// </summary>
        Int,

        /// <summary>
        /// 字符串
        /// </summary>
        String,

        /// <summary>
        /// X/Y坐标
        /// </summary>
        Coordinate,

        /// <summary>
        /// 浮点数
        /// </summary>
        Double
    }

    /// <summary>
    /// 数值解析任务卡片 - 根据索引从引用的数组中提取值
    /// </summary>
    public partial class ArrayParseTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ArrayParse;

        /// <summary>
        /// 解析的数组类型
        /// </summary>
        [ObservableProperty]
        private ArrayDataType _arrayDataType = ArrayDataType.Int;

        /// <summary>
        /// 数组引用表达式（如 #1 模板匹配.匹配坐标 或 #1 模板匹配.结果分数）- 旧属性，保留用于向后兼容
        /// </summary>
        [ObservableProperty]
        private string _sourceExpression = string.Empty;

        /// <summary>
        /// 数组来源任务ID（通过下拉框选择）
        /// </summary>
        [ObservableProperty]
        private Guid? _sourceTaskIdForArray;

        /// <summary>
        /// 当任务有多重数组输出时，指定的数组属性名（如"匹配坐标"、"结果分数"）
        /// </summary>
        [ObservableProperty]
        private string _sourcePropertyForArray = string.Empty;

        /// <summary>
        /// 输出索引（固定值，0-based）
        /// </summary>
        [ObservableProperty]
        private int _parseIndex;

        /// <summary>
        /// 索引表达式（支持变量 @变量名 和任务引用）
        /// </summary>
        [ObservableProperty]
        private string _parseIndexExpression = string.Empty;

        /// <summary>
        /// 是否使用表达式索引
        /// </summary>
        [ObservableProperty]
        private bool _useExpressionIndex;

        /// <summary>
        /// 输出的 int 值
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private int _outputIntValue;

        /// <summary>
        /// 输出的 string 值
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private string _outputStringValue = string.Empty;

        /// <summary>
        /// 输出的 double 值
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private double _outputDoubleValue;

        public ArrayParseTaskCard()
        {
            Name = "数组解析";
        }

        public override bool OutputsCoordinates => ArrayDataType == ArrayDataType.Coordinate;
        public override bool OutputsBoolResult => true;

        public override void Reset()
        {
            base.Reset();
            OutputIntValue = 0;
            OutputStringValue = string.Empty;
            OutputDoubleValue = 0;
        }
    }

    /// <summary>
    /// 数组生成任务卡片 - 在循环中收集数据，构建动态数组
    /// </summary>
    public partial class ArrayBuilderTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ArrayBuilder;

        /// <summary>
        /// 数组元素类型
        /// </summary>
        [ObservableProperty]
        private ArrayDataType _arrayDataType = ArrayDataType.String;

        /// <summary>
        /// 要追加的数据表达式（支持 #N / @变量引用）
        /// </summary>
        [ObservableProperty]
        private string _inputExpression = "";

        /// <summary>
        /// 插入索引表达式（-1 = 自动追加到末尾）
        /// </summary>
        [ObservableProperty]
        private string _indexExpression = "-1";

        /// <summary>
        /// 自动导出文件路径（流程结束时自动导出，留空则不导出）
        /// </summary>
        [ObservableProperty]
        private string _autoExportPath = "";

        /// <summary>
        /// 清空数组开关表达式（bool 类型，当为 true 时清空数组）
        /// </summary>
        [ObservableProperty]
        private string _clearExpression = "";

        /// <summary>
        /// 输出：数组当前容量
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private int _outputArrayCount;

        /// <summary>
        /// 输出：保存文件路径
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private string? _outputSavePath;

        public ArrayBuilderTaskCard()
        {
            Name = "数组生成";
        }

        public override bool OutputsBoolResult => true;
        public override bool OutputsStringArray => true;
    }

    /// <summary>
    /// 读取文件任务卡片 - 读取文件按分隔符分割成数组，缓存到内存
    /// </summary>
    public partial class FileReadTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.FileRead;

        /// <summary>
        /// 文件路径表达式
        /// </summary>
        [ObservableProperty]
        private string _filePathExpression = "";

        /// <summary>
        /// 分隔符（默认 \n）
        /// </summary>
        [ObservableProperty]
        private string _delimiter = "\\n";

        /// <summary>
        /// 输出：数组元素数量
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private int _outputArrayCount;

        public override bool OutputsBoolResult => true;
        public override bool OutputsStringArray => true;

        public FileReadTaskCard()
        {
            Name = "读取文件";
        }

        public override void Reset()
        {
            base.Reset();
            OutputArrayCount = 0;
        }
    }

    /// <summary>
    /// 事件监听任务卡片 - 暂停流程，等待用户输入事件触发
    /// </summary>
    public partial class EventListenerTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.EventListener;

        /// <summary>
        /// 事件类型: MouseLeft / MouseRight / Enter / Space
        /// </summary>
        [ObservableProperty]
        private string _eventType = "MouseLeft";

        public override bool OutputsBoolResult => true;

        public EventListenerTaskCard()
        {
            Name = "Win事件监听";
        }
    }

    /// <summary>
    /// 匹配查找任务卡片 - 在数组中搜索文本，支持多种匹配模式
    /// </summary>
    public partial class ArraySearchTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ArraySearch;

        /// <summary>
        /// 要搜索的文本表达式
        /// </summary>
        [ObservableProperty]
        private string _searchExpression = "";

        /// <summary>
        /// 数组来源引用表达式（#N 名称.数组）- 旧属性，保留用于向后兼容
        /// </summary>
        [ObservableProperty]
        private string _arraySourceExpression = "";

        /// <summary>
        /// 数组来源任务ID（通过下拉框选择）
        /// </summary>
        [ObservableProperty]
        private Guid? _sourceTaskIdForArray;

        /// <summary>
        /// 匹配模式: Exact / Contains / Best
        /// </summary>
        [ObservableProperty]
        private string _matchMode = "Contains";

        /// <summary>
        /// 输出：匹配到的索引（-1=未找到）
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private int _outputMatchIndex = -1;

        /// <summary>
        /// 输出：匹配到的值
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private string? _outputMatchValue;

        public override bool OutputsBoolResult => true;

        public ArraySearchTaskCard()
        {
            Name = "匹配查找";
        }

        public override void Reset()
        {
            base.Reset();
            OutputMatchIndex = -1;
            OutputMatchValue = null;
        }
    }

    /// <summary>
    /// 调用子流程任务卡片 - 在主流程中调用一个子流程
    /// </summary>
    public partial class CallSubFlowTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.CallSubFlow;

        /// <summary>
        /// 目标子流程ID
        /// </summary>
        [ObservableProperty]
        private Guid? _targetSubFlowId;

        // 传入子流程的参数源
        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private Guid? _sourceTaskIdForText;

        [ObservableProperty]
        private Guid? _sourceTaskIdForX;

        [ObservableProperty]
        private Guid? _sourceTaskIdForY;

        public CallSubFlowTaskCard()
        {
            Name = "调用子流程";
        }

        public override bool OutputsImage => true;
        public override bool OutputsText => true;
        public override bool OutputsCoordinates => true;
        public override bool OutputsBoolResult => true;

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            // 目标子流程 ID
            if (props.TryGetValue("targetSubFlowId", out var subId) && Guid.TryParse(subId, out var guid))
                TargetSubFlowId = guid;
            else
                missing.Add(new AiFlowReportItem { PropertyName = "目标子流程", Hint = "需要选择要调用的子流程" });

            // 传入图像来源
            if (props.TryGetValue("inputImage", out var inputImage))
            {
                var imgGuid = SubFlowHelper.ResolveSourceGuid(inputImage, stepToCard);
                if (imgGuid.HasValue) SourceTaskIdForImage = imgGuid.Value;
            }

            // 传入文本来源
            if (props.TryGetValue("inputText", out var inputText))
            {
                var txtGuid = SubFlowHelper.ResolveSourceGuid(inputText, stepToCard);
                if (txtGuid.HasValue) SourceTaskIdForText = txtGuid.Value;
            }

            // 传入坐标 X 来源
            if (props.TryGetValue("inputX", out var inputX))
            {
                var xGuid = SubFlowHelper.ResolveSourceGuid(inputX, stepToCard);
                if (xGuid.HasValue) SourceTaskIdForX = xGuid.Value;
            }

            // 传入坐标 Y 来源
            if (props.TryGetValue("inputY", out var inputY))
            {
                var yGuid = SubFlowHelper.ResolveSourceGuid(inputY, stepToCard);
                if (yGuid.HasValue) SourceTaskIdForY = yGuid.Value;
            }

            return missing;
        }
    }

    /// <summary>
    /// 子流程输入卡片 - 在子流程的开头固定存在，接收主流程传入的数据
    /// </summary>
    public partial class SubFlowInputTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.SubFlowInput;

        public SubFlowInputTaskCard()
        {
            Name = "子流程输入";
        }

        public override bool OutputsImage => true;
        public override bool OutputsText => true;
        public override bool OutputsCoordinates => true;
        public override bool OutputsBoolResult => true;
    }

    /// <summary>
    /// 子流程输出(返回)卡片 - 将数据返回给调用者，并中断子流程执行
    /// </summary>
    public partial class SubFlowOutputTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.SubFlowOutput;

        // 子流程内部的引用的源
        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private Guid? _sourceTaskIdForText;

        [ObservableProperty]
        private Guid? _sourceTaskIdForX;

        [ObservableProperty]
        private Guid? _sourceTaskIdForY;

        [ObservableProperty]
        private Guid? _sourceTaskIdForResult;

        public SubFlowOutputTaskCard()
        {
            Name = "子流程返回";
        }

        public override bool CanBeReferenced => false;

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            var props = step.Properties;

            // 返回图像来源
            if (props.TryGetValue("returnImage", out var retImg))
            {
                var imgGuid = SubFlowHelper.ResolveSourceGuid(retImg, stepToCard);
                if (imgGuid.HasValue) SourceTaskIdForImage = imgGuid.Value;
            }

            // 返回文本来源
            if (props.TryGetValue("returnText", out var retText))
            {
                var txtGuid = SubFlowHelper.ResolveSourceGuid(retText, stepToCard);
                if (txtGuid.HasValue) SourceTaskIdForText = txtGuid.Value;
            }

            // 返回坐标 X 来源
            if (props.TryGetValue("returnX", out var retX))
            {
                var xGuid = SubFlowHelper.ResolveSourceGuid(retX, stepToCard);
                if (xGuid.HasValue) SourceTaskIdForX = xGuid.Value;
            }

            // 返回坐标 Y 来源
            if (props.TryGetValue("returnY", out var retY))
            {
                var yGuid = SubFlowHelper.ResolveSourceGuid(retY, stepToCard);
                if (yGuid.HasValue) SourceTaskIdForY = yGuid.Value;
            }

            // 返回布尔结果来源
            if (props.TryGetValue("returnResult", out var retResult))
            {
                var resGuid = SubFlowHelper.ResolveSourceGuid(retResult, stepToCard);
                if (resGuid.HasValue) SourceTaskIdForResult = resGuid.Value;
            }

            return missing;
        }
    }

    /// <summary>
    /// 自定义脚本卡片 - 用户可以在流程中嵌入任意 C# 代码片段
    /// 通过 TaskFlowPro 桥接对象与主流程双向通信
    /// </summary>
    public partial class CustomScriptTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.CustomScript;

        /// <summary>用户编写的 C# 脚本源代码</summary>
        [ObservableProperty]
        private string _scriptCode = "// 在此编写 C# 代码\n// TaskFlowPro.GetTool(\"#1 任务名.输出文本\")\n// TaskFlowPro.SetVar(\"@变量名\", 100)\n";

        /// <summary>脚本执行输出日志（运行时，不序列化）</summary>
        [JsonIgnore]
        [ObservableProperty]
        private string _outputLog = "";

        // 输出声明全开，因为脚本可以通过 TaskFlowPro 设定任意输出
        public override bool OutputsImage => true;
        public override bool OutputsText => true;
        public override bool OutputsCoordinates => true;
        public override bool OutputsBoolResult => true;

        public CustomScriptTaskCard()
        {
            Name = "自定义脚本";
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            if (step.Properties.TryGetValue("scriptCode", out var code) && !string.IsNullOrWhiteSpace(code))
                ScriptCode = code;
            return missing;
        }

        public override void Reset()
        {
            base.Reset();
            OutputLog = "";
        }
    }

    /// <summary>
    /// 辅助类：解析 AI 属性中的 #N 引用格式为 TaskCard Guid
    /// </summary>
    internal static class SubFlowHelper
    {
        /// <summary>
        /// 从 AI 属性值中解析出目标 TaskCard 的 Guid。
        /// 支持格式：
        ///   - "#N 卡片名.输出属性" → 从 stepToCard[N] 获取 Id
        ///   - 直接的 Guid 字符串
        /// </summary>
        public static Guid? ResolveSourceGuid(string value, Dictionary<int, TaskCardBase> stepToCard)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "null")
                return null;

            // 尝试解析 #N 引用格式（如 "#2 拼接加密后缀.输出文本"）
            var match = Regex.Match(value.Trim(), @"^#(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var stepNum))
            {
                if (stepToCard.TryGetValue(stepNum, out var card))
                    return card.Id;
            }

            // 尝试直接作为 Guid 解析
            if (Guid.TryParse(value, out var directGuid))
                return directGuid;

            return null;
        }
    }
}
