using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.App.ViewModels.Lotes;

/// <summary>Registro de un lote nuevo: compra a proveedor o nacimiento / cría propia.</summary>
public sealed partial class NuevoLoteViewModel : DialogViewModelBase
{
    private readonly ILoteService _lotes;
    private readonly ICatalogoService _catalogo;
    private readonly ITarifaProgramaService _tarifas;
    private readonly Programa _programa;
    private readonly int? _granjaInicialId;
    private int _version;

    public NuevoLoteViewModel(
        ILoteService lotes, ICatalogoService catalogo, ITarifaProgramaService tarifas, IReloj reloj, IDialogService dialogos, Programa programa,
        int? granjaInicialId = null)
        : base(dialogos)
    {
        _granjaInicialId = granjaInicialId;
        _lotes = lotes;
        _catalogo = catalogo;
        _tarifas = tarifas;
        _programa = programa;
        _fechaIngreso = reloj.Hoy;
    }

    public override string Titulo => $"Nuevo lote — {_programa.Nombre}";

    public override double Ancho => 760;

    public ObservableCollection<OpcionFiltro> Granjas { get; } = [];

    public ObservableCollection<OpcionFiltro> Etapas { get; } = [];

    /// <summary>Líneas / razas activas del programa del lote (catálogo por programa).</summary>
    public ObservableCollection<string> RazasDisponibles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsNacimiento))]
    private bool _esCompra = true;

    public bool EsNacimiento
    {
        get => !EsCompra;
        set => EsCompra = !value;
    }

    [ObservableProperty]
    private OpcionFiltro? _granjaSeleccionada;

    [ObservableProperty]
    private OpcionFiltro? _etapaSeleccionada;

    [ObservableProperty]
    private string _codigoLote = string.Empty;

    [ObservableProperty]
    private string _ubicacion = string.Empty;

    [ObservableProperty]
    private string _lineaRaza = string.Empty;

    [ObservableProperty]
    private DateTime? _fechaIngreso;

    /// <summary>Marcada = estanque sin conteo de individuos (las cantidades no aplican).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LlevaConteo))]
    private bool _sinControlNumerico;

    public bool LlevaConteo => !SinControlNumerico;

    [ObservableProperty]
    private int _cantidad;

    [ObservableProperty]
    private decimal _precioUnitario;

    [ObservableProperty]
    private string _factura = string.Empty;

    [ObservableProperty]
    private string _ordenCompra = string.Empty;

    [ObservableProperty]
    private string _proveedor = string.Empty;

    [ObservableProperty]
    private string _origenReferencia = string.Empty;

    [ObservableProperty]
    private string _observaciones = string.Empty;

    [ObservableProperty]
    private string _valorInicialTexto = "—";

    public async Task<bool> InicializarAsync()
        => await EjecutarAsync(async () =>
        {
            var idsGranjas = (await _catalogo.GetGranjaIdsDeProgramaAsync(_programa.Id)).ToHashSet();
            foreach (var g in (await _catalogo.GetGranjasAsync(soloActivas: true)).Where(g => idsGranjas.Contains(g.Id)))
            {
                Granjas.Add(new OpcionFiltro(g.Id, g.Nombre));
            }

            foreach (var c in await _catalogo.GetCategoriasDeProgramaAsync(_programa.Id))
            {
                Etapas.Add(new OpcionFiltro(c.Id, c.Nombre));
            }

            await CargarRazasAsync();
            GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == _granjaInicialId) ?? Granjas.FirstOrDefault();
            EtapaSeleccionada = Etapas.FirstOrDefault();
            await ActualizarValorAsync();
        }, "preparar el formulario de lotes");

    /// <summary>Carga las líneas / razas del programa del lote y deja elegida la opción por defecto.</summary>
    private async Task CargarRazasAsync()
    {
        RazasDisponibles.Clear();
        foreach (var r in await _catalogo.GetRazasDeProgramaAsync(_programa.Id))
        {
            RazasDisponibles.Add(r.Nombre);
        }

        if (!RazasDisponibles.Contains(LineaRaza, StringComparer.OrdinalIgnoreCase))
        {
            LineaRaza = RazasDisponibles.FirstOrDefault() ?? string.Empty;
        }
    }

    partial void OnEtapaSeleccionadaChanged(OpcionFiltro? value) => _ = ActualizarValorAsync();

    partial void OnEsCompraChanged(bool value) => _ = ActualizarValorAsync();

    private async Task ActualizarValorAsync()
    {
        var version = ++_version;
        if (EsCompra || EtapaSeleccionada?.Id is not { } etapa)
        {
            ValorInicialTexto = "El valor inicial es el precio de factura.";
            return;
        }

        var valor = await _tarifas.ValorDeReferenciaAsync(etapa);
        if (version == _version)
        {
            ValorInicialTexto = valor == 0m ? "$0 (la etapa no tiene valor de referencia)" : $"{Formato.Dinero(valor)} por cabeza (valor de referencia de la etapa)";
        }
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        if (GranjaSeleccionada?.Id is not { } granja || EtapaSeleccionada?.Id is not { } etapa)
        {
            Dialogos.Advertir("Seleccione la granja y la etapa del lote.");
            return;
        }

        var guardado = await EjecutarAsync(() => _lotes.RegistrarNuevoLoteAsync(new NuevoLoteDto
        {
            ProgramaId = _programa.Id,
            GranjaId = granja,
            EtapaId = etapa,
            ClaseEntrada = EsCompra ? ClaseEntrada.Compra : ClaseEntrada.Nacimiento,
            CodigoLote = CodigoLote,
            Ubicacion = Ubicacion,
            LineaRaza = LineaRaza,
            FechaIngreso = FechaIngreso ?? default,
            ControlNumericoPoblacion = !SinControlNumerico,
            Cantidad = Cantidad,
            PrecioUnitarioFactura = PrecioUnitario,
            Factura = Factura,
            OrdenCompra = OrdenCompra,
            Proveedor = Proveedor,
            OrigenReferencia = OrigenReferencia,
            Observaciones = Observaciones,
        }), "registrar el lote");

        if (guardado)
        {
            Dialogos.Informar($"Lote {CodigoLote.Trim()} registrado.", "Nuevo lote");
            Cerrar(true);
        }
    }
}

/// <summary>Promoción de etapa o desdoble de un lote.</summary>
public sealed partial class CambioEtapaDesdobleViewModel : DialogViewModelBase
{
    private readonly ILoteService _lotes;
    private readonly ICatalogoService _catalogo;
    private readonly LoteDetalle _lote;

    public CambioEtapaDesdobleViewModel(ILoteService lotes, ICatalogoService catalogo, IDialogService dialogos, LoteDetalle lote)
        : base(dialogos)
    {
        _lotes = lotes;
        _catalogo = catalogo;
        _lote = lote;
        _ubicacionNueva = lote.Lote.Ubicacion;
    }

    public override string Titulo => $"Cambiar etapa / desdoblar — {_lote.Lote.CodigoLote}";

    public override double Ancho => 640;

    public string Resumen => $"Lote {_lote.Lote.CodigoLote} · Etapa actual: {_lote.EtapaNombre} · Ubicación: {_lote.Lote.Ubicacion} · " +
        (_lote.Lote.ControlNumericoPoblacion ? $"{_lote.Lote.CantidadActual:N0} cabezas" : "sin control numérico");

    /// <summary>El desdoble por cantidades solo existe en lotes con conteo.</summary>
    public bool PuedeDesdoblar => _lote.Lote.ControlNumericoPoblacion;

    public ObservableCollection<OpcionFiltro> Etapas { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsDesdoble))]
    private bool _esPromocion = true;

    public bool EsDesdoble
    {
        get => !EsPromocion;
        set => EsPromocion = !value;
    }

    [ObservableProperty]
    private OpcionFiltro? _etapaDestino;

    [ObservableProperty]
    private string _ubicacionNueva;

    [ObservableProperty]
    private int _cantidad;

    [ObservableProperty]
    private string _codigoNuevoLote = string.Empty;

    public async Task<bool> InicializarAsync()
        => await EjecutarAsync(async () =>
        {
            foreach (var c in await _catalogo.GetCategoriasDeProgramaAsync(_lote.Lote.ProgramaId))
            {
                Etapas.Add(new OpcionFiltro(c.Id, c.Nombre));
            }

            EtapaDestino = Etapas.FirstOrDefault(e => e.Id == _lote.Lote.EtapaId);
        }, "preparar la ventana");

    [RelayCommand]
    private async Task GuardarAsync()
    {
        if (EsPromocion)
        {
            if (EtapaDestino?.Id is not { } etapa)
            {
                Dialogos.Advertir("Seleccione la etapa destino.");
                return;
            }

            CambioEtapaResultado? resultado = null;
            if (!await EjecutarAsync(async () => resultado = await _lotes.CambiarEtapaAsync(_lote.Lote.Id, etapa, UbicacionNueva), "cambiar la etapa") || resultado is null)
            {
                return;
            }

            var mensaje = $"Etapa actualizada.\n\nValor del lote: {Formato.Dinero(resultado.ValorAnteriorLote)} → {Formato.Dinero(resultado.ValorNuevoLote)}\n" +
                          $"Ajuste de valorización: {Formato.DineroConSigno(resultado.Delta)}";
            if (resultado.SinValorDeReferencia)
            {
                mensaje += "\n\nLa etapa no tiene valor de referencia: el lote conserva su valor. Defínalo en Configuración › Tarifas.";
            }

            Dialogos.Informar(mensaje, "Cambio de etapa");
            Cerrar(true);
            return;
        }

        if (await EjecutarAsync(() => _lotes.DesdoblarAsync(_lote.Lote.Id, Cantidad, CodigoNuevoLote, UbicacionNueva), "desdoblar el lote"))
        {
            Dialogos.Informar($"Se crearon el lote {CodigoNuevoLote.Trim()} con {Cantidad:N0} cabezas; el saldo financiero total no cambia.", "Desdoble");
            Cerrar(true);
        }
    }
}

/// <summary>Registro rápido de mortandad, hurto o descarte sanitario.</summary>
public sealed partial class BajaLoteViewModel : DialogViewModelBase
{
    private readonly ILoteService _lotes;
    private readonly LoteDetalle _lote;

    public BajaLoteViewModel(ILoteService lotes, IReloj reloj, IDialogService dialogos, LoteDetalle lote)
        : base(dialogos)
    {
        _lotes = lotes;
        _lote = lote;
        _fecha = reloj.Hoy;
        _causa = LoteService.CausasDeBaja[0];
    }

    public override string Titulo => $"Mortandad / baja — {_lote.Lote.CodigoLote}";

    public override double Ancho => 560;

    public IReadOnlyList<string> Causas => LoteService.CausasDeBaja;

    public string Resumen => $"Lote {_lote.Lote.CodigoLote} · {_lote.Lote.CantidadActual:N0} cabezas vivas · {Formato.Dinero(_lote.Lote.ValorContableUnitario)} por cabeza";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaldoTexto))]
    private int _cantidad;

    [ObservableProperty]
    private string _causa;

    [ObservableProperty]
    private DateTime? _fecha;

    [ObservableProperty]
    private string _observaciones = string.Empty;

    /// <summary>Recálculo instantáneo del saldo y del valor tras la baja.</summary>
    public string SaldoTexto
    {
        get
        {
            var quedan = _lote.Lote.CantidadActual - Cantidad;
            return Cantidad <= 0 || quedan < 0
                ? "—"
                : $"Quedarán {quedan:N0} cabezas · {Formato.Dinero(quedan * _lote.Lote.ValorContableUnitario)}" + (quedan == 0 ? " · el lote pasará a Finalizado" : string.Empty);
        }
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        if (await EjecutarAsync(() => _lotes.RegistrarBajaAsync(_lote.Lote.Id, Cantidad, Causa, Fecha ?? default, Observaciones), "registrar la baja"))
        {
            Cerrar(true);
        }
    }
}

/// <summary>
/// Ficha de un lote: se corrigen sus datos descriptivos y su población, etapa, granja y valor contable, como en la ficha del
/// animal; el valor de compra, la entrada y lo que se calcula (población actual, total y estado) son de solo lectura.
/// </summary>
public sealed partial class FichaLoteViewModel : DialogViewModelBase
{
    private readonly ILoteService _lotes;
    private readonly ICatalogoService _catalogo;
    private readonly int _loteId;
    private LoteDetalle _detalle;
    private bool _cargando;

    public FichaLoteViewModel(ILoteService lotes, ICatalogoService catalogo, IDialogService dialogos, LoteDetalle detalle)
        : base(dialogos)
    {
        _lotes = lotes;
        _catalogo = catalogo;
        _detalle = detalle;
        _loteId = detalle.Lote.Id;
        CerrarCommand = new RelayCommand(() => Cerrar(HuboCambios));
        Cargar();
    }

    private LotePoblacion Lote => _detalle.Lote;

    public override string Titulo => $"Ficha del lote {Lote.CodigoLote}";

    public override double Ancho => 720;

    /// <summary>true si se guardaron correcciones: la pantalla que abrió la ficha debe refrescarse.</summary>
    public bool HuboCambios { get; private set; }

    public IRelayCommand CerrarCommand { get; }

    public ObservableCollection<OpcionFiltro> Granjas { get; } = [];

    public ObservableCollection<OpcionFiltro> Etapas { get; } = [];

    /// <summary>Líneas / razas activas del programa del lote (más la actual si es antigua y ya no está en el catálogo).</summary>
    public ObservableCollection<string> RazasDisponibles { get; } = [];

    /// <summary>Un lote con la compra anulada no se edita.</summary>
    public bool EsEditable => !Lote.Anulado;

    /// <summary>Proveedor y orden de compra solo existen en los lotes que entraron por compra.</summary>
    public bool EsCompra => Lote.ClaseEntrada == ClaseEntrada.Compra;

    /// <summary>Las cantidades solo se editan en lotes con control numérico de población.</summary>
    public bool LlevaConteo => Lote.ControlNumericoPoblacion;

    public bool PuedeEditarConteo => EsEditable && LlevaConteo;

    /// <summary>Carga las granjas donde corre el programa del lote y sus etapas.</summary>
    public async Task<bool> InicializarAsync()
    {
        return await EjecutarAsync(async () =>
        {
            _cargando = true;
            try
            {
                Granjas.Clear();
                foreach (var granja in await _catalogo.GetGranjasAsync(soloActivas: false))
                {
                    var esLaActual = granja.Id == Lote.GranjaId;
                    if (esLaActual || (granja.Activa
                        && (await _catalogo.GetProgramasDeGranjaAsync(granja.Id)).Any(p => p.Id == Lote.ProgramaId)))
                    {
                        Granjas.Add(new OpcionFiltro(granja.Id, granja.Nombre));
                    }
                }

                Etapas.Clear();
                foreach (var etapa in await _catalogo.GetCategoriasDeProgramaAsync(Lote.ProgramaId))
                {
                    Etapas.Add(new OpcionFiltro(etapa.Id, etapa.Nombre));
                }

                RazasDisponibles.Clear();
                foreach (var raza in await _catalogo.GetRazasDeProgramaAsync(Lote.ProgramaId))
                {
                    RazasDisponibles.Add(raza.Nombre);
                }

                if (!string.IsNullOrWhiteSpace(Lote.LineaRaza) && !RazasDisponibles.Contains(Lote.LineaRaza, StringComparer.OrdinalIgnoreCase))
                {
                    RazasDisponibles.Add(Lote.LineaRaza);
                }

                GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == Lote.GranjaId);
                EtapaSeleccionada = Etapas.FirstOrDefault(x => x.Id == Lote.EtapaId);
            }
            finally
            {
                _cargando = false;
            }

            HayCambios = false;
        }, "abrir la ficha del lote");
    }

    // ---- Datos editables --------------------------------------------------------------------------------------------

    [ObservableProperty]
    private string _codigoLote = string.Empty;

    [ObservableProperty]
    private string _ubicacion = string.Empty;

    [ObservableProperty]
    private string _lineaRaza = string.Empty;

    [ObservableProperty]
    private DateTime? _fechaIngreso;

    [ObservableProperty]
    private string _origenReferencia = string.Empty;

    [ObservableProperty]
    private string _proveedor = string.Empty;

    [ObservableProperty]
    private string _ordenCompra = string.Empty;

    [ObservableProperty]
    private string _anotaciones = string.Empty;

    [ObservableProperty]
    private OpcionFiltro? _granjaSeleccionada;

    [ObservableProperty]
    private OpcionFiltro? _etapaSeleccionada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PoblacionActualTexto), nameof(ValorTotalTexto), nameof(EstadoTexto))]
    private int _cantidadInicial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PoblacionActualTexto), nameof(ValorTotalTexto), nameof(EstadoTexto))]
    private int _bajas;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValorTotalTexto))]
    private decimal _valorContable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCambiosCommand))]
    private bool _hayCambios;

    // ---- Datos de solo lectura --------------------------------------------------------------------------------------

    public string Entrada => Lote.ClaseEntrada switch
    {
        ClaseEntrada.Compra => "Compra",
        ClaseEntrada.Nacimiento => "Nacimiento / cría propia",
        ClaseEntrada.Desdoble => "Desdoble",
        _ => Lote.ClaseEntrada,
    };

    public string ValorCompra => Formato.Dinero(Lote.ValorCompraUnitario);

    /// <summary>Población actual = la guardada ± lo que se cambie de cantidad inicial y bajas (las demás salidas se conservan).</summary>
    private int PoblacionActual => Lote.CantidadActual + (CantidadInicial - Lote.CantidadInicial) - (Bajas - Lote.MortandadAcumulada);

    public string PoblacionActualTexto => LlevaConteo ? PoblacionActual.ToString("N0", Formato.Cultura) : "N/A";

    public string ValorTotalTexto => Formato.Dinero(LlevaConteo ? PoblacionActual * ValorContable : ValorContable);

    public string EstadoTexto => Lote.Anulado ? "Compra anulada" : LlevaConteo && PoblacionActual == 0 ? EstadoLote.Finalizado : EstadoLote.Activo;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_cargando && e.PropertyName is nameof(CodigoLote) or nameof(Ubicacion) or nameof(LineaRaza) or nameof(FechaIngreso)
            or nameof(OrigenReferencia) or nameof(Proveedor) or nameof(OrdenCompra) or nameof(Anotaciones)
            or nameof(GranjaSeleccionada) or nameof(EtapaSeleccionada) or nameof(CantidadInicial) or nameof(Bajas) or nameof(ValorContable))
        {
            HayCambios = true;
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarCambiosAsync()
    {
        if (GranjaSeleccionada?.Id is not { } granjaId || EtapaSeleccionada?.Id is not { } etapaId)
        {
            Dialogos.Advertir("Seleccione la granja y la etapa del lote.");
            return;
        }

        var guardado = await EjecutarAsync(async () =>
        {
            await _lotes.ActualizarDatosAsync(new ActualizarLoteDto
            {
                LoteId = _loteId,
                CodigoLote = CodigoLote,
                Ubicacion = Ubicacion,
                LineaRaza = LineaRaza,
                FechaIngreso = FechaIngreso ?? default,
                OrigenReferencia = OrigenReferencia,
                Proveedor = Proveedor,
                OrdenCompra = OrdenCompra,
                Observaciones = Anotaciones,
                GranjaId = granjaId,
                EtapaId = etapaId,
                CantidadInicial = CantidadInicial,
                MortandadAcumulada = Bajas,
                ValorContableUnitario = ValorContable,
            });
            HuboCambios = true;
            _detalle = await _lotes.ObtenerAsync(_loteId);
            Cargar();
            _cargando = true;
            try
            {
                GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == Lote.GranjaId);
                EtapaSeleccionada = Etapas.FirstOrDefault(x => x.Id == Lote.EtapaId);
            }
            finally
            {
                _cargando = false;
            }

            HayCambios = false;
        }, "guardar los cambios del lote");

        if (guardado)
        {
            Dialogos.Informar("Los datos del lote se guardaron.", "Lote actualizado");
        }
    }

    private bool PuedeGuardar() => HayCambios && EsEditable;

    private void Cargar()
    {
        _cargando = true;
        try
        {
            CodigoLote = Lote.CodigoLote;
            Ubicacion = Lote.Ubicacion;
            LineaRaza = Lote.LineaRaza;
            FechaIngreso = Lote.FechaNacimientoIngreso;
            OrigenReferencia = Lote.OrigenReferencia ?? string.Empty;
            Proveedor = Lote.Proveedor ?? string.Empty;
            OrdenCompra = Lote.OrdenCompra ?? string.Empty;
            Anotaciones = Lote.Observaciones ?? string.Empty;
            CantidadInicial = Lote.CantidadInicial;
            Bajas = Lote.MortandadAcumulada;
            ValorContable = Lote.ValorContableUnitario;
        }
        finally
        {
            _cargando = false;
        }

        HayCambios = false;
        foreach (var nombre in new[] { nameof(Titulo), nameof(ValorCompra), nameof(PoblacionActualTexto), nameof(ValorTotalTexto), nameof(EstadoTexto) })
        {
            OnPropertyChanged(nombre);
        }
    }
}
