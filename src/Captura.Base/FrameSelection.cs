using System;
using System.Collections.Generic;
using System.Linq;

namespace Captura
{
    /// <summary>当前查看位置、范围锚点和导出选择彼此独立。</summary>
    public sealed class FrameSelection
    {
        readonly SortedSet<int> _selected = new SortedSet<int>();
        public IReadOnlyCollection<int> Selected => _selected;
        public int Count => _selected.Count;
        public int FrameCount { get; private set; }
        public int Current { get; private set; }
        public int Anchor { get; private set; }

        public void Reset(int frameCount)
        {
            if (frameCount < 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
            FrameCount = frameCount; Current = Anchor = 0; _selected.Clear();
        }
        void Validate(int frame)
        {
            if (frame < 0 || frame >= FrameCount) throw new ArgumentOutOfRangeException(nameof(frame));
        }
        public bool Contains(int frame) => _selected.Contains(frame);
        public void View(int frame) { Validate(frame); Current = frame; }
        public void Toggle(int frame)
        {
            Validate(frame);
            if (!_selected.Remove(frame)) _selected.Add(frame);
            Current = Anchor = frame;
        }
        public void Click(int frame, bool control, bool shift)
        {
            Validate(frame);
            if (shift) SelectRange(Anchor, frame, control);
            else if (control) Toggle(frame);
            else { _selected.Clear(); _selected.Add(frame); Current = Anchor = frame; }
        }
        public void SetAnchor(int frame) { Validate(frame); Anchor = frame; }
        public void SelectRange(int from, int to, bool add)
        {
            Validate(from); Validate(to);
            if (!add) _selected.Clear();
            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++) _selected.Add(i);
            Current = to;
        }
        public void SelectAll() { for (var i = 0; i < FrameCount; i++) _selected.Add(i); }
        public void Clear() => _selected.Clear();
        public int[] Snapshot() => _selected.ToArray();
    }
}
