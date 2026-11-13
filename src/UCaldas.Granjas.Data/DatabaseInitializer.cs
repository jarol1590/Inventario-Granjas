using System.Text;
using Dapper;

namespace UCaldas.Granjas.Data;

public interface IDatabaseInitializer
{
    /// <summary>
    /// Crea la base de datos con su esquema y datos semilla si no existe y, si viene de una versión anterior, la migra.
    /// Idempotente: si ya está en la versión vigente no hace nada y nunca borra ni sobrescribe datos.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);
}

public sealed class DatabaseInitializer(IDbConnectionFactory conexiones) : IDatabaseInitializer
{
    /// <summary>
    /// Versión del esquema (<c>PRAGMA user_version</c>). 1 = esquema v2.1; 2 = esquema v2.2 (recuperación de contraseña en
    /// <c>CONFIGURACION_SISTEMA</c> y huecos de la matriz 2026 completados); 3 = esquema v2.3 (programas generalizados, tarifas de referencia
    /// por categoría y módulo de lotes); 4 = esquema v2.4 (documentos de lotes y edición de su compra).
    /// </summary>
    public const int VersionEsquemaActual = 4;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var carpeta = Path.GetDirectoryName(conexiones.DatabasePath);
        if (!string.IsNullOrEmpty(carpeta))
        {
            Directory.CreateDirectory(carpeta);
        }

        await using var connection = await conexiones.OpenAsync(ct);
        var version = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition("PRAGMA user_version;", cancellationToken: ct));

        if (version == VersionEsquemaActual)
        {
            return;
        }

        if (version is not (0 or 1 or 2 or 3))
        {
            throw new InvalidOperationException(
                $"La base de datos tiene una versión de esquema desconocida ({version}); se esperaba {VersionEsquemaActual}. " +
                "No se modificó ningún dato.");
        }

        // Todo se aplica en una sola transacción: o queda completo o no queda nada.
        await using var transaction = connection.BeginTransaction();
        if (version == 0)
        {
            // Base nueva: el esquema ya incluye la v2.4 y las semillas llevan la matriz completa.
            await connection.ExecuteAsync(new CommandDefinition(LeerScript("schema.sql"), transaction: transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(LeerScript("seed.sql"), transaction: transaction, cancellationToken: ct));
        }
        else
        {
            // Bases anteriores: se aplican en orden las migraciones que falten, sin tocar los datos existentes.
            if (version == 1)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v2.sql"), transaction: transaction, cancellationToken: ct));
            }

            if (version <= 2)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v3.sql"), transaction: transaction, cancellationToken: ct));
            }

            await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v4.sql"), transaction: transaction, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            $"PRAGMA user_version = {VersionEsquemaActual};", transaction: transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
    }

    private static string LeerScript(string nombre)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        var recurso = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("." + nombre, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException($"No se encontró el recurso incrustado {recurso}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
