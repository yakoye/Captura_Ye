using System;
using System.Globalization;
using System.Windows.Data;
using Captura.Loc;

namespace Captura
{
    // 只转换显示文字，保留枚举值和编码参数的原始值。
    public class LocalizedTextConverter : IValueConverter
    {
        public object Convert(object Value, Type TargetType, object Parameter, CultureInfo Culture)
            => Value == null ? "" : LanguageManager.Instance[Value.ToString()];

        public object ConvertBack(object Value, Type TargetType, object Parameter, CultureInfo Culture)
            => Binding.DoNothing;
    }
}
