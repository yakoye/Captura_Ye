using System.Collections;
using System.Collections.Generic;

namespace Captura.Video
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class DiscardWriterProvider : IVideoWriterProvider
    {
        readonly IPreviewWindow _previewWindow;

        public DiscardWriterProvider(IPreviewWindow PreviewWindow)
        {
            _previewWindow = PreviewWindow;
        }

        public IEnumerator<IVideoWriterItem> GetEnumerator()
        {
            yield return new DiscardWriterItem(_previewWindow);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public string Name { get; } = Captura.Loc.LanguageManager.Instance["PreviewOnly"];

        public string Description => Captura.Loc.LanguageManager.Instance["Fortestingpurposes"];

        public override string ToString() => Name;

        public IVideoWriterItem ParseCli(string Cli) => null;
    }
}