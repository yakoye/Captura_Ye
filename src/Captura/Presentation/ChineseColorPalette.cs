using System.Collections.ObjectModel;
using System.Windows.Media;
using Xceed.Wpf.Toolkit;

namespace Captura
{
    public static class ChineseColorPalette
    {
        public static ObservableCollection<ColorItem> Colors { get; } = new ObservableCollection<ColorItem>
        {
            new ColorItem(System.Windows.Media.Colors.Transparent, "透明"),
            new ColorItem(System.Windows.Media.Colors.Black, "黑色"),
            new ColorItem(System.Windows.Media.Colors.White, "白色"),
            new ColorItem(System.Windows.Media.Colors.Gray, "灰色"),
            new ColorItem(System.Windows.Media.Colors.Red, "红色"),
            new ColorItem(System.Windows.Media.Colors.Orange, "橙色"),
            new ColorItem(System.Windows.Media.Colors.Yellow, "黄色"),
            new ColorItem(System.Windows.Media.Colors.Green, "绿色"),
            new ColorItem(System.Windows.Media.Colors.Cyan, "青色"),
            new ColorItem(System.Windows.Media.Colors.Blue, "蓝色"),
            new ColorItem(System.Windows.Media.Colors.Purple, "紫色"),
            new ColorItem(System.Windows.Media.Colors.Pink, "粉色"),
            new ColorItem(System.Windows.Media.Colors.Brown, "棕色"),
            new ColorItem(System.Windows.Media.Colors.Gold, "金色"),
            new ColorItem(System.Windows.Media.Colors.Silver, "银色")
        };
    }
}
