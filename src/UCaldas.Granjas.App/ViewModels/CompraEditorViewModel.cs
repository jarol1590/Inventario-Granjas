using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Compra de animales amparada por una factura: crear, consultar (detalle), editar y anular. El valor de la factura de cada
/// animal queda como su costo de origen, que no se puede modificar después de la compra.
/// </summary>
public sealed partial class CompraEditorViewModel : DialogViewModelBase
{
    private readonly IKardexService _kardex;
    private readonly IConsultasService _consultas;
    private readonly ICatalogoService _catalogo;
    private readonly ContextoGlobal _contexto;
    private int? _remisionId;
    private int _orientacionPredeterminada;

    public CompraEditorViewModel(
        IKardexService kardex,
        IConsultasService consultas,
        ICatalogoService catalogo,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _kardex = kardex;
        _consultas = consultas;
        _catalogo = catalogo;
        _contexto = contexto;
        _fechaCompra = reloj.Hoy;
        Animales.CollectionChanged += AlCambiarAnimales;
    }

    public override string Titulo => Modo switch
    {
        ModoDocumento.Nuevo => "Nueva compra",
        ModoDocumento.Detalle => $"Detalle de la compra {NumeroFactura}",
        _ => $"Editar compra {NumeroFactura}",
    };

    public override double Ancho => 1200;

    public override double Alto => 760;

    public IReadOnlyList<string> Sexos { get; } = [Domain.Constants.Sexo.Hembra, Domain.Constants.Sexo.Macho];

    public ObservableCollection<OpcionFiltro> Granjas { get; } = [];

    public ObservableCollection<OpcionFiltro> Orientaciones { get; } = [];

    public ObservableCollection<string> Razas { get; } = [];

    public ObservableCollection<CompraAnimalFila> Animales { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo), nameof(EsLectura), nameof(EsEditable), nameof(PuedeEditarODesechar))]
    private ModoDocumento _modo = ModoDocumento.Nuevo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditable), nameof(PuedeEditarODesechar))]
    private bool _anulada;

    [ObservableProperty]
    private string _numeroOrden = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private string _numeroFactura = string.Empty;

    [ObservableProperty]
    private DateTime? _fechaCompra;

    [ObservableProperty]
    private OpcionFiltro? _granjaSeleccionada;

    [ObservableProperty]
    private string _proveedor = string.Empty;

    [ObservableProperty]
    private string _nit = string.Empty;

    [ObservableProperty]
    private string _observaciones = string.Empty;

    [ObservableProperty]
    private CompraAnimalFila? _filaSeleccionada;

    public bool EsLectura => Modo == ModoDocumento.Detalle;

    /// <summary>Se pueden modificar los datos (documento nuevo o en edición, y no anulado).</summary>
    public bool EsEditable => Modo != ModoDocumento.Detalle && !Anulada;

    public bool PuedeEditarODesechar => EsLectura && !Anulada;

    public decimal Total => Animales.Sum(a => a.PrecioFactura);

    public string TotalTexto => Formato.Dinero(Total);

    public string CantidadTexto => $"Total animales: {Animales.Count}";

    public async Task<bool> InicializarNuevaAsync()
    {
        _remisionId = null;
        return await EjecutarAsync(async () =>
        {
            await CargarCatalogosAsync();
            GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == _contexto.GranjaId) ?? Granjas.FirstOrDefault();
            Modo = ModoDocumento.Nuevo;
            AgregarAnimal();
        }, "preparar el formulario de compra");
    }

    public async Task<bool> InicializarExistenteAsync(int remisionId)
    {
        _remisionId = remisionId;
        return await EjecutarAsync(async () =>
        {
            await CargarCatalogosAsync();
            var editable = await _consultas.ObtenerCompraAsync(remisionId);
            var dto = editable.Dto;

            NumeroFactura = dto.NumeroFactura;
            NumeroOrden = dto.OrdenCompra ?? string.Empty;
            FechaCompra = dto.FechaCompra;
            GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == dto.GranjaDestinoId);
            Proveedor = dto.Proveedor;
            Nit = dto.ProveedorNit ?? string.Empty;
            Observaciones = dto.Observaciones ?? string.Empty;

            Animales.Clear();
            foreach (var animal in dto.Animales)
            {
                Animales.Add(new CompraAnimalFila(animal));
            }

            Anulada = editable.Anulada;
            Modo = ModoDocumento.Detalle;
        }, "abrir la compra");
    }

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private void AgregarAnimal()
        => Animales.Add(new CompraAnimalFila(null, _orientacionPredeterminada));

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private void QuitarAnimal(CompraAnimalFila? fila)
    {
        if (fila is not null)
        {
            Animales.Remove(fila);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeEditarODesechar))]
    private void EditarCompra() => Modo = ModoDocumento.Edicion;

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private async Task GuardarAsync()
    {
        if (GranjaSeleccionada?.Id is not { } granjaId)
        {
            Dialogos.Advertir("Seleccione la granja de destino de la compra.");
            return;
        }

        var dto = new CompraDto
        {
            RemisionId = _remisionId,
            NumeroFactura = NumeroFactura,
            OrdenCompra = Vacio(NumeroOrden),
            FechaCompra = FechaCompra ?? default,
            GranjaDestinoId = granjaId,
            Proveedor = Proveedor,
            ProveedorNit = Vacio(Nit),
            Observaciones = Vacio(Observaciones),
            Animales = [.. Animales.Select(a => a.ToDto())],
        };

        CompraResultado? resultado = null;
        var guardado = await EjecutarAsync(async () =>
        {
            resultado = _remisionId is null
                ? await _kardex.RegistrarCompraAsync(dto)
                : await _kardex.EditarCompraAsync(dto);
        }, "guardar la compra");

        if (!guardado || resultado is null)
        {
            return;
        }

        var accion = _remisionId is null ? "registrada" : "actualizada";
        var mensaje = $"Compra {accion}: {Formato.Animales(resultado.AnimalIds.Count)} por {Formato.Dinero(resultado.TotalFactura)}.";
        mensaje += $"\nValor contable según la matriz de tarifas: {Formato.Dinero(resultado.TotalCostoSistema)}.";
        if (resultado.SinTarifa.Count > 0)
        {
            mensaje += $"\n\nSin tarifa vigente (quedan como «Sin clasificar»): {string.Join(", ", resultado.SinTarifa)}.";
        }

        Dialogos.Informar(mensaje, "Compra guardada");
        Cerrar(true);
    }

    [RelayCommand(CanExecute = nameof(PuedeEditarODesechar))]
    private async Task AnularAsync()
    {
        if (_remisionId is not { } id)
        {
            return;
        }

        if (!Dialogos.Confirmar(
                $"Se anulará la compra {NumeroFactura}: los animales dejan de contar en el inventario y en el Kardex, " +
                "y el número de factura queda consumido.\n\n¿Desea continuar?",
                "Anular compra", destructivo: true))
        {
            return;
        }

        var motivo = Dialogos.Pedir("Anular compra", "Motivo de la anulación:");
        if (motivo is null)
        {
            return;
        }

        if (await EjecutarAsync(() => _kardex.AnularAsync(id, motivo), "anular la compra"))
        {
            Dialogos.Informar($"La compra {NumeroFactura} fue anulada.", "Compra anulada");
            Cerrar(true);
        }
    }

    partial void OnModoChanged(ModoDocumento value) => NotificarComandos();

    partial void OnAnuladaChanged(bool value) => NotificarComandos();

    private void NotificarComandos()
    {
        AgregarAnimalCommand.NotifyCanExecuteChanged();
        QuitarAnimalCommand.NotifyCanExecuteChanged();
        EditarCompraCommand.NotifyCanExecuteChanged();
        GuardarCommand.NotifyCanExecuteChanged();
        AnularCommand.NotifyCanExecuteChanged();
    }

    private async Task CargarCatalogosAsync()
    {
        Granjas.Clear();
        foreach (var g in await _catalogo.GetGranjasAsync(soloActivas: true))
        {
            Granjas.Add(new OpcionFiltro(g.Id, g.Nombre));
        }

        Orientaciones.Clear();
        foreach (var o in await _catalogo.GetOrientacionesAsync())
        {
            Orientaciones.Add(new OpcionFiltro(o.Id, o.Nombre));
        }

        _orientacionPredeterminada = Orientaciones.FirstOrDefault()?.Id ?? 0;

        Razas.Clear();
        foreach (var r in await _catalogo.GetRazasAsync())
        {
            Razas.Add(r);
        }
    }

    private void AlCambiarAnimales(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (CompraAnimalFila fila in e.NewItems)
            {
                fila.PropertyChanged += AlCambiarFila;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (CompraAnimalFila fila in e.OldItems)
            {
                fila.PropertyChanged -= AlCambiarFila;
            }
        }

        NotificarTotales();
    }

    private void AlCambiarFila(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompraAnimalFila.PrecioFactura))
        {
            NotificarTotales();
        }
    }

    private void NotificarTotales()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalTexto));
        OnPropertyChanged(nameof(CantidadTexto));
    }

    private static string? Vacio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
