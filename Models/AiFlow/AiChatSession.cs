using System;
using System.Collections.Generic;

namespace TaskFlow.Models.AiFlow
{
    /// <summary>
    /// 表示一次完整的 Orchid 对话会话
    /// </summary>
    public class AiChatSession
    {
        /// <summary>会话唯一标识</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>会话标题（自动从第一条用户消息截取）</summary>
        public string Title { get; set; } = "新对话";

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>最后更新时间</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>该会话中的所有消息</summary>
        public List<AiChatMessage> Messages { get; set; } = new();
    }
}
