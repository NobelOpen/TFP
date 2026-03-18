namespace TaskFlow.Models.AiFlow
{
    /// <summary>
    /// Vision 模型坐标标定记录
    /// </summary>
    public class CalibrationData
    {
        /// <summary>模型 ID</summary>
        public string ModelId { get; set; } = "";

        /// <summary>标定时的图像宽度</summary>
        public int Width { get; set; }

        /// <summary>标定时的图像高度</summary>
        public int Height { get; set; }

        /// <summary>X 轴缩放系数</summary>
        public double ScaleX { get; set; } = 1.0;

        /// <summary>Y 轴缩放系数</summary>
        public double ScaleY { get; set; } = 1.0;

        /// <summary>X 轴偏移量</summary>
        public double OffsetX { get; set; }

        /// <summary>Y 轴偏移量</summary>
        public double OffsetY { get; set; }

        /// <summary>标定时间</summary>
        public DateTime CalibratedAt { get; set; }

        /// <summary>平均误差（像素）</summary>
        public double AvgError { get; set; }

        /// <summary>采样次数</summary>
        public int SampleCount { get; set; }

        /// <summary>生成存储 Key: modelId_WxH</summary>
        public string Key => $"{ModelId}_{Width}x{Height}";
    }
}
