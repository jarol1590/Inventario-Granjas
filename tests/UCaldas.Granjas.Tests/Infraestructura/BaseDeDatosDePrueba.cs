using Dapper;
using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Data;
using UCaldas.Granjas.Domain.Abstractions;

namespace UCaldas.Granjas.Tests.Infraestructura;

/// <summary>
/// Base de datos SQLite real (esquema v2.2 + semillas) en un archivo temporal, exclusiva de cada prueba.
/// Se elimina al liberarla. Uso: <c>await using var db = await BaseDeDatosDePrueba.CrearAsync();</c>
/// </summary>
public sealed class BaseDeDatosDePrueba : IAsyncDisposable
{
    private BaseDeDatosDePrueba(string carpeta, SqliteConnectionFactory conexiones)
    {
        Carpeta = carpeta;
        Conexiones = conexiones;
        Uow = new UnitOfWorkFactory(conexiones);
    }

    public string Carpeta { get; }

    public string Ruta => Conexiones.DatabasePath;

    public SqliteConnectionFactory Conexiones { get; }

    public IUnitOfWorkFactory Uow { get; }

    public static async Task<BaseDeDatosDePrueba> CrearAsync()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "SistemaGranjasPruebas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        var conexiones = new SqliteConnectionFactory(Path.Combine(carpeta, "granjas.db"));
        await new DatabaseInitializer(conexiones).InitializeAsync();
        return new BaseDeDatosDePrueba(carpeta, conexiones);
    }

    public async Task<T> EscalarAsync<T>(string sql, object? parametros = null)
    {
        await using var conexion = await Conexiones.OpenAsync();
        return (await conexion.ExecuteScalarAsync<T>(sql, parametros))!;
    }

    public async Task<int> EjecutarAsync(string sql, object? parametros = null)
    {
        await using var conexion = await Conexiones.OpenAsync();
        return await conexion.ExecuteAsync(sql, parametros);
    }

    /// <summary>
    /// Quita de la matriz 2026 la fila de Levante para machos de 12 a 17 meses. Deja el hueco que tenía la tabla original:
    /// sirve para probar el camino "sin tarifa", que sigue existiendo cuando el usuario edita su matriz y deja un hueco.
    /// </summary>
    public Task<int> QuitarFilaDeLevanteParaMachosAsync()
        => EjecutarAsync("DELETE FROM TABLA_VALORIZACION_BASE WHERE sexo = 'Macho' AND edad_minima_meses = 12 AND edad_maxima_meses = 17");

    public async Task<IReadOnlyList<T>> ConsultarAsync<T>(string sql, object? parametros = null)
    {
        await using var conexion = await Conexiones.OpenAsync();
        return (await conexion.QueryAsync<T>(sql, parametros)).AsList();
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Archivo aún en uso por el sistema operativo: el directorio temporal se limpia después.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
