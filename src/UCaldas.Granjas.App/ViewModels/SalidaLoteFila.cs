using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Una línea de lote de una venta: cabezas vendidas, peso en báscula y valor pactado.</summary>
public sealed partial class SalidaLoteFila : ObservableObject
{
    public SalidaLoteFila(LoteSeleccionable lote, int cantidad = 0, decimal? pesoKg = null, decimal valorVenta = 0m, string? observaciones = null)
    {
        LoteId = lote.Id;
        Codigo = lote.Codigo;
        Etapa = lote.Etapa;
        Ubicacion = lote.Ubicacion;
        Disponible = lote.CantidadActual;
        ControlNumerico = lote.ControlNumerico;
        ValorUnitario = lote.ValorUnitario;
        _cantidad = lote.ControlNumerico ? cantidad : 0;
        _pesoKg = pesoKg ?? 0m;
        _valorVenta = valorVenta;
        _observaciones = observaciones ?? string.Empty;
    }

    public int LoteId { get; }

    public string Codigo { get; }

    public string Etapa { get; }

    public string Ubicacion { get; }

    /// <summary>Cabezas del lote al abrir la ventana (al editar, ya sin las de este documento).</summary>
    public int Disponible { get; }

    public bool ControlNumerico { get; }

    public decimal ValorUnitario { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValorEnLibros))]
    private int _cantidad;

    [ObservableProperty]
    private decimal _pesoKg;

    [ObservableProperty]
    private decimal _valorVenta;

    [ObservableProperty]
    private string _observaciones;

    /// <summary>Costo en libros: cabezas × valor unitario (el lote entero si no lleva conteo).</summary>
    public decimal ValorEnLibros => ControlNumerico ? Cantidad * ValorUnitario : ValorUnitario;

    public VentaLoteLineaDto ToVentaDto() => new()
    {
        LoteId = LoteId,
        Cantidad = Cantidad,
        PesoKg = PesoKg > 0 ? PesoKg : null,
        ValorVenta = ValorVenta,
        ObservacionesEspecificas = string.IsNullOrWhiteSpace(Observaciones) ? null : Observaciones.Trim(),
    };
}