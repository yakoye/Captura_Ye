using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Captura
{
    public partial class FrameReviewControl
    {
        CancellationTokenSource _indexing;
        bool ExactIndexReady => _media != null && !_media.IsApproximate && _load == null;
        public static readonly DependencyProperty CanSelectFramesProperty = DependencyProperty.Register(
            nameof(CanSelectFrames), typeof(bool), typeof(FrameReviewControl), new PropertyMetadata(false));
        public bool CanSelectFrames { get => (bool)GetValue(CanSelectFramesProperty); private set => SetValue(CanSelectFramesProperty, value); }

        async Task CompleteIndexAsync(Captura.FFmpeg.ReviewMedia initial)
        {
            var request = new CancellationTokenSource(); _indexing = request;
            try {
                Status.Text = "可先播放和拖动定位，正在后台建立精确帧索引……";
                var media = await _service.CompleteIndexAsync(initial, request.Token, new Progress<int>(n => {
                    if (_indexing == request && !_disposed) Status.Text = $"可先播放和定位，后台已读取 {n} 帧……";
                }));
                request.Token.ThrowIfCancellationRequested();
                if (_disposed || _media != initial) return;
                var time = _playing && _playerReady ? Player.Position.TotalSeconds : FrameTimes[_index];
                _frame?.Cancel(); _thumbnailRequest?.Cancel(); _pendingThumbnailIndices = null; _timelineRevision++;
                _media = media; _timeline = new FrameTimeline(media.Times, media.Duration);
                _index = media.FrameAt(time); _currentTile = _displayedIndex = -1;
                _selection.Reset(media.Times.Length); _markedSelection.Clear(); _cache.Clear(); _thumbnails.Clear();
                _tiles = media.Times.Select((t, i) => new FrameTile(i, t)).ToList();
                _updating = true;
                try { FrameStrip.ItemsSource = _tiles; Timeline.Maximum = media.Times.Length - 1; }
                finally { _updating = false; }
                Status.Text = ""; RefreshSelection(); UpdatePosition();
                if (!_playing) await ShowFrameAsync();
                ScheduleThumbnails();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) {
                if (_media == initial && !_disposed) Status.Text = "精确索引读取失败，仍可播放和定位；请重新打开文件重试：" + error.Message;
            }
            finally { if (_indexing == request) _indexing = null; request.Dispose(); if (!_disposed) UpdateExportState(); }
        }
    }
}
