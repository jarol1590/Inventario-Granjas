using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Data.Repositories;

internal sealed class VigenciaRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<VigenciaValorizacion>(connection, transaction), IVigenciaRepository
{
    public Task<VigenciaValorizacion?> GetActivaAsync(CancellationToken ct = default)
        => UnoAsync<VigenciaValorizacion>("SELECT * FROM VIGENCIA_VALORIZACION WHERE activa = 1 LIMIT 1", null, ct);

    public Task<VigenciaValorizacion?> GetByAnioAsync(int anio, CancellationToken ct = default)
        => UnoAsync<VigenciaValorizacion>("SELECT * FROM VIGENCIA_VALORIZACION WHERE anio = @anio", new { anio }, ct);

    public async Task ActivarAsync(int vigenciaId, CancellationToken ct = default)
    {
        if (await GetByIdAsync(vigenciaId, ct) is null)
        {
            throw new InvalidOperationException($"No existe la vigencia con id {vigenciaId}.");
        }

        // Primero se desactivan las demás: el índice único parcial solo admite una activa a la vez.
        await EjecutarAsync("UPDATE VIGENCIA_VALORIZACION SET activa = 0 WHERE activa = 1 AND id <> @vigenciaId",
            new { vigenciaId }, ct);
        await EjecutarAsync("UPDATE VIGENCIA_VALORIZACION SET activa = 1 WHERE id = @vigenciaId",
            new { vigenciaId }, ct);
    }
}

internal sealed class TarifaRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<TablaValorizacionBase>(connection, transaction), ITarifaRepository
{
    public Task<IReadOnlyList<TablaValorizacionBase>> GetByVigenciaAsync(int vigenciaId, CancellationToken ct = default)
        => ListaAsync<TablaValorizacionBase>(
            """
            SELECT * FROM TABLA_VALORIZACION_BASE
            WHERE vigencia_id = @vigenciaId
            ORDER BY categoria_id, edad_minima_meses, sexo, COALESCE(orientacion_id, 0), id
            """,
            new { vigenciaId }, ct);

    public async Task ReemplazarVigenciaAsync(int vigenciaId, IEnumerable<TablaValorizacionBase> filas, CancellationToken ct = default)
    {
        await EjecutarAsync("DELETE FROM TABLA_VALORIZACION_BASE WHERE vigencia_id = @vigenciaId", new { vigenciaId }, ct);
        foreach (var fila in filas)
        {
            fila.Id = 0;
            fila.VigenciaId = vigenciaId;
            await InsertAsync(fila, ct);
        }
    }
}
