using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Captura.FFmpeg;
using Xunit;

namespace Captura.Tests
{
    public class FrameEditingTests
    {
        readonly Xunit.Abstractions.ITestOutputHelper _output;
        public FrameEditingTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;
        [Fact]
        public async Task FFmpegThirtyMinuteImportProvidesPreviewWithoutFullDecode()
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-long-index-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var clip = Path.Combine(directory, "clip.mp4"); var file = Path.Combine(directory, "thirty-minutes.mp4");
                await FrameReviewService.RunBytesAsync(FFmpegPath, "-v error -f lavfi -i testsrc2=s=384x216:r=24:d=4 -c:v libx264 -b:v 256k -minrate 256k -maxrate 256k -bufsize 512k -x264-params nal-hrd=cbr -pix_fmt yuv420p " + FrameReviewService.Quote(clip), CancellationToken.None);
                await FrameReviewService.RunBytesAsync(FFmpegPath, "-v error -stream_loop 449 -i " + FrameReviewService.Quote(clip) + " -c copy " + FrameReviewService.Quote(file), CancellationToken.None);
                Assert.True(new FileInfo(file).Length > 50 * 1024 * 1024);
                var service = new FrameReviewService(FFmpegPath); var clock = System.Diagnostics.Stopwatch.StartNew();
                var initial = await service.OpenInitialAsync(file, CancellationToken.None); var metadataMs = clock.ElapsedMilliseconds;
                Assert.True(initial.IsApproximate); Assert.Equal(1800, initial.Duration, 2);
                Assert.NotEmpty(await service.ReadFrameAsync(initial, 0, CancellationToken.None)); var previewMs = clock.ElapsedMilliseconds;
                var exact = await service.CompleteIndexAsync(initial, CancellationToken.None); var completeMs = clock.ElapsedMilliseconds;
                Assert.False(exact.IsApproximate); Assert.Equal(43200, exact.Times.Length); Assert.Equal(1800, exact.Duration, 2);
                Assert.Equal(1200, exact.Times[28800], 5);
                _output.WriteLine($"bytes={new FileInfo(file).Length}; duration=1800s; frames=43200; metadataMs={metadataMs}; firstPreviewMs={previewMs}; exactIndexTotalMs={completeMs}");
            } finally { Directory.Delete(directory, true); }
        }
        [Fact]
        public void DeletingSparsePositionsPreservesSourceMappingAndRealDurations()
        {
            var timeline = new FrameTimeline(new[] { 0.0, 0.1, 0.4, 0.9 }, 1.0);
            timeline.Remove(new[] { 1, 3 });
            Assert.Equal(new[] { 0, 2 }, timeline.Snapshot());
            Assert.Equal(new[] { 0.0, 0.1 }, timeline.Times);
            Assert.Equal(0.6, timeline.Duration, 8);
            Assert.Equal(1, timeline.FrameAt(0.5));
            Assert.Equal(1, timeline.LocateSource(1));
            Assert.Equal(0, timeline.FrameAt(-10));
            Assert.Equal(1, timeline.FrameAt(100));
        }
        [Fact]
        public void UndoRedoRestoresExactMappingAndBranchingDropsRedo()
        {
            var timeline = new FrameTimeline(new[] { 0.0, 0.2, 0.5, 0.7 }, 0.9);
            timeline.Remove(new[] { 1 }); timeline.Reduce(2);
            Assert.Equal(new[] { 0, 3 }, timeline.Snapshot());
            timeline.Undo(); Assert.Equal(new[] { 0, 2, 3 }, timeline.Snapshot());
            timeline.Undo(); Assert.False(timeline.IsEdited); Assert.Equal(0.9, timeline.Duration, 8);
            timeline.Redo(); Assert.Equal(new[] { 0, 2, 3 }, timeline.Snapshot());
            timeline.Remove(new[] { 2 }); Assert.False(timeline.CanRedo);
            Assert.Equal(new[] { 0, 2 }, timeline.Snapshot());
        }
        [Fact]
        public void InvalidOrCompleteRemovalIsAtomicAndLeavesUndoHistoryIntact()
        {
            var timeline = new FrameTimeline(new[] { 0.0, 1.0 }, 2);
            Assert.Throws<InvalidOperationException>(() => timeline.Remove(new[] { 0, 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => timeline.Remove(new[] { 0, 7 }));
            Assert.Equal(new[] { 0, 1 }, timeline.Snapshot()); Assert.False(timeline.CanUndo);
            Assert.False(timeline.Remove(new int[0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => timeline.Reduce(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => timeline.Reduce(1001));
        }
        [Fact]
        public void ReducingKeepsFirstOfEachGroupAndAtLeastOneFrame()
        {
            var timeline = new FrameTimeline(Enumerable.Range(0, 9).Select(i => i * 0.1).ToArray(), 0.9);
            timeline.Reduce(4); Assert.Equal(new[] { 0, 4, 8 }, timeline.Snapshot());
            timeline.Reduce(1000); Assert.Equal(new[] { 0 }, timeline.Snapshot());
            Assert.False(timeline.Reduce(2)); Assert.True(timeline.Undo());
            Assert.Equal(new[] { 0, 4, 8 }, timeline.Snapshot());
        }
        [Fact]
        public void HistoryAndSourceSnapshotsAreBoundedAndIndependent()
        {
            var timeline = new FrameTimeline(Enumerable.Range(0, 40).Select(i => (double)i).ToArray(), 40);
            var snapshot = timeline.Snapshot(); snapshot[0] = 999;
            Assert.Equal(0, timeline.SourceAt(0));
            for (var i = 0; i < 30; i++) timeline.Remove(new[] { timeline.Count - 1 });
            var count = 0; while (timeline.Undo()) count++;
            Assert.Equal(20, count);
        }
        [Fact]
        public void SimilarityRequiresEveryByteAtOneHundredAndMeasuresSmallChanges()
        {
            Assert.Equal(100, FrameSimilarity.Percent(new byte[] { 0, 100, 255, 255 }, new byte[] { 0, 100, 255, 255 }));
            var near = FrameSimilarity.Percent(new byte[] { 0, 100, 255, 255 }, new byte[] { 0, 101, 255, 255 });
            Assert.True(near < 100 && near > 99);
            Assert.Equal(0, FrameSimilarity.Percent(new byte[] { 0, 0, 0, 255 }, new byte[] { 255, 255, 255, 255 }));
            Assert.Equal(0, FrameSimilarity.Percent(new byte[] { 0, 0, 0, 0 }, new byte[] { 0, 0, 0, 255 }));
            Assert.Throws<ArgumentException>(() => FrameSimilarity.Percent(new byte[0], new byte[0]));
        }
        static string FFmpegPath => FFmpegService.DetectExecutable() ?? throw new InvalidOperationException("测试需要 FFmpeg。");
        [Fact]
        public async Task FFmpegProgressiveTrimmedMp4IgnoresDiscardedPrerollPackets()
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-trim-index-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var source = Path.Combine(directory, "source.mp4"); var file = Path.Combine(directory, "trimmed.mp4");
                await FrameReviewService.RunBytesAsync(FFmpegPath, "-v error -f lavfi -i testsrc2=s=64x48:r=24:d=5 -c:v libx264 -g 120 -bf 3 " + FrameReviewService.Quote(source), CancellationToken.None);
                await FrameReviewService.RunBytesAsync(FFmpegPath, "-v error -ss 2.3 -i " + FrameReviewService.Quote(source) + " -t 1 -c copy " + FrameReviewService.Quote(file), CancellationToken.None);
                var service = new FrameReviewService(FFmpegPath); var initial = await service.OpenInitialAsync(file, CancellationToken.None);
                Assert.True(initial.IsApproximate);
                var exact = await service.CompleteIndexAsync(initial, CancellationToken.None);
                var canonical = await service.OpenAsync(file, CancellationToken.None);
                Assert.True(exact.Times.Length < initial.Times.Length);
                Assert.Equal(canonical.Times, exact.Times); Assert.Equal(canonical.Duration, exact.Duration, 5);
                Assert.Equal(await service.ReadFrameAsync(canonical, 0, CancellationToken.None), await service.ReadFrameAsync(exact, 0, CancellationToken.None));
            } finally { Directory.Delete(directory, true); }
        }
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FFmpegProgressiveOpenReturnsPreviewBeforeExactIndexAndMatchesBFrames(bool useProbe)
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-index-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var file = Path.Combine(directory, "bframes.mp4");
                await FrameReviewService.RunBytesAsync(FFmpegPath, "-v error -f lavfi -i testsrc2=s=64x48:r=24:d=3 -c:v libx264 -bf 3 " + FrameReviewService.Quote(file), CancellationToken.None);
                var service = new FrameReviewService(FFmpegPath, useProbe);
                var initial = await service.OpenInitialAsync(file, CancellationToken.None);
                Assert.True(initial.IsApproximate); Assert.Equal(3, initial.Duration, 2);
                Assert.NotEmpty(await service.ReadFrameAsync(initial, initial.FrameAt(1.5), CancellationToken.None));
                var exact = await service.CompleteIndexAsync(initial, CancellationToken.None);
                var canonical = await service.OpenAsync(file, CancellationToken.None);
                Assert.False(exact.IsApproximate); Assert.True(initial.IsApproximate);
                Assert.Equal(72, exact.Times.Length); Assert.Equal(canonical.Times, exact.Times);
                Assert.Equal(canonical.Duration, exact.Duration, 5);
            } finally { Directory.Delete(directory, true); }
        }
        [Fact]
        public async Task FFmpegProgressiveIndexPreservesVariableTimestamps()
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-vfr-index-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var file = Path.Combine(directory, "variable.mp4");
                await FrameReviewService.RunBytesAsync(FFmpegPath, "-v error -f lavfi -i testsrc2=s=64x48:r=10:d=2 -vf \"select='eq(n,0)+eq(n,2)+eq(n,3)+eq(n,10)+eq(n,19)'\" -fps_mode vfr -c:v libx264 -bf 3 " + FrameReviewService.Quote(file), CancellationToken.None);
                var service = new FrameReviewService(FFmpegPath);
                var initial = await service.OpenInitialAsync(file, CancellationToken.None);
                var exact = await service.CompleteIndexAsync(initial, CancellationToken.None);
                var canonical = await service.OpenAsync(file, CancellationToken.None);
                Assert.Equal(5, exact.Times.Length); Assert.Equal(canonical.Times, exact.Times);
                Assert.Equal(canonical.Duration, exact.Duration, 5);
            } finally { Directory.Delete(directory, true); }
        }
        [Fact]
        public async Task FFmpegProgressiveIndexCanCancelWithoutChangingInitialPreview()
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-index-cancel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var file = await Fixture(directory, true); var service = new FrameReviewService(FFmpegPath);
                var initial = await service.OpenInitialAsync(file, CancellationToken.None); var times = initial.Times.ToArray();
                using var stop = new CancellationTokenSource(); stop.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompleteIndexAsync(initial, stop.Token));
                Assert.True(initial.IsApproximate); Assert.Equal(times, initial.Times);
                Assert.NotEmpty(await service.ReadFrameAsync(initial, 0, CancellationToken.None));
            } finally { Directory.Delete(directory, true); }
        }
        [Fact]
        public async Task FFmpegProgressiveGifUsesExactDurationsImmediately()
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-gif-index-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var file = Path.Combine(directory, "sample.gif");
                await FrameReviewService.RunBytesAsync(FFmpegPath, "-v error -f lavfi -i testsrc2=s=32x24:r=5:d=1 " + FrameReviewService.Quote(file), CancellationToken.None);
                var service = new FrameReviewService(FFmpegPath); var initial = await service.OpenInitialAsync(file, CancellationToken.None);
                Assert.False(initial.IsApproximate); Assert.Equal(5, initial.Times.Length);
                Assert.Same(initial, await service.CompleteIndexAsync(initial, CancellationToken.None));
            } finally { Directory.Delete(directory, true); }
        }
        static async Task<string> Fixture(string directory, bool longClip = false)
        {
            var file = Path.Combine(directory, "source.mkv");
            var input = longClip ? "-f lavfi -i color=c=red:s=32x20:r=60:d=5" : "-f lavfi -i color=c=red:s=32x20:r=4:d=0.5 -f lavfi -i color=c=blue:s=32x20:r=4:d=0.5 -filter_complex \"[0:v][1:v]concat=n=2:v=1:a=0\"";
            await FrameReviewService.RunBytesAsync(FFmpegPath, "-v error -nostdin " + input + " -c:v ffv1 " + FrameReviewService.Quote(file), CancellationToken.None);
            return file;
        }
        [Fact]
        public async Task FFmpegDuplicateScanUsesFullPixelsAndActiveSourceMapping()
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-edit-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var file = await Fixture(directory); var before = File.ReadAllBytes(file);
                var service = new FrameReviewService(FFmpegPath); var media = await service.OpenAsync(file, CancellationToken.None);
                Assert.Equal(4, media.Times.Length);
                Assert.Equal(new[] { 1, 3 }, await service.FindDuplicatesAsync(media, new[] { 0, 1, 2, 3 }, 100, CancellationToken.None));
                Assert.Equal(new[] { 1 }, await service.FindDuplicatesAsync(media, new[] { 0, 1, 2 }, 99, CancellationToken.None));
                Assert.Empty(await service.FindDuplicatesAsync(media, new[] { 0, 2 }, 100, CancellationToken.None));
                var timeline = new FrameTimeline(media.Times, media.Duration); timeline.Remove(new[] { 1, 3 });
                var export = Path.Combine(directory, "export");
                await service.ExportAsync(media, timeline.Snapshot(), export, "png", 4, CancellationToken.None);
                var manifest = File.ReadAllText(Path.Combine(export, "frames.csv"));
                Assert.Contains("frame-000001.png,1,0", manifest); Assert.Contains("frame-000002.png,3,0.5", manifest);
                Assert.Equal(before, File.ReadAllBytes(file));
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FindDuplicatesAsync(media, new[] { 0, 1 }, 100, cancelled.Token));
            } finally { Directory.Delete(directory, true); }
        }
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task FFmpegEditedPlaybackOnlyDecodesRetainedFramesAndMatchesOriginalPng(int start)
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-play-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var file = await Fixture(directory); var service = new FrameReviewService(FFmpegPath); var media = await service.OpenAsync(file, CancellationToken.None);
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var session = await service.StartPlaybackAsync(media, new[] { 0, 2 }, start, stop.Token);
                var frames = new List<DecodedReviewFrame>();
                while (!session.IsCompleted) {
                    while (session.TryRead(out var frame)) frames.Add(frame);
                    await Task.Delay(5, stop.Token);
                }
                Assert.Null(session.Error); Assert.Equal(2 - start, frames.Count);
                for (var i = 0; i < frames.Count; i++) {
                    Assert.Equal(i + start, frames[i].Index);
                    var png = await service.ReadFrameAsync(media, new[] { 0, 2 }[i + start], stop.Token);
                    using var stream = new MemoryStream(png); using var bitmap = new Bitmap(stream);
                    var color = bitmap.GetPixel(0, 0); var pixels = frames[i].Pixels;
                    Assert.Equal(color.B, pixels[0]); Assert.Equal(color.G, pixels[1]); Assert.Equal(color.R, pixels[2]); Assert.Equal(color.A, pixels[3]);
                }
            } finally { Directory.Delete(directory, true); }
        }
        [Fact]
        public async Task FFmpegBackpressuredPlaybackCanBeCancelled()
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-cancel-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var file = await Fixture(directory, true); var service = new FrameReviewService(FFmpegPath); var media = await service.OpenAsync(file, CancellationToken.None);
                using var stop = new CancellationTokenSource();
                using var session = await service.StartPlaybackAsync(media, Enumerable.Range(0, media.Times.Length).ToArray(), 0, stop.Token);
                await Task.Delay(100); stop.Cancel();
                Assert.Same(session.Completion, await Task.WhenAny(session.Completion, Task.Delay(5000)));
                Assert.Null(session.Error);
            } finally { Directory.Delete(directory, true); }
        }
        sealed class InlineProgress : IProgress<int>
        {
            readonly Action<int> _report;
            public InlineProgress(Action<int> report) { _report = report; }
            public void Report(int value) => _report(value);
        }
        [Fact]
        public async Task FFmpegRunningDuplicateScanCancellationDoesNotReturnAnEditPlan()
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-scan-cancel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var file = await Fixture(directory, true); var before = File.ReadAllBytes(file);
                var service = new FrameReviewService(FFmpegPath); var media = await service.OpenAsync(file, CancellationToken.None);
                using var stop = new CancellationTokenSource(); var compared = 0;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FindDuplicatesAsync(media, Enumerable.Range(0, media.Times.Length).ToArray(), 100, stop.Token,
                    new InlineProgress(n => { compared = n; if (n == 2) stop.Cancel(); })));
                Assert.Equal(2, compared); Assert.Equal(before, File.ReadAllBytes(file));
            } finally { Directory.Delete(directory, true); }
        }
        [Fact]
        public async Task FFmpegHundredPercentRetainsSinglePixelDifference()
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-pixel-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var first = new byte[16 * 16 * 4];
                for (var i = 3; i < first.Length; i += 4) first[i] = 255;
                var second = (byte[])first.Clone(); second[0] = 1;
                var raw = Path.Combine(directory, "pixels.raw"); File.WriteAllBytes(raw, first.Concat(second).Concat(second).ToArray());
                var file = Path.Combine(directory, "pixels.mkv");
                await FrameReviewService.RunBytesAsync(FFmpegPath, "-v error -f rawvideo -pixel_format bgra -video_size 16x16 -framerate 2 -i " + FrameReviewService.Quote(raw) + " -c:v ffv1 " + FrameReviewService.Quote(file), CancellationToken.None);
                var service = new FrameReviewService(FFmpegPath); var media = await service.OpenAsync(file, CancellationToken.None);
                Assert.Equal(new[] { 2 }, await service.FindDuplicatesAsync(media, new[] { 0, 1, 2 }, 100, CancellationToken.None));
                Assert.Equal(new[] { 1, 2 }, await service.FindDuplicatesAsync(media, new[] { 0, 1, 2 }, 99.9, CancellationToken.None));
            } finally { Directory.Delete(directory, true); }
        }
        [Theory]
        [InlineData("gif")]
        [InlineData("vfr")]
        public async Task FFmpegGifAndVariableRateDuplicatesUseSourceFrames(string kind)
        {
            var directory = Path.Combine(Path.GetTempPath(), "captura-format-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                var source = await Fixture(directory); var file = Path.Combine(directory, "sample.gif");
                var filter = kind == "vfr" ? " -vf \"select='eq(n,0)+eq(n,1)+eq(n,3)'\" -fps_mode vfr" : "";
                await FrameReviewService.RunBytesAsync(FFmpegPath, "-v error -i " + FrameReviewService.Quote(source) + filter + " " + FrameReviewService.Quote(file), CancellationToken.None);
                var service = new FrameReviewService(FFmpegPath); var media = await service.OpenAsync(file, CancellationToken.None);
                Assert.Equal(kind == "vfr" ? 3 : 4, media.Times.Length);
                var duplicates = await service.FindDuplicatesAsync(media, Enumerable.Range(0, media.Times.Length).ToArray(), 100, CancellationToken.None);
                Assert.Equal(kind == "vfr" ? new[] { 1 } : new[] { 1, 3 }, duplicates);
                var timeline = new FrameTimeline(media.Times, media.Duration); timeline.Remove(duplicates);
                Assert.Equal(new[] { 0, 2 }, timeline.Snapshot());
                Assert.Equal(media.Times[1], timeline.Times[1], 8);
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var session = await service.StartPlaybackAsync(media, timeline.Snapshot(), 1, stop.Token);
                DecodedReviewFrame frame;
                while (!session.TryRead(out frame)) { Assert.Null(session.Error); await Task.Delay(5, stop.Token); }
                Assert.Equal(1, frame.Index);
                var png = await service.ReadFrameAsync(media, 2, stop.Token);
                using var stream = new MemoryStream(png); using var bitmap = new Bitmap(stream);
                var color = bitmap.GetPixel(0, 0);
                Assert.Equal(color.B, frame.Pixels[0]); Assert.Equal(color.R, frame.Pixels[2]);
                await session.Completion;
            } finally { Directory.Delete(directory, true); }
        }
    }
}
