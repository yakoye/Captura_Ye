using System;
using System.IO;
using System.Linq;
using Captura.FFmpeg;
using Captura.Models;
using Xunit;

namespace Captura.Tests
{
    [Collection(nameof(Tests))]
    public class ReleaseBehaviorTests
    {
        [Fact]
        public void DiscoveryKeepsDirectoryPriority()
        {
            var root = CreateRoot();
            try
            {
                var first = CreateCandidate(root, "manual");
                var second = CreateCandidate(root, "path");
                Assert.Equal(Path.Combine(first, "ffmpeg.exe"), FFmpegDiscovery.FindInDirectories(new[] { first, second }));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void DiscoverySkipsMissingAndMalformedPaths()
        {
            var root = CreateRoot();
            try
            {
                var candidate = CreateCandidate(root, "有效目录");
                Assert.Equal(Path.Combine(candidate, "ffmpeg.exe"), FFmpegDiscovery.FindInDirectories(new[] { null, "", "\0", Path.Combine(root, "missing"), candidate }));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void DiscoveryAcceptsQuotedFoldersWithSpaces()
        {
            var root = CreateRoot();
            try
            {
                var candidate = CreateCandidate(root, "folder with spaces");
                Assert.Equal(Path.Combine(candidate, "ffmpeg.exe"), FFmpegDiscovery.FindInDirectories(new[] { "\"" + candidate + "\"" }));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void DiscoveryReturnsNullWhenNothingExists()
        {
            var root = CreateRoot();
            try { Assert.Null(FFmpegDiscovery.FindInDirectories(new[] { root, root })); }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void DiscoveryChecksPathBeforeApplicationCodecs()
        {
            var root = CreateRoot();
            try
            {
                CreateCandidate(root, "Codecs");
                var candidate = CreateCandidate(root, "environment-path");
                Assert.Equal(Path.Combine(candidate, "ffmpeg.exe"), FFmpegDiscovery.FindInDirectories(FFmpegDiscovery.GetSearchDirectories(root, candidate)));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void SaveNotificationKeepsFullPathAndOpenActions()
        {
            var path = Path.Combine(Path.GetTempPath(), "录制目录", "视频.mp4");
            var recent = new FileRecentItem(path, RecentFileType.Video);
            var notification = new FileSaveNotification(recent);
            notification.Saved();
            Assert.Equal(path, notification.SecondaryText);
            Assert.True(notification.Finished);
            Assert.Equal(3, notification.Actions.Count());
        }

        [Fact]
        public void SaveNotificationUsesConvertedFileLocation()
        {
            var original = Path.Combine(Path.GetTempPath(), "source.mp4");
            var converted = Path.Combine(Path.GetTempPath(), "转换后", "result.webm");
            var notification = new FileSaveNotification(new FileRecentItem(original, RecentFileType.Video));
            notification.Converted(converted);
            Assert.Equal(converted, notification.SecondaryText);
        }

        [Fact]
        public void SaveNotificationNormalizesDateSubfolderSeparators()
        {
            var root = Path.GetTempPath();
            var notification = new FileSaveNotification(new FileRecentItem(root + "录制目录/date/video.mp4", RecentFileType.Video));
            Assert.Equal(Path.Combine(root, "录制目录", "date", "video.mp4"), notification.SecondaryText);
        }

        static string CreateRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "captura-release-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        static string CreateCandidate(string Root, string Name)
        {
            var directory = Path.Combine(Root, Name);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "ffmpeg.exe"), Array.Empty<byte>());
            return directory;
        }
    }
}
