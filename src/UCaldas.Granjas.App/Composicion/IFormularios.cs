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

    Task<bool> NuevaCompraAsync();

    Task<bool> AbrirCompraAsync(int remisionId);

    Task<bool> NuevaSalidaAsync();

    /// <param name="clase">Clase de salida de la fila (VENTA, MUERTE, HURTO, DESCARTE_SANITARIO).</param>
    Task<bool> AbrirSalidaAsync(int remisionId, string clase);

    Task<bool> NuevoTrasladoAsync();

    /// <summary>Muestra los animales Disponibles de la granja para marcar varios. Nulo si se cancela.</summary>
    Task<IReadOnlyList<AnimalSeleccionable>?> SeleccionarAnimalesAsync(
        int granjaId, IReadOnlyCollection<int> excluidos, string? textoInicial = null);
}
