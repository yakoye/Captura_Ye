using Captura.Video;

namespace Captura.FFmpeg
{
    class NvencVideoCodec : FFmpegVideoCodec
    {
        readonly string _fFmpegCodecName;

        static readonly string NVencSupport = Captura.Loc.LanguageManager.Instance["NvencSupport"];

        NvencVideoCodec(string Name, string FFmpegCodecName, string Description)
            : base(Name, ".mp4", $"{Description}\n{NVencSupport}")
        {
            _fFmpegCodecName = FFmpegCodecName;
        }

        public override FFmpegAudioArgsProvider AudioArgsProvider => FFmpegAudioItem.Aac;

        public override void Apply(FFmpegSettings Settings, VideoWriterArgs WriterArgs, FFmpegOutputArgs OutputArgs)
        {
            OutputArgs.AddArg("c:v", _fFmpegCodecName)
                .AddArg("pixel_format", "yuv444p")
                .AddArg("preset", "fast");
        }

        public static NvencVideoCodec CreateH264()
        {
            return new NvencVideoCodec("NVenc: Mp4 (H.264, AAC)", "h264_nvenc", Captura.Loc.LanguageManager.Instance["NvencH264Description"]);
        }

        public static NvencVideoCodec CreateHevc()
        {
            return new NvencVideoCodec("NVenc: Mp4 (HEVC, AAC)", "hevc_nvenc", Captura.Loc.LanguageManager.Instance["NvencHevcDescription"]);
        }
    }
}