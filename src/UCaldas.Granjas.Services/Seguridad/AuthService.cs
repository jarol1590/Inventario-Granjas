using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.Services.Seguridad;

/// <summary>Estado de la recuperación de contraseña: si está configurada y cuál es la pregunta secreta elegida.</summary>
public sealed record EstadoDeRecuperacion(bool Configurada, string? PreguntaSecreta);

public interface IAuthService
{
    /// <summary>false en el primer arranque: todavía no se ha creado la contraseña.</summary>
    Task<bool> EstaConfiguradoAsync(CancellationToken ct = default);

    /// <summary>
    /// Primer arranque: crea la contraseña, guarda la pregunta secreta con su respuesta y genera la clave de recuperación
    /// (16 caracteres). La clave se devuelve una sola vez; en la base solo queda su hash.
    /// </summary>
    Task<string> CrearAccesoAsync(string contrasena, string confirmacion, string preguntaSecreta, string respuesta, CancellationToken ct = default);

    /// <summary>Crea solo la contraseña, sin recuperación (acceso heredado). Se puede completar luego con <see cref="ConfigurarRecuperacionAsync"/>.</summary>
    Task CrearContrasenaAsync(string contrasena, string confirmacion, CancellationToken ct = default);

    Task<bool> VerificarAsync(string contrasena, CancellationToken ct = default);

    Task CambiarContrasenaAsync(string actual, string nueva, string confirmacion, CancellationToken ct = default);

    Task<EstadoDeRecuperacion> ObtenerEstadoDeRecuperacionAsync(CancellationToken ct = default);

    /// <summary>
    /// Restablece la contraseña si la respuesta a la pregunta secreta es correcta, sin tocar el resto de la base.
    /// Devuelve una clave de recuperación NUEVA (la anterior deja de servir).
    /// </summary>
    Task<string> RestablecerConRespuestaAsync(string respuesta, string nueva, string confirmacion, CancellationToken ct = default);

    /// <summary>Restablece la contraseña si la clave de recuperación es correcta. Devuelve una clave NUEVA (la usada deja de servir).</summary>
    Task<string> RestablecerConClaveAsync(string clave, string nueva, string confirmacion, CancellationToken ct = default);

    /// <summary>Configura o cambia la pregunta secreta y genera una clave de recuperación nueva. Exige la contraseña actual.</summary>
    Task<string> ConfigurarRecuperacionAsync(string contrasenaActual, string preguntaSecreta, string respuesta, CancellationToken ct = default);
}

/// <summary>
/// Acceso por contraseña única (el sistema es monousuario y funciona sin conexión). Nada sensible se guarda en claro: la
/// contraseña, la respuesta a la pregunta secreta y la clave de recuperación se almacenan como hash PBKDF2-SHA256 con sal
/// propia (200.000 iteraciones) y se comparan en tiempo constante. La recuperación no necesita red ni correo: se valida
/// contra esos hashes, de modo que se puede redefinir la contraseña sin borrar la base de datos.
/// </summary>
public sealed class AuthService(IUnitOfWorkFactory fabrica, IReloj reloj) : IAuthService
{
    public const int LongitudMinima = 4;

    /// <summary>Largo de la clave de recuperación (sin los guiones de presentación).</summary>
    public const int LongitudClave = 16;

    public const int LongitudMinimaRespuesta = 3;

    /// <summary>Preguntas entre las que se elige al crear el acceso.</summary>
    public static readonly IReadOnlyList<string> PreguntasSecretas =
    [
        "¿Cuál es el nombre de su primera mascota?",
        "¿En qué municipio nació su madre?",
        "¿Cómo se llamaba su escuela de primaria?",
        "¿Cuál era el apodo que tenía en la infancia?",
        "¿Cuál es el nombre de su mejor amigo(a) de la infancia?",
        "¿Cuál es su comida favorita?",
        "¿Cómo se llamaba la primera finca o granja que visitó?",
        "¿Cuál es el nombre de su primer jefe?",
    ];

    // Sin I, L, O, 0 ni 1 para que la clave no se confunda al copiarla a mano: 31 símbolos ≈ 79 bits de entropía en 16.
    private const string AlfabetoClave = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int LongitudMaximaPregunta = 200;

    private const string Algoritmo = "PBKDF2-SHA256";
    private const int Iteraciones = 200_000;
    private const int LongitudSal = 16;
    private const int LongitudHash = 32;

    public async Task<bool> EstaConfiguradoAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return await uow.Configuracion.GetSistemaAsync(ct) is not null;
    }

    public async Task<string> CrearAccesoAsync(
        string contrasena, string confirmacion, string preguntaSecreta, string respuesta, CancellationToken ct = default)
    {
        ValidarNueva(contrasena, confirmacion);
        var pregunta = ValidarPregunta(preguntaSecreta);
        var respuestaNormalizada = ValidarRespuesta(respuesta);

        await using var uow = await fabrica.BeginAsync(ct);
        Regla.Exigir(await uow.Configuracion.GetSistemaAsync(ct) is null, "La contraseña ya fue creada.");

        var clave = GenerarClaveDeRecuperacion();
        var configuracion = NuevaConfiguracion(contrasena);
        AplicarRecuperacion(configuracion, pregunta, respuestaNormalizada, clave);
        await uow.Configuracion.GuardarSistemaAsync(configuracion, ct);
        await uow.CommitAsync(ct);
        return clave;
    }

    public async Task CrearContrasenaAsync(string contrasena, string confirmacion, CancellationToken ct = default)
    {
        ValidarNueva(contrasena, confirmacion);

        await using var uow = await fabrica.BeginAsync(ct);
        Regla.Exigir(await uow.Configuracion.GetSistemaAsync(ct) is null, "La contraseña ya fue creada.");

        await uow.Configuracion.GuardarSistemaAsync(NuevaConfiguracion(contrasena), ct);
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

    public async Task<EstadoDeRecuperacion> ObtenerEstadoDeRecuperacionAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var configuracion = await uow.Configuracion.GetSistemaAsync(ct);
        return configuracion is not null && TieneRecuperacion(configuracion)
            ? new EstadoDeRecuperacion(true, configuracion.PreguntaSecreta)
            : new EstadoDeRecuperacion(false, null);
    }

    public Task<string> RestablecerConRespuestaAsync(string respuesta, string nueva, string confirmacion, CancellationToken ct = default)
        => RestablecerAsync(
            nueva, confirmacion,
            configuracion => CoincideConHash(NormalizarRespuesta(respuesta), configuracion.RespuestaHash!),
            "La respuesta a la pregunta secreta no es correcta.",
            ct);

    public async Task<string> RestablecerConClaveAsync(string clave, string nueva, string confirmacion, CancellationToken ct = default)
    {
        var normalizada = NormalizarClave(clave);
        Regla.Exigir(
            normalizada.Length == LongitudClave,
            $"La clave de recuperación tiene {LongitudClave} caracteres (por ejemplo, ABCD-EFGH-JKMN-PQRS).");

        return await RestablecerAsync(
            nueva, confirmacion,
            configuracion => CoincideConHash(normalizada, configuracion.RecoveryKeyHash!),
            "La clave de recuperación no es correcta.",
            ct);
    }

    public async Task<string> ConfigurarRecuperacionAsync(
        string contrasenaActual, string preguntaSecreta, string respuesta, CancellationToken ct = default)
    {
        var pregunta = ValidarPregunta(preguntaSecreta);
        var respuestaNormalizada = ValidarRespuesta(respuesta);

        await using var uow = await fabrica.BeginAsync(ct);
        var configuracion = await uow.Configuracion.GetSistemaAsync(ct) ?? throw Regla.Violada("Aún no se ha creado una contraseña.");
        Regla.Exigir(CoincideConHash(contrasenaActual ?? string.Empty, configuracion.PasswordHash), "La contraseña actual no es correcta.");

        var clave = GenerarClaveDeRecuperacion();
        AplicarRecuperacion(configuracion, pregunta, respuestaNormalizada, clave);
        await uow.Configuracion.GuardarSistemaAsync(configuracion, ct);
        await uow.CommitAsync(ct);
        return clave;
    }

    /// <summary>Comprobaciones y cambio comunes a los dos caminos de restablecimiento; el cambio es todo o nada.</summary>
    private async Task<string> RestablecerAsync(
        string nueva, string confirmacion, Func<ConfiguracionSistema, bool> credencialValida, string mensajeSiFalla, CancellationToken ct)
    {
        ValidarNueva(nueva, confirmacion);

        await using var uow = await fabrica.BeginAsync(ct);
        var configuracion = await uow.Configuracion.GetSistemaAsync(ct) ?? throw Regla.Violada("Aún no se ha creado una contraseña.");
        Regla.Exigir(
            TieneRecuperacion(configuracion),
            "Esta instalación no tiene configurada la pregunta secreta ni la clave de recuperación (se creó antes de que existieran), " +
            "así que la contraseña no se puede restablecer desde aquí.");
        Regla.Exigir(credencialValida(configuracion), mensajeSiFalla);

        // La contraseña cambia y la clave de recuperación se renueva: la usada (o la que pudo verse) ya no sirve.
        var claveNueva = GenerarClaveDeRecuperacion();
        configuracion.PasswordHash = GenerarHash(nueva);
        configuracion.RecoveryKeyHash = GenerarHash(NormalizarClave(claveNueva));
        await uow.Configuracion.GuardarSistemaAsync(configuracion, ct);
        await uow.CommitAsync(ct);
        return claveNueva;
    }

    private ConfiguracionSistema NuevaConfiguracion(string contrasena) => new()
    {
        PasswordHash = GenerarHash(contrasena),
        BackupAutomaticoSalir = true,
        RutaBackupPredeterminada = Rutas.CarpetaRespaldosPredeterminada,

        // La valorización parte del último fin de mes ya terminado: no se inventa historia anterior al sistema.
        UltimoCierreValorizacion = EdadCalculator.FinDeMes(reloj.Hoy.AddMonths(-1)),
    };

    private static bool TieneRecuperacion(ConfiguracionSistema configuracion)
        => !string.IsNullOrWhiteSpace(configuracion.PreguntaSecreta)
           && !string.IsNullOrEmpty(configuracion.RespuestaHash)
           && !string.IsNullOrEmpty(configuracion.RecoveryKeyHash);

    private static void AplicarRecuperacion(ConfiguracionSistema configuracion, string pregunta, string respuestaNormalizada, string clave)
    {
        configuracion.PreguntaSecreta = pregunta;
        configuracion.RespuestaHash = GenerarHash(respuestaNormalizada);
        configuracion.RecoveryKeyHash = GenerarHash(NormalizarClave(clave));
    }

    private static void ValidarNueva(string contrasena, string confirmacion)
    {
        Regla.Exigir(!string.IsNullOrEmpty(contrasena) && contrasena.Length >= LongitudMinima,
            $"La contraseña debe tener al menos {LongitudMinima} caracteres.");
        Regla.Exigir(contrasena == confirmacion, "La confirmación no coincide con la contraseña.");
    }

    private static string ValidarPregunta(string? pregunta)
    {
        var limpia = pregunta?.Trim() ?? string.Empty;
        Regla.Exigir(limpia.Length > 0, "Elija una pregunta secreta.");
        Regla.Exigir(limpia.Length <= LongitudMaximaPregunta, $"La pregunta secreta no puede pasar de {LongitudMaximaPregunta} caracteres.");
        return limpia;
    }

    private static string ValidarRespuesta(string? respuesta)
    {
        var normalizada = NormalizarRespuesta(respuesta);
        Regla.Exigir(normalizada.Length >= LongitudMinimaRespuesta, $"La respuesta secreta debe tener al menos {LongitudMinimaRespuesta} caracteres.");
        return normalizada;
    }

    // ---- Clave de recuperación y respuesta ---------------------------------------------------------------------------

    /// <summary>16 caracteres al azar (generador criptográfico) tomados de un alfabeto sin símbolos ambiguos.</summary>
    public static string GenerarClaveDeRecuperacion()
    {
        var caracteres = new char[LongitudClave];
        for (var i = 0; i < caracteres.Length; i++)
        {
            caracteres[i] = AlfabetoClave[RandomNumberGenerator.GetInt32(AlfabetoClave.Length)];
        }

        return new string(caracteres);
    }

    /// <summary>Presenta la clave en grupos de cuatro ("ABCD-EFGH-JKMN-PQRS") para leerla y copiarla mejor.</summary>
    public static string FormatearClave(string clave)
    {
        var limpia = NormalizarClave(clave);
        return string.Join('-', Enumerable.Range(0, (limpia.Length + 3) / 4).Select(i => limpia.Substring(i * 4, Math.Min(4, limpia.Length - i * 4))));
    }

    /// <summary>Quita guiones, espacios y cualquier otro separador y pasa a mayúsculas: se acepta como la copie el usuario.</summary>
    public static string NormalizarClave(string? clave)
        => string.Concat((clave ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();

    /// <summary>
    /// Compara respuestas sin importar mayúsculas, tildes ni espacios de más: "  Peña  Roja " y "pena roja" son la misma.
    /// </summary>
    public static string NormalizarRespuesta(string? respuesta)
    {
        var descompuesta = (respuesta ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesta.Length);
        var espacioPendiente = false;
        foreach (var c in descompuesta)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                espacioPendiente = sb.Length > 0;
                continue;
            }

            if (espacioPendiente)
            {
                sb.Append(' ');
                espacioPendiente = false;
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // ---- Hash --------------------------------------------------------------------------------------------------------

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
