using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Captura.FFmpeg
{
    public sealed class ReviewMedia
    {
        public string FilePath { get; set; }
        public double[] Times { get; set; }
        public double Duration { get; set; }
        public int FrameAt(double seconds)
        {
            var i = Array.BinarySearch(Times, seconds);
            return Math.Max(0, Math.Min(Times.Length - 1, i >= 0 ? i : ~i - 1));
        }
    }

    // 预览和导出共用源帧索引，避免用平均帧率推算可变帧率视频。
    public sealed class FrameReviewService
    {
        readonly string _ffmpeg;
        readonly string _ffprobe;
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public FrameReviewService(string ffmpeg, bool useProbe = true)
        {
            _ffmpeg = ffmpeg;
            var adjacent = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe.exe");
            _ffprobe = File.Exists(adjacent) ? adjacent :
                (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(p => Path.Combine(p.Trim('"'), "ffprobe.exe")).FirstOrDefault(File.Exists);
            if (!useProbe) _ffprobe = null;
        }

        public async Task<ReviewMedia> OpenAsync(string file, CancellationToken token)
        {
            if (!File.Exists(file)) throw new IOException("找不到要预览的文件。");
            // 精简版 FFmpeg 可能不附带 ffprobe，此时顺序解码生成真实帧索引。
            var output = _ffprobe != null
                ? await RunAsync(_ffprobe, "-v error -select_streams v:0 -show_frames -show_entries frame=best_effort_timestamp_time,duration_time,pkt_duration_time -of compact=p=0:nk=0 " + Quote(file), token).ConfigureAwait(false)
                : Encoding.UTF8.GetString(await RunBytesAsync(_ffmpeg, "-hide_banner -nostdin -i " + Quote(file) + " -map 0:v:0 -an -vf showinfo -f null -", token, true).ConfigureAwait(false));
            var times = new List<double>();
            double lastDuration = 0.04;
            foreach (var line in output.Split('\n'))
            {
                token.ThrowIfCancellationRequested();
                if (_ffprobe == null)
                {
                    var match = Regex.Match(line, @"\bn:\s*\d+\s+pts:.*?pts_time:([-+\d.eE]+)");
                    if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, Invariant, out var timestamp)) times.Add(timestamp);
                    continue;
                }
                double? time = null;
                foreach (var part in line.Trim().Split('|'))
                {
                    var pair = part.Split('=');
                    if (pair.Length != 2 || !double.TryParse(pair[1], NumberStyles.Float, Invariant, out var value)) continue;
                    if (pair[0] == "best_effort_timestamp_time") time = value;
                    else if ((pair[0] == "duration_time" || pair[0] == "pkt_duration_time") && value > 0) lastDuration = value;
                }
                if (time.HasValue) times.Add(time.Value);
            }
            if (times.Count == 0) throw new IOException("文件没有可读取的视频帧。");
            var start = times[0];
            var normalized = times.Select(t => Math.Max(0, t - start)).ToArray();
            if (_ffprobe == null && normalized.Length > 1) lastDuration = normalized.Last() - normalized[normalized.Length - 2];
            return new ReviewMedia { FilePath = Path.GetFullPath(file), Times = normalized, Duration = normalized.Last() + lastDuration };
        }

        public async Task<byte[]> ReadFrameAsync(ReviewMedia media, int index, CancellationToken token)
        {
            if (index < 0 || index >= media.Times.Length) throw new ArgumentOutOfRangeException(nameof(index));
            // 输入侧精确定位由解码器从关键帧向前解码；轻微提前可避免时间戳小数舍入跨到下一帧。
            var seek = Math.Max(0, media.Times[index] - 0.000001).ToString("0.######", Invariant);
            return await RunBytesAsync(_ffmpeg, "-v error -nostdin -ss " + seek + " -i " + Quote(media.FilePath) + " -map 0:v:0 -frames:v 1 -f image2pipe -c:v png -", token);
        }

        public Task<byte[]> ReadThumbnailAsync(ReviewMedia media, int index, CancellationToken token)
        {
            if (index < 0 || index >= media.Times.Length) throw new ArgumentOutOfRangeException(nameof(index));
            var seek = Math.Max(0, media.Times[index] - 0.000001).ToString("0.######", Invariant);
            return RunBytesAsync(_ffmpeg, "-v error -nostdin -ss " + seek + " -i " + Quote(media.FilePath) + " -map 0:v:0 -frames:v 1 -vf \"scale=144:90:force_original_aspect_ratio=decrease\" -f image2pipe -c:v mjpeg -", token);
        }

        public static int[] NormalizeSelection(IEnumerable<int> frames, int count)
        {
            var result = frames.Distinct().OrderBy(n => n).ToArray();
            if (result.Length == 0) throw new ArgumentException("请先选择需要导出的帧。");
            if (result[0] < 0 || result.Last() >= count) throw new ArgumentOutOfRangeException(nameof(frames));
            return result;
        }

        public async Task ExportAsync(ReviewMedia media, IEnumerable<int> selection, string directory, string kind, double fps, CancellationToken token, IProgress<string> progress = null)
        {
            var frames = NormalizeSelection(selection, media.Times.Length);
            if (kind != "png" && kind != "mp4" && kind != "gif") throw new ArgumentException("不支持的导出格式。");
            if (fps < 1 || fps > 60 || double.IsNaN(fps)) throw new ArgumentOutOfRangeException(nameof(fps));
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("导出目录已存在，请选择新目录。");
            Directory.CreateDirectory(directory);
            var help = await RunAsync(_ffmpeg, "-hide_banner -h full", token);
            var syncOption = help.Contains("-fps_mode") ? " -fps_mode passthrough" : " -vsync 0";
            // 一次顺序解码批量抽帧，按批限制命令长度，避免数十帧时重复解码整个视频。
            for (var offset = 0; offset < frames.Length; offset += 64)
            {
                var batch = frames.Skip(offset).Take(64).ToArray();
                var terms = new List<string>();
                for (var i = 0; i < batch.Length; i++) {
                    var first = batch[i];
                    while (i + 1 < batch.Length && batch[i + 1] == batch[i] + 1) i++;
                    terms.Add(first == batch[i] ? "eq(n\\," + first + ")" : "between(n\\," + first + "\\," + batch[i] + ")");
                }
                var expression = string.Join("+", terms);
                progress?.Report($"正在导出图片：{offset + 1}—{offset + batch.Length} / {frames.Length}");
                await RunAsync(_ffmpeg, "-v error -nostdin -n -i " + Quote(media.FilePath) + " -map 0:v:0 -an -vf \"select=" + expression + "\"" + syncOption + " -frames:v " + batch.Length + " -start_number " + (offset + 1) + " " + Quote(Path.Combine(directory, "frame-%06d.png")), token);
            }
            var manifest = new StringBuilder("图片,原始帧号,时间秒\r\n");
            for (var i = 0; i < frames.Length; i++)
                manifest.AppendFormat(Invariant, "frame-{0:000000}.png,{1},{2:0.######}\r\n", i + 1, frames[i] + 1, media.Times[frames[i]]);
            File.WriteAllText(Path.Combine(directory, "frames.csv"), manifest.ToString(), new UTF8Encoding(true));
            if (Directory.GetFiles(directory, "frame-*.png").Length != frames.Length) throw new IOException("抽帧数量与选择数量不一致，已保留输出供检查。");
            if (kind == "png") return;
            progress?.Report("图片已导出，正在合成" + (kind == "gif" ? " GIF……" : "视频……"));
            var input = "-v error -nostdin -n -framerate " + fps.ToString(Invariant) + " -i " + Quote(Path.Combine(directory, "frame-%06d.png"));
            var options = kind == "mp4"
                ? " -an -vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" -c:v libx264 -pix_fmt yuv420p -movflags +faststart "
                : " -filter_complex \"split[a][b];[a]palettegen[p];[b][p]paletteuse\" -loop 0 ";
            await RunAsync(_ffmpeg, input + options + Quote(Path.Combine(directory, "selected." + kind)), token);
        }

        public static string Quote(string value)
        {
            if (value.IndexOf('"') >= 0 || value.IndexOf('\0') >= 0) throw new ArgumentException("文件路径无效。");
            return "\"" + value + "\"";
        }

        static async Task<string> RunAsync(string executable, string args, CancellationToken token) =>
            Encoding.UTF8.GetString(await RunBytesAsync(executable, args, token));

        public static async Task<byte[]> RunBytesAsync(string executable, string args, CancellationToken token, bool captureError = false)
        {
            token.ThrowIfCancellationRequested();
            using var process = new Process { StartInfo = new ProcessStartInfo(executable, args) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true } };
            process.Start();
            using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } });
            var errorTask = process.StandardError.ReadToEndAsync();
            using var output = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(output);
            var error = await errorTask;
            await Task.Run(() => process.WaitForExit());
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException("媒体处理失败，请检查文件或 FFmpeg。\n" + error.Substring(0, Math.Min(error.Length, 1500)));
            return captureError ? Encoding.UTF8.GetBytes(error) : output.ToArray();
        }
    }
}
