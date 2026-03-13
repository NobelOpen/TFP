using System;
using System.Collections.Generic;
using OpenCvSharp;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Services
{
    public interface IOpenCVService
    {
        Mat CropImage(Mat source, int x, int y, int width, int height);
        (bool Success, int CenterX, int CenterY, double MaxVal, Mat? ResultImage) TemplateMatch(Mat source, Mat template, double threshold);
        List<MatchResult> TemplateMatchMulti(Mat source, Mat template, double threshold, int maxCount);
        (double MeanH, double MeanS, double MeanV, double MatchRatio, Mat? MaskImage) DetectHsvColor(
            Mat source, int lowerH, int lowerS, int lowerV, int upperH, int upperS, int upperV);

        /// <summary>
        /// 颜色分割：只保留HSV范围内的区域，其余涂黑
        /// </summary>
        Mat? SegmentByHsvColor(Mat source, int lowerH, int lowerS, int lowerV, int upperH, int upperS, int upperV);

        /// <summary>
        /// 图像预处理：灰度化/二值化/形态学
        /// </summary>
        Mat PreprocessImage(Mat source, bool enableGrayscale, BinarizeMethod binarizeMethod, int binarizeThreshold, MorphologyMethod morphologyMethod, int kernelSize);

        /// <summary>
        /// Blob分析：连通域分析
        /// </summary>
        (List<BlobResult> Blobs, Mat? ResultImage) BlobAnalysis(Mat source, int minArea, int maxArea, BlobSortMode sortMode, int maxCount, bool invertBinary);
    }

    public class OpenCVService : IOpenCVService
    {
        public Mat CropImage(Mat source, int x, int y, int width, int height)
        {
            if (source == null || source.Empty())
            {
                throw new ArgumentException("源图像为空");
            }

            // 边界检查
            x = Math.Max(0, Math.Min(x, source.Width - 1));
            y = Math.Max(0, Math.Min(y, source.Height - 1));
            width = Math.Min(width, source.Width - x);
            height = Math.Min(height, source.Height - y);

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("裁剪区域无效");
            }

            var roi = new Rect(x, y, width, height);
            using var sub = new Mat(source, roi); // using 确保子矩阵被释放
            return sub.Clone();
        }

        public (bool Success, int CenterX, int CenterY, double MaxVal, Mat? ResultImage) TemplateMatch(Mat source, Mat template, double threshold)
        {
            if (source == null || source.Empty())
            {
                return (false, 0, 0, 0, null);
            }

            if (template == null || template.Empty())
            {
                return (false, 0, 0, 0, null);
            }

            // 确保模板不大于源图像
            if (template.Width > source.Width || template.Height > source.Height)
            {
                return (false, 0, 0, 0, null);
            }

            try
            {
                // 转换为灰度图
                using var sourceGray = source.Channels() == 1 ? source.Clone() : source.CvtColor(ColorConversionCodes.BGR2GRAY);
                using var templateGray = template.Channels() == 1 ? template.Clone() : template.CvtColor(ColorConversionCodes.BGR2GRAY);

                // 模板匹配
                using var result = new Mat();
                Cv2.MatchTemplate(sourceGray, templateGray, result, TemplateMatchModes.CCoeffNormed);

                // 获取最大值位置
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                if (maxVal >= threshold)
                {
                    int centerX = maxLoc.X + template.Width / 2;
                    int centerY = maxLoc.Y + template.Height / 2;

                    // 创建结果图像（在源图像上标记匹配位置）
                    var resultImage = source.Clone();
                    Cv2.Rectangle(resultImage,
                        new OpenCvSharp.Point(maxLoc.X, maxLoc.Y),
                        new OpenCvSharp.Point(maxLoc.X + template.Width, maxLoc.Y + template.Height),
                        new Scalar(0, 255, 0), 2);
                    Cv2.Circle(resultImage, new OpenCvSharp.Point(centerX, centerY), 5, new Scalar(0, 0, 255), -1);

                    return (true, centerX, centerY, maxVal, resultImage);
                }

                return (false, 0, 0, maxVal, null);
            }
            catch (Exception)
            {
                return (false, 0, 0, 0, null);
            }
        }

        /// <summary>
        /// 多目标模板匹配：查找所有超过阈值的匹配位置，使用NMS去重
        /// </summary>
        public List<MatchResult> TemplateMatchMulti(Mat source, Mat template, double threshold, int maxCount)
        {
            var results = new List<MatchResult>();
            if (source == null || source.Empty() || template == null || template.Empty()) return results;
            if (template.Width > source.Width || template.Height > source.Height) return results;

            try
            {
                using var sourceGray = source.Channels() == 1 ? source.Clone() : source.CvtColor(ColorConversionCodes.BGR2GRAY);
                using var templateGray = template.Channels() == 1 ? template.Clone() : template.CvtColor(ColorConversionCodes.BGR2GRAY);

                using var result = new Mat();
                Cv2.MatchTemplate(sourceGray, templateGray, result, TemplateMatchModes.CCoeffNormed);

                // 遍历结果矩阵，找所有超过阈值的匹配位置
                int tw = template.Width;
                int th = template.Height;
                var candidates = new List<(int x, int y, double score)>();

                for (int y = 0; y < result.Rows; y++)
                {
                    for (int x = 0; x < result.Cols; x++)
                    {
                        float val = result.At<float>(y, x);
                        if (val >= threshold)
                        {
                            candidates.Add((x, y, val));
                        }
                    }
                }

                // 按分数降序排序
                candidates.Sort((a, b) => b.score.CompareTo(a.score));

                // NMS去重：剔除重叠区域
                var accepted = new List<(int x, int y, double score)>();
                foreach (var c in candidates)
                {
                    if (accepted.Count >= maxCount) break;

                    bool overlaps = false;
                    foreach (var a in accepted)
                    {
                        // 如果两个匹配位置的距离小于模板尺寸的一半，认为重叠
                        if (Math.Abs(c.x - a.x) < tw / 2 && Math.Abs(c.y - a.y) < th / 2)
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

                foreach (var a in accepted)
                {
                    results.Add(new MatchResult
                    {
                        X = a.x + tw / 2,
                        Y = a.y + th / 2,
                        Score = Math.Round(a.score, 4)
                    });
                }

                return results;
            }
            catch (Exception)
            {
                return results;
            }
        }

        public (double MeanH, double MeanS, double MeanV, double MatchRatio, Mat? MaskImage) DetectHsvColor(
            Mat source, int lowerH, int lowerS, int lowerV, int upperH, int upperS, int upperV)
        {
            if (source == null || source.Empty())
            {
                return (0, 0, 0, 0, null);
            }

            try
            {
                // 转换为HSV
                using var hsv = source.CvtColor(ColorConversionCodes.BGR2HSV);

                // 计算整张图像的平均HSV
                var meanScalar = Cv2.Mean(hsv);
                double meanH = meanScalar.Val0;
                double meanS = meanScalar.Val1;
                double meanV = meanScalar.Val2;

                // 应用HSV范围过滤
                var lowerBound = new Scalar(lowerH, lowerS, lowerV);
                var upperBound = new Scalar(upperH, upperS, upperV);
                using var mask = new Mat();
                Cv2.InRange(hsv, lowerBound, upperBound, mask);

                // 计算匹配像素占比
                int totalPixels = mask.Rows * mask.Cols;
                int matchPixels = Cv2.CountNonZero(mask);
                double matchRatio = totalPixels > 0 ? (double)matchPixels / totalPixels : 0;

                // 创建可视化结果图像：在匹配区域叠加高亮
                var resultImage = source.Clone();
                using var highlight = new Mat(source.Size(), source.Type(), new Scalar(0, 255, 0));
                highlight.CopyTo(resultImage, mask);

                // 混合原图和高亮以便观察
                Cv2.AddWeighted(source, 0.6, resultImage, 0.4, 0, resultImage);

                return (Math.Round(meanH, 2), Math.Round(meanS, 2), Math.Round(meanV, 2), Math.Round(matchRatio, 4), resultImage);
            }
            catch (Exception)
            {
                return (0, 0, 0, 0, null);
            }
        }

        public Mat? SegmentByHsvColor(Mat source, int lowerH, int lowerS, int lowerV, int upperH, int upperS, int upperV)
        {
            if (source == null || source.Empty())
            {
                return null;
            }

            try
            {
                // 转换为HSV
                using var hsv = source.CvtColor(ColorConversionCodes.BGR2HSV);

                // 应用HSV范围过滤生成掩码
                var lowerBound = new Scalar(lowerH, lowerS, lowerV);
                var upperBound = new Scalar(upperH, upperS, upperV);
                using var mask = new Mat();
                Cv2.InRange(hsv, lowerBound, upperBound, mask);

                // 使用掩码保留匹配区域，非匹配区域涂黑
                var result = new Mat();
                Cv2.BitwiseAnd(source, source, result, mask);

                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public Mat PreprocessImage(Mat source, bool enableGrayscale, BinarizeMethod binarizeMethod, int binarizeThreshold, MorphologyMethod morphologyMethod, int kernelSize)
        {
            if (source == null || source.Empty())
                throw new ArgumentException("源图像为空");

            Mat result = source.Clone();

            // 步骤1：灰度化
            if (enableGrayscale && result.Channels() > 1)
            {
                var gray = result.CvtColor(ColorConversionCodes.BGR2GRAY);
                result.Dispose();
                result = gray;
            }

            // 步骤2：二值化
            if (binarizeMethod != BinarizeMethod.None)
            {
                // 确保是单通道
                if (result.Channels() > 1)
                {
                    var gray = result.CvtColor(ColorConversionCodes.BGR2GRAY);
                    result.Dispose();
                    result = gray;
                }

                var binary = new Mat();
                switch (binarizeMethod)
                {
                    case BinarizeMethod.Binary:
                        Cv2.Threshold(result, binary, binarizeThreshold, 255, ThresholdTypes.Binary);
                        break;
                    case BinarizeMethod.BinaryInv:
                        Cv2.Threshold(result, binary, binarizeThreshold, 255, ThresholdTypes.BinaryInv);
                        break;
                    case BinarizeMethod.Otsu:
                        Cv2.Threshold(result, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                        break;
                    case BinarizeMethod.Triangle:
                        Cv2.Threshold(result, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Triangle);
                        break;
                }
                result.Dispose();
                result = binary;
            }

            // 步骤3：形态学操作
            if (morphologyMethod != MorphologyMethod.None)
            {
                int kSize = Math.Max(1, kernelSize | 1); // 确保为奇数
                using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(kSize, kSize));
                var morphed = new Mat();

                switch (morphologyMethod)
                {
                    case MorphologyMethod.Open:
                        Cv2.MorphologyEx(result, morphed, MorphTypes.Open, kernel);
                        break;
                    case MorphologyMethod.Close:
                        Cv2.MorphologyEx(result, morphed, MorphTypes.Close, kernel);
                        break;
                    case MorphologyMethod.Dilate:
                        Cv2.Dilate(result, morphed, kernel);
                        break;
                    case MorphologyMethod.Erode:
                        Cv2.Erode(result, morphed, kernel);
                        break;
                }
                result.Dispose();
                result = morphed;
            }

            return result;
        }

        public (List<BlobResult> Blobs, Mat? ResultImage) BlobAnalysis(Mat source, int minArea, int maxArea, BlobSortMode sortMode, int maxCount, bool invertBinary)
        {
            if (source == null || source.Empty())
                return (new List<BlobResult>(), null);

            try
            {
                // 确保是单通道二值图
                Mat binary;
                if (source.Channels() > 1)
                {
                    using var gray = source.CvtColor(ColorConversionCodes.BGR2GRAY);
                    binary = new Mat();
                    Cv2.Threshold(gray, binary, 1, 255, ThresholdTypes.Binary);
                }
                else
                {
                    binary = source.Clone();
                    // 确保是二值图
                    Cv2.Threshold(binary, binary, 1, 255, ThresholdTypes.Binary);
                }

                // 颜色极性反转：检测暗色Blob
                if (invertBinary)
                {
                    Cv2.BitwiseNot(binary, binary);
                }

                // 连通域分析
                using var labels = new Mat();
                using var stats = new Mat();
                using var centroids = new Mat();
                int numLabels = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);
                binary.Dispose();

                var blobs = new List<BlobResult>();

                // 跳过背景（label=0）
                for (int i = 1; i < numLabels; i++)
                {
                    int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                    if (area < minArea || area > maxArea) continue;

                    int x = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                    int y = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                    int w = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                    int h = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);

                    blobs.Add(new BlobResult
                    {
                        X = x + w / 2,  // 中心坐标
                        Y = y + h / 2,
                        Width = w,
                        Height = h,
                        Area = area
                    });
                }

                // 排序
                blobs = sortMode switch
                {
                    BlobSortMode.AreaDesc => blobs.OrderByDescending(b => b.Area).ToList(),
                    BlobSortMode.AreaAsc => blobs.OrderBy(b => b.Area).ToList(),
                    BlobSortMode.LeftToRight => blobs.OrderBy(b => b.X).ToList(),
                    BlobSortMode.TopToBottom => blobs.OrderBy(b => b.Y).ToList(),
                    _ => blobs
                };

                // 截取最大数量
                if (blobs.Count > maxCount)
                    blobs = blobs.Take(maxCount).ToList();

                // 生成结果图像
                var resultImage = source.Channels() == 1
                    ? source.CvtColor(ColorConversionCodes.GRAY2BGR)
                    : source.Clone();

                for (int i = 0; i < blobs.Count; i++)
                {
                    var blob = blobs[i];
                    int bx = blob.X - blob.Width / 2;
                    int by = blob.Y - blob.Height / 2;

                    // 画边界框
                    Cv2.Rectangle(resultImage,
                        new OpenCvSharp.Point(bx, by),
                        new OpenCvSharp.Point(bx + blob.Width, by + blob.Height),
                        new Scalar(0, 255, 0), 2);

                    // 画中心点
                    Cv2.Circle(resultImage, new OpenCvSharp.Point(blob.X, blob.Y), 5, new Scalar(0, 0, 255), -1);

                    // 标注编号和面积
                    Cv2.PutText(resultImage, $"#{i + 1} A:{blob.Area}",
                        new OpenCvSharp.Point(bx, by - 8),
                        HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 0), 1);
                }

                return (blobs, resultImage);
            }
            catch (Exception)
            {
                return (new List<BlobResult>(), null);
            }
        }
    }
}

