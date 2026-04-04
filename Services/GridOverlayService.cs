using System;
using System.Collections.Generic;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using CvRect = OpenCvSharp.Rect;

namespace TaskFlow.Services
{
    /// <summary>
    /// 递归网格叠加服务：在截图上绘制宏观/微观网格标签，
    /// 用于 LLM 交互式定位点击目标
    /// </summary>
    public static class GridOverlayService
    {
        // 宏观网格行标签（字母）
        private static readonly string[] RowLabels = { "A", "B", "C", "D", "E", "F", "G", "H" };

        /// <summary>获取最大网格标签（如 rows=4, cols=4 → "D4"）</summary>
        public static string GetMaxLabel(int rows, int cols) =>
            $"{RowLabels[Math.Min(rows - 1, RowLabels.Length - 1)]}{cols}";
        /// <summary>
        /// 在图像上绘制宏观网格（如 4×4，标签 A1~D4）
        /// </summary>
        /// <param name="image">原始截图（不会被修改）</param>
        /// <param name="rows">行数（默认 4）</param>
        /// <param name="cols">列数（默认 4）</param>
        /// <returns>带网格的图像副本 + 各网格在原图坐标系中的像素区域</returns>
        public static (Mat GridImage, Dictionary<string, CvRect> Layout) DrawMacroGrid(
            Mat image, int rows = 4, int cols = 4)
        {
            var result = image.Clone();
            var layout = new Dictionary<string, CvRect>();

            int cellW = image.Width / cols;
            int cellH = image.Height / rows;

            // 根据图像尺寸自适应字体大小
            double fontScale = Math.Max(0.5, Math.Min(2.0, image.Width / 960.0));
            int thickness = Math.Max(1, (int)(fontScale * 2));
            int lineThick = Math.Max(1, (int)(fontScale * 1.2));

            // 绘制网格线
            for (int r = 1; r < rows; r++)
            {
                int y = r * cellH;
                Cv2.Line(result, new CvPoint(0, y), new CvPoint(image.Width, y),
                    new Scalar(255, 255, 255), lineThick, LineTypes.AntiAlias);
            }
            for (int c = 1; c < cols; c++)
            {
                int x = c * cellW;
                Cv2.Line(result, new CvPoint(x, 0), new CvPoint(x, image.Height),
                    new Scalar(255, 255, 255), lineThick, LineTypes.AntiAlias);
            }

            // 在每个网格中心绘制标签
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    string label = $"{RowLabels[r]}{c + 1}";
                    int x = c * cellW;
                    int y = r * cellH;

                    layout[label] = new CvRect(x, y, cellW, cellH);

                    // 标签位置：网格中心
                    int cx = x + cellW / 2;
                    int cy = y + cellH / 2;

                    // 计算文字尺寸以绘制背景矩形
                    var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, fontScale, thickness, out int baseline);
                    int textX = cx - textSize.Width / 2;
                    int textY = cy + textSize.Height / 2;

                    // 绘制半透明黑底背景（提高标签在任何底色上的可读性）
                    int pad = 6;
                    Cv2.Rectangle(result,
                        new CvPoint(textX - pad, textY - textSize.Height - pad),
                        new CvPoint(textX + textSize.Width + pad, textY + baseline + pad),
                        new Scalar(0, 0, 0), -1); // 实心黑底

                    // 绘制白色描边文字（双层：先黑色描边再白色填充）
                    Cv2.PutText(result, label, new CvPoint(textX, textY),
                        HersheyFonts.HersheySimplex, fontScale, new Scalar(0, 0, 0), thickness + 2);
                    Cv2.PutText(result, label, new CvPoint(textX, textY),
                        HersheyFonts.HersheySimplex, fontScale, new Scalar(255, 255, 255), thickness);
                }
            }

            return (result, layout);
        }

        /// <summary>
        /// 裁切指定区域并放大，叠加微观子网格（如 3×3，标签 1~9）
        /// </summary>
        /// <param name="image">原始截图（不会被修改）</param>
        /// <param name="region">要裁切的区域（原图坐标系）</param>
        /// <param name="subRows">子网格行数（默认 3）</param>
        /// <param name="subCols">子网格列数（默认 3）</param>
        /// <param name="minWidth">裁切后的最小宽度（保证 LLM 看得清楚）</param>
        /// <returns>放大后带子网格的图像 + 各子网格在原图坐标系中的像素区域</returns>
        public static (Mat ZoomedImage, Dictionary<string, CvRect> SubLayout) DrawMicroGrid(
            Mat image, CvRect region, int subRows = 3, int subCols = 3, int minWidth = 640)
        {
            // 安全边界裁剪（防止越界）
            int clampX = Math.Max(0, region.X);
            int clampY = Math.Max(0, region.Y);
            int clampW = Math.Min(region.Width, image.Width - clampX);
            int clampH = Math.Min(region.Height, image.Height - clampY);
            var safeRegion = new CvRect(clampX, clampY, clampW, clampH);

            using var cropped = new Mat(image, safeRegion);

            // 等比缩放到不低于 minWidth（保持长宽比）
            double scale = 1.0;
            if (cropped.Width < minWidth)
            {
                scale = (double)minWidth / cropped.Width;
            }

            int newW = (int)(cropped.Width * scale);
            int newH = (int)(cropped.Height * scale);

            var zoomed = new Mat();
            Cv2.Resize(cropped, zoomed, new CvSize(newW, newH), 0, 0, InterpolationFlags.Cubic);

            var subLayout = new Dictionary<string, CvRect>();
            int cellW = newW / subCols;
            int cellH = newH / subRows;

            // 根据缩放后图像尺寸自适应字体大小
            double fontScale = Math.Max(0.6, Math.Min(2.5, newW / 640.0));
            int thickness = Math.Max(1, (int)(fontScale * 2));
            int lineThick = Math.Max(1, (int)(fontScale * 1.5));

            // 绘制子网格线（青色，区分宏观白色）
            for (int r = 1; r < subRows; r++)
            {
                int y = r * cellH;
                Cv2.Line(zoomed, new CvPoint(0, y), new CvPoint(newW, y),
                    new Scalar(0, 255, 255), lineThick, LineTypes.AntiAlias);
            }
            for (int c = 1; c < subCols; c++)
            {
                int x = c * cellW;
                Cv2.Line(zoomed, new CvPoint(x, 0), new CvPoint(x, newH),
                    new Scalar(0, 255, 255), lineThick, LineTypes.AntiAlias);
            }

            // 绘制数字标签并记录布局（映射回原图坐标系）
            int idx = 1;
            for (int r = 0; r < subRows; r++)
            {
                for (int c = 0; c < subCols; c++)
                {
                    string label = idx.ToString();

                    // 缩放后图片上的子网格位置
                    int zx = c * cellW;
                    int zy = r * cellH;
                    int cx = zx + cellW / 2;
                    int cy = zy + cellH / 2;

                    // 逆推回原图坐标系（缩放前 → 偏移到原图位置）
                    int origX = safeRegion.X + (int)(zx / scale);
                    int origY = safeRegion.Y + (int)(zy / scale);
                    int origW = (int)(cellW / scale);
                    int origH = (int)(cellH / scale);
                    subLayout[label] = new CvRect(origX, origY, origW, origH);

                    // 绘制标签（黑底青字）
                    var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, fontScale, thickness, out int baseline);
                    int textX = cx - textSize.Width / 2;
                    int textY = cy + textSize.Height / 2;

                    int pad = 5;
                    Cv2.Rectangle(zoomed,
                        new CvPoint(textX - pad, textY - textSize.Height - pad),
                        new CvPoint(textX + textSize.Width + pad, textY + baseline + pad),
                        new Scalar(0, 0, 0), -1);

                    Cv2.PutText(zoomed, label, new CvPoint(textX, textY),
                        HersheyFonts.HersheySimplex, fontScale, new Scalar(0, 0, 0), thickness + 2);
                    Cv2.PutText(zoomed, label, new CvPoint(textX, textY),
                        HersheyFonts.HersheySimplex, fontScale, new Scalar(0, 255, 255), thickness);

                    idx++;
                }
            }

            return (zoomed, subLayout);
        }
    }
}
