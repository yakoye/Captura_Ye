using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
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
        readonly FrameSelection _selection = new FrameSelection();
        IReadOnlyCollection<int> _selected => _selection.Selected;
        List<FrameTile> _tiles = new List<FrameTile>();
        HashSet<int> _markedSelection = new HashSet<int>();
        readonly Dictionary<int, BitmapSource> _thumbnails = new Dictionary<int, BitmapSource>();
        readonly SemaphoreSlim _thumbnailGate = new SemaphoreSlim(1, 1);
        CancellationTokenSource _thumbnailRequest;
        bool _thumbnailRefreshQueued, _syncingSelection;
        int _currentTile = -1;
        int[] _pendingThumbnailIndices;
        GifPlayback _gif;
        readonly Stopwatch _gifClock = new Stopwatch();
        double _gifStartTime;
        int _index, _rangeStart, _displayedIndex = -1;
        bool _updating, _playing, _disposed, _playerReady;
        string _output;

        public FrameReviewControl()
        {
            InitializeComponent();
            FrameStrip.ItemContainerGenerator.StatusChanged += (sender, e) => ScheduleThumbnails();
            Loaded += (sender, e) => ScheduleThumbnails();
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
            _load?.Cancel(); _frame?.Cancel(); _thumbnailRequest?.Cancel();
            var load = new CancellationTokenSource();
            _load = load;
            Pause(); Player.Close(); Player.Source = null; Still.Source = null; _cache.Clear();
            _gif?.Dispose(); _gif = null;
            _media = null; _pendingThumbnailIndices = null; _selection.Reset(0); _markedSelection.Clear(); _tiles.Clear(); _thumbnails.Clear();
            FrameStrip.ItemsSource = null; _currentTile = -1; RefreshSelection();
            _displayedIndex = -1; _index = _rangeStart = 0;
            FrameMode.IsChecked = false;
            RangeStartButton.Content = "以当前帧为起点";
            FileLabel.Text = Path.GetFileName(file); FileLabel.ToolTip = file;
            EmptyPanel.Visibility = Visibility.Visible; EmptyHint.Text = "正在读取帧索引……";
            ReviewRequested?.Invoke(); SetBusy(true);
            try
            {
                var executable = await Task.Run(() => FFmpegService.DetectExecutable(), load.Token);
                if (executable == null) throw new IOException("未找到 FFmpeg，请到设置中的 FFmpeg 页面自动检测或指定目录。");
                var service = new FrameReviewService(executable);
                var media = await service.OpenAsync(file, load.Token);
                load.Token.ThrowIfCancellationRequested();
                _service = service; _media = media;
                _selection.Reset(media.Times.Length);
                _tiles = media.Times.Select((time, i) => new FrameTile(i, time)).ToList();
                FrameStrip.ItemsSource = _tiles;
                var preferred = ServiceProvider.Get<Settings>().FrameExportFolder;
                OutputRoot.Text = string.IsNullOrWhiteSpace(preferred) ? Path.Combine(Path.GetDirectoryName(media.FilePath), "Captura-exports") : preferred;
                _playerReady = false;
                if (Path.GetExtension(file).Equals(".gif", StringComparison.OrdinalIgnoreCase)) {
                    _gif = new GifPlayback(file);
                    if (_gif.FrameCount != media.Times.Length) throw new IOException("GIF 帧索引与解码器不一致，无法播放。");
                }
                else { Player.Source = new Uri(Path.GetFullPath(file)); Player.Pause(); }
                Timeline.Maximum = media.Times.Length - 1;
                EmptyPanel.Visibility = Visibility.Collapsed;
                ExportFps.Text = Math.Max(1, Math.Min(60, Math.Round(media.Times.Length / media.Duration, 3))).ToString(CultureInfo.InvariantCulture);
                Status.Text = "";
                UpdatePosition(); await ShowFrameAsync(); ScheduleThumbnails();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!load.IsCancellationRequested) { EmptyPanel.Visibility = Visibility.Visible; EmptyHint.Text = "无法打开媒体，请查看下方提示。"; Status.Text = e.Message; } }
            finally { if (_load == load) { _load = null; SetBusy(false); } load.Dispose(); }
        }

        void UpdatePosition()
        {
            if (_media == null) return;
            _selection.View(_index);
            _updating = true; Timeline.Value = _index; _updating = false;
            PositionLabel.Text = $"当前查看：第 {_index + 1} / {_media.Times.Length} 帧 · {TimeSpan.FromSeconds(_media.Times[_index]):hh\\:mm\\:ss\\.fff}";
            if (_currentTile != _index && _tiles.Count > _index) {
                if (_currentTile >= 0 && _currentTile < _tiles.Count) _tiles[_currentTile].IsCurrent = false;
                _tiles[_index].IsCurrent = true; _currentTile = _index;
                if (_playing) FrameStrip.ScrollIntoView(_tiles[_index]);
            }
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
            UpdatePosition(); RevealCurrent(); await ShowFrameAsync();
        }
        public void Suspend() { Pause(); _frame?.Cancel(); _pendingThumbnailIndices = null; _thumbnailRequest?.Cancel(); }
        public void ResumePreview() => ScheduleThumbnails();
        void Pause()
        {
            _playing = false; _timer.Stop(); _gifClock.Stop();
            if (_gif == null) Player.Pause();
            UpdatePlayIcon(false);
        }
        void UpdatePlayIcon(bool playing)
        {
            PlayGlyph.Data = (Geometry)FindResource(playing ? "PlayerIcon-pause" : "PlayerIcon-play");
            var label = playing ? "暂停" : "播放";
            PlayButton.ToolTip = label + "（空格）";
            System.Windows.Automation.AutomationProperties.SetName(PlayButton, label);
        }
        void TogglePlay()
        {
            if (_media == null || _load != null) return;
            if (_playing) { Pause(); Step(0); return; }
            _frame?.Cancel();
            if (_index == _media.Times.Length - 1) _index = 0;
            _playing = true; UpdatePlayIcon(true);
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
            if (_playing) { Pause(); Step(0); }
            _selection.Toggle(_index);
            RefreshSelection(); UpdatePosition();
        }
        void RefreshSelection()
        {
            var selected = new HashSet<int>(_selected);
            _syncingSelection = true;
            foreach (var i in _markedSelection.Except(selected)) if (i < _tiles.Count) _tiles[i].IsSelected = false;
            foreach (var i in selected.Except(_markedSelection)) if (i < _tiles.Count) _tiles[i].IsSelected = true;
            _syncingSelection = false;
            _markedSelection = selected;
            SelectionLabel.Text = $"已选 {_selected.Count} 帧";
            SelectionSummary.Text = _selected.Count == 0 ? ""
                : "帧号：" + string.Join("、", _selected.Take(6).Select(i => i + 1)) + (_selected.Count > 6 ? " 等" : "") + "；按原时间顺序导出";
            UpdateExportState();
        }
        void UpdateExportState()
        {
            if (ExportButton != null) ExportButton.IsEnabled = _media != null && _selected.Count > 0 && _load == null && _export == null;
        }
        void RevealCurrent()
        {
            if (_index >= 0 && _index < _tiles.Count) FrameStrip.ScrollIntoView(_tiles[_index]);
            ScheduleThumbnails();
        }
        static T Ancestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null) {
                if (source is T result) return result;
                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            return null;
        }
        bool IsReviewFocus(DependencyObject source)
        {
            if (Ancestor<TextBoxBase>(source) != null || Ancestor<PasswordBox>(source) != null || Ancestor<ComboBox>(source) != null
                || Ancestor<ButtonBase>(source) != null) return false;
            for (var item = source; item != null; item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
                if (item == PreviewSurface || item == FrameStrip || item == Timeline || item == this) return true;
            return false;
        }
        void Review_KeyDown(object sender, KeyEventArgs e)
        {
            if (!IsReviewFocus(e.OriginalSource as DependencyObject) || _media == null || _load != null) return;
            var modifiers = Keyboard.Modifiers;
            if (modifiers.HasFlag(ModifierKeys.Alt)) return;
            if ((e.Key == Key.Return || e.Key == Key.Enter) && modifiers == ModifierKeys.Control) ToggleSelected();
            else if (e.Key == Key.A && modifiers == ModifierKeys.Control) { if (_export == null) { _selection.SelectAll(); RefreshSelection(); } }
            else if (e.Key == Key.A && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { if (_export == null) { _selection.Clear(); RefreshSelection(); } }
            else if (e.Key == Key.Home || e.Key == Key.End) {
                Pause(); _index = e.Key == Key.Home ? 0 : _media.Times.Length - 1;
                UpdatePosition(); RevealCurrent(); _ = ShowFrameAsync();
            }
            else if (e.Key == Key.PageUp || e.Key == Key.PageDown) Step((e.Key == Key.PageUp ? -1 : 1) * Math.Max(1, (int)(FrameStrip.ActualWidth / 162)));
            else if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Space && modifiers != ModifierKeys.None) { }
            else if (e.Key == Key.Left || e.Key == Key.Right) {
                var direction = e.Key == Key.Left ? -1 : 1;
                if (FrameMode.IsChecked == true) {
                    if (modifiers.HasFlag(ModifierKeys.Shift) && _export == null) {
                        var target = Math.Max(0, Math.Min(_media.Times.Length - 1, _index + direction));
                        _selection.SelectRange(_selection.Anchor, target, modifiers.HasFlag(ModifierKeys.Control)); RefreshSelection();
                    }
                    Step(direction);
                }
                else if (!modifiers.HasFlag(ModifierKeys.Control)) SeekSeconds(direction * (modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1));
                else return;
            }
            else if (e.Key == Key.Space && modifiers == ModifierKeys.None) { if (!e.IsRepeat) TogglePlay(); }
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
            if (FrameMode.IsChecked == true && _media != null) { Pause(); Step(0); }
            UpdatePosition();
            if (IsLoaded && _media != null) PreviewSurface.Focus();
        }

        void FrameStrip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var container = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (!(container?.DataContext is FrameTile tile) || _media == null) return;
            e.Handled = true; FrameStrip.Focus();
            if (_export == null) {
                SelectTile(tile, Ancestor<CheckBox>(e.OriginalSource as DependencyObject) != null);
                RefreshSelection();
            }
            FrameMode.IsChecked = true;
            Pause(); _index = tile.Index; UpdatePosition();
            _ = ShowFrameAsync();
        }
        void FrameStrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSelection) return;
            // 原生列表的焦点导航不能替换导出选择；由选择模型统一维护状态。
            _syncingSelection = true;
            try {
                foreach (var tile in e.AddedItems.Cast<object>().Concat(e.RemovedItems.Cast<object>()).OfType<FrameTile>())
                    (FrameStrip.ItemContainerGenerator.ContainerFromItem(tile) as ListBoxItem)?.GetBindingExpression(ListBoxItem.IsSelectedProperty)?.UpdateTarget();
            }
            finally { _syncingSelection = false; }
        }
        void TileCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_export != null || !((sender as FrameworkElement)?.DataContext is FrameTile tile)) return;
            e.Handled = true; SelectTile(tile, true); RefreshSelection();
            Pause(); _index = tile.Index; FrameMode.IsChecked = true; UpdatePosition(); _ = ShowFrameAsync();
            FrameStrip.Focus();
        }
        void SelectTile(FrameTile tile, bool checkbox)
        {
            var modifiers = Keyboard.Modifiers;
            if (checkbox && !modifiers.HasFlag(ModifierKeys.Shift)) _selection.Toggle(tile.Index);
            else _selection.Click(tile.Index, modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift));
        }
        static T VisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var nested = VisualChild<T>(child); if (nested != null) return nested;
            }
            return null;
        }
        void FrameStrip_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scroll = VisualChild<ScrollViewer>(FrameStrip);
            if (scroll == null) return;
            if (e.Delta < 0) scroll.LineRight(); else scroll.LineLeft();
            e.Handled = true;
        }
        void FrameStrip_ScrollChanged(object sender, ScrollChangedEventArgs e) => ScheduleThumbnails();
        void ScheduleThumbnails()
        {
            if (_disposed || _media == null || _thumbnailRefreshQueued) return;
            _thumbnailRefreshQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
                _thumbnailRefreshQueued = false;
                if (_disposed || _media == null) return;
                var panel = VisualChild<VirtualizingStackPanel>(FrameStrip);
                if (panel == null) return;
                var visible = panel.Children.OfType<ListBoxItem>().Where(c => {
                    var point = c.TranslatePoint(new Point(0, 0), FrameStrip);
                    return point.X + c.ActualWidth > 0 && point.X < FrameStrip.ActualWidth;
                }).Select(c => c.DataContext).OfType<FrameTile>().Select(t => t.Index).ToArray();
                if (visible.Length == 0) return;
                var first = Math.Max(0, visible.Min() - 2);
                var last = Math.Min(_tiles.Count - 1, visible.Max() + 2);
                var center = (visible.Min() + visible.Max()) / 2;
                _pendingThumbnailIndices = Enumerable.Range(first, last - first + 1).OrderBy(i => Math.Abs(i - center)).ToArray();
                if (_thumbnailRequest != null && !_thumbnailRequest.IsCancellationRequested) return;
                var request = new CancellationTokenSource(); _thumbnailRequest = request;
                _ = LoadThumbnailsAsync(request, _media);
            }));
        }
        async Task LoadThumbnailsAsync(CancellationTokenSource request, ReviewMedia media)
        {
            try {
                await Task.Delay(60, request.Token);
                await _thumbnailGate.WaitAsync(request.Token);
                try {
                    while (_pendingThumbnailIndices != null) {
                    request.Token.ThrowIfCancellationRequested();
                    if (_media != media || _disposed) return;
                    var indices = _pendingThumbnailIndices; _pendingThumbnailIndices = null;
                    foreach (var i in indices) {
                        request.Token.ThrowIfCancellationRequested();
                        if (_media != media || _disposed) return;
                        if (_pendingThumbnailIndices != null && !_pendingThumbnailIndices.Contains(i)) break;
                        if (_thumbnails.ContainsKey(i)) continue;
                        try {
                            var bytes = await _service.ReadThumbnailAsync(media, i, request.Token);
                            request.Token.ThrowIfCancellationRequested();
                            if (_media != media || _disposed) return;
                            var image = DecodeBitmap(bytes); _thumbnails[i] = image; _tiles[i].Thumbnail = image;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (IOException) { if (_media == media && !_disposed && i < _tiles.Count) _tiles[i].ThumbnailHint = "缩略图暂不可用"; }
                    }
                    var center = indices.First();
                    foreach (var evict in _thumbnails.Keys.OrderByDescending(i => Math.Abs(i - center)).Take(Math.Max(0, _thumbnails.Count - 160)).ToArray()) {
                        _tiles[evict].Thumbnail = null; _thumbnails.Remove(evict);
                    }
                    }
                }
                finally { _thumbnailGate.Release(); }
            }
            catch (OperationCanceledException) { }
            finally { if (_thumbnailRequest == request) _thumbnailRequest = null; request.Dispose(); }
        }
        async void Timeline_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating || _media == null || _load != null) return;
            Pause(); _index = (int)Math.Round(e.NewValue); UpdatePosition(); await ShowFrameAsync();
        }
        void Previous_Click(object sender, RoutedEventArgs e) { FrameMode.IsChecked = true; Step(-1); Focus(); }
        void Next_Click(object sender, RoutedEventArgs e) { FrameMode.IsChecked = true; Step(1); Focus(); }
        void First_Click(object sender, RoutedEventArgs e) { GoToEdge(false); Focus(); }
        void Last_Click(object sender, RoutedEventArgs e) { GoToEdge(true); Focus(); }
        void GoToEdge(bool last)
        {
            if (_media == null || _load != null) return;
            Pause(); _index = last ? _media.Times.Length - 1 : 0; Step(0);
        }
        void BackFive_Click(object sender, RoutedEventArgs e) { SeekSeconds(-5); Focus(); }
        void BackOne_Click(object sender, RoutedEventArgs e) { SeekSeconds(-1); Focus(); }
        void ForwardOne_Click(object sender, RoutedEventArgs e) { SeekSeconds(1); Focus(); }
        void ForwardFive_Click(object sender, RoutedEventArgs e) { SeekSeconds(5); Focus(); }
        void Play_Click(object sender, RoutedEventArgs e) { TogglePlay(); Focus(); }
        void Help_Click(object sender, RoutedEventArgs e) { ReviewHelp.IsOpen = !ReviewHelp.IsOpen; }
        void HelpClose_Click(object sender, RoutedEventArgs e) { ReviewHelp.IsOpen = false; }
        void Select_Click(object sender, RoutedEventArgs e) { FrameMode.IsChecked = true; ToggleSelected(); Focus(); }
        void Clear_Click(object sender, RoutedEventArgs e) { if (_export != null) return; _selection.Clear(); RefreshSelection(); }
        void All_Click(object sender, RoutedEventArgs e) { if (_export != null) return; _selection.SelectAll(); RefreshSelection(); }
        void RangeStart_Click(object sender, RoutedEventArgs e) { if (_media == null) return; _rangeStart = _index; _selection.SetAnchor(_index); RangeStartButton.Content = $"起点：第 {_index + 1} 帧"; }
        void RangeEnd_Click(object sender, RoutedEventArgs e)
        {
            if (_media == null || _export != null) return;
            _selection.SelectRange(_rangeStart, _index, true);
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
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) await OpenMediaAsync(files[0]);
        }
        void Preview_MouseDown(object sender, MouseButtonEventArgs e) { PreviewSurface.Focus(); e.Handled = true; }
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
        void SetBusy(bool busy) { CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; UpdateExportState(); }
        void Cancel_Click(object sender, RoutedEventArgs e) { _load?.Cancel(); _export?.Cancel(); Status.Text = "已请求取消；已导出的文件会保留。"; }
        async void ExportSelected_Click(object sender, RoutedEventArgs e) => await ExportAsync(ExportFormat.SelectedIndex == 1 ? "mp4" : ExportFormat.SelectedIndex == 2 ? "gif" : "png");
        void ExportFormat_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (RatePanel != null) RatePanel.Visibility = ExportFormat.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        async Task ExportAsync(string kind)
        {
            if (_media == null || _selected.Count == 0) { Status.Text = "请先勾选缩略图，或使用 Ctrl / Shift 多选需要导出的帧。"; return; }
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
            _disposed = true; _load?.Cancel(); _frame?.Cancel(); _export?.Cancel(); _thumbnailRequest?.Cancel();
            _timer.Stop(); Player.Close(); Still.Source = null; _cache.Clear(); _thumbnails.Clear(); _tiles.Clear();
            _gif?.Dispose(); _gif = null;
        }
    }
}
