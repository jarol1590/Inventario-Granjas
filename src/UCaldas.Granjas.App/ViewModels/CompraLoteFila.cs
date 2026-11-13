using UCaldas.Granjas.Services.Lotes;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>La fila de lote de una compra de lote (la compra de un lote es un solo lote).</summary>
public sealed partial class CompraLoteFila : ObservableObject
{
    public CompraLoteFila(LoteDetalle detalle)
    {
        var lote = detalle.Lote;
        LoteId = lote.Id;
        Codigo = lote.CodigoLote;
        Etapa = detalle.EtapaNombre;
        ControlNumerico = lote.ControlNumericoPoblacion;
        _ubicacion = lote.Ubicacion;
        _lineaRaza = lote.LineaRaza;
        _cantidad = lote.CantidadInicial;
        _precioUnitario = lote.ValorCompraUnitario;
    }

    public int LoteId { get; }

    public string Codigo { get; }

    public string Etapa { get; }

    public bool ControlNumerico { get; }

    /// <summary>La cantidad inicial solo existe en los lotes con control numérico de población.</summary>
    public bool PuedeCorregirCantidad => ControlNumerico;

    [ObservableProperty]
    private string _ubicacion;

    [ObservableProperty]
    private string _lineaRaza;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Total))]
    private int _cantidad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Total))]
    private decimal _precioUnitario;

    /// <summary>cantidad × precio; en un lote sin conteo, el valor del lote entero.</summary>
    public decimal Total => ControlNumerico ? Cantidad * PrecioUnitario : PrecioUnitario;
}