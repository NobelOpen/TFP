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
            if (!ResolveCoordinateExpression(task.StartXExpression, "X", ref x, task, allTasks)) return false;
            if (!ResolveCoordinateExpression(task.StartYExpression, "Y", ref y, task, allTasks)) return false;

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

            // 加载模板图像
            Mat? templateImage = null;
            if (!string.IsNullOrEmpty(task.TemplateImagePath) && System.IO.File.Exists(task.TemplateImagePath))
            {
                templateImage = Cv2.ImRead(task.TemplateImagePath);
            }

            if (templateImage == null || templateImage.Empty())
            {
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

                templateImage.Dispose();
                return Task.FromResult(task.OutputResult.GetValueOrDefault());
            }
            catch (Exception ex)
            {
                templateImage?.Dispose();
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
                (bool Success, string Text, string? Error) result;
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

                // 计算缩放倍率（取宽度方向）
                task.OutputScaleRatio = (double)targetW / sourceImage.Width;

                task.OutputImage?.Dispose();
                task.OutputImage = resized;

                Log($"[{DateTime.Now:HH:mm:ss}] 图像缩放: {sourceImage.Width}x{sourceImage.Height} -> {targetW}x{targetH} (倍率: {task.OutputScaleRatio:F4})");
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
        /// </summary>
        private string EvaluateArithmetic(string expression)
        {
            string trimmed = expression.Trim();

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

        private bool EvaluateCondition(IfElseBranchTaskCard ifCard, IList<TaskCardBase> allTasks)
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
                return false;
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
    }
}

