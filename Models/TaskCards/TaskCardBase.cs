using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using OpenCvSharp;
using TaskFlow.Models.AiFlow;

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
        RestartFlow,
        CallSubFlow,
        SubFlowInput,
        SubFlowOutput,

        // Windows操作
        WinLaunchApp,
        WinScreenshot,
        WinClick,
        WinCloseApp,
        WinUiAutomation,
        WinSimulateInput,
        WinSubtitle,
        WinFindFile,
        ClipboardWatch,
        TextExtractor,

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
        ImgOnnxDetect,
        ImgCaliperMeasure,

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
        GetTimestamp,

        // 输入组合
        WinTextInput,
        InputCombo,

        // 自定义脚本
        CustomScript,

        // 浏览器操作（基于 CDP 附着 Chrome）
        BrowserGetText,
        BrowserExecuteJs,
        BrowserWaitForElement,
        BrowserNativeClick,
        BrowserNativeInput,
        BrowserSimulatedClick,
        BrowserCdpCommand,
        BrowserScreenshot,

        // 网络请求
        HttpRequest
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
        public static string GetTaskTypeName(TaskType type) => type switch
        {
            TaskType.IfStart => TaskFlow.Resources.Strings.TaskType_IfStart,
            TaskType.IfEnd => TaskFlow.Resources.Strings.TaskType_IfEnd,
            TaskType.ElifStart => TaskFlow.Resources.Strings.TaskType_ElifStart,
            TaskType.ElseStart => TaskFlow.Resources.Strings.TaskType_ElseStart,
            TaskType.ElseEnd => TaskFlow.Resources.Strings.TaskType_ElseEnd,
            TaskType.ForLoopStart => TaskFlow.Resources.Strings.TaskType_ForLoopStart,
            TaskType.ForLoopEnd => TaskFlow.Resources.Strings.TaskType_ForLoopEnd,
            TaskType.BreakLoop => TaskFlow.Resources.Strings.TaskType_BreakLoop,
            TaskType.EndTask => TaskFlow.Resources.Strings.TaskType_EndTask,
            TaskType.EndAllFlows => TaskFlow.Resources.Strings.TaskType_EndAllFlows,
            TaskType.PauseTask => TaskFlow.Resources.Strings.TaskType_PauseTask,
            TaskType.RestartFlow => TaskFlow.Resources.Strings.TaskType_RestartFlow,
            TaskType.CallSubFlow => TaskFlow.Resources.Strings.TaskType_CallSubFlow,
            TaskType.SubFlowInput => TaskFlow.Resources.Strings.TaskType_SubFlowInput,
            TaskType.SubFlowOutput => TaskFlow.Resources.Strings.TaskType_SubFlowOutput,
            TaskType.WinLaunchApp => TaskFlow.Resources.Strings.TaskType_WinLaunchApp,
            TaskType.WinScreenshot => TaskFlow.Resources.Strings.TaskType_WinScreenshot,
            TaskType.WinClick => TaskFlow.Resources.Strings.TaskType_WinClick,
            TaskType.WinCloseApp => TaskFlow.Resources.Strings.TaskType_WinCloseApp,
            TaskType.WinUiAutomation => TaskFlow.Resources.Strings.TaskType_WinUiAutomation,
            TaskType.WinSimulateInput => TaskFlow.Resources.Strings.TaskType_WinSimulateInput,
            TaskType.WinSubtitle => TaskFlow.Resources.Strings.TaskType_WinSubtitle,
            TaskType.WinFindFile => TaskFlow.Resources.Strings.TaskType_WinFindFile,
            TaskType.AdbConnect => TaskFlow.Resources.Strings.TaskType_AdbConnect,
            TaskType.AdbLaunchApp => TaskFlow.Resources.Strings.TaskType_AdbLaunchApp,
            TaskType.AdbScreenshot => TaskFlow.Resources.Strings.TaskType_AdbScreenshot,
            TaskType.AdbClick => TaskFlow.Resources.Strings.TaskType_AdbClick,
            TaskType.AdbCloseApp => TaskFlow.Resources.Strings.TaskType_AdbCloseApp,
            TaskType.AdbDisconnect => TaskFlow.Resources.Strings.TaskType_AdbDisconnect,
            TaskType.ImgCrop => TaskFlow.Resources.Strings.TaskType_ImgCrop,
            TaskType.ImgTemplateMatch => TaskFlow.Resources.Strings.TaskType_ImgTemplateMatch,
            TaskType.ImgOcr => TaskFlow.Resources.Strings.TaskType_ImgOcr,
            TaskType.ImgColorDetect => TaskFlow.Resources.Strings.TaskType_ImgColorDetect,
            TaskType.ImgColorSegment => TaskFlow.Resources.Strings.TaskType_ImgColorSegment,
            TaskType.ImgPreprocess => TaskFlow.Resources.Strings.TaskType_ImgPreprocess,
            TaskType.ImgBlobAnalysis => TaskFlow.Resources.Strings.TaskType_ImgBlobAnalysis,
            TaskType.ImgResize => TaskFlow.Resources.Strings.TaskType_ImgResize,
            TaskType.ImgOnnxDetect => TaskFlow.Resources.Strings.TaskType_ImgOnnxDetect,
            TaskType.ImgCaliperMeasure => TaskFlow.Resources.Strings.TaskType_ImgCaliperMeasure,
            TaskType.ExpressionEval => TaskFlow.Resources.Strings.TaskType_ExpressionEval,
            TaskType.StringSubstring => TaskFlow.Resources.Strings.TaskType_StringSubstring,
            TaskType.TypeConvert => TaskFlow.Resources.Strings.TaskType_TypeConvert,
            TaskType.ArrayParse => TaskFlow.Resources.Strings.TaskType_ArrayParse,
            TaskType.ArrayBuilder => TaskFlow.Resources.Strings.TaskType_ArrayBuilder,
            TaskType.GetTimestamp => TaskFlow.Resources.Strings.TaskType_GetTimestamp,
            TaskType.LlmTranslate => TaskFlow.Resources.Strings.TaskType_LlmTranslate,
            TaskType.LlmVision => TaskFlow.Resources.Strings.TaskType_LlmVision,
            TaskType.LlmFileTranslate => TaskFlow.Resources.Strings.TaskType_LlmFileTranslate,
            TaskType.FileRead => TaskFlow.Resources.Strings.TaskType_FileRead,
            TaskType.EventListener => TaskFlow.Resources.Strings.TaskType_EventListener,
            TaskType.ArraySearch => TaskFlow.Resources.Strings.TaskType_ArraySearch,
            TaskType.WinTextInput => TaskFlow.Resources.Strings.TaskType_WinTextInput,
            TaskType.InputCombo => TaskFlow.Resources.Strings.TaskType_InputCombo,
            TaskType.CustomScript => TaskFlow.Resources.Strings.TaskType_CustomScript,
            TaskType.BrowserGetText => TaskFlow.Resources.Strings.TaskType_BrowserGetText,
            TaskType.BrowserExecuteJs => TaskFlow.Resources.Strings.TaskType_BrowserExecuteJs,
            TaskType.BrowserWaitForElement => TaskFlow.Resources.Strings.TaskType_BrowserWaitForElement,
            TaskType.BrowserNativeClick => TaskFlow.Resources.Strings.TaskType_BrowserNativeClick,
            TaskType.BrowserNativeInput => TaskFlow.Resources.Strings.TaskType_BrowserNativeInput,
            TaskType.BrowserSimulatedClick => TaskFlow.Resources.Strings.TaskType_BrowserSimulatedClick,
            TaskType.BrowserCdpCommand => TaskFlow.Resources.Strings.TaskType_BrowserCdpCommand,
            TaskType.BrowserScreenshot => TaskFlow.Resources.Strings.TaskType_BrowserScreenshot,
            TaskType.HttpRequest => TaskFlow.Resources.Strings.TaskType_HttpRequest,
            TaskType.ClipboardWatch => TaskFlow.Resources.Strings.TaskType_ClipboardWatch,
            TaskType.TextExtractor => TaskFlow.Resources.Strings.TaskType_TextExtractor,
            _ => type.ToString()
        };

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

        /// <summary>
        /// 是否输出任意形式的数组
        /// </summary>
        public virtual bool OutputsArray => OutputsStringArray;

        /// <summary>
        /// AI 方案属性填充（子类覆写以处理自身特有属性）。
        /// 返回未填写的必要属性列表，供 UI 提示用户手动配置。
        /// </summary>
        /// <param name="step">AI 方案中的步骤定义</param>
        /// <param name="stepToCard">步骤编号到已创建卡片的映射表</param>
        public virtual List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step,
            Dictionary<int, TaskCardBase> stepToCard)
        {
            return new List<AiFlowReportItem>();
        }

        /// <summary>
        /// 绑定图像来源（有 SourceTaskIdForImage 属性的图像类卡片覆写此方法）
        /// </summary>
        public virtual void BindImageSource(TaskCardBase sourceCard) { }

        /// <summary>
        /// 辅助方法：尝试绑定图像来源，返回缺失项列表。
        /// 图像类卡片在 FillFromAiPlan 中调用此方法简化代码。
        /// </summary>
        protected List<AiFlowReportItem> TryBindImageSource(
            AiFlowPlanStep step,
            Dictionary<int, TaskCardBase> stepToCard,
            string hint = "需要绑定一个输出图像的任务")
        {
            var missing = new List<AiFlowReportItem>();
            if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var source))
            {
                if (source.OutputsImage)
                    BindImageSource(source);
            }
            else if (step.Properties.TryGetValue("imageFilePath", out var path) && !string.IsNullOrEmpty(path))
            {
                var targetProp = this.GetType().GetProperty("ImageFilePath");
                if (targetProp != null && targetProp.CanWrite)
                {
                    targetProp.SetValue(this, path);
                    
                    var useSourceProp = this.GetType().GetProperty("UseSourceTaskImage");
                    if (useSourceProp != null && useSourceProp.CanWrite)
                    {
                        useSourceProp.SetValue(this, false);
                    }
                }
            }
            else
            {
                missing.Add(new AiFlowReportItem { PropertyName = "图像来源", Hint = hint });
            }
            return missing;
        }
    }
}
