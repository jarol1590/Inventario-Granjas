using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Data.Repositories;

internal sealed class OficioRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<OficioVenta>(connection, transaction), IOficioRepository
{
    public Task<OficioVenta?> GetByNumeroAsync(string numeroOficio, CancellationToken ct = default)
        => UnoAsync<OficioVenta>("SELECT * FROM OFICIO_VENTA WHERE numero_oficio = @numeroOficio", new { numeroOficio }, ct);
}

internal sealed class RemisionRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<Remision>(connection, transaction), IRemisionRepository
{
    public Task<Remision?> GetByNumeroAsync(string numeroRemision, CancellationToken ct = default)
        => UnoAsync<Remision>("SELECT * FROM REMISION WHERE numero_remision = @numeroRemision", new { numeroRemision }, ct);

    public Task<bool> ExisteNumeroAsync(string numeroRemision, int? excluirId = null, CancellationToken ct = default)
        => ExisteAsync(
            "SELECT COUNT(1) FROM REMISION WHERE numero_remision = @numeroRemision AND (@excluirId IS NULL OR id <> @excluirId)",
            new { numeroRemision, excluirId }, ct);
}

internal sealed class RemisionDetalleRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<RemisionDetalle>(connection, transaction), IRemisionDetalleRepository
{
    public Task<IReadOnlyList<RemisionDetalle>> GetByRemisionAsync(int remisionId, CancellationToken ct = default)
        => ListaAsync<RemisionDetalle>("SELECT * FROM REMISION_DETALLE WHERE remision_id = @remisionId ORDER BY id",
            new { remisionId }, ct);

    public Task<IReadOnlyList<RemisionDetalle>> GetByAnimalAsync(int animalId, CancellationToken ct = default)
        => ListaAsync<RemisionDetalle>("SELECT * FROM REMISION_DETALLE WHERE animal_id = @animalId ORDER BY id",
            new { animalId }, ct);

    public Task EliminarPorRemisionAsync(int remisionId, CancellationToken ct = default)
        => EjecutarAsync("DELETE FROM REMISION_DETALLE WHERE remision_id = @remisionId", new { remisionId }, ct);
}

internal sealed class MovimientoRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<MovimientoInventario>(connection, transaction), IMovimientoRepository
{
    public Task<IReadOnlyList<MovimientoInventario>> GetByAnimalAsync(int animalId, CancellationToken ct = default)
        => ListaAsync<MovimientoInventario>(
            "SELECT * FROM MOVIMIENTO_INVENTARIO WHERE animal_id = @animalId ORDER BY fecha_movimiento, id",
            new { animalId }, ct);

    public Task<IReadOnlyList<MovimientoInventario>> GetByRemisionAsync(int remisionId, CancellationToken ct = default)
        => ListaAsync<MovimientoInventario>(
            "SELECT * FROM MOVIMIENTO_INVENTARIO WHERE remision_id = @remisionId ORDER BY id",
            new { remisionId }, ct);

    public Task EliminarPorRemisionAsync(int remisionId, CancellationToken ct = default)
        => EjecutarAsync("DELETE FROM MOVIMIENTO_INVENTARIO WHERE remision_id = @remisionId", new { remisionId }, ct);

    public Task EliminarPorAnimalAsync(int animalId, CancellationToken ct = default)
        => EjecutarAsync("DELETE FROM MOVIMIENTO_INVENTARIO WHERE animal_id = @animalId", new { animalId }, ct);

    public async Task<IReadOnlyDictionary<int, decimal>> GetSaldosPorAnimalAsync(CancellationToken ct = default)
    {
        var filas = await ListaAsync<FilaSaldo>(
            "SELECT animal_id AS AnimalId, costo_asociado AS Costo FROM MOVIMIENTO_INVENTARIO WHERE animal_id IS NOT NULL",
            null, ct);

        // El dinero se suma en C# con decimal: SUM() de SQLite usa coma flotante.
        var saldos = new Dictionary<int, decimal>();
        foreach (var fila in filas)
        {
            saldos[fila.AnimalId] = saldos.GetValueOrDefault(fila.AnimalId) + fila.Costo;
        }

        return saldos;
    }

    public async Task<decimal> GetSaldoAnimalAsync(int animalId, CancellationToken ct = default)
    {
        var filas = await ListaAsync<FilaSaldo>(
            "SELECT animal_id AS AnimalId, costo_asociado AS Costo FROM MOVIMIENTO_INVENTARIO WHERE animal_id = @animalId",
            new { animalId }, ct);
        return filas.Sum(f => f.Costo);
    }

    internal sealed class FilaSaldo
    {
        public int AnimalId { get; set; }

        public decimal Costo { get; set; }
    }
}
