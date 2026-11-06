using Microsoft.Data.Sqlite;

namespace UCaldas.Granjas.Data;

/// <summary>Ubicaciones estándar de los archivos del sistema.</summary>
public static class RutasSistema
{
    public const string NombreBaseDatos = "granjas_semovientes.db";

    /// <summary>Variable de entorno que redirige la carpeta de datos (pruebas manuales, instalaciones portátiles).</summary>
    public const string VariableCarpetaDatos = "SISTEMA_GRANJAS_DATOS";

    /// <summary>%LOCALAPPDATA%\SistemaGranjas — nunca dentro de OneDrive — salvo que <see cref="VariableCarpetaDatos"/> la redirija.</summary>
    public static string CarpetaDatos =>
        Environment.GetEnvironmentVariable(VariableCarpetaDatos) is { Length: > 0 } personalizada
            ? personalizada
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SistemaGranjas");

    public static string RutaBaseDatos => Path.Combine(CarpetaDatos, NombreBaseDatos);

    public static string CarpetaLogs => Path.Combine(CarpetaDatos, "logs");
}

/// <summary>Abre conexiones a la base SQLite local.</summary>
public interface IDbConnectionFactory
{
    string DatabasePath { get; }

    /// <summary>Abre una conexión nueva con las claves foráneas activadas.</summary>
    Task<SqliteConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        SqliteMapping.Initialize();
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
