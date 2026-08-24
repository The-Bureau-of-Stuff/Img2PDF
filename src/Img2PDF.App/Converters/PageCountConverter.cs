using Microsoft.UI.Xaml.Data;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Img2PDF.App.Converters;

public sealed class PageCountConverter : IValueConverter
{
    private static readonly ResourceLoader ResourceLoader = new();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        int count = value is int i ? i : 0;
        return count == 1
            ? ResourceLoader.GetString("PageCountSingular")
            : string.Format(ResourceLoader.GetString("PageCountPluralFormat"), count);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
