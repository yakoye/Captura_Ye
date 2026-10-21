using System;

namespace Captura.FFmpeg
{
    public class FFmpegNotFoundException : Exception
    {
        public override string Message => Captura.Loc.LanguageManager.Instance["FFmpegcouldnotbefoundPleasedownloadusingtheinbuiltdownloader"];
    }
}