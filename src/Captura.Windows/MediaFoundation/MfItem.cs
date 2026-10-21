using Captura.Video;
using SharpDX.Direct3D11;

namespace Captura.Windows.MediaFoundation
{
    public class MfItem : IVideoWriterItem
    {
        readonly Device _device;

        public string Extension => ".mp4";
        public string Description { get; } = "使用 Media Foundation 硬件编码 MP4：H.264 视频和 AAC 音频。";

        readonly string _name = "MF";

        public MfItem(Device Device)
        {
            _device = Device;
        }

        public override string ToString() => _name;

        public virtual IVideoFileWriter GetVideoFileWriter(VideoWriterArgs Args)
        {
            return new MfWriter(Args, _device);
        }
    }
}