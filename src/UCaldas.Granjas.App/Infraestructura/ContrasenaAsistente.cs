using System.Windows;
using System.Windows.Controls;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>
/// Comportamiento adjunto que enlaza <see cref="PasswordBox.Password"/> con una propiedad del ViewModel
/// (el PasswordBox no es enlazable por diseño). Evita lógica en el code-behind.
/// </summary>
public static class ContrasenaAsistente
{
    // El valor predeterminado es nulo (y no "") para que el primer enlace, que entrega "", dispare la devolución de
    // llamada y se enganche el evento PasswordChanged.
    public static readonly DependencyProperty ContrasenaProperty = DependencyProperty.RegisterAttached(
        "Contrasena", typeof(string), typeof(ContrasenaAsistente),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, AlCambiarContrasena));

    private static readonly DependencyProperty ActualizandoProperty = DependencyProperty.RegisterAttached(
        "Actualizando", typeof(bool), typeof(ContrasenaAsistente), new PropertyMetadata(false));

    public static string? GetContrasena(DependencyObject objeto) => (string?)objeto.GetValue(ContrasenaProperty);

    public static void SetContrasena(DependencyObject objeto, string? valor) => objeto.SetValue(ContrasenaProperty, valor);

    private static void AlCambiarContrasena(DependencyObject objeto, DependencyPropertyChangedEventArgs e)
    {
        if (objeto is not PasswordBox caja)
        {
            return;
        }

        caja.PasswordChanged -= AlEscribir;
        if (!(bool)caja.GetValue(ActualizandoProperty))
        {
            var nueva = e.NewValue as string ?? string.Empty;
            if (caja.Password != nueva)
            {
                caja.Password = nueva;
            }
        }

        caja.PasswordChanged += AlEscribir;
    }

    private static void AlEscribir(object sender, RoutedEventArgs e)
    {
        var caja = (PasswordBox)sender;
        caja.SetValue(ActualizandoProperty, true);
        try
        {
            SetContrasena(caja, caja.Password);
        }
        finally
        {
            caja.SetValue(ActualizandoProperty, false);
        }
    }
}
