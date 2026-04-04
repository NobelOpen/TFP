using System.Collections.Generic;
using OpenCvSharp;

namespace TaskFlow.Models
{
    /// <summary>
    /// 递归网格缩放状态（跨多轮 LLM 调用持久化）
    /// 用于记录当前交互式网格定位的层级、布局与坐标偏移
    /// </summary>
    public class GridZoomState
    {
        /// <summary>当前缩放层级（0=宏观网格, 1=微观子网格, ...）</summary>
        public int ZoomLevel { get; set; }

        /// <summary>宏观网格布局：标签 → 原图像素区域（如 "B3" → Rect）</summary>
        public Dictionary<string, Rect>? MacroLayout { get; set; }

        /// <summary>上一轮选中的宏观网格区域（原图坐标系）</summary>
        public Rect? SelectedRegion { get; set; }

        /// <summary>微观子网格布局：标签 → 原图像素区域（如 "5" → Rect）</summary>
        public Dictionary<string, Rect>? MicroLayout { get; set; }

        /// <summary>截图左上角相对于桌面原点的 X 偏移（绝对物理坐标）</summary>
        public int OffsetX { get; set; }

        /// <summary>截图左上角相对于桌面原点的 Y 偏移（绝对物理坐标）</summary>
        public int OffsetY { get; set; }

        /// <summary>
        /// 从当前状态中解析网格编号对应的绝对屏幕坐标（中心点）
        /// </summary>
        public (int X, int Y)? ResolveAbsoluteCenter(string cellLabel)
        {
            // 优先从微观布局查找（zoom 后的子网格）
            if (MicroLayout != null && MicroLayout.TryGetValue(cellLabel, out var microRect))
            {
                return (microRect.X + microRect.Width / 2 + OffsetX,
                        microRect.Y + microRect.Height / 2 + OffsetY);
            }

            // 其次从宏观布局查找（直接选大格点击）
            if (MacroLayout != null && MacroLayout.TryGetValue(cellLabel, out var macroRect))
            {
                return (macroRect.X + macroRect.Width / 2 + OffsetX,
                        macroRect.Y + macroRect.Height / 2 + OffsetY);
            }

            return null;
        }
    }
}
