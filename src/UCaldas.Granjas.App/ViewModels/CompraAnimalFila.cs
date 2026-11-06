using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Una fila editable de la grilla de animales de una compra.</summary>
public sealed partial class CompraAnimalFila : ObservableObject
{
    public CompraAnimalFila(CompraAnimalDto? dto = null, int orientacionPredeterminada = 0, DateTime? fechaNacimientoPredeterminada = null)
    {
        AnimalId = dto?.AnimalId;
        _chapetaAmarilla = dto?.ChapetaAmarilla ?? string.Empty;
        _chapetaRoja = dto?.ChapetaRoja ?? string.Empty;
        _nombre = dto?.Nombre ?? string.Empty;
        _sexo = string.IsNullOrEmpty(dto?.Sexo) ? Domain.Constants.Sexo.Hembra : dto.Sexo;
        _fechaNacimiento = dto is null ? fechaNacimientoPredeterminada : dto.FechaNacimiento;
        _raza = dto?.Raza ?? string.Empty;
        _orientacionId = dto?.OrientacionId ?? orientacionPredeterminada;
        _numeroPartos = dto?.NumeroPartos ?? 0;
        _pesoKg = dto?.PesoKg ?? 0m;
        _precioFactura = dto?.PrecioFactura ?? 0m;
        _observaciones = dto?.Observaciones ?? string.Empty;
    }

    /// <summary>Solo al editar: animal ya registrado por esta compra.</summary>
    public int? AnimalId { get; }

    [ObservableProperty]
    private string _chapetaAmarilla;

    [ObservableProperty]
    private string _chapetaRoja;

    [ObservableProperty]
    private string _nombre;

    [ObservableProperty]
    private string _sexo;

    [ObservableProperty]
    private DateTime? _fechaNacimiento;

    [ObservableProperty]
    private string _raza;

    [ObservableProperty]
    private int _orientacionId;

    [ObservableProperty]
    private int _numeroPartos;

    [ObservableProperty]
    private decimal _pesoKg;

    [ObservableProperty]
    private decimal _precioFactura;

    [ObservableProperty]
    private string _observaciones;

    public CompraAnimalDto ToDto() => new()
    {
        AnimalId = AnimalId,
        ChapetaAmarilla = ChapetaAmarilla,
        ChapetaRoja = Vacio(ChapetaRoja),
        Nombre = Vacio(Nombre),
        Sexo = Sexo,
        FechaNacimiento = FechaNacimiento ?? default,
        Raza = Raza,
        OrientacionId = OrientacionId,
        NumeroPartos = Sexo == Domain.Constants.Sexo.Hembra ? NumeroPartos : 0,
        PesoKg = PesoKg,
        PrecioFactura = PrecioFactura,
        Observaciones = Vacio(Observaciones),
    };

    private static string? Vacio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
