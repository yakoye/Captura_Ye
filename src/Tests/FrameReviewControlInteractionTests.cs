using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Captura.FFmpeg;
using Xunit;

namespace Captura.Tests
{
    public partial class FrameReviewControlTests
    {
        [Fact]
        public void PreviewClickAnchorsRangeAcrossScrollingWithoutChangingExistingSelection()
        {
            OnSta(control => {
                var tiles = Field<List<FrameTile>>(control, "_tiles");
                var strip = Named<ListBox>(control, "FrameStrip"); strip.ItemsSource = tiles; Layout(control);
                var selection = Field<FrameSelection>(control, "_selection"); selection.Toggle(1);
                var fifth = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromIndex(4);
                Descendant<Image>(fifth).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Assert.Equal(4, selection.Current); Assert.Equal(4, selection.Anchor); Assert.Equal(new[] { 1 }, selection.Snapshot());
                var scroll = Descendant<ScrollViewer>(strip); scroll.ScrollToHorizontalOffset(12); Layout(control);
                Assert.Equal(4, selection.Anchor);
                Call(control, "ActivateTile", tiles[14], 1, ModifierKeys.Shift);
                Assert.Equal(Enumerable.Range(4, 11), selection.Snapshot()); Assert.Equal(14, selection.Current);
                Call(control, "ActivateTile", tiles[19], 1, ModifierKeys.Shift);
                Assert.Equal(Enumerable.Range(4, 16), selection.Snapshot()); Assert.Equal(4, selection.Anchor);
                Arrow(control, Key.Left); Assert.Equal(18, selection.Current); Assert.Equal(4, selection.Anchor);
                Assert.Equal(Enumerable.Range(4, 16), selection.Snapshot());
            }, 40);
        }

        [Fact]
        public void DoubleClickTogglesOnlyClickedFrameAndModifiedDoubleClickDoesNotToggleTwice()
        {
            OnSta(control => {
                var tiles = Field<List<FrameTile>>(control, "_tiles");
                var selection = Field<FrameSelection>(control, "_selection"); selection.Toggle(0);
                Call(control, "ActivateTile", tiles[4], 1, ModifierKeys.None);
                Assert.Equal(new[] { 0 }, selection.Snapshot());
                Call(control, "ActivateTile", tiles[4], 2, ModifierKeys.None);
                Assert.Equal(new[] { 0, 4 }, selection.Snapshot());
                Call(control, "ActivateTile", tiles[4], 1, ModifierKeys.None);
                Call(control, "ActivateTile", tiles[4], 2, ModifierKeys.None);
                Assert.Equal(new[] { 0 }, selection.Snapshot());
                Call(control, "ActivateTile", tiles[6], 1, ModifierKeys.Control);
                Call(control, "ActivateTile", tiles[6], 2, ModifierKeys.Control);
                Assert.Equal(new[] { 0, 6 }, selection.Snapshot());
                Call(control, "ActivateTile", tiles[3], 1, ModifierKeys.None);
                Call(control, "ActivateTile", tiles[5], 1, ModifierKeys.Control | ModifierKeys.Shift);
                Assert.Equal(new[] { 0, 3, 4, 5, 6 }, selection.Snapshot());
                Click(control, "ClearTimelineButton");
                Assert.Empty(selection.Selected); Assert.Equal(5, selection.Current);
                Assert.False(Named<Button>(control, "ClearTimelineButton").IsEnabled);
            });
        }

        [Theory]
        [InlineData(960, 640, 640, 480)]
        [InlineData(960, 640, 1920, 1080)]
        [InlineData(860, 560, 1080, 1920)]
        public void CompactPreviewMatchesSourceAspectAndKeepsCardsAtSeventyFivePercent(int width, int height, int sourceWidth, int sourceHeight)
        {
            OnSta(control => {
                var pixels = new byte[sourceWidth * sourceHeight * 4];
                for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i / 4 % sourceWidth * 255 / sourceWidth); pixels[i + 1] = 100; pixels[i + 2] = 80; pixels[i + 3] = 255; }
                var image = BitmapSource.Create(sourceWidth, sourceHeight, 96, 96, PixelFormats.Bgra32, null, pixels, sourceWidth * 4);
                Named<Image>(control, "Still").Source = image;
                Layout(control, width, height); Call(control, "FitPreviewSurface"); Layout(control, width, height);
                var host = Named<Grid>(control, "PreviewHost"); var surface = Named<Border>(control, "PreviewSurface");
                Assert.Equal((double)sourceWidth / sourceHeight, (surface.ActualWidth - 12) / (surface.ActualHeight - 12), 3);
                Assert.True(surface.ActualWidth <= host.ActualWidth + 1); Assert.True(surface.ActualHeight <= host.ActualHeight + 1);
                var strip = Named<ListBox>(control, "FrameStrip"); strip.ItemsSource = Field<List<FrameTile>>(control, "_tiles"); Layout(control, width, height);
                Assert.Equal(117, ((ListBoxItem)strip.ItemContainerGenerator.ContainerFromIndex(0)).ActualWidth);
                var firstTile = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.True(firstTile.ActualHeight <= Descendant<ScrollViewer>(strip).ViewportHeight + 1, "帧卡片和时间不能被水平滚动条裁切");
                Assert.Equal(196, Named<Grid>(control, "ExportSidebar").ActualWidth);
                SaveCurrentRender(control, "compact-" + sourceWidth + "x" + sourceHeight);
            });
        }

        [Fact]
        public void CollapseAndExpandPreserveWindowWidthAndRememberExpandedHeight()
        {
            OnSta(control => {
                var window = new Window { Width = 960, Height = 780 };
                try {
                    var layout = new ReviewWindowLayout();
                    layout.Apply(window, true, new Size(1920, 1080));
                    window.Width = 1030; window.Height = 820;
                    for (var i = 0; i < 3; i++) {
                        layout.Apply(window, false, new Size(1920, 1080));
                        Assert.Equal(1030, window.Width); Assert.Equal(90, window.Height);
                        layout.Apply(window, true, new Size(1920, 1080));
                        Assert.Equal(1030, window.Width); Assert.Equal(820, window.Height);
                    }
                    Assert.Null(PresentationSource.FromVisual(window));
                } finally { window.Close(); }
            });
        }

        [Fact]
        public void RecordingStartCollapsesBeforeEncoderInitialization()
        {
            OnSta(control => {
                var model = ServiceProvider.Get<Captura.ViewModels.RecordingViewModel>();
                var settings = model.Settings; var originalExpanded = settings.UI.Expanded;
                var writers = Field<Captura.ViewModels.VideoWritersViewModel>(model, "_videoWritersViewModel");
                var originalWriter = writers.SelectedVideoWriter;
                var writer = new Moq.Mock<Captura.Video.IVideoWriterItem>();
                var reachedEncoder = false;
                writer.Setup(w => w.Extension).Returns(() => {
                    reachedEncoder = true; Assert.False(settings.UI.Expanded);
                    throw new OperationCanceledException("测试在编码器初始化入口结束，不开始真实录制");
                });
                try {
                    writers.SelectedVideoWriter = writer.Object; settings.UI.Expanded = true;
                    var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => Call(model, "StartRecording"));
                    Assert.IsType<OperationCanceledException>(error.InnerException);
                    Assert.True(reachedEncoder); Assert.False(settings.UI.Expanded);
                } finally { writers.SelectedVideoWriter = originalWriter; settings.UI.Expanded = originalExpanded; }
            });
        }

        [Theory]
        [InlineData(0, "png")]
        [InlineData(1, "mp4")]
        [InlineData(2, "gif")]
        public void ExportFormatSelectionProducesOnlyRequestedFiles(int selectedIndex, string kind)
        {
            OnSta(control => {
                var executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p.Trim('"'), "ffmpeg.exe")).FirstOrDefault(File.Exists);
                Assert.NotNull(executable);
                var root = Path.Combine(Path.GetTempPath(), "Captura-wpf-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                try {
                    var file = Path.Combine(root, "输入 视频.mp4");
                    Complete(FrameReviewService.RunBytesAsync(executable, "-v error -nostdin -f lavfi -i testsrc2=size=96x64:rate=10:duration=1 " + FrameReviewService.Quote(file), CancellationToken.None));
                    var service = new FrameReviewService(executable); var opening = service.OpenAsync(file, CancellationToken.None); Complete(opening);
                    Set(control, "_media", opening.Result); Set(control, "_service", service);
                    var selection = Field<FrameSelection>(control, "_selection"); selection.Toggle(0); selection.Toggle(4); selection.Toggle(9); Call(control, "RefreshSelection");
                    Named<ComboBox>(control, "ExportFormat").SelectedIndex = selectedIndex;
                    Named<TextBox>(control, "OutputRoot").Text = root;
                    Named<TextBox>(control, "ExportFps").Text = kind == "png" ? "不是帧率" : "5";
                    var exporting = (Task)control.GetType().GetMethod("ExportSelectionAsync", Private).Invoke(control, null); Complete(exporting);
                    var output = Field<string>(control, "_output"); Assert.NotNull(output);
                    Assert.DoesNotContain("失败", Named<TextBlock>(control, "Status").Text);
                    if (kind == "png") {
                        Assert.Equal(3, Directory.GetFiles(output, "*.png").Length); Assert.Equal(4, Directory.GetFiles(output).Length);
                        Assert.Equal(Visibility.Collapsed, Named<Button>(control, "OutputFileButton").Visibility);
                    } else {
                        Assert.Empty(Directory.GetFiles(output, "*.png", SearchOption.AllDirectories)); Assert.Equal(2, Directory.GetFiles(output).Length);
                        var result = service.OpenAsync(Path.Combine(output, "selected." + kind), CancellationToken.None); Complete(result);
                        Assert.Equal(3, result.Result.Times.Length); Assert.InRange(result.Result.Duration, 0.59, 0.61);
                        Assert.Equal(Visibility.Visible, Named<Button>(control, "OutputFileButton").Visibility);
                        Assert.EndsWith("selected." + kind, Named<TextBlock>(control, "Status").Text);
                    }
                    Assert.Equal(new[] { 0, 4, 9 }, selection.Snapshot());
                } finally { Directory.Delete(root, true); }
            });
        }
        static void Complete(Task task)
        {
            var frame = new DispatcherFrame(); var timeout = DateTime.UtcNow.AddSeconds(20);
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (s, e) => { if (task.IsCompleted || DateTime.UtcNow > timeout) frame.Continue = false; };
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            Assert.True(task.IsCompleted, "离屏媒体操作超时"); task.GetAwaiter().GetResult();
        }
    }
}
