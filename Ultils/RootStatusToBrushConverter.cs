using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LightGateway.Ultils
{
    /// <summary>
    /// 将“是否根节点”的字符串值转换为醒目颜色。
    /// </summary>
    [ValueConversion(typeof(string), typeof(Brush))]
    public class RootStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value?.ToString()?.Trim().ToLowerInvariant() ?? "";
            return s switch
            {
                "1" or "true" or "是" or "yes" => new SolidColorBrush(Colors.ForestGreen),
                "0" or "false" or "否" or "no" => new SolidColorBrush(Colors.Crimson),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("RootStatusToBrushConverter 仅支持单向绑定。");
        }
    }
}
