using Microsoft.Extensions.DependencyInjection;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.App.ViewModels.Lotes;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.App.Composicion;

/// <summary>Crea los ViewModels de las ventanas modales, los inicializa y los muestra.</summary>
public sealed class Formularios(IServiceProvider servicios, IDialogService dialogos) : IFormularios
{
    public async Task<bool> NuevoNacimientoAsync()
    {
        var viewModel = servicios.GetRequiredService<NacimientoViewModel>();
        return await viewModel.InicializarAsync() && dialogos.Mostrar(viewModel);
    }

    public async Task<bool> VerFichaAsync(int animalId)
    {
        var viewModel = servicios.GetRequiredService<FichaViewModel>();
        if (!await viewModel.InicializarAsync(animalId))
        {
            return false;
        }

        // Aunque el usuario cierre con la X, si guardó correcciones la pantalla de origen debe refrescarse.
        var aceptado = dialogos.Mostrar(viewModel);
        return aceptado || viewModel.HuboCambios;
    }

    public async Task<bool> NuevaCompraAsync()
    {
        // Programa por lotes: la compra se registra como un lote comprado (aún no hay remisión de lotes).
        if (await NuevoLoteSiHayProgramaDeLotesAsync() is { } resultado)
        {
            return resultado;
        }

        var viewModel = servicios.GetRequiredService<CompraEditorViewModel>();
        return await viewModel.InicializarNuevaAsync() && dialogos.Mostrar(viewModel);
    }

    public async Task<bool> NuevoLoteAsync() => await NuevoLoteSiHayProgramaDeLotesAsync() ?? false;

    /// <summary>Abre "Nuevo lote" si el programa de los filtros globales es por lotes; null si no lo es.</summary>
    private async Task<bool?> NuevoLoteSiHayProgramaDeLotesAsync()
    {
        var contexto = servicios.GetRequiredService<ContextoGlobal>();
        if (contexto.ProgramaId is not { } programaId
            || (await servicios.GetRequiredService<ICatalogoService>().GetProgramasAsync()).FirstOrDefault(p => p.Id == programaId)
                is not { TipoInventario: TipoInventario.Lote } programa)
        {
            return null;
        }

        var lote = new NuevoLoteViewModel(
            servicios.GetRequiredService<ILoteService>(), servicios.GetRequiredService<ICatalogoService>(),
            servicios.GetRequiredService<ITarifaProgramaService>(), servicios.GetRequiredService<IReloj>(), dialogos, programa, contexto.GranjaId);
        return await lote.InicializarAsync() && dialogos.Mostrar(lote);
    }

    public async Task<bool> AbrirCompraAsync(int remisionId)
    {
        var viewModel = servicios.GetRequiredService<CompraEditorViewModel>();
        return await viewModel.InicializarExistenteAsync(remisionId) && dialogos.Mostrar(viewModel);
    }

    public async Task<bool> AbrirCompraLoteAsync(int loteId)
    {
        var viewModel = servicios.GetRequiredService<CompraEditorViewModel>();
        return await viewModel.InicializarLoteAsync(loteId) && dialogos.Mostrar(viewModel);
    }

    public async Task<bool> NuevaSalidaAsync()
    {
        var viewModel = servicios.GetRequiredService<SalidaEditorViewModel>();
        return await viewModel.InicializarNuevaAsync() && dialogos.Mostrar(viewModel);
    }

    public async Task<bool> AbrirSalidaAsync(int remisionId, string clase)
    {
        var viewModel = servicios.GetRequiredService<SalidaEditorViewModel>();
        return await viewModel.InicializarExistenteAsync(remisionId, clase) && dialogos.Mostrar(viewModel);
    }

    public async Task<bool> NuevoTrasladoAsync()
    {
        var viewModel = servicios.GetRequiredService<TrasladoEditorViewModel>();
        return await viewModel.InicializarAsync() && dialogos.Mostrar(viewModel);
    }

    public async Task<bool> AbrirTrasladoAsync(int remisionId)
    {
        var viewModel = servicios.GetRequiredService<TrasladoEditorViewModel>();
        return await viewModel.InicializarExistenteAsync(remisionId) && dialogos.Mostrar(viewModel);
    }

    public async Task<IReadOnlyList<AnimalSeleccionable>?> SeleccionarAnimalesAsync(
        int granjaId, IReadOnlyCollection<int> excluidos, string? textoInicial = null)
    {
        var viewModel = servicios.GetRequiredService<SelectorAnimalesViewModel>();
        if (!await viewModel.InicializarAsync(granjaId, excluidos, textoInicial))
        {
            return null;
        }

        return dialogos.Mostrar(viewModel) ? viewModel.Seleccionados : null;
    }
}
