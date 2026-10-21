using System;

namespace Captura
{
    /// <summary>以项目为单位，只在当前帧超出完整可见范围时滚动。</summary>
    public static class FrameViewport
    {
        public static double Reveal(int current, double offset, double viewport, double extent)
        {
            if (current < 0 || viewport <= 0 || extent <= 0) return offset;
            var visible = Math.Max(1, Math.Floor(viewport));
            var target = offset;
            if (current < offset) target = current;
            else if (current + 1 > offset + visible) target = current - visible + 1;
            return Math.Max(0, Math.Min(Math.Max(0, extent - viewport), target));
        }
    }
}
