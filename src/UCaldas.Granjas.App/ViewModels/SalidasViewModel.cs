using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Histórico de salidas (ventas, bajas y traslados): rango de meses, resumen, búsqueda por chapeta y detalle.</summary>
public sealed partial class SalidasViewModel : ViewModelBase, IUsaFiltroGlobal
{
    private readonly IConsultasService _consultas;
    private readonly IFormularios _formularios;
    private readonly INavegador _navegador;
    private readonly ContextoGlobal _contexto;
    private int _version;

    public SalidasViewModel(
        IConsultasService consultas,
        IFormularios formularios,
        INavegador navegador,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _consultas = consultas;
        _formularios = formularios;
        _navegador = navegador;
        _contexto = contexto;
        Rango = new FiltroMensual(reloj.Hoy);
        Rango.Cambio += () => _ = CargarAsync();
    }

    public FiltroMensual Rango { get; }

    public ObservableCollection<SalidaListItem> Salidas { get; } = [];

    [ObservableProperty]
    private string _textoChapeta = string.Empty;

    [ObservableProperty]
    private bool _incluirAnuladas;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerDetalleCommand))]
    private SalidaListItem? _seleccionada;

    [ObservableProperty]
    private string _recaudoTexto = Formato.Dinero(0);

    [ObservableProperty]
    private int _animalesRetirados;

    [ObservableProperty]
    private int _bajasPorMuerteHurto;

    partial void OnIncluirAnuladasChanged(bool value) => _ = CargarAsync();

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
            var resultado = await _consultas.ListarSalidasAsync(new SalidaFiltro(
                _contexto.GranjaId,
                Rango.Desde,
                Rango.Hasta,
                string.IsNullOrWhiteSpace(TextoChapeta) ? null : TextoChapeta.Trim(),
                IncluirAnuladas));
            if (version != _version)
            {
                return;
            }

            Salidas.Clear();
            foreach (var salida in resultado.Items)
            {
                Salidas.Add(salida);
            }

            RecaudoTexto = Formato.Dinero(resultado.Resumen.RecaudoVentas);
            AnimalesRetirados = resultado.Resumen.AnimalesRetirados;
            BajasPorMuerteHurto = resultado.Resumen.BajasPorMuerteHurto;
        }, "consultar las salidas");
    }

    [RelayCommand]
    private Task BuscarAsync() => CargarAsync();

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task VerDetalleAsync(SalidaListItem? item)
    {
        item ??= Seleccionada;
        if (item is null)
        {
            return;
        }

        // Los traslados tienen su propio detalle (también editable), que se abre desde aquí o desde la sección Traslados.
        var guardado = item.Clase == ClaseSalida.Traslado
            ? await _formularios.AbrirTrasladoAsync(item.RemisionId)
            : await _formularios.AbrirSalidaAsync(item.RemisionId, item.Clase);
        if (guardado)
        {
            await CargarAsync();
        }
    }

    [RelayCommand]
    private async Task NuevaSalidaAsync()
    {
        if (await _formularios.NuevaSalidaAsync())
        {
            await CargarAsync();
        }
    }

    [RelayCommand]
    private void GenerarInforme() => _navegador.IrA(ShellViewModel.ClaveInformes);

    private bool HaySeleccion() => Seleccionada is not null;
}
