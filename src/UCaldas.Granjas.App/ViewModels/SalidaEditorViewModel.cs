using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels.Remisiones;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Comercial;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Salida de animales: venta (Oficio → Remisión → Recibo de Caja) o baja por muerte, hurto o descarte sanitario (solo
/// Remisión). Permite crear, consultar, editar y anular. Los traslados se registran en su propia sección.
/// </summary>
public sealed partial class SalidaEditorViewModel : DialogViewModelBase
{
    private readonly IKardexService _kardex;
    private readonly IConsultasService _consultas;
    private readonly ICatalogoService _catalogo;
    private readonly ILoteService _lotes;
    private Programa? _programaLote;
    private readonly IFormularios _formularios;
    private readonly IFormulariosRemisiones _formulariosRemisiones;
    private readonly IRemisionesQueryService _remisiones;
    private readonly ContextoGlobal _contexto;
    private readonly IReloj _reloj;
    private ReciboCaja? _reciboExistente;
    private int? _remisionId;
    private int _programaId;
    private bool _cargando;
    private bool _revirtiendo;

    public SalidaEditorViewModel(
        IKardexService kardex,
        IConsultasService consultas,
        ICatalogoService catalogo,
        ILoteService lotes,
        IFormularios formularios,
        IFormulariosRemisiones formulariosRemisiones,
        IRemisionesQueryService remisiones,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _formulariosRemisiones = formulariosRemisiones;
        _remisiones = remisiones;
        _kardex = kardex;
        _consultas = consultas;
        _catalogo = catalogo;
        _lotes = lotes;
        _formularios = formularios;
        _contexto = contexto;
        _reloj = reloj;
        _fechaSalida = reloj.Hoy;
        _claseSeleccionada = Clases[0];
        Animales.CollectionChanged += AlCambiarAnimales;
    }

    public override string Titulo => Modo switch
    {
        ModoDocumento.Nuevo => "Nueva salida",
        ModoDocumento.Detalle => $"Detalle de la salida {NumeroRemision}",
        _ => $"Editar salida {NumeroRemision}",
    };

    public override double Ancho => 1100;

    public override double Alto => 780;

    public IReadOnlyList<OpcionTexto> Clases { get; } =
    [
        new(ClaseSalida.Venta, "Venta"),
        new(ClaseSalida.Muerte, "Muerte"),
        new(ClaseSalida.Hurto, "Hurto"),
        new(ClaseSalida.DescarteSanitario, "Descarte sanitario"),
    ];

    public ObservableCollection<OpcionFiltro> Granjas { get; } = [];

    public ObservableCollection<SalidaAnimalFila> Animales { get; } = [];

    /// <summary>Números de los oficios abiertos del programa de la venta (lista desplegable del campo «N° Oficio»).</summary>
    public ObservableCollection<string> OficiosAbiertos { get; } = [];

    /// <summary>La salida es la venta de lotes de un programa por lotes (misma pantalla, con una grilla de lotes).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsIndividual), nameof(PuedeCambiarClase), nameof(CantidadTexto), nameof(TotalTexto), nameof(MostrarAgregarAnimal), nameof(MostrarAgregarLote))]
    private bool _esLote;

    public bool MostrarAgregarAnimal => EsEditable && EsIndividual;

    public bool MostrarAgregarLote => EsEditable && EsLote;

    public bool EsIndividual => !EsLote;

    public ObservableCollection<SalidaLoteFila> Lotes { get; } = [];

    /// <summary>Lotes activos de la granja de origen que todavía no están en la venta.</summary>
    public ObservableCollection<LoteSeleccionable> LotesDisponibles { get; } = [];

    [ObservableProperty]
    private LoteSeleccionable? _loteParaAgregar;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo), nameof(EsLectura), nameof(EsEditable), nameof(PuedeEditarODesechar), nameof(PuedeCambiarClase), nameof(MostrarAgregarAnimal), nameof(MostrarAgregarLote))]
    private ModoDocumento _modo = ModoDocumento.Nuevo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditable), nameof(PuedeEditarODesechar), nameof(MostrarAgregarAnimal), nameof(MostrarAgregarLote))]
    private bool _anulada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsVenta), nameof(EsBaja), nameof(TotalTexto), nameof(EtiquetaTotal))]
    private OpcionTexto _claseSeleccionada;

    [ObservableProperty]
    private OpcionFiltro? _granjaSeleccionada;

    [ObservableProperty]
    private DateTime? _fechaSalida;

    [ObservableProperty]
    private string _numeroOficio = string.Empty;

    /// <summary>Recibo de Caja SP de la venta: «Automático» hasta guardarla; después su número («829 SP»).</summary>
    [ObservableProperty]
    private string _reciboCaja = ReciboAutomatico;

    private const string ReciboAutomatico = "Automático (se abre al guardar)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private string _numeroRemision = string.Empty;

    [ObservableProperty]
    private string _comprador = string.Empty;

    [ObservableProperty]
    private string _documentoSoporte = string.Empty;

    [ObservableProperty]
    private string _observaciones = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerDetalleAnimalCommand))]
    private SalidaAnimalFila? _filaSeleccionada;

    public bool EsVenta => ClaseSeleccionada.Valor == ClaseSalida.Venta;

    public bool EsBaja => !EsVenta;

    public bool EsLectura => Modo == ModoDocumento.Detalle;

    public bool EsEditable => Modo != ModoDocumento.Detalle && !Anulada;

    public bool PuedeEditarODesechar => EsLectura && !Anulada;

    /// <summary>La clase solo se elige al crear el documento (una venta no se convierte en baja).</summary>
    public bool PuedeCambiarClase => Modo == ModoDocumento.Nuevo && !EsLote;

    public string CantidadTexto => EsLote ? $"Total cabezas: {Lotes.Sum(l => l.Cantidad):N0}" : $"Total Animales: {Animales.Count}";

    public string EtiquetaTotal => EsVenta ? "Total venta" : "Valor en libros";

    public string TotalTexto => Formato.Dinero(
        EsLote ? Lotes.Sum(l => l.ValorVenta)
        : EsVenta ? Animales.Sum(a => a.PrecioVenta) : Animales.Sum(a => a.Animal.ValorContable));

    public async Task<bool> InicializarNuevaAsync()
    {
        _remisionId = null;
        return await EjecutarAsync(async () =>
        {
            await CargarCatalogosAsync();
            _cargando = true;
            try
            {
                GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == _contexto.GranjaId) ?? Granjas.FirstOrDefault();
            }
            finally
            {
                _cargando = false;
            }

            // En un programa por lotes la salida es la venta de lotes (las bajas por mortandad se registran en Lotes).
            var programaElegido = _contexto.ProgramaId is { } elegido
                ? (await _catalogo.GetProgramasAsync()).FirstOrDefault(p => p.Id == elegido)
                : null;

            // Un programa individual elegido en los filtros (p. ej. Equinos) fija el programa de la venta y de su oficio.
            if (programaElegido is { TipoInventario: TipoInventario.Individual, ManejaInventarioAnimal: true })
            {
                _programaId = programaElegido.Id;
            }

            if (programaElegido is { TipoInventario: TipoInventario.Lote } programa)
            {
                _programaLote = programa;
                _programaId = programa.Id;

                // Sin granja elegida en los filtros se parte de una granja donde se ejecuta el programa (donde están sus lotes).
                if (_contexto.GranjaId is null)
                {
                    var delPrograma = (await _catalogo.GetGranjaIdsDeProgramaAsync(programa.Id)).ToHashSet();
                    GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id is { } id && delPrograma.Contains(id)) ?? GranjaSeleccionada;
                }

                EsLote = true;
                await CargarLotesDisponiblesAsync();
            }

            NumeroRemision = (await _remisiones.SiguientesConsecutivosAsync()).Remision;

            // Oficios abiertos del programa: uno solo se propone en el campo; con varios el usuario elige de la lista.
            OficiosAbiertos.Clear();
            foreach (var oficio in await _remisiones.ListarOficiosAbiertosAsync(await ProgramaDeGanaderiaAsync()))
            {
                OficiosAbiertos.Add(oficio.NumeroOficio);
            }

            // Sin un único oficio abierto se propone el siguiente número (OFGAN-26001); el usuario puede cambiarlo o elegir uno existente.
            NumeroOficio = OficiosAbiertos.Count == 1
                ? OficiosAbiertos[0]
                : await _remisiones.SiguienteNumeroOficioAsync(await ProgramaDeGanaderiaAsync(), _reloj.Hoy);

            Modo = ModoDocumento.Nuevo;
        }, "preparar el formulario de salida");
    }

    public async Task<bool> InicializarExistenteAsync(int remisionId, string clase)
    {
        _remisionId = remisionId;
        return await EjecutarAsync(async () =>
        {
            await CargarCatalogosAsync();
            _cargando = true;
            try
            {
                if (await _consultas.EsDocumentoDeLotesAsync(remisionId))
                {
                    await CargarVentaDeLotesAsync(remisionId);
                }
                else if (clase == ClaseSalida.Venta)
                {
                    var venta = await _consultas.ObtenerVentaAsync(remisionId);
                    var dto = venta.Dto;
                    ClaseSeleccionada = Clases.First(c => c.Valor == ClaseSalida.Venta);
                    GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == dto.GranjaOrigenId);
                    FechaSalida = dto.FechaRemision;
                    NumeroOficio = dto.NumeroOficio;
                    await CargarReciboExistenteAsync(remisionId);
                    NumeroRemision = dto.NumeroRemision;
                    Comprador = dto.Comprador;
                    Observaciones = dto.Observaciones ?? string.Empty;
                    _programaId = dto.ProgramaId;
                    Anulada = venta.Anulada;
                    CargarFilas(venta.Animales, dto.Animales.ToDictionary(a => a.AnimalId));
                }
                else
                {
                    var baja = await _consultas.ObtenerBajaAsync(remisionId);
                    var dto = baja.Dto;
                    ClaseSeleccionada = Clases.FirstOrDefault(c => c.Valor == dto.ClaseSalida) ?? Clases[1];
                    GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == dto.GranjaOrigenId);
                    FechaSalida = dto.FechaBaja;
                    NumeroRemision = dto.NumeroRemision;
                    DocumentoSoporte = dto.DocumentoSoporte;
                    Observaciones = dto.Observaciones ?? string.Empty;
                    Anulada = baja.Anulada;
                    CargarFilas(baja.Animales, null);
                }
            }
            finally
            {
                _cargando = false;
            }

            Modo = ModoDocumento.Detalle;
            NotificarTotales();
        }, "abrir la salida");
    }

    partial void OnNumeroOficioChanged(string value) => AvisoOficio = string.Empty;

    partial void OnModoChanged(ModoDocumento value) => NotificarComandos();

    partial void OnAnuladaChanged(bool value) => NotificarComandos();

    partial void OnClaseSeleccionadaChanged(OpcionTexto value) => NotificarTotales();

    partial void OnGranjaSeleccionadaChanged(OpcionFiltro? oldValue, OpcionFiltro? newValue)
    {
        if (_cargando || _revirtiendo || newValue is null || (EsLote ? Lotes.Count : Animales.Count) == 0)
        {
            if (EsLote && !_cargando && !_revirtiendo && newValue is not null)
            {
                _ = EjecutarAsync(CargarLotesDisponiblesAsync, "consultar los lotes de la granja");
            }

            return;
        }

        if (!Dialogos.Confirmar(
                "Al cambiar la granja de origen se quitarán de la lista los animales ya agregados, porque pertenecen a la granja anterior.\n\n¿Desea continuar?",
                "Cambiar granja de origen"))
        {
            ContextoUi.Diferir(() =>
            {
                _revirtiendo = true;
                try
                {
                    GranjaSeleccionada = oldValue;
                }
                finally
                {
                    _revirtiendo = false;
                }
            });
            return;
        }

        Animales.Clear();
        Lotes.Clear();
        if (EsLote)
        {
            _ = EjecutarAsync(CargarLotesDisponiblesAsync, "consultar los lotes de la granja");
        }
    }

    private async Task CargarLotesDisponiblesAsync()
    {
        LotesDisponibles.Clear();
        if (_programaLote is null || GranjaSeleccionada?.Id is not { } granjaId)
        {
            return;
        }

        var enVenta = Lotes.Select(l => l.LoteId).ToHashSet();
        foreach (var d in (await _lotes.ListarAsync(_programaLote.Id)).Where(d => d.Lote.Estado == EstadoLote.Activo && d.Lote.GranjaId == granjaId && !enVenta.Contains(d.Lote.Id)))
        {
            LotesDisponibles.Add(new LoteSeleccionable(
                d.Lote.Id, d.Lote.CodigoLote, d.EtapaNombre, d.Lote.CantidadActual, d.Lote.ControlNumericoPoblacion, d.Lote.ValorContableUnitario));
        }

        LoteParaAgregar = LotesDisponibles.FirstOrDefault();
    }

    /// <summary>Recibo SP ya emitido de la venta: se muestra su número y se precarga al editar.</summary>
    private async Task CargarReciboExistenteAsync(int remisionId)
    {
        var vista = await _remisiones.ObtenerAsync(remisionId);
        _reciboExistente = vista.Recibo;
        DescuentoDelRecibo = vista.Remision.DescuentoPorcentaje;
        ReciboCaja = _reciboExistente is { } r ? $"{r.NumeroRecibo} {r.Prefijo}" + (r.Anulado ? " (anulado)" : string.Empty) : ReciboAutomatico;
    }

    /// <summary>
    /// Abre la ventana del Recibo de Caja SP con las líneas de la venta (misma ventana que usa el módulo Remisiones). Nulo si el
    /// usuario la cancela. Con datos incompletos devuelve un recibo vacío y deja que el servicio explique qué falta.
    /// </summary>
    private async Task<(bool Continuar, ReciboCajaDto? Recibo)> CapturarReciboAsync()
    {
        var lineas = new List<LineaRemision>();
        if (EsLote)
        {
            lineas.AddRange(Lotes.Select(l => new LineaRemision(
                ClaseDeLinea.Lote, null, l.LoteId, null, l.Codigo, $"Lote {l.Codigo} · {l.Etapa}", "Cabeza", l.Cantidad,
                l.Cantidad > 0 ? l.ValorVenta / l.Cantidad : l.ValorVenta, l.ValorVenta, l.PesoKg > 0 ? l.PesoKg : null, l.ValorEnLibros, null, null, null)));
        }
        else
        {
            lineas.AddRange(Animales.Select(a => new LineaRemision(
                ClaseDeLinea.Animal, a.AnimalId, null, null, a.ChapetaAmarilla, $"{a.Animal.Categoria} — {a.Animal.Raza}", "Cabeza", 1m,
                a.PrecioVenta, a.PrecioVenta, a.PesoKg, a.Animal.ValorContable, null, null, null)));
        }

        if (lineas.Count == 0 || string.IsNullOrWhiteSpace(Comprador))
        {
            return (true, null);
        }

        var etiqueta = _reciboExistente is { } r
            ? $"{r.NumeroRecibo} {r.Prefijo}"
            : $"{(await _remisiones.SiguientesConsecutivosAsync()).Recibo} SP";
        var programa = (await _catalogo.GetProgramasAsync()).FirstOrDefault(p => p.Id == (EsLote ? _programaId : _programaId > 0 ? _programaId : _contexto.ProgramaId))?.Nombre ?? "Ganadería";
        var contexto = new ReciboContexto(
            true, etiqueta, NumeroRemision.Trim(), programa, Comprador.Trim(), null, lineas, lineas.Sum(l => l.ValorTotal), 0m,
            _reciboExistente is null ? null : new ReciboCajaDto
            {
                Fecha = _reciboExistente.Fecha, FormaPago = _reciboExistente.FormaPago, BancoConsignacion = _reciboExistente.BancoConsignacion,
                NumeroCuenta = _reciboExistente.NumeroCuenta, TipoCuenta = _reciboExistente.TipoCuenta,
                EsEfectivo = _reciboExistente.EsEfectivo, Cheque1Num = _reciboExistente.Cheque1Num, Cheque2Num = _reciboExistente.Cheque2Num,
                Observaciones = _reciboExistente.Observaciones, TotalPagado = _reciboExistente.TotalPagado,
                DescuentoPorcentaje = DescuentoDelRecibo,
            });
        return await _formulariosRemisiones.CapturarReciboAsync(contexto) is { } captura ? (true, captura.Recibo) : (false, null);
    }

    private decimal DescuentoDelRecibo { get; set; }

    private async Task CargarVentaDeLotesAsync(int remisionId)
    {
        var venta = await _consultas.ObtenerVentaLoteAsync(remisionId);
        var dto = venta.Dto;
        _programaLote = (await _catalogo.GetProgramasAsync()).FirstOrDefault(p => p.Id == dto.ProgramaId);
        _programaId = dto.ProgramaId;
        EsLote = true;
        ClaseSeleccionada = Clases.First(c => c.Valor == ClaseSalida.Venta);
        GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == dto.GranjaOrigenId);
        FechaSalida = dto.FechaRemision;
        NumeroOficio = dto.NumeroOficio;
        await CargarReciboExistenteAsync(remisionId);
        NumeroRemision = dto.NumeroRemision;
        Comprador = dto.Comprador;
        Observaciones = dto.Observaciones ?? string.Empty;
        Anulada = venta.Anulada;

        Lotes.Clear();
        foreach (var linea in dto.Lotes)
        {
            var lote = venta.Lotes.First(l => l.Id == linea.LoteId);
            AgregarFilaDeLote(new SalidaLoteFila(lote, linea.Cantidad, linea.PesoKg, linea.ValorVenta, linea.ObservacionesEspecificas));
        }

        await CargarLotesDisponiblesAsync();
    }

    private void AgregarFilaDeLote(SalidaLoteFila fila)
    {
        fila.PropertyChanged += (_, _) => NotificarTotales();
        Lotes.Add(fila);
        NotificarTotales();
    }

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private void AgregarLote()
    {
        if (LoteParaAgregar is not { } lote)
        {
            Dialogos.Advertir("No hay lotes activos en la granja de origen para agregar.");
            return;
        }

        AgregarFilaDeLote(new SalidaLoteFila(lote, lote.ControlNumerico ? lote.CantidadActual : 0));
        LotesDisponibles.Remove(lote);
        LoteParaAgregar = LotesDisponibles.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private void QuitarLote(SalidaLoteFila? fila)
    {
        if (fila is null)
        {
            return;
        }

        Lotes.Remove(fila);
        _ = EjecutarAsync(CargarLotesDisponiblesAsync, "consultar los lotes de la granja");
        NotificarTotales();
    }

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private async Task AgregarAnimalAsync()
    {
        if (GranjaSeleccionada?.Id is not { } granjaId)
        {
            Dialogos.Advertir("Seleccione primero la granja de origen.");
            return;
        }

        var elegidos = await _formularios.SeleccionarAnimalesAsync(granjaId, [.. Animales.Select(a => a.AnimalId)]);
        if (elegidos is null)
        {
            return;
        }

        foreach (var animal in elegidos)
        {
            Animales.Add(new SalidaAnimalFila(animal));
        }
    }

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private void QuitarAnimal(SalidaAnimalFila? fila)
    {
        if (fila is not null)
        {
            Animales.Remove(fila);
        }
    }

    [RelayCommand(CanExecute = nameof(HayFilaSeleccionada))]
    private void VerDetalleAnimal()
    {
        if (FilaSeleccionada is not { } fila)
        {
            return;
        }

        var detalle = new SalidaAnimalViewModel(
            fila, ClaseSeleccionada.Nombre, NumeroOficio, ReciboCaja, EsVenta ? Comprador : DocumentoSoporte,
            FechaSalida, EsVenta, EsEditable, Dialogos);
        if (Dialogos.Mostrar(detalle))
        {
            NotificarTotales();
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeEditarODesechar))]
    private void EditarSalida() => Modo = ModoDocumento.Edicion;

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private async Task GuardarAsync()
    {
        if (GranjaSeleccionada?.Id is not { } granjaId)
        {
            Dialogos.Advertir("Seleccione la granja de origen.");
            return;
        }

        // Un número de remisión ya usado no se duplica: se ofrece abrir ese documento para editarlo.
        if (_abriendoExistente || (_remisionId is null && await OfrecerAbrirExistenteAsync()))
        {
            return;
        }

        // Toda venta (animales o lotes) lleva su Recibo de Caja SP: se captura en la misma ventana del módulo Remisiones y se guarda
        // junto con la venta, en una sola transacción.
        ReciboCajaDto? recibo = null;
        if (EsVenta || EsLote)
        {
            var (continuar, capturado) = await CapturarReciboAsync();
            if (!continuar)
            {
                return;
            }

            recibo = capturado;
        }

        SalidaResultado? resultado = null;
        var guardado = await EjecutarAsync(async () =>
        {
            if (EsLote)
            {
                var dto = new VentaLoteDto
                {
                    RemisionId = _remisionId,
                    NumeroOficio = NumeroOficio,
                    FechaOficio = FechaSalida ?? default,
                    ProgramaId = _programaId,
                    NumeroRemision = NumeroRemision,
                    FechaRemision = FechaSalida ?? default,
                    Recibo = recibo,
                    GranjaOrigenId = granjaId,
                    Comprador = Comprador,
                    Observaciones = Vacio(Observaciones),
                    Lotes = [.. Lotes.Select(l => l.ToVentaDto())],
                };
                resultado = _remisionId is null ? await _kardex.RegistrarVentaLoteAsync(dto) : await _kardex.EditarVentaLoteAsync(dto);
            }
            else if (EsVenta)
            {
                var dto = new VentaDto
                {
                    RemisionId = _remisionId,
                    NumeroOficio = NumeroOficio,
                    FechaOficio = FechaSalida ?? default,
                    ProgramaId = await ProgramaDeGanaderiaAsync(),
                    NumeroRemision = NumeroRemision,
                    FechaRemision = FechaSalida ?? default,
                    Recibo = recibo,
                    GranjaOrigenId = granjaId,
                    Comprador = Comprador,
                    Observaciones = Vacio(Observaciones),
                    Animales = [.. Animales.Select(a => a.ToVentaDto())],
                };
                resultado = _remisionId is null ? await _kardex.RegistrarVentaAsync(dto) : await _kardex.EditarVentaAsync(dto);
            }
            else
            {
                var dto = new BajaDto
                {
                    RemisionId = _remisionId,
                    NumeroRemision = NumeroRemision,
                    FechaBaja = FechaSalida ?? default,
                    GranjaOrigenId = granjaId,
                    ClaseSalida = ClaseSeleccionada.Valor!,
                    DocumentoSoporte = DocumentoSoporte,
                    Observaciones = Vacio(Observaciones),
                    Animales = [.. Animales.Select(a => a.ToBajaDto())],
                };
                resultado = _remisionId is null ? await _kardex.RegistrarBajaAsync(dto) : await _kardex.EditarBajaAsync(dto);
            }
        }, "guardar la salida");

        if (!guardado || resultado is null)
        {
            return;
        }

        var accion = _remisionId is null ? "registrada" : "actualizada";
        var mensaje = EsLote
            ? $"Venta de lotes {accion}: {resultado.Animales:N0} cabezas por {Formato.Dinero(resultado.TotalVenta)}.\nCosto en libros: {Formato.Dinero(resultado.TotalCostoSistema)}."
            : EsVenta
            ? $"Venta {accion}: {Formato.Animales(resultado.Animales)} por {Formato.Dinero(resultado.TotalVenta)}.\nCosto en libros: {Formato.Dinero(resultado.TotalCostoSistema)}."
            : $"Baja {accion}: {Formato.Animales(resultado.Animales)}.\nCosto en libros: {Formato.Dinero(resultado.TotalCostoSistema)}.";
        Dialogos.Informar(mensaje, "Salida guardada");
        Cerrar(true);
    }

    /// <summary>Se ejecuta al salir del campo «Remisión»: si el número ya existe, ofrece abrir esa remisión.</summary>
    [RelayCommand]
    private async Task VerificarRemisionAsync()
    {
        if (_remisionId is null && Modo == ModoDocumento.Nuevo && !_abriendoExistente)
        {
            await OfrecerAbrirExistenteAsync();
        }
    }

    /// <summary>
    /// Se ejecuta al salir del campo «N° Oficio». Un oficio existente es válido (la remisión se agrega a él), pero se avisa antes
    /// de guardar si está cerrado o si es de otro programa; si es válido se muestra bajo el campo a qué oficio se agregará.
    /// </summary>
    [RelayCommand]
    private async Task VerificarOficioAsync()
    {
        AvisoOficio = string.Empty;
        if (_remisionId is not null || Modo != ModoDocumento.Nuevo || !EsVenta || string.IsNullOrWhiteSpace(NumeroOficio) || _abriendoExistente)
        {
            return;
        }

        OficioVenta? oficio = null;
        var programaId = 0;
        string? programaDelOficio = null;
        if (!await EjecutarAsync(async () =>
            {
                oficio = await _remisiones.BuscarOficioPorNumeroAsync(NumeroOficio);
                programaId = EsLote ? _programaId : await ProgramaDeGanaderiaAsync();
                if (oficio is not null && oficio.ProgramaId != programaId)
                {
                    programaDelOficio = (await _catalogo.GetProgramasAsync()).FirstOrDefault(p => p.Id == oficio.ProgramaId)?.Nombre;
                }
            }, "consultar el oficio"))
        {
            return;
        }

        if (oficio is null)
        {
            return;
        }

        if (oficio.ProgramaId != programaId)
        {
            Dialogos.Advertir($"El oficio {oficio.NumeroOficio} pertenece al programa «{programaDelOficio}»: un oficio solo puede tener remisiones de un solo programa. Escriba otro número de oficio.");
        }
        else if (oficio.Cerrado)
        {
            Dialogos.Advertir($"El oficio {oficio.NumeroOficio} está cerrado y no admite remisiones nuevas. Reábralo en Remisiones › Oficios o escriba otro número.");
        }
        else
        {
            AvisoOficio = $"El oficio {oficio.NumeroOficio} ya existe (fecha {oficio.FechaOficio:dd/MM/yyyy}): la remisión se agregará a ese oficio.";
        }
    }

    /// <summary>Texto bajo el campo del oficio cuando ya existe y puede recibir la remisión.</summary>
    [ObservableProperty]
    private string _avisoOficio = string.Empty;

    private bool _abriendoExistente;

    /// <summary>
    /// Si el número escrito ya existe, pregunta si se abre esa remisión para editarla. Devuelve <c>true</c> cuando el guardado debe
    /// detenerse (se abrió la existente o el usuario prefiere corregir el número).
    /// </summary>
    private async Task<bool> OfrecerAbrirExistenteAsync()
    {
        Remision? existente = null;
        if (!await EjecutarAsync(async () => existente = await _remisiones.BuscarPorNumeroAsync(NumeroRemision), "consultar el número de remisión"))
        {
            return true;
        }

        if (existente is null)
        {
            return false;
        }

        var numero = existente.NumeroRemision;
        if (existente.TipoMovimiento == TipoRemision.Compra)
        {
            Dialogos.Advertir($"El número {numero} pertenece a una compra. Escriba otro número de remisión.");
            return true;
        }

        if (!Dialogos.Confirmar(
                $"Ya existe la remisión {numero}.\n\n¿Desea abrirla para editarla? (si elige No, corrija el número de esta salida)",
                "Remisión existente"))
        {
            return true;
        }

        var id = existente.Id;
        var clase = existente.ClaseSalida ?? ClaseSalida.Venta;
        var esBaja = existente.TipoMovimiento == TipoRemision.Baja;
        // La existente se abre encima (la pila de modales lo admite); al volver se cierra esta salida y se recarga el listado.
        // La bandera evita que el foco perdido al pulsar un botón dispare otra verificación o un segundo cierre.
        _abriendoExistente = true;
        await EjecutarAsync(
            () => esBaja ? _formularios.AbrirSalidaAsync(id, clase) : _formulariosRemisiones.AbrirRemisionAsync(id),
            "abrir la remisión existente");
        Cerrar(true);
        return true;
    }

    [RelayCommand(CanExecute = nameof(PuedeEditarODesechar))]
    private async Task AnularAsync()
    {
        if (_remisionId is not { } id)
        {
            return;
        }

        if (!Dialogos.Confirmar(
                $"Se anulará la salida {NumeroRemision}: " + (EsLote ? "las cabezas vuelven a sus lotes. " : "los animales vuelven al inventario y se reversan sus asientos del Kardex. ") +
                "El número del documento queda consumido.\n\n¿Desea continuar?",
                "Anular salida", destructivo: true))
        {
            return;
        }

        var motivo = Dialogos.Pedir("Anular salida", "Motivo de la anulación:");
        if (motivo is null)
        {
            return;
        }

        if (await EjecutarAsync(() => _kardex.AnularAsync(id, motivo), "anular la salida"))
        {
            Dialogos.Informar($"La salida {NumeroRemision} fue anulada.", "Salida anulada");
            Cerrar(true);
        }
    }

    private bool HayFilaSeleccionada() => FilaSeleccionada is not null;

    private void NotificarComandos()
    {
        AgregarAnimalCommand.NotifyCanExecuteChanged();
        AgregarLoteCommand.NotifyCanExecuteChanged();
        QuitarLoteCommand.NotifyCanExecuteChanged();
        QuitarAnimalCommand.NotifyCanExecuteChanged();
        EditarSalidaCommand.NotifyCanExecuteChanged();
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
    }

    /// <summary>El Oficio de Venta pertenece a un solo programa; los bovinos pertenecen a Ganadería.</summary>
    private async Task<int> ProgramaDeGanaderiaAsync()
    {
        if (_programaId != 0)
        {
            return _programaId;
        }

        var programas = await _catalogo.GetProgramasAsync();
        _programaId = programas.FirstOrDefault(p => p.Codigo == NombresBase.ProgramaGanaderia)?.Id
            ?? programas.First(p => p.TipoInventario == TipoInventario.Individual && p.ManejaInventarioAnimal).Id;
        return _programaId;
    }

    private void CargarFilas(IReadOnlyList<AnimalSeleccionable> animales, Dictionary<int, VentaAnimalDto>? detalleVenta)
    {
        Animales.Clear();
        foreach (var animal in animales)
        {
            var detalle = detalleVenta is not null && detalleVenta.TryGetValue(animal.Id, out var d) ? d : null;
            Animales.Add(new SalidaAnimalFila(animal, detalle?.PrecioVenta ?? 0m, detalle?.ObservacionesEspecificas, detalle?.PesoKg));
        }
    }

    private void AlCambiarAnimales(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (SalidaAnimalFila fila in e.NewItems)
            {
                fila.PropertyChanged += AlCambiarFila;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (SalidaAnimalFila fila in e.OldItems)
            {
                fila.PropertyChanged -= AlCambiarFila;
            }
        }

        NotificarTotales();
    }

    private void AlCambiarFila(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SalidaAnimalFila.PrecioVenta))
        {
            NotificarTotales();
        }
    }

    private void NotificarTotales()
    {
        OnPropertyChanged(nameof(CantidadTexto));
        OnPropertyChanged(nameof(TotalTexto));
        OnPropertyChanged(nameof(EtiquetaTotal));
    }

    private static string? Vacio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
