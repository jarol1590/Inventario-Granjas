using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Domain.Abstractions;

/// <summary>CRUD básico por clave entera. Todas las operaciones participan de la transacción de su <see cref="IUnitOfWork"/>.</summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Inserta la entidad, asigna el id generado a su propiedad <c>Id</c> y lo devuelve.</summary>
    Task<int> InsertAsync(T entity, CancellationToken ct = default);

    /// <exception cref="InvalidOperationException">Si no existe una fila con ese id.</exception>
    Task UpdateAsync(T entity, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}

/// <summary>Filas únicas de configuración (sistema y firma).</summary>
public interface IConfiguracionRepository
{
    /// <summary>Nulo mientras no se haya creado la contraseña (primer arranque).</summary>
    Task<ConfiguracionSistema?> GetSistemaAsync(CancellationToken ct = default);

    Task GuardarSistemaAsync(ConfiguracionSistema configuracion, CancellationToken ct = default);

    Task<ConfiguracionFirma?> GetFirmaAsync(CancellationToken ct = default);

    Task GuardarFirmaAsync(ConfiguracionFirma firma, CancellationToken ct = default);
}

public interface IGranjaRepository : IRepository<Granja>
{
    Task<Granja?> GetByCodigoAsync(string codigo, CancellationToken ct = default);

    Task<bool> ExisteCodigoAsync(string codigo, int? excluirId = null, CancellationToken ct = default);

    Task<bool> ExisteNombreAsync(string nombre, int? excluirId = null, CancellationToken ct = default);

    /// <summary>Animales Disponibles más lotes Activos de la granja.</summary>
    Task<int> ContarInventarioActivoAsync(int granjaId, CancellationToken ct = default);

    /// <summary>True si algún animal, lote, remisión o movimiento referencia la granja (no se puede eliminar).</summary>
    Task<bool> TieneReferenciasAsync(int granjaId, CancellationToken ct = default);
}

public interface IProgramaRepository : IRepository<Programa>
{
    Task<Programa?> GetByCodigoAsync(string codigo, CancellationToken ct = default);

    Task<bool> ExisteCodigoAsync(string codigo, int? excluirId = null, CancellationToken ct = default);

    /// <summary>True si categorías, oficios o lotes referencian el programa (no se puede eliminar).</summary>
    Task<bool> TieneReferenciasAsync(int programaId, CancellationToken ct = default);
}

public interface IGranjaProgramaRepository : IRepository<GranjaPrograma>
{
    Task<IReadOnlyList<int>> GetProgramaIdsAsync(int granjaId, CancellationToken ct = default);

    Task<IReadOnlyList<int>> GetGranjaIdsAsync(int programaId, CancellationToken ct = default);

    /// <summary>Deja el programa asociado exactamente a las granjas indicadas.</summary>
    Task ReemplazarGranjasDeProgramaAsync(int programaId, IEnumerable<int> granjaIds, CancellationToken ct = default);

    Task EliminarPorGranjaAsync(int granjaId, CancellationToken ct = default);

    Task EliminarPorProgramaAsync(int programaId, CancellationToken ct = default);
}

public interface IOrientacionRepository : IRepository<OrientacionProductiva>
{
    Task<OrientacionProductiva?> GetByNombreAsync(string nombre, CancellationToken ct = default);
}

public interface ICategoriaRepository : IRepository<CategoriaEtapa>
{
    Task<IReadOnlyList<CategoriaEtapa>> GetByProgramaAsync(int programaId, CancellationToken ct = default);

    Task<CategoriaEtapa?> GetByNombreAsync(int programaId, string nombre, CancellationToken ct = default);
}

public interface ITarifaProgramaRepository : IRepository<TarifaCategoriaPrograma>
{
    Task<IReadOnlyList<TarifaCategoriaPrograma>> GetByProgramaAsync(int programaId, CancellationToken ct = default);

    Task<TarifaCategoriaPrograma?> GetByCategoriaAsync(int categoriaId, CancellationToken ct = default);

    Task EliminarPorProgramaAsync(int programaId, CancellationToken ct = default);
}

public interface IVigenciaRepository : IRepository<VigenciaValorizacion>
{
    Task<VigenciaValorizacion?> GetActivaAsync(CancellationToken ct = default);

    Task<VigenciaValorizacion?> GetByAnioAsync(int anio, CancellationToken ct = default);

    /// <summary>Marca la vigencia como activa y desactiva las demás (solo una puede estar activa).</summary>
    Task ActivarAsync(int vigenciaId, CancellationToken ct = default);
}

public interface ITarifaRepository : IRepository<TablaValorizacionBase>
{
    Task<IReadOnlyList<TablaValorizacionBase>> GetByVigenciaAsync(int vigenciaId, CancellationToken ct = default);

    /// <summary>Reemplaza todas las filas de la vigencia por las indicadas (los ids se reasignan).</summary>
    Task ReemplazarVigenciaAsync(int vigenciaId, IEnumerable<TablaValorizacionBase> filas, CancellationToken ct = default);
}

public interface IAnimalRepository : IRepository<Animal>
{
    Task<Animal?> GetByChapetaAsync(string chapetaAmarilla, CancellationToken ct = default);

    Task<bool> ExisteChapetaAsync(string chapetaAmarilla, int? excluirId = null, CancellationToken ct = default);

    Task<IReadOnlyList<Animal>> GetByEstadoAsync(string estado, CancellationToken ct = default);

    Task<IReadOnlyList<Animal>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default);

    /// <summary>
    /// Actualiza los datos editables del animal. <b>Nunca</b> modifica <c>valor_compra_origen</c>, <c>fecha_registro</c>
    /// ni <c>clase_entrada</c> (inmutables); para el valor de compra existe <see cref="ActualizarValorCompraOrigenAsync"/>.
    /// </summary>
    new Task UpdateAsync(Animal entity, CancellationToken ct = default);

    /// <summary>Categoría y valor contable calculados por el AgingEngine.</summary>
    Task ActualizarValoracionAsync(int animalId, int categoriaId, decimal valorContable, CancellationToken ct = default);

    /// <summary>Solo para corregir la factura al editar una compra; el resto del sistema no debe usarlo.</summary>
    Task ActualizarValorCompraOrigenAsync(int animalId, decimal valor, CancellationToken ct = default);
}

public interface IGenealogiaRepository : IRepository<DatosGenealogia>
{
    Task<DatosGenealogia?> GetByAnimalIdAsync(int animalId, CancellationToken ct = default);

    Task EliminarPorAnimalAsync(int animalId, CancellationToken ct = default);

    /// <summary>True si el animal figura como padre (monta directa) de otro animal registrado.</summary>
    Task<bool> EsPadreAsync(int animalId, CancellationToken ct = default);
}

public interface ILoteRepository : IRepository<LotePoblacion>
{
    Task<LotePoblacion?> GetByCodigoAsync(string codigoLote, CancellationToken ct = default);

    Task<bool> ExisteCodigoAsync(string codigoLote, CancellationToken ct = default);
}

public interface IOficioRepository : IRepository<OficioVenta>
{
    Task<OficioVenta?> GetByNumeroAsync(string numeroOficio, CancellationToken ct = default);
}

public interface IRemisionRepository : IRepository<Remision>
{
    Task<Remision?> GetByNumeroAsync(string numeroRemision, CancellationToken ct = default);

    Task<bool> ExisteNumeroAsync(string numeroRemision, int? excluirId = null, CancellationToken ct = default);
}

public interface IRemisionDetalleRepository : IRepository<RemisionDetalle>
{
    Task<IReadOnlyList<RemisionDetalle>> GetByRemisionAsync(int remisionId, CancellationToken ct = default);

    Task<IReadOnlyList<RemisionDetalle>> GetByAnimalAsync(int animalId, CancellationToken ct = default);

    Task EliminarPorRemisionAsync(int remisionId, CancellationToken ct = default);
}

public interface IMovimientoRepository : IRepository<MovimientoInventario>
{
    /// <summary>Movimientos del animal en orden cronológico.</summary>
    Task<IReadOnlyList<MovimientoInventario>> GetByAnimalAsync(int animalId, CancellationToken ct = default);

    Task<IReadOnlyList<MovimientoInventario>> GetByRemisionAsync(int remisionId, CancellationToken ct = default);

    Task EliminarPorRemisionAsync(int remisionId, CancellationToken ct = default);

    Task EliminarPorAnimalAsync(int animalId, CancellationToken ct = default);

    /// <summary>Suma del libro (Σ costo_asociado, calculada en C#) de cada animal con movimientos.</summary>
    Task<IReadOnlyDictionary<int, decimal>> GetSaldosPorAnimalAsync(CancellationToken ct = default);

    Task<decimal> GetSaldoAnimalAsync(int animalId, CancellationToken ct = default);
}
