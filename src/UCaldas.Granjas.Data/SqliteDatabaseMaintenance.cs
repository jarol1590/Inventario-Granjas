using Dapper;
using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Domain.Abstractions;

namespace UCaldas.Granjas.Data;

public sealed class SqliteDatabaseMaintenance(IDbConnectionFactory conexiones) : IDatabaseMaintenance
{
    private static readonly string[] TablasRequeridas = ["ANIMAL", "GRANJA", "REMISION", "MOVIMIENTO_INVENTARIO", "TABLA_VALORIZACION_BASE"];

    public string RutaBaseDatos => conexiones.DatabasePath;

    public async Task CopiarConsistenteAsync(string destino, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destino);
        if (File.Exists(destino))
        {
            throw new IOException($"El archivo de destino ya existe: {destino}");
        }

        await using var conexion = await conexiones.OpenAsync(ct);
        await conexion.ExecuteAsync(new CommandDefinition("VACUUM INTO @destino", new { destino }, cancellationToken: ct));
    }

    public async Task<(long Antes, long Despues)> CompactarAsync(CancellationToken ct = default)
    {
        var antes = new FileInfo(RutaBaseDatos).Length;
        await using (var conexion = await conexiones.OpenAsync(ct))
        {
            await conexion.ExecuteAsync(new CommandDefinition("VACUUM", cancellationToken: ct));
        }

        return (antes, new FileInfo(RutaBaseDatos).Length);
    }

    public async Task<bool> EsBaseValidaAsync(string ruta, CancellationToken ct = default)
    {
        if (!File.Exists(ruta))
        {
            return false;
        }

        // Conexión aparte y sin pool: no debe interferir con la base en uso ni dejar el archivo bloqueado.
        var cadena = new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        try
        {
            await using var conexion = new SqliteConnection(cadena);
            await conexion.OpenAsync(ct);

            var integridad = await conexion.ExecuteScalarAsync<string>(new CommandDefinition("PRAGMA integrity_check;", cancellationToken: ct));
            if (!string.Equals(integridad, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var version = await conexion.ExecuteScalarAsync<long>(new CommandDefinition("PRAGMA user_version;", cancellationToken: ct));
            if (version < 1 || version > DatabaseInitializer.VersionEsquemaActual)
            {
                return false;
            }

            var tablas = (await conexion.QueryAsync<string>(new CommandDefinition(
                "SELECT name FROM sqlite_master WHERE type = 'table'", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return TablasRequeridas.All(tablas.Contains);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public async Task ReemplazarBaseAsync(string origen, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // El pool mantiene abiertos los archivos: hay que soltarlos antes de sobrescribir.
        SqliteConnection.ClearAllPools();
        File.Copy(origen, RutaBaseDatos, overwrite: true);
        foreach (var sufijo in new[] { "-journal", "-wal", "-shm" })
        {
            var auxiliar = RutaBaseDatos + sufijo;
            if (File.Exists(auxiliar))
            {
                File.Delete(auxiliar);
            }
        }

        // Un respaldo hecho con una versión anterior del sistema se actualiza de inmediato, para que la aplicación
        // (que ya espera el esquema vigente) pueda seguir usándolo sin reiniciar.
        await new DatabaseInitializer(conexiones).InitializeAsync(ct);
    }
}
