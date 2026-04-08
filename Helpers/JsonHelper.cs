using System;
using System.IO;
using TaskFlow.Resources;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaskFlow.Models;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Helpers
{
    public static class JsonHelper
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Converters = new List<JsonConverter> { new TaskCardConverter() }
        };

        public static string Serialize(IEnumerable<TaskCardBase> tasks)
        {
            return JsonConvert.SerializeObject(tasks, Settings);
        }

        public static List<TaskCardBase> Deserialize(string json)
        {
            var result = JsonConvert.DeserializeObject<List<TaskCardBase>>(json, Settings);
            var tasks = result ?? new List<TaskCardBase>();
            return tasks;
        }

        public static void SaveToFile(string filePath, IEnumerable<TaskCardBase> tasks)
        {
            var json = Serialize(tasks);
            File.WriteAllText(filePath, json);
        }

        public static List<TaskCardBase> LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(string.Format(Strings.Json_FileNotFound, filePath));
            }

            var json = File.ReadAllText(filePath);
            return Deserialize(json);
        }

        /// <summary>
        /// 保存任务卡片和变量到文件（新格式）
        /// </summary>
        public static void SaveToFileWithVariables(string filePath, IEnumerable<TaskCardBase> tasks, IEnumerable<Variable> variables)
        {
            var wrapper = new JObject
            {
                ["variables"] = JArray.FromObject(variables),
                ["tasks"] = JArray.Parse(Serialize(tasks))
            };
            File.WriteAllText(filePath, wrapper.ToString(Formatting.Indented));
        }

        /// <summary>
        /// 从文件加载任务卡片和变量（兼容旧格式）
        /// </summary>
        public static (List<TaskCardBase> Tasks, List<Variable> Variables) LoadFromFileWithVariables(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(string.Format(Strings.Json_FileNotFound, filePath));
            }

            var json = File.ReadAllText(filePath);
            var token = JToken.Parse(json);

            // 新格式：{ "variables": [...], "tasks": [...] }
            if (token is JObject obj && obj.ContainsKey("tasks"))
            {
                var tasks = Deserialize(obj["tasks"]!.ToString());
                var variables = new List<Variable>();
                if (obj.ContainsKey("variables"))
                {
                    variables = obj["variables"]!.ToObject<List<Variable>>() ?? new List<Variable>();
                }
                return (tasks, variables);
            }

            // 旧格式：纯数组
            return (Deserialize(json), new List<Variable>());
        }

        /// <summary>
        /// 保存全部分页数据和共享变量到文件
        /// </summary>
        public static void SaveToFileWithTabs(string filePath, IEnumerable<WorkflowTab> tabs, IEnumerable<Variable> variables)
        {
            var tabsArray = new JArray();
            foreach (var tab in tabs)
            {
                var tabObj = new JObject
                {
                    ["id"] = tab.Id.ToString(),
                    ["type"] = tab.Type.ToString(),
                    ["name"] = tab.Name,
                    ["tasks"] = JArray.Parse(Serialize(tab.TaskCards))
                };
                tabsArray.Add(tabObj);
            }

            var wrapper = new JObject
            {
                ["variables"] = JArray.FromObject(variables),
                ["models"] = JArray.FromObject(TaskFlow.Helpers.LlmModelManager.Models),
                ["onnxModels"] = JArray.FromObject(TaskFlow.Helpers.OnnxModelManager.Models),
                ["tabs"] = tabsArray
            };
            File.WriteAllText(filePath, wrapper.ToString(Formatting.Indented));
        }

        /// <summary>
        /// 从文件加载全部分页数据和共享变量（兼容旧格式）
        /// </summary>
        public static (List<WorkflowTab> Tabs, List<Variable> Variables) LoadFromFileWithTabs(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException(string.Format(Strings.Json_FileNotFound, filePath));

            var json = File.ReadAllText(filePath);
            var token = JToken.Parse(json);
            var result = new List<WorkflowTab>();
            var variables = new List<Variable>();

            if (token is JObject obj && obj.ContainsKey("tabs"))
            {
                // 新格式：{ "variables": [...], "models": [...], "tabs": [ { "name", "tasks" }, ... ] }
                if (obj.ContainsKey("variables"))
                {
                    variables = obj["variables"]!.ToObject<List<Variable>>() ?? new List<Variable>();
                }
                
                if (obj.ContainsKey("models"))
                {
                    var loadedModels = obj["models"]!.ToObject<List<TaskFlow.Models.LlmModelConfig>>();
                    TaskFlow.Helpers.LlmModelManager.Initialize(loadedModels);
                }
                else
                {
                    TaskFlow.Helpers.LlmModelManager.Initialize(new List<TaskFlow.Models.LlmModelConfig>());
                }

                if (obj.ContainsKey("onnxModels"))
                {
                    var loadedOnnxModels = obj["onnxModels"]!.ToObject<List<TaskFlow.Models.OnnxModelConfig>>();
                    TaskFlow.Helpers.OnnxModelManager.Initialize(loadedOnnxModels);
                }
                else
                {
                    TaskFlow.Helpers.OnnxModelManager.Initialize(new List<TaskFlow.Models.OnnxModelConfig>());
                }

                foreach (var tabToken in obj["tabs"]!)
                {
                    var tab = new WorkflowTab
                    {
                        Name = tabToken["name"]?.ToString() ?? Strings.Json_DefaultFlowName,
                        TaskCards = new ObservableCollection<TaskCardBase>(
                            Deserialize(tabToken["tasks"]?.ToString() ?? "[]"))
                    };
                    // 恢复 Tab 的唯一标识和流程类型
                    if (tabToken["id"] != null && Guid.TryParse(tabToken["id"]!.ToString(), out var tabId))
                        tab.Id = tabId;
                    if (tabToken["type"] != null && Enum.TryParse<FlowType>(tabToken["type"]!.ToString(), out var flowType))
                        tab.Type = flowType;
                    tab.NextTaskNumber = tab.TaskCards.Count > 0
                        ? tab.TaskCards.Max(t => t.Order) + 1
                        : 1;
                    result.Add(tab);
                }
            }
            else if (token is JObject obj2 && obj2.ContainsKey("tasks"))
            {
                // 旧格式（含变量）：{ "tasks": [...], "variables": [...] }
                var (tasks, vars) = LoadFromFileWithVariables(filePath);
                variables = vars;
                TaskFlow.Helpers.LlmModelManager.Initialize(new List<TaskFlow.Models.LlmModelConfig>());
                result.Add(new WorkflowTab
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                    TaskCards = new ObservableCollection<TaskCardBase>(tasks),
                    NextTaskNumber = tasks.Count > 0 ? tasks.Max(t => t.Order) + 1 : 1
                });
            }
            else
            {
                // 最旧格式：纯数组
                TaskFlow.Helpers.LlmModelManager.Initialize(new List<TaskFlow.Models.LlmModelConfig>());
                var tasks = Deserialize(json);
                result.Add(new WorkflowTab
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                    TaskCards = new ObservableCollection<TaskCardBase>(tasks),
                    NextTaskNumber = tasks.Count > 0 ? tasks.Max(t => t.Order) + 1 : 1
                });
            }

            return (result, variables);
        }
    }

    /// <summary>
    /// 任务卡片JSON转换器
    /// </summary>
    public class TaskCardConverter : JsonConverter<TaskCardBase>
    {
        /// <summary>
        /// 运行时属性名称列表，序列化时排除、反序列化时忽略
        /// </summary>
        private static readonly HashSet<string> RuntimePropertyNames = new()
        {
            // TaskCardBase 运行时状态
            "Status", "ErrorMessage", "StartTime", "CompletionTime", "ExecutionDuration",
            "IsHiddenByCollapse", "IsSelected", "IndentLevel",
            // TaskCardBase 输出数据
            "OutputImage", "OutputText", "OutputX", "OutputY", "OutputResult", "OutputLoopIndex",
            // TaskCardBase 计算属性
            "TaskTypeName",
            // IfElseBranchTaskCard
            "ConditionResult",
            // ForLoopTaskCard
            "CurrentLoopIndex",
            // GetTimestampTaskCard
            "OutputTimestamp",
            // TypeConvertTaskCard / ArrayParseTaskCard
            "OutputIntValue", "OutputStringValue", "OutputDoubleValue",
            // ImgTemplateMatchTaskCard
            "OutputMatchScore", "OutputMatchCount", "OutputMatchResults",
            // ImgColorDetectTaskCard
            "OutputMeanH", "OutputMeanS", "OutputMeanV", "OutputMatchRatio",
            // ImgBlobAnalysisTaskCard
            "OutputBlobCount", "OutputBlobResults",
            // ImgOnnxDetectTaskCard
            "OutputDetectionCount", "OutputTopClassName", "OutputTopConfidence", "OutputDetectionsArray",
            // ImgCaliperMeasureTaskCard
            "OutputDistance"
        };
        public override TaskCardBase? ReadJson(JsonReader reader, Type objectType, TaskCardBase? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var jsonObject = JObject.Load(reader);

            var taskTypeStr = jsonObject["TaskType"]?.ToString();
            if (string.IsNullOrEmpty(taskTypeStr) || !Enum.TryParse<TaskType>(taskTypeStr, out var taskType))
            {
                return null;
            }

            TaskCardBase task = taskType switch
            {
                TaskType.IfStart => new IfElseBranchTaskCard(BranchRole.IfStart),
                TaskType.IfEnd => new IfElseBranchTaskCard(BranchRole.IfEnd),
                TaskType.ElifStart => new IfElseBranchTaskCard(BranchRole.ElifStart),
                TaskType.ElseStart => new IfElseBranchTaskCard(BranchRole.ElseStart),
                TaskType.ElseEnd => new IfElseBranchTaskCard(BranchRole.ElseEnd),
                TaskType.ForLoopStart => new ForLoopTaskCard(BranchRole.ForLoopStart),
                TaskType.ForLoopEnd => new ForLoopTaskCard(BranchRole.ForLoopEnd),
                TaskType.EndTask => new EndTaskCard(),
                TaskType.RestartFlow => new RestartFlowTaskCard(),
                TaskType.PauseTask => new PauseTaskCard(),
                TaskType.WinLaunchApp => new WinLaunchAppTaskCard(),
                TaskType.WinScreenshot => new WinScreenshotTaskCard(),
                TaskType.WinClick => new WinClickTaskCard(),
                TaskType.WinCloseApp => new WinCloseAppTaskCard(),
                TaskType.WinUiAutomation => new WinUiAutomationTaskCard(),
                TaskType.WinSimulateInput => new WinSimulateInputTaskCard(),
                TaskType.WinSubtitle => new WinSubtitleTaskCard(),
                TaskType.WinFindFile => new WinFindFileTaskCard(),
                TaskType.EventListener => new EventListenerTaskCard(),
                TaskType.AdbConnect => new AdbConnectTaskCard(),
                TaskType.AdbLaunchApp => new AdbLaunchAppTaskCard(),
                TaskType.AdbScreenshot => new AdbScreenshotTaskCard(),
                TaskType.AdbClick => new AdbClickTaskCard(),
                TaskType.AdbCloseApp => new AdbCloseAppTaskCard(),
                TaskType.AdbDisconnect => new AdbDisconnectTaskCard(),
                TaskType.ImgCrop => new ImgCropTaskCard(),
                TaskType.ImgTemplateMatch => new ImgTemplateMatchTaskCard(),
                TaskType.ImgOcr => new ImgOcrTaskCard(),
                TaskType.ImgColorDetect => new ImgColorDetectTaskCard(),
                TaskType.ImgColorSegment => new ImgColorSegmentTaskCard(),
                TaskType.ImgPreprocess => new ImgPreprocessTaskCard(),
                TaskType.ImgBlobAnalysis => new ImgBlobAnalysisTaskCard(),
                TaskType.ImgResize => new ImgResizeTaskCard(),
                TaskType.ImgOnnxDetect => new ImgOnnxDetectTaskCard(),
                TaskType.ImgCaliperMeasure => new ImgCaliperMeasureTaskCard(),
                TaskType.ExpressionEval => new ExpressionEvalTaskCard(),
                TaskType.BreakLoop => new BreakLoopTaskCard(),
                TaskType.StringSubstring => new StringSubstringTaskCard(),
                TaskType.TypeConvert => new TypeConvertTaskCard(),
                TaskType.ArrayParse => new ArrayParseTaskCard(),
                TaskType.GetTimestamp => new GetTimestampTaskCard(),
                TaskType.LlmTranslate => new LlmTranslateTaskCard(),
                TaskType.LlmVision => new LlmVisionTaskCard(),
                TaskType.ArrayBuilder => new ArrayBuilderTaskCard(),
                TaskType.LlmFileTranslate => new LlmFileTranslateTaskCard(),
                TaskType.WinTextInput => new WinTextInputTaskCard(),
                TaskType.InputCombo => new InputComboTaskCard(),
                TaskType.CallSubFlow => new CallSubFlowTaskCard(),
                TaskType.SubFlowInput => new SubFlowInputTaskCard(),
                TaskType.SubFlowOutput => new SubFlowOutputTaskCard(),
                TaskType.CustomScript => new CustomScriptTaskCard(),
                TaskType.BrowserGetText => new BrowserGetTextTaskCard(),
                TaskType.BrowserExecuteJs => new BrowserExecuteJsTaskCard(),
                TaskType.BrowserWaitForElement => new BrowserWaitForElementTaskCard(),
                TaskType.BrowserNativeClick => new BrowserNativeClickTaskCard(),
                TaskType.BrowserNativeInput => new BrowserNativeInputTaskCard(),
                TaskType.BrowserSimulatedClick => new BrowserSimulatedClickTaskCard(),
                TaskType.BrowserCdpCommand => new BrowserCdpCommandTaskCard(),
                TaskType.BrowserScreenshot => new BrowserScreenshotTaskCard(),
                TaskType.HttpRequest => new HttpRequestTaskCard(),
                TaskType.EndAllFlows => new EndAllFlowsTaskCard(),
                TaskType.FileRead => new FileReadTaskCard(),
                TaskType.ArraySearch => new ArraySearchTaskCard(),
                TaskType.ClipboardWatch => new ClipboardWatchTaskCard(),
                TaskType.TextExtractor => new TextExtractorTaskCard(),
                _ => throw new NotSupportedException($"Unknown task type: {taskType}")
            };

            // 读取前清除旧 JSON 中的运行时属性（兼容旧文件）
            foreach (var prop in RuntimePropertyNames)
            {
                jsonObject.Remove(prop);
            }

            serializer.Populate(jsonObject.CreateReader(), task);

            // 模板匹配卡片：如果图像路径无效，从 Base64 还原图像文件
            if (task is ImgTemplateMatchTaskCard matchTask)
            {
                RestoreImageFromBase64(matchTask.Id, jsonObject, "TemplateImageBase64",
                    matchTask.TemplateImagePath, path => matchTask.TemplateImagePath = path, "template");
                RestoreImageFromBase64(matchTask.Id, jsonObject, "MaskImageBase64",
                    matchTask.MaskImagePath, path => matchTask.MaskImagePath = path, "mask");
            }

            // OCR卡片：从 Base64 还原掩膜
            if (task is ImgOcrTaskCard ocrTask)
            {
                RestoreImageFromBase64(ocrTask.Id, jsonObject, "MaskImageBase64",
                    ocrTask.MaskImagePath, path => ocrTask.MaskImagePath = path, "mask");
            }

            // Blob分析卡片：从 Base64 还原掩膜
            if (task is ImgBlobAnalysisTaskCard blobTask)
            {
                RestoreImageFromBase64(blobTask.Id, jsonObject, "MaskImageBase64",
                    blobTask.MaskImagePath, path => blobTask.MaskImagePath = path, "mask");
            }

            return task;
        }

        public override void WriteJson(JsonWriter writer, TaskCardBase? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            // 使用安全的序列化器，跳过OpenCV Mat等原生对象属性
            var safeSerializer = JsonSerializer.CreateDefault(new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = SafeContractResolver.Instance
            });

            var jsonObject = JObject.FromObject(value, safeSerializer);

            // 确保TaskType被序列化
            jsonObject["TaskType"] = value.TaskType.ToString();

            // 移除不需要序列化的运行时属性
            foreach (var prop in RuntimePropertyNames)
            {
                jsonObject.Remove(prop);
            }

            // 模板匹配卡片：将模板图像和掩膜图像编码为 Base64 写入 JSON
            if (value is ImgTemplateMatchTaskCard tmTask)
            {
                EmbedImageAsBase64(jsonObject, "TemplateImageBase64", tmTask.TemplateImagePath);
                EmbedImageAsBase64(jsonObject, "MaskImageBase64", tmTask.MaskImagePath);
            }

            // OCR卡片：将掩膜图像编码为 Base64
            if (value is ImgOcrTaskCard ocrTask)
            {
                EmbedImageAsBase64(jsonObject, "MaskImageBase64", ocrTask.MaskImagePath);
            }

            // Blob分析卡片：将掩膜图像编码为 Base64
            if (value is ImgBlobAnalysisTaskCard blobTask)
            {
                EmbedImageAsBase64(jsonObject, "MaskImageBase64", blobTask.MaskImagePath);
            }

            jsonObject.WriteTo(writer);
        }

        /// <summary>
        /// 将图像文件读取为 Base64 字符串并写入 JObject
        /// </summary>
        private static void EmbedImageAsBase64(JObject jsonObject, string base64Key, string? imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;
            try
            {
                var bytes = File.ReadAllBytes(imagePath);
                var ext = Path.GetExtension(imagePath).ToLowerInvariant();
                jsonObject[base64Key] = Convert.ToBase64String(bytes);
                jsonObject[base64Key + "Ext"] = ext;
            }
            catch { /* 图像读取失败则跳过 */ }
        }

        /// <summary>
        /// 如果图像路径无效，从 Base64 还原图像文件到本地 templates 目录
        /// </summary>
        private static void RestoreImageFromBase64(Guid taskId, JObject jsonObject,
            string base64Key, string? currentPath, Action<string> setPath, string prefix)
        {
            // 如果当前路径有效，不需要还原
            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath)) return;

            var base64 = jsonObject[base64Key]?.ToString();
            if (string.IsNullOrEmpty(base64)) return;

            try
            {
                var ext = jsonObject[base64Key + "Ext"]?.ToString() ?? ".png";
                var bytes = Convert.FromBase64String(base64);

                // 存储到 AppData/TaskFlow/templates 目录
                var templatesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TaskFlow", "templates");
                Directory.CreateDirectory(templatesDir);

                var fileName = $"{prefix}_{taskId}{ext}";
                var filePath = Path.Combine(templatesDir, fileName);
                File.WriteAllBytes(filePath, bytes);

                setPath(filePath);
            }
            catch { /* 还原失败则跳过 */ }
        }
    }

    /// <summary>
    /// 安全的ContractResolver，跳过OpenCV Mat等原生对象类型的属性
    /// 避免序列化时访问原生内存导致ExecutionEngineException
    /// </summary>
    internal class SafeContractResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
    {
        public static readonly SafeContractResolver Instance = new();

        private static readonly HashSet<Type> IgnoredTypes = new()
        {
            typeof(OpenCvSharp.Mat)
        };

        protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(Type type, Newtonsoft.Json.MemberSerialization memberSerialization)
        {
            var props = base.CreateProperties(type, memberSerialization);
            // 过滤掉原生对象类型的属性
            return props.Where(p => p.PropertyType == null || !IgnoredTypes.Contains(p.PropertyType)).ToList();
        }
    }
}
