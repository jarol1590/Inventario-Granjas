using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.App.Composicion;

/// <summary>
/// Abre las ventanas modales de la aplicación (ficha, nacimiento, compra, salida, traslado, selector de animales).
/// Cada método devuelve true si se guardó algo (para que la pantalla que lo invocó se actualice).
/// </summary>
public interface IFormularios
{
    Task<bool> NuevoNacimientoAsync();

    Task<bool> VerFichaAsync(int animalId);

    /// <summary>
    /// Nueva compra del programa activo en los filtros globales: en un programa de animales individuales abre el editor de
    /// compras; en un programa por lotes abre "Nuevo lote" en modo compra. Ambos preseleccionan la granja del filtro.
    /// </summary>
    Task<bool> NuevaCompraAsync();

    /// <summary>"Nuevo lote" del programa por lotes activo (compra o nacimiento a elección), con la granja del filtro.</summary>
    Task<bool> NuevoLoteAsync();

    Task<bool> AbrirCompraAsync(int remisionId);

    /// <summary>Abre la compra de un lote en la misma pantalla de detalle de compra (editable y anulable).</summary>
    Task<bool> AbrirCompraLoteAsync(int loteId);

    Task<bool> NuevaSalidaAsync();

    /// <param name="clase">Clase de salida de la fila (VENTA, MUERTE, HURTO, DESCARTE_SANITARIO).</param>
    Task<bool> AbrirSalidaAsync(int remisionId, string clase);

    Task<bool> NuevoTrasladoAsync();

    /// <summary>Abre un traslado registrado en modo de consulta (desde ahí se puede editar o anular).</summary>
    Task<bool> AbrirTrasladoAsync(int remisionId);

    /// <summary>Muestra los animales Disponibles de la granja para marcar varios. Nulo si se cancela.</summary>
    Task<IReadOnlyList<AnimalSeleccionable>?> SeleccionarAnimalesAsync(
        int granjaId, IReadOnlyCollection<int> excluidos, string? textoInicial = null);
}
