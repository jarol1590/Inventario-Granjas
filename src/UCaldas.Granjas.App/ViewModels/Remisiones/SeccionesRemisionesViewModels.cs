using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Comercial;
using UCaldas.Granjas.Services.Reportes;

namespace UCaldas.Granjas.App.ViewModels.Remisiones;

/// <summary>
/// Bandeja de remisiones de venta con KPI del mes, filtros y las acciones sobre la remisión seleccionada. Lista las ventas de
/// subproductos del módulo y también las de animales y lotes hechas en Sistema Granjas (estas se abren en su «Detalle de la
/// salida»). Los traslados no se listan: se manejan en Sistema Granjas.
/// </summary>
public sealed partial class RemisionesViewModel : ViewModelBase, IUsaFiltroGlobal
{
    private readonly IRemisionesQueryService _consultas;
    private readonly IFormulariosRemisiones _formularios;
    private readonly IExcelReportingEngine _excel;
    private readonly ContextoGlobal _contexto;
    private readonly IReloj _reloj;
    private int _version;

    public RemisionesViewModel(
        IRemisionesQueryService consultas,
        IFormulariosRemisiones formularios,
        IExcelReportingEngine excel,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _consultas = consultas;
        _formularios = formularios;
        _excel = excel;
        _contexto = contexto;
        _reloj = reloj;
        Rango = new FiltroMensual(reloj.Hoy);
        Rango.Cambio += () => _ = CargarAsync();
        Clases =
        [
            new(null, "Todas"), new(ClaseRemision.Productos, "Subproductos"), new(ClaseRemision.Animales, "Animales"), new(ClaseRemision.Lotes, "Lotes"),
        ];
        Estados = [new(null, "Todas"), .. EstadoRemision.Todos.Where(e => e != EstadoRemision.TrasladoInterno).Select(e => new OpcionTexto(e, e))];
        _clase = Clases[0];
        _estado = Estados[0];
    }

    public FiltroMensual Rango { get; }

    public IReadOnlyList<OpcionTexto> Clases { get; }

    public IReadOnlyList<OpcionTexto> Estados { get; }

    public ObservableCollection<RemisionFila> Remisiones { get; } = [];

    [ObservableProperty]
    private OpcionTexto _clase;

    [ObservableProperty]
    private OpcionTexto _estado;

    [ObservableProperty]
    private string _texto = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerDetalleCommand), nameof(GenerarReciboCommand), nameof(ImprimirRemisionCommand))]
    private RemisionFila? _seleccionada;

    [ObservableProperty]
    private int _remisionesDelMes;

    [ObservableProperty]
    private string _ventasDelMesTexto = Formato.Dinero(0);

    partial void OnClaseChanged(OpcionTexto value) => _ = CargarAsync();

    partial void OnEstadoChanged(OpcionTexto value) => _ = CargarAsync();

    partial void OnTextoChanged(string value) => _ = CargarAsync();

    public async Task CargarAsync()
    {
        if (Rango.Validar() is { } error)
        {
            Dialogos.Advertir(error);
            return;
        }

        var version = ++_version;
        await EjecutarAsync(async () =>
        {
            var previo = Seleccionada?.Id;
            var filas = await _consultas.ListarAsync(new RemisionFiltro(
                _contexto.GranjaId, _contexto.ProgramaId, TipoRemision.Venta, Estado.Valor, string.IsNullOrWhiteSpace(Texto) ? null : Texto,
                Rango.Desde, Rango.Hasta, IncluirAnuladas: Estado.Valor == EstadoRemision.Anulada, Clase: Clase.Valor));
            var resumen = await _consultas.ResumirAsync(_reloj.Hoy, _contexto.GranjaId, _contexto.ProgramaId);
            if (version != _version)
            {
                return;
            }

            Remisiones.Clear();
            foreach (var f in filas)
            {
                Remisiones.Add(f);
            }

            Seleccionada = Remisiones.FirstOrDefault(r => r.Id == previo);
            RemisionesDelMes = resumen.RemisionesDelMes;
            VentasDelMesTexto = Formato.Dinero(resumen.VentasDelMes);
        }, "consultar las remisiones");
    }

    private bool HaySeleccion() => Seleccionada is not null;

    [RelayCommand]
    private async Task NuevaRemisionAsync()
    {
        if (await _formularios.NuevaRemisionAsync())
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task VerDetalleAsync(RemisionFila? fila)
    {
        fila ??= Seleccionada;
        if (fila is not null && await _formularios.AbrirRemisionAsync(fila.Id))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task GenerarReciboAsync()
    {
        if (Seleccionada is not { } fila)
        {
            return;
        }

        if (fila.Anulada)
        {
            Dialogos.Advertir($"La remisión {fila.Numero} está anulada: no admite recibo.");
            return;
        }

        if (await _formularios.EditarReciboAsync(fila.Id))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task ImprimirRemisionAsync()
    {
        if (Seleccionada is not { } fila)
        {
            return;
        }

        var ruta = Dialogos.GuardarArchivo("Guardar la remisión", "Libro de Excel (*.xlsx)|*.xlsx", $"Remision_{fila.Numero}.xlsx");
        if (ruta is null)
        {
            return;
        }

        string? generado = null;
        if (await EjecutarAsync(async () => generado = await _excel.GenerarRemisionAsync(fila.Id, ruta), "generar el Excel de la remisión") && generado is not null)
        {
            Dialogos.Abrir(generado);
        }
    }
}

/// <summary>Recibos de caja SP emitidos (de toda venta: subproductos, animales y lotes): consulta, edición y reimpresión.</summary>
public sealed partial class RecibosCajaViewModel : ViewModelBase, ISeccion
{
    private readonly IRemisionesQueryService _consultas;
    private readonly IFormulariosRemisiones _formularios;
    private readonly IExcelReportingEngine _excel;
    private readonly INavegador _navegador;
    private int _version;

    public RecibosCajaViewModel(
        IRemisionesQueryService consultas,
        IFormulariosRemisiones formularios,
        IExcelReportingEngine excel,
        INavegador navegador,
        IDialogService dialogos)
        : base(dialogos)
    {
        _consultas = consultas;
        _formularios = formularios;
        _excel = excel;
        _navegador = navegador;
        Formas =
        [
            new(null, "Todas las formas"), new(FormaPago.Consignacion, "Consignación bancaria"), new(FormaPago.Transferencia, "Transferencia electrónica"),
            new(FormaPago.RecursosPropios, "Recursos propios"), new(FormaPago.EfectivoCheque, "Efectivo / Cheque (anteriores)"),
        ];
        _forma = Formas[0];
    }

    public IReadOnlyList<OpcionTexto> Formas { get; }

    public ObservableCollection<ReciboFila> Recibos { get; } = [];

    [ObservableProperty]
    private OpcionTexto _forma;

    [ObservableProperty]
    private string _texto = string.Empty;

    [ObservableProperty]
    private bool _incluirAnulados;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirReciboCommand), nameof(ReimprimirCommand))]
    private ReciboFila? _seleccionado;

    [ObservableProperty]
    private string _consignacionTexto = Formato.Dinero(0);

    [ObservableProperty]
    private string _transferenciaTexto = Formato.Dinero(0);

    [ObservableProperty]
    private string _recursosPropiosTexto = Formato.Dinero(0);

    [ObservableProperty]
    private string _ultimoTexto = "—";

    partial void OnFormaChanged(OpcionTexto value) => _ = CargarAsync();

    partial void OnTextoChanged(string value) => _ = CargarAsync();

    partial void OnIncluirAnuladosChanged(bool value) => _ = CargarAsync();

    public async Task CargarAsync()
    {
        var version = ++_version;
        await EjecutarAsync(async () =>
        {
            var previo = Seleccionado?.Recibo.Id;
            var lista = await _consultas.ListarRecibosAsync(Forma.Valor, string.IsNullOrWhiteSpace(Texto) ? null : Texto, IncluirAnulados);
            var resumen = await _consultas.ResumirRecibosAsync();
            if (version != _version)
            {
                return;
            }

            Recibos.Clear();
            foreach (var r in lista)
            {
                Recibos.Add(r);
            }

            Seleccionado = Recibos.FirstOrDefault(r => r.Recibo.Id == previo);
            ConsignacionTexto = Formato.Dinero(resumen.Consignacion);
            TransferenciaTexto = Formato.Dinero(resumen.Transferencia);
            RecursosPropiosTexto = Formato.Dinero(resumen.RecursosPropios);
            UltimoTexto = resumen.UltimoConsecutivo;
        }, "consultar los recibos de caja");
    }

    private bool HaySeleccion() => Seleccionado is not null;

    /// <summary>Un recibo nace con su remisión: la acción lleva a la bandeja de remisiones.</summary>
    [RelayCommand]
    private void NuevoRecibo() => _navegador.IrA(ShellViewModel.ClaveRemisiones);

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task AbrirReciboAsync(ReciboFila? fila)
    {
        fila ??= Seleccionado;
        if (fila is null)
        {
            return;
        }

        if (fila.Recibo.Anulado)
        {
            Dialogos.Advertir($"El recibo {fila.Etiqueta} está anulado: su remisión fue anulada.");
            return;
        }

        if (await _formularios.EditarReciboAsync(fila.Recibo.RemisionId))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task ReimprimirAsync()
    {
        if (Seleccionado is not { } fila)
        {
            return;
        }

        var ruta = Dialogos.GuardarArchivo("Guardar el recibo de caja", "Libro de Excel (*.xlsx)|*.xlsx", $"Recibo_{fila.Recibo.NumeroRecibo}_SP.xlsx");
        if (ruta is null)
        {
            return;
        }

        string? generado = null;
        if (await EjecutarAsync(async () => generado = await _excel.GenerarReciboAsync(fila.Recibo.RemisionId, ruta), "generar el Excel del recibo") && generado is not null)
        {
            Dialogos.Abrir(generado);
        }
    }
}

/// <summary>Informe de Ventas por Programas: rango de fechas y programa; solo Excel.</summary>
public sealed partial class InformesRemisionesViewModel : ViewModelBase, ISeccion
{
    private readonly IExcelReportingEngine _excel;
    private readonly ICatalogoService _catalogo;
    private readonly ContextoGlobal _contexto;
    private readonly IReloj _reloj;

    public InformesRemisionesViewModel(
        IExcelReportingEngine excel,
        ICatalogoService catalogo,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _excel = excel;
        _catalogo = catalogo;
        _contexto = contexto;
        _reloj = reloj;
        _desde = new DateTime(reloj.Hoy.Year, reloj.Hoy.Month, 1);
        _hasta = reloj.Hoy;
    }

    public ObservableCollection<OpcionFiltro> Programas { get; } = [];

    [ObservableProperty]
    private OpcionFiltro? _programa;

    [ObservableProperty]
    private DateTime? _desde;

    [ObservableProperty]
    private DateTime? _hasta;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirUltimoCommand))]
    private string? _ultimoArchivo;

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            var previo = Programa?.Id;
            Programas.Clear();
            Programas.Add(new OpcionFiltro(null, "Todos los programas"));
            foreach (var p in await _catalogo.GetProgramasAsync())
            {
                Programas.Add(new OpcionFiltro(p.Id, p.Nombre));
            }

            // Por defecto «Todos los programas» (no sigue al combo global); se conserva la elección del usuario al recargar.
            Programa = Programas.FirstOrDefault(o => previo is not null && o.Id == previo) ?? Programas[0];
        }, "preparar el informe");
    }

    [RelayCommand]
    private async Task GenerarAsync()
    {
        if (Desde is null || Hasta is null)
        {
            Dialogos.Advertir("Indique el rango de fechas del informe.");
            return;
        }

        var sufijo = $"{Desde:yyyyMMdd}_{Hasta:yyyyMMdd}";
        var ruta = Dialogos.GuardarArchivo("Guardar el informe", "Libro de Excel (*.xlsx)|*.xlsx", $"Ventas_por_Programas_{sufijo}.xlsx");
        if (ruta is null)
        {
            return;
        }

        var request = new VentasReporteRequest(Desde.Value, Hasta.Value, Programa?.Id, ruta);
        string? generado = null;
        var terminado = await EjecutarAsync(
            async () => generado = await _excel.GenerarVentasPorProgramaAsync(request),
            "generar el informe de ventas");
        if (!terminado || generado is null)
        {
            return;
        }

        UltimoArchivo = generado;
        if (Dialogos.Confirmar($"El informe se guardó en:\n\n{generado}\n\n¿Desea abrirlo ahora?", "Informe generado"))
        {
            Dialogos.Abrir(generado);
        }
    }

    [RelayCommand(CanExecute = nameof(HayUltimo))]
    private void AbrirUltimo()
    {
        if (UltimoArchivo is { } ruta)
        {
            Dialogos.Abrir(ruta);
        }
    }

    private bool HayUltimo() => !string.IsNullOrEmpty(UltimoArchivo) && File.Exists(UltimoArchivo);
}
