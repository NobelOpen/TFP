using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace TaskFlow.Services
{
    public interface IOcrService
    {
        Task<(bool Success, string Text, System.Collections.Generic.List<TaskFlow.Models.TaskCards.OcrResultItem>? Items, string? Error)> RecognizeAsync(Mat image);
        void Initialize();
        void Dispose();
    }

    public class OcrService : IOcrService, IDisposable
    {
        private PaddleOcrAll? _ocrEngine;
        private readonly object _lock = new();
        private bool _isInitialized;

        public void Initialize()
        {
            lock (_lock)
            {
                if (_isInitialized) return;

                try
                {
                    // 使用本地模型
                    var model = LocalFullModels.ChineseV5;
                    _ocrEngine = new PaddleOcrAll(model)
                    {
                        AllowRotateDetection = true,
                        Enable180Classification = false
                    };
                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"OCR引擎初始化失败: {ex.Message}", ex);
                }
            }
        }

        public async Task<(bool Success, string Text, System.Collections.Generic.List<TaskFlow.Models.TaskCards.OcrResultItem>? Items, string? Error)> RecognizeAsync(Mat image)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!_isInitialized || _ocrEngine == null)
                    {
                        Initialize();
                    }

                    if (_ocrEngine == null)
                    {
                        return (false, string.Empty, null, "OCR引擎未初始化");
                    }

                    if (image == null || image.Empty())
                    {
                        return (false, string.Empty, null, "输入图像为空");
                    }

                    // 重试机制，应对PaddlePredictor偶发性失败
                    const int maxRetries = 3;
                    Exception? lastException = null;

                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            lock (_lock)
                            {
                                if (_ocrEngine == null)
                                    return (false, string.Empty, null, "OCR引擎未初始化");
                                var result = _ocrEngine.Run(image);
                                
                                var items = new System.Collections.Generic.List<TaskFlow.Models.TaskCards.OcrResultItem>();
                                if (result.Regions != null)
                                {
                                    foreach (var region in result.Regions)
                                    {
                                        items.Add(new TaskFlow.Models.TaskCards.OcrResultItem
                                        {
                                            Text = region.Text ?? string.Empty,
                                            X = (int)Math.Round(region.Rect.Center.X),
                                            Y = (int)Math.Round(region.Rect.Center.Y),
                                            Width = (int)Math.Round(region.Rect.Size.Width),
                                            Height = (int)Math.Round(region.Rect.Size.Height),
                                            Confidence = region.Score
                                        });
                                    }
                                }

                                return (true, result.Text, items, (string?)null);
                            }
                        }
                        catch (Exception ex) when (attempt < maxRetries)
                        {
                            lastException = ex;
                            // 短暂等待后重试
                            Thread.Sleep(200);

                            // 如果是PaddlePredictor错误，尝试重新初始化引擎
                            if (ex.Message.Contains("PaddlePredictor"))
                            {
                                lock (_lock)
                                {
                                    _ocrEngine?.Dispose();
                                    _ocrEngine = null;
                                    _isInitialized = false;
                                }
                                Initialize();
                            }
                        }
                    }

                    return (false, string.Empty, null, lastException?.Message ?? "OCR识别失败");
                }
                catch (Exception ex)
                {
                    return (false, string.Empty, null, ex.Message);
                }
            });
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _ocrEngine?.Dispose();
                _ocrEngine = null;
                _isInitialized = false;
            }
        }
    }
}
