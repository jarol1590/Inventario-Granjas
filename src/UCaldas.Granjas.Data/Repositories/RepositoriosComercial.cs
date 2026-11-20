using Dapper;
using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Data.Repositories;

internal sealed class ConvenioRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<Convenio>(connection, transaction), IConvenioRepository
{
    public Task<Convenio?> GetByNitAsync(string nit, CancellationToken ct = default)
        => UnoAsync<Convenio>("SELECT * FROM CONVENIO WHERE nit = @nit", new { nit }, ct);
}

internal sealed class ClienteRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<Cliente>(connection, transaction), IClienteRepository
{
    public Task<Cliente?> GetByNitAsync(string nitCc, CancellationToken ct = default)
        => UnoAsync<Cliente>("SELECT * FROM CLIENTE WHERE nit_cc = @nitCc", new { nitCc }, ct);
}

internal sealed class CatalogoProductoRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<CatalogoProducto>(connection, transaction), ICatalogoProductoRepository
{
    public Task<CatalogoProducto?> GetByCodigoAsync(string codigo, CancellationToken ct = default)
        => UnoAsync<CatalogoProducto>("SELECT * FROM CATALOGO_PRODUCTO WHERE codigo = @codigo", new { codigo }, ct);

    public Task<IReadOnlyList<CatalogoProducto>> GetByProgramaAsync(int programaId, CancellationToken ct = default)
        => ListaAsync<CatalogoProducto>("SELECT * FROM CATALOGO_PRODUCTO WHERE programa_id = @programaId ORDER BY nombre", new { programaId }, ct);
}

/// <summary>La configuración es una fila única (id = 1) que siembra la base: solo se lee y se actualiza.</summary>
internal sealed class ConfiguracionRemisionesRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<ConfiguracionRemisiones>(connection, transaction), IConfiguracionRemisionesRepository
{
    public async Task<ConfiguracionRemisiones> GetAsync(CancellationToken ct = default)
        => await UnoAsync<ConfiguracionRemisiones>("SELECT * FROM CONFIGURACION_REMISIONES WHERE id = 1", null, ct)
           ?? throw new InvalidOperationException("Falta la fila de configuración del módulo Remisiones (CONFIGURACION_REMISIONES).");

    public Task GuardarAsync(ConfiguracionRemisiones configuracion, CancellationToken ct = default)
    {
        configuracion.Id = 1;
        return UpdateAsync(configuracion, ct);
    }
}

internal sealed class ReciboCajaRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<ReciboCaja>(connection, transaction), IReciboCajaRepository
{
    public Task<ReciboCaja?> GetByRemisionAsync(int remisionId, CancellationToken ct = default)
        => UnoAsync<ReciboCaja>("SELECT * FROM RECIBO_CAJA WHERE remision_id = @remisionId", new { remisionId }, ct);

    public async Task<int> GetMaxNumeroAsync(CancellationToken ct = default)
        => (int)await EscalarAsync("SELECT COALESCE(MAX(numero_recibo), 0) FROM RECIBO_CAJA", null, ct);
}
