using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TaskFlow.Models.TaskCards;
using TaskStatus = TaskFlow.Models.TaskCards.TaskStatus;

namespace TaskFlow.Converters
{
    /// <summary>
    /// 任务状态到颜色转换器
    /// </summary>
    public class TaskStatusToColorConverter : IValueConverter
    {
        // Anthropic 极致纯净风格：状态用高饱和度亮色作为 Accent (边框或图标颜色)
        private static readonly SolidColorBrush IdleBrush = CreateFrozenBrush(Colors.Transparent);
        private static readonly SolidColorBrush RunningBrush = CreateFrozenBrush(74, 156, 214);  // 蓝 #4a9cd6
        private static readonly SolidColorBrush SuccessBrush = CreateFrozenBrush(50, 160, 100);   // 绿 #32a064
        private static readonly SolidColorBrush FailedBrush = CreateFrozenBrush(203, 64, 64);     // 红 #cb4040
        private static readonly SolidColorBrush DefaultBrush = CreateFrozenBrush(Colors.Transparent);

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TaskStatus status)
            {
                return status switch
                {
                    TaskStatus.Idle => IdleBrush,
                    TaskStatus.Running => RunningBrush,
                    TaskStatus.Success => SuccessBrush,
                    TaskStatus.Failed => FailedBrush,
                    _ => DefaultBrush
                };
            }
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 选中状态到边框颜色转换器
    /// </summary>
    public class SelectedToBorderColorConverter : IValueConverter
    {
        // 使用静态缓存并冻结，避免每次绑定都创建新Brush对象
        private static readonly SolidColorBrush SelectedBrush;
        private static readonly SolidColorBrush NormalBrush;

        static SelectedToBorderColorConverter()
        {
            SelectedBrush = new SolidColorBrush(Color.FromRgb(217, 119, 87)); // Anthropic 橙 #d97757
            SelectedBrush.Freeze();
            NormalBrush = new SolidColorBrush(Color.FromRgb(232, 230, 220)); // #e8e6dc
            NormalBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
            {
                return SelectedBrush;
            }
            return NormalBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 折叠状态到图标转换器
    /// </summary>
    public class CollapseIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCollapsed)
            {
                return isCollapsed ? "▶" : "▼";
            }
            return "▼";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 反向布尔到可见性转换器（用于隐藏折叠的卡片）
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isHidden && isHidden)
            {
                return Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 反向布尔转换器（true 转 false，false 转 true）
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }
            return false;
        }
    }

    /// <summary>
    /// 折叠图标可见性转换器（只对IfStart和ForLoopStart显示）
    /// </summary>
    public class CollapseIconVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BranchRole role)
            {
                return (role == BranchRole.IfStart || role == BranchRole.ForLoopStart)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 拖拽手柄可见性转换器
    /// 非分支卡片始终可见，分支卡片只有IfStart/ForLoopStart且折叠时可见
    /// </summary>
    public class DragHandleVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return Visibility.Visible;

            var branchRole = values[0] as BranchRole? ?? BranchRole.None;
            var isCollapsed = values[1] as bool? ?? false;

            // 非分支卡片始终可拖拽
            if (branchRole == BranchRole.None)
            {
                return Visibility.Visible;
            }

            // IfStart或ForLoopStart在折叠状态下可拖拽
            if ((branchRole == BranchRole.IfStart || branchRole == BranchRole.ForLoopStart) && isCollapsed)
            {
                return Visibility.Visible;
            }

            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 布尔到可见性转换器
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }

    /// <summary>
    /// Mat到BitmapSource转换器 — 直接从 Mat 像素数据创建 BitmapSource，跳过 GDI Bitmap 中间步骤
    /// </summary>
    public class MatToBitmapSourceConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is OpenCvSharp.Mat mat && !mat.Empty())
            {
                try
                {
                    // 根据通道数选择像素格式
                    System.Windows.Media.PixelFormat pixelFormat = mat.Channels() switch
                    {
                        1 => PixelFormats.Gray8,
                        3 => PixelFormats.Bgr24,
                        4 => PixelFormats.Bgra32,
                        _ => PixelFormats.Bgr24
                    };

                    // 确保数据连续
                    OpenCvSharp.Mat src = mat.IsContinuous() ? mat : mat.Clone();
                    try
                    {
                        var bitmapSource = System.Windows.Media.Imaging.BitmapSource.Create(
                            src.Width, src.Height,
                            96, 96,
                            pixelFormat, null,
                            src.Data, (int)(src.Step() * src.Height), (int)src.Step());
                        bitmapSource.Freeze(); // 冻结以允许跨线程访问
                        return bitmapSource;
                    }
                    finally
                    {
                        // 仅释放我们自己创建的克隆
                        if (!ReferenceEquals(src, mat))
                            src.Dispose();
                    }
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 零值到可见性转换器（用于显示空状态提示）
    /// </summary>
    public class ZeroToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && count == 0)
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 缩进等级到左边距转换器（用于分支内卡片的视觉缩进）
    /// 同时包含底部阴影模拟的 2px 额外右下边距
    /// </summary>
    public class IndentLevelToMarginConverter : IValueConverter
    {
        // 缓存常用缩进级别的 Thickness 值，减少 GC 压力
        private static readonly Dictionary<int, Thickness> _cache = new();
        private static readonly Thickness DefaultMargin = new(4, 4, 6, 6);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int indentLevel && indentLevel > 0)
            {
                if (!_cache.TryGetValue(indentLevel, out var margin))
                {
                    margin = new Thickness(indentLevel * 24, 4, 6, 6);
                    _cache[indentLevel] = margin;
                }
                return margin;
            }
            return DefaultMargin;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 任务状态到图标转换器（替代 DataTrigger，减少模板复杂度）
    /// </summary>
    public class TaskStatusToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TaskStatus status)
            {
                return status switch
                {
                    TaskStatus.Running => "⏳",
                    TaskStatus.Success => "✓",
                    TaskStatus.Failed => "✗",
                    _ => ""
                };
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Null/空Mat到可见性转换器（值为null或空Mat时Visible，否则Collapsed）
    /// </summary>
    public class NullToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Visible;
            if (value is string str && string.IsNullOrEmpty(str)) return Visibility.Visible;
            if (value is OpenCvSharp.Mat mat && mat.Empty()) return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 非 null/非空集合时 Visible，否则 Collapsed（用于内嵌配置气泡显隐）
    /// </summary>
    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;
            if (value is System.Collections.ICollection col && col.Count == 0) return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 字符串转大写转换器
    /// </summary>
    public class ToUpperConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                return str.ToUpper();
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    /// <summary>
    /// 可滚动区域转可见性转换器（大于0为Visible，否则Collapsed）
    /// </summary>
    public class ScrollableToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double scrollableWidth && scrollableWidth > 0)
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 非空字符串到可见性转换器（用于思考过程区域的显隐）
    /// </summary>
    public class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && !string.IsNullOrEmpty(str))
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
