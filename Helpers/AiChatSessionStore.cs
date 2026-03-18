using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskFlow.Models.AiFlow;

namespace TaskFlow.Helpers
{
    /// <summary>
    /// Orchid 对话会话的持久化管理器（JSON 序列化/反序列化）
    /// </summary>
    public static class AiChatSessionStore
    {
        private static readonly string SessionDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "AiSessions");

        private static readonly string SessionFilePath = Path.Combine(SessionDir, "sessions.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// 从磁盘加载所有历史会话
        /// </summary>
        public static List<AiChatSession> Load()
        {
            try
            {
                if (!File.Exists(SessionFilePath))
                    return new List<AiChatSession>();

                var json = File.ReadAllText(SessionFilePath);
                return JsonSerializer.Deserialize<List<AiChatSession>>(json, JsonOptions) ?? new List<AiChatSession>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiChatSessionStore] 加载会话失败: {ex.Message}");
                return new List<AiChatSession>();
            }
        }

        /// <summary>
        /// 将所有历史会话保存到磁盘
        /// </summary>
        public static void Save(List<AiChatSession> sessions)
        {
            try
            {
                if (!Directory.Exists(SessionDir))
                    Directory.CreateDirectory(SessionDir);

                var json = JsonSerializer.Serialize(sessions, JsonOptions);
                File.WriteAllText(SessionFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiChatSessionStore] 保存会话失败: {ex.Message}");
            }
        }
    }
}
