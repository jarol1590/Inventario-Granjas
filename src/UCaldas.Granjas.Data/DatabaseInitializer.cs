using System.Text;
using Dapper;

namespace UCaldas.Granjas.Data;

public interface IDatabaseInitializer
{
    /// <summary>
    /// Crea la base de datos con su esquema y datos semilla si no existe. Idempotente: si ya está
    /// en la versión vigente no hace nada y nunca borra ni sobrescribe datos.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);
}

public sealed class DatabaseInitializer(IDbConnectionFactory conexiones) : IDatabaseInitializer
{
    /// <summary>Versión del esquema (<c>PRAGMA user_version</c>). 1 = esquema v2.1.</summary>
    public const int VersionEsquemaActual = 1;

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

        if (version != 0)
        {
            throw new InvalidOperationException(
                $"La base de datos tiene una versión de esquema desconocida ({version}); se esperaba {VersionEsquemaActual}. " +
                "No se modificó ningún dato.");
        }

        // Esquema, semillas y versión se aplican en una sola transacción: o queda todo o no queda nada.
        await using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync(new CommandDefinition(LeerScript("schema.sql"), transaction: transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(LeerScript("seed.sql"), transaction: transaction, cancellationToken: ct));
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
