using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskFlow.Models
{
    /// <summary>
    /// 应用设置模型，保存到 %LOCALAPPDATA%/TaskFlow/settings.json
    /// </summary>
    public class AppSettings
    {
        /// <summary>开机自启动</summary>
        public bool AutoStartWithOS { get; set; } = false;

        /// <summary>启动后立即隐藏窗口</summary>
        public bool HideOnStartup { get; set; } = false;

        /// <summary>启动后立即运行全部流程</summary>
        public bool RunAllOnStartup { get; set; } = false;

        /// <summary>启动时自动加载上次项目</summary>
        public bool AutoLoadLastProject { get; set; } = true;

        /// <summary>流程执行间隔（毫秒）</summary>
        public int FlowExecutionIntervalMs { get; set; } = 0;

        /// <summary>日志最大行数</summary>
        public int MaxLogLines { get; set; } = 500;

        /// <summary>自动保存日志到文件</summary>
        public bool AutoSaveLogToFile { get; set; } = false;

        /// <summary>重复循环执行全部流程</summary>
        public bool RepeatRunAll { get; set; } = false;

        /// <summary>重复执行间隔（毫秒）</summary>
        public int RepeatIntervalMs { get; set; } = 0;

        /// <summary>运行中保持屏幕不息屏</summary>
        public bool KeepScreenOn { get; set; } = false;

        /// <summary>界面语言，默认英文。可选值: "en", "zh-CN"</summary>
        public string Language { get; set; } = "en";

        /// <summary>系统主题。可选值: "Light", "Dark"</summary>
        public string Theme { get; set; } = "Light";

        /// <summary>AI 助手模式：0=设计, 1=自主</summary>
        public int AiAssistantMode { get; set; } = 0;

        /// <summary>Orchid 单次调用模式（跳过类别判断阶段，适合高级模型）</summary>
        public bool OrchidSingleStage { get; set; } = false;

        /// <summary>显式配置的轻量路由模型 ID（用于第一阶段意图分类）</summary>
        public string? RouterModelId { get; set; }

        // ========== 微信 OCR ==========

        /// <summary>微信 OCR 可执行文件路径（WeChatOCR.exe 或 wxocr.dll）</summary>
        public string? WeChatOcrExePath { get; set; }

        /// <summary>微信运行时目录</summary>
        public string? WeChatOcrDirPath { get; set; }

        /// <summary>微信 OCR 是否已通过可用性测试</summary>
        public bool WeChatOcrVerified { get; set; } = false;

        // ========== 序列化 ==========

        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskFlow");

        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        /// <summary>加载设置，文件不存在则返回默认设置</summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        /// <summary>保存设置到文件</summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
