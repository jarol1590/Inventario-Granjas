using System.Windows;
using System.Windows.Input;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>
/// Comportamiento adjunto: ejecuta un comando del ViewModel cuando el elemento (p. ej. un <c>TextBox</c>) pierde el foco del
/// teclado. Evita escribir código en el code-behind para validar un campo «al salir» de él.
/// </summary>
public static class AlPerderElFoco
{
    public static readonly DependencyProperty ComandoProperty = DependencyProperty.RegisterAttached(
        "Comando", typeof(ICommand), typeof(AlPerderElFoco), new PropertyMetadata(null, AlCambiarComando));

    public static ICommand? GetComando(DependencyObject objeto) => (ICommand?)objeto.GetValue(ComandoProperty);

    public static void SetComando(DependencyObject objeto, ICommand? valor) => objeto.SetValue(ComandoProperty, valor);

    private static void AlCambiarComando(DependencyObject objeto, DependencyPropertyChangedEventArgs e)
    {
        if (objeto is not UIElement elemento)
        {
            return;
        }

        elemento.LostKeyboardFocus -= AlPerderFoco;
        if (e.NewValue is not null)
        {
            elemento.LostKeyboardFocus += AlPerderFoco;
        }
    }

    private static void AlPerderFoco(object sender, KeyboardFocusChangedEventArgs e)
    {
        // El foco sigue dentro del mismo elemento (p. ej. pasa a una parte interna del TextBox): no se «salió» del campo.
        if (sender is not UIElement elemento || e.NewFocus is DependencyObject nuevo && elemento.IsAncestorOf(nuevo))
        {
            return;
        }

        if (GetComando(elemento) is { } comando && comando.CanExecute(null))
        {
            comando.Execute(null);
        }
    }
}
