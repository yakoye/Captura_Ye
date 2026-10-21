namespace Captura.Video
{
    public class ImageFolderWriterItem : IVideoWriterItem
    {
        public string Extension { get; } = "";

        public IVideoFileWriter GetVideoFileWriter(VideoWriterArgs Args)
        {
            return new ImageFolderWriter(Args.FileName);
        }

        public override string ToString() => Captura.Loc.LanguageManager.Instance["Images"];

        public string Description => Captura.Loc.LanguageManager.Instance["Writesimagestoafolder"];
    }
}