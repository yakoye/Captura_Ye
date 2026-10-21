namespace Captura.FFmpeg
{
    class TwitchVideoCodec : StreamingVideoCodec
    {
        public TwitchVideoCodec() : base("Twitch", Captura.Loc.LanguageManager.Instance["StreamtoTwitch"]) { }

        protected override string GetLink(FFmpegSettings Settings)
        {
            return $"rtmp://live.twitch.tv/app/{Settings.TwitchKey}";
        }
    }
}