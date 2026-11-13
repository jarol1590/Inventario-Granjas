using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Histórico de compras: rango de meses, resumen, búsqueda por factura y acceso al detalle / nueva compra.</summary>
public sealed partial class ComprasViewModel : ViewModelBase, IUsaFiltroGlobal
{
    private readonly IConsultasService _consultas;
    private readonly IFormularios _formularios;
    private readonly INavegador _navegador;
    private readonly ContextoGlobal _contexto;
    private int _version;

    public ComprasViewModel(
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

    public ObservableCollection<CompraListItem> Compras { get; } = [];

    [ObservableProperty]
    private string _textoFactura = string.Empty;

    [ObservableProperty]
    private bool _incluirAnuladas;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerDetalleCommand))]
    private CompraListItem? _seleccionada;

    [ObservableProperty]
    private int _totalCompras;

    [ObservableProperty]
    private int _animalesComprados;

    [ObservableProperty]
    private string _valorTotalTexto = Formato.Dinero(0);

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
            var resultado = await _consultas.ListarComprasAsync(new CompraFiltro(
                _contexto.GranjaId,
                Rango.Desde,
                Rango.Hasta,
                string.IsNullOrWhiteSpace(TextoFactura) ? null : TextoFactura.Trim(),
                IncluirAnuladas,
                _contexto.ProgramaId));
            if (version != _version)
            {
                return;
            }

            Compras.Clear();
            foreach (var compra in resultado.Items)
            {
                Compras.Add(compra);
            }

            TotalCompras = resultado.Resumen.TotalCompras;
            AnimalesComprados = resultado.Resumen.AnimalesComprados;
            ValorTotalTexto = Formato.Dinero(resultado.Resumen.ValorTotal);
        }, "consultar las compras");
    }

    [RelayCommand]
    private Task BuscarAsync() => CargarAsync();

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task VerDetalleAsync(CompraListItem? item)
    {
        item ??= Seleccionada;
        if (item is null)
        {
            return;
        }

        // La compra de un lote se abre en la misma pantalla de detalle (editable y anulable).
        var guardado = item.LoteId is { } loteId
            ? await _formularios.AbrirCompraLoteAsync(loteId)
            : await _formularios.AbrirCompraAsync(item.RemisionId);
        if (guardado)
        {
            await CargarAsync();
        }
    }

    [RelayCommand]
    private async Task NuevaCompraAsync()
    {
        if (await _formularios.NuevaCompraAsync())
        {
            await CargarAsync();
        }
    }

    [RelayCommand]
    private void GenerarInforme() => _navegador.IrA(ShellViewModel.ClaveInformes);

    private bool HaySeleccion() => Seleccionada is not null;
}
