using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json.Linq;
using TaskFlow.Helpers;

namespace TaskFlow.Services
{
    /// <summary>
    /// 基于本地 Embedding 模型的语义路由服务。
    /// 使用 paraphrase-multilingual-MiniLM-L12-v2 ONNX 模型将文本转换为 384 维向量，
    /// 支持 50+ 语言（包括中文），通过余弦相似度在零 Token 消耗下完成意图到卡片类别的语义匹配。
    /// </summary>
    public class SemanticRouter : IDisposable
    {
        // ====================[ 单例管理 ]====================
        private static SemanticRouter? _instance;
        private static readonly object _instanceLock = new();

        public static SemanticRouter GetInstance()
        {
            if (_instance != null) return _instance;
            lock (_instanceLock)
            {
                _instance ??= new SemanticRouter();
                return _instance;
            }
        }

        // ====================[ ONNX 模型 ]====================
        private InferenceSession? _session;
        private bool _isReady;

        // ====================[ Unigram 分词器 ]====================
        // 从 tokenizer.json 加载的 token→id 查找表（支持 250K+ 多语言 token）
        private Dictionary<string, int>? _vocabMap;
        // 按 token 长度降序排列的 token 列表，用于最长前缀匹配
        private List<string>? _sortedTokens;
        // SentencePiece 使用 ▁ (U+2581) 作为空格前缀标记
        private const char SP_SPACE = '\u2581';

        // ====================[ 特殊 Token ID ]====================
        // paraphrase-multilingual-MiniLM-L12-v2 使用 XLM-RoBERTa 的 SentencePiece 词汇表
        // <s> = 0（BOS/CLS），</s> = 2（EOS/SEP），<pad> = 1，<unk> = 3
        private const int CLS_TOKEN_ID = 0;  // <s>
        private const int SEP_TOKEN_ID = 2;  // </s>
        private const int PAD_TOKEN_ID = 1;  // <pad>
        private const int UNK_TOKEN_ID = 3;  // <unk>

        // ====================[ 卡片向量缓存 ]====================
        // 存储每张卡片的对应分类、卡片类型名称，以及 384 维归一化向量
        private readonly List<(string Category, string TaskType, float[] Vector)> _cardVectors = new();
        private static readonly object _cardLock = new();

        // ====================[ 初始化 ]====================

        private SemanticRouter()
        {
            TryInitialize();
        }

        private void TryInitialize()
        {
            try
            {
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

                // 加载多语言 ONNX 模型
                var modelPath = Path.Combine(assemblyDir, "Resources", "Models", "paraphrase-multilingual-MiniLM-L12-v2.onnx");
                // 加载 tokenizer.json（包含完整的 Unigram 词汇表）
                var tokenizerPath = Path.Combine(assemblyDir, "Resources", "Models", "tokenizer.json");

                if (!File.Exists(modelPath))
                {
                    AiFlowLogger.Warn($"语义路由：ONNX 模型文件不存在 ({modelPath})，将回退到 LLM 分类模式。");
                    return;
                }

                // 加载 ONNX 推理会话
                var sessionOptions = new SessionOptions();
                sessionOptions.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
                try { sessionOptions.AppendExecutionProvider_DML(0); } catch { /* CPU 回退 */ }

                _session = new InferenceSession(modelPath, sessionOptions);

                // 加载 Unigram 分词器词汇表
                if (File.Exists(tokenizerPath))
                {
                    LoadTokenizerVocab(tokenizerPath);
                    AiFlowLogger.Info($"语义路由：Unigram 多语言分词器已加载（{_vocabMap?.Count ?? 0} 个 token）。");
                }
                else
                {
                    AiFlowLogger.Warn($"语义路由：tokenizer.json 不存在 ({tokenizerPath})，将使用字符级回退分词。");
                }

                _isReady = true;
                AiFlowLogger.Info("语义路由：多语言 ONNX 模型 (paraphrase-multilingual-MiniLM-L12-v2) 已加载完成。");
            }
            catch (Exception ex)
            {
                AiFlowLogger.Warn($"语义路由初始化失败，将回退到 LLM 分类模式: {ex.Message}");
                _isReady = false;
            }
        }

        /// <summary>
        /// 从 HuggingFace tokenizer.json 中加载 Unigram 词汇表。
        /// vocab 格式：[ ["token_string", score], ... ]
        /// </summary>
        private void LoadTokenizerVocab(string tokenizerPath)
        {
            var json = File.ReadAllText(tokenizerPath, Encoding.UTF8);
            var root = JObject.Parse(json);

            var vocabArray = root["model"]?["vocab"] as JArray;
            if (vocabArray == null || vocabArray.Count == 0)
            {
                AiFlowLogger.Warn("语义路由：tokenizer.json 中未找到 vocab 字段。");
                return;
            }

            _vocabMap = new Dictionary<string, int>(vocabArray.Count);

            for (int i = 0; i < vocabArray.Count; i++)
            {
                var entry = vocabArray[i] as JArray;
                if (entry != null && entry.Count >= 1)
                {
                    var token = entry[0]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(token) && !_vocabMap.ContainsKey(token))
                    {
                        _vocabMap[token] = i;
                    }
                }
            }

            // 补充 added_tokens（特殊 token）
            var addedTokens = root["added_tokens"] as JArray;
            if (addedTokens != null)
            {
                foreach (var at in addedTokens)
                {
                    var content = at["content"]?.ToString();
                    var id = at["id"]?.Value<int>() ?? -1;
                    if (!string.IsNullOrEmpty(content) && id >= 0)
                        _vocabMap[content] = id;
                }
            }

            // 构建按长度降序排列的 token 列表（用于贪心最长匹配）
            // 只保留长度 > 0 且不是特殊 token 的普通 token
            _sortedTokens = _vocabMap.Keys
                .Where(t => t.Length > 0 && !t.StartsWith("<") && !t.EndsWith(">"))
                .OrderByDescending(t => t.Length)
                .ToList();
        }

        /// <summary>
        /// 语义路由是否可用（模型已加载）
        /// </summary>
        public bool IsReady => _isReady;

        // ====================[ 卡片向量预计算 ]====================

        /// <summary>
        /// 为所有卡片预先计算 Embedding 向量并缓存。
        /// 每次 AiFlowDescriptions.json 发生变化时调用一次即可。
        /// </summary>
        public void PrecomputeCardVectors(IEnumerable<(string Category, string TaskType, string Description, string Usage)> cards)
        {
            if (!_isReady) return;

            lock (_cardLock)
            {
                _cardVectors.Clear();
                foreach (var (category, taskType, desc, usage) in cards)
                {
                    // 将卡片属性拼接为语义文本，以单张卡片为粒度更能保持语义纯度
                    var text = $"{taskType} {desc} {usage}";
                    var vec = GetEmbedding(text);
                    if (vec != null)
                        _cardVectors.Add((category, taskType, Normalize(vec)));
                }
                AiFlowLogger.Info($"语义路由：已预计算 {_cardVectors.Count} 个卡片向量。");
            }
        }

        // ====================[ 路由核心逻辑 ]====================

        /// <summary>
        /// 根据用户输入语义匹配最相关的卡片，提取对应的卡片类别。
        /// </summary>
        /// <param name="userPrompt">用户输入</param>
        /// <param name="threshold">相似度阈值（≥此值才认为相关，默认 0.35）</param>
        /// <param name="minCategories">至少返回几个类别（防止阈值过高导致空结果）</param>
        /// <returns>匹配的类别名称列表</returns>
        public List<string> Route(string userPrompt, float threshold = 0.45f, int minCategories = 1)
        {
            if (!_isReady || _cardVectors.Count == 0)
                return new List<string>();

            try
            {
                // 计算用户输入的 Embedding
                var userVec = GetEmbedding(userPrompt);
                if (userVec == null) return new List<string>();
                var normalizedUser = Normalize(userVec);

                // 计算所有卡片的余弦相似度（已预归一化，直接点积即可）
                var scores = new List<(string Category, string TaskType, float Score)>();
                lock (_cardLock)
                {
                    foreach (var card in _cardVectors)
                    {
                        float sim = CosineSimilarityNormalized(normalizedUser, card.Vector);
                        scores.Add((card.Category, card.TaskType, sim));
                    }
                }

                scores.Sort((a, b) => b.Score.CompareTo(a.Score));

                // 记录排名前几位的详细得分方便调试
                var topCards = scores.Take(8).ToList();
                var scoreLog = string.Join(", ", topCards.Select(s => $"{s.TaskType}={s.Score:F3}"));
                AiFlowLogger.Info($"语义路由卡片得分 Top8: [{scoreLog}]");

                // 筛选出大于阈值的卡片，再提取这些卡片所属的 Category（去重）
                var resultCategories = scores.Where(s => s.Score >= threshold)
                                             .Select(s => s.Category)
                                             .Distinct()
                                             .ToList();

                // 如果高分分类过少，按得分从高到低强制补全到 minCategories 个
                if (resultCategories.Count < minCategories)
                {
                    resultCategories = scores.Select(s => s.Category)
                                             .Distinct()
                                             .Take(minCategories)
                                             .ToList();
                }

                AiFlowLogger.Info($"语义路由最终大类结果（阈值 {threshold}）: [{string.Join(", ", resultCategories)}]");
                return resultCategories;
            }
            catch (Exception ex)
            {
                AiFlowLogger.Warn($"语义路由执行失败: {ex.Message}");
                return new List<string>();
            }
        }

        // ====================[ Embedding 计算 ]====================

        private float[]? GetEmbedding(string text)
        {
            if (_session == null) return null;

            // 截断到最多 128 个 token（路由场景文本较短，减少计算量）
            var (inputIds, attentionMask) = Tokenize(text, maxLength: 128);

            // 构建输入 Tensor
            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            };

            // 检查模型是否需要 token_type_ids 输入
            var inputNames = _session.InputMetadata.Keys.ToHashSet();
            if (inputNames.Contains("token_type_ids"))
            {
                var tokenTypeIds = new long[inputIds.Length];
                var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { 1, tokenTypeIds.Length });
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor));
            }

            using var outputs = _session.Run(inputs);

            // last_hidden_state shape: [1, seq_len, 384]
            // Mean Pooling（只对 attention_mask=1 的 token 求均值）
            var lastHiddenState = outputs.First(o => o.Name == "last_hidden_state")
                                          .AsEnumerable<float>().ToArray();

            int seqLen = inputIds.Length;
            int hiddenSize = 384;

            var pooled = new float[hiddenSize];
            int validTokens = 0;

            for (int i = 0; i < seqLen; i++)
            {
                if (attentionMask[i] == 1)
                {
                    for (int j = 0; j < hiddenSize; j++)
                        pooled[j] += lastHiddenState[i * hiddenSize + j];
                    validTokens++;
                }
            }

            if (validTokens > 0)
                for (int j = 0; j < hiddenSize; j++)
                    pooled[j] /= validTokens;

            return pooled;
        }

        // ====================[ 分词 ]====================

        /// <summary>
        /// 使用 Unigram 词汇表对文本进行贪心最长匹配分词，生成 ONNX 模型所需的输入张量。
        /// 格式：[CLS] token1 token2 ... [SEP] [PAD] [PAD] ...
        /// </summary>
        private (long[] InputIds, long[] AttentionMask) Tokenize(string text, int maxLength)
        {
            List<int> tokenIds;

            if (_vocabMap != null && _sortedTokens != null)
            {
                // 使用 Unigram 词汇表分词
                tokenIds = UnigramTokenize(text, maxLength - 2);
            }
            else
            {
                // 字符级分词回退
                tokenIds = new List<int>();
                foreach (char c in text)
                {
                    var charStr = c.ToString();
                    tokenIds.Add(_vocabMap?.GetValueOrDefault(charStr, UNK_TOKEN_ID) ?? UNK_TOKEN_ID);
                    if (tokenIds.Count >= maxLength - 2) break;
                }
            }

            // 构建完整序列：<s> + tokens + </s>
            var inputIds = new List<long>(maxLength) { CLS_TOKEN_ID };
            foreach (var id in tokenIds)
                inputIds.Add(id);
            inputIds.Add(SEP_TOKEN_ID);

            var attentionMask = Enumerable.Repeat(1L, inputIds.Count).ToList();

            // Padding 到 maxLength
            while (inputIds.Count < maxLength)
            {
                inputIds.Add(PAD_TOKEN_ID);
                attentionMask.Add(0L);
            }

            return (inputIds.ToArray(), attentionMask.ToArray());
        }

        /// <summary>
        /// 基于 Unigram 词汇表的贪心最长前缀匹配分词。
        /// SentencePiece 的文本预处理：将空格替换为 ▁ (U+2581)，并在文本开头添加 ▁。
        /// </summary>
        private List<int> UnigramTokenize(string text, int maxTokens)
        {
            var result = new List<int>();

            // SentencePiece 预处理：小写化 + 空格替换为 ▁ + 开头添加 ▁
            var processed = SP_SPACE + text.ToLowerInvariant().Replace(' ', SP_SPACE);

            int pos = 0;
            while (pos < processed.Length && result.Count < maxTokens)
            {
                bool found = false;

                // 贪心最长匹配：从最长 token 开始尝试
                int remaining = processed.Length - pos;
                foreach (var token in _sortedTokens!)
                {
                    if (token.Length > remaining) continue;

                    // 检查当前位置是否匹配此 token
                    if (processed.AsSpan(pos, token.Length).SequenceEqual(token.AsSpan()))
                    {
                        result.Add(_vocabMap![token]);
                        pos += token.Length;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // 未找到匹配的 token，当前字符映射为 UNK 并跳过
                    result.Add(UNK_TOKEN_ID);
                    pos++;
                }
            }

            return result;
        }

        // ====================[ 向量工具 ]====================

        private static float[] Normalize(float[] vec)
        {
            float norm = (float)Math.Sqrt(vec.Sum(x => x * x));
            if (norm < 1e-8f) return vec;
            return vec.Select(x => x / norm).ToArray();
        }

        private static float CosineSimilarityNormalized(float[] a, float[] b)
        {
            // 已归一化，余弦相似度 = 点积
            float dot = 0f;
            for (int i = 0; i < a.Length; i++)
                dot += a[i] * b[i];
            return dot;
        }

        // ====================[ 释放资源 ]====================

        public void Dispose()
        {
            _session?.Dispose();
            _session = null;
            _isReady = false;
        }
    }
}
