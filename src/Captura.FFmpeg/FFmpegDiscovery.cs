using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Captura.FFmpeg
{
    public static class FFmpegDiscovery
    {
        public static string FindInDirectories(IEnumerable<string> Directories)
        {
            foreach (var directory in Directories.Where(M => !string.IsNullOrWhiteSpace(M)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var folder = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
                    var path = Path.GetFullPath(Path.Combine(folder, "ffmpeg.exe"));
                    if (File.Exists(path)) return path;
                }
                catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
                {
                    // 不可访问的目录不阻止继续查找其他位置。
                }
            }
            return null;
        }

        public static IEnumerable<string> GetSearchDirectories(string AppDirectory, string PathValue = null)
        {
            foreach (var folder in (PathValue ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                yield return folder;
            yield return AppDirectory;
            yield return Path.Combine(AppDirectory, "Codecs");
            yield return Path.Combine(AppDirectory, "ffmpeg", "bin");
            yield return Path.Combine(AppDirectory, "lib");
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(local, "Captura");
            yield return Path.Combine(local, "Programs", "ffmpeg", "bin");
            yield return Path.Combine(local, "Microsoft", "WinGet", "Links");
            yield return Path.Combine(profile, "scoop", "apps", "ffmpeg", "current", "bin");
            yield return Path.Combine(profile, "scoop", "apps", "ffmpeg-essentials", "current", "bin");
            yield return @"C:\ffmpeg\bin";
            yield return @"C:\tools\ffmpeg\bin";
            yield return Path.Combine(Environment.GetEnvironmentVariable("ChocolateyInstall") ?? @"C:\ProgramData\chocolatey", "bin");
            foreach (var root in new[]
            {
                Path.Combine(local, "Microsoft", "WinGet", "Packages"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            })
            {
                foreach (var package in SafeDirectories(root).Where(M => Path.GetFileName(M).IndexOf("ffmpeg", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    yield return package;
                    yield return Path.Combine(package, "bin");
                    foreach (var child in SafeDirectories(package))
                    {
                        yield return child;
                        yield return Path.Combine(child, "bin");
                    }
                }
            }
        }

        static string[] SafeDirectories(string Path)
        {
            try { return Directory.Exists(Path) ? Directory.GetDirectories(Path) : Array.Empty<string>(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { return Array.Empty<string>(); }
        }
    }
}
