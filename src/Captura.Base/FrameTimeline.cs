using System;
using System.Collections.Generic;
using System.Linq;

namespace Captura
{
    /// <summary>工作区帧编辑：显示索引与原始帧号分离，源文件不变。</summary>
    public sealed class FrameTimeline
    {
        readonly double[] _sourceDurations;
        readonly List<int[]> _undo = new List<int[]>(), _redo = new List<int[]>();
        int[] _frames;
        public double[] Times { get; private set; }
        public double Duration { get; private set; }
        public int Count => _frames.Length;
        public bool IsEdited => Count != _sourceDurations.Length;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public FrameTimeline(double[] sourceTimes, double duration)
        {
            if (sourceTimes == null || sourceTimes.Length == 0 || duration <= sourceTimes.Last()) throw new ArgumentException("媒体时间轴无效。");
            _sourceDurations = new double[sourceTimes.Length];
            for (var i = 0; i < sourceTimes.Length; i++) {
                var next = i + 1 < sourceTimes.Length ? sourceTimes[i + 1] : duration;
                if (next < sourceTimes[i]) throw new ArgumentException("源帧时间必须递增。");
                _sourceDurations[i] = Math.Max(0.000001, next - sourceTimes[i]);
            }
            _frames = Enumerable.Range(0, sourceTimes.Length).ToArray(); Rebuild();
        }
        public int SourceAt(int index) => _frames[index];
        public int[] Snapshot() => (int[])_frames.Clone();
        public int LocateSource(int source)
        {
            var i = Array.BinarySearch(_frames, source);
            return Math.Min(Count - 1, i >= 0 ? i : ~i);
        }
        public int FrameAt(double time)
        {
            var i = Array.BinarySearch(Times, time);
            return Math.Max(0, Math.Min(Count - 1, i >= 0 ? i : ~i - 1));
        }
        public bool Remove(IEnumerable<int> positions)
        {
            var remove = new HashSet<int>(positions);
            if (remove.Any(i => i < 0 || i >= Count)) throw new ArgumentOutOfRangeException(nameof(positions));
            if (remove.Count == 0) return false;
            if (remove.Count == Count) throw new InvalidOperationException("至少需要保留一帧。");
            var next = _frames.Where((_, i) => !remove.Contains(i)).ToArray();
            Remember(_undo, _frames); _redo.Clear(); _frames = next; Rebuild(); return true;
        }
        public bool Reduce(int interval)
        {
            if (interval < 2 || interval > 1000) throw new ArgumentOutOfRangeException(nameof(interval));
            return Remove(Enumerable.Range(0, Count).Where(i => i % interval != 0));
        }
        public bool Undo()
        {
            if (!CanUndo) return false;
            Remember(_redo, _frames); _frames = Take(_undo); Rebuild(); return true;
        }
        public bool Redo()
        {
            if (!CanRedo) return false;
            Remember(_undo, _frames); _frames = Take(_redo); Rebuild(); return true;
        }
        static int[] Take(List<int[]> history) { var result = history.Last(); history.RemoveAt(history.Count - 1); return result; }
        static void Remember(List<int[]> history, int[] frames)
        {
            history.Add(frames);
            while (history.Count > 1 && (history.Count > 20 || history.Sum(s => (long)s.Length * 4) > 16 * 1024 * 1024)) history.RemoveAt(0);
        }
        void Rebuild()
        {
            Times = new double[Count]; var time = 0.0;
            for (var i = 0; i < Count; i++) { Times[i] = time; time += _sourceDurations[_frames[i]]; }
            Duration = time;
        }
    }

    public static class FrameSimilarity
    {
        // 原尺寸 BGRA。RGB 平均绝对差与 Alpha 差取较大值；100% 不允许任何字节不同。
        public static double Percent(byte[] first, byte[] second)
        {
            if (first == null || second == null || first.Length == 0 || first.Length != second.Length || first.Length % 4 != 0) throw new ArgumentException("BGRA 帧尺寸不一致。");
            double difference = 0;
            for (var i = 0; i < first.Length; i += 4) {
                var color = (Math.Abs(first[i] - second[i]) + Math.Abs(first[i + 1] - second[i + 1]) + Math.Abs(first[i + 2] - second[i + 2])) / 3.0;
                difference += Math.Max(color, Math.Abs(first[i + 3] - second[i + 3]));
            }
            return difference == 0 ? 100 : Math.Min(99.999999999, 100.0 * (1 - difference / (255.0 * (first.Length / 4))));
        }
    }
}
