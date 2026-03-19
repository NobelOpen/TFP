using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using TaskFlow.Models.AiFlow;

namespace TaskFlow.Models.TaskCards
{
    /// <summary>
    /// 图像裁剪任务卡片
    /// </summary>
    public partial class ImgCropTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ImgCrop;

        [ObservableProperty]
        private string? _imageFilePath;

        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private bool _useSourceTaskImage;

        // ROI区域
        [ObservableProperty]
        private int _roiX;

        [ObservableProperty]
        private int _roiY;

        [ObservableProperty]
        private int _roiWidth;

        [ObservableProperty]
        private int _roiHeight;

        // ROI表达式（支持 @变量 / #任务引用）
        [ObservableProperty]
        private string _roiXExpression = string.Empty;

        [ObservableProperty]
        private string _roiYExpression = string.Empty;

        [ObservableProperty]
        private string _roiWidthExpression = string.Empty;

        [ObservableProperty]
        private string _roiHeightExpression = string.Empty;

        public ImgCropTaskCard()
        {
            Name = "图像裁剪";
        }

        public override bool OutputsImage => true;

        public override void BindImageSource(TaskCardBase sourceCard)
        {
            UseSourceTaskImage = true;
            SourceTaskIdForImage = sourceCard.Id;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = TryBindImageSource(step, stepToCard);
            var props = step.Properties;

            // 设置 ROI 区域
            if (props.TryGetValue("roiX", out var rxStr) && int.TryParse(rxStr, out var rx))
                RoiX = rx;
            if (props.TryGetValue("roiY", out var ryStr) && int.TryParse(ryStr, out var ry))
                RoiY = ry;
            if (props.TryGetValue("roiWidth", out var rwStr) && int.TryParse(rwStr, out var rw))
                RoiWidth = rw;
            if (props.TryGetValue("roiHeight", out var rhStr) && int.TryParse(rhStr, out var rh))
                RoiHeight = rh;
            if (RoiWidth <= 0 || RoiHeight <= 0)
                missing.Add(new AiFlowReportItem { PropertyName = "ROI区域", Hint = "裁剪区域坐标和尺寸" });

            return missing;
        }
    }

    /// <summary>
    /// 模板匹配任务卡片
    /// </summary>
    public partial class ImgTemplateMatchTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ImgTemplateMatch;

        [ObservableProperty]
        private string? _imageFilePath;

        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private bool _useSourceTaskImage;

        // 模板ROI区域
        [ObservableProperty]
        private int _templateRoiX;

        [ObservableProperty]
        private int _templateRoiY;

        [ObservableProperty]
        private int _templateRoiWidth;

        [ObservableProperty]
        private int _templateRoiHeight;

        // 匹配阈值
        [ObservableProperty]
        private double _matchThreshold = 0.8;

        // 最大匹配数量（1=单目标匹配，>1=多目标匹配）
        [ObservableProperty]
        private int _maxMatchCount = 1;

        // 是否引用其他任务输出的图像作为模板（动态模板）
        [ObservableProperty]
        private bool _useSourceTaskTemplate;

        // 模板来源任务 ID（当 UseSourceTaskTemplate 为 true 时使用）
        [ObservableProperty]
        private Guid? _sourceTaskIdForTemplate;

        // 保存的模板图像路径（静态模板）
        [ObservableProperty]
        private string? _templateImagePath;

        // 掩膜图像路径（黑白二值图：白色=保留，黑色=遮蔽）
        [ObservableProperty]
        private string? _maskImagePath;

        // 搜索区域ROI（先裁剪再匹配）
        [ObservableProperty]
        private int _roiX;

        [ObservableProperty]
        private int _roiY;

        [ObservableProperty]
        private int _roiWidth;

        [ObservableProperty]
        private int _roiHeight;

        public ImgTemplateMatchTaskCard()
        {
            Name = "模板匹配";
        }

        /// <summary>
        /// 实际匹配分数（运行时输出）
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private double _outputMatchScore;

        /// <summary>
        /// 多目标匹配结果数量
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private int _outputMatchCount;

        /// <summary>
        /// 多目标匹配结果列表（每项包含 X, Y 坐标和匹配分数）
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private List<MatchResult> _outputMatchResults = new();

        public override bool OutputsImage => true;
        public override bool OutputsBoolResult => true;
        public override bool OutputsCoordinates => true;

        public override void Reset()
        {
            base.Reset();
            OutputMatchScore = 0;
            OutputMatchCount = 0;
            OutputMatchResults = new();
        }

        public override void BindImageSource(TaskCardBase sourceCard)
        {
            UseSourceTaskImage = true;
            SourceTaskIdForImage = sourceCard.Id;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            // 搜索图来源
            var missing = TryBindImageSource(step, stepToCard);

            // 模板来源（templateSourceStep）
            if (step.TemplateSourceStep.HasValue && stepToCard.TryGetValue(step.TemplateSourceStep.Value, out var tmplSource))
            {
                if (tmplSource.OutputsImage)
                {
                    UseSourceTaskTemplate = true;
                    SourceTaskIdForTemplate = tmplSource.Id;
                }
            }
            else if (step.Properties.TryGetValue("templateImagePath", out var tmplPath) && !string.IsNullOrEmpty(tmplPath))
            {
                TemplateImagePath = tmplPath;
            }
            else
            {
                missing.Add(new AiFlowReportItem { PropertyName = "模板来源", Hint = "需要绑定模板图来源或指定模板图路径" });
            }

            // 设置匹配阈值
            if ((step.Properties.TryGetValue("matchThreshold", out var threshStr) || step.Properties.TryGetValue("threshold", out threshStr))
                && double.TryParse(threshStr, out var thresh))
                MatchThreshold = thresh;

            return missing;
        }
    }

    /// <summary>
    /// 模板匹配结果
    /// </summary>
    public class MatchResult
    {
        public int X { get; set; }
        public int Y { get; set; }
        public double Score { get; set; }
    }

    /// <summary>
    /// OCR识别任务卡片
    /// </summary>
    public partial class ImgOcrTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ImgOcr;

        [ObservableProperty]
        private string? _imageFilePath;

        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private bool _useSourceTaskImage;

        /// <summary>
        /// OCR 引擎选择
        /// </summary>
        [ObservableProperty]
        private OcrEngine _ocrEngine = OcrEngine.PaddleOCR;

        // 是否检查包含指定字符串
        [ObservableProperty]
        private bool _checkContainsText;

        [ObservableProperty]
        private string _targetText = string.Empty;

        // 掩膜图像路径（黑白二值图：白色=保留，黑色=遮蔽）
        [ObservableProperty]
        private string? _maskImagePath;

        // 识别区域ROI（先裁剪再识别）
        [ObservableProperty]
        private int _roiX;

        [ObservableProperty]
        private int _roiY;

        [ObservableProperty]
        private int _roiWidth;

        [ObservableProperty]
        private int _roiHeight;

        public ImgOcrTaskCard()
        {
            Name = "OCR识别";
        }

        public override bool OutputsText => true;
        public override bool OutputsBoolResult => true;

        public override void BindImageSource(TaskCardBase sourceCard)
        {
            UseSourceTaskImage = true;
            SourceTaskIdForImage = sourceCard.Id;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            return TryBindImageSource(step, stepToCard, "需要绑定一个输出图像的任务（如截图卡片）");
        }
    }

    /// <summary>
    /// 颜色识别任务卡片 - 识别图像HSV值
    /// </summary>
    public partial class ImgColorDetectTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ImgColorDetect;

        [ObservableProperty]
        private string? _imageFilePath;

        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private bool _useSourceTaskImage;

        // HSV下限
        [ObservableProperty]
        private int _hsvLowerH = 0;

        [ObservableProperty]
        private int _hsvLowerS = 0;

        [ObservableProperty]
        private int _hsvLowerV = 0;

        // HSV上限
        [ObservableProperty]
        private int _hsvUpperH = 180;

        [ObservableProperty]
        private int _hsvUpperS = 255;

        [ObservableProperty]
        private int _hsvUpperV = 255;

        // 输出的平均HSV值
        [JsonIgnore]
        [ObservableProperty]
        private double _outputMeanH;

        [JsonIgnore]
        [ObservableProperty]
        private double _outputMeanS;

        [JsonIgnore]
        [ObservableProperty]
        private double _outputMeanV;

        // 在HSV范围内的像素占比
        [JsonIgnore]
        [ObservableProperty]
        private double _outputMatchRatio;

        // ROI识别区域
        [ObservableProperty]
        private int _roiX;

        [ObservableProperty]
        private int _roiY;

        [ObservableProperty]
        private int _roiWidth;

        [ObservableProperty]
        private int _roiHeight;

        public ImgColorDetectTaskCard()
        {
            Name = "颜色识别";
        }

        public override bool OutputsImage => true;
        public override bool OutputsBoolResult => true;

        public override void BindImageSource(TaskCardBase sourceCard)
        {
            UseSourceTaskImage = true;
            SourceTaskIdForImage = sourceCard.Id;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            return TryBindImageSource(step, stepToCard);
        }
    }

    /// <summary>
    /// 颜色分割任务卡片 - 将图像中不符合HSV范围的区域涂黑，只保留匹配区域
    /// </summary>
    public partial class ImgColorSegmentTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ImgColorSegment;

        [ObservableProperty]
        private string? _imageFilePath;

        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private bool _useSourceTaskImage;

        // HSV下限
        [ObservableProperty]
        private int _hsvLowerH = 0;

        [ObservableProperty]
        private int _hsvLowerS = 0;

        [ObservableProperty]
        private int _hsvLowerV = 0;

        // HSV上限
        [ObservableProperty]
        private int _hsvUpperH = 180;

        [ObservableProperty]
        private int _hsvUpperS = 255;

        [ObservableProperty]
        private int _hsvUpperV = 255;

        public ImgColorSegmentTaskCard()
        {
            Name = "颜色分割";
        }

        public override bool OutputsImage => true;

        public override void BindImageSource(TaskCardBase sourceCard)
        {
            UseSourceTaskImage = true;
            SourceTaskIdForImage = sourceCard.Id;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            return TryBindImageSource(step, stepToCard);
        }
    }

    /// <summary>
    /// 二值化方式枚举
    /// </summary>
    public enum BinarizeMethod
    {
        None,
        Binary,
        BinaryInv,
        Otsu,
        Triangle
    }

    /// <summary>
    /// 形态学操作枚举
    /// </summary>
    public enum MorphologyMethod
    {
        None,
        Open,
        Close,
        Dilate,
        Erode
    }

    /// <summary>
    /// Blob排序方式枚举
    /// </summary>
    public enum BlobSortMode
    {
        AreaDesc,
        AreaAsc,
        LeftToRight,
        TopToBottom
    }

    /// <summary>
    /// 图像预处理任务卡片 - 灰度化/二值化/形态学处理
    /// </summary>
    public partial class ImgPreprocessTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ImgPreprocess;

        [ObservableProperty]
        private string? _imageFilePath;

        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private bool _useSourceTaskImage;

        /// <summary>
        /// 是否启用灰度转换
        /// </summary>
        [ObservableProperty]
        private bool _enableGrayscale = true;

        /// <summary>
        /// 二值化方式
        /// </summary>
        [ObservableProperty]
        private BinarizeMethod _binarizeMethod = BinarizeMethod.None;

        /// <summary>
        /// 二值化阈值（Binary/BinaryInv时使用）
        /// </summary>
        [ObservableProperty]
        private int _binarizeThreshold = 128;

        /// <summary>
        /// 形态学操作
        /// </summary>
        [ObservableProperty]
        private MorphologyMethod _morphologyMethod = MorphologyMethod.None;

        /// <summary>
        /// 形态学核大小
        /// </summary>
        [ObservableProperty]
        private int _morphologyKernelSize = 3;

        public ImgPreprocessTaskCard()
        {
            Name = "图像预处理";
        }

        public override bool OutputsImage => true;

        public override void BindImageSource(TaskCardBase sourceCard)
        {
            UseSourceTaskImage = true;
            SourceTaskIdForImage = sourceCard.Id;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            return TryBindImageSource(step, stepToCard);
        }
    }

    /// <summary>
    /// Blob分析结果
    /// </summary>
    public class BlobResult
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Area { get; set; }
    }

    /// <summary>
    /// Blob分析任务卡片 - 连通域分析
    /// </summary>
    public partial class ImgBlobAnalysisTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ImgBlobAnalysis;

        [ObservableProperty]
        private string? _imageFilePath;

        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private bool _useSourceTaskImage;

        /// <summary>
        /// 面积最小值
        /// </summary>
        [ObservableProperty]
        private int _minArea = 100;

        /// <summary>
        /// 面积最大值
        /// </summary>
        [ObservableProperty]
        private int _maxArea = 999999;

        /// <summary>
        /// 排序方式
        /// </summary>
        [ObservableProperty]
        private BlobSortMode _sortMode = BlobSortMode.AreaDesc;

        /// <summary>
        /// 最大返回Blob数量
        /// </summary>
        [ObservableProperty]
        private int _maxBlobCount = 10;

        /// <summary>
        /// 颜色极性反转（true=检测暗色Blob，false=检测亮色Blob）
        /// </summary>
        [ObservableProperty]
        private bool _invertBinary;

        // 掩膜图像路径（黑白二值图：白色=保留，黑色=遮蔽）
        [ObservableProperty]
        private string? _maskImagePath;

        // 分析区域ROI（先裁剪再分析）
        [ObservableProperty]
        private int _roiX;

        [ObservableProperty]
        private int _roiY;

        [ObservableProperty]
        private int _roiWidth;

        [ObservableProperty]
        private int _roiHeight;

        // 输出字段
        [JsonIgnore]
        [ObservableProperty]
        private int _outputBlobCount;

        [JsonIgnore]
        [ObservableProperty]
        private List<BlobResult> _outputBlobResults = new();

        public ImgBlobAnalysisTaskCard()
        {
            Name = "Blob分析";
        }

        public override bool OutputsImage => true;
        public override bool OutputsBoolResult => true;
        public override bool OutputsCoordinates => true;
        public override bool OutputsText => false;

        public override void Reset()
        {
            base.Reset();
            OutputBlobCount = 0;
            OutputBlobResults = new();
        }

        public override void BindImageSource(TaskCardBase sourceCard)
        {
            UseSourceTaskImage = true;
            SourceTaskIdForImage = sourceCard.Id;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            return TryBindImageSource(step, stepToCard);
        }
    }

    /// <summary>
    /// 图像缩放任务卡片
    /// </summary>
    public partial class ImgResizeTaskCard : TaskCardBase
    {
        public override TaskType TaskType => TaskType.ImgResize;

        [ObservableProperty]
        private string? _imageFilePath;

        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private bool _useSourceTaskImage;

        /// <summary>
        /// 目标宽度
        /// </summary>
        [ObservableProperty]
        private int _targetWidth;

        /// <summary>
        /// 目标高度
        /// </summary>
        [ObservableProperty]
        private int _targetHeight;

        /// <summary>
        /// 输出缩放倍率
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private double _outputScaleRatio;

        public ImgResizeTaskCard()
        {
            Name = "图像缩放";
        }

        public override bool OutputsImage => true;

        public override void Reset()
        {
            base.Reset();
            OutputScaleRatio = 0;
        }

        public override void BindImageSource(TaskCardBase sourceCard)
        {
            UseSourceTaskImage = true;
            SourceTaskIdForImage = sourceCard.Id;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            return TryBindImageSource(step, stepToCard);
        }
    }
}
