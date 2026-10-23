using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Captura
{
    public partial class FrameReviewControl
    {
        double _zoom = 1;
        bool _panning;
        Point _panStart;
        double _panHorizontal, _panVertical;

        void UpdateZoomState()
        {
            if (ZoomInButton == null) return;
            var enabled = _media != null && !_disposed;
            ZoomInButton.IsEnabled = enabled && _zoom < PreviewZoom.Maximum;
            ZoomOutButton.IsEnabled = enabled && _zoom > PreviewZoom.Minimum;
            ZoomFitButton.IsEnabled = enabled;
            ZoomLabel.Text = _zoom.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×";
            PreviewCanvas.Cursor = _zoom > 1 ? Cursors.SizeAll : Cursors.Arrow;
        }
        void SizeZoomCanvas()
        {
            // 适应状态禁用滚动条，防止两条 Auto 滚动条互相挤占空间后无法自行收起。
            var visibility = _zoom > 1 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            PreviewViewport.HorizontalScrollBarVisibility = PreviewViewport.VerticalScrollBarVisibility = visibility;
            PreviewCanvas.Width = Math.Max(1, PreviewSurface.ActualWidth - 12) * _zoom;
            PreviewCanvas.Height = Math.Max(1, PreviewSurface.ActualHeight - 12) * _zoom;
        }
        void ResetPreviewZoom()
        {
            _zoom = 1; EndPan(); SizeZoomCanvas();
            PreviewViewport.ScrollToHorizontalOffset(0); PreviewViewport.ScrollToVerticalOffset(0); UpdateZoomState();
        }
        void SetPreviewZoom(double scale, Point? pointer = null)
        {
            if (_media == null || _disposed) return;
            scale = PreviewZoom.Clamp(scale); if (Math.Abs(scale - _zoom) < 0.00001) return;
            EndPan();
            var point = pointer ?? new Point(PreviewViewport.ViewportWidth / 2, PreviewViewport.ViewportHeight / 2);
            var ax = PreviewZoom.Anchor(point.X, PreviewViewport.HorizontalOffset, PreviewCanvas.Width, PreviewViewport.ViewportWidth);
            var ay = PreviewZoom.Anchor(point.Y, PreviewViewport.VerticalOffset, PreviewCanvas.Height, PreviewViewport.ViewportHeight);
            _zoom = scale; SizeZoomCanvas(); PreviewViewport.UpdateLayout();
            PreviewViewport.ScrollToHorizontalOffset(PreviewZoom.Offset(ax, point.X, PreviewCanvas.Width, PreviewViewport.ViewportWidth));
            PreviewViewport.ScrollToVerticalOffset(PreviewZoom.Offset(ay, point.Y, PreviewCanvas.Height, PreviewViewport.ViewportHeight));
            UpdateZoomState();
        }
        void Preview_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (PreviewCanvas != null) SizeZoomCanvas();
        }
        void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || _media == null) return;
            e.Handled = true;
            SetPreviewZoom(PreviewZoom.Step(_zoom, e.Delta / 120.0), e.GetPosition(PreviewViewport));
        }
        void ZoomIn_Click(object sender, RoutedEventArgs e) { SetPreviewZoom(PreviewZoom.Step(_zoom, 1)); PreviewSurface.Focus(); }
        void ZoomOut_Click(object sender, RoutedEventArgs e) { SetPreviewZoom(PreviewZoom.Step(_zoom, -1)); PreviewSurface.Focus(); }
        void ZoomFit_Click(object sender, RoutedEventArgs e) { ResetPreviewZoom(); PreviewSurface.Focus(); }
        void Preview_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Ancestor<ButtonBase>(e.OriginalSource as DependencyObject) != null || Ancestor<ScrollBar>(e.OriginalSource as DependencyObject) != null) return;
            PreviewSurface.Focus(); e.Handled = true;
            if (_zoom <= 1 || _media == null) return;
            _panStart = e.GetPosition(PreviewSurface); _panHorizontal = PreviewViewport.HorizontalOffset; _panVertical = PreviewViewport.VerticalOffset;
            _panning = PreviewSurface.CaptureMouse();
        }
        void Preview_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_panning) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndPan(); return; }
            var point = e.GetPosition(PreviewSurface);
            PreviewViewport.ScrollToHorizontalOffset(_panHorizontal - (point.X - _panStart.X));
            PreviewViewport.ScrollToVerticalOffset(_panVertical - (point.Y - _panStart.Y)); e.Handled = true;
        }
        void Preview_MouseUp(object sender, MouseButtonEventArgs e) { if (_panning) { EndPan(); e.Handled = true; } }
        void Preview_LostCapture(object sender, MouseEventArgs e) => _panning = false;
        void EndPan() { _panning = false; if (PreviewSurface.IsMouseCaptured) PreviewSurface.ReleaseMouseCapture(); }
    }
}
