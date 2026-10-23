using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Captura.FFmpeg;
using Xunit;

namespace Captura.Tests
{
    // 实例化产品 XAML 和控件，在 STA 线程布局及派发事件；不 Show 窗口或注入桌面输入。
    public class FrameReviewControlTests
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        static T Named<T>(FrameReviewControl control, string name) => (T)control.FindName(name);

        static void Layout(FrameReviewControl control, double width = 1120, double height = 700)
        {
            control.Width = width; control.Height = height;
            control.Measure(new Size(width, height));
            control.Arrange(new Rect(0, 0, width, height)); control.UpdateLayout();
        }
        static void OnSta(Action<FrameReviewControl> test, int count = 10)
        {
            Exception error = null;
            var thread = new Thread(() => {
                FrameReviewControl control = null;
                try {
                    control = new FrameReviewControl();
                    control.Resources.MergedDictionaries.Add(new ResourceDictionary {
                        Source = new Uri("/FirstFloor.ModernUI;component/Assets/ModernUI.xaml", UriKind.Relative)
                    });
                    control.Resources.MergedDictionaries.Add(new ResourceDictionary {
                        Source = new Uri("/FirstFloor.ModernUI;component/Assets/ModernUI.Light.xaml", UriKind.Relative)
                    });
                    control.Background = (Brush)control.FindResource("WindowBackground");
                    var times = Enumerable.Range(0, count).Select(i => i * 0.1).ToArray();
                    Set(control, "_media", new ReviewMedia { Times = times, Duration = count * 0.1 });
                    Set(control, "_timeline", new FrameTimeline(times, count * 0.1));
                    Field<FrameSelection>(control, "_selection").Reset(times.Length);
                    var tiles = times.Select((time, i) => new FrameTile(i, time)).ToList();
                    Set(control, "_tiles", tiles);
                    var cache = Field<Dictionary<int, BitmapSource>>(control, "_cache");
                    foreach (var tile in tiles) {
                        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                            new byte[] { 40, 80, 180, 255, 180, 80, 40, 255, 180, 80, 40, 255, 40, 80, 180, 255 }, 8);
                        bitmap.Freeze(); cache[tile.Index] = bitmap; tile.Thumbnail = bitmap;
                    }
                    Named<Slider>(control, "Timeline").Maximum = count - 1;
                    Named<StackPanel>(control, "EmptyPanel").Visibility = Visibility.Collapsed;
                    Call(control, "RefreshSelection");
                    Layout(control); Call(control, "SizeZoomCanvas"); Layout(control);
                    test(control);
                }
                catch (Exception ex) { error = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex; }
                finally { control?.Dispose(); System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "离屏 WPF 测试超时");
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }
        static void Click(FrameReviewControl control, string name) =>
            Named<ButtonBase>(control, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        [Fact]
        public void ActualZoomButtonsResetBothScrollbarsAfterEnlargingAndPanning()
        {
            OnSta(control => {
                Assert.Null(PresentationSource.FromVisual(control));
                var viewport = Named<ScrollViewer>(control, "PreviewViewport");
                var canvas = Named<Grid>(control, "PreviewCanvas");
                for (var i = 0; i < 4; i++) Click(control, "ZoomInButton");
                Layout(control);
                Assert.True(viewport.ScrollableWidth > 0); Assert.True(viewport.ScrollableHeight > 0);
                viewport.ScrollToHorizontalOffset(150); viewport.ScrollToVerticalOffset(90); Layout(control);
                Assert.True(viewport.HorizontalOffset > 0); Assert.True(viewport.VerticalOffset > 0);
                SaveRender(control, "zoom-enlarged");
                Click(control, "ZoomFitButton"); Layout(control);
                Assert.Equal("1×", Named<TextBlock>(control, "ZoomLabel").Text);
                Assert.Equal(Visibility.Collapsed, viewport.ComputedHorizontalScrollBarVisibility);
                Assert.Equal(Visibility.Collapsed, viewport.ComputedVerticalScrollBarVisibility);
                Assert.Equal(0, viewport.HorizontalOffset); Assert.Equal(0, viewport.VerticalOffset);
                Assert.True(canvas.ActualWidth <= viewport.ViewportWidth + 1);
                Assert.True(canvas.ActualHeight <= viewport.ViewportHeight + 1);
                SaveRender(control, "zoom-fit");
                Click(control, "ZoomOutButton"); Layout(control);
                Assert.Equal("0.8×", Named<TextBlock>(control, "ZoomLabel").Text);
                Assert.True(canvas.ActualWidth < viewport.ViewportWidth);
                Assert.Equal(Visibility.Collapsed, viewport.ComputedHorizontalScrollBarVisibility);
                Assert.Equal(Visibility.Collapsed, viewport.ComputedVerticalScrollBarVisibility);
                SaveRender(control, "zoom-reduced");
            });
        }

        [Fact]
        public void RoutedArrowKeysFollowLastFrameOneSecondAndFiveSecondButtons()
        {
            OnSta(control => {
                var selection = Field<FrameSelection>(control, "_selection"); selection.Toggle(1);
                var slider = Named<Slider>(control, "Timeline"); slider.Value = 100;
                Click(control, "NextButton"); Assert.Equal(101, selection.Current);
                Arrow(control, Key.Right); Assert.Equal(102, selection.Current);
                Arrow(control, Key.Left); Assert.Equal(101, selection.Current);
                ClickByTooltip(control, "后1秒"); Assert.Equal(111, selection.Current);
                Arrow(control, Key.Right); Assert.Equal(121, selection.Current);
                Arrow(control, Key.Left); Assert.Equal(111, selection.Current);
                ClickByTooltip(control, "后5秒"); Assert.Equal(161, selection.Current);
                Arrow(control, Key.Right); Assert.Equal(211, selection.Current);
                Arrow(control, Key.Left); Assert.Equal(161, selection.Current);
                Click(control, "PreviousButton"); Assert.Equal(160, selection.Current);
                Arrow(control, Key.Left); Assert.Equal(159, selection.Current);
                Assert.Equal(new[] { 1 }, selection.Snapshot());
                Assert.Null(PresentationSource.FromVisual(control));
            }, 300);
        }

        [Fact]
        public void SliderValueChangesImmediatelyRefreshCachedFramesWithoutClearingSelectionOrZoom()
        {
            OnSta(control => {
                var selection = Field<FrameSelection>(control, "_selection"); selection.Toggle(1); selection.Toggle(3);
                Click(control, "ZoomInButton");
                var slider = Named<Slider>(control, "Timeline");
                foreach (var index in new[] { 2, 8, 4, 0, 9 }) {
                    slider.Value = index;
                    Assert.Equal(index, selection.Current);
                    Assert.Same(Field<Dictionary<int, BitmapSource>>(control, "_cache")[index], Named<Image>(control, "Still").Source);
                    Assert.Equal(new[] { 1, 3 }, selection.Snapshot());
                    Assert.Equal("1.25×", Named<TextBlock>(control, "ZoomLabel").Text);
                }
            });
        }

        [Fact]
        public void ActualZoomButtonsDisableAtBoundsAndResetKeepsViewedFrame()
        {
            OnSta(control => {
                Call(control, "Step", 4);
                var source = Named<Image>(control, "Still").Source;
                for (var i = 0; i < 40; i++) Click(control, "ZoomInButton");
                Assert.Equal("8×", Named<TextBlock>(control, "ZoomLabel").Text);
                Assert.False(Named<Button>(control, "ZoomInButton").IsEnabled);
                for (var i = 0; i < 50; i++) Click(control, "ZoomOutButton");
                Assert.Equal("0.25×", Named<TextBlock>(control, "ZoomLabel").Text);
                Assert.False(Named<Button>(control, "ZoomOutButton").IsEnabled);
                Click(control, "ZoomFitButton");
                Assert.True(Named<Button>(control, "ZoomInButton").IsEnabled);
                Assert.True(Named<Button>(control, "ZoomOutButton").IsEnabled);
                Assert.Equal(4, Field<FrameSelection>(control, "_selection").Current);
                Assert.Same(source, Named<Image>(control, "Still").Source);
            });
        }

        // PresentationSource 仅供 KeyEventArgs 构造，无 HWND、前台焦点或系统输入。
        sealed class OffscreenSource : PresentationSource
        {
            public override Visual RootVisual { get; set; }
            public override bool IsDisposed => false;
            protected override CompositionTarget GetCompositionTargetCore() => null;
        }
        static void Arrow(FrameReviewControl control, Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, new OffscreenSource { RootVisual = control }, 0, key) {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            control.RaiseEvent(args); Assert.True(args.Handled);
        }
        static void ClickByTooltip(DependencyObject parent, string tooltip)
        {
            var button = Descendants<ButtonBase>(parent).Single(b => Equals(b.ToolTip, tooltip));
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
        static void SaveRender(FrameReviewControl control, string name)
        {
            var directory = Environment.GetEnvironmentVariable("CAPTURA_WPF_TEST_ARTIFACTS");
            if (string.IsNullOrEmpty(directory)) return;
            Call(control, "Step", 0); Layout(control);
            var bitmap = new RenderTargetBitmap((int)control.ActualWidth, (int)control.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(control); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(directory);
            using (var output = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(output);
        }

        [Fact]
        public void ResizePreservesZoomAndSourceWithoutChangingMarks()
        {
            OnSta(control => {
                var selection = Field<FrameSelection>(control, "_selection");
                selection.Toggle(0); selection.Toggle(2); Call(control, "Step", 6);
                var source = Named<Image>(control, "Still").Source;
                Click(control, "ZoomInButton"); Layout(control, 1500, 900);
                var surface = Named<Border>(control, "PreviewSurface");
                var canvas = Named<Grid>(control, "PreviewCanvas");
                Assert.Equal((surface.ActualWidth - 12) * 1.25, canvas.Width, 3);
                Assert.Equal((surface.ActualHeight - 12) * 1.25, canvas.Height, 3);
                Assert.Same(source, Named<Image>(control, "Still").Source);
                Assert.Equal(new[] { 0, 2 }, selection.Snapshot()); Assert.Equal(6, selection.Current);
                Assert.Null(PresentationSource.FromVisual(control));
            });
        }

        [Fact]
        public void RealThumbnailAndCheckboxEventsKeepOneThreeSevenIndependentOfPreview()
        {
            OnSta(control => {
                var tiles = Field<List<FrameTile>>(control, "_tiles");
                var strip = Named<ListBox>(control, "FrameStrip");
                strip.ItemsSource = tiles; Layout(control);
                var selection = Field<FrameSelection>(control, "_selection");
                Action<int> check = index => {
                    var item = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromIndex(index);
                    Assert.NotNull(item);
                    var checkbox = Descendant<CheckBox>(item); Assert.NotNull(checkbox);
                    var before = selection.Snapshot();
                    checkbox.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
                        RoutedEvent = Mouse.PreviewMouseDownEvent
                    });
                    Assert.Equal(before, selection.Snapshot());
                    checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                };
                Action<int> view = index => {
                    var item = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromIndex(index);
                    Assert.NotNull(item);
                    var image = Descendant<Image>(item); Assert.NotNull(image);
                    image.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
                        RoutedEvent = Mouse.PreviewMouseDownEvent
                    });
                };
                check(0); view(2);
                Assert.Equal(2, selection.Current); Assert.Equal(new[] { 0 }, selection.Snapshot());
                check(2); view(6); check(6);
                Assert.Equal(6, selection.Current); Assert.Equal(new[] { 0, 2, 6 }, selection.Snapshot());
                Assert.True(tiles[6].IsCurrent); Assert.False(tiles[0].IsCurrent);
                Call(control, "Step", 1);
                Assert.Equal(7, selection.Current); Assert.Equal(new[] { 0, 2, 6 }, selection.Snapshot());
                Assert.Same(Field<Dictionary<int, BitmapSource>>(control, "_cache")[7], Named<Image>(control, "Still").Source);
                Click(control, "ZoomInButton"); Click(control, "ZoomFitButton");
                Assert.Equal(7, selection.Current); Assert.Equal(new[] { 0, 2, 6 }, selection.Snapshot());
                SaveRender(control, "selection-one-three-seven-preview-eight");
            });
        }
        static T Descendant<T>(DependencyObject node) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T result) return result;
                var nested = Descendant<T>(child); if (nested != null) return nested;
            }
            return null;
        }
    }
}
