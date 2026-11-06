using UCaldas.Granjas.App.Infraestructura;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Detalle de un animal dentro de una salida: sus datos, el precio contable y, si el documento se puede modificar,
/// el precio de venta y las observaciones específicas del animal.
/// </summary>
public sealed partial class SalidaAnimalViewModel : DialogViewModelBase
{
    private readonly SalidaAnimalFila _fila;

    public SalidaAnimalViewModel(
        SalidaAnimalFila fila,
        string claseSalida,
        string numeroOficio,
        string reciboCaja,
        string destino,
        DateTime? fechaSalida,
        bool esVenta,
        bool esEditable,
        IDialogService dialogos)
        : base(dialogos)
    {
        _fila = fila;
        ClaseSalida = claseSalida;
        NumeroOficio = numeroOficio;
        ReciboCaja = reciboCaja;
        Destino = destino;
        FechaSalida = fechaSalida;
        EsVenta = esVenta;
        EsEditable = esEditable;
        _precioVenta = fila.PrecioVenta;
        _observaciones = fila.Observaciones;
    }

    public override string Titulo => $"Detalle del animal {_fila.ChapetaAmarilla}";

    public override double Ancho => 760;

    public string ClaseSalida { get; }

    public string NumeroOficio { get; }

    public string ReciboCaja { get; }

    public string Destino { get; }

    public DateTime? FechaSalida { get; }

    public bool EsVenta { get; }

    public bool EsEditable { get; }

    public Services.Consultas.AnimalSeleccionable Animal => _fila.Animal;

    public string PrecioAnimalTexto => Formato.Dinero(_fila.Animal.ValorContable);

    [ObservableProperty]
    private decimal _precioVenta;

    [ObservableProperty]
    private string _observaciones;

    [RelayCommand]
    private void Guardar()
    {
        _fila.PrecioVenta = PrecioVenta;
        _fila.Observaciones = Observaciones;
        Cerrar(true);
    }
}
