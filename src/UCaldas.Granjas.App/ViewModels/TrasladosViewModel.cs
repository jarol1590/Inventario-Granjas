using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Traslados entre granjas: resumen del período, búsqueda por remisión o chapeta, registro, detalle editable y anulación.</summary>
public sealed partial class TrasladosViewModel : ViewModelBase, IUsaFiltroGlobal
{
    private readonly IConsultasService _consultas;
    private readonly IKardexService _kardex;
    private readonly IFormularios _formularios;
    private readonly INavegador _navegador;
    private readonly ContextoGlobal _contexto;
    private int _version;

    public TrasladosViewModel(
        IConsultasService consultas,
        IKardexService kardex,
        IFormularios formularios,
        INavegador navegador,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _consultas = consultas;
        _kardex = kardex;
        _formularios = formularios;
        _navegador = navegador;
        _contexto = contexto;
        Rango = new FiltroMensual(reloj.Hoy);
        Rango.Cambio += () => _ = CargarAsync();
    }

    public FiltroMensual Rango { get; }

    public ObservableCollection<TrasladoListItem> Traslados { get; } = [];

    [ObservableProperty]
    private string _texto = string.Empty;

    [ObservableProperty]
    private bool _incluirAnulados;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnularCommand), nameof(VerDetalleCommand))]
    private TrasladoListItem? _seleccionado;

    [ObservableProperty]
    private int _trasladosRealizados;

    [ObservableProperty]
    private int _animalesMovidos;

    partial void OnIncluirAnuladosChanged(bool value) => _ = CargarAsync();

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
            var resultado = await _consultas.ListarTrasladosAsync(new TrasladoFiltro(
                _contexto.GranjaId,
                Rango.Desde,
                Rango.Hasta,
                string.IsNullOrWhiteSpace(Texto) ? null : Texto.Trim(),
                IncluirAnulados));
            if (version != _version)
            {
                return;
            }

            Traslados.Clear();
            foreach (var traslado in resultado.Items)
            {
                Traslados.Add(traslado);
            }

            TrasladosRealizados = resultado.Resumen.TrasladosRealizados;
            AnimalesMovidos = resultado.Resumen.AnimalesMovidos;
        }, "consultar los traslados");
    }

    [RelayCommand]
    private Task BuscarAsync() => CargarAsync();

    [RelayCommand]
    private async Task RegistrarTrasladoAsync()
    {
        if (await _formularios.NuevoTrasladoAsync())
        {
            await CargarAsync();
        }
    }

    /// <summary>Abre el detalle del traslado (doble clic o botón); desde ahí se puede editar o anular.</summary>
    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task VerDetalleAsync(TrasladoListItem? item)
    {
        item ??= Seleccionado;
        if (item is not null && await _formularios.AbrirTrasladoAsync(item.RemisionId))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeAnular))]
    private async Task AnularAsync()
    {
        if (Seleccionado is not { } traslado)
        {
            return;
        }

        var motivo = AnulacionDeTraslados.PedirMotivo(Dialogos, traslado.NumeroRemision, traslado.Cantidad, traslado.Origen);
        if (motivo is null)
        {
            return;
        }

        if (await EjecutarAsync(() => _kardex.AnularAsync(traslado.RemisionId, motivo), "anular el traslado"))
        {
            Dialogos.Informar($"El traslado {traslado.NumeroRemision} fue anulado.", "Traslado anulado");
            await CargarAsync();
        }
    }

    [RelayCommand]
    private void GenerarInforme() => _navegador.IrA(ShellViewModel.ClaveInformes);

    private bool HaySeleccion() => Seleccionado is not null;

    private bool PuedeAnular() => Seleccionado is { Anulado: false };
}
