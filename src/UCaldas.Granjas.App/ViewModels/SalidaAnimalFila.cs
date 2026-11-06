using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Un animal de un documento de salida (venta o baja) con su precio de venta y observaciones específicas.</summary>
public sealed partial class SalidaAnimalFila : ObservableObject
{
    public SalidaAnimalFila(AnimalSeleccionable animal, decimal precioVenta = 0m, string? observaciones = null, decimal? pesoKg = null)
    {
        Animal = animal;
        _precioVenta = precioVenta;
        _observaciones = observaciones ?? string.Empty;
        PesoKg = pesoKg;
    }

    public AnimalSeleccionable Animal { get; }

    public decimal? PesoKg { get; }

    public int AnimalId => Animal.Id;

    public string ChapetaAmarilla => Animal.ChapetaAmarilla;

    /// <summary>Precio real pactado (solo ventas).</summary>
    [ObservableProperty]
    private decimal _precioVenta;

    [ObservableProperty]
    private string _observaciones;

    public VentaAnimalDto ToVentaDto() => new()
    {
        AnimalId = Animal.Id,
        PrecioVenta = PrecioVenta,
        PesoKg = PesoKg,
        ObservacionesEspecificas = Vacio(Observaciones),
    };

    public BajaAnimalDto ToBajaDto() => new()
    {
        AnimalId = Animal.Id,
        ObservacionesEspecificas = Vacio(Observaciones),
    };

    private static string? Vacio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
