using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Services.Seguridad;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Seguridad;

/// <summary>Recuperación de contraseña 100 % local: pregunta secreta y clave de recuperación, siempre guardadas como hash.</summary>
public class RecuperacionDeContrasenaTests
{
    private const string Pregunta = "¿Cuál es el nombre de su primera mascota?";

    private static AuthService Crear(BaseDeDatosDePrueba db) => new(db.Uow, new RelojFijo(Datos.F("2026-11-06")));

    /// <summary>Acceso completo: contraseña "Granjas2026", respuesta "Peña Roja" y la clave que se devolvió.</summary>
    private static async Task<(AuthService Auth, string Clave)> AccesoCompletoAsync(BaseDeDatosDePrueba db)
    {
        var auth = Crear(db);
        var clave = await auth.CrearAccesoAsync("Granjas2026", "Granjas2026", Pregunta, "Peña Roja");
        return (auth, clave);
    }

    // ---- Creación del acceso ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Crear_el_acceso_guarda_los_tres_hashes_y_devuelve_una_clave_de_16_caracteres_que_no_se_guarda()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var (auth, clave) = await AccesoCompletoAsync(db);

        clave.Should().HaveLength(AuthService.LongitudClave).And.MatchRegex("^[A-HJKMNP-Z2-9]{16}$");
        (await auth.EstaConfiguradoAsync()).Should().BeTrue();
        (await auth.VerificarAsync("Granjas2026")).Should().BeTrue();

        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var configuracion = (await uow.Configuracion.GetSistemaAsync())!;
        configuracion.PreguntaSecreta.Should().Be(Pregunta);
        foreach (var hash in new[] { configuracion.PasswordHash, configuracion.RespuestaHash!, configuracion.RecoveryKeyHash! })
        {
            hash.Should().StartWith("PBKDF2-SHA256$");
        }

        // Nada sensible queda en claro en ninguna columna de la tabla.
        var fila = string.Join('|', await db.ConsultarAsync<string>(
            "SELECT password_hash || '|' || respuesta_hash || '|' || recovery_key_hash || '|' || pregunta_secreta FROM CONFIGURACION_SISTEMA"));
        fila.Should().NotContain("Granjas2026").And.NotContain(clave).And.NotContain("Peña Roja").And.NotContain("pena roja");
        configuracion.RespuestaHash.Should().NotBe(configuracion.RecoveryKeyHash).And.NotBe(configuracion.PasswordHash);
    }

    [Fact]
    public void Cada_clave_generada_es_distinta_y_usa_solo_el_alfabeto_sin_simbolos_ambiguos()
    {
        var claves = Enumerable.Range(0, 200).Select(_ => AuthService.GenerarClaveDeRecuperacion()).ToList();

        claves.Distinct().Should().HaveCount(200);
        claves.Should().OnlyContain(c => c.Length == 16 && c.All(ch => "ABCDEFGHJKMNPQRSTUVWXYZ23456789".Contains(ch)));
        claves.SelectMany(c => c).Should().NotContain(['0', 'O', '1', 'I', 'L']);
    }

    [Theory]
    [InlineData("", "Peña", "Elija una pregunta secreta")]
    [InlineData("   ", "Peña", "Elija una pregunta secreta")]
    [InlineData(Pregunta, "", "al menos 3 caracteres")]
    [InlineData(Pregunta, "  a ", "al menos 3 caracteres")]
    public async Task Crear_el_acceso_exige_pregunta_y_una_respuesta_de_al_menos_3_caracteres(string pregunta, string respuesta, string mensaje)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).CrearAccesoAsync("Granjas2026", "Granjas2026", pregunta, respuesta);

        await act.Should().ThrowAsync<ValidationException>().WithMessage($"*{mensaje}*");
        (await Crear(db).EstaConfiguradoAsync()).Should().BeFalse("no se guardó nada");
    }

    [Fact]
    public async Task Crear_el_acceso_sigue_validando_la_contrasena_y_no_se_puede_repetir()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);

        await ((Func<Task>)(() => auth.CrearAccesoAsync("abc", "abc", Pregunta, "Peña"))).Should().ThrowAsync<ValidationException>().WithMessage("*al menos 4*");
        await ((Func<Task>)(() => auth.CrearAccesoAsync("Granjas2026", "Otra", Pregunta, "Peña"))).Should().ThrowAsync<ValidationException>().WithMessage("*confirmación no coincide*");

        await auth.CrearAccesoAsync("Granjas2026", "Granjas2026", Pregunta, "Peña");
        await ((Func<Task>)(() => auth.CrearAccesoAsync("Otra1234", "Otra1234", Pregunta, "Peña"))).Should().ThrowAsync<ValidationException>().WithMessage("*ya fue creada*");
        (await auth.VerificarAsync("Granjas2026")).Should().BeTrue();
    }

    // ---- Normalización -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("  Peña   Roja ", "pena roja")]
    [InlineData("PEÑA ROJA", "pena roja")]
    [InlineData("Peña\tRoja", "pena roja")]
    [InlineData("Él", "el")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void La_respuesta_se_compara_sin_mayusculas_tildes_ni_espacios_de_mas(string? entrada, string esperado)
        => AuthService.NormalizarRespuesta(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("abcd-efgh-jkmn-pqrs", "ABCDEFGHJKMNPQRS")]
    [InlineData(" ABCD EFGH JKMN PQRS ", "ABCDEFGHJKMNPQRS")]
    [InlineData("ABCDEFGHJKMNPQRS", "ABCDEFGHJKMNPQRS")]
    [InlineData(null, "")]
    public void La_clave_se_acepta_como_la_copie_el_usuario(string? entrada, string esperado)
        => AuthService.NormalizarClave(entrada).Should().Be(esperado);

    [Fact]
    public void La_clave_se_presenta_en_cuatro_grupos_de_cuatro()
        => AuthService.FormatearClave("ABCDEFGHJKMNPQRS").Should().Be("ABCD-EFGH-JKMN-PQRS");

    // ---- Restablecer con la pregunta secreta -------------------------------------------------------------------------

    [Fact]
    public async Task La_respuesta_correcta_aunque_cambie_en_mayusculas_o_tildes_permite_redefinir_la_contrasena()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (auth, clave) = await AccesoCompletoAsync(db);
        await db.EjecutarAsync("INSERT INTO GRANJA (codigo, nombre) VALUES ('G-99', 'Granja de prueba')");

        var nueva = await auth.RestablecerConRespuestaAsync("  PENA   roja ", "Nueva2027", "Nueva2027");

        (await auth.VerificarAsync("Nueva2027")).Should().BeTrue();
        (await auth.VerificarAsync("Granjas2026")).Should().BeFalse("la contraseña anterior ya no sirve");
        nueva.Should().HaveLength(16).And.NotBe(clave, "al restablecer se emite una clave de recuperación nueva");
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(4, "la base de datos no se toca: no se borra nada");
    }

    [Fact]
    public async Task Una_respuesta_incorrecta_no_cambia_nada()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (auth, clave) = await AccesoCompletoAsync(db);

        var act = () => auth.RestablecerConRespuestaAsync("Pelusa", "Nueva2027", "Nueva2027");

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*respuesta a la pregunta secreta no es correcta*");
        (await auth.VerificarAsync("Granjas2026")).Should().BeTrue();
        (await auth.VerificarAsync("Nueva2027")).Should().BeFalse();
        // La clave original sigue valiendo.
        await auth.RestablecerConClaveAsync(clave, "Otra2028", "Otra2028");
        (await auth.VerificarAsync("Otra2028")).Should().BeTrue();
    }

    // ---- Restablecer con la clave de recuperación --------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task La_clave_correcta_con_o_sin_guiones_ni_mayusculas_permite_redefinir_la_contrasena(bool comoLaMuestraLaPantalla)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (auth, clave) = await AccesoCompletoAsync(db);
        var escrita = comoLaMuestraLaPantalla ? AuthService.FormatearClave(clave).ToLowerInvariant() : clave;

        var nueva = await auth.RestablecerConClaveAsync(escrita, "Nueva2027", "Nueva2027");

        (await auth.VerificarAsync("Nueva2027")).Should().BeTrue();
        (await auth.VerificarAsync("Granjas2026")).Should().BeFalse();
        nueva.Should().NotBe(clave);
    }

    [Fact]
    public async Task La_clave_se_usa_una_sola_vez_la_anterior_deja_de_servir_y_la_nueva_si_sirve()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (auth, clave) = await AccesoCompletoAsync(db);
        var nueva = await auth.RestablecerConClaveAsync(clave, "Nueva2027", "Nueva2027");

        var reuso = () => auth.RestablecerConClaveAsync(clave, "Robada1234", "Robada1234");

        await reuso.Should().ThrowAsync<ValidationException>().WithMessage("*clave de recuperación no es correcta*");
        (await auth.VerificarAsync("Robada1234")).Should().BeFalse();
        await auth.RestablecerConClaveAsync(nueva, "Tercera2029", "Tercera2029");
        (await auth.VerificarAsync("Tercera2029")).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCD-EFGH")]
    [InlineData("ABCDEFGHJKMNPQRST")]
    public async Task Una_clave_con_el_largo_equivocado_se_rechaza_con_un_mensaje_claro(string clave)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (auth, _) = await AccesoCompletoAsync(db);

        var act = () => auth.RestablecerConClaveAsync(clave, "Nueva2027", "Nueva2027");

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*16 caracteres*");
    }

    [Fact]
    public async Task Una_clave_de_16_caracteres_pero_incorrecta_se_rechaza()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (auth, clave) = await AccesoCompletoAsync(db);
        var otra = clave == "ABCDEFGHJKMNPQRS" ? "ZZZZZZZZZZZZZZZZ" : "ABCDEFGHJKMNPQRS";

        var act = () => auth.RestablecerConClaveAsync(otra, "Nueva2027", "Nueva2027");

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*clave de recuperación no es correcta*");
        (await auth.VerificarAsync("Granjas2026")).Should().BeTrue();
    }

    [Theory]
    [InlineData("abc", "abc", "*al menos 4*")]
    [InlineData("Nueva2027", "Distinta", "*confirmación no coincide*")]
    public async Task Al_restablecer_la_contrasena_nueva_tambien_se_valida(string nueva, string confirmacion, string mensaje)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (auth, clave) = await AccesoCompletoAsync(db);

        var porClave = () => auth.RestablecerConClaveAsync(clave, nueva, confirmacion);
        var porRespuesta = () => auth.RestablecerConRespuestaAsync("Peña Roja", nueva, confirmacion);

        await porClave.Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
        await porRespuesta.Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
        (await auth.VerificarAsync("Granjas2026")).Should().BeTrue("nada cambió");
    }

    // ---- Instalaciones sin recuperación y configuración posterior ----------------------------------------------------

    [Fact]
    public async Task Un_acceso_creado_sin_recuperacion_no_se_puede_restablecer_y_lo_dice()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);
        await auth.CrearContrasenaAsync("Granjas2026", "Granjas2026");

        (await auth.ObtenerEstadoDeRecuperacionAsync()).Should().Be(new EstadoDeRecuperacion(false, null));
        var porRespuesta = () => auth.RestablecerConRespuestaAsync("lo que sea", "Nueva2027", "Nueva2027");
        var porClave = () => auth.RestablecerConClaveAsync("ABCD-EFGH-JKMN-PQRS", "Nueva2027", "Nueva2027");

        await porRespuesta.Should().ThrowAsync<ValidationException>().WithMessage("*no tiene configurada la pregunta secreta*");
        await porClave.Should().ThrowAsync<ValidationException>().WithMessage("*no tiene configurada la pregunta secreta*");
        (await auth.VerificarAsync("Granjas2026")).Should().BeTrue();
    }

    [Fact]
    public async Task El_estado_informa_la_pregunta_elegida_cuando_hay_recuperacion()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);
        (await auth.ObtenerEstadoDeRecuperacionAsync()).Configurada.Should().BeFalse("primer arranque");

        await AccesoCompletoAsync(db);

        (await auth.ObtenerEstadoDeRecuperacionAsync()).Should().Be(new EstadoDeRecuperacion(true, Pregunta));
    }

    [Fact]
    public async Task Configurar_la_recuperacion_de_un_acceso_antiguo_exige_la_contrasena_actual_y_entrega_la_clave()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);
        await auth.CrearContrasenaAsync("Granjas2026", "Granjas2026");

        var equivocada = () => auth.ConfigurarRecuperacionAsync("mal", Pregunta, "Peña Roja");
        await equivocada.Should().ThrowAsync<ValidationException>().WithMessage("*contraseña actual no es correcta*");
        (await auth.ObtenerEstadoDeRecuperacionAsync()).Configurada.Should().BeFalse();

        var clave = await auth.ConfigurarRecuperacionAsync("Granjas2026", Pregunta, "Peña Roja");

        clave.Should().HaveLength(16);
        (await auth.ObtenerEstadoDeRecuperacionAsync()).Should().Be(new EstadoDeRecuperacion(true, Pregunta));
        await auth.RestablecerConClaveAsync(clave, "Nueva2027", "Nueva2027");
        (await auth.VerificarAsync("Nueva2027")).Should().BeTrue();
    }

    [Fact]
    public async Task Volver_a_configurar_la_recuperacion_cambia_pregunta_respuesta_y_clave_y_anula_lo_anterior()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (auth, claveVieja) = await AccesoCompletoAsync(db);
        const string otraPregunta = "¿Cuál es su comida favorita?";

        var claveNueva = await auth.ConfigurarRecuperacionAsync("Granjas2026", otraPregunta, "Sancocho");

        claveNueva.Should().NotBe(claveVieja);
        (await auth.ObtenerEstadoDeRecuperacionAsync()).PreguntaSecreta.Should().Be(otraPregunta);
        await ((Func<Task>)(() => auth.RestablecerConClaveAsync(claveVieja, "Robada1234", "Robada1234"))).Should().ThrowAsync<ValidationException>();
        await ((Func<Task>)(() => auth.RestablecerConRespuestaAsync("Peña Roja", "Robada1234", "Robada1234"))).Should().ThrowAsync<ValidationException>();
        await auth.RestablecerConRespuestaAsync("sancocho", "Nueva2027", "Nueva2027");
        (await auth.VerificarAsync("Nueva2027")).Should().BeTrue();
    }

    [Fact]
    public async Task Cambiar_la_contrasena_normalmente_no_toca_la_recuperacion()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (auth, clave) = await AccesoCompletoAsync(db);

        await auth.CambiarContrasenaAsync("Granjas2026", "Nueva2027", "Nueva2027");

        (await auth.ObtenerEstadoDeRecuperacionAsync()).Configurada.Should().BeTrue();
        await auth.RestablecerConClaveAsync(clave, "Final2030", "Final2030");
        (await auth.VerificarAsync("Final2030")).Should().BeTrue();
    }

    [Fact]
    public void Hay_preguntas_secretas_para_elegir_sin_repetidas_y_todas_validas()
    {
        AuthService.PreguntasSecretas.Should().HaveCountGreaterThanOrEqualTo(5).And.OnlyHaveUniqueItems();
        AuthService.PreguntasSecretas.Should().OnlyContain(p => p.StartsWith('¿') && p.EndsWith('?') && p.Length <= 200);
    }
}
