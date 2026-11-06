using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>true → false y false → true (p. ej. para <c>IsReadOnly = !EsEditable</c>).</summary>
public sealed class InvertirBooleanoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>true → Collapsed, false → Visible.</summary>
public sealed class InvertirVisibilidadConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Nulo (o texto vacío) → Collapsed; con valor → Visible. Con el parámetro "invertir" es al revés.</summary>
public sealed class NuloAVisibilidadConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var tieneValor = value is not null && !(value is string texto && string.IsNullOrWhiteSpace(texto));
        if (parameter is "invertir")
        {
            tieneValor = !tieneValor;
        }

        return tieneValor ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Número con signo → pincel: verde si es positivo, rojo si es negativo, gris si es cero.</summary>
public sealed class SignoABrochaConverter : IValueConverter
{
    private static readonly Brush Positivo = Congelar("#2E7D32");
    private static readonly Brush Negativo = Congelar("#B3261E");
    private static readonly Brush Neutro = Congelar("#5B6577");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var numero = value is IConvertible convertible ? convertible.ToDecimal(CultureInfo.InvariantCulture) : 0m;
        return numero > 0 ? Positivo : numero < 0 ? Negativo : Neutro;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Congelar(string color)
    {
        var brocha = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brocha.Freeze();
        return brocha;
    }
}
