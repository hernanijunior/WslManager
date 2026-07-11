using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WslManager.Views;

/// <summary>Inverte um booleano (true ↔ false).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>true quando o valor não é nulo nem string vazia (para IsOpen de InfoBar).</summary>
public sealed class NotNullToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && value is not "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
