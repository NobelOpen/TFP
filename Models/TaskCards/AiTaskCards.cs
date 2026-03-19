using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using TaskFlow.Models.AiFlow;

namespace TaskFlow.Models.TaskCards
{
    public partial class LlmTranslateTaskCard : TaskCardBase
    {
        [ObservableProperty]
        private string _modelId = "";

        [ObservableProperty]
        private string _sourceTextExpression = "";

        [ObservableProperty]
        private string _targetLanguage = "中文";

        [ObservableProperty]
        private string _systemPrompt = "你是一位专业翻译。请将以下文本翻译成{目标语言}，只输出翻译结果，不要添加任何解释。";
        public override TaskType TaskType => TaskType.LlmTranslate;
        public override bool OutputsText => true;

        public LlmTranslateTaskCard()
        {
            Name = "LLM翻译";
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            missing.Add(new AiFlowReportItem { PropertyName = "ModelId", Hint = "选择翻译模型" });

            if (step.Properties.TryGetValue("targetLanguage", out var lang))
                TargetLanguage = lang;

            return missing;
        }
    }

    /// <summary>
    /// 多模态识图任务卡片 - 将图像和提示词发送给LLM模型，获取回复文本
    /// </summary>
    public partial class LlmVisionTaskCard : TaskCardBase
    {
        /// <summary>选择的模型 ID</summary>
        [ObservableProperty]
        private string _modelId = "";

        /// <summary>用户提示词（支持 @变量 / #任务引用）</summary>
        [ObservableProperty]
        private string _promptExpression = "";

        /// <summary>系统提示词 (System Prompt)</summary>
        [ObservableProperty]
        private string _systemPrompt = "你是一位图像分析助手。请根据用户的要求分析图像内容，只输出判断结果，不要添加多余的解释。";

        // ===== 图像来源属性（标准模式） =====

        [ObservableProperty]
        private string? _imageFilePath;

        [ObservableProperty]
        private Guid? _sourceTaskIdForImage;

        [ObservableProperty]
        private bool _useSourceTaskImage;

        public override TaskType TaskType => TaskType.LlmVision;
        public override bool OutputsText => true;
        public override bool OutputsBoolResult => true;

        public LlmVisionTaskCard()
        {
            Name = "多模态识图";
        }

        public override void BindImageSource(TaskCardBase sourceCard)
        {
            UseSourceTaskImage = true;
            SourceTaskIdForImage = sourceCard.Id;
        }

        public override List<AiFlowReportItem> FillFromAiPlan(
            AiFlowPlanStep step, Dictionary<int, TaskCardBase> stepToCard)
        {
            var missing = new List<AiFlowReportItem>();
            missing.Add(new AiFlowReportItem { PropertyName = "ModelId", Hint = "选择多模态模型" });

            // 绑定图像来源
            if (step.SourceStep.HasValue && stepToCard.TryGetValue(step.SourceStep.Value, out var visionSource))
            {
                if (visionSource.OutputsImage)
                    BindImageSource(visionSource);
            }

            return missing;
        }
    }

    /// <summary>
    /// LLM文件翻译任务卡片 - 从文件读取待翻译文本，批量翻译后写入输出文件
    /// </summary>
    public partial class LlmFileTranslateTaskCard : TaskCardBase
    {
        /// <summary>选择的模型 ID</summary>
        [ObservableProperty]
        private string _modelId = "";

        /// <summary>输入文件路径（支持 @变量）</summary>
        [ObservableProperty]
        private string _inputFilePath = "";

        /// <summary>翻译结果输出文件路径（支持 @变量）</summary>
        [ObservableProperty]
        private string _outputFilePath = "";

        /// <summary>目标语言</summary>
        [ObservableProperty]
        private string _targetLanguage = "简体中文";

        /// <summary>系统提示词</summary>
        [ObservableProperty]
        private string _systemPrompt = "你是一位专业的视觉小说翻译家。以下是按顺序排列的游戏对话文本，请逐行翻译成{目标语言}。要求：\n1. 保持行数完全一致，每行输出对应翻译\n2. 注意前后文语境，保持角色口吻一致\n3. 保留角色名部分不翻译\n4. 只输出翻译结果，不要编号、不要解释";

        /// <summary>每批最大字符数（用于自动分段）</summary>
        [ObservableProperty]
        private int _maxCharsPerBatch = 8000;

        /// <summary>
        /// 输出：已翻译文件路径
        /// </summary>
        [JsonIgnore]
        [ObservableProperty]
        private string? _outputTranslatedFilePath;

        public override TaskType TaskType => TaskType.LlmFileTranslate;

        public LlmFileTranslateTaskCard()
        {
            Name = "LLM文件翻译";
        }
    }
}
