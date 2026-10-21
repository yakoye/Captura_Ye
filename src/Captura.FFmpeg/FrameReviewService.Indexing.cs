using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Captura.FFmpeg
{
    public sealed partial class FrameReviewService
    {
        public async Task<ReviewMedia> OpenInitialAsync(string file, CancellationToken token)
        {
            if (!File.Exists(file)) throw new IOException("媒体文件不存在。");
            // GIF 的逐帧合成及延时由原有精确路径处理，长视频先读取少量流信息。
            if (Path.GetExtension(file).Equals(".gif", StringComparison.OrdinalIgnoreCase)) return await OpenAsync(file, token);
            double duration = 0, rate = 0; var expected = 0; string codec = null;
            if (_ffprobe != null) {
                var info = await RunAsync(_ffprobe, "-v error -select_streams v:0 -show_entries stream=codec_name,avg_frame_rate,r_frame_rate,nb_frames,duration:format=duration -of compact=p=0:nk=0 " + Quote(file), token);
                foreach (var field in info.Split(new[] { '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                    var pair = field.Split(new[] { '=' }, 2); if (pair.Length != 2) continue;
                    if (pair[0] == "duration" && duration <= 0 && double.TryParse(pair[1], NumberStyles.Float, Invariant, out var d) && d > 0 && !double.IsInfinity(d)) duration = d;
                    if (pair[0] == "avg_frame_rate" || pair[0] == "r_frame_rate") {
                        var fraction = pair[1].Split('/');
                        if (fraction.Length == 2 && double.TryParse(fraction[0], NumberStyles.Float, Invariant, out var a) && double.TryParse(fraction[1], NumberStyles.Float, Invariant, out var b) && b > 0 && a > 0) {
                            if (pair[0] == "avg_frame_rate" || rate <= 0) rate = a / b;
                        }
                    }
                    if (pair[0] == "nb_frames") int.TryParse(pair[1], out expected);
                    if (pair[0] == "codec_name") codec = pair[1];
                }
            } else {
                var info = System.Text.Encoding.UTF8.GetString(await RunBytesAsync(_ffmpeg, "-hide_banner -nostdin -i " + Quote(file) + " -map 0:v:0 -frames:v 1 -f null -", token, true));
                var time = Regex.Match(info, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
                if (time.Success) duration = double.Parse(time.Groups[1].Value, Invariant) * 3600 + double.Parse(time.Groups[2].Value, Invariant) * 60 + double.Parse(time.Groups[3].Value, Invariant);
                var fps = Regex.Match(info, @"(\d+(?:\.\d+)?)\s+fps"); if (fps.Success) rate = double.Parse(fps.Groups[1].Value, Invariant);
            }
            if (duration <= 0 || double.IsNaN(duration) || double.IsInfinity(duration)) return await OpenAsync(file, token);
            if (rate <= 0 || double.IsNaN(rate) || double.IsInfinity(rate)) rate = 25;
            var count = (int)Math.Max(1, Math.Min(1000000, expected > 0 ? expected : Math.Ceiling(duration * rate)));
            return new ReviewMedia { FilePath = Path.GetFullPath(file), Duration = duration, Times = Enumerable.Range(0, count).Select(i => duration * i / count).ToArray(), IsApproximate = true, ExpectedFrames = expected, Codec = codec };
        }
        public async Task<ReviewMedia> CompleteIndexAsync(ReviewMedia initial, CancellationToken token, IProgress<int> progress = null)
        {
            if (!initial.IsApproximate) return initial;
            var packetCodecs = new[] { "h264", "hevc", "vp8", "vp9", "mpeg4", "mjpeg", "rawvideo", "ffv1", "huffyuv", "png", "qtrle", "prores" };
            if (_ffprobe == null || !packetCodecs.Contains(initial.Codec)) return await OpenAsync(initial.FilePath, token);
            try {
                return await Task.Run(async () => {
                    using var process = new Process { StartInfo = new ProcessStartInfo(_ffprobe, "-v error -select_streams v:0 -show_packets -show_entries packet=pts_time,duration_time -of compact=p=0:nk=0 " + Quote(initial.FilePath)) {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
                    process.Start();
                    using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } });
                    var error = process.StandardError.ReadToEndAsync();
                    var points = new List<Tuple<double, double>>(); var clock = Stopwatch.StartNew();
                    string line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) != null) {
                        token.ThrowIfCancellationRequested(); double? time = null; double length = 0;
                        foreach (var field in line.Split('|')) {
                            var pair = field.Split(new[] { '=' }, 2);
                            if (pair.Length != 2 || !double.TryParse(pair[1], NumberStyles.Float, Invariant, out var value) || double.IsNaN(value) || double.IsInfinity(value)) continue;
                            if (pair[0] == "pts_time") time = value;
                            if (pair[0] == "duration_time") length = value;
                        }
                        if (time.HasValue) points.Add(Tuple.Create(time.Value, length));
                        if (points.Count == 1 || clock.ElapsedMilliseconds >= 150) { progress?.Report(points.Count); clock.Restart(); }
                    }
                    var details = await error; await Task.Run(() => process.WaitForExit()); token.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0 || points.Count == 0 || (initial.ExpectedFrames > 0 && initial.ExpectedFrames != points.Count)) throw new IOException("数据包索引需要进一步校验。" + details);
                    var sorted = points.OrderBy(p => p.Item1).ToArray(); var start = sorted[0].Item1;
                    var times = sorted.Select(p => p.Item1 - start).ToArray();
                    var last = sorted.Last().Item2;
                    if (last <= 0) last = times.Length > 1 ? times.Last() - times[times.Length - 2] : initial.Duration;
                    if (last <= 0) last = 0.1;
                    progress?.Report(times.Length);
                    return new ReviewMedia { FilePath = initial.FilePath, Times = times, Duration = times.Last() + last };
                }, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException) { return await OpenAsync(initial.FilePath, token); }
        }
    }
}
