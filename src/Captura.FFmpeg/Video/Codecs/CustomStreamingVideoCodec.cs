namespace Captura.FFmpeg
{
    class CustomStreamingVideoCodec : StreamingVideoCodec
    {
        public CustomStreamingVideoCodec() : base(Captura.Loc.LanguageManager.Instance["Custom"], Captura.Loc.LanguageManager.Instance["Streamtocustomservice"]) { }

        protected override string GetLink(FFmpegSettings Settings)
        {
            return Settings.CustomStreamingUrl;
        }
    }
}