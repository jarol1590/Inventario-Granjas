using System.Windows;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>
/// Permite enlazar propiedades de los DataGrid que no están en el árbol visual (como <c>DataGridColumn.Visibility</c>)
/// con el ViewModel: se declara como recurso con <c>Data="{Binding}"</c> y se usa con <c>Source={StaticResource ...}</c>.
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
