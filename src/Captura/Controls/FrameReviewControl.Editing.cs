using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Captura.FFmpeg;

namespace Captura
{
    public partial class FrameReviewControl
    {
        FrameTimeline _timeline;
        CancellationTokenSource _edit, _playbackRequest;
        FramePlaybackSession _editedPlayback;
        DecodedReviewFrame _nextDecoded;
        int _timelineRevision;
        int FrameCount => _timeline?.Count ?? _media?.Times.Length ?? 0;
        bool Edited => _timeline?.IsEdited == true;
        double[] FrameTimes => Edited ? _timeline.Times : _media.Times;
        double FrameDuration => Edited ? _timeline.Duration : _media.Duration;
        int FrameAt(double time) => Edited ? _timeline.FrameAt(time) : _media.FrameAt(time);
        int SourceAt(int index) => _timeline?.SourceAt(index) ?? index;
        bool CanEdit => ExactIndexReady && _media != null && _timeline != null && _load == null && _export == null && _edit == null && !_disposed;

        void UpdateEditState()
        {
            if (DuplicatesApply == null) return;
            DuplicatesApply.IsEnabled = CanEdit; SimilarityInput.IsEnabled = CanEdit;
            DuplicatesCancel.IsEnabled = _edit != null;
            DeleteFramesButton.IsEnabled = CanEdit && _selected.Count > 0;
            ReduceFramesButton.IsEnabled = CanEdit && FrameCount > 1;
            UndoEditButton.IsEnabled = CanEdit && _timeline.CanUndo;
            RedoEditButton.IsEnabled = CanEdit && _timeline.CanRedo;
        }
        async void DuplicatesApply_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEdit) return;
            if (!double.TryParse(SimilarityInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var similarity)
                || double.IsNaN(similarity) || similarity < 0.1 || similarity > 100) {
                Status.Text = "相似度须为 0.1—100，100% 仅移除像素完全相同的连续帧。"; return;
            }
            Pause(); var media = _media; var source = SourceAt(_index); var frames = _timeline.Snapshot();
            var operation = new CancellationTokenSource(); _edit = operation; SetBusy(true);
            try {
                var remove = await _service.FindDuplicatesAsync(media, frames, similarity, operation.Token,
                    new Progress<int>(n => { if (_edit == operation && !_disposed) Status.Text = $"正在比较帧：{n} / {frames.Length}"; }));
                operation.Token.ThrowIfCancellationRequested();
                if (_disposed || _media != media) return;
                if (_timeline.Remove(remove)) { RebuildEditedView(source); Status.Text = $"已移除 {remove.Length} 个重复帧，保留每组首帧；可撤销。"; }
                else Status.Text = "没有找到达到该相似度的连续重复帧。";
            }
            catch (OperationCanceledException) { if (!_disposed) Status.Text = "已取消比较，时间轴未修改。"; }
            catch (Exception error) { if (!_disposed) Status.Text = "移除重复失败：" + error.Message; }
            finally { if (_edit == operation) _edit = null; operation.Dispose(); if (!_disposed) SetBusy(false); }
        }
        void DuplicatesCancel_Click(object sender, RoutedEventArgs e) => _edit?.Cancel();
        void DeleteFrames_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEdit || _selected.Count == 0) return;
            var source = SourceAt(_index); var count = _selected.Count;
            try { if (_timeline.Remove(_selected.ToArray())) { RebuildEditedView(source); Status.Text = $"已删除 {count} 帧；原文件保留，可撤销。"; } }
            catch (InvalidOperationException error) { Status.Text = error.Message; }
        }
        void ReduceFrames_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEdit) return;
            ReduceHint.Text = "作用于整个时间轴，保留每组首帧；可撤销。"; ReduceFramesPopup.IsOpen = true;
        }
        void ReduceCancel_Click(object sender, RoutedEventArgs e) => ReduceFramesPopup.IsOpen = false;
        void ReduceApply_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEdit) return;
            if (!int.TryParse(ReduceInterval.Text, out var interval) || interval < 2 || interval > 1000) { ReduceHint.Text = "间隔须为 2—1000 的整数。"; return; }
            var source = SourceAt(_index); var before = FrameCount;
            if (_timeline.Reduce(interval)) { RebuildEditedView(source); Status.Text = $"已按每 {interval} 帧保留 1 帧，移除 {before - FrameCount} 帧；可撤销。"; }
            ReduceFramesPopup.IsOpen = false;
        }
        void UndoEdit_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEdit) return;
            var source = SourceAt(_index);
            if (_timeline.Undo()) { RebuildEditedView(source); Status.Text = $"已撤销编辑，当前 {FrameCount} 帧。"; }
        }
        void RedoEdit_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEdit) return;
            var source = SourceAt(_index);
            if (_timeline.Redo()) { RebuildEditedView(source); Status.Text = $"已重做编辑，当前 {FrameCount} 帧。"; }
        }
        void RebuildEditedView(int source)
        {
            Pause(); _frame?.Cancel(); _thumbnailRequest?.Cancel(); _pendingThumbnailIndices = null; _timelineRevision++;
            _cache.Clear(); _thumbnails.Clear(); _markedSelection.Clear(); _selection.Reset(FrameCount);
            _index = _timeline.LocateSource(source); _selection.SetAnchor(_index); _currentTile = _displayedIndex = -1; _rangeStart = 0;
            _tiles = FrameTimes.Select((time, i) => new FrameTile(i, time)).ToList();
            _updating = true;
            try { FrameStrip.ItemsSource = _tiles; Timeline.Maximum = FrameCount - 1; FrameMode.IsChecked = true; }
            finally { _updating = false; }
            RangeStartButton.Content = "以当前帧为起点";
            RefreshSelection(); UpdatePosition(); RevealCurrent(); _ = ShowFrameAsync(); ScheduleThumbnails(); UpdateEditState();
        }
        void PresentEditedVideo()
        {
            if (_editedPlayback == null) return;
            if (_editedPlayback.Error != null) { var error = _editedPlayback.Error; Pause(); Status.Text = "编辑预览失败：" + error.Message; return; }
            var time = _gifStartTime + _gifClock.Elapsed.TotalSeconds;
            var desired = FrameAt(Math.Min(FrameDuration, time));
            DecodedReviewFrame latest = null;
            while (true) {
                if (_nextDecoded == null && !_editedPlayback.TryRead(out _nextDecoded)) break;
                if (_nextDecoded.Index > desired) break;
                latest = _nextDecoded; _nextDecoded = null;
            }
            if (latest != null) {
                var image = BitmapSource.Create(_editedPlayback.Width, _editedPlayback.Height, 96, 96, PixelFormats.Bgra32, null, latest.Pixels, _editedPlayback.Width * 4);
                image.Freeze(); Still.Source = image; Still.Visibility = Visibility.Visible;
                _index = _displayedIndex = latest.Index;
            }
            if (time >= FrameDuration && _index == FrameCount - 1) Pause();
        }
    }
}
