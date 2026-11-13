namespace UCaldas.Granjas.Domain.Abstractions;

/// <summary>
/// Unidad de trabajo: agrupa los repositorios sobre una misma conexión y transacción.
/// Si se libera sin <see cref="CommitAsync"/>, todo se revierte.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IConfiguracionRepository Configuracion { get; }
    IGranjaRepository Granjas { get; }
    IProgramaRepository Programas { get; }
    IGranjaProgramaRepository GranjaProgramas { get; }
    IOrientacionRepository Orientaciones { get; }
    ICategoriaRepository Categorias { get; }
    IVigenciaRepository Vigencias { get; }
    ITarifaRepository Tarifas { get; }
    ITarifaProgramaRepository TarifasPrograma { get; }
    IAnimalRepository Animales { get; }
    IGenealogiaRepository Genealogias { get; }
    ILoteRepository Lotes { get; }
    IOficioRepository Oficios { get; }
    IRemisionRepository Remisiones { get; }
    IRemisionDetalleRepository RemisionDetalles { get; }
    IMovimientoRepository Movimientos { get; }

    /// <summary>Confirma la transacción. En una sesión de solo lectura no hace nada.</summary>
    Task CommitAsync(CancellationToken ct = default);

    Task RollbackAsync(CancellationToken ct = default);
}

public interface IUnitOfWorkFactory
{
    /// <summary>Abre una unidad de trabajo transaccional; toma el bloqueo de escritura de inmediato.</summary>
    Task<IUnitOfWork> BeginAsync(CancellationToken ct = default);

    /// <summary>Abre una sesión de solo lectura (sin transacción explícita) para consultas.</summary>
    Task<IUnitOfWork> OpenReadOnlyAsync(CancellationToken ct = default);
}
