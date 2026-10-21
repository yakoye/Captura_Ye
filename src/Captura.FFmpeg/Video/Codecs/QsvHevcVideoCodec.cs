using Captura.Video;

namespace Captura.FFmpeg
{
    class QsvHevcVideoCodec : FFmpegVideoCodec
    {
        static readonly string Descr = Captura.Loc.LanguageManager.Instance["QsvDescription"];

        public QsvHevcVideoCodec() : base("Intel QuickSync: Mp4 (HEVC, AAC)", ".mp4", Descr) { }

        public override FFmpegAudioArgsProvider AudioArgsProvider => FFmpegAudioItem.Aac;

        public override void Apply(FFmpegSettings Settings, VideoWriterArgs WriterArgs, FFmpegOutputArgs OutputArgs)
        {
            OutputArgs.AddArg("vcodec", "hevc_qsv")
                .AddArg("load_plugin", "hevc_hw")
                .AddArg("q", 2)
                .AddArg("preset:v", "veryfast");
        }
    }
}