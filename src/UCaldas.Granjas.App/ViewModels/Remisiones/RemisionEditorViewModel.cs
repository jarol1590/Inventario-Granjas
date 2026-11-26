using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Comercial;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Reportes;

namespace UCaldas.Granjas.App.ViewModels.Remisiones;

/// <summary>
/// Una línea de subproducto escrita a mano (leche, huevos, gallinaza, fruta...). La descripción es libre; si coincide con un
/// producto del catálogo del programa, la línea queda ligada a él y propone su unidad y su precio.
/// </summary>
public sealed partial class LineaEditable : ObservableObject
{
    private bool _aplicando;

    public LineaEditable()
    {
        _descripcion = string.Empty;
        _unidad = "Unidad";
        _cantidad = 1m;
    }

    /// <summary>Línea ya registrada: conserva sus datos sin proponer los del catálogo.</summary>
    public LineaEditable(CatalogoProducto? producto, string descripcion, string unidad, decimal cantidad, decimal valorUnitario)
    {
        _producto = producto;
        _descripcion = descripcion;
        _unidad = unidad;
        _cantidad = cantidad;
        _valorUnitario = valorUnitario;
    }

    /// <summary>Producto del catálogo elegido en la sugerencia; nulo = ítem libre.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProductoId), nameof(Codigo))]
    private CatalogoProducto? _producto;

    [ObservableProperty]
    private string _descripcion;

    [ObservableProperty]
    private string _unidad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValorTotal))]
    private decimal _cantidad;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValorTotal))]
    private decimal _valorUnitario;

    public int? ProductoId => Producto?.Id;

    public string Codigo => Producto?.Codigo ?? "—";

    public decimal ValorTotal => Math.Round(Cantidad * ValorUnitario, 2, MidpointRounding.AwayFromZero);

    partial void OnProductoChanged(CatalogoProducto? value)
    {
        if (value is null || _aplicando)
        {
            return;
        }

        _aplicando = true;
        try
        {
            Descripcion = value.Nombre;
            Unidad = value.UnidadMedida;
            ValorUnitario = value.PrecioUnitario;
        }
        finally
        {
            _aplicando = false;
        }
    }

    partial void OnDescripcionChanged(string value)
    {
        // Si el usuario reescribe la descripción, la línea deja de ser el producto del catálogo (queda como ítem libre).
        if (!_aplicando && Producto is { } p && !string.Equals(p.Nombre, value?.Trim(), StringComparison.CurrentCultureIgnoreCase))
        {
            Producto = null;
        }
    }

    public LineaRemision ParaMostrar() => new(
        ClaseDeLinea.Producto, null, null, ProductoId, Codigo, Descripcion.Trim(), Unidad.Trim(), Cantidad, ValorUnitario, ValorTotal, null, 0m, null, null, null);
}

/// <summary>
/// Remisión de venta de subproductos del módulo Remisiones (v2.9): oficio abierto (fija el programa), comprador (convenio u otro)
/// y una tabla de ítems escritos a mano con sugerencias del catálogo. Al continuar abre el Recibo de Caja SP y guarda remisión
/// y recibo en una sola transacción. Las ventas y traslados de animales o lotes se hacen en Sistema Granjas. Después de guardada
/// se consulta, se edita, se anula o se imprime.
/// </summary>
public sealed partial class RemisionEditorViewModel : DialogViewModelBase
{
    /// <summary>Unidades que se sugieren en la tabla (la celda admite cualquier otra).</summary>
    public static IReadOnlyList<string> UnidadesSugeridas { get; } = ["Unidad", "Kg", "Litro", "Cubeta", "Bulto", "Docena", "Canastilla", "Caja", "Tonelada", "Manojo"];

    private readonly IKardexService _kardex;
    private readonly IRemisionesQueryService _consultas;
    private readonly IOficioService _oficios;
    private readonly ICatalogoService _catalogo;
    private readonly ICatalogoComercialService _comercial;
    private readonly IFormulariosRemisiones _formulariosRemisiones;
    private readonly IPdfDocumentService _pdf;
    private readonly ContextoGlobal _contexto;
    private bool _cargando;
    private int? _remisionId;
    private ReciboCaja? _recibo;
    private string _reciboEtiqueta = string.Empty;

    public RemisionEditorViewModel(
        IKardexService kardex,
        IRemisionesQueryService consultas,
        IOficioService oficios,
        ICatalogoService catalogo,
        ICatalogoComercialService comercial,
        IFormulariosRemisiones formulariosRemisiones,
        IPdfDocumentService pdf,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _kardex = kardex;
        _consultas = consultas;
        _oficios = oficios;
        _catalogo = catalogo;
        _comercial = comercial;
        _formulariosRemisiones = formulariosRemisiones;
        _pdf = pdf;
        _contexto = contexto;
        _fechaRemision = reloj.Hoy;
        Lineas.CollectionChanged += LineasCambiadas;
    }

    public override string Titulo => Modo switch
    {
        ModoDocumento.Nuevo => "Nueva remisión de subproductos",
        ModoDocumento.Detalle => $"Detalle de la remisión {NumeroRemision}",
        _ => $"Editar la remisión {NumeroRemision}",
    };

    public override double Ancho => 1000;

    // ---- Estado del formulario ---------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo), nameof(EsLectura), nameof(EsEditable), nameof(EsNueva), nameof(PuedeEditarODesechar))]
    private ModoDocumento _modo = ModoDocumento.Nuevo;

    public bool EsLectura => Modo == ModoDocumento.Detalle;

    public bool EsEditable => Modo != ModoDocumento.Detalle;

    public bool EsNueva => Modo == ModoDocumento.Nuevo;

    public bool PuedeEditarODesechar => EsLectura && !Anulada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeEditarODesechar), nameof(EstadoTexto))]
    private bool _anulada;

    public string EstadoTexto => Anulada ? "ANULADA" : _remisionId is null ? string.Empty : EstadoRemisionTexto;

    private string EstadoRemisionTexto { get; set; } = string.Empty;

    [ObservableProperty]
    private string _numeroRemision = string.Empty;

    [ObservableProperty]
    private DateTime? _fechaRemision;

    [ObservableProperty]
    private string _observaciones = string.Empty;

    // ---- Oficio (programa) y granja ------------------------------------------------------------------------------------------

    public ObservableCollection<OficioResumen> Oficios { get; } = [];

    public ObservableCollection<Granja> Granjas { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgramaNombre))]
    private OficioResumen? _oficioSeleccionado;

    /// <summary>El programa de la remisión es el del oficio.</summary>
    public int? ProgramaId => OficioSeleccionado?.Oficio.ProgramaId;

    public string ProgramaNombre => OficioSeleccionado?.ProgramaNombre ?? "—";

    [ObservableProperty]
    private Granja? _granjaOrigen;

    // ---- Comprador --------------------------------------------------------------------------------------------------------------

    public ObservableCollection<Convenio> Convenios { get; } = [];

    public ObservableCollection<Cliente> Clientes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsOtro))]
    private bool _esConvenio = true;

    public bool EsOtro
    {
        get => !EsConvenio;
        set => EsConvenio = !value;
    }

    [ObservableProperty]
    private Convenio? _convenio;

    [ObservableProperty]
    private Cliente? _cliente;

    // ---- Líneas ------------------------------------------------------------------------------------------------------------------

    public ObservableCollection<LineaEditable> Lineas { get; } = [];

    /// <summary>Productos del catálogo del programa del oficio: sugerencias para la descripción.</summary>
    public ObservableCollection<CatalogoProducto> ProductosDisponibles { get; } = [];

    public string CantidadTexto => $"Ítems: {Lineas.Count}";

    public string TotalTexto => $"Total remisión: {Formato.Dinero(Lineas.Sum(l => l.ValorTotal))}";

    public string ReciboTexto => _recibo is { } r
        ? $"Recibo de caja {r.NumeroRecibo} {r.Prefijo}" + (r.Anulado ? " (anulado)" : string.Empty)
        : "El recibo de caja SP se genera al continuar: recibirá el siguiente consecutivo.";

    private void LineasCambiadas(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var linea in e.OldItems?.OfType<LineaEditable>() ?? [])
        {
            linea.PropertyChanged -= LineaCambiada;
        }

        foreach (var linea in e.NewItems?.OfType<LineaEditable>() ?? [])
        {
            linea.PropertyChanged += LineaCambiada;
        }

        RefrescarTotales();
    }

    private void LineaCambiada(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LineaEditable.ValorTotal))
        {
            RefrescarTotales();
        }
    }

    private void RefrescarTotales()
    {
        OnPropertyChanged(nameof(CantidadTexto));
        OnPropertyChanged(nameof(TotalTexto));
    }

    // ---- Inicialización -----------------------------------------------------------------------------------------------------------

    public async Task<bool> InicializarNuevaAsync(int? oficioId = null)
    {
        return await EjecutarAsync(async () =>
        {
            _cargando = true;
            try
            {
                await CargarCatalogosAsync(null);
                var siguientes = await _consultas.SiguientesConsecutivosAsync();
                NumeroRemision = siguientes.Remision;
                GranjaOrigen = Granjas.FirstOrDefault(g => g.Id == _contexto.GranjaId) ?? Granjas.FirstOrDefault();
                OficioSeleccionado = Oficios.FirstOrDefault(o => o.Oficio.Id == oficioId)
                    ?? Oficios.FirstOrDefault(o => o.Oficio.ProgramaId == _contexto.ProgramaId) ?? Oficios.FirstOrDefault();
                Convenio = Convenios.FirstOrDefault();
                Cliente = Clientes.FirstOrDefault();
                EsConvenio = Convenios.Count > 0;
                Modo = ModoDocumento.Nuevo;
            }
            finally
            {
                _cargando = false;
            }

            await RecargarProductosAsync();
            if (Lineas.Count == 0)
            {
                Lineas.Add(new LineaEditable());
            }
        }, "preparar la remisión");
    }

    public async Task<bool> InicializarExistenteAsync(int remisionId)
    {
        return await EjecutarAsync(async () =>
        {
            var vista = await _consultas.ObtenerAsync(remisionId);
            Domain.Regla.Exigir(vista.Clase == "PRODUCTOS", $"La remisión {vista.Remision.NumeroRemision} es de animales o lotes: se consulta y edita en Sistema Granjas.");
            _remisionId = remisionId;
            _recibo = vista.Recibo;
            _descuentoActual = vista.Remision.DescuentoPorcentaje;
            _cargando = true;
            try
            {
                await CargarCatalogosAsync(vista.Oficio?.Id);
                var r = vista.Remision;
                NumeroRemision = r.NumeroRemision;
                FechaRemision = r.FechaRemision;
                Observaciones = r.Observaciones ?? string.Empty;
                Anulada = r.Anulada;
                EstadoRemisionTexto = vista.Estado;
                GranjaOrigen = Granjas.FirstOrDefault(g => g.Id == r.GranjaOrigenId);
                OficioSeleccionado = Oficios.FirstOrDefault(o => o.Oficio.Id == r.OficioId);
                EsConvenio = r.TipoComprador != TipoComprador.Otro;
                Convenio = Convenios.FirstOrDefault(c => c.Id == r.ConvenioId) ?? Convenios.FirstOrDefault();
                Cliente = Clientes.FirstOrDefault(c => c.Id == r.ClienteId) ?? Clientes.FirstOrDefault();
                Modo = ModoDocumento.Detalle;
            }
            finally
            {
                _cargando = false;
            }

            await RecargarProductosAsync();
            Lineas.Clear();
            foreach (var l in vista.Lineas)
            {
                var producto = l.ProductoId is { } pid ? ProductosDisponibles.FirstOrDefault(p => p.Id == pid) : null;
                Lineas.Add(new LineaEditable(producto, l.Descripcion, l.Unidad, l.Cantidad, l.ValorUnitario));
            }

            OnPropertyChanged(nameof(ReciboTexto));
            OnPropertyChanged(nameof(EstadoTexto));
        }, "consultar la remisión");
    }

    private async Task CargarCatalogosAsync(int? oficioActualId)
    {
        Granjas.Clear();
        foreach (var g in await _catalogo.GetGranjasAsync(soloActivas: true))
        {
            Granjas.Add(g);
        }

        // Un solo listado de oficios para los dos módulos: solo los abiertos admiten remisiones nuevas.
        Oficios.Clear();
        foreach (var o in await _oficios.ListarAsync())
        {
            if (!o.Oficio.Cerrado || o.Oficio.Id == oficioActualId)
            {
                Oficios.Add(o);
            }
        }

        Convenios.Clear();
        foreach (var c in await _comercial.ListarConveniosAsync(soloActivos: true))
        {
            Convenios.Add(c);
        }

        Clientes.Clear();
        foreach (var c in await _comercial.ListarClientesAsync(soloActivos: true))
        {
            Clientes.Add(c);
        }
    }

    partial void OnOficioSeleccionadoChanged(OficioResumen? value)
    {
        if (!_cargando)
        {
            _ = EjecutarAsync(RecargarProductosAsync, "consultar el catálogo del programa");
        }
    }

    /// <summary>Catálogo del programa del oficio (sugerencias). Una línea ligada a un producto de otro programa queda como ítem libre.</summary>
    private async Task RecargarProductosAsync()
    {
        ProductosDisponibles.Clear();
        if (ProgramaId is { } programa)
        {
            foreach (var p in await _comercial.ListarProductosAsync(programa, soloActivos: true))
            {
                ProductosDisponibles.Add(p);
            }
        }

        foreach (var linea in Lineas.Where(l => l.Producto is { } p && p.ProgramaId != ProgramaId))
        {
            linea.Producto = null;
        }
    }

    // ---- Agregar y quitar líneas --------------------------------------------------------------------------------------------------

    [RelayCommand]
    private void AgregarLinea() => Lineas.Add(new LineaEditable());

    [RelayCommand]
    private void Quitar(LineaEditable? linea)
    {
        if (linea is not null)
        {
            Lineas.Remove(linea);
        }
    }

    [RelayCommand]
    private async Task NuevoClienteAsync()
    {
        if (await _formulariosRemisiones.NuevoClienteAsync() is { } cliente)
        {
            Clientes.Add(cliente);
            Cliente = cliente;
            EsConvenio = false;
        }
    }

    // ---- Continuar al recibo y guardar ------------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task ContinuarAReciboAsync()
    {
        // Las filas que quedaron totalmente en blanco no cuentan.
        foreach (var vacia in Lineas.Where(EsVacia).ToList())
        {
            Lineas.Remove(vacia);
        }

        if (!Validar())
        {
            if (Lineas.Count == 0)
            {
                Lineas.Add(new LineaEditable());
            }

            return;
        }

        var siguientes = _recibo is null ? await _consultas.SiguientesConsecutivosAsync() : default;
        _reciboEtiqueta = _recibo is null ? $"{siguientes.Recibo} SP" : $"{_recibo.NumeroRecibo} {_recibo.Prefijo}";
        var (comprador, nit) = DatosDelComprador();
        var contexto = new ReciboContexto(
            true,
            _reciboEtiqueta,
            NumeroRemision.Trim(),
            ProgramaNombre,
            comprador,
            nit,
            [.. Lineas.Select(l => l.ParaMostrar())],
            Lineas.Sum(l => l.ValorTotal),
            0m,
            _recibo is null ? null : new ReciboCajaDto
            {
                Fecha = _recibo.Fecha, FormaPago = _recibo.FormaPago, BancoConsignacion = _recibo.BancoConsignacion, EsEfectivo = _recibo.EsEfectivo,
                Cheque1Num = _recibo.Cheque1Num, Cheque2Num = _recibo.Cheque2Num, Observaciones = _recibo.Observaciones,
                DescuentoPorcentaje = _descuentoActual, TotalPagado = _recibo.TotalPagado,
            });

        if (await _formulariosRemisiones.CapturarReciboAsync(contexto) is not { } captura)
        {
            return;
        }

        if (await EjecutarAsync(() => GuardarAsync(captura.Recibo), "guardar la remisión y su recibo"))
        {
            Dialogos.Informar($"La remisión {NumeroRemision.Trim()} y su recibo de caja {_reciboEtiqueta} quedaron guardados.", "Remisión guardada");
            Cerrar(true);
        }
    }

    private decimal _descuentoActual;

    private static bool EsVacia(LineaEditable l)
        => string.IsNullOrWhiteSpace(l.Descripcion) && l.Producto is null && l.ValorUnitario == 0m;

    private bool Validar()
    {
        string? error = null;
        if (OficioSeleccionado is null)
        {
            error = "Seleccione el oficio de la venta (o cree uno en la sección Oficios).";
        }
        else if (string.IsNullOrWhiteSpace(NumeroRemision))
        {
            error = "Indique el número de la remisión.";
        }
        else if (FechaRemision is null)
        {
            error = "Indique la fecha de la remisión.";
        }
        else if (GranjaOrigen is null)
        {
            error = "Seleccione la granja de origen.";
        }
        else if (EsConvenio ? Convenio is null : Cliente is null)
        {
            error = EsConvenio ? "Seleccione el convenio comprador." : "Seleccione o registre el cliente comprador.";
        }
        else if (Lineas.Count == 0)
        {
            error = "Agregue al menos un ítem a la remisión.";
        }
        else if (Lineas.Any(l => string.IsNullOrWhiteSpace(l.Descripcion)))
        {
            error = "Escriba la descripción de cada ítem vendido.";
        }
        else if (Lineas.Any(l => string.IsNullOrWhiteSpace(l.Unidad)))
        {
            error = "Indique la unidad de medida de cada ítem.";
        }
        else if (Lineas.Any(l => l.Cantidad <= 0))
        {
            error = "La cantidad de cada ítem debe ser mayor que cero.";
        }
        else if (Lineas.Any(l => l.ValorUnitario <= 0))
        {
            error = "Cada ítem vendido debe tener un valor unitario mayor que cero.";
        }

        if (error is not null)
        {
            Dialogos.Advertir(error);
            return false;
        }

        return true;
    }

    private (string Nombre, string? Nit) DatosDelComprador()
        => EsConvenio ? (Convenio!.Nombre, Convenio.Nit) : (Cliente!.NombreRazonSocial, Cliente.NitCc);

    private DatosComercialesDto Comercial()
        => EsConvenio
            ? new DatosComercialesDto { TipoComprador = TipoComprador.Convenio, ConvenioId = Convenio?.Id }
            : new DatosComercialesDto { TipoComprador = TipoComprador.Otro, ClienteId = Cliente?.Id };

    private async Task GuardarAsync(ReciboCajaDto recibo)
    {
        var editando = Modo == ModoDocumento.Edicion && _remisionId is not null;
        var dto = new RemisionProductosDto
        {
            RemisionId = editando ? _remisionId : null,
            Tipo = TipoRemision.Venta,
            ProgramaId = ProgramaId!.Value,
            NumeroOficio = OficioSeleccionado!.Oficio.NumeroOficio,
            NumeroRemision = NumeroRemision.Trim(),
            Fecha = FechaRemision!.Value.Date,
            GranjaOrigenId = GranjaOrigen!.Id,
            Comprador = DatosDelComprador().Nombre,
            Observaciones = string.IsNullOrWhiteSpace(Observaciones) ? null : Observaciones.Trim(),
            Comercial = Comercial(),
            Recibo = recibo,
            Lineas = [.. Lineas.Select(l => new LineaProductoDto
            {
                ProductoId = l.ProductoId, Descripcion = l.Descripcion.Trim(), UnidadMedida = l.Unidad.Trim(), Cantidad = l.Cantidad, ValorUnitario = l.ValorUnitario,
            })],
        };
        _ = editando ? await _kardex.EditarRemisionProductosAsync(dto) : await _kardex.RegistrarRemisionProductosAsync(dto);
    }

    // ---- Consulta: editar, anular, imprimir -----------------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(PuedeEditarODesechar))]
    private void Editar() => Modo = ModoDocumento.Edicion;

    [RelayCommand]
    private async Task AnularAsync()
    {
        if (_remisionId is not { } id)
        {
            return;
        }

        var motivo = Dialogos.Confirmar(
                $"Se anulará la remisión {NumeroRemision}" + (_recibo is null ? string.Empty : $" y su recibo de caja {_recibo.NumeroRecibo} {_recibo.Prefijo}") +
                ". Los números quedan consumidos.\n\n¿Desea continuar?",
                "Anular remisión", destructivo: true)
            ? Dialogos.Pedir("Anular remisión", "Motivo de la anulación:")
            : null;
        if (motivo is null)
        {
            return;
        }

        if (await EjecutarAsync(() => _kardex.AnularAsync(id, motivo), "anular la remisión"))
        {
            Cerrar(true);
        }
    }

    [RelayCommand]
    private async Task ImprimirRemisionAsync()
    {
        if (_remisionId is { } id)
        {
            await ImprimirAsync(id, recibo: false);
        }
    }

    [RelayCommand]
    private async Task ImprimirReciboAsync()
    {
        if (_remisionId is { } id)
        {
            await ImprimirAsync(id, recibo: true);
        }
    }

    private async Task ImprimirAsync(int remisionId, bool recibo)
    {
        var nombre = recibo ? $"Recibo_{_recibo?.NumeroRecibo}_SP.pdf" : $"Remision_{NumeroRemision}.pdf";
        var ruta = Dialogos.GuardarArchivo(recibo ? "Guardar el recibo de caja" : "Guardar la remisión", "Documento PDF (*.pdf)|*.pdf", nombre);
        if (ruta is null)
        {
            return;
        }

        string? generado = null;
        if (await EjecutarAsync(
                async () => generado = recibo ? await _pdf.GenerarReciboAsync(remisionId, ruta) : await _pdf.GenerarRemisionAsync(remisionId, ruta),
                "generar el PDF") && generado is not null)
        {
            Dialogos.Abrir(generado);
        }
    }

    [RelayCommand]
    private async Task EditarReciboAsync()
    {
        if (_remisionId is { } id && await _formulariosRemisiones.EditarReciboAsync(id))
        {
            Cerrar(true);
        }
    }

    partial void OnModoChanged(ModoDocumento value) => EditarCommand.NotifyCanExecuteChanged();
}
