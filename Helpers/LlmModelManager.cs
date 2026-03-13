using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TaskFlow.Models;

namespace TaskFlow.Helpers
{
    public static class LlmModelManager
    {
        public static List<LlmModelConfig> Models { get; set; } = new List<LlmModelConfig>();

        /// <summary>
        /// 当载入新空项目或者文件时被外部初始化调用。
        /// </summary>
#pragma warning disable CS8625
        public static void Initialize(List<LlmModelConfig> models = null)
#pragma warning restore CS8625
        {
            Models = models ?? new List<LlmModelConfig>();
        }

        public static void Load()
        {
            // 旧版系统独立保存机制已废弃，预留此空方法兼容现有调用。
        }

        public static void Save()
        {
            // 旧版系统独立保存机制已废弃，预留此空方法兼容现有调用。
        }

        public static LlmModelConfig GetModelById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Models.Find(m => m.Id == id);
        }
    }
}
