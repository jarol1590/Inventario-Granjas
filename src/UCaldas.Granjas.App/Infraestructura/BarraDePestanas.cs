namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Una pestaña de la barra superior. <see cref="Contenido"/> es el ViewModel que se muestra al elegirla.</summary>
public sealed record Pestana(string Icono, string Titulo, object? Contenido = null, bool Habilitada = true, string? Ayuda = null);

/// <summary>Barra de botones segmentados (módulos del sistema o pestañas de Configuración).</summary>
public sealed partial class BarraDePestanas : ObservableObject
{
    private bool _revirtiendo;

    public BarraDePestanas(IReadOnlyList<Pestana> pestanas, Pestana seleccionada)
    {
        Pestanas = pestanas;
        _seleccionada = seleccionada;
    }

    public IReadOnlyList<Pestana> Pestanas { get; }

    // Admite nulo: el ListBox deja la selección vacía al refrescarse o al perder un elemento deshabilitado.
    [ObservableProperty]
    private Pestana? _seleccionada;

    /// <summary>Permite a quien posee la barra cancelar un cambio de pestaña (p. ej. hay cambios sin guardar).</summary>
    public Func<Pestana, bool>? PuedeCambiar { get; set; }

    /// <summary>Se dispara solo con cambios aceptados, nunca con las reversiones.</summary>
    public event Action<Pestana>? CambioConfirmado;

    /// <summary>Marca una pestaña sin disparar <see cref="CambioConfirmado"/> ni consultar <see cref="PuedeCambiar"/>.</summary>
    public void SeleccionarSinNotificar(Pestana pestana)
    {
        _revirtiendo = true;
        try
        {
            Seleccionada = pestana;
        }
        finally
        {
            _revirtiendo = false;
        }
    }

    partial void OnSeleccionadaChanged(Pestana? oldValue, Pestana? newValue)
    {
        if (_revirtiendo)
        {
            return;
        }

        if (newValue is null || (PuedeCambiar is not null && !PuedeCambiar(newValue)))
        {
            Revertir(oldValue);
            return;
        }

        CambioConfirmado?.Invoke(newValue);
    }

    // Revertir dentro del mismo binding no refresca el ListBox: se difiere al siguiente turno de la interfaz.
    private void Revertir(Pestana? anterior)
    {
        if (anterior is null)
        {
            return;
        }

        ContextoUi.Diferir(() =>
        {
            _revirtiendo = true;
            try
            {
                Seleccionada = anterior;
            }
            finally
            {
                _revirtiendo = false;
            }
        });
    }
}
