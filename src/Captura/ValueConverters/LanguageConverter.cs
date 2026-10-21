using System;
using System.Windows.Data;
using System.Globalization;

namespace Captura
{
    public class LanguageConverter : IValueConverter
    {
        public object Convert(object Value, Type TargetType, object Parameter, CultureInfo Culture)
        {
            if (Value is CultureInfo c)
            {
                switch (c.Name)
                {
                    case "en":
                        return "English";

                    case "zh-CN":
                        return "简体中文";

                    case "zh-TW":
                        return "繁體中文";

                    default:
                        return $"{c.DisplayName} ({c.NativeName})";
                }
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object Value, Type TargetType, object Parameter, CultureInfo Culture)
        {
            return Binding.DoNothing;
        }
    }
}
