using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using OpenCvSharp;

namespace TaskFlow.Models.TaskCards
{
    /// <summary>
    /// OCR 引擎类型
    /// </summary>
    public enum OcrEngine
    {
        /// <summary>内置 PaddleOCR</summary>
        PaddleOCR,
        /// <summary>微信 OCR（需安装微信）</summary>
        WeChatOCR
    }

    /// <summary>
    /// 任务类型枚举
    /// </summary>
    public enum TaskType
    {
        // 控制流
        IfStart,
        IfEnd,
        ElifStart,
        ElseStart,
        ElseEnd,
        ForLoopStart,
        ForLoopEnd,
        EndTask,
        EndAllFlows,
        PauseTask,
        BreakLoop,

        // Windows操作
        WinLaunchApp,
        WinScreenshot,
        WinClick,
        WinCloseApp,
        WinUiAutomation,
        WinSimulateInput,
        WinSubtitle,
        WinFindFile,

        // ADB操作
        AdbConnect,
        AdbLaunchApp,
        AdbScreenshot,
        AdbClick,
        AdbCloseApp,
        AdbDisconnect,

        // 图像处理
        ImgCrop,
        ImgTemplateMatch,
        ImgOcr,
        ImgColorDetect,
        ImgColorSegment,
        ImgPreprocess,
        ImgBlobAnalysis,
        ImgResize,

        // 逻辑判断
        ExpressionEval,

        // 字符串操作
        StringSubstring,

        // 数据类型转换
        TypeConvert,

        // 数值解析
        ArrayParse,

        // 数组生成
        ArrayBuilder,

        // 文件读取
        FileRead,

        // 事件监听
        EventListener,

        // 匹配查找
        ArraySearch,
        
        // AI操作
        LlmTranslate,
        LlmVision,
        LlmFileTranslate,

        // 时间
        GetTimestamp
    }

    /// <summary>
    /// 任务状态枚举
    /// </summary>
    public enum TaskStatus
    {
        Idle,
        Running,
        Success,
        Failed
    }

    /// <summary>
    /// 分支角色枚举
    /// </summary>
    public enum BranchRole
    {
        None,
        IfStart,
        IfEnd,
        ElifStart,
        ElseStart,
        ElseEnd,
        ForLoopStart,
        ForLoopEnd
    }

    /// <summary>
    /// 条件来源枚举
    /// </summary>
    public enum ConditionSource
    {
        ManualInput,
        TaskResult
    }

    /// <summary>
    /// 点击类型枚举
    /// </summary>
    public enum ClickType
    {
        Single,
        Double,
        Swipe
    }

    /// <summary>
    /// UI自动化元素匹配方式
    /// </summary>
    public enum UiMatchMode
    {
        /// <summary>精确匹配</summary>
        Exact,
        /// <summary>包含匹配</summary>
        Contains,
        /// <summary>正则表达式匹配</summary>
        Regex
    }

    /// <summary>
    /// 字幕背景样式
    /// </summary>
    public enum SubtitleBackground
    {
        /// <summary>亚克力毛玻璃</summary>
        Acrylic,
        /// <summary>纯色背景</summary>
        SolidColor,
        /// <summary>透明背景</summary>
        Transparent,
        /// <summary>自动吸色（采样字幕区域边缘颜色作为背景）</summary>
        AutoSample
    }

    /// <summary>
    /// UI自动化元素查找依据
    /// </summary>
    public enum UiSearchBy
    {
        /// <summary>按控件名称查找</summary>
        Name,
        /// <summary>按 AutomationId 查找</summary>
        AutomationId
    }

    /// <summary>
    /// 任务卡片基类
    /// </summary>
    public abstract partial class TaskCardBase : ObservableObject
    {
        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        private string _name = "新任务";

        public string Name
        {
            get => _name;
            set
            {
                value ??= "";
                value = new string(value.Where(c => !char.IsPunctuation(c) && !char.IsSymbol(c)).ToArray());
                SetProperty(ref _name, value);
            }
        }

        [ObservableProperty]
        private int _order;

        [JsonIgnore]
        [ObservableProperty]
        private TaskStatus _status = TaskStatus.Idle;

        [JsonIgnore]
        [ObservableProperty]
        private string? _errorMessage;

        [JsonIgnore]
        [ObservableProperty]
        private DateTime? _startTime;

        [JsonIgnore]
        [ObservableProperty]
        private DateTime? _completionTime;

        [JsonIgnore]
        [ObservableProperty]
        private TimeSpan? _executionDuration;

        // 分支相关
        [ObservableProperty]
        private Guid? _branchGroupId;

        [ObservableProperty]
        private BranchRole _branchRole = BranchRole.None;

        [ObservableProperty]
        private bool _isCollapsed;

        [JsonIgnore]
        [ObservableProperty]
        private bool _isHiddenByCollapse;

        [JsonIgnore]
        [ObservableProperty]
        private bool _isSelected;

        [JsonIgnore]
        [ObservableProperty]
        private int _indentLevel;

        [JsonIgnore]
        [ObservableProperty]
        private string? _breadcrumbText;

        // 输出数据
        [JsonIgnore]
        [ObservableProperty]
        private Mat? _outputImage;

        [JsonIgnore]
        [ObservableProperty]
        private string? _outputText;

        [JsonIgnore]
        [ObservableProperty]
        private int? _outputX;

        [JsonIgnore]
        [ObservableProperty]
        private int? _outputY;

        [JsonIgnore]
        [ObservableProperty]
        private bool? _outputResult;

        [JsonIgnore]
        [ObservableProperty]
        private int? _outputLoopIndex;

        /// <summary>
        /// 任务类型（抽象属性，由子类实现）
        /// </summary>
        public abstract TaskType TaskType { get; }

        /// <summary>
        /// 获取任务类型的显示名称
        /// </summary>
        public string TaskTypeName => GetTaskTypeName(TaskType);

        /// <summary>
        /// 获取任务类型的显示名称
        /// </summary>
        public static string GetTaskTypeName(TaskType type)
        {
            return type switch
            {
                TaskType.IfStart => Resources.Strings.TaskType_IfStart,
                TaskType.IfEnd => Resources.Strings.TaskType_IfEnd,
                TaskType.ElifStart => Resources.Strings.TaskType_ElifStart,
                TaskType.ElseStart => Resources.Strings.TaskType_ElseStart,
                TaskType.ElseEnd => Resources.Strings.TaskType_ElseEnd,
                TaskType.ForLoopStart => Resources.Strings.TaskType_ForLoopStart,
                TaskType.ForLoopEnd => Resources.Strings.TaskType_ForLoopEnd,
                TaskType.EndTask => Resources.Strings.TaskType_EndTask,
                TaskType.EndAllFlows => Resources.Strings.TaskType_EndAllFlows,
                TaskType.PauseTask => Resources.Strings.TaskType_PauseTask,
                TaskType.WinLaunchApp => Resources.Strings.TaskType_WinLaunchApp,
                TaskType.WinScreenshot => Resources.Strings.TaskType_WinScreenshot,
                TaskType.WinClick => Resources.Strings.TaskType_WinClick,
                TaskType.WinCloseApp => Resources.Strings.TaskType_WinCloseApp,
                TaskType.WinUiAutomation => Resources.Strings.TaskType_WinUiAutomation,
                TaskType.WinSimulateInput => Resources.Strings.TaskType_WinSimulateInput,
                TaskType.WinSubtitle => Resources.Strings.TaskType_WinSubtitle,
                TaskType.WinFindFile => Resources.Strings.TaskType_WinFindFile,
                TaskType.AdbConnect => Resources.Strings.TaskType_AdbConnect,
                TaskType.AdbLaunchApp => Resources.Strings.TaskType_AdbLaunchApp,
                TaskType.AdbScreenshot => Resources.Strings.TaskType_AdbScreenshot,
                TaskType.AdbClick => Resources.Strings.TaskType_AdbClick,
                TaskType.AdbCloseApp => Resources.Strings.TaskType_AdbCloseApp,
                TaskType.AdbDisconnect => Resources.Strings.TaskType_AdbDisconnect,
                TaskType.ImgCrop => Resources.Strings.TaskType_ImgCrop,
                TaskType.ImgTemplateMatch => Resources.Strings.TaskType_ImgTemplateMatch,
                TaskType.ImgOcr => Resources.Strings.TaskType_ImgOcr,
                TaskType.ImgColorDetect => Resources.Strings.TaskType_ImgColorDetect,
                TaskType.ImgColorSegment => Resources.Strings.TaskType_ImgColorSegment,
                TaskType.ImgPreprocess => Resources.Strings.TaskType_ImgPreprocess,
                TaskType.ImgBlobAnalysis => Resources.Strings.TaskType_ImgBlobAnalysis,
                TaskType.ImgResize => Resources.Strings.TaskType_ImgResize,
                TaskType.ExpressionEval => Resources.Strings.TaskType_ExpressionEval,
                TaskType.BreakLoop => Resources.Strings.TaskType_BreakLoop,
                TaskType.StringSubstring => Resources.Strings.TaskType_StringSubstring,
                TaskType.TypeConvert => Resources.Strings.TaskType_TypeConvert,
                TaskType.ArrayParse => Resources.Strings.TaskType_ArrayParse,
                TaskType.ArrayBuilder => Resources.Strings.TaskType_ArrayBuilder,
                TaskType.GetTimestamp => Resources.Strings.TaskType_GetTimestamp,
                TaskType.LlmTranslate => Resources.Strings.TaskType_LlmTranslate,
                TaskType.LlmVision => Resources.Strings.TaskType_LlmVision,
                TaskType.LlmFileTranslate => Resources.Strings.TaskType_LlmFileTranslate,
                TaskType.FileRead => Resources.Strings.TaskType_FileRead,
                TaskType.EventListener => Resources.Strings.TaskType_EventListener,
                TaskType.ArraySearch => Resources.Strings.TaskType_ArraySearch,
                _ => type.ToString()
            };
        }

        /// <summary>
        /// 重置任务状态
        /// </summary>
        public virtual void Reset()
        {
            Status = TaskStatus.Idle;
            ErrorMessage = null;
            StartTime = null;
            CompletionTime = null;
            ExecutionDuration = null;
            OutputImage?.Dispose();
            OutputImage = null;
            OutputText = null;
            OutputX = null;
            OutputY = null;
            OutputResult = null;
        }

        /// <summary>
        /// 是否可以被其他任务引用结果
        /// </summary>
        public virtual bool CanBeReferenced => true;

        /// <summary>
        /// 是否输出图像
        /// </summary>
        public virtual bool OutputsImage => false;

        /// <summary>
        /// 是否输出坐标
        /// </summary>
        public virtual bool OutputsCoordinates => false;

        /// <summary>
        /// 是否输出文本
        /// </summary>
        public virtual bool OutputsText => false;

        /// <summary>
        /// 是否输出结果
        /// </summary>
        public virtual bool OutputsBoolResult => false;

        /// <summary>
        /// 是否输出字符串数组（如 FileRead、ArrayBuilder）
        /// </summary>
        public virtual bool OutputsStringArray => false;
    }
}
