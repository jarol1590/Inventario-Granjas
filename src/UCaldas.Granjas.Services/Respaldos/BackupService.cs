using System.IO.Compression;
using System.Text;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;

namespace UCaldas.Granjas.Services.Respaldos;

public sealed record ResultadoRespaldo(string Ruta, long Bytes, DateTime Fecha);

public sealed record RespaldoInfo(string Ruta, DateTime Fecha, long Bytes);

public interface IBackupService
{
    /// <summary>
    /// Empaqueta la base de datos en <c>backup_AAAAMMDD_HHmm.zip</c>. La copia es consistente aunque la aplicación esté
    /// en uso y el archivo se publica de forma atómica: nunca queda un .zip a medias.
    /// </summary>
    /// <param name="carpetaDestino">Nula = la carpeta predeterminada de la configuración.</param>
    Task<ResultadoRespaldo> CrearRespaldoAsync(string? carpetaDestino = null, CancellationToken ct = default);

    /// <summary>Respaldo más reciente (<c>backup_*.zip</c>) de la carpeta; nulo si no hay ninguno.</summary>
    Task<RespaldoInfo?> UltimoRespaldoAsync(string? carpeta = null, CancellationToken ct = default);

    /// <summary>
    /// Restaura la base desde un respaldo. Antes valida el archivo y guarda una copia de seguridad de la base actual
    /// (<c>antes_de_restaurar_*.zip</c>). Si el archivo no es válido, la base actual no se toca.
    /// </summary>
    Task RestaurarAsync(string rutaZip, CancellationToken ct = default);

    /// <summary>Compacta la base (VACUUM) y devuelve su tamaño antes y después, en bytes.</summary>
    Task<(long Antes, long Despues)> CompactarAsync(CancellationToken ct = default);

    /// <summary>Respaldo automático al cerrar la aplicación si está activado. Nunca lanza: informa el resultado.</summary>
    Task<(bool Exito, string Mensaje)> RespaldoAlSalirAsync(CancellationToken ct = default);
}

public sealed class BackupService(IUnitOfWorkFactory fabrica, IDatabaseMaintenance mantenimiento, IReloj reloj) : IBackupService
{
    public const string NombreBaseEnZip = "granjas_semovientes.db";

    public Task<ResultadoRespaldo> CrearRespaldoAsync(string? carpetaDestino = null, CancellationToken ct = default)
        => CrearAsync(carpetaDestino, "backup", ct);

    public async Task<RespaldoInfo?> UltimoRespaldoAsync(string? carpeta = null, CancellationToken ct = default)
    {
        carpeta ??= await CarpetaConfiguradaAsync(ct);
        if (!Directory.Exists(carpeta))
        {
            return null;
        }

        return new DirectoryInfo(carpeta).EnumerateFiles("backup_*.zip")
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new RespaldoInfo(f.FullName, f.LastWriteTime, f.Length))
            .FirstOrDefault();
    }

    public async Task RestaurarAsync(string rutaZip, CancellationToken ct = default)
    {
        Regla.Exigir(File.Exists(rutaZip), "El archivo de respaldo no existe.");

        var temporal = Path.Combine(Path.GetTempPath(), "SistemaGranjas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporal);
        try
        {
            var extraida = Path.Combine(temporal, NombreBaseEnZip);
            try
            {
                using var zip = ZipFile.OpenRead(rutaZip);
                var entrada = zip.GetEntry(NombreBaseEnZip)
                    ?? throw Regla.Violada($"El archivo no es un respaldo del sistema: no contiene «{NombreBaseEnZip}».");
                entrada.ExtractToFile(extraida, overwrite: true);
            }
            catch (InvalidDataException)
            {
                throw Regla.Violada("El archivo no es un .zip válido.");
            }

            Regla.Exigir(
                await mantenimiento.EsBaseValidaAsync(extraida, ct),
                "El respaldo está dañado o es de una versión incompatible del sistema. No se modificó la base de datos actual.");

            // Red de seguridad: antes de sobrescribir se guarda la base actual.
            await CrearAsync(await CarpetaConfiguradaAsync(ct), "antes_de_restaurar", ct);
            await mantenimiento.ReemplazarBaseAsync(extraida, ct);
        }
        finally
        {
            try
            {
                Directory.Delete(temporal, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public Task<(long Antes, long Despues)> CompactarAsync(CancellationToken ct = default) => mantenimiento.CompactarAsync(ct);

    public async Task<(bool Exito, string Mensaje)> RespaldoAlSalirAsync(CancellationToken ct = default)
    {
        try
        {
            await using var uow = await fabrica.OpenReadOnlyAsync(ct);
            var configuracion = await uow.Configuracion.GetSistemaAsync(ct);
            if (configuracion is null || !configuracion.BackupAutomaticoSalir)
            {
                return (true, "El respaldo automático está desactivado.");
            }

            var resultado = await CrearRespaldoAsync(configuracion.RutaBackupPredeterminada, ct);
            return (true, $"Respaldo guardado en {resultado.Ruta}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Al cerrar la aplicación un respaldo fallido (disco lleno, carpeta sin permisos...) no debe impedir salir.
            return (false, $"No se pudo crear el respaldo automático: {ex.Message}");
        }
    }

    private async Task<ResultadoRespaldo> CrearAsync(string? carpetaDestino, string prefijo, CancellationToken ct)
    {
        var carpeta = string.IsNullOrWhiteSpace(carpetaDestino) ? await CarpetaConfiguradaAsync(ct) : carpetaDestino.Trim();
        var ahora = reloj.Ahora;

        try
        {
            Directory.CreateDirectory(carpeta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw Regla.Violada($"No se pudo usar la carpeta de respaldos «{carpeta}»: {ex.Message}");
        }

        var destino = Path.Combine(carpeta, $"{prefijo}_{ahora:yyyyMMdd_HHmm}.zip");
        for (var n = 2; File.Exists(destino); n++)
        {
            destino = Path.Combine(carpeta, $"{prefijo}_{ahora:yyyyMMdd_HHmm}_{n}.zip");
        }

        var copiaBase = Path.Combine(Path.GetTempPath(), $"granjas_{Guid.NewGuid():N}.db");
        var zipTemporal = destino + ".tmp";
        try
        {
            await mantenimiento.CopiarConsistenteAsync(copiaBase, ct);

            using (var zip = ZipFile.Open(zipTemporal, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(copiaBase, NombreBaseEnZip, CompressionLevel.Optimal);
                var manifiesto = zip.CreateEntry("manifiesto.txt");
                await using var escritor = new StreamWriter(manifiesto.Open(), new UTF8Encoding(false));
                await escritor.WriteLineAsync("Sistema Unificado de Gestión de Granjas - Universidad de Caldas");
                await escritor.WriteLineAsync($"Respaldo creado: {ahora:yyyy-MM-dd HH:mm:ss}");
                await escritor.WriteLineAsync($"Base de datos: {NombreBaseEnZip}");
            }

            File.Move(zipTemporal, destino);
        }
        catch
        {
            TryDelete(zipTemporal);
            throw;
        }
        finally
        {
            TryDelete(copiaBase);
        }

        return new ResultadoRespaldo(destino, new FileInfo(destino).Length, ahora);
    }

    private async Task<string> CarpetaConfiguradaAsync(CancellationToken ct)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var configuracion = await uow.Configuracion.GetSistemaAsync(ct);
        return string.IsNullOrWhiteSpace(configuracion?.RutaBackupPredeterminada)
            ? Rutas.CarpetaRespaldosPredeterminada
            : configuracion.RutaBackupPredeterminada;
    }

    private static void TryDelete(string ruta)
    {
        try
        {
            if (File.Exists(ruta))
            {
                File.Delete(ruta);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
