using System;

namespace Captura.FFmpeg
{
    public class FFmpegException : Exception
    {
        public FFmpegException(int ExitCode, Exception InnerException = null)
            : base($"FFmpeg 执行失败，退出代码：{ExitCode}。\n请查看 FFmpeg 日志了解详情。", InnerException) { }
    }
}