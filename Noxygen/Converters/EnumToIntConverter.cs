using System;
using Microsoft.UI.Xaml.Data;

namespace Noxygen.Converters;

public class EnumToIntConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is Enum ? (int)value : 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is int intVal ? Enum.ToObject(targetType, intVal) : 0;
    }
}