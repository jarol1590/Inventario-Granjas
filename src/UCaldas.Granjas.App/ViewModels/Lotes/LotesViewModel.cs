using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.App.ViewModels.Lotes;

/// <summary>Una fila de la grilla de lotes.</summary>
public sealed class LoteFila(LoteDetalle detalle)
{
    public LoteDetalle Detalle { get; } = detalle;

    public int Id => Detalle.Lote.Id;
    public string Codigo => Detalle.Lote.CodigoLote;
    public string Granja => Detalle.GranjaNombre;
    public string Ubicacion => Detalle.Lote.Ubicacion;
    public string Origen => Detalle.Lote.ClaseEntrada;
    public string Etapa => Detalle.EtapaNombre;
    public string Estado => Detalle.Lote.Estado;
    public string Inicial => Texto(Detalle.Lote.CantidadInicial);
    public string Bajas => Texto(Detalle.Lote.MortandadAcumulada);
    public string Actual => Texto(Detalle.Lote.CantidadActual);
    public decimal ValorTotal => Detalle.Lote.ValorTotalContable;

    private string Texto(int n) => Detalle.Lote.ControlNumericoPoblacion ? n.ToString("N0", Formato.Cultura) : "N/A";
}

/// <summary>Pestaña de Lotes: un programa de inventario por lotes con sus KPIs, filtros, grilla y acciones.</summary>
public sealed partial class ProgramaLoteTabViewModel : ViewModelBase
{
    private const string Todos = "Todos";

    private readonly ILoteService _lotes;
    private readonly ICatalogoService _catalogo;
    private readonly ITarifaProgramaService _tarifas;
    private readonly IReloj _reloj;
    private IReadOnlyList<LoteDetalle> _todos = [];
    private bool _cargado;
    private bool _aplicando;

    public ProgramaLoteTabViewModel(
        Programa programa, ILoteService lotes, ICatalogoService catalogo, ITarifaProgramaService tarifas, IReloj reloj, IDialogService dialogos)
        : base(dialogos)
    {
        Programa = programa;
        Especie = EspecieDePrograma.De(programa);
        _lotes = lotes;
        _catalogo = catalogo;
        _tarifas = tarifas;
        _reloj = reloj;
        Estados = [new(EstadoLote.Activo, "Activo"), new(EstadoLote.Finalizado, "Finalizado"), new(null, "Todos")];
        _estadoSeleccionado = Estados[0];
    }

    public Programa Programa { get; }

    public EspecieDePrograma Especie { get; }

    public string Titulo => Especie.Titulo;

    public string Icono => Especie.Icono;

    public ObservableCollection<LoteFila> Lotes { get; } = [];

    public ObservableCollection<OpcionFiltro> Granjas { get; } = [];

    public ObservableCollection<OpcionFiltro> Etapas { get; } = [];

    public IReadOnlyList<OpcionTexto> Estados { get; }

    [ObservableProperty]
    private string _texto = string.Empty;

    [ObservableProperty]
    private OpcionFiltro? _granjaSeleccionada;

    [ObservableProperty]
    private OpcionFiltro? _etapaSeleccionada;

    [ObservableProperty]
    private OpcionTexto _estadoSeleccionado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CambiarEtapaCommand), nameof(RegistrarBajaCommand), nameof(VerFichaCommand))]
    private LoteFila? _seleccionado;

    [ObservableProperty]
    private string _poblacionTexto = "0 animales";

    [ObservableProperty]
    private string _mortandadTexto = "0 bajas (0 %)";

    [ObservableProperty]
    private string _saldoTexto = Formato.Dinero(0);

    [ObservableProperty]
    private string _resumenTexto = "Mostrando: 0 lotes";

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            if (!_cargado)
            {
                await CargarOpcionesAsync();
            }

            _todos = await _lotes.ListarAsync(Programa.Id);
            Aplicar();
        }, $"consultar los lotes ({Especie.Titulo})");
    }

    private async Task CargarOpcionesAsync()
    {
        _aplicando = true;
        try
        {
            Granjas.Add(new OpcionFiltro(null, Todos));
            foreach (var g in await _catalogo.GetGranjasAsync())
            {
                Granjas.Add(new OpcionFiltro(g.Id, g.Nombre));
            }

            Etapas.Add(new OpcionFiltro(null, Todos));
            foreach (var c in await _catalogo.GetCategoriasDeProgramaAsync(Programa.Id))
            {
                Etapas.Add(new OpcionFiltro(c.Id, c.Nombre));
            }

            GranjaSeleccionada = Granjas[0];
            EtapaSeleccionada = Etapas[0];
            _cargado = true;
        }
        finally
        {
            _aplicando = false;
        }
    }

    // Los filtros se aplican en memoria sobre los lotes ya consultados.
    partial void OnTextoChanged(string value) => Aplicar();

    partial void OnGranjaSeleccionadaChanged(OpcionFiltro? value) => Aplicar();

    partial void OnEtapaSeleccionadaChanged(OpcionFiltro? value) => Aplicar();

    partial void OnEstadoSeleccionadoChanged(OpcionTexto value) => Aplicar();

    private void Aplicar()
    {
        if (_aplicando)
        {
            return;
        }

        var texto = Texto?.Trim();
        var filtrados = _todos.Where(d =>
                (GranjaSeleccionada?.Id is not { } g || d.Lote.GranjaId == g)
                && (EtapaSeleccionada?.Id is not { } e || d.Lote.EtapaId == e)
                && (EstadoSeleccionado.Valor is not { } s || d.Lote.Estado == s)
                && (string.IsNullOrEmpty(texto)
                    || d.Lote.CodigoLote.Contains(texto, StringComparison.OrdinalIgnoreCase)
                    || d.Lote.Ubicacion.Contains(texto, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var seleccionado = Seleccionado?.Id;
        Lotes.Clear();
        foreach (var d in filtrados)
        {
            Lotes.Add(new LoteFila(d));
        }

        Seleccionado = Lotes.FirstOrDefault(l => l.Id == seleccionado);

        var resumen = ResumenLotes.De(filtrados.Select(d => d.Lote));
        PoblacionTexto = $"{resumen.PoblacionViva:N0} {Especie.Plural}";
        MortandadTexto = $"{resumen.MortandadAcumulada:N0} bajas ({resumen.PorcentajeMortandad:0.##} %)";
        SaldoTexto = Formato.Dinero(resumen.ValorContable);
        ResumenTexto = $"Mostrando: {filtrados.Count:N0} {(filtrados.Count == 1 ? "lote" : "lotes")}   Valor contable: {Formato.Dinero(resumen.ValorContable)}";
    }

    [RelayCommand]
    private async Task NuevoLoteAsync()
    {
        var modal = new NuevoLoteViewModel(_lotes, _catalogo, _tarifas, _reloj, Dialogos, Programa);
        if (await modal.InicializarAsync() && Dialogos.Mostrar(modal))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task CambiarEtapaAsync()
    {
        if (Seleccionado is not { } fila)
        {
            return;
        }

        var modal = new CambioEtapaDesdobleViewModel(_lotes, _catalogo, Dialogos, fila.Detalle);
        if (await modal.InicializarAsync() && Dialogos.Mostrar(modal))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task RegistrarBajaAsync()
    {
        if (Seleccionado is { } fila && Dialogos.Mostrar(new BajaLoteViewModel(_lotes, _reloj, Dialogos, fila.Detalle)))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task VerFichaAsync()
    {
        if (Seleccionado is not { } fila)
        {
            return;
        }

        var ficha = new FichaLoteViewModel(_lotes, _catalogo, Dialogos, fila.Detalle);
        if (!await ficha.InicializarAsync())
        {
            return;
        }

        // Aunque se cierre con la X, si se guardaron correcciones la grilla debe refrescarse.
        if (Dialogos.Mostrar(ficha) || ficha.HuboCambios)
        {
            await CargarAsync();
        }
    }

    private bool HaySeleccion() => Seleccionado is not null;
}

/// <summary>
/// Lotes: inventario de animales vivos en grupo (aves, peces, cerdos). Una pestaña por programa con
/// <c>tipo_inventario = LOTE</c>. Todavía sin Kardex de lotes.
/// </summary>
public sealed partial class LotesViewModel(
    ILoteService lotes, ICatalogoService catalogo, ITarifaProgramaService tarifas, IReloj reloj, IDialogService dialogos)
    : ViewModelBase(dialogos), ISeccion
{
    private bool _cargadas;
    private bool _cargandoPestanas;

    /// <summary>Cada pestaña tiene sus propios filtros: el combo global de Programa no aplica.</summary>
    public bool MostrarFiltroDePrograma => false;

    public ObservableCollection<ProgramaLoteTabViewModel> Programas { get; } = [];

    [ObservableProperty]
    private ProgramaLoteTabViewModel? _programaActivo;

    public bool HayProgramas => Programas.Count > 0;

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            if (!_cargadas)
            {
                _cargandoPestanas = true;
                try
                {
                    foreach (var p in await catalogo.GetProgramasPorTipoAsync(TipoInventario.Lote))
                    {
                        Programas.Add(new ProgramaLoteTabViewModel(p, lotes, catalogo, tarifas, reloj, Dialogos));
                    }

                    ProgramaActivo = Programas.FirstOrDefault();
                }
                finally
                {
                    _cargandoPestanas = false;
                }

                OnPropertyChanged(nameof(HayProgramas));
                _cargadas = true;
            }

            if (ProgramaActivo is { } activa)
            {
                await activa.CargarAsync();
            }
        }, "consultar los lotes");
    }

    partial void OnProgramaActivoChanged(ProgramaLoteTabViewModel? value)
    {
        if (!_cargandoPestanas && value is not null)
        {
            _ = value.CargarAsync();
        }
    }
}