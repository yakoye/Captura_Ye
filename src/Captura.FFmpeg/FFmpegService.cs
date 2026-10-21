using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;

namespace Captura.FFmpeg
{
    public static class FFmpegService
    {
        const string FFmpegExeName = "ffmpeg.exe";

        static FFmpegSettings GetSettings() => ServiceProvider.Get<FFmpegSettings>();

        static readonly object DiscoveryLock = new object();
        static string _discoveredPath;
        static System.DateTime _lastDiscovery;

        public static string DetectExecutable(bool Force = false)
        {
            var configured = Path.Combine(GetSettings().GetFolderPath(), FFmpegExeName);
            if (File.Exists(configured)) return Path.GetFullPath(configured);
            lock (DiscoveryLock)
            {
                if (!Force && _discoveredPath != null && File.Exists(_discoveredPath)) return _discoveredPath;
                if (!Force && System.DateTime.UtcNow - _lastDiscovery < System.TimeSpan.FromSeconds(10)) return null;
                _discoveredPath = FFmpegDiscovery.FindInDirectories(FFmpegDiscovery.GetSearchDirectories(ServiceProvider.AppDir));
                _lastDiscovery = System.DateTime.UtcNow;
                return _discoveredPath;
            }
        }

        public static bool FFmpegExists => DetectExecutable() != null;

        public static string FFmpegExePath => DetectExecutable() ?? FFmpegExeName;

        public static Process StartFFmpeg(string Arguments, string FileName, out IFFmpegLogEntry FFmpegLog)
        {
            var process = new Process
            {
                StartInfo =
                {
                    FileName = FFmpegExePath,
                    Arguments = Arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                },
                EnableRaisingEvents = true
            };
            
            var log = ServiceProvider.Get<IFFmpegLogRepository>();

            var logItem = log.CreateNew(Path.GetFileName(FileName), Arguments);
            FFmpegLog = logItem;
                        
            process.ErrorDataReceived += (S, E) => logItem.Write(E.Data);

            process.Start();

            process.BeginErrorReadLine();
            
            return process;
        }

        public static bool WaitForConnection(this NamedPipeServerStream ServerStream, int Timeout)
        {
            var asyncResult = ServerStream.BeginWaitForConnection(Ar => {}, null);

            if (asyncResult.AsyncWaitHandle.WaitOne(Timeout))
            {
                ServerStream.EndWaitForConnection(asyncResult);

                return ServerStream.IsConnected;
            }

            return false;
        }
    }
}
