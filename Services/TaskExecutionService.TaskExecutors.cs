using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenCvSharp;
using TaskFlow.Helpers;
using TaskFlow.Models;
using TaskFlow.Resources;
using TaskFlow.Models.TaskCards;
using TaskStatus = TaskFlow.Models.TaskCards.TaskStatus;

namespace TaskFlow.Services
{
    // ADB / 图像处理 / 字符串&数据类型 执行器
    public partial class TaskExecutionService
    {
        #region ADB Executors

        private async Task<bool> ExecuteAdbLaunchAppAsync(AdbLaunchAppTaskCard task)
        {
            var result = await _adbService.LaunchAppAsync(task.DeviceSerial, task.PackageName, task.ActivityName);
            if (!result.Success)
            {
                task.ErrorMessage = result.Message;
            }
            return result.Success;
        }

        private async Task<bool> ExecuteAdbCloseAppAsync(AdbCloseAppTaskCard task)
        {
            var result = await _adbService.ForceStopAppAsync(task.DeviceSerial, task.PackageName);
            if (!result.Success)
            {
                task.ErrorMessage = result.Message;
            }
            return result.Success;
        }

        private async Task<bool> ExecuteAdbDisconnectAsync(AdbDisconnectTaskCard task)
        {
            var result = await _adbService.DisconnectAsync(task.DeviceSerial);
            if (!result.Success)
            {
                task.ErrorMessage = result.Message;
            }
            Log($"[{DateTime.Now:HH:mm:ss}] ADB断开设备: {task.DeviceSerial} - {result.Message}");
            return result.Success;
        }

        private async Task<bool> ExecuteAdbScreenshotAsync(AdbScreenshotTaskCard task)
        {
            var result = await _adbService.ScreenshotAsync(task.DeviceSerial);
            if (result.Success && result.Image != null)
            {
                task.OutputImage?.Dispose();
                task.OutputImage = ApplyGrayscaleIfNeeded(result.Image, task.ConvertToGrayscale);
                return true;
            }
            task.ErrorMessage = "ADB截屏失败";
            return false;
        }

        private async Task<bool> ExecuteAdbClickAsync(AdbClickTaskCard task, IList<TaskCardBase> allTasks)
        {
            int x = task.StartX;
            int y = task.StartY;

            // 解析 X/Y 坐标表达式
            if (task.UseVariableCoordinates)
            {
                if (!ResolveCoordinateExpression(task.StartXExpression, "X", ref x, task, allTasks)) return false;
                if (!ResolveCoordinateExpression(task.StartYExpression, "Y", ref y, task, allTasks)) return false;
            }

            if (task.UseSourceTaskCoordinates && task.SourceTaskIdForCoordinates.HasValue)
            {
                var sourceTask = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForCoordinates.Value);
                if (sourceTask?.OutputX != null && sourceTask?.OutputY != null)
                {
                    x = sourceTask.OutputX.Value;
                    y = sourceTask.OutputY.Value;
                }
            }

            (bool success, string message) result = task.ClickType switch
            {
                ClickType.Single => await _adbService.ClickAsync(task.DeviceSerial, x, y),
                ClickType.Double => await ExecuteAdbMultiClickAsync(task, x, y),
                ClickType.Swipe => await _adbService.SwipeAsync(task.DeviceSerial, x, y, task.EndX, task.EndY, task.SwipeDurationMs),
                _ => (false, "未知类型")
            };

            if (result.success)
            {
                task.OutputX = x;
                task.OutputY = y;
                task.OutputText = $"已点击坐标: ({x}, {y})";
                Log($"[{DateTime.Now:HH:mm:ss}] ADB点击成功: ({x}, {y})");
            }
            else
            {
                task.ErrorMessage = result.message;
            }
            return result.success;
        }

        private Task<bool> ExecuteImgCropAsync(ImgCropTaskCard task, IList<TaskCardBase> allTasks)
        {
            // 解析 ROI 表达式
            int roiX = task.RoiX, roiY = task.RoiY, roiW = task.RoiWidth, roiH = task.RoiHeight;
            if (!ResolveCoordinateExpression(task.RoiXExpression, "ROI X", ref roiX, task, allTasks)) return Task.FromResult(false);
            if (!ResolveCoordinateExpression(task.RoiYExpression, "ROI Y", ref roiY, task, allTasks)) return Task.FromResult(false);
            if (!ResolveCoordinateExpression(task.RoiWidthExpression, Strings.Svc_RoiWidth, ref roiW, task, allTasks)) return Task.FromResult(false);
            if (!ResolveCoordinateExpression(task.RoiHeightExpression, Strings.Svc_RoiHeight, ref roiH, task, allTasks)) return Task.FromResult(false);

            // 将解析后的值回写，确保预览窗口显示的 ROI 位置与实际裁剪位置一致
            task.RoiX = roiX; task.RoiY = roiY;
            task.RoiWidth = roiW; task.RoiHeight = roiH;

            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);

            if (sourceImage == null)
            {
                task.ErrorMessage = "无法获取源图像";
                return Task.FromResult(false);
            }

            try
            {
                var croppedImage = _openCVService.CropImage(sourceImage, roiX, roiY, roiW, roiH);
                task.OutputImage?.Dispose();
                task.OutputImage = croppedImage;
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return Task.FromResult(false);
            }
            finally
            {
                // 从文件加载的Mat需要释放，引用其他任务的不能释放
                if (shouldDispose) sourceImage.Dispose();
            }
        }

        private Task<bool> ExecuteImgTemplateMatchAsync(ImgTemplateMatchTaskCard task, IList<TaskCardBase> allTasks)
        {
            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);

            if (sourceImage == null)
            {
                task.ErrorMessage = "无法获取源图像";
                return Task.FromResult(false);
            }

            // 加载模板图像：支持动态模板（引用其他任务输出）和静态模板（文件路径）
            Mat? templateImage = null;
            bool shouldDisposeTemplate = true;
            if (task.UseSourceTaskTemplate && task.SourceTaskIdForTemplate.HasValue)
            {
                // 从其他任务的输出图像获取动态模板
                var templateTask = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForTemplate.Value);
                if (templateTask?.OutputImage != null && !templateTask.OutputImage.Empty())
                {
                    templateImage = templateTask.OutputImage;
                    shouldDisposeTemplate = false; // 引用其他任务的Mat不能释放
                }
            }
            else if (!string.IsNullOrEmpty(task.TemplateImagePath) && System.IO.File.Exists(task.TemplateImagePath))
            {
                templateImage = Cv2.ImRead(task.TemplateImagePath);
            }

            if (templateImage == null || templateImage.Empty())
            {
                if (shouldDisposeTemplate) templateImage?.Dispose();
                if (shouldDispose) sourceImage.Dispose();
                task.ErrorMessage = "无法加载模板图像";
                return Task.FromResult(false);
            }

            // 先应用掩膜（掩膜基于全尺寸图像）
            Mat? maskedImage = ApplyMask(sourceImage, task.MaskImagePath);
            Mat imageAfterMask = maskedImage ?? sourceImage;

            // 再裁剪ROI
            Mat? croppedImage = null;
            int roiOffsetX = 0, roiOffsetY = 0;
            if (task.RoiWidth > 0 && task.RoiHeight > 0)
            {
                croppedImage = _openCVService.CropImage(imageAfterMask, task.RoiX, task.RoiY, task.RoiWidth, task.RoiHeight);
                roiOffsetX = task.RoiX;
                roiOffsetY = task.RoiY;
            }
            Mat imageToMatch = croppedImage ?? imageAfterMask;

            try
            {
                if (task.MaxMatchCount > 1)
                {
                    // 多目标匹配
                    var matches = _openCVService.TemplateMatchMulti(imageToMatch, templateImage, task.MatchThreshold, task.MaxMatchCount);

                    task.OutputMatchCount = matches.Count;
                    task.OutputResult = matches.Count > 0;

                    // 调整坐标加回ROI偏移量
                    foreach (var m in matches)
                    {
                        m.X += roiOffsetX;
                        m.Y += roiOffsetY;
                    }
                    task.OutputMatchResults = matches;

                    if (matches.Count > 0)
                    {
                        task.OutputX = matches[0].X;
                        task.OutputY = matches[0].Y;
                        task.OutputMatchScore = matches[0].Score;

                        // 在源图上标记所有匹配位置
                        var resultImage = sourceImage.Clone();
                        int tw = templateImage.Width;
                        int th = templateImage.Height;
                        for (int i = 0; i < matches.Count; i++)
                        {
                            int mx = matches[i].X - roiOffsetX;
                            int my = matches[i].Y - roiOffsetY;
                            // 如果有ROI偏移，画到源图上需加回
                            int drawX = matches[i].X;
                            int drawY = matches[i].Y;
                            Cv2.Rectangle(resultImage,
                                new OpenCvSharp.Point(drawX - tw / 2, drawY - th / 2),
                                new OpenCvSharp.Point(drawX + tw / 2, drawY + th / 2),
                                new Scalar(0, 255, 0), 2);
                            Cv2.Circle(resultImage, new OpenCvSharp.Point(drawX, drawY), 5, new Scalar(0, 0, 255), -1);
                            // 标注编号
                            Cv2.PutText(resultImage, $"#{i + 1}", new OpenCvSharp.Point(drawX + 8, drawY - 8),
                                HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 0), 2);
                        }
                        task.OutputImage?.Dispose();
                        task.OutputImage = resultImage;

                        Log($"[{DateTime.Now:HH:mm:ss}] 多目标模板匹配: 找到 {matches.Count} 个匹配");
                    }
                    else
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] 多目标模板匹配: 未找到匹配");
                    }
                }
                else
                {
                    // 单目标匹配（原有逻辑）
                    var result = _openCVService.TemplateMatch(imageToMatch, templateImage, task.MatchThreshold);

                    task.OutputResult = result.Success;
                    task.OutputMatchScore = result.MaxVal;
                    task.OutputMatchCount = result.Success ? 1 : 0;
                    task.OutputMatchResults = result.Success
                        ? new List<MatchResult> { new MatchResult { X = result.CenterX + roiOffsetX, Y = result.CenterY + roiOffsetY, Score = Math.Round(result.MaxVal, 4) } }
                        : new List<MatchResult>();

                    if (result.Success)
                    {
                        task.OutputX = result.CenterX + roiOffsetX;
                        task.OutputY = result.CenterY + roiOffsetY;
                        task.OutputImage?.Dispose();
                        task.OutputImage = result.ResultImage;
                        Log($"[{DateTime.Now:HH:mm:ss}] 模板匹配成功: 阈值={result.MaxVal:F4}, X={task.OutputX}, Y={task.OutputY}");
                    }
                    else
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] 模板匹配失败: 最大值={result.MaxVal:F4}");
                    }
                }

                if (shouldDisposeTemplate) templateImage.Dispose();
                return Task.FromResult(task.OutputResult.GetValueOrDefault());
            }
            catch (Exception ex)
            {
                if (shouldDisposeTemplate) templateImage?.Dispose();
                task.ErrorMessage = ex.Message;
                return Task.FromResult(false);
            }
            finally
            {
                maskedImage?.Dispose();
                croppedImage?.Dispose();
                if (shouldDispose) sourceImage.Dispose();
            }
        }

        /// <summary>
        /// 清理OCR识别结果：去除换行和空格
        /// </summary>
        private static string CleanOcrText(string text)
        {
            return text
                .Replace("\r\n", "").Replace("\n", "").Replace("\r", "")
                .Replace(" ", "").Replace("　", ""); // 全角空格也去除
        }

        private async Task<bool> ExecuteImgOcrAsync(ImgOcrTaskCard task, IList<TaskCardBase> allTasks)
        {
            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);

            if (sourceImage == null)
            {
                task.ErrorMessage = "无法获取源图像";
                return false;
            }

            // 先应用掩膜（掩膜基于全尺寸图像）
            Mat? maskedImage = ApplyMask(sourceImage, task.MaskImagePath);
            Mat imageAfterMask = maskedImage ?? sourceImage;

            // 再裁剪ROI
            Mat? croppedImage = null;
            if (task.RoiWidth > 0 && task.RoiHeight > 0)
            {
                croppedImage = _openCVService.CropImage(imageAfterMask, task.RoiX, task.RoiY, task.RoiWidth, task.RoiHeight);
            }
            Mat imageToOcr = croppedImage ?? imageAfterMask;

            try
            {
                // 根据选择的引擎执行 OCR
                (bool Success, string Text, System.Collections.Generic.List<TaskFlow.Models.TaskCards.OcrResultItem>? Items, string? Error) result;
                if (task.OcrEngine == OcrEngine.WeChatOCR && _weChatOcrService.IsAvailable)
                {
                    result = await _weChatOcrService.RecognizeAsync(imageToOcr);
                    Log($"[{DateTime.Now:HH:mm:ss}] 使用微信 OCR 引擎");
                }
                else
                {
                    if (task.OcrEngine == OcrEngine.WeChatOCR)
                        Log($"[{DateTime.Now:HH:mm:ss}] 微信 OCR 不可用，回退到 PaddleOCR");
                    result = await _ocrService.RecognizeAsync(imageToOcr);
                }

                if (result.Success)
                {
                    var cleanedText = CleanOcrText(result.Text);
                    task.OutputText = cleanedText;
                    
                    if (result.Items != null)
                    {
                        var adjustedItems = new System.Collections.Generic.List<TaskFlow.Models.TaskCards.OcrResultItem>();
                        foreach (var item in result.Items)
                        {
                            item.X += task.RoiX;
                            item.Y += task.RoiY;
                            adjustedItems.Add(item);
                        }
                        task.OutputOcrResults = adjustedItems;
                        task.OutputResultCount = adjustedItems.Count;
                    }
                    else
                    {
                        task.OutputOcrResults = new();
                        task.OutputResultCount = 0;
                    }
                    
                    Log($"[{DateTime.Now:HH:mm:ss}] OCR识别结果: {cleanedText}");

                    if (task.CheckContainsText && !string.IsNullOrEmpty(task.TargetText))
                    {
                        task.OutputResult = cleanedText.Contains(task.TargetText);
                    }
                    else
                    {
                        task.OutputResult = true;
                    }

                    return true;
                }

                task.ErrorMessage = Strings.Svc_OcrFailed;
                return false;
            }
            finally
            {
                maskedImage?.Dispose();
                croppedImage?.Dispose();
                if (shouldDispose) sourceImage.Dispose();
            }
        }

        /// <summary>
        /// 应用掩膜到图像：将掩膜黑色区域在源图上置黑
        /// </summary>
        private static Mat? ApplyMask(Mat source, string? maskPath)
        {
            if (string.IsNullOrEmpty(maskPath) || !System.IO.File.Exists(maskPath))
                return null;

            try
            {
                using var mask = Cv2.ImRead(maskPath, OpenCvSharp.ImreadModes.Grayscale);
                if (mask == null || mask.Empty()) return null;
                if (mask.Width != source.Width || mask.Height != source.Height) return null;

                var result = new Mat();
                Cv2.BitwiseAnd(source, source, result, mask);
                return result;
            }
            catch
            {
                return null;
            }
        }

        private Task<bool> ExecuteImgColorDetectAsync(ImgColorDetectTaskCard task, IList<TaskCardBase> allTasks)
        {
            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);

            if (sourceImage == null)
            {
                task.ErrorMessage = "无法获取源图像";
                return Task.FromResult(false);
            }

            // 跟踪需要释放的ROI裁剪图像
            Mat? croppedImage = null;
            try
            {
                // 如果设置了ROI区域，先裁剪
                if (task.RoiWidth > 0 && task.RoiHeight > 0)
                {
                    croppedImage = _openCVService.CropImage(sourceImage, task.RoiX, task.RoiY, task.RoiWidth, task.RoiHeight);
                    if (croppedImage != null)
                    {
                        sourceImage = croppedImage;
                    }
                }

                var result = _openCVService.DetectHsvColor(
                    sourceImage,
                    task.HsvLowerH, task.HsvLowerS, task.HsvLowerV,
                    task.HsvUpperH, task.HsvUpperS, task.HsvUpperV);

                task.OutputMeanH = result.MeanH;
                task.OutputMeanS = result.MeanS;
                task.OutputMeanV = result.MeanV;
                task.OutputMatchRatio = result.MatchRatio;

                // 判断是否匹配（匹配像素占比 > 0）
                task.OutputResult = result.MatchRatio > 0;

                if (result.MaskImage != null)
                {
                    task.OutputImage?.Dispose();
                    task.OutputImage = result.MaskImage;
                }

                Log($"[{DateTime.Now:HH:mm:ss}] 颜色识别结果: 平均HSV=({result.MeanH}, {result.MeanS}, {result.MeanV}), " +
                    $"范围内像素占比={result.MatchRatio:P2}");

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return Task.FromResult(false);
            }
            finally
            {
                // 释放ROI裁剪产生的临时Mat
                croppedImage?.Dispose();
                // 从文件加载的Mat需要释放
                if (shouldDispose) sourceImage?.Dispose();
            }
        }

        private Task<bool> ExecuteImgColorSegmentAsync(ImgColorSegmentTaskCard task, IList<TaskCardBase> allTasks)
        {
            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);

            if (sourceImage == null)
            {
                task.ErrorMessage = "无法获取源图像";
                return Task.FromResult(false);
            }

            try
            {
                var result = _openCVService.SegmentByHsvColor(
                    sourceImage,
                    task.HsvLowerH, task.HsvLowerS, task.HsvLowerV,
                    task.HsvUpperH, task.HsvUpperS, task.HsvUpperV);

                if (result == null)
                {
                    task.ErrorMessage = Strings.Svc_ColorSegFailed;
                    return Task.FromResult(false);
                }

                task.OutputImage?.Dispose();
                task.OutputImage = result;

                Log($"[{DateTime.Now:HH:mm:ss}] 颜色分割完成: HSV范围=({task.HsvLowerH},{task.HsvLowerS},{task.HsvLowerV})-({task.HsvUpperH},{task.HsvUpperS},{task.HsvUpperV})");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return Task.FromResult(false);
            }
            finally
            {
                // 从文件加载的Mat需要释放
                if (shouldDispose) sourceImage?.Dispose();
            }
        }

        private bool ExecuteImgPreprocess(ImgPreprocessTaskCard task, IList<TaskCardBase> allTasks)
        {
            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);

            if (sourceImage == null)
            {
                task.ErrorMessage = "无法获取源图像";
                return false;
            }

            try
            {
                var result = _openCVService.PreprocessImage(
                    sourceImage,
                    task.EnableGrayscale,
                    task.BinarizeMethod,
                    task.BinarizeThreshold,
                    task.MorphologyMethod,
                    task.MorphologyKernelSize);

                task.OutputImage?.Dispose();
                task.OutputImage = result;

                Log($"[{DateTime.Now:HH:mm:ss}] 图像预处理完成: 灰度={task.EnableGrayscale}, 二值化={task.BinarizeMethod}, 形态学={task.MorphologyMethod}");
                return true;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
            finally
            {
                if (shouldDispose) sourceImage?.Dispose();
            }
        }

        private bool ExecuteImgBlobAnalysis(ImgBlobAnalysisTaskCard task, IList<TaskCardBase> allTasks)
        {
            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);

            if (sourceImage == null)
            {
                task.ErrorMessage = "无法获取源图像";
                return false;
            }

            // 先应用掩膜（掩膜基于全尺寸图像）
            Mat? maskedImage = ApplyMask(sourceImage, task.MaskImagePath);
            Mat imageAfterMask = maskedImage ?? sourceImage;

            // 再裁剪ROI
            Mat? croppedImage = null;
            if (task.RoiWidth > 0 && task.RoiHeight > 0)
            {
                croppedImage = _openCVService.CropImage(imageAfterMask, task.RoiX, task.RoiY, task.RoiWidth, task.RoiHeight);
            }
            Mat imageToAnalyze = croppedImage ?? imageAfterMask;

            try
            {
                var (blobs, resultImage) = _openCVService.BlobAnalysis(
                    imageToAnalyze,
                    task.MinArea,
                    task.MaxArea,
                    task.SortMode,
                    task.MaxBlobCount,
                    task.InvertBinary);

                task.OutputBlobCount = blobs.Count;
                task.OutputBlobResults = blobs;
                task.OutputResult = blobs.Count > 0;

                if (blobs.Count > 0)
                {
                    // 第一个 Blob 的中心坐标
                    task.OutputX = blobs[0].X;
                    task.OutputY = blobs[0].Y;
                }


                task.OutputImage?.Dispose();
                task.OutputImage = resultImage;

                Log($"[{DateTime.Now:HH:mm:ss}] Blob分析完成: 找到 {blobs.Count} 个 Blob");
                return true;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
            finally
            {
                maskedImage?.Dispose();
                croppedImage?.Dispose();
                if (shouldDispose) sourceImage?.Dispose();
            }
        }

        private bool ExecuteImgResize(ImgResizeTaskCard task, IList<TaskCardBase> allTasks)
        {
            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);

            if (sourceImage == null)
            {
                task.ErrorMessage = "无法获取源图像";
                return false;
            }

            try
            {
                int targetW = task.TargetWidth;
                int targetH = task.TargetHeight;

                if (targetW <= 0 || targetH <= 0)
                {
                    task.ErrorMessage = "目标宽度和高度必须大于0";
                    return false;
                }

                var resized = new Mat();
                Cv2.Resize(sourceImage, resized, new OpenCvSharp.Size(targetW, targetH));

                // 计算宽度和高度缩放倍率
                task.OutputWidthScale = (double)targetW / sourceImage.Width;
                task.OutputHeightScale = (double)targetH / sourceImage.Height;

                task.OutputImage?.Dispose();
                task.OutputImage = resized;

                Log($"[{DateTime.Now:HH:mm:ss}] 图像缩放: {sourceImage.Width}x{sourceImage.Height} -> {targetW}x{targetH} (宽缩放: {task.OutputWidthScale:F4}, 高缩放: {task.OutputHeightScale:F4})");
                return true;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
            finally
            {
                if (shouldDispose) sourceImage.Dispose();
            }
        }

        /// <summary>
        /// 执行 ONNX 目标检测（YOLO 定位）
        /// </summary>
        private async Task<bool> ExecuteImgOnnxDetectAsync(ImgOnnxDetectTaskCard task, IList<TaskCardBase> allTasks)
        {
            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);

            if (sourceImage == null)
            {
                task.ErrorMessage = "无法获取源图像";
                return false;
            }

            try
            {
                // 获取模型配置
                var config = TaskFlow.Helpers.OnnxModelManager.GetModelById(task.OnnxModelId);
                if (config == null)
                {
                    task.ErrorMessage = "未选择 ONNX 模型或模型不存在，请在属性面板中选择模型";
                    return false;
                }

                if (!config.FileExists)
                {
                    task.ErrorMessage = $"模型文件不存在: {config.FilePath}";
                    return false;
                }

                // 如果卡片覆盖了置信度阈值，临时修改配置
                var originalThreshold = config.ConfidenceThreshold;
                if (task.ConfidenceOverride > 0 && task.ConfidenceOverride <= 1)
                {
                    config.ConfidenceThreshold = task.ConfidenceOverride;
                }

                // 在线程池中执行推理（避免阻塞 UI）
                var detections = await Task.Run(() =>
                {
                    if (_onnxDetectionService == null)
                        _onnxDetectionService = new OnnxDetectionService();
                    return _onnxDetectionService.Detect(sourceImage, config);
                });

                // 恢复原始阈值
                config.ConfidenceThreshold = originalThreshold;

                // 过滤类别
                if (!string.IsNullOrWhiteSpace(task.FilterClassName))
                {
                    var filterClasses = task.FilterClassName
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    detections = detections.Where(d => filterClasses.Contains(d.ClassName)).ToList();
                }

                // 写入输出
                task.OutputDetectionCount = detections.Count;
                task.OutputResult = detections.Count > 0;

                if (detections.Count > 0)
                {
                    var top = detections[0]; // 已按置信度降序
                    task.OutputX = top.X;
                    task.OutputY = top.Y;
                    task.OutputTopClassName = top.ClassName;
                    task.OutputTopConfidence = top.Confidence;

                    // 生成坐标数组字符串
                    task.OutputDetectionsArray = string.Join(";",
                        detections.Select(d => $"{d.X},{d.Y}"));

                    // 绘制检测结果
                    var annotated = _onnxDetectionService!.DrawDetections(sourceImage, detections);
                    task.OutputImage?.Dispose();
                    task.OutputImage = annotated;

                    Log($"[{DateTime.Now:HH:mm:ss}] ONNX 检测: 找到 {detections.Count} 个目标, " +
                        $"最高置信度: {top.ClassName} ({top.Confidence:F4}), 坐标: ({top.X}, {top.Y})");
                }
                else
                {
                    task.OutputTopClassName = null;
                    task.OutputTopConfidence = 0;
                    task.OutputDetectionsArray = null;
                    Log($"[{DateTime.Now:HH:mm:ss}] ONNX 检测: 未找到目标");
                }

                return true;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"ONNX 推理失败: {ex.Message}";
                return false;
            }
            finally
            {
                if (shouldDispose) sourceImage.Dispose();
            }
        }

        private Task<bool> ExecuteImgCaliperMeasureAsync(ImgCaliperMeasureTaskCard task, IList<TaskCardBase> allTasks)
        {
            Mat? sourceImage = GetSourceImage(task.UseSourceTaskImage, task.SourceTaskIdForImage, task.ImageFilePath, allTasks, out bool shouldDispose);

            if (sourceImage == null)
            {
                task.ErrorMessage = "无法获取源图像";
                return Task.FromResult(false);
            }

            try
            {
                // ROI 参数不支持表达式，直接使用
                var result = _openCVService.MeasureCaliperWidth(
                    sourceImage,
                    task.RoiX, task.RoiY, task.RoiWidth, task.RoiHeight,
                    task.SearchDirection,
                    task.Edge1Polarity, task.Edge2Polarity, task.Edge1Selection, task.Edge2Selection);

                if (!result.Success)
                {
                    task.OutputDistance = 0;
                    task.OutputResult = false;
                    task.ErrorMessage = "测量失败或未找到符合极性的双边缘";
                    Log($"[{DateTime.Now:HH:mm:ss}] 卡尺测量未找到符合条件的双边。");
                    return Task.FromResult(false);
                }

                task.OutputDistance = result.Distance;
                task.OutputResult = true;

                task.OutputImage?.Dispose();
                if (result.ResultImage != null)
                {
                    task.OutputImage = result.ResultImage;
                }

                Log($"[{DateTime.Now:HH:mm:ss}] 卡尺测量成功: 测量距离 {result.Distance:F2} 像素");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"测量失败: {ex.Message}";
                return Task.FromResult(false);
            }
            finally
            {
                if (shouldDispose) sourceImage.Dispose();
            }
        }

        #endregion

        #region String Substring

        private bool ExecuteStringSubstring(StringSubstringTaskCard task, IList<TaskCardBase> allTasks)
        {
            // 获取输入文本
            string? inputText = null;

            if (task.SourceTaskIdForText.HasValue)
            {
                var sourceTask = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForText.Value);
                if (sourceTask?.OutputText != null)
                {
                    inputText = sourceTask.OutputText;
                }
            }

            if (string.IsNullOrEmpty(inputText))
            {
                inputText = task.InputText;
            }

            if (string.IsNullOrEmpty(inputText))
            {
                task.ErrorMessage = "输入文本为空";
                return false;
            }

            try
            {
                // 计算起始位置
                int startIndex;
                if (task.StartMode == StartIndexMode.FindChar)
                {
                    if (string.IsNullOrEmpty(task.SearchChar))
                    {
                        task.ErrorMessage = "查找字符为空";
                        return false;
                    }

                    int foundIndex = inputText.IndexOf(task.SearchChar);
                    if (foundIndex < 0)
                    {
                        task.ErrorMessage = string.Format(Strings.Svc_CharNotFound, task.SearchChar);
                        return false;
                    }

                    startIndex = foundIndex + task.SearchCharOffset;
                }
                else
                {
                    startIndex = task.ManualStartIndex;
                }

                // 边界检查
                if (startIndex < 0 || startIndex >= inputText.Length)
                {
                    task.ErrorMessage = $"起始位置 {startIndex} 超出字符串范围 (0-{inputText.Length - 1})";
                    return false;
                }

                // 计算截取长度
                int length = task.SubstringLength;
                if (length < 0)
                {
                    // -1 表示截取到末尾
                    length = inputText.Length - startIndex;
                }

                if (startIndex + length > inputText.Length)
                {
                    length = inputText.Length - startIndex;
                }

                string result = inputText.Substring(startIndex, length);
                task.OutputText = result;

                Log($"[{DateTime.Now:HH:mm:ss}] 字符串截取: \"{inputText}\" => \"{result}\" (起始={startIndex}, 长度={length})");
                return true;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"字符串截取失败: {ex.Message}";
                return false;
            }
        }

        #endregion

        #region Type Conversion

        /// <summary>
        /// 执行数据类型转换：将string转换为int
        /// 支持引用其他任务的文本输出或变量
        /// 自动剔除无法转换的字符
        /// </summary>
        private bool ExecuteTypeConvert(TypeConvertTaskCard task, IList<TaskCardBase> allTasks)
        {
            // 获取输入文本
            string? inputText = null;

            // 优先从引用任务获取
            if (task.SourceTaskIdForText.HasValue)
            {
                var sourceTask = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForText.Value);
                if (sourceTask?.OutputText != null)
                {
                    inputText = sourceTask.OutputText;
                }
            }

            // 如果未引用任务或引用任务无输出，使用手动输入表达式
            if (string.IsNullOrEmpty(inputText))
            {
                if (!string.IsNullOrEmpty(task.InputExpression))
                {
                    try
                    {
                        // 解析变量引用（引用不存在的变量时报错）
                        inputText = _variableStore.ResolveVariableReferences(task.InputExpression, throwOnMissing: true);
                        // 去掉引号（变量解析后string类型会带引号）
                        inputText = inputText.Trim('"');
                    }
                    catch (Exception ex)
                    {
                        task.ErrorMessage = ex.Message;
                        return false;
                    }
                }
            }

            if (string.IsNullOrEmpty(inputText))
            {
                task.ErrorMessage = "输入为空，请设置来源任务或输入表达式";
                return false;
            }

            try
            {
                // 去除前后空白
                string trimmed = inputText.Trim();
                int intResult;

                // 先尝试直接转换
                if (int.TryParse(trimmed, out intResult))
                {
                    task.OutputIntValue = intResult;
                    task.OutputText = intResult.ToString();
                    Log($"[{DateTime.Now:HH:mm:ss}] 类型转换: \"{trimmed}\" => {intResult} (int)");
                    return true;
                }

                // 尝试先转为double再取整
                if (double.TryParse(trimmed, out double doubleResult))
                {
                    intResult = (int)doubleResult;
                    task.OutputIntValue = intResult;
                    task.OutputText = intResult.ToString();
                    Log($"[{DateTime.Now:HH:mm:ss}] 类型转换: \"{trimmed}\" => {intResult} (int, 截断小数)");
                    return true;
                }

                // 自动剔除无法转换的字符，只保留数字、负号和小数点
                string cleaned = new string(trimmed.Where((c, i) =>
                    char.IsDigit(c) || c == '.' || (c == '-' && i == 0)).ToArray());

                if (string.IsNullOrEmpty(cleaned))
                {
                    task.ErrorMessage = $"输入 \"{trimmed}\" 中没有可转换的数字字符";
                    return false;
                }

                if (int.TryParse(cleaned, out intResult))
                {
                    task.OutputIntValue = intResult;
                    task.OutputText = intResult.ToString();
                    Log($"[{DateTime.Now:HH:mm:ss}] 类型转换: \"{trimmed}\" => 清理为 \"{cleaned}\" => {intResult} (int, 已剔除无效字符)");
                    return true;
                }

                if (double.TryParse(cleaned, out doubleResult))
                {
                    intResult = (int)doubleResult;
                    task.OutputIntValue = intResult;
                    task.OutputText = intResult.ToString();
                    Log($"[{DateTime.Now:HH:mm:ss}] 类型转换: \"{trimmed}\" => 清理为 \"{cleaned}\" => {intResult} (int, 已剔除无效字符并截断小数)");
                    return true;
                }

                task.ErrorMessage = $"无法将 \"{trimmed}\" 转换为整数（清理后: \"{cleaned}\"）";
                return false;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = string.Format(Strings.Svc_ConvertException, ex.Message);
                return false;
            }
        }

        #endregion

        #region Expression Evaluation

        private bool ExecuteExpressionEval(ExpressionEvalTaskCard task, IList<TaskCardBase> allTasks)
        {
            if (string.IsNullOrWhiteSpace(task.Expression))
            {
                task.ErrorMessage = Strings.Svc_ExprEmpty;
                return false;
            }

            try
            {
                // 支持半角和全角分号分割多条赋值语句，也支持换行符
                var statements = task.Expression
                    .Replace("；", ";")
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                if (statements.Count == 0)
                {
                    task.ErrorMessage = Strings.Svc_ExprEmpty;
                    return false;
                }

                var results = new List<string>();

                foreach (var statement in statements)
                {
                    if (!_variableStore.IsAssignment(statement))
                    {
                        task.ErrorMessage = $"无效的赋值语句: {statement}\n格式应为: @变量名 = 表达式";
                        return false;
                    }

                    // 提取右侧表达式并解析变量引用（引用不存在的变量时报错）
                    string rightSide = _variableStore.GetAssignmentRightSide(statement);
                    string resolvedRight = _variableStore.ResolveVariableReferences(rightSide, throwOnMissing: true);
                    resolvedRight = ExpressionEvaluator.ResolveExpression(resolvedRight, allTasks, _variableStore);

                    // 尝试计算简单的数学表达式
                    string finalValue = EvaluateArithmetic(resolvedRight);

                    if (!_variableStore.TryAssign(statement, finalValue))
                    {
                        // 赋值失败，可能是变量不存在
                        var varNameMatch = System.Text.RegularExpressions.Regex.Match(statement, @"^\s*@([\w\u4e00-\u9fff]+)");
                        string varName = varNameMatch.Success ? varNameMatch.Groups[1].Value : Strings.Svc_Unknown;
                        task.ErrorMessage = $"变量 @{varName} 不存在，请先在变量管理器中添加";
                        return false;
                    }

                    results.Add($"{statement} => {finalValue}");
                    Log($"[{DateTime.Now:HH:mm:ss}] 变量赋值: {statement} => {finalValue}");
                }

                task.OutputResult = true;
                return true;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"表达式处理失败: {ex.Message}";
                task.OutputResult = false;
                return false;
            }
        }

        /// <summary>
        /// 尝试计算简单的整数加减表达式（用于变量赋值场景）
        /// 支持显式带双引号的字符串字面量
        /// </summary>
        private string EvaluateArithmetic(string expression)
        {
            string trimmed = expression.Trim();

            // 如果是被双引号整个包裹的字符串字面量，为了在后续 TryAssign 中正确判断它确实是原生字面量，保留其双引号并直接返回
            if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2)
            {
                return trimmed;
            }

            // 如果是纯数字或已经是字符串，直接返回
            if (int.TryParse(trimmed, out _) || double.TryParse(trimmed, out _))
                return trimmed;

            // 尝试计算简单的加减乘除
            try
            {
                var dt = new System.Data.DataTable();
                var result = dt.Compute(trimmed, null);
                return result?.ToString() ?? trimmed;
            }
            catch
            {
                // 如果不仅不是数字/数学表达式，也没有被双引号包裹，则直接返回原始文本
                return trimmed;
            }
        }

        /// <summary>
        /// 解析表达式中的任务引用，格式: #N 名称.输出属性
        /// 支持的输出属性：
        ///   .循环索引  - ForLoop的CurrentLoopIndex
        ///   .执行结果  - OutputResult (true/false)
        ///   .输出文本  - OutputText
        ///   .X / .Y    - OutputX / OutputY
        ///   .当前计数  - Counter的CurrentCount
        /// 示例: "#1 循环开始.循环索引>1", "#3 颜色识别.匹配率>0.5"
        /// </summary>


        #endregion

        #region Helper Methods

        /// <summary>
        /// 评估条件表达式，返回 true/false，异常时返回 null 表示评估失败
        /// </summary>
        private bool? EvaluateCondition(IfElseBranchTaskCard ifCard, IList<TaskCardBase> allTasks)
        {
            var expression = ifCard.ConditionExpression;
            Log($"[调试] EvaluateCondition: Expression=\"{expression}\"");

            if (string.IsNullOrWhiteSpace(expression))
            {
                Log($"[调试] 表达式为空，默认返回 true");
                return true;
            }

            try
            {
                // 解析表达式中的任务引用和变量引用，替换为实际值
                string resolvedExpression = ExpressionEvaluator.ResolveExpression(expression, allTasks, _variableStore);
                bool result = ExpressionEvaluator.Evaluate(resolvedExpression);

                Log($"[调试] 条件判断: {expression} => {resolvedExpression} => {result}");
                return result;
            }
            catch (Exception ex)
            {
                Log($"[调试] 条件表达式评估失败: {ex.Message}");
                ifCard.ErrorMessage = $"条件表达式评估失败: {ex.Message}";
                return null;
            }
        }

        private int FindBranchIndex(IList<TaskCardBase> tasks, Guid? branchGroupId, BranchRole role)
        {
            if (!branchGroupId.HasValue) return -1;

            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].BranchGroupId == branchGroupId && tasks[i].BranchRole == role)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 查找当前索引之后的下一个ElifStart或ElseStart卡片
        /// </summary>
        private int FindNextBranchIndex(IList<TaskCardBase> tasks, int currentIndex, Guid? branchGroupId)
        {
            if (!branchGroupId.HasValue) return -1;

            for (int i = currentIndex + 1; i < tasks.Count; i++)
            {
                if (tasks[i].BranchGroupId == branchGroupId &&
                    (tasks[i].BranchRole == BranchRole.ElifStart || tasks[i].BranchRole == BranchRole.ElseStart))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 获取源图像。shouldDispose 为 true 表示返回的是新建的 Mat（从文件加载），调用方使用完后需要释放。
        /// shouldDispose 为 false 表示返回的是其他任务的 OutputImage 引用，不应释放。
        /// </summary>
        private Mat? GetSourceImage(bool useSourceTask, Guid? sourceTaskId, string? imagePath, IList<TaskCardBase> allTasks, out bool shouldDispose)
        {
            shouldDispose = false;

            if (useSourceTask && sourceTaskId.HasValue)
            {
                var sourceTask = allTasks.FirstOrDefault(t => t.Id == sourceTaskId.Value);
                if (sourceTask?.OutputImage != null && !sourceTask.OutputImage.Empty())
                {
                    return sourceTask.OutputImage;
                }
            }

            if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
            {
                shouldDispose = true; // 从文件新建的Mat，调用方需要释放
                return Cv2.ImRead(imagePath);
            }

            return null;
        }

        #endregion

        #region FileRead / EventListener / ArraySearch 执行器

        /// <summary>
        /// 读取文件，按分隔符分割成数组并缓存
        /// </summary>
        private async Task<bool> ExecuteFileReadAsync(
            FileReadTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                // 解析文件路径表达式
                string filePath = task.FilePathExpression;
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    filePath = _variableStore.ResolveVariableReferences(filePath);
                    filePath = ExpressionEvaluator.ResolveExpression(filePath, allTasks, _variableStore);
                    filePath = filePath.Trim('"');
                }

                if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                {
                    task.ErrorMessage = $"文件不存在: {filePath}";
                    return false;
                }

                // 检查缓存：路径不变时跳过 IO
                if (_fileReadData.TryGetValue(task.Id, out var cached) && cached.Path == filePath)
                {
                    task.OutputArrayCount = cached.Data.Count;
                    Log($"[{DateTime.Now:HH:mm:ss}] 读取文件: 使用缓存，{cached.Data.Count} 条数据");
                    return true;
                }

                // 读取文件
                string content = await System.IO.File.ReadAllTextAsync(filePath, System.Text.Encoding.UTF8, ct);

                // 解析分隔符（处理转义字符）
                string delimiter = task.Delimiter;
                delimiter = delimiter.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\r", "\r");

                // 分割
                var data = string.IsNullOrEmpty(delimiter)
                    ? new List<string> { content }
                    : content.Split(new[] { delimiter }, StringSplitOptions.None).ToList();

                // 移除末尾空元素
                while (data.Count > 0 && string.IsNullOrEmpty(data[^1]))
                    data.RemoveAt(data.Count - 1);

                // 缓存
                _fileReadData[task.Id] = (filePath, data);
                task.OutputArrayCount = data.Count;

                Log($"[{DateTime.Now:HH:mm:ss}] 读取文件: {filePath}, {data.Count} 条数据");
                return true;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 事件监听：使用 GetAsyncKeyState 轮询，等待指定输入事件
        /// </summary>
        private async Task<bool> ExecuteEventListenerAsync(
            EventListenerTaskCard task, CancellationToken ct)
        {
            try
            {
                // 映射事件类型到虚拟键码
                int vkCode = task.EventType switch
                {
                    "MouseLeft" => 0x01,   // VK_LBUTTON
                    "MouseRight" => 0x02,  // VK_RBUTTON
                    "Enter" => 0x0D,       // VK_RETURN
                    "Space" => 0x20,       // VK_SPACE
                    _ => 0x01              // 默认鼠标左键
                };

                Log($"[{DateTime.Now:HH:mm:ss}] 事件监听: 等待 {task.EventType} 事件...");

                // 先等待按键释放（避免上次点击残留）
                while (IsKeyDown(vkCode))
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(20, ct);
                }

                // 持续轮询，等待按键按下
                while (!IsKeyDown(vkCode))
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(30, ct);
                }

                Log($"[{DateTime.Now:HH:mm:ss}] 事件监听: {task.EventType} 已触发");
                return true;
            }
            catch (OperationCanceledException)
            {
                task.ErrorMessage = "任务已取消";
                return false;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsKeyDown(int vkCode)
        {
            return (GetAsyncKeyState(vkCode) & 0x8000) != 0;
        }

        /// <summary>
        /// 匹配查找：在数组中搜索文本
        /// </summary>
        private bool ExecuteArraySearch(
            ArraySearchTaskCard task, IList<TaskCardBase> allTasks)
        {
            try
            {
                // 解析搜索文本
                string searchText = task.SearchExpression;
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    searchText = _variableStore.ResolveVariableReferences(searchText);
                    searchText = ExpressionEvaluator.ResolveExpression(searchText, allTasks, _variableStore);
                    searchText = searchText.Trim('"');
                }

                // 通过 SourceTaskIdForArray 定位数组来源任务
                List<string>? dataList = null;
                if (task.SourceTaskIdForArray.HasValue)
                {
                    var refTask = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForArray.Value);
                    if (refTask is FileReadTaskCard)
                    {
                        if (_fileReadData.TryGetValue(refTask.Id, out var cached))
                            dataList = cached.Data;
                    }
                    else if (refTask is ArrayBuilderTaskCard)
                    {
                        if (_arrayBuilderData.TryGetValue(refTask.Id, out var abData))
                            dataList = abData;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(task.ArraySourceExpression))
                {
                    // 向后兼容：旧的表达式解析逻辑
                    string arrayRef = _variableStore.ResolveVariableReferences(task.ArraySourceExpression);
                    var refMatch = System.Text.RegularExpressions.Regex.Match(arrayRef, @"#(\d+)");
                    if (refMatch.Success)
                    {
                        int order = int.Parse(refMatch.Groups[1].Value);
                        var refTask = allTasks.FirstOrDefault(t => t.Order == order);
                        if (refTask is FileReadTaskCard)
                        {
                            if (_fileReadData.TryGetValue(refTask.Id, out var cached))
                                dataList = cached.Data;
                        }
                        else if (refTask is ArrayBuilderTaskCard)
                        {
                            if (_arrayBuilderData.TryGetValue(refTask.Id, out var abData))
                                dataList = abData;
                        }
                    }
                }

                if (dataList == null || dataList.Count == 0)
                {
                    task.OutputMatchIndex = -1;
                    task.OutputMatchValue = null;
                    task.ErrorMessage = "引用的数组为空或不存在";
                    return false;
                }

                // 根据匹配模式查找
                int matchIndex = -1;
                switch (task.MatchMode)
                {
                    case "Exact":
                        matchIndex = dataList.FindIndex(s => s == searchText);
                        break;

                    case "Contains":
                        matchIndex = dataList.FindIndex(s =>
                            s.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                            searchText.Contains(s, StringComparison.OrdinalIgnoreCase));
                        break;

                    case "Best":
                        // 最佳匹配：计算 LCS 相似度
                        double bestScore = 0;
                        for (int i = 0; i < dataList.Count; i++)
                        {
                            double score = CalculateSimilarity(searchText, dataList[i]);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                matchIndex = i;
                            }
                        }
                        // 相似度阈值
                        if (bestScore < 0.5) matchIndex = -1;
                        break;

                    default:
                        matchIndex = dataList.FindIndex(s =>
                            s.Contains(searchText, StringComparison.OrdinalIgnoreCase));
                        break;
                }

                task.OutputMatchIndex = matchIndex;
                task.OutputMatchValue = matchIndex >= 0 ? dataList[matchIndex] : null;
                task.OutputResult = matchIndex >= 0;

                Log($"[{DateTime.Now:HH:mm:ss}] 匹配查找: 模式={task.MatchMode}, 结果索引={matchIndex}");
                return matchIndex >= 0;
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 计算两个字符串的 LCS（最长公共子序列）相似度
        /// </summary>
        private static double CalculateSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            int maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0) return 1;

            // LCS 动态规划
            int[,] dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    dp[i, j] = a[i - 1] == b[j - 1]
                        ? dp[i - 1, j - 1] + 1
                        : Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }

            return (double)dp[a.Length, b.Length] / maxLen;
        }

        #endregion

        #region Win Find File

        /// <summary>
        /// 执行Win路径查找：在指定目录或全盘搜索文件，返回第一个匹配的完整路径
        /// </summary>
        private async Task<bool> ExecuteWinFindFileAsync(
            WinFindFileTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                // 1. 解析表达式
                string fileName = _variableStore.ResolveVariableReferences(task.FileName);
                fileName = ExpressionEvaluator.ResolveExpression(fileName, allTasks, _variableStore);
                fileName = fileName.Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    task.ErrorMessage = "文件名称不能为空";
                    return false;
                }

                string searchRoot = task.SearchRootPath;
                if (!string.IsNullOrWhiteSpace(searchRoot))
                {
                    searchRoot = _variableStore.ResolveVariableReferences(searchRoot);
                    searchRoot = ExpressionEvaluator.ResolveExpression(searchRoot, allTasks, _variableStore);
                    searchRoot = searchRoot.Trim().Trim('"');
                }

                int maxDepth = task.MaxDepth;
                bool useWildcard = task.UseWildcard;

                // 2. 确定搜索根目录列表
                var searchRoots = new List<string>();
                if (!string.IsNullOrWhiteSpace(searchRoot))
                {
                    if (!System.IO.Directory.Exists(searchRoot))
                    {
                        task.ErrorMessage = $"搜索根目录不存在: {searchRoot}";
                        return false;
                    }
                    searchRoots.Add(searchRoot);
                }
                else
                {
                    // 枚举所有就绪的逻辑驱动器
                    foreach (var drive in System.IO.DriveInfo.GetDrives())
                    {
                        if (drive.IsReady)
                        {
                            searchRoots.Add(drive.RootDirectory.FullName);
                        }
                    }
                }

                Log($"[{DateTime.Now:HH:mm:ss}] Win路径查找: 文件名={fileName}, 根目录={string.Join(";", searchRoots)}, 最大深度={maxDepth}, 通配符={useWildcard}");

                // 3. 在后台线程执行搜索
                string? foundPath = await Task.Run(() =>
                {
                    foreach (var root in searchRoots)
                    {
                        ct.ThrowIfCancellationRequested();
                        var result = SearchFileRecursive(root, fileName, useWildcard, maxDepth, 1, ct);
                        if (result != null) return result;
                    }
                    return null;
                }, ct);

                // 4. 输出结果
                if (foundPath != null)
                {
                    task.OutputFilePath = foundPath;
                    task.OutputResult = true;
                    Log($"[{DateTime.Now:HH:mm:ss}] Win路径查找: 找到文件 {foundPath}");
                    return true;
                }
                else
                {
                    task.OutputFilePath = string.Empty;
                    task.OutputResult = false;
                    Log($"[{DateTime.Now:HH:mm:ss}] Win路径查找: 未找到文件 {fileName}");
                    return true; // 任务本身执行成功，只是没找到文件
                }
            }
            catch (OperationCanceledException)
            {
                throw; // 交给上层处理取消
            }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 递归搜索文件，支持深度限制、通配符、权限异常跳过
        /// </summary>
        /// <param name="directory">当前搜索目录</param>
        /// <param name="fileName">要查找的文件名</param>
        /// <param name="useWildcard">是否启用通配符匹配</param>
        /// <param name="maxDepth">最大深度（0=不限）</param>
        /// <param name="currentDepth">当前深度（从1开始）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>找到的文件完整路径，未找到返回 null</returns>
        private static string? SearchFileRecursive(
            string directory, string fileName, bool useWildcard,
            int maxDepth, int currentDepth, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // 搜索当前目录下的文件
                IEnumerable<string> files;
                if (useWildcard)
                {
                    // 通配符模式：直接使用 fileName 作为搜索模式
                    files = System.IO.Directory.EnumerateFiles(directory, fileName);
                }
                else
                {
                    // 精确匹配模式：列出所有文件，逐一比较文件名（忽略大小写）
                    files = System.IO.Directory.EnumerateFiles(directory);
                }

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    if (useWildcard)
                    {
                        // 通配符模式下 EnumerateFiles 已经做了匹配
                        return file;
                    }
                    else
                    {
                        // 精确匹配：比较文件名（忽略大小写）
                        var name = System.IO.Path.GetFileName(file);
                        if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            return file;
                        }
                    }
                }

                // 如果达到最大深度则不再递归
                if (maxDepth > 0 && currentDepth >= maxDepth)
                    return null;

                // 递归搜索子目录
                IEnumerable<string> subDirs;
                try
                {
                    subDirs = System.IO.Directory.EnumerateDirectories(directory);
                }
                catch (UnauthorizedAccessException) { return null; }
                catch (System.IO.IOException) { return null; }

                foreach (var subDir in subDirs)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var result = SearchFileRecursive(subDir, fileName, useWildcard, maxDepth, currentDepth + 1, ct);
                        if (result != null) return result;
                    }
                    catch (UnauthorizedAccessException) { /* 跳过无权限目录 */ }
                    catch (System.IO.IOException) { /* 跳过IO异常目录 */ }
                }
            }
            catch (UnauthorizedAccessException) { /* 跳过无权限目录 */ }
            catch (System.IO.IOException) { /* 跳过IO异常目录 */ }

            return null;
        }

        #endregion

        #region ClipboardWatch
        private async Task<bool> ExecuteClipboardWatchAsync(ClipboardWatchTaskCard task, CancellationToken ct)
        {
            try
            {
                int timeoutMs = task.TimeoutMs;
                var tcs = new TaskCompletionSource<string>();

                // 在 UI 线程创建隐藏的窗口来接收剪贴板消息
                System.Windows.Interop.HwndSource hwndSource = null;
                string baseline = task.LastOutputText;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var parameters = new System.Windows.Interop.HwndSourceParameters("ClipboardWatchWindow")
                    {
                        Width = 0, Height = 0, WindowStyle = 0
                    };
                    hwndSource = new System.Windows.Interop.HwndSource(parameters);
                    hwndSource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                    {
                        const int WM_CLIPBOARDUPDATE = 0x031D;
                        if (msg == WM_CLIPBOARDUPDATE)
                        {
                            try
                            {
                                if (!System.Windows.Clipboard.ContainsText())
                                    return IntPtr.Zero;

                                string currentText = System.Windows.Clipboard.GetText();
                                if (string.IsNullOrEmpty(currentText))
                                    return IntPtr.Zero;

                                // 去重检查
                                string compareBaseline = task.EnableDedup ? task.LastOutputText : baseline;
                                if (task.TrimWhitespace)
                                    compareBaseline = compareBaseline.Trim();

                                if (currentText != compareBaseline)
                                {
                                    tcs.TrySetResult(currentText);
                                }
                            }
                            catch { /* 剪贴板访问异常，忽略 */ }
                        }
                        return IntPtr.Zero;
                    });

                    // 注册剪贴板变化监听
                    AddClipboardFormatListener(hwndSource.Handle);
                });

                try
                {
                    // 等待变化或超时
                    if (timeoutMs > 0)
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(timeoutMs);

                        try
                        {
                            var registrations = new List<CancellationTokenRegistration>();
                            registrations.Add(timeoutCts.Token.Register(() => tcs.TrySetCanceled()));
                            string newText = await tcs.Task;

                            foreach (var reg in registrations) reg.Dispose();

                            // 成功检测到变化
                            task.OutputText = newText;
                            task.OutputResult = true;
                            task.LastOutputText = newText;
                            Log($"[{DateTime.Now:HH:mm:ss}] 剪贴板监听: 检测到新文本({newText.Length}字)");
                            return true;
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            // 超时
                            task.OutputText = null;
                            task.OutputResult = false;
                            Log($"[{DateTime.Now:HH:mm:ss}] 剪贴板监听: 等待超时 ({timeoutMs}ms)");
                            return true; // 超时不算失败，OutputResult=false 即可
                        }
                    }
                    else
                    {
                        // 无限等待
                        using var reg = ct.Register(() => tcs.TrySetCanceled());
                        string newText = await tcs.Task;

                        task.OutputText = newText;
                        task.OutputResult = true;
                        task.LastOutputText = newText;
                        Log($"[{DateTime.Now:HH:mm:ss}] 剪贴板监听: 检测到新文本({newText.Length}字)");
                        return true;
                    }
                }
                finally
                {
                    // 清理：在 UI 线程移除监听并销毁消息窗口
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (hwndSource != null)
                        {
                            RemoveClipboardFormatListener(hwndSource.Handle);
                            hwndSource.Dispose();
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw; // 交给上层处理取消
            }
            catch (Exception ex)
            {
                task.ErrorMessage = $"剪贴板监听失败: {ex.Message}";
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        #endregion

        #region Galgame AutoTracker
        
        // Define internal states mimicking the JSON
        private class DfsNode
        {
            [System.Text.Json.Serialization.JsonPropertyName("total")]
            public int TotalChoices { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("index")]
            public int CurrentIndex { get; set; }
        }
        
        private class RouteState
        {
            [System.Text.Json.Serialization.JsonPropertyName("nodes")]
            public List<DfsNode> Nodes { get; set; } = new();
        }

        private async Task<bool> ExecuteAutoRouteTrackerAsync(AutoRouteTrackerTaskCard task, IList<TaskCardBase> allTasks, CancellationToken cancellationToken)
        {
            try
            {
                // 1. Get OCR results
                ImgOcrTaskCard? sourceCard = null;
                if (task.SourceOcrTaskId.HasValue)
                {
                    sourceCard = allTasks.FirstOrDefault(t => t.Id == task.SourceOcrTaskId.Value) as ImgOcrTaskCard;
                }
                
                if (sourceCard == null)
                {
                    task.ErrorMessage = "未绑定图像OCR资源或未找到该卡片";
                    return false;
                }
                
                if (sourceCard.OutputResultCount == 0 || sourceCard.OutputOcrResults.Count == 0)
                {
                    task.ErrorMessage = "OCR 未输出任何选项数据";
                    return false;
                }

                // Filtering: Filter out empty strings or strings that look like noise if neccessary.
                // Assuming all elements in OutputOcrResults are valid choices.
                int numChoices = sourceCard.OutputResultCount;

                // 2. Load Route State JSON
                string jsonPath = task.RouteStateFilePath;
                // If relative path, place it relative to the executable
                if (!Path.IsPathRooted(jsonPath))
                {
                    jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", jsonPath);
                }
                
                Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

                RouteState state = new RouteState();
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        string jsonText = await File.ReadAllTextAsync(jsonPath, cancellationToken);
                        state = System.Text.Json.JsonSerializer.Deserialize<RouteState>(jsonText) ?? new RouteState();
                    }
                    catch (Exception ex)
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] 路线判定: 读取JSON失败 {ex.Message}，将作为新档开始。");
                    }
                }

                // 3. Evaluate state
                int d = task.CurrentDepth;
                int targetIndex = 0;

                if (state.Nodes == null) state.Nodes = new();

                if (d >= state.Nodes.Count)
                {
                    // Newly discovered depth node!
                    state.Nodes.Add(new DfsNode { TotalChoices = numChoices, CurrentIndex = 0 });
                    targetIndex = 0;
                    Log($"[{DateTime.Now:HH:mm:ss}] 路线判定: 发现新分歧(深度={d}, 选项数={numChoices})，选择第一个路线。");
                }
                else
                {
                    // Existing node, fetch the index
                    var node = state.Nodes[d];
                    // Correct potential mismatches in OCR choice counting
                    if (numChoices != node.TotalChoices)
                    {
                        Log($"[{DateTime.Now:HH:mm:ss}] 路线判定: 警告！历史记录的选项数({node.TotalChoices})与图面的选项数({numChoices})不符！可能由于遮挡或识别误差。");
                        // Automatically cap
                        if (node.CurrentIndex >= numChoices) 
                            node.CurrentIndex = Math.Max(0, numChoices - 1); 
                    }
                    targetIndex = node.CurrentIndex;
                    Log($"[{DateTime.Now:HH:mm:ss}] 路线判定: 已知分歧(深度={d})，提取历史决定路线 {targetIndex+1}/{node.TotalChoices}。");
                }

                // Save immediately
                string newJson = System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(jsonPath, newJson, cancellationToken);

                // 4. Extract Coordinates
                var targetItem = sourceCard.OutputOcrResults[Math.Max(0, targetIndex)];
                task.OutputX = targetItem.X;
                task.OutputY = targetItem.Y;
                
                // Track depth traversal for any subsequent chained choice in the same run/flow
                task.CurrentDepth++;
                task.OutputResult = true;

                return true;
            }
            catch(Exception ex)
            {
                task.ErrorMessage = "追踪发生异常：" + ex.Message;
                return false;
            }
        }

        private bool ExecuteAutoRouteAdvance(AutoRouteAdvanceTaskCard task)
        {
             try
             {
                string jsonPath = task.RouteStateFilePath;
                if (!Path.IsPathRooted(jsonPath))
                    jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", jsonPath);
                    
                if (!File.Exists(jsonPath))
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] DFS推进：没有找到存档文件 '{jsonPath}'，忽略推进。");
                    return true;
                }
                string jsonText = File.ReadAllText(jsonPath);
                var state = System.Text.Json.JsonSerializer.Deserialize<RouteState>(jsonText);
                if (state == null || state.Nodes == null || state.Nodes.Count == 0) 
                    return true;
                    
                // Backtrack! Increment deepest node, if exhaustive, remove it and increment its parent
                int idx = state.Nodes.Count - 1;
                while(idx >= 0)
                {
                    var node = state.Nodes[idx];
                    if (node.CurrentIndex + 1 < node.TotalChoices)
                    {
                        // Increment and finish backtrack
                        node.CurrentIndex++;
                        Log($"[{DateTime.Now:HH:mm:ss}] DFS推进: 分歧深度 {idx} 切换为路线 {node.CurrentIndex+1}/{node.TotalChoices}");
                        break;
                    }
                    else
                    {
                        // Exhausted, pop!
                        Log($"[{DateTime.Now:HH:mm:ss}] DFS推进: 分歧深度 {idx} 已穷尽，向上回溯。");
                        state.Nodes.RemoveAt(idx);
                        idx--;
                    }
                }
                
                if (idx < 0)
                {
                    Log($"[{DateTime.Now:HH:mm:ss}] DFS推进: 全部游戏分歧路线穷尽！完结撒花。");
                }

                string newJson = System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(jsonPath, newJson);
                task.OutputResult = true;
                return true;
             }
             catch (Exception ex)
             {
                 task.ErrorMessage = "推进异常: " + ex.Message;
                 return false;
             }
        }

        private bool ExecuteOcrKeywordAnchor(OcrKeywordAnchorTaskCard task, IList<TaskCardBase> allTasks)
        {
            try
            {
                ImgOcrTaskCard? sourceCard = null;
                if (task.SourceOcrTaskId.HasValue)
                    sourceCard = allTasks.FirstOrDefault(t => t.Id == task.SourceOcrTaskId.Value) as ImgOcrTaskCard;

                if (sourceCard == null)
                {
                    task.ErrorMessage = "未绑定图像OCR资源或未找到该卡片";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(task.TargetKeywords))
                {
                    task.ErrorMessage = "锚点目标关键字为空";
                    return false;
                }

                string[] keywords = task.TargetKeywords.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(s => s.Trim().ToLowerInvariant())
                                           .ToArray();

                if (sourceCard.OutputOcrResults != null)
                {
                    foreach(var item in sourceCard.OutputOcrResults)
                    {
                        string lowerItem = item.Text.ToLowerInvariant();
                        foreach(var kw in keywords)
                        {
                            if (lowerItem.Contains(kw))
                            {
                                task.OutputX = item.X;
                                task.OutputY = item.Y;
                                task.OutputResult = true;
                                Log($"[{DateTime.Now:HH:mm:ss}] 关键词定锚: 成功匹配 '{item.Text}' 命中 '{kw}'，输出坐标 {item.X},{item.Y}");
                                return true;
                            }
                        }
                    }
                }

                task.ErrorMessage = "未能在 OCR 结果中找到指定关键词。";
                task.OutputResult = false;
                return true; // Return true to indicate task completed successfully, allowing condition checks.
            }
            catch(Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }
        #endregion

    }
}
