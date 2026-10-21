namespace Captura.FFmpeg
{
    class YouTubeLiveVideoCodec : StreamingVideoCodec
    {
        public YouTubeLiveVideoCodec() : base("YouTube Live", Captura.Loc.LanguageManager.Instance["StreamtoYouTubeLiveNotTested"]) { }

        protected override string GetLink(FFmpegSettings Settings)
        {
            return $"rtmp://a.rtmp.youtube.com/live2/{Settings.YouTubeLiveKey}";
        }
    }
}