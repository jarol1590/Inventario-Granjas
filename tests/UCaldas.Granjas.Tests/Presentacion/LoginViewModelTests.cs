using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.Services.Seguridad;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Presentacion;

public class LoginViewModelTests
{
    private const string Pregunta = "¿Cuál es el nombre de su primera mascota?";

    private static (LoginViewModel Vm, AuthService Auth, List<string> Eventos, DialogoFalso Dialogos) Crear(BaseDeDatosDePrueba db)
    {
        var reloj = new RelojFijo(Datos.F("2026-11-06"));
        var auth = new AuthService(db.Uow, reloj);
        var dialogos = new DialogoFalso();
        var vm = new LoginViewModel(auth, reloj, dialogos);
        var eventos = new List<string>();
        vm.IngresoExitoso += () => eventos.Add("ingreso");
        return (vm, auth, eventos, dialogos);
    }

    /// <summary>Acceso con recuperación: contraseña "granjas2026", respuesta "Peña Roja". Devuelve la clave de recuperación.</summary>
    private static Task<string> CrearAccesoAsync(AuthService auth)
        => auth.CrearAccesoAsync("granjas2026", "granjas2026", Pregunta, "Peña Roja");

    // ---- Ingreso ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_acceso_empieza_pidiendo_solo_la_contrasena()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, _, _) = Crear(db);
        await CrearAccesoAsync(auth);

        vm.Paso.Should().Be(PasoDeAcceso.Ingreso);
        vm.EsIngreso.Should().BeTrue();
        vm.EsRecuperacion.Should().BeFalse();
        vm.EsClaveNueva.Should().BeFalse();
    }

    [Fact]
    public async Task Una_contrasena_incorrecta_muestra_el_mensaje_limpia_el_campo_y_no_ingresa()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, eventos, _) = Crear(db);
        await CrearAccesoAsync(auth);
        vm.Contrasena = "otra-clave";

        await vm.AceptarCommand.ExecuteAsync(null);

        vm.MensajeError.Should().Be("La contraseña no es correcta.");
        vm.Contrasena.Should().BeEmpty();
        eventos.Should().BeEmpty();
        vm.EstaOcupado.Should().BeFalse();
    }

    [Fact]
    public async Task La_contrasena_correcta_deja_ingresar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, eventos, _) = Crear(db);
        await CrearAccesoAsync(auth);
        vm.Contrasena = "granjas2026";

        await vm.AceptarCommand.ExecuteAsync(null);

        eventos.Should().Equal("ingreso");
        vm.MensajeError.Should().BeNull();
    }

    // ---- ¿Olvidó su contraseña? ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Olvide_la_contrasena_abre_el_restablecimiento_con_la_pregunta_secreta_configurada()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, _, _) = Crear(db);
        await CrearAccesoAsync(auth);
        vm.Contrasena = "algo";

        await vm.OlvideLaContrasenaCommand.ExecuteAsync(null);

        vm.Paso.Should().Be(PasoDeAcceso.Recuperacion);
        vm.EsRecuperacion.Should().BeTrue();
        vm.RecuperacionDisponible.Should().BeTrue();
        vm.PreguntaSecreta.Should().Be(Pregunta);
        vm.UsaPregunta.Should().BeTrue("la pregunta secreta es la opción inicial");
        vm.UsaClave.Should().BeFalse();
        vm.Contrasena.Should().BeEmpty();
    }

    [Fact]
    public async Task Los_botones_de_opcion_cambian_entre_pregunta_y_clave()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, _, _) = Crear(db);
        await CrearAccesoAsync(auth);
        await vm.OlvideLaContrasenaCommand.ExecuteAsync(null);

        vm.UsaClave = true;
        vm.UsaPregunta.Should().BeFalse();
        vm.Metodo.Should().Be(MetodoDeRecuperacion.ClaveDeRecuperacion);

        vm.UsaClave = false;   // el botón que se desmarca no cambia el método
        vm.Metodo.Should().Be(MetodoDeRecuperacion.ClaveDeRecuperacion);

        vm.UsaPregunta = true;
        vm.UsaClave.Should().BeFalse();
        vm.Metodo.Should().Be(MetodoDeRecuperacion.PreguntaSecreta);
    }

    [Fact]
    public async Task Restablecer_con_la_pregunta_secreta_cambia_la_contrasena_entrega_una_clave_nueva_y_vuelve_al_ingreso()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, eventos, _) = Crear(db);
        var claveVieja = await CrearAccesoAsync(auth);
        await db.EjecutarAsync("INSERT INTO GRANJA (codigo, nombre) VALUES ('G-99', 'Prueba')");
        await vm.OlvideLaContrasenaCommand.ExecuteAsync(null);
        vm.Respuesta = "  PEÑA roja ";
        vm.NuevaContrasena = vm.ConfirmacionNueva = "nueva2027";

        await vm.RestablecerCommand.ExecuteAsync(null);

        vm.MensajeError.Should().BeNull();
        vm.Paso.Should().Be(PasoDeAcceso.ClaveNueva);
        vm.ClaveNueva.Should().NotBeNull();
        AuthService.NormalizarClave(vm.ClaveNueva!.Clave).Should().HaveLength(16).And.NotBe(claveVieja);
        vm.Respuesta.Should().BeEmpty("no se conservan secretos en memoria");
        vm.NuevaContrasena.Should().BeEmpty();
        (await auth.VerificarAsync("nueva2027")).Should().BeTrue();
        (await auth.VerificarAsync("granjas2026")).Should().BeFalse();
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(4, "la base de datos no se elimina ni se toca");

        // No se puede salir de este paso sin confirmar que se guardó la clave nueva.
        vm.ContinuarTrasRestablecerCommand.CanExecute(null).Should().BeFalse();
        vm.ClaveNueva.Confirmada = true;
        vm.ContinuarTrasRestablecerCommand.CanExecute(null).Should().BeTrue();
        vm.ContinuarTrasRestablecerCommand.Execute(null);

        vm.Paso.Should().Be(PasoDeAcceso.Ingreso);
        vm.ClaveNueva.Should().BeNull();
        vm.MensajeInformativo.Should().Contain("Contraseña restablecida");
        eventos.Should().BeEmpty("tras restablecer se vuelve al ingreso: no se entra solo");

        vm.Contrasena = "nueva2027";
        await vm.AceptarCommand.ExecuteAsync(null);
        eventos.Should().Equal("ingreso");
    }

    [Fact]
    public async Task Restablecer_con_la_clave_de_recuperacion_funciona_con_o_sin_guiones()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, _, _) = Crear(db);
        var clave = await CrearAccesoAsync(auth);
        await vm.OlvideLaContrasenaCommand.ExecuteAsync(null);
        vm.UsaClave = true;
        vm.ClaveIngresada = AuthService.FormatearClave(clave).ToLowerInvariant();
        vm.NuevaContrasena = vm.ConfirmacionNueva = "nueva2027";

        await vm.RestablecerCommand.ExecuteAsync(null);

        vm.Paso.Should().Be(PasoDeAcceso.ClaveNueva);
        (await auth.VerificarAsync("nueva2027")).Should().BeTrue();
        vm.ClaveIngresada.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "Pelusa", "granjas2026", "*respuesta a la pregunta secreta no es correcta*")]
    [InlineData(true, "ZZZZ-ZZZZ-ZZZZ-ZZZZ", "granjas2026", "*clave de recuperación no es correcta*")]
    [InlineData(true, "ABCD", "granjas2026", "*16 caracteres*")]
    public async Task Una_credencial_incorrecta_muestra_el_error_y_no_cambia_la_contrasena(
        bool usaClave, string credencial, string nuevaYConfirmacion, string mensaje)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, _, _) = Crear(db);
        await CrearAccesoAsync(auth);
        await vm.OlvideLaContrasenaCommand.ExecuteAsync(null);
        vm.UsaClave = usaClave;
        vm.UsaPregunta = !usaClave;
        vm.Respuesta = usaClave ? string.Empty : credencial;
        vm.ClaveIngresada = usaClave ? credencial : string.Empty;
        vm.NuevaContrasena = vm.ConfirmacionNueva = "Otra5678";

        await vm.RestablecerCommand.ExecuteAsync(null);

        vm.MensajeError.Should().Match(mensaje);
        vm.Paso.Should().Be(PasoDeAcceso.Recuperacion, "sigue en el mismo paso para corregir");
        vm.EstaOcupado.Should().BeFalse();
        (await auth.VerificarAsync(nuevaYConfirmacion)).Should().BeTrue("la contraseña original sigue vigente");
        (await auth.VerificarAsync("Otra5678")).Should().BeFalse();
    }

    [Fact]
    public async Task Una_contrasena_nueva_invalida_o_sin_confirmar_se_rechaza_con_su_mensaje()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, _, _) = Crear(db);
        await CrearAccesoAsync(auth);
        await vm.OlvideLaContrasenaCommand.ExecuteAsync(null);
        vm.Respuesta = "Peña Roja";

        vm.NuevaContrasena = vm.ConfirmacionNueva = "abc";
        await vm.RestablecerCommand.ExecuteAsync(null);
        vm.MensajeError.Should().Contain("al menos 4 caracteres");

        vm.NuevaContrasena = "nueva2027";
        vm.ConfirmacionNueva = "otra2027";
        await vm.RestablecerCommand.ExecuteAsync(null);
        vm.MensajeError.Should().Contain("confirmación no coincide");

        vm.Paso.Should().Be(PasoDeAcceso.Recuperacion);
        (await auth.VerificarAsync("granjas2026")).Should().BeTrue();
    }

    [Fact]
    public async Task Una_instalacion_sin_recuperacion_lo_indica_y_no_deja_restablecer()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, _, _) = Crear(db);
        await auth.CrearContrasenaAsync("granjas2026", "granjas2026"); // acceso antiguo: solo contraseña

        await vm.OlvideLaContrasenaCommand.ExecuteAsync(null);

        vm.Paso.Should().Be(PasoDeAcceso.Recuperacion);
        vm.RecuperacionDisponible.Should().BeFalse();
        vm.PreguntaSecreta.Should().BeNull();

        vm.Respuesta = "lo que sea";
        vm.NuevaContrasena = vm.ConfirmacionNueva = "nueva2027";
        await vm.RestablecerCommand.ExecuteAsync(null);

        vm.MensajeError.Should().Contain("no tiene configurada la pregunta secreta");
        (await auth.VerificarAsync("granjas2026")).Should().BeTrue();
    }

    [Fact]
    public async Task Volver_al_ingreso_limpia_lo_escrito_y_los_mensajes()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, _, _) = Crear(db);
        await CrearAccesoAsync(auth);
        await vm.OlvideLaContrasenaCommand.ExecuteAsync(null);
        vm.Respuesta = "algo";
        vm.NuevaContrasena = vm.ConfirmacionNueva = "nueva2027";
        vm.MensajeError = "un error";

        vm.VolverAlIngresoCommand.Execute(null);

        vm.Paso.Should().Be(PasoDeAcceso.Ingreso);
        vm.Respuesta.Should().BeEmpty();
        vm.NuevaContrasena.Should().BeEmpty();
        vm.ConfirmacionNueva.Should().BeEmpty();
        vm.MensajeError.Should().BeNull();
    }
}
