using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Captura.FFmpeg
{
    internal sealed class RawFrameReader : IDisposable
    {
        readonly Process _process;
        readonly Task<string> _error;
        readonly CancellationToken _token;
        readonly CancellationTokenRegistration _registration;
        public RawFrameReader(string executable, string arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); _token = token;
            _process = new Process { StartInfo = new ProcessStartInfo(executable, arguments) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, StandardErrorEncoding = Encoding.UTF8 } };
            _process.Start(); _error = _process.StandardError.ReadToEndAsync();
            _registration = token.Register(Kill);
        }
        void Kill() { try { if (!_process.HasExited) _process.Kill(); } catch (InvalidOperationException) { } }
        public async Task<bool> ReadAsync(byte[] frame)
        {
            var offset = 0;
            while (offset < frame.Length) {
                _token.ThrowIfCancellationRequested();
                var read = await _process.StandardOutput.BaseStream.ReadAsync(frame, offset, frame.Length - offset, _token);
                _token.ThrowIfCancellationRequested();
                if (read == 0) {
                    var error = await _error; await Task.Run(() => _process.WaitForExit()); _token.ThrowIfCancellationRequested();
                    if (_process.ExitCode != 0 || offset != 0) throw new IOException("帧解码失败：" + error.Substring(0, Math.Min(1000, error.Length)));
                    return false;
                }
                offset += read;
            }
            return true;
        }
        public void Dispose()
        {
            _registration.Dispose(); Kill();
            try { _process.WaitForExit(2000); } catch (InvalidOperationException) { }
            _ = _error.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            _process.Dispose();
        }
    }

    public sealed class DecodedReviewFrame
    {
        public int Index { get; }
        public byte[] Pixels { get; }
        internal DecodedReviewFrame(int index, byte[] pixels) { Index = index; Pixels = pixels; }
    }

    /// <summary>编辑后的视频只解码保留帧，有界预取；不创建临时视频或修改源文件。</summary>
    public sealed class FramePlaybackSession : IDisposable
    {
        readonly BlockingCollection<DecodedReviewFrame> _frames;
        readonly CancellationTokenSource _stop;
        readonly Task _pump;
        int _disposed;
        public int Width { get; }
        public int Height { get; }
        public Exception Error { get; private set; }
        public Task Completion => _pump;
        public bool IsCompleted => _frames.IsCompleted;
        public bool TryRead(out DecodedReviewFrame frame) => _frames.TryTake(out frame);
        internal FramePlaybackSession(string executable, string arguments, int width, int height, int[] sourceFrames, int start, CancellationToken token)
        {
            Width = width; Height = height;
            var bytes = checked(width * height * 4);
            _frames = new BlockingCollection<DecodedReviewFrame>(Math.Max(1, Math.Min(8, 32 * 1024 * 1024 / bytes)));
            _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            _pump = Task.Run(async () => {
                try {
                    using var reader = new RawFrameReader(executable, arguments, _stop.Token);
                    var buffer = new byte[bytes]; var position = start; var source = sourceFrames[start];
                    while (position < sourceFrames.Length && await reader.ReadAsync(buffer)) {
                        if (source == sourceFrames[position]) {
                            _frames.Add(new DecodedReviewFrame(position++, buffer), _stop.Token);
                            buffer = new byte[bytes];
                        }
                        source++;
                    }
                    _stop.Token.ThrowIfCancellationRequested();
                    if (position < sourceFrames.Length) throw new IOException("源文件解码帧数与时间轴不一致。");
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { if (!_stop.IsCancellationRequested) Error = e; }
                finally { _frames.CompleteAdding(); }
            });
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel();
            _ = _pump.ContinueWith(_ => { _frames.Dispose(); _stop.Dispose(); }, TaskScheduler.Default);
        }
    }
}
