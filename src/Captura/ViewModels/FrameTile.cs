using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Captura
{
    public sealed class FrameTile : INotifyPropertyChanged
    {
        public int Index { get; }
        public int Number => Index + 1;
        readonly double _seconds;
        public string Time => TimeSpan.FromSeconds(_seconds).ToString(@"hh\:mm\:ss\.fff");
        public string Label => $"第 {Number} 帧 · {Time}";
        bool _selected, _current;
        BitmapSource _thumbnail;
        string _thumbnailHint = "缩略图载入中";
        public string ThumbnailHint { get => _thumbnailHint; set { _thumbnailHint = value; Changed(); } }
        public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; Changed(); } }
        public bool IsCurrent { get => _current; set { if (_current == value) return; _current = value; Changed(); } }
        public BitmapSource Thumbnail { get => _thumbnail; set { _thumbnail = value; Changed(); } }
        public FrameTile(int index, double seconds) { Index = index; _seconds = seconds; }
        public event PropertyChangedEventHandler PropertyChanged;
        void Changed([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
