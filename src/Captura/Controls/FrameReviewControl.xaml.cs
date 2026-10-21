using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Captura.FFmpeg;
using Microsoft.Win32;

namespace Captura
{
    public partial class FrameReviewControl : UserControl, IDisposable
    {
        public event Action LiveRequested;
        public event Action ReviewRequested;
        FrameReviewService _service;
        ReviewMedia _media;
        CancellationTokenSource _load, _frame, _export;
        readonly DispatcherTimer _timer;
        readonly Dictionary<int, BitmapSource> _cache = new Dictionary<int, BitmapSource>();
        readonly SortedSet<int> _selected = new SortedSet<int>();
        GifPlayback _gif;
        readonly Stopwatch _gifClock = new Stopwatch();
        double _gifStartTime;
        int _index, _rangeStart, _displayedIndex = -1;
        bool _updating, _playing, _disposed, _playerReady;
        string _output;

        public FrameReviewControl()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _timer.Tick += (_, __) => {
                if (!_playing || _media == null) return;
                if (_gif != null) {
                    var time = _gifStartTime + _gifClock.Elapsed.TotalSeconds;
                    if (time >= _media.Duration) { Pause(); _index = _media.Times.Length - 1; Step(0); return; }
                    var index = _media.FrameAt(time);
                    if (index != _displayedIndex) {
                        try { Still.Source = DecodeBitmap(_gif.ReadFrame(index)); _displayedIndex = index; }
                        catch (Exception e) { Pause(); Status.Text = "GIF 播放失败：" + e.Message; }
                    }
                    _index = index;
                }
                else {
                    if (!_playerReady) return;
                    _index = _media.FrameAt(Player.Position.TotalSeconds);
                }
                UpdatePosition();
            };
        }

        public async Task OpenMediaAsync(string file)
        {
            if (_disposed) return;
            if (_export != null) { Status.Text = "请先等待导出结束，或取消处理后打开其他文件。"; return; }
            if (_selected.Count > 0 && MessageBox.Show("打开其他文件会清空当前选帧，是否继续？", "打开媒体", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _load?.Cancel(); _frame?.Cancel();
            var load = new CancellationTokenSource();
            _load = load;
            Pause(); Player.Close(); Player.Source = null; Still.Source = null; _cache.Clear();
            _gif?.Dispose(); _gif = null;
            _media = null; _selected.Clear(); RefreshSelection();
            _displayedIndex = -1; _index = _rangeStart = 0;
            FrameMode.IsChecked = false;
            RangeStartButton.Content = "设为范围起点";
            FileLabel.Text = Path.GetFileName(file); FileLabel.ToolTip = file;
            EmptyHint.Visibility = Visibility.Visible; EmptyHint.Text = "正在读取帧索引……";
            ReviewRequested?.Invoke(); SetBusy(true);
            try
            {
                var executable = await Task.Run(() => FFmpegService.DetectExecutable(), load.Token);
                if (executable == null) throw new IOException("未找到 FFmpeg，请到设置中的 FFmpeg 页面自动检测或指定目录。");
                var service = new FrameReviewService(executable);
                var media = await service.OpenAsync(file, load.Token);
                load.Token.ThrowIfCancellationRequested();
                _service = service; _media = media;
                var preferred = ServiceProvider.Get<Settings>().FrameExportFolder;
                OutputRoot.Text = string.IsNullOrWhiteSpace(preferred) ? Path.Combine(Path.GetDirectoryName(media.FilePath), "Captura-exports") : preferred;
                _playerReady = false;
                if (Path.GetExtension(file).Equals(".gif", StringComparison.OrdinalIgnoreCase)) {
                    _gif = new GifPlayback(file);
                    if (_gif.FrameCount != media.Times.Length) throw new IOException("GIF 帧索引与解码器不一致，无法播放。");
                }
                else { Player.Source = new Uri(Path.GetFullPath(file)); Player.Pause(); }
                Timeline.Maximum = media.Times.Length - 1;
                EmptyHint.Visibility = Visibility.Collapsed;
                ExportFps.Text = Math.Max(1, Math.Min(60, Math.Round(media.Times.Length / media.Duration, 3))).ToString(CultureInfo.InvariantCulture);
                Status.Text = $"共 {media.Times.Length} 帧。播放模式：左右键按 1 秒移动；开启选帧模式后逐帧移动、空格选中。";
                UpdatePosition(); await ShowFrameAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!load.IsCancellationRequested) { EmptyHint.Text = "无法打开媒体，请查看下方提示。"; Status.Text = e.Message; } }
            finally { if (_load == load) { _load = null; SetBusy(false); } load.Dispose(); }
        }

        void UpdatePosition()
        {
            if (_media == null) return;
            _updating = true; Timeline.Value = _index; _updating = false;
            PositionLabel.Text = (FrameMode.IsChecked == true ? "选帧 · " : "播放 · ") + $"第 {_index + 1} / {_media.Times.Length} 帧 · {TimeSpan.FromSeconds(_media.Times[_index]):hh\\:mm\\:ss\\.fff}";
            SelectButton.Content = _selected.Contains(_index) ? "取消选中（空格）" : "选中帧（空格）";
        }

        async Task ShowFrameAsync()
        {
            if (_media == null || _playing || _disposed) return;
            _frame?.Cancel();
            var request = new CancellationTokenSource(); _frame = request;
            var index = _index; var media = _media;
            try
            {
                // 连按方向键时合并过时请求，且终止对应解码进程。
                await Task.Delay(25, request.Token);
                if (!_cache.TryGetValue(index, out var bitmap))
                {
                    var bytes = await _service.ReadFrameAsync(media, index, request.Token);
                    var image = DecodeBitmap(bytes);
                    bitmap = image;
                    // 按解码后的图像大小限制缓存，避免 4K 逐帧耗尽内存。
                    var capacity = Math.Max(2, Math.Min(32, 64 * 1024 * 1024 / Math.Max(1L, (long)image.PixelWidth * image.PixelHeight * 4)));
                    if (_cache.Count >= capacity) _cache.Remove(_cache.Keys.OrderByDescending(n => Math.Abs(n - index)).First());
                    _cache[index] = bitmap;
                }
                request.Token.ThrowIfCancellationRequested();
                if (_media != media || _index != index) return;
                Still.Source = bitmap; Still.Visibility = Visibility.Visible; _displayedIndex = index;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!request.IsCancellationRequested) Status.Text = "读取帧失败：" + e.Message; }
            finally { if (_frame == request) _frame = null; request.Dispose(); }
        }

        async void Step(int delta)
        {
            if (_media == null || _load != null) return;
            Pause(); _index = Math.Max(0, Math.Min(_media.Times.Length - 1, _index + delta));
            UpdatePosition(); await ShowFrameAsync();
        }
        public void Suspend() { Pause(); _frame?.Cancel(); }
        void Pause()
        {
            _playing = false; _timer.Stop(); _gifClock.Stop();
            if (_gif == null) Player.Pause();
            PlayButton.Content = "播放";
        }
        void TogglePlay()
        {
            if (_media == null || _load != null) return;
            if (_playing) { Pause(); Step(0); return; }
            _frame?.Cancel();
            if (_index == _media.Times.Length - 1) _index = 0;
            _playing = true; PlayButton.Content = "暂停";
            if (_gif != null) {
                Still.Visibility = Visibility.Visible;
                Still.Source = DecodeBitmap(_gif.ReadFrame(_index)); _displayedIndex = _index;
                _gifStartTime = _media.Times[_index]; _gifClock.Restart(); _timer.Start(); return;
            }
            Still.Visibility = Visibility.Collapsed;
            Player.Play();
            if (_playerReady) Player.Position = TimeSpan.FromSeconds(_media.Times[_index]);
            _timer.Start();
        }
        void ToggleSelected()
        {
            if (_media == null || _load != null || _export != null) return;
            if (_playing) { Pause(); Step(0); Status.Text = "已暂停，请确认画面后再次按空格选中。"; return; }
            // 不选择尚未显示的帧，避免快速连按时导出与眼前画面不一致。
            if (_displayedIndex != _index) { Status.Text = "正在定位帧，画面更新后即可选中。"; return; }
            if (!_selected.Remove(_index)) _selected.Add(_index);
            RefreshSelection(); UpdatePosition();
        }
        void RefreshSelection()
        {
            _updating = true;
            SelectedFrames.ItemsSource = _selected.Select(i => new FrameItem { Index = i, Text = $"第 {i + 1} 帧 · {TimeSpan.FromSeconds(_media.Times[i]):hh\\:mm\\:ss\\.fff}" }).ToArray();
            SelectedFrames.DisplayMemberPath = "Text"; _updating = false;
            SelectionLabel.Text = $"已选 {_selected.Count} 帧";
        }
        sealed class FrameItem { public int Index { get; set; } public string Text { get; set; } }
        void Review_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            if (e.Key == Key.Left || e.Key == Key.Right) {
                var direction = e.Key == Key.Left ? -1 : 1;
                if (FrameMode.IsChecked == true) Step(direction);
                else SeekSeconds(direction * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1));
            }
            else if (e.Key == Key.Space) { if (FrameMode.IsChecked == true) ToggleSelected(); else TogglePlay(); }
            else if (e.Key == Key.P) TogglePlay();
            else return;
            e.Handled = true;
        }
        void SeekSeconds(int seconds)
        {
            if (_media == null || _load != null) return;
            var resume = _playing;
            var time = _playing ? (_gif != null ? _gifStartTime + _gifClock.Elapsed.TotalSeconds : Player.Position.TotalSeconds) : _media.Times[_index];
            Pause(); _index = _media.FrameAt(Math.Max(0, Math.Min(_media.Duration, time + seconds)));
            if (resume && _index < _media.Times.Length - 1) TogglePlay(); else Step(0);
            UpdatePosition();
        }
        void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (Status == null) return;
            Status.Text = FrameMode.IsChecked == true ? "选帧模式：← → 每次一帧，空格选中 / 取消；P 播放 / 暂停。" : "播放模式：← → 按 1 秒移动，Shift + ← → 按 5 秒移动，空格播放 / 暂停。";
            if (FrameMode.IsChecked == true && _media != null) { Pause(); Step(0); }
            UpdatePosition();
        }
        async void Timeline_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating || _media == null || _load != null) return;
            Pause(); _index = (int)Math.Round(e.NewValue); UpdatePosition(); await ShowFrameAsync();
        }
        async void Selection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_updating || !(SelectedFrames.SelectedItem is FrameItem item)) return;
            Pause(); _index = item.Index; UpdatePosition(); await ShowFrameAsync();
        }
        void Previous_Click(object sender, RoutedEventArgs e) { FrameMode.IsChecked = true; Step(-1); Focus(); }
        void Next_Click(object sender, RoutedEventArgs e) { FrameMode.IsChecked = true; Step(1); Focus(); }
        void Play_Click(object sender, RoutedEventArgs e) { TogglePlay(); Focus(); }
        void Select_Click(object sender, RoutedEventArgs e) { FrameMode.IsChecked = true; ToggleSelected(); Focus(); }
        void Clear_Click(object sender, RoutedEventArgs e) { if (_export != null) return; _selected.Clear(); RefreshSelection(); UpdatePosition(); }
        void RangeStart_Click(object sender, RoutedEventArgs e) { if (_media == null) return; _rangeStart = _index; RangeStartButton.Content = $"起点：第 {_index + 1} 帧"; }
        void RangeEnd_Click(object sender, RoutedEventArgs e)
        {
            if (_media == null || _export != null) return;
            for (var i = Math.Min(_rangeStart, _index); i <= Math.Max(_rangeStart, _index); i++) _selected.Add(i);
            RefreshSelection(); UpdatePosition();
        }
        async void Open_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "打开视频或 GIF", Filter = "视频与 GIF|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.webm;*.gif;*.m4v|所有文件|*.*" };
            if (dialog.ShowDialog() == true) await OpenMediaAsync(dialog.FileName);
        }
        void Live_Click(object sender, RoutedEventArgs e) { Suspend(); LiveRequested?.Invoke(); }
        void Media_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
        async void Media_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) await OpenMediaAsync(files[0]);
            e.Handled = true;
        }
        void Preview_MouseDown(object sender, MouseButtonEventArgs e) { Focus(); e.Handled = true; }
        void Player_Opened(object sender, RoutedEventArgs e)
        {
            if (_gif != null) return;
            _playerReady = true;
            if (_media == null) return;
            Player.Position = TimeSpan.FromSeconds(_media.Times[_index]);
            if (!_playing) Player.Pause();
        }
        static BitmapImage DecodeBitmap(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            return image;
        }
        void Player_Ended(object sender, RoutedEventArgs e) { if (_gif != null) return; Pause(); if (_media != null) { _index = _media.Times.Length - 1; Step(0); } }
        void Player_Failed(object sender, ExceptionRoutedEventArgs e) { if (_gif != null) return; Pause(); Status.Text = "系统播放器无法播放此编码；仍可使用左右键逐帧查看和导出。"; Step(0); }
        void SetBusy(bool busy) { CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; ExportBar.IsEnabled = !busy; }
        void Cancel_Click(object sender, RoutedEventArgs e) { _load?.Cancel(); _export?.Cancel(); Status.Text = "已请求取消；已导出的文件会保留。"; }
        async void ExportPng_Click(object sender, RoutedEventArgs e) => await ExportAsync("png");
        async void ExportMp4_Click(object sender, RoutedEventArgs e) => await ExportAsync("mp4");
        async void ExportGif_Click(object sender, RoutedEventArgs e) => await ExportAsync("gif");
        async Task ExportAsync(string kind)
        {
            if (_media == null || _selected.Count == 0) { Status.Text = "请先用空格选择帧，或设置起点后选择连续范围。"; return; }
            if (_export != null || _load != null) return;
            if (!double.TryParse(ExportFps.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) || fps < 1 || fps > 60 || double.IsNaN(fps)) { Status.Text = "合成帧率须为 1—60。"; return; }
            Pause(); var media = _media; var frames = _selected.ToArray();
            string output;
            try {
                if (string.IsNullOrWhiteSpace(OutputRoot.Text)) throw new IOException("请设置导出目录。");
                var root = Path.GetFullPath(OutputRoot.Text);
                Directory.CreateDirectory(root);
                ServiceProvider.Get<Settings>().FrameExportFolder = root;
                output = Path.Combine(root, "Captura-frames-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            }
            catch (Exception e) { Status.Text = "导出位置无效：" + e.Message; return; }
            var operation = new CancellationTokenSource(); _export = operation; SetBusy(true);
            _output = output; OutputButton.Visibility = Visibility.Collapsed;
            try
            {
                await _service.ExportAsync(media, frames, output, kind, fps, operation.Token, new Progress<string>(s => Status.Text = s));
                Status.Text = $"已导出 {frames.Length} 帧" + (kind == "png" ? "图片" : "并合成 " + kind.ToUpperInvariant() + "（无音频）") + "：" + output;
            }
            catch (OperationCanceledException) { Status.Text = "已取消，部分输出保留在：" + output; }
            catch (Exception e) { Status.Text = "导出失败：" + e.Message + "\n部分输出：" + output; }
            finally { _export = null; operation.Dispose(); SetBusy(false); OutputButton.Visibility = Directory.Exists(output) ? Visibility.Visible : Visibility.Collapsed; }
        }
        void Output_Click(object sender, RoutedEventArgs e) { if (Directory.Exists(_output)) ServiceProvider.LaunchFile(new ProcessStartInfo(_output)); }
        void OutputFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "选择导出位置（自动创建独立子目录）", ShowNewFolderButton = true };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) OutputRoot.Text = dialog.SelectedPath;
        }
        public void Dispose()
        {
            _disposed = true; _load?.Cancel(); _frame?.Cancel(); _export?.Cancel();
            _timer.Stop(); Player.Close(); Still.Source = null; _cache.Clear();
            _gif?.Dispose(); _gif = null;
        }
    }
}
