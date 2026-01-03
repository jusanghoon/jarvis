using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace javis.Converters;

public sealed class DateHasTodoToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 2) return Visibility.Collapsed;

        if (values[0] is not DateTime date) return Visibility.Collapsed;
        if (values[1] is not IEnumerable<DateTime> dates) return Visibility.Collapsed;

        var d = date.Date;
        return dates.Any(x => x.Date == d) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
