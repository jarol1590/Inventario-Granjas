using System.Security.Cryptography;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.Services.Seguridad;

public interface IAuthService
{
    /// <summary>false en el primer arranque: todavía no se ha creado la contraseña.</summary>
    Task<bool> EstaConfiguradoAsync(CancellationToken ct = default);

    /// <summary>Primer arranque: crea la contraseña y la configuración inicial del sistema.</summary>
    Task CrearContrasenaAsync(string contrasena, string confirmacion, CancellationToken ct = default);

    Task<bool> VerificarAsync(string contrasena, CancellationToken ct = default);

    Task CambiarContrasenaAsync(string actual, string nueva, string confirmacion, CancellationToken ct = default);
}

/// <summary>
/// Acceso por contraseña única (el sistema es monousuario). La contraseña nunca se guarda: se almacena un hash
/// PBKDF2-SHA256 con sal propia y se compara en tiempo constante.
/// </summary>
public sealed class AuthService(IUnitOfWorkFactory fabrica, IReloj reloj) : IAuthService
{
    public const int LongitudMinima = 4;

    private const string Algoritmo = "PBKDF2-SHA256";
    private const int Iteraciones = 200_000;
    private const int LongitudSal = 16;
    private const int LongitudHash = 32;

    public async Task<bool> EstaConfiguradoAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return await uow.Configuracion.GetSistemaAsync(ct) is not null;
    }

    public async Task CrearContrasenaAsync(string contrasena, string confirmacion, CancellationToken ct = default)
    {
        ValidarNueva(contrasena, confirmacion);

        await using var uow = await fabrica.BeginAsync(ct);
        Regla.Exigir(await uow.Configuracion.GetSistemaAsync(ct) is null, "La contraseña ya fue creada.");

        await uow.Configuracion.GuardarSistemaAsync(new ConfiguracionSistema
        {
            PasswordHash = GenerarHash(contrasena),
            BackupAutomaticoSalir = true,
            RutaBackupPredeterminada = Rutas.CarpetaRespaldosPredeterminada,

            // La valorización parte del último fin de mes ya terminado: no se inventa historia anterior al sistema.
            UltimoCierreValorizacion = EdadCalculator.FinDeMes(reloj.Hoy.AddMonths(-1)),
        }, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<bool> VerificarAsync(string contrasena, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(contrasena))
        {
            return false;
        }

        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var configuracion = await uow.Configuracion.GetSistemaAsync(ct);
        return configuracion is not null && CoincideConHash(contrasena, configuracion.PasswordHash);
    }

    public async Task CambiarContrasenaAsync(string actual, string nueva, string confirmacion, CancellationToken ct = default)
    {
        ValidarNueva(nueva, confirmacion);

        await using var uow = await fabrica.BeginAsync(ct);
        var configuracion = await uow.Configuracion.GetSistemaAsync(ct) ?? throw Regla.Violada("Aún no se ha creado una contraseña.");
        Regla.Exigir(CoincideConHash(actual ?? string.Empty, configuracion.PasswordHash), "La contraseña actual no es correcta.");

        configuracion.PasswordHash = GenerarHash(nueva);
        await uow.Configuracion.GuardarSistemaAsync(configuracion, ct);
        await uow.CommitAsync(ct);
    }

    private static void ValidarNueva(string contrasena, string confirmacion)
    {
        Regla.Exigir(!string.IsNullOrEmpty(contrasena) && contrasena.Length >= LongitudMinima,
            $"La contraseña debe tener al menos {LongitudMinima} caracteres.");
        Regla.Exigir(contrasena == confirmacion, "La confirmación no coincide con la contraseña.");
    }

    /// <summary>Formato almacenado: <c>PBKDF2-SHA256$iteraciones$sal$hash</c> (sal y hash en Base64).</summary>
    public static string GenerarHash(string contrasena)
    {
        var sal = RandomNumberGenerator.GetBytes(LongitudSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, Iteraciones, HashAlgorithmName.SHA256, LongitudHash);
        return $"{Algoritmo}${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public static bool CoincideConHash(string contrasena, string almacenado)
    {
        var partes = almacenado.Split('$');
        if (partes.Length != 4 || partes[0] != Algoritmo || !int.TryParse(partes[1], out var iteraciones) || iteraciones is < 1 or > 10_000_000)
        {
            return false;
        }

        try
        {
            var sal = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
