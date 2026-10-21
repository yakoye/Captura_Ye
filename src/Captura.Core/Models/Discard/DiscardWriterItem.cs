namespace Captura.Video
{
    public class DiscardWriterItem : IVideoWriterItem
    {
        readonly IPreviewWindow _previewWindow;

        public DiscardWriterItem(IPreviewWindow PreviewWindow)
        {
            _previewWindow = PreviewWindow;
        }

        public string Extension { get; } = "";

        public IVideoFileWriter GetVideoFileWriter(VideoWriterArgs Args)
        {
            _previewWindow.Show();

            return new DiscardWriter();
        }

        public override string ToString() => Captura.Loc.LanguageManager.Instance["Preview"];

        public string Description => Captura.Loc.LanguageManager.Instance["Fortestingpurposes"];
    }
}