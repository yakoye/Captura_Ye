using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Captura
{
    /// <summary>使用 Windows 原生 GIF 解码器播放，无需临时转码或系统视频编码器。</summary>
    public sealed class GifPlayback : IDisposable
    {
        readonly Image _image;
        public int FrameCount { get; }
        public GifPlayback(string file)
        {
            _image = Image.FromFile(file);
            FrameCount = _image.GetFrameCount(FrameDimension.Time);
        }
        public byte[] ReadFrame(int index)
        {
            if (index < 0 || index >= FrameCount) throw new ArgumentOutOfRangeException(nameof(index));
            _image.SelectActiveFrame(FrameDimension.Time, index);
            using var bitmap = new Bitmap(_image.Width, _image.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.DrawImageUnscaled(_image, 0, 0);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        public void Dispose() => _image.Dispose();
    }
}
