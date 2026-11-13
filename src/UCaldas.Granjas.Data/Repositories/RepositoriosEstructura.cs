using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Data.Repositories;

internal sealed class ConfiguracionRepository : IConfiguracionRepository
{
    private readonly SimpleRepository<ConfiguracionSistema> _sistema;
    private readonly SimpleRepository<ConfiguracionFirma> _firma;

    public ConfiguracionRepository(SqliteConnection connection, SqliteTransaction? transaction)
    {
        _sistema = new SimpleRepository<ConfiguracionSistema>(connection, transaction);
        _firma = new SimpleRepository<ConfiguracionFirma>(connection, transaction);
    }

    public async Task<ConfiguracionSistema?> GetSistemaAsync(CancellationToken ct = default)
        => (await _sistema.GetAllAsync(ct)).FirstOrDefault();

    public async Task GuardarSistemaAsync(ConfiguracionSistema configuracion, CancellationToken ct = default)
    {
        var actual = await GetSistemaAsync(ct);
        if (actual is null)
        {
            await _sistema.InsertAsync(configuracion, ct);
            return;
        }

        configuracion.Id = actual.Id;
        await _sistema.UpdateAsync(configuracion, ct);
    }

    public async Task<ConfiguracionFirma?> GetFirmaAsync(CancellationToken ct = default)
        => (await _firma.GetAllAsync(ct)).FirstOrDefault();

    public async Task GuardarFirmaAsync(ConfiguracionFirma firma, CancellationToken ct = default)
    {
        var actual = await GetFirmaAsync(ct);
        if (actual is null)
        {
            await _firma.InsertAsync(firma, ct);
            return;
        }

        firma.Id = actual.Id;
        await _firma.UpdateAsync(firma, ct);
    }
}

internal sealed class GranjaRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<Granja>(connection, transaction), IGranjaRepository
{
    public Task<Granja?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
        => UnoAsync<Granja>("SELECT * FROM GRANJA WHERE codigo = @codigo", new { codigo }, ct);

    public Task<bool> ExisteCodigoAsync(string codigo, int? excluirId = null, CancellationToken ct = default)
        => ExisteAsync(
            "SELECT COUNT(1) FROM GRANJA WHERE codigo = @codigo AND (@excluirId IS NULL OR id <> @excluirId)",
            new { codigo, excluirId }, ct);

    public Task<bool> ExisteNombreAsync(string nombre, int? excluirId = null, CancellationToken ct = default)
        => ExisteAsync(
            "SELECT COUNT(1) FROM GRANJA WHERE nombre = @nombre AND (@excluirId IS NULL OR id <> @excluirId)",
            new { nombre, excluirId }, ct);

    public async Task<int> ContarInventarioActivoAsync(int granjaId, CancellationToken ct = default)
        => (int)await EscalarAsync(
            """
            SELECT (SELECT COUNT(1) FROM ANIMAL WHERE granja_id = @id AND estado = 'Disponible')
                 + (SELECT COUNT(1) FROM LOTE_POBLACION WHERE granja_id = @id AND estado = 'Activo')
            """,
            new { id = granjaId }, ct);

    public Task<bool> TieneReferenciasAsync(int granjaId, CancellationToken ct = default)
        => ExisteAsync(
            """
            SELECT (SELECT COUNT(1) FROM ANIMAL WHERE granja_id = @id)
                 + (SELECT COUNT(1) FROM LOTE_POBLACION WHERE granja_id = @id)
                 + (SELECT COUNT(1) FROM REMISION WHERE granja_origen_id = @id OR granja_destino_id = @id)
                 + (SELECT COUNT(1) FROM MOVIMIENTO_INVENTARIO WHERE granja_origen_id = @id OR granja_destino_id = @id)
            """,
            new { id = granjaId }, ct);
}

internal sealed class ProgramaRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<Programa>(connection, transaction), IProgramaRepository
{
    public Task<Programa?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
        => UnoAsync<Programa>("SELECT * FROM PROGRAMA WHERE codigo = @codigo", new { codigo }, ct);

    public Task<bool> ExisteCodigoAsync(string codigo, int? excluirId = null, CancellationToken ct = default)
        => ExisteAsync(
            "SELECT COUNT(1) FROM PROGRAMA WHERE codigo = @codigo AND (@excluirId IS NULL OR id <> @excluirId)",
            new { codigo, excluirId }, ct);

    public Task<bool> TieneReferenciasAsync(int programaId, CancellationToken ct = default)
        => ExisteAsync(
            """
            -- Un programa bovino con categorías no se elimina; en los demás, las categorías (General, etapas) se eliminan junto
            -- con el programa y solo cuentan los animales, lotes y oficios que dependen de ellas.
            SELECT (SELECT COUNT(1) FROM CATEGORIA_ETAPA WHERE programa_id = @id
                      AND (SELECT usa_valorizacion_biologica FROM PROGRAMA WHERE id = @id) = 1)
                 + (SELECT COUNT(1) FROM ANIMAL WHERE categoria_id IN (SELECT id FROM CATEGORIA_ETAPA WHERE programa_id = @id))
                 + (SELECT COUNT(1) FROM OFICIO_VENTA WHERE programa_id = @id)
                 + (SELECT COUNT(1) FROM LOTE_POBLACION WHERE programa_id = @id)
            """,
            new { id = programaId }, ct);
}

internal sealed class GranjaProgramaRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<GranjaPrograma>(connection, transaction), IGranjaProgramaRepository
{
    public Task<IReadOnlyList<int>> GetProgramaIdsAsync(int granjaId, CancellationToken ct = default)
        => ListaAsync<int>("SELECT programa_id FROM GRANJA_PROGRAMA WHERE granja_id = @granjaId ORDER BY programa_id",
            new { granjaId }, ct);

    public Task<IReadOnlyList<int>> GetGranjaIdsAsync(int programaId, CancellationToken ct = default)
        => ListaAsync<int>("SELECT granja_id FROM GRANJA_PROGRAMA WHERE programa_id = @programaId ORDER BY granja_id",
            new { programaId }, ct);

    public async Task ReemplazarGranjasDeProgramaAsync(int programaId, IEnumerable<int> granjaIds, CancellationToken ct = default)
    {
        await EliminarPorProgramaAsync(programaId, ct);
        foreach (var granjaId in granjaIds.Distinct())
        {
            await InsertAsync(new GranjaPrograma { GranjaId = granjaId, ProgramaId = programaId }, ct);
        }
    }

    public Task EliminarPorGranjaAsync(int granjaId, CancellationToken ct = default)
        => EjecutarAsync("DELETE FROM GRANJA_PROGRAMA WHERE granja_id = @granjaId", new { granjaId }, ct);

    public Task EliminarPorProgramaAsync(int programaId, CancellationToken ct = default)
        => EjecutarAsync("DELETE FROM GRANJA_PROGRAMA WHERE programa_id = @programaId", new { programaId }, ct);
}

internal sealed class OrientacionRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<OrientacionProductiva>(connection, transaction), IOrientacionRepository
{
    public Task<OrientacionProductiva?> GetByNombreAsync(string nombre, CancellationToken ct = default)
        => UnoAsync<OrientacionProductiva>("SELECT * FROM ORIENTACION_PRODUCTIVA WHERE nombre = @nombre", new { nombre }, ct);
}

internal sealed class CategoriaRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<CategoriaEtapa>(connection, transaction), ICategoriaRepository
{
    public Task<IReadOnlyList<CategoriaEtapa>> GetByProgramaAsync(int programaId, CancellationToken ct = default)
        => ListaAsync<CategoriaEtapa>("SELECT * FROM CATEGORIA_ETAPA WHERE programa_id = @programaId ORDER BY id",
            new { programaId }, ct);

    public Task<CategoriaEtapa?> GetByNombreAsync(int programaId, string nombre, CancellationToken ct = default)
        => UnoAsync<CategoriaEtapa>(
            "SELECT * FROM CATEGORIA_ETAPA WHERE programa_id = @programaId AND nombre = @nombre",
            new { programaId, nombre }, ct);
}

internal sealed class TarifaProgramaRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<TarifaCategoriaPrograma>(connection, transaction), ITarifaProgramaRepository
{
    public Task<IReadOnlyList<TarifaCategoriaPrograma>> GetByProgramaAsync(int programaId, CancellationToken ct = default)
        => ListaAsync<TarifaCategoriaPrograma>(
            "SELECT * FROM TARIFA_CATEGORIA_PROGRAMA WHERE programa_id = @programaId ORDER BY es_default DESC, id", new { programaId }, ct);

    public Task<TarifaCategoriaPrograma?> GetByCategoriaAsync(int categoriaId, CancellationToken ct = default)
        => UnoAsync<TarifaCategoriaPrograma>("SELECT * FROM TARIFA_CATEGORIA_PROGRAMA WHERE categoria_id = @categoriaId", new { categoriaId }, ct);

    public Task EliminarPorProgramaAsync(int programaId, CancellationToken ct = default)
        => EjecutarAsync("DELETE FROM TARIFA_CATEGORIA_PROGRAMA WHERE programa_id = @programaId", new { programaId }, ct);
}