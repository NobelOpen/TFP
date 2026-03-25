using System;
using System.Text.Json.Serialization;

namespace TaskFlow.Models
{
    public class LlmModelConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        
        /// <summary>用户自定义的名称</summary>
        public string DisplayName { get; set; } = "新建模型";
        
        /// <summary>API 地址，如 https://api.openai.com/v1/chat/completions</summary>
        public string ApiEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
        
        /// <summary>API Key</summary>
        public string ApiKey { get; set; } = "";
        
        /// <summary>实际调用的模型名称</summary>
        public string ModelName { get; set; } = "gpt-3.5-turbo";
        
        /// <summary>超时设置(秒)</summary>
        public int TimeoutSeconds { get; set; } = 60;
        
        /// <summary>自定义请求头，每行一个 Key: Value 格式</summary>
        public string CustomHeaders { get; set; } = "";
        
        /// <summary>是否启用本地 API 代理（绕过 Cloudflare TLS 指纹检测）</summary>
        public bool UseProxy { get; set; } = false;
        
        /// <summary>代理目标域名（如 api.312800.xyz）</summary>
        public string ProxyTargetHost { get; set; } = "";
        
        /// <summary>统计总消耗的 input tokens</summary>
        public long TotalInputTokens { get; set; } = 0;
        
        /// <summary>统计总消耗的 output tokens</summary>
        public long TotalOutputTokens { get; set; } = 0;
        
        /// <summary>计算总消耗 tokens</summary>
        [JsonIgnore]
        public long TotalTokens => TotalInputTokens + TotalOutputTokens;
        
        public LlmModelConfig Clone()
        {
            return new LlmModelConfig
            {
                Id = this.Id, // 注意 clone 默认保持同样Id，若是复制成新模型需重置 Id
                DisplayName = this.DisplayName,
                ApiEndpoint = this.ApiEndpoint,
                ApiKey = this.ApiKey,
                ModelName = this.ModelName,
                TimeoutSeconds = this.TimeoutSeconds,
                CustomHeaders = this.CustomHeaders,
                UseProxy = this.UseProxy,
                ProxyTargetHost = this.ProxyTargetHost,
                TotalInputTokens = this.TotalInputTokens,
                TotalOutputTokens = this.TotalOutputTokens
            };
        }

        public override string ToString() => DisplayName;
    }
}
