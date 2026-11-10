using Microsoft.Extensions.DependencyInjection;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.Services.Consultas;

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
        var viewModel = servicios.GetRequiredService<CompraEditorViewModel>();
        return await viewModel.InicializarNuevaAsync() && dialogos.Mostrar(viewModel);
    }

    public async Task<bool> AbrirCompraAsync(int remisionId)
    {
        var viewModel = servicios.GetRequiredService<CompraEditorViewModel>();
        return await viewModel.InicializarExistenteAsync(remisionId) && dialogos.Mostrar(viewModel);
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
