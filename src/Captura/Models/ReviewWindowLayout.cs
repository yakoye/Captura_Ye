using System;
using System.Windows;

namespace Captura
{
    // 折叠和展开共享同一宽度，只保存展开高度，避免录制时窗口横向跳动。
    public sealed class ReviewWindowLayout
    {
        double _expandedHeight = 780;
        bool _expanded;
        public void Apply(Window window, bool expanded, Size workArea)
        {
            var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            window.MinWidth = Math.Min(860, workArea.Width - 20);
            if (expanded) {
                window.ResizeMode = ResizeMode.CanResize;
                window.MinHeight = Math.Min(640, workArea.Height - 20);
                window.Width = Math.Min(width, workArea.Width - 20);
                window.Height = Math.Min(_expandedHeight, workArea.Height - 20);
            } else {
                if (_expanded && window.WindowState == WindowState.Normal)
                    _expandedHeight = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
                if (window.WindowState == WindowState.Maximized) {
                    if (window.RestoreBounds.Height > 0) _expandedHeight = window.RestoreBounds.Height;
                    window.WindowState = WindowState.Normal;
                }
                window.MinHeight = 86;
                window.Width = Math.Min(width, workArea.Width - 20); window.Height = 90;
                window.ResizeMode = ResizeMode.CanMinimize;
            }
            _expanded = expanded;
        }
    }
}
