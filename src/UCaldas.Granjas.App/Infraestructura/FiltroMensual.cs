namespace UCaldas.Granjas.App.Infraestructura;

public sealed record OpcionMes(int Numero, string Nombre);

/// <summary>Rango "Mes desde … hasta …" de los listados históricos (Compras, Salidas, Traslados).</summary>
public sealed partial class FiltroMensual : ObservableObject
{
    public static IReadOnlyList<OpcionMes> Meses { get; } = Enumerable.Range(1, 12)
        .Select(m =>
        {
            var nombre = Formato.Cultura.DateTimeFormat.GetMonthName(m);
            return new OpcionMes(m, char.ToUpper(nombre[0], Formato.Cultura) + nombre[1..]);
        })
        .ToList();

    private bool _inicializando = true;

    /// <summary>Por omisión va de enero del año actual al mes actual.</summary>
    public FiltroMensual(DateTime hoy)
    {
        Anios = Enumerable.Range(hoy.Year - 6, 8).ToList();
        _mesDesde = Meses[0];
        _anioDesde = hoy.Year;
        _mesHasta = Meses[hoy.Month - 1];
        _anioHasta = hoy.Year;
        _inicializando = false;
    }

    public IReadOnlyList<int> Anios { get; }

    [ObservableProperty]
    private OpcionMes _mesDesde;

    [ObservableProperty]
    private int _anioDesde;

    [ObservableProperty]
    private OpcionMes _mesHasta;

    [ObservableProperty]
    private int _anioHasta;

    /// <summary>Primer día del mes inicial.</summary>
    public DateTime Desde => new(AnioDesde, MesDesde.Numero, 1);

    /// <summary>Último día del mes final.</summary>
    public DateTime Hasta => new DateTime(AnioHasta, MesHasta.Numero, 1).AddMonths(1).AddDays(-1);

    /// <summary>Se dispara cuando el usuario cambia cualquiera de los cuatro combos.</summary>
    public event Action? Cambio;

    /// <summary>Mensaje de error si el rango es incoherente; nulo si es válido.</summary>
    public string? Validar() => Desde > Hasta ? "El mes inicial no puede ser posterior al mes final." : null;

    partial void OnMesDesdeChanged(OpcionMes value) => Notificar();

    partial void OnAnioDesdeChanged(int value) => Notificar();

    partial void OnMesHastaChanged(OpcionMes value) => Notificar();

    partial void OnAnioHastaChanged(int value) => Notificar();

    private void Notificar()
    {
        if (!_inicializando)
        {
            Cambio?.Invoke();
        }
    }
}
