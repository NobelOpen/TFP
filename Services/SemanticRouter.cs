using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using TaskFlow.Helpers;

namespace TaskFlow.Services
{
    /// <summary>
    /// 基于本地 Embedding 模型的语义路由服务。
    /// 使用 all-MiniLM-L6-v2 ONNX 模型将文本转换为 384 维向量，
    /// 通过余弦相似度在零 Token 消耗下完成意图到卡片类别的语义匹配。
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
        private BertTokenizer? _tokenizer;
        private bool _isReady;

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
                var modelPath = Path.Combine(assemblyDir, "Resources", "Models", "all-MiniLM-L6-v2.onnx");
                var vocabPath = Path.Combine(assemblyDir, "Resources", "Models", "vocab.txt");

                if (!File.Exists(modelPath))
                {
                    AiFlowLogger.Warn($"语义路由：ONNX 模型文件不存在 ({modelPath})，将回退到 LLM 分类模式。");
                    return;
                }

                // 加载 ONNX 推理会话（禁用多余日志）
                var sessionOptions = new SessionOptions();
                sessionOptions.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
                // 启用 DirectML 硬件加速（如有 GPU/NPU 可用）
                try { sessionOptions.AppendExecutionProvider_DML(0); } catch { /* CPU 回退 */ }

                _session = new InferenceSession(modelPath, sessionOptions);

                // 加载 BERT WordPiece 分词器
                if (File.Exists(vocabPath))
                {
                    _tokenizer = BertTokenizer.Create(vocabPath);
                }
                else
                {
                    AiFlowLogger.Warn("语义路由：vocab.txt 不存在，将使用内置分词逻辑。");
                }

                _isReady = true;
                AiFlowLogger.Info("语义路由：ONNX 模型已加载完成，语义路由已启用。");
            }
            catch (Exception ex)
            {
                AiFlowLogger.Warn($"语义路由初始化失败，将回退到 LLM 分类模式: {ex.Message}");
                _isReady = false;
            }
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
        /// <param name="threshold">相似度阈值（≥此值才认为相关，默认 0.30）</param>
        /// <param name="minCategories">至少返回几个类别（防止阈值过高导致空结果）</param>
        /// <returns>匹配的类别名称列表</returns>
        public List<string> Route(string userPrompt, float threshold = 0.30f, int minCategories = 1)
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

            // 截断到最多 512 个 token（BERT 上限）
            var (inputIds, attentionMask, tokenTypeIds) = Tokenize(text, maxLength: 512);

            // 构建输入 Tensor
            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { 1, tokenTypeIds.Length });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };

            using var outputs = _session.Run(inputs);

            // last_hidden_state shape: [1, seq_len, 384]
            // 取所有 token 向量的均值（Mean Pooling）
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

        private (long[] InputIds, long[] AttentionMask, long[] TokenTypeIds) Tokenize(string text, int maxLength)
        {
            if (_tokenizer != null)
            {
                // 使用 BertTokenizer（WordPiece）
                var encoding = _tokenizer.EncodeToIds(text);
                var ids = encoding.Take(maxLength - 2).ToList();

                // 添加 [CLS] 和 [SEP]
                var inputIds = new long[] { 101 }.Concat(ids.Select(id => (long)id)).Concat(new long[] { 102 }).ToArray();
                var attentionMask = Enumerable.Repeat(1L, inputIds.Length).ToArray();
                var tokenTypeIds = new long[inputIds.Length];

                // Padding 到 maxLength
                if (inputIds.Length < maxLength)
                {
                    int padLen = maxLength - inputIds.Length;
                    inputIds = inputIds.Concat(new long[padLen]).ToArray();
                    attentionMask = attentionMask.Concat(new long[padLen]).ToArray();
                    tokenTypeIds = new long[maxLength];
                }

                return (inputIds, attentionMask, tokenTypeIds);
            }
            else
            {
                // 最简单字符级分词回退（词汇表不存在时使用）
                var bytes = Encoding.UTF8.GetBytes(text.ToLower());
                int len = Math.Min(bytes.Length, maxLength - 2);
                var inputIds = new long[maxLength];
                var attentionMask = new long[maxLength];
                var tokenTypeIds = new long[maxLength];

                inputIds[0] = 101; // [CLS]
                for (int i = 0; i < len; i++)
                    inputIds[i + 1] = bytes[i] + 1000;
                inputIds[len + 1] = 102; // [SEP]

                for (int i = 0; i <= len + 1; i++)
                    attentionMask[i] = 1;

                return (inputIds, attentionMask, tokenTypeIds);
            }
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
