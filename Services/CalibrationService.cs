using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Models.AiFlow;

namespace TaskFlow.Services
{
    /// <summary>
    /// Vision 模型坐标标定服务：生成标定图、多次采样、计算线性校正参数、本地存储
    /// </summary>
    public class CalibrationService
    {
        private static readonly string CalibrationFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskFlow", "calibration.json");

        private static readonly HttpClient _httpClient = new();

        /// <summary>标定采样次数</summary>
        private const int SampleCount = 3;

        /// <summary>标定图上标记点数量（3x3=9个）</summary>
        private static readonly double[] GridPositions = { 0.25, 0.5, 0.75 };

        private readonly Action<string>? _log;

        public CalibrationService(Action<string>? logCallback = null)
        {
            _log = logCallback;
        }

        #region 标定数据存储

        /// <summary>
        /// 加载所有标定数据
        /// </summary>
        public static List<CalibrationData> LoadAll()
        {
            try
            {
                if (File.Exists(CalibrationFilePath))
                {
                    var json = File.ReadAllText(CalibrationFilePath, Encoding.UTF8);
                    return JsonConvert.DeserializeObject<List<CalibrationData>>(json) ?? new();
                }
            }
            catch { }
            return new();
        }

        /// <summary>
        /// 保存所有标定数据
        /// </summary>
        public static void SaveAll(List<CalibrationData> data)
        {
            try
            {
                var dir = Path.GetDirectoryName(CalibrationFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(CalibrationFilePath, json, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// 获取指定模型和分辨率的标定数据（如果存在）
        /// </summary>
        public static CalibrationData? GetCalibration(string modelId, int width, int height)
        {
            var all = LoadAll();
            return all.FirstOrDefault(c =>
                c.ModelId == modelId && c.Width == width && c.Height == height);
        }

        /// <summary>
        /// 删除指定标定记录
        /// </summary>
        public static void DeleteCalibration(string key)
        {
            var all = LoadAll();
            all.RemoveAll(c => c.Key == key);
            SaveAll(all);
        }

        /// <summary>
        /// 清空所有标定数据
        /// </summary>
        public static void ClearAll()
        {
            SaveAll(new List<CalibrationData>());
        }

        #endregion

        #region 坐标校正

        /// <summary>
        /// 校正坐标：用标定数据将模型原始坐标转换为精确坐标
        /// </summary>
        public static (int x, int y) CalibrateCoordinates(CalibrationData cal, int rawX, int rawY)
        {
            int correctedX = (int)Math.Round(cal.ScaleX * rawX + cal.OffsetX);
            int correctedY = (int)Math.Round(cal.ScaleY * rawY + cal.OffsetY);

            // 限制在图像范围内
            correctedX = Math.Max(0, Math.Min(correctedX, cal.Width - 1));
            correctedY = Math.Max(0, Math.Min(correctedY, cal.Height - 1));

            return (correctedX, correctedY);
        }

        #endregion

        #region 标定执行

        /// <summary>
        /// 执行标定流程：生成标定图 → 多次采样 → 计算校正参数 → 保存
        /// </summary>
        public async Task<CalibrationData?> CalibrateAsync(
            string modelId, int width, int height, CancellationToken cancellationToken)
        {
            var modelConfig = LlmModelManager.Models.FirstOrDefault(m => m.Id == modelId);
            if (modelConfig == null)
            {
                _log?.Invoke($"[标定] 模型 {modelId} 不存在");
                return null;
            }

            _log?.Invoke($"[标定] 开始对模型 {modelConfig.DisplayName} 进行坐标标定 (分辨率: {width}x{height})");

            // 1. 生成标定图
            using var calibrationImage = GenerateCalibrationImage(width, height);
            byte[] pngBytes = calibrationImage.ToBytes(".png");
            string base64Image = Convert.ToBase64String(pngBytes);
            string imageDataUrl = $"data:image/png;base64,{base64Image}";

            // 2. 生成真实坐标列表
            var truePoints = GenerateGridPoints(width, height);

            // 3. 多次采样
            var allSamples = new List<List<(int x, int y)>>();
            for (int sample = 0; sample < SampleCount; sample++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _log?.Invoke($"[标定] 第 {sample + 1}/{SampleCount} 次采样...");

                var points = await AskModelForCoordinatesAsync(
                    modelConfig, imageDataUrl, truePoints.Count, width, height, cancellationToken);

                if (points != null && points.Count == truePoints.Count)
                {
                    allSamples.Add(points);
                }
                else
                {
                    _log?.Invoke($"[标定] 第 {sample + 1} 次采样失败，跳过");
                }
            }

            if (allSamples.Count == 0)
            {
                _log?.Invoke("[标定] 所有采样均失败，标定中止");
                return null;
            }

            // 4. 取平均值
            var avgPoints = new List<(double x, double y)>();
            for (int i = 0; i < truePoints.Count; i++)
            {
                double avgX = allSamples.Average(s => s[i].x);
                double avgY = allSamples.Average(s => s[i].y);
                avgPoints.Add((avgX, avgY));
            }

            // 5. 最小二乘法计算线性校正参数
            var cal = ComputeLinearCalibration(modelId, width, height, truePoints, avgPoints, allSamples.Count);

            // 6. 保存
            var all = LoadAll();
            all.RemoveAll(c => c.Key == cal.Key);
            all.Add(cal);
            SaveAll(all);

            _log?.Invoke($"[标定] 完成！ScaleX={cal.ScaleX:F4}, ScaleY={cal.ScaleY:F4}, " +
                        $"OffsetX={cal.OffsetX:F1}, OffsetY={cal.OffsetY:F1}, 平均误差={cal.AvgError:F1}px");

            return cal;
        }

        /// <summary>
        /// 获取标定数据，如果没有则自动执行标定
        /// </summary>
        public async Task<CalibrationData?> GetOrCalibrateAsync(
            string modelId, int width, int height, CancellationToken cancellationToken)
        {
            var existing = GetCalibration(modelId, width, height);
            if (existing != null) return existing;
            return await CalibrateAsync(modelId, width, height, cancellationToken);
        }

        #endregion

        #region 标定图生成

        /// <summary>
        /// 生成纯色背景的标定图，上面画 9 个标记点（十字线+坐标）
        /// </summary>
        private static Mat GenerateCalibrationImage(int width, int height)
        {
            // 纯白背景
            var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(255, 255, 255));

            // 3x3 网格的标记点
            var points = GenerateGridPoints(width, height);

            foreach (var (px, py) in points)
            {
                int crossSize = Math.Min(width, height) / 30;

                // 画十字线（红色）
                Cv2.Line(image, new OpenCvSharp.Point(px - crossSize, py),
                    new OpenCvSharp.Point(px + crossSize, py), new Scalar(0, 0, 200), 2);
                Cv2.Line(image, new OpenCvSharp.Point(px, py - crossSize),
                    new OpenCvSharp.Point(px, py + crossSize), new Scalar(0, 0, 200), 2);

                // 标注坐标文字（黑色）
                string label = $"({px},{py})";
                double fontScale = Math.Min(width, height) / 1200.0;
                fontScale = Math.Max(0.4, Math.Min(fontScale, 1.0));
                Cv2.PutText(image, label, new OpenCvSharp.Point(px + 8, py - 8),
                    HersheyFonts.HersheySimplex, fontScale, new Scalar(0, 0, 0), 1);
            }

            // 在图像顶部写标题
            string title = $"Calibration Grid {width}x{height}";
            Cv2.PutText(image, title, new OpenCvSharp.Point(10, 25),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(100, 100, 100), 1);

            return image;
        }

        /// <summary>
        /// 生成 3x3 网格的坐标点（位于 1/4、1/2、3/4 位置）
        /// </summary>
        private static List<(int x, int y)> GenerateGridPoints(int width, int height)
        {
            var points = new List<(int x, int y)>();
            foreach (double gy in GridPositions)
            {
                foreach (double gx in GridPositions)
                {
                    points.Add(((int)(width * gx), (int)(height * gy)));
                }
            }
            return points;
        }

        #endregion

        #region AI 坐标识别

        /// <summary>
        /// 让模型识别标定图上所有标记点的坐标
        /// </summary>
        private async Task<List<(int x, int y)>?> AskModelForCoordinatesAsync(
            LlmModelConfig modelConfig, string imageDataUrl,
            int expectedCount, int imageWidth, int imageHeight,
            CancellationToken cancellationToken)
        {
            string prompt = $"这是一张 {imageWidth}x{imageHeight} 的标定图，包含 {expectedCount} 个红色十字标记点。" +
                           $"请逐个报告每个标记点的中心像素坐标。" +
                           $"严格按以下 JSON 格式回复，不要添加任何其他文字：\n" +
                           $"{{\"points\": [[x1,y1], [x2,y2], ...]}}";

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(modelConfig.TimeoutSeconds > 0 ? modelConfig.TimeoutSeconds : 60));

                var requestBody = new
                {
                    model = modelConfig.ModelName,
                    messages = new object[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = prompt },
                                new { type = "image_url", image_url = new { url = imageDataUrl } }
                            }
                        }
                    },
                    temperature = 0.0,
                    max_tokens = 512
                };

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, modelConfig.ApiEndpoint);
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", modelConfig.ApiKey);
                requestMessage.Content = new StringContent(
                    JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(requestMessage, cts.Token);
                string responseString = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode) return null;

                var jsonResponse = JObject.Parse(responseString);
                var replyText = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString();
                if (string.IsNullOrEmpty(replyText)) return null;

                // 解析 JSON 坐标
                return ParseCoordinates(replyText);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[标定] API 调用异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解析模型返回的坐标 JSON
        /// </summary>
        private static List<(int x, int y)>? ParseCoordinates(string response)
        {
            try
            {
                // 提取 JSON（模型可能在 JSON 前后加其他文字）
                int start = response.IndexOf('{');
                int end = response.LastIndexOf('}');
                if (start < 0 || end < 0 || end <= start) return null;

                string jsonPart = response.Substring(start, end - start + 1);
                var json = JObject.Parse(jsonPart);
                var pointsArray = json["points"] as JArray;
                if (pointsArray == null) return null;

                var result = new List<(int x, int y)>();
                foreach (var p in pointsArray)
                {
                    if (p is JArray coords && coords.Count >= 2)
                    {
                        result.Add(((int)coords[0], (int)coords[1]));
                    }
                }
                return result.Count > 0 ? result : null;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region 校正参数计算

        /// <summary>
        /// 最小二乘法计算线性校正参数：corrected = scale * raw + offset
        /// </summary>
        private static CalibrationData ComputeLinearCalibration(
            string modelId, int width, int height,
            List<(int x, int y)> truePoints,
            List<(double x, double y)> modelPoints,
            int sampleCount)
        {
            int n = truePoints.Count;

            // X 轴：trueX = scaleX * modelX + offsetX
            double sumMx = 0, sumTx = 0, sumMxMx = 0, sumMxTx = 0;
            double sumMy = 0, sumTy = 0, sumMyMy = 0, sumMyTy = 0;

            for (int i = 0; i < n; i++)
            {
                double mx = modelPoints[i].x, tx = truePoints[i].x;
                double my = modelPoints[i].y, ty = truePoints[i].y;

                sumMx += mx; sumTx += tx; sumMxMx += mx * mx; sumMxTx += mx * tx;
                sumMy += my; sumTy += ty; sumMyMy += my * my; sumMyTy += my * ty;
            }

            // 求解线性方程组: scale = (n*sumMT - sumM*sumT) / (n*sumMM - sumM*sumM)
            double detX = n * sumMxMx - sumMx * sumMx;
            double detY = n * sumMyMy - sumMy * sumMy;

            double scaleX = detX != 0 ? (n * sumMxTx - sumMx * sumTx) / detX : 1.0;
            double offsetX = detX != 0 ? (sumTx - scaleX * sumMx) / n : 0.0;

            double scaleY = detY != 0 ? (n * sumMyTy - sumMy * sumTy) / detY : 1.0;
            double offsetY = detY != 0 ? (sumTy - scaleY * sumMy) / n : 0.0;

            // 计算平均误差
            double totalError = 0;
            for (int i = 0; i < n; i++)
            {
                double corrX = scaleX * modelPoints[i].x + offsetX;
                double corrY = scaleY * modelPoints[i].y + offsetY;
                double err = Math.Sqrt(
                    Math.Pow(corrX - truePoints[i].x, 2) +
                    Math.Pow(corrY - truePoints[i].y, 2));
                totalError += err;
            }

            return new CalibrationData
            {
                ModelId = modelId,
                Width = width,
                Height = height,
                ScaleX = Math.Round(scaleX, 6),
                ScaleY = Math.Round(scaleY, 6),
                OffsetX = Math.Round(offsetX, 2),
                OffsetY = Math.Round(offsetY, 2),
                CalibratedAt = DateTime.Now,
                AvgError = Math.Round(totalError / n, 2),
                SampleCount = sampleCount
            };
        }

        #endregion
    }
}
