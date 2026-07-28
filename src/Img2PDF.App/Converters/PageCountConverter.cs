using Microsoft.UI.Xaml.Data;

namespace Img2PDF.App.Converters;

public sealed class PageCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        int count = value is int i ? i : 0;
        return count == 1 ? "1 page" : $"{count} pages";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
