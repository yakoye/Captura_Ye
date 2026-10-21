using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Captura.FFmpeg;
using Xunit;

namespace Captura.Tests
{
    public class FrameReviewTests
    {
        [Fact]
        public void ControlClickKeepsArbitraryDiscreteFramesAndCanRemoveOne()
        {
            var selection = new FrameSelection(); selection.Reset(100);
            selection.Click(2, false, false); selection.Click(17, true, false); selection.Click(63, true, false);
            Assert.Equal(new[] { 2, 17, 63 }, selection.Snapshot());
            selection.Click(17, true, false);
            Assert.Equal(new[] { 2, 63 }, selection.Snapshot());
            Assert.Equal(17, selection.Current);
        }
        [Fact]
        public void ViewingAndPlaybackPositionsDoNotReplaceSelection()
        {
            var selection = new FrameSelection(); selection.Reset(400);
            selection.Toggle(3); selection.Toggle(70); selection.Toggle(399);
            for (var i = 0; i < 400; i++) selection.View(i);
            Assert.Equal(new[] { 3, 70, 399 }, selection.Snapshot());
            Assert.Equal(399, selection.Anchor);
        }
        [Fact]
        public void ShiftRangeUsesAnchorAndCanShrinkInEitherDirection()
        {
            var selection = new FrameSelection(); selection.Reset(100);
            selection.Click(20, false, false); selection.Click(25, false, true);
            Assert.Equal(Enumerable.Range(20, 6), selection.Snapshot());
            selection.Click(22, false, true);
            Assert.Equal(new[] { 20, 21, 22 }, selection.Snapshot());
            selection.Click(18, false, true);
            Assert.Equal(new[] { 18, 19, 20 }, selection.Snapshot());
            Assert.Equal(20, selection.Anchor);
        }
        [Fact]
        public void ControlShiftAddsRangeToExistingDiscreteSelection()
        {
            var selection = new FrameSelection(); selection.Reset(100);
            selection.Toggle(1); selection.Toggle(50); selection.Click(53, true, true);
            Assert.Equal(new[] { 1, 50, 51, 52, 53 }, selection.Snapshot());
        }
        [Fact]
        public void PlainClickReplacesSelectionWhileCheckboxToggleAdds()
        {
            var selection = new FrameSelection(); selection.Reset(50);
            selection.Toggle(0); selection.Toggle(7); selection.Toggle(49);
            selection.Click(10, false, false);
            Assert.Equal(new[] { 10 }, selection.Snapshot());
            selection.Toggle(20); Assert.Equal(new[] { 10, 20 }, selection.Snapshot());
        }
        [Fact]
        public void ExportSnapshotIsStableWhileLiveSelectionChanges()
        {
            var selection = new FrameSelection(); selection.Reset(30);
            selection.Toggle(19); selection.Toggle(4);
            var snapshot = selection.Snapshot(); selection.Clear(); selection.SelectAll();
            Assert.Equal(new[] { 4, 19 }, snapshot);
        }
        [Fact]
        public void LoadingAnotherMediaClearsSelectionAndAnchor()
        {
            var selection = new FrameSelection(); selection.Reset(1000); selection.Toggle(999);
            selection.Reset(2);
            Assert.Empty(selection.Selected); Assert.Equal(0, selection.Current); Assert.Equal(0, selection.Anchor);
            Assert.Equal(2, selection.FrameCount);
        }
        [Fact]
        public void InvalidRangeDoesNotDestroyExistingSelection()
        {
            var selection = new FrameSelection(); selection.Reset(10); selection.Toggle(5);
            Assert.Throws<ArgumentOutOfRangeException>(() => selection.SelectRange(2, 10, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => selection.Toggle(-1));
            Assert.Equal(new[] { 5 }, selection.Snapshot());
        }
        [Fact]
        public void ThousandsOfSparseFramesKeepSourceOrder()
        {
            var selection = new FrameSelection(); selection.Reset(10000);
            var expected = Enumerable.Range(0, 10000).Where(i => i % 7 == 0).ToArray();
            foreach (var i in expected.Reverse()) selection.Toggle(i);
            Assert.Equal(expected, selection.Snapshot());
        }
        [Fact]
        public void SelectAllAndClearKeepTheViewingPosition()
        {
            var selection = new FrameSelection(); selection.Reset(40); selection.View(12);
            selection.SelectAll(); Assert.Equal(40, selection.Count); Assert.Equal(12, selection.Current);
            selection.Clear(); Assert.Empty(selection.Selected); Assert.Equal(12, selection.Current);
            selection.Reset(0); selection.SelectAll(); Assert.Empty(selection.Selected);
        }
        [Fact]
        public void TimelineUsesRealFrameTimesAndClampsEdges()
        {
            var media = new ReviewMedia { Times = new[] { 0.0, 0.04, 0.12, 0.5 } };
            Assert.Equal(0, media.FrameAt(-1));
            Assert.Equal(1, media.FrameAt(0.04));
            Assert.Equal(1, media.FrameAt(0.1));
            Assert.Equal(2, media.FrameAt(0.49));
            Assert.Equal(3, media.FrameAt(100));
        }

        [Fact]
        public void SelectionIsOrderedUniqueAndRejectsInvalidFrames()
        {
            Assert.Equal(new[] { 0, 3, 9 }, FrameReviewService.NormalizeSelection(new[] { 9, 3, 0, 3 }, 10));
            Assert.Throws<ArgumentException>(() => FrameReviewService.NormalizeSelection(new int[0], 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => FrameReviewService.NormalizeSelection(new[] { -1 }, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => FrameReviewService.NormalizeSelection(new[] { 10 }, 10));
        }

        [Theory]
        [InlineData("mp4")]
        [InlineData("vfr")]
        [InlineData("gif")]
        public async Task ExactFramePreviewMatchesBatchPngAndCompositions(string kind)
        {
            var executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(p => Path.Combine(p.Trim('"'), "ffmpeg.exe")).FirstOrDefault(File.Exists);
            Assert.NotNull(executable);
            var root = Path.Combine(Path.GetTempPath(), "Captura-frame-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var format = kind == "vfr" ? "gif" : kind;
                var file = Path.Combine(root, "测试 视频." + format);
                await FrameReviewService.RunBytesAsync(executable, "-v error -nostdin -f lavfi -i testsrc2=size=96x64:rate=10:duration=1 " + (kind == "vfr" ? "-vf \"select='eq(n,0)+eq(n,1)+eq(n,5)+eq(n,9)'\" -fps_mode vfr " : "") + FrameReviewService.Quote(file), CancellationToken.None);
                var service = new FrameReviewService(executable);
                var media = await service.OpenAsync(file, CancellationToken.None);
                var thumbnail = await service.ReadThumbnailAsync(media, 0, CancellationToken.None);
                using (var thumbnailStream = new MemoryStream(thumbnail))
                using (var thumbnailImage = new System.Drawing.Bitmap(thumbnailStream)) {
                    Assert.InRange(thumbnailImage.Width, 1, 144);
                    Assert.InRange(thumbnailImage.Height, 1, 90);
                }
                Assert.Equal(kind == "vfr" ? 4 : 10, media.Times.Length);
                var frames = kind == "vfr" ? new[] { 0, 1, 3 } : new[] { 0, 4, 9 };
                var fallbackMedia = await new FrameReviewService(executable, false).OpenAsync(file, CancellationToken.None);
                Assert.Equal(media.Times, fallbackMedia.Times);
                var output = Path.Combine(root, "选帧导出");
                await service.ExportAsync(media, frames.Reverse().Concat(frames), output, format, 5, CancellationToken.None);
                Assert.Equal(3, Directory.GetFiles(output, "frame-*.png").Length);
                for (var i = 0; i < frames.Length; i++)
                {
                    var preview = await service.ReadFrameAsync(media, frames[i], CancellationToken.None);
                    Assert.Equal(File.ReadAllBytes(Path.Combine(output, $"frame-{i + 1:000000}.png")), preview);
                }
                if (format == "gif") {
                    using var gif = new GifPlayback(file);
                    Assert.Equal(media.Times.Length, gif.FrameCount);
                    // 检查向后、向前随机定位与 FFmpeg 导出画面一致，覆盖原生 GIF 解码和资源释放。
                    foreach (var i in new[] { 2, 0, 1 }) {
                        using var actualStream = new MemoryStream(gif.ReadFrame(frames[i]));
                        using var actual = new System.Drawing.Bitmap(actualStream);
                        using var expected = new System.Drawing.Bitmap(Path.Combine(output, $"frame-{i + 1:000000}.png"));
                        Assert.Equal(expected.Size, actual.Size);
                        for (var y = 0; y < actual.Height; y++)
                            for (var x = 0; x < actual.Width; x++) Assert.Equal(expected.GetPixel(x, y).ToArgb(), actual.GetPixel(x, y).ToArgb());
                    }
                }
                Assert.Contains(kind == "vfr" ? "frame-000002.png,2,0.1" : "frame-000002.png,5,0.4", File.ReadAllText(Path.Combine(output, "frames.csv")));
                var result = await service.OpenAsync(Path.Combine(output, "selected." + format), CancellationToken.None);
                Assert.Equal(3, result.Times.Length);
                Assert.InRange(result.Duration, 0.59, 0.61);
                await Assert.ThrowsAsync<IOException>(() => service.ExportAsync(media, frames, output, "png", 10, CancellationToken.None));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public async Task LargeSelectionCrossesBatchBoundaryWithoutLosingFrames()
        {
            var executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(p => Path.Combine(p.Trim('"'), "ffmpeg.exe")).FirstOrDefault(File.Exists);
            Assert.NotNull(executable);
            var root = Path.Combine(Path.GetTempPath(), "Captura-frame-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                var file = Path.Combine(root, "many.mp4");
                await FrameReviewService.RunBytesAsync(executable, "-v error -nostdin -f lavfi -i testsrc2=size=32x24:rate=30:duration=10 " + FrameReviewService.Quote(file), CancellationToken.None);
                var service = new FrameReviewService(executable);
                var media = await service.OpenAsync(file, CancellationToken.None);
                var selection = Enumerable.Range(0, 130).Select(n => n * 2).Reverse();
                var output = Path.Combine(root, "batch");
                await service.ExportAsync(media, selection, output, "png", 30, CancellationToken.None);
                Assert.Equal(130, Directory.GetFiles(output, "frame-*.png").Length);
                Assert.Equal(131, File.ReadAllLines(Path.Combine(output, "frames.csv")).Length);
                var last = await service.ReadFrameAsync(media, 258, CancellationToken.None);
                Assert.Equal(last, File.ReadAllBytes(Path.Combine(output, "frame-000130.png")));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public async Task RunningDecoderCanBeCancelled()
        {
            var executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(p => Path.Combine(p.Trim('"'), "ffmpeg.exe")).FirstOrDefault(File.Exists);
            Assert.NotNull(executable);
            using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FrameReviewService.RunBytesAsync(executable, "-v error -nostdin -re -f lavfi -i testsrc2=size=32x24:rate=10 -t 30 -f null -", source.Token));
        }

        [Fact]
        public async Task CancellationDoesNotLaunchMediaProcess()
        {
            using var source = new CancellationTokenSource(); source.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FrameReviewService.RunBytesAsync("missing.exe", "", source.Token));
        }
    }
}
