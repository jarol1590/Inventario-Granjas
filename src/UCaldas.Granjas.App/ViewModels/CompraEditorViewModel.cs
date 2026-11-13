using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Lotes;

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
    private readonly ILoteService _lotes;
    private int? _loteId;
    private readonly ContextoGlobal _contexto;
    private int? _remisionId;
    private int _orientacionPredeterminada;

    public CompraEditorViewModel(
        IKardexService kardex,
        IConsultasService consultas,
        ICatalogoService catalogo,
        ILoteService lotes,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _kardex = kardex;
        _consultas = consultas;
        _catalogo = catalogo;
        _lotes = lotes;
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

    /// <summary>Programas de inventario individual (bovinos, equinos...).</summary>
    public ObservableCollection<Programa> Programas { get; } = [];

    /// <summary>Categorías del programa elegido (programas sin valorización biológica).</summary>
    public ObservableCollection<OpcionFiltro> Categorias { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsBovino), nameof(NoEsBovino))]
    private Programa? _programaSeleccionado;

    /// <summary>Orientación y partos son de los bovinos; los demás programas eligen categoría.</summary>
    public bool EsBovino => ProgramaSeleccionado?.UsaValorizacionBiologica ?? true;

    public bool NoEsBovino => !EsBovino;

    /// <summary>El programa solo se elige al crear la compra.</summary>
    public bool PuedeElegirPrograma => Modo == ModoDocumento.Nuevo;

    private int _categoriaPredeterminada;

    public ObservableCollection<string> Razas { get; } = [];

    public ObservableCollection<CompraAnimalFila> Animales { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo), nameof(EsLectura), nameof(EsEditable), nameof(PuedeEditarODesechar), nameof(PuedeElegirPrograma), nameof(MostrarAgregarAnimal))]
    private ModoDocumento _modo = ModoDocumento.Nuevo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditable), nameof(PuedeEditarODesechar), nameof(MostrarAgregarAnimal))]
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

    /// <summary>La compra es la de un lote (programa por lotes): se muestra su fila de lote en lugar de los animales.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsIndividual), nameof(MostrarAgregarAnimal))]
    private bool _esLote;

    public bool MostrarAgregarAnimal => EsEditable && EsIndividual;

    public bool EsIndividual => !EsLote;

    /// <summary>Una compra de lote tiene un solo lote.</summary>
    public ObservableCollection<CompraLoteFila> Lotes { get; } = [];

    public decimal Total => EsLote ? Lotes.Sum(l => l.Total) : Animales.Sum(a => a.PrecioFactura);

    public string TotalTexto => Formato.Dinero(Total);

    public string CantidadTexto => EsLote
        ? $"Total cabezas: {(Lotes.FirstOrDefault() is { ControlNumerico: true } l ? l.Cantidad.ToString("N0", Formato.Cultura) : "N/A")}"
        : $"Total animales: {Animales.Count}";

    /// <summary>Abre la compra de un lote: la misma pantalla de detalle, editable y anulable como la de animales.</summary>
    public async Task<bool> InicializarLoteAsync(int loteId)
    {
        _remisionId = null;
        _loteId = loteId;
        return await EjecutarAsync(async () =>
        {
            await CargarCatalogosAsync();
            var detalle = await _lotes.ObtenerAsync(loteId);
            var lote = detalle.Lote;

            EsLote = true;
            NumeroFactura = lote.OrigenReferencia ?? string.Empty;
            NumeroOrden = lote.OrdenCompra ?? string.Empty;
            FechaCompra = lote.FechaNacimientoIngreso;
            GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == lote.GranjaId);
            Proveedor = lote.Proveedor ?? string.Empty;
            Nit = string.Empty;
            Observaciones = lote.Observaciones ?? string.Empty;
            // El combo solo lista programas individuales: el programa por lotes de esta compra se agrega para mostrarlo.
            if (Programas.All(p => p.Id != lote.ProgramaId) && (await _catalogo.GetProgramasAsync()).FirstOrDefault(p => p.Id == lote.ProgramaId) is { } programaDelLote)
            {
                Programas.Add(programaDelLote);
            }

            ProgramaSeleccionado = Programas.FirstOrDefault(p => p.Id == lote.ProgramaId) ?? ProgramaSeleccionado;

            Lotes.Clear();
            var fila = new CompraLoteFila(detalle);
            fila.PropertyChanged += (_, _) => NotificarTotales();
            Lotes.Add(fila);

            Anulada = lote.Anulado;
            Modo = ModoDocumento.Detalle;
            NotificarTotales();
        }, "abrir la compra del lote");
    }

    public async Task<bool> InicializarNuevaAsync()
    {
        _remisionId = null;
        return await EjecutarAsync(async () =>
        {
            await CargarCatalogosAsync();
            GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == _contexto.GranjaId) ?? Granjas.FirstOrDefault();
            ProgramaSeleccionado = Programas.FirstOrDefault(p => p.Id == _contexto.ProgramaId)
                ?? Programas.FirstOrDefault(p => p.UsaValorizacionBiologica) ?? Programas.FirstOrDefault();
            await CargarCategoriasAsync();
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

            // El programa de la compra se deduce de la categoría de sus animales.
            var categoriaDeCompra = dto.Animales.Select(a => a.CategoriaId).FirstOrDefault(id => id > 0);
            var programaId = (await _catalogo.GetCategoriasAsync()).FirstOrDefault(c => c.Id == categoriaDeCompra)?.ProgramaId;
            ProgramaSeleccionado = Programas.FirstOrDefault(p => p.Id == programaId) ?? ProgramaSeleccionado;
            await CargarCategoriasAsync();

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
    {
        var fila = new CompraAnimalFila(null, _orientacionPredeterminada);
        fila.CategoriaId = NoEsBovino ? _categoriaPredeterminada : 0;
        Animales.Add(fila);
    }

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private void QuitarAnimal(CompraAnimalFila? fila)
    {
        if (fila is not null)
        {
            Animales.Remove(fila);
        }
    }

    /// <summary>Quitar el lote de la compra = anular la compra (como quitar el único animal de una factura), con confirmación y motivo.</summary>
    [RelayCommand(CanExecute = nameof(PuedeQuitarLote))]
    private async Task QuitarLoteAsync()
    {
        if (_loteId is { } loteId)
        {
            await AnularCompraDeLoteAsync(loteId);
        }
    }

    private bool PuedeQuitarLote() => EsEditable && EsLote && _loteId is not null;

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

        if (EsLote)
        {
            await GuardarCompraDeLoteAsync(granjaId);
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
            Animales = [.. Animales.Select(a => a.ToDto(NoEsBovino))],
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

    private async Task GuardarCompraDeLoteAsync(int granjaId)
    {
        if (Lotes.FirstOrDefault() is not { } fila || _loteId is not { } loteId)
        {
            return;
        }

        var guardado = await EjecutarAsync(() => _lotes.EditarCompraAsync(new EditarCompraLoteDto
        {
            LoteId = loteId,
            Factura = NumeroFactura,
            OrdenCompra = NumeroOrden,
            Proveedor = Proveedor,
            FechaIngreso = FechaCompra ?? default,
            GranjaId = granjaId,
            Ubicacion = fila.Ubicacion,
            LineaRaza = fila.LineaRaza,
            Cantidad = fila.Cantidad,
            PrecioUnitarioFactura = fila.PrecioUnitario,
            Observaciones = Vacio(Observaciones),
        }), "guardar la compra del lote");

        if (guardado)
        {
            Dialogos.Informar($"Compra del lote {fila.Codigo} actualizada: {Formato.Dinero(fila.Total)}.", "Compra guardada");
            Cerrar(true);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeEditarODesechar))]
    private async Task AnularAsync()
    {
        if (EsLote && _loteId is { } loteDeCompra)
        {
            await AnularCompraDeLoteAsync(loteDeCompra);
            return;
        }

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

    private async Task AnularCompraDeLoteAsync(int loteId)
    {
        if (!Dialogos.Confirmar(
                $"Quitar el lote equivale a anular la compra {NumeroFactura}: el lote deja de contar en el inventario y su código y número de factura quedan consumidos.\n\n¿Desea continuar?",
                "Anular compra", destructivo: true))
        {
            return;
        }

        var motivo = Dialogos.Pedir("Anular compra", "Motivo de la anulación:");
        if (motivo is null)
        {
            return;
        }

        if (await EjecutarAsync(() => _lotes.AnularCompraAsync(loteId, motivo), "anular la compra del lote"))
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
        QuitarLoteCommand.NotifyCanExecuteChanged();
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

        Programas.Clear();
        foreach (var p in await _catalogo.GetProgramasPorTipoAsync(TipoInventario.Individual))
        {
            Programas.Add(p);
        }

        Razas.Clear();
        foreach (var r in await _catalogo.GetRazasAsync())
        {
            Razas.Add(r);
        }
    }

    partial void OnProgramaSeleccionadoChanged(Programa? value)
    {
        if (Modo != ModoDocumento.Nuevo || _remisionId is not null || _loteId is not null)
        {
            return;
        }

        _ = EjecutarAsync(async () =>
        {
            await CargarCategoriasAsync();
            foreach (var fila in Animales)
            {
                fila.CategoriaId = NoEsBovino ? _categoriaPredeterminada : 0;
            }
        }, "cargar las categorías del programa");
    }

    private async Task CargarCategoriasAsync()
    {
        Categorias.Clear();
        _categoriaPredeterminada = 0;
        if (ProgramaSeleccionado is not { UsaValorizacionBiologica: false } programa)
        {
            return;
        }

        foreach (var c in await _catalogo.GetCategoriasDeProgramaAsync(programa.Id))
        {
            Categorias.Add(new OpcionFiltro(c.Id, c.Nombre));
        }

        _categoriaPredeterminada = Categorias.FirstOrDefault()?.Id ?? 0;
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
