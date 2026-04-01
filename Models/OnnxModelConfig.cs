using System;
using System.IO;
using System.Text.Json.Serialization;

namespace TaskFlow.Models
{
    /// <summary>
    /// ONNX 视觉模型配置（YOLO 目标检测模型）
    /// </summary>
    public class OnnxModelConfig
    {
        /// <summary>唯一标识</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>用户自定义的显示名称</summary>
        public string DisplayName { get; set; } = "新建模型";

        /// <summary>模型文件名（存储在统一目录下）</summary>
        public string FileName { get; set; } = "";

        /// <summary>模型输入宽度（默认 640）</summary>
        public int InputWidth { get; set; } = 640;

        /// <summary>模型输入高度（默认 640）</summary>
        public int InputHeight { get; set; } = 640;

        /// <summary>置信度阈值（默认 0.5）</summary>
        public double ConfidenceThreshold { get; set; } = 0.5;

        /// <summary>NMS IoU 阈值（默认 0.45）</summary>
        public double IouThreshold { get; set; } = 0.45;

        /// <summary>类别标签列表（逗号分隔，如 "player,enemy,item"）</summary>
        public string ClassLabels { get; set; } = "";

        /// <summary>模型统一存储目录</summary>
        [JsonIgnore]
        public static readonly string ModelsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskFlow", "onnx_models");

        /// <summary>模型文件完整路径</summary>
        [JsonIgnore]
        public string FilePath => Path.Combine(ModelsDir, FileName);

        /// <summary>类别标签数组</summary>
        [JsonIgnore]
        public string[] ClassLabelArray => string.IsNullOrWhiteSpace(ClassLabels)
            ? Array.Empty<string>()
            : ClassLabels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>类别数量</summary>
        [JsonIgnore]
        public int ClassCount => ClassLabelArray.Length;

        /// <summary>模型文件是否存在</summary>
        [JsonIgnore]
        public bool FileExists => File.Exists(FilePath);

        public override string ToString() => DisplayName;
    }
}
