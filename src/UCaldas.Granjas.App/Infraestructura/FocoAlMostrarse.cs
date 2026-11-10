using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>
/// Comportamiento adjunto para los asistentes de varios pasos: cuando el elemento pasa a ser visible (un panel que aparece
/// al cambiar de paso) lleva el foco del teclado a <see cref="DestinoProperty"/> o, si no se indica, al primer control que
/// pueda recibirlo. Sin esto, al ocultarse el control que tenía el foco este cae en un contenedor: el usuario de teclado queda
/// sin saber dónde está y WPF dibuja un rectángulo punteado alrededor de toda la tarjeta.
/// </summary>
public static class FocoAlMostrarse
{
    public static readonly DependencyProperty ActivoProperty = DependencyProperty.RegisterAttached(
        "Activo", typeof(bool), typeof(FocoAlMostrarse), new PropertyMetadata(false, AlCambiarActivo));

    /// <summary>Control que recibe el foco al mostrarse el elemento (opcional).</summary>
    public static readonly DependencyProperty DestinoProperty = DependencyProperty.RegisterAttached(
        "Destino", typeof(UIElement), typeof(FocoAlMostrarse), new PropertyMetadata(null));

    public static bool GetActivo(DependencyObject objeto) => (bool)objeto.GetValue(ActivoProperty);

    public static void SetActivo(DependencyObject objeto, bool valor) => objeto.SetValue(ActivoProperty, valor);

    public static UIElement? GetDestino(DependencyObject objeto) => (UIElement?)objeto.GetValue(DestinoProperty);

    public static void SetDestino(DependencyObject objeto, UIElement? valor) => objeto.SetValue(DestinoProperty, valor);

    private static void AlCambiarActivo(DependencyObject objeto, DependencyPropertyChangedEventArgs e)
    {
        if (objeto is not UIElement elemento)
        {
            return;
        }

        elemento.IsVisibleChanged -= AlCambiarVisibilidad;
        if (e.NewValue is true)
        {
            elemento.IsVisibleChanged += AlCambiarVisibilidad;
        }
    }

    private static void AlCambiarVisibilidad(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not UIElement elemento)
        {
            return;
        }

        // Se difiere: mientras cambia la visibilidad el diseño todavía no se recalculó y el destino aún no puede recibir el foco.
        elemento.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Enfocar(elemento)));
    }

    private static void Enfocar(UIElement elemento)
    {
        if (!elemento.IsVisible)
        {
            return;
        }

        if (GetDestino(elemento) is { IsVisible: true } destino && destino.Focus())
        {
            return;
        }

        if (elemento.Focusable && elemento.Focus())
        {
            return;
        }

        elemento.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
}
