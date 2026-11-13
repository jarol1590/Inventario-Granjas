using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Una línea de lote de un traslado: cabezas a mover y, si el traslado es parcial, el código del lote nuevo.</summary>
public sealed partial class TrasladoLoteFila : ObservableObject
{
    public TrasladoLoteFila(LoteSeleccionable lote, int cantidad, string? codigoNuevo = null, string? ubicacionDestino = null)
    {
        LoteId = lote.Id;
        Codigo = lote.Codigo;
        Etapa = lote.Etapa;
        Ubicacion = lote.Ubicacion;
        Disponible = lote.CantidadActual;
        ControlNumerico = lote.ControlNumerico;
        ValorUnitario = lote.ValorUnitario;
        _cantidad = lote.ControlNumerico ? cantidad : 0;
        _codigoNuevoLote = codigoNuevo ?? string.Empty;
        _ubicacionDestino = ubicacionDestino ?? string.Empty;
    }

    public int LoteId { get; }

    public string Codigo { get; }

    public string Etapa { get; }

    public string Ubicacion { get; }

    public int Disponible { get; }

    public bool ControlNumerico { get; }

    public decimal ValorUnitario { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsParcial), nameof(Valor))]
    private int _cantidad;

    [ObservableProperty]
    private string _codigoNuevoLote;

    [ObservableProperty]
    private string _ubicacionDestino;

    /// <summary>Se traslada solo una parte: se crea un lote nuevo en la granja destino (exige su código).</summary>
    public bool EsParcial => ControlNumerico && Cantidad < Disponible;

    public decimal Valor => ControlNumerico ? Cantidad * ValorUnitario : ValorUnitario;

    public TrasladoLoteLineaDto ToDto() => new()
    {
        LoteId = LoteId,
        Cantidad = Cantidad,
        CodigoNuevoLote = EsParcial && !string.IsNullOrWhiteSpace(CodigoNuevoLote) ? CodigoNuevoLote.Trim() : null,
        UbicacionDestino = string.IsNullOrWhiteSpace(UbicacionDestino) ? null : UbicacionDestino.Trim(),
    };
}