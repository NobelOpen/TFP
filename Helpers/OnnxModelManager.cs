using System;
using System.Collections.Generic;
using TaskFlow.Models;

namespace TaskFlow.Helpers
{
    /// <summary>
    /// ONNX 视觉模型全局管理器（与 LlmModelManager 同构）
    /// </summary>
    public static class OnnxModelManager
    {
        public static List<OnnxModelConfig> Models { get; set; } = new List<OnnxModelConfig>();

        public static event EventHandler? ModelsChanged;

        public static void NotifyModelsChanged()
        {
            ModelsChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// 当载入新空项目或者文件时被外部初始化调用。
        /// </summary>
        public static void Initialize(List<OnnxModelConfig>? models = null)
        {
            Models = models ?? new List<OnnxModelConfig>();
            NotifyModelsChanged();
        }

        /// <summary>
        /// 根据 ID 获取模型配置
        /// </summary>
        public static OnnxModelConfig? GetModelById(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Models.Find(m => m.Id == id);
        }
    }
}
