using System.Collections.Generic;
using System.IO;
using Captura.Loc;
using Captura.Models;

namespace Captura.ViewModels
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class FileNameFormatViewModel : ViewModelBase
    {
        public FileNameFormatViewModel(Settings Settings, ILocalizationProvider Loc)
            : base(Settings, Loc) { }

        public IEnumerable<FileNameFormatGroup> FormatGroups { get; } = new []
        {
            new FileNameFormatGroup(Captura.Loc.LanguageManager.Instance["Time"], new []
            {
                new FileNameFormatItem("%HH%", Captura.Loc.LanguageManager.Instance["Hours24hr20"]),
                new FileNameFormatItem("%hh%", Captura.Loc.LanguageManager.Instance["Hours12hr08"]),
                new FileNameFormatItem("%mm%", Captura.Loc.LanguageManager.Instance["Minutes58"]),
                new FileNameFormatItem("%ss%", Captura.Loc.LanguageManager.Instance["Seconds54"]),
                new FileNameFormatItem("%tt%", Captura.Loc.LanguageManager.Instance["AMPM"]),
                new FileNameFormatItem("%zzz%", Captura.Loc.LanguageManager.Instance["TimeZone0530"])
            }),
            new FileNameFormatGroup(Captura.Loc.LanguageManager.Instance["Year"], new []
            {
                new FileNameFormatItem("%yyyy%", Captura.Loc.LanguageManager.Instance["Year2018"]),
                new FileNameFormatItem("%yy%", Captura.Loc.LanguageManager.Instance["Year18"])
            }),
            new FileNameFormatGroup(Captura.Loc.LanguageManager.Instance["Month"], new []
            {
                new FileNameFormatItem("%MMMM%", Captura.Loc.LanguageManager.Instance["MonthSeptember"]),
                new FileNameFormatItem("%MMM%", Captura.Loc.LanguageManager.Instance["MonthSep"]),
                new FileNameFormatItem("%MM%", Captura.Loc.LanguageManager.Instance["Month09"])
            }),
            new FileNameFormatGroup(Captura.Loc.LanguageManager.Instance["Date"], new []
            {
                new FileNameFormatItem("%dd%", Captura.Loc.LanguageManager.Instance["Date22"]),
                new FileNameFormatItem("%ddd%", Captura.Loc.LanguageManager.Instance["DayTue"]),
                new FileNameFormatItem("%dddd%", Captura.Loc.LanguageManager.Instance["DayTuesday"])
            }),
            new FileNameFormatGroup(Captura.Loc.LanguageManager.Instance["Other"], new []
            {
                new FileNameFormatItem("%computer%", Captura.Loc.LanguageManager.Instance["CompanyName"]),
                new FileNameFormatItem("%user%", Captura.Loc.LanguageManager.Instance["UserName"])
            }),
        };

        public string FilenameFormat
        {
            get => Settings.FilenameFormat;
            set
            {
                var invalidChars = Path.GetInvalidFileNameChars();

                foreach (var invalidChar in invalidChars)
                {
                    if (invalidChar == '/' || invalidChar == '\\')
                        continue;

                    value = value.Replace(invalidChar.ToString(), "");
                }

                Settings.FilenameFormat = value;

                OnPropertyChanged();

                RaisePropertyChanged(nameof(FilenamePreview));
            }
        }

        public string FilenamePreview => Settings.GetFileName(".mp4");
    }
}
