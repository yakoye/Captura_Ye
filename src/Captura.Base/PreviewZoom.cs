using System;

namespace Captura
{
    /// <summary>缩放倍率相对适应窗口；锚点计算同时处理留白和滚动后的坐标。</summary>
    public static class PreviewZoom
    {
        public const double Minimum = 0.25, Maximum = 8;
        public static double Clamp(double scale) => double.IsNaN(scale) ? 1 : Math.Max(Minimum, Math.Min(Maximum, scale));
        public static double Step(double scale, double notches) => Clamp(scale * Math.Pow(1.25, Math.Max(-32, Math.Min(32, notches))));
        public static double Anchor(double point, double offset, double content, double viewport)
        {
            if (content <= 0) return 0.5;
            var margin = Math.Max(0, (viewport - content) / 2);
            return Math.Max(0, Math.Min(1, (point + offset - margin) / content));
        }
        public static double Offset(double anchor, double point, double content, double viewport)
        {
            var margin = Math.Max(0, (viewport - content) / 2);
            return Math.Max(0, Math.Min(Math.Max(0, content - viewport), anchor * content + margin - point));
        }
    }
}
