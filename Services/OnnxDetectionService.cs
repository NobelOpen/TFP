using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using TaskFlow.Models;

namespace TaskFlow.Services
{
    /// <summary>
    /// ONNX 目标检测结果
    /// </summary>
    public class OnnxDetectionResult
    {
        /// <summary>边界框中心 X 坐标（相对于原图）</summary>
        public int X { get; set; }
        /// <summary>边界框中心 Y 坐标（相对于原图）</summary>
        public int Y { get; set; }
        /// <summary>边界框宽度（相对于原图）</summary>
        public int Width { get; set; }
        /// <summary>边界框高度（相对于原图）</summary>
        public int Height { get; set; }
        /// <summary>类别名称</summary>
        public string ClassName { get; set; } = "";
        /// <summary>类别索引</summary>
        public int ClassIndex { get; set; }
        /// <summary>置信度</summary>
        public double Confidence { get; set; }
    }

    /// <summary>
    /// 基于 ONNX Runtime 的 YOLO 目标检测推理服务
    /// </summary>
    public class OnnxDetectionService : IDisposable
    {
        /// <summary>推理会话缓存（Key=模型文件路径），避免每次推理重新加载模型</summary>
        private readonly ConcurrentDictionary<string, InferenceSession> _sessionCache = new();

        /// <summary>
        /// 对图像执行 YOLO 目标检测
        /// </summary>
        /// <param name="source">源图像（OpenCV Mat，BGR 格式）</param>
        /// <param name="config">ONNX 模型配置</param>
        /// <returns>检测到的目标列表</returns>
        public List<OnnxDetectionResult> Detect(Mat source, OnnxModelConfig config)
        {
            if (source == null || source.Empty())
                throw new ArgumentException("源图像为空");

            if (!System.IO.File.Exists(config.FilePath))
                throw new System.IO.FileNotFoundException($"模型文件不存在: {config.FilePath}");

            // 获取或创建推理会话
            var session = GetOrCreateSession(config.FilePath);

            // 获取输入名称
            var inputName = session.InputMetadata.Keys.First();
            var inputMeta = session.InputMetadata[inputName];

            int inputW = config.InputWidth;
            int inputH = config.InputHeight;

            // 预处理：将图像调整为模型输入尺寸并归一化
            float ratioW = (float)source.Width / inputW;
            float ratioH = (float)source.Height / inputH;

            using var resized = new Mat();
            Cv2.Resize(source, resized, new OpenCvSharp.Size(inputW, inputH));

            // 转换为 float32 张量 [1, 3, H, W]（NCHW 格式，RGB 归一化到 0~1）
            var tensor = new DenseTensor<float>(new[] { 1, 3, inputH, inputW });

            unsafe
            {
                var data = resized.DataPointer;
                int channels = resized.Channels();
                int step = (int)resized.Step();

                for (int y = 0; y < inputH; y++)
                {
                    byte* row = (byte*)data + y * step;
                    for (int x = 0; x < inputW; x++)
                    {
                        int offset = x * channels;
                        // OpenCV 是 BGR，YOLO 期望 RGB
                        tensor[0, 0, y, x] = row[offset + 2] / 255f; // R
                        tensor[0, 1, y, x] = row[offset + 1] / 255f; // G
                        tensor[0, 2, y, x] = row[offset + 0] / 255f; // B
                    }
                }
            }

            // 运行推理
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, tensor)
            };

            using var results = session.Run(inputs);
            var output = results.First();
            var outputTensor = output.AsTensor<float>();

            // 解析 YOLO 输出并应用 NMS
            var detections = ParseYoloOutput(outputTensor, config, ratioW, ratioH);

            return detections;
        }

        /// <summary>
        /// 解析 YOLO 输出张量（支持 YOLOv8/v11 的两种常见格式）
        /// </summary>
        private List<OnnxDetectionResult> ParseYoloOutput(
            Tensor<float> output, OnnxModelConfig config,
            float ratioW, float ratioH)
        {
            var labels = config.ClassLabelArray;
            var candidates = new List<OnnxDetectionResult>();
            var dims = output.Dimensions.ToArray();

            if (dims.Length == 3 && dims[0] == 1)
            {
                int dim1 = dims[1]; // 行数
                int dim2 = dims[2]; // 列数

                // YOLOv8/v11 标准格式：[1, 4+numClasses, numBoxes]
                // 其中 dim1 = 4 + numClasses，dim2 = numBoxes (如 8400)
                if (dim1 > dim2 || (dim1 >= 5 && dim2 > 100))
                {
                    // 格式 A：[1, 4+C, N] — 每列是一个检测框
                    int numClasses = dim1 - 4;
                    int numBoxes = dim2;

                    for (int i = 0; i < numBoxes; i++)
                    {
                        // 找到最高置信度的类别
                        int bestClass = 0;
                        float bestScore = 0;

                        for (int c = 0; c < numClasses; c++)
                        {
                            float score = output[0, 4 + c, i];
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestClass = c;
                            }
                        }

                        if (bestScore < config.ConfidenceThreshold) continue;

                        // 提取中心坐标和宽高（相对于输入尺寸）
                        float cx = output[0, 0, i];
                        float cy = output[0, 1, i];
                        float w = output[0, 2, i];
                        float h = output[0, 3, i];

                        candidates.Add(new OnnxDetectionResult
                        {
                            X = (int)(cx * ratioW),
                            Y = (int)(cy * ratioH),
                            Width = (int)(w * ratioW),
                            Height = (int)(h * ratioH),
                            ClassIndex = bestClass,
                            ClassName = bestClass < labels.Length ? labels[bestClass] : $"class_{bestClass}",
                            Confidence = Math.Round(bestScore, 4)
                        });
                    }
                }
                else
                {
                    // 格式 B：[1, N, 4+C] 或 [1, N, 6]（某些导出格式）
                    // 每行是一个检测框
                    int numBoxes = dim1;
                    int cols = dim2;

                    if (cols == 6)
                    {
                        // [x1, y1, x2, y2, confidence, classIndex]
                        for (int i = 0; i < numBoxes; i++)
                        {
                            float conf = output[0, i, 4];
                            if (conf < config.ConfidenceThreshold) continue;

                            float x1 = output[0, i, 0] * ratioW;
                            float y1 = output[0, i, 1] * ratioH;
                            float x2 = output[0, i, 2] * ratioW;
                            float y2 = output[0, i, 3] * ratioH;
                            int classIdx = (int)output[0, i, 5];

                            candidates.Add(new OnnxDetectionResult
                            {
                                X = (int)((x1 + x2) / 2),
                                Y = (int)((y1 + y2) / 2),
                                Width = (int)(x2 - x1),
                                Height = (int)(y2 - y1),
                                ClassIndex = classIdx,
                                ClassName = classIdx < labels.Length ? labels[classIdx] : $"class_{classIdx}",
                                Confidence = Math.Round(conf, 4)
                            });
                        }
                    }
                    else
                    {
                        // [cx, cy, w, h, class0_score, class1_score, ...]
                        int numClasses = cols - 4;
                        for (int i = 0; i < numBoxes; i++)
                        {
                            int bestClass = 0;
                            float bestScore = 0;
                            for (int c = 0; c < numClasses; c++)
                            {
                                float score = output[0, i, 4 + c];
                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    bestClass = c;
                                }
                            }

                            if (bestScore < config.ConfidenceThreshold) continue;

                            float cx = output[0, i, 0] * ratioW;
                            float cy = output[0, i, 1] * ratioH;
                            float w = output[0, i, 2] * ratioW;
                            float h = output[0, i, 3] * ratioH;

                            candidates.Add(new OnnxDetectionResult
                            {
                                X = (int)cx,
                                Y = (int)cy,
                                Width = (int)w,
                                Height = (int)h,
                                ClassIndex = bestClass,
                                ClassName = bestClass < labels.Length ? labels[bestClass] : $"class_{bestClass}",
                                Confidence = Math.Round(bestScore, 4)
                            });
                        }
                    }
                }
            }

            // 按置信度降序排序
            candidates.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

            // NMS 去重
            return ApplyNms(candidates, config.IouThreshold);
        }

        /// <summary>
        /// 非极大值抑制（NMS）
        /// </summary>
        private List<OnnxDetectionResult> ApplyNms(List<OnnxDetectionResult> candidates, double iouThreshold)
        {
            var accepted = new List<OnnxDetectionResult>();

            foreach (var c in candidates)
            {
                bool overlaps = false;
                foreach (var a in accepted)
                {
                    if (a.ClassIndex == c.ClassIndex && CalculateIou(a, c) > iouThreshold)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps)
                {
                    accepted.Add(c);
                }
            }

            return accepted;
        }

        /// <summary>
        /// 计算两个边界框的 IoU
        /// </summary>
        private double CalculateIou(OnnxDetectionResult a, OnnxDetectionResult b)
        {
            int ax1 = a.X - a.Width / 2, ay1 = a.Y - a.Height / 2;
            int ax2 = a.X + a.Width / 2, ay2 = a.Y + a.Height / 2;
            int bx1 = b.X - b.Width / 2, by1 = b.Y - b.Height / 2;
            int bx2 = b.X + b.Width / 2, by2 = b.Y + b.Height / 2;

            int ix1 = Math.Max(ax1, bx1), iy1 = Math.Max(ay1, by1);
            int ix2 = Math.Min(ax2, bx2), iy2 = Math.Min(ay2, by2);

            int interW = Math.Max(0, ix2 - ix1);
            int interH = Math.Max(0, iy2 - iy1);
            double interArea = interW * interH;

            double aArea = a.Width * a.Height;
            double bArea = b.Width * b.Height;
            double unionArea = aArea + bArea - interArea;

            return unionArea > 0 ? interArea / unionArea : 0;
        }

        /// <summary>
        /// 获取或创建推理会话（带缓存）
        /// </summary>
        private InferenceSession GetOrCreateSession(string modelPath)
        {
            return _sessionCache.GetOrAdd(modelPath, path =>
            {
                var options = new SessionOptions();
                // 使用 CPU 推理（兼容所有设备）
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                return new InferenceSession(path, options);
            });
        }

        /// <summary>
        /// 在图像上绘制检测结果标注
        /// </summary>
        public Mat DrawDetections(Mat source, List<OnnxDetectionResult> detections)
        {
            var result = source.Clone();

            for (int i = 0; i < detections.Count; i++)
            {
                var det = detections[i];
                int x1 = det.X - det.Width / 2;
                int y1 = det.Y - det.Height / 2;

                // 画边界框
                Cv2.Rectangle(result,
                    new OpenCvSharp.Point(x1, y1),
                    new OpenCvSharp.Point(x1 + det.Width, y1 + det.Height),
                    new Scalar(0, 255, 0), 2);

                // 画中心点
                Cv2.Circle(result, new OpenCvSharp.Point(det.X, det.Y), 5, new Scalar(0, 0, 255), -1);

                // 标注类别和置信度
                string label = $"{det.ClassName} {det.Confidence:F2}";
                Cv2.PutText(result, label,
                    new OpenCvSharp.Point(x1, y1 - 8),
                    HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 0), 1);
            }

            return result;
        }

        /// <summary>
        /// 清除指定模型的推理会话缓存
        /// </summary>
        public void RemoveSessionCache(string modelPath)
        {
            if (_sessionCache.TryRemove(modelPath, out var session))
            {
                session.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (var session in _sessionCache.Values)
            {
                session.Dispose();
            }
            _sessionCache.Clear();
        }
    }
}
