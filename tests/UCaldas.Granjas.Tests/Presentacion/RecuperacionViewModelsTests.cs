using System.Text;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.App.ViewModels.Configuracion;
using UCaldas.Granjas.Services.Seguridad;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>Primer inicio, panel de la clave de recuperación y configuración posterior de la recuperación.</summary>
public class RecuperacionViewModelsTests
{
    private static readonly RelojFijo Reloj = new(Datos.F("2026-11-06").AddHours(14).AddMinutes(35));

    private static AuthService CrearAuth(BaseDeDatosDePrueba db) => new(db.Uow, Reloj);

    // ---- Panel de la clave de recuperación ----------------------------------------------------------------------------

    [Fact]
    public void La_clave_se_muestra_en_grupos_de_cuatro()
    {
        var vm = new ClaveDeRecuperacionViewModel(new DialogoFalso(), Reloj, "ABCDEFGHJKMNPQRS");

        vm.Clave.Should().Be("ABCD-EFGH-JKMN-PQRS");
        vm.Confirmada.Should().BeFalse("el usuario debe confirmar que la guardó");
    }

    [Fact]
    public void Copiar_pone_la_clave_formateada_en_el_portapapeles_y_lo_avisa()
    {
        var dialogos = new DialogoFalso();
        var vm = new ClaveDeRecuperacionViewModel(dialogos, Reloj, "ABCDEFGHJKMNPQRS");

        vm.CopiarCommand.Execute(null);

        dialogos.Copiados.Should().Equal("ABCD-EFGH-JKMN-PQRS");
        vm.Mensaje.Should().Contain("se copió al portapapeles");
    }

    [Fact]
    public void Si_el_portapapeles_esta_ocupado_el_aviso_lo_dice_y_no_hay_excepcion()
    {
        var dialogos = new DialogoFalso { PortapapelesDisponible = false };
        var vm = new ClaveDeRecuperacionViewModel(dialogos, Reloj, "ABCDEFGHJKMNPQRS");

        vm.CopiarCommand.Execute(null);

        dialogos.Copiados.Should().BeEmpty();
        vm.Mensaje.Should().Contain("No se pudo copiar");
    }

    [Fact]
    public async Task Guardar_escribe_un_archivo_de_texto_con_la_clave_la_fecha_y_las_instrucciones()
    {
        var ruta = Path.Combine(Path.GetTempPath(), "SistemaGranjasPruebas", Guid.NewGuid().ToString("N"), "clave.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        var dialogos = new DialogoFalso { RutaAGuardar = ruta };
        var vm = new ClaveDeRecuperacionViewModel(dialogos, Reloj, "ABCDEFGHJKMNPQRS");

        try
        {
            await vm.GuardarCommand.ExecuteAsync(null);

            dialogos.NombresSugeridos.Should().Equal(ClaveDeRecuperacionViewModel.NombreSugeridoDelArchivo);
            var bytes = await File.ReadAllBytesAsync(ruta);
            bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF); // UTF-8 con marca: el Bloc de notas muestra bien las tildes
            var texto = Encoding.UTF8.GetString(bytes);
            texto.Should().Contain("Clave: ABCD-EFGH-JKMN-PQRS")
                .And.Contain("Generada el: 06/11/2026 14:35")
                .And.Contain("CLAVE DE RECUPERACIÓN")
                .And.Contain("Se usa una sola vez");
            vm.Mensaje.Should().Contain(ruta);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(ruta)!, recursive: true);
        }
    }

    [Fact]
    public async Task Si_se_cancela_el_cuadro_de_guardar_no_se_escribe_nada()
    {
        var dialogos = new DialogoFalso { RutaAGuardar = null };
        var vm = new ClaveDeRecuperacionViewModel(dialogos, Reloj, "ABCDEFGHJKMNPQRS");

        await vm.GuardarCommand.ExecuteAsync(null);

        vm.Mensaje.Should().BeNull();
        dialogos.Errores.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_error_al_escribir_el_archivo_se_informa_sin_cerrar_la_aplicacion()
    {
        var dialogos = new DialogoFalso { RutaAGuardar = Path.Combine(Path.GetTempPath(), "SistemaGranjasPruebas", Guid.NewGuid().ToString("N"), "no-existe", "clave.txt") };
        var vm = new ClaveDeRecuperacionViewModel(dialogos, Reloj, "ABCDEFGHJKMNPQRS");

        await vm.GuardarCommand.ExecuteAsync(null);

        dialogos.Errores.Should().ContainSingle().Which.Should().Contain("No se pudo guardar la clave de recuperación");
        vm.Mensaje.Should().BeNull();
    }

    // ---- Primer inicio ------------------------------------------------------------------------------------------------

    private static (InicializacionPasswordViewModel Vm, AuthService Auth, List<string> Eventos, DialogoFalso Dialogos) CrearInicio(BaseDeDatosDePrueba db)
    {
        var auth = CrearAuth(db);
        var dialogos = new DialogoFalso();
        var vm = new InicializacionPasswordViewModel(auth, Reloj, dialogos);
        var eventos = new List<string>();
        vm.Terminado += () => eventos.Add("terminado");
        return (vm, auth, eventos, dialogos);
    }

    [Fact]
    public async Task El_primer_inicio_ofrece_las_preguntas_y_empieza_con_la_primera_elegida()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, _, _, _) = CrearInicio(db);

        vm.Preguntas.Should().Equal(AuthService.PreguntasSecretas);
        vm.PreguntaSecreta.Should().Be(AuthService.PreguntasSecretas[0]);
        vm.MuestraFormulario.Should().BeTrue();
        vm.MuestraClave.Should().BeFalse();
        vm.ContinuarCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Crear_el_acceso_guarda_los_hashes_muestra_la_clave_y_no_deja_continuar_hasta_confirmar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, eventos, _) = CrearInicio(db);
        vm.Contrasena = vm.Confirmacion = "granjas2026";
        vm.PreguntaSecreta = AuthService.PreguntasSecretas[2];
        vm.Respuesta = "Escuela Rural La Palma";

        await vm.CrearCommand.ExecuteAsync(null);

        vm.MensajeError.Should().BeNull();
        vm.MuestraFormulario.Should().BeFalse();
        vm.MuestraClave.Should().BeTrue();
        vm.Clave.Should().NotBeNull();
        vm.Contrasena.Should().BeEmpty("los secretos no se conservan en memoria");
        vm.Respuesta.Should().BeEmpty();
        vm.CrearCommand.CanExecute(null).Should().BeFalse("ya se creó el acceso");

        (await auth.VerificarAsync("granjas2026")).Should().BeTrue();
        (await auth.ObtenerEstadoDeRecuperacionAsync()).Should().Be(new EstadoDeRecuperacion(true, AuthService.PreguntasSecretas[2]));
        await using (var uow = await db.Uow.OpenReadOnlyAsync())
        {
            var configuracion = (await uow.Configuracion.GetSistemaAsync())!;
            configuracion.RespuestaHash.Should().StartWith("PBKDF2-SHA256$");
            configuracion.RecoveryKeyHash.Should().StartWith("PBKDF2-SHA256$");
        }

        // La clave que se muestra es la que valida el restablecimiento.
        var clave = AuthService.NormalizarClave(vm.Clave!.Clave);
        clave.Should().HaveLength(16);

        vm.ContinuarCommand.CanExecute(null).Should().BeFalse("falta confirmar que guardó la clave");
        vm.Clave.Confirmada = true;
        vm.ContinuarCommand.CanExecute(null).Should().BeTrue();
        vm.ContinuarCommand.Execute(null);
        eventos.Should().Equal("terminado");

        await auth.RestablecerConClaveAsync(clave, "nueva2027", "nueva2027");
        (await auth.VerificarAsync("nueva2027")).Should().BeTrue();
    }

    [Theory]
    [InlineData("abc", "abc", "Mascota", "al menos 4 caracteres")]
    [InlineData("granjas2026", "otra", "Mascota", "confirmación no coincide")]
    [InlineData("granjas2026", "granjas2026", "", "al menos 3 caracteres")]
    [InlineData("granjas2026", "granjas2026", "ab", "al menos 3 caracteres")]
    public async Task Los_datos_invalidos_se_muestran_en_la_ventana_y_no_se_guarda_nada(
        string contrasena, string confirmacion, string respuesta, string mensaje)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, eventos, _) = CrearInicio(db);
        vm.Contrasena = contrasena;
        vm.Confirmacion = confirmacion;
        vm.Respuesta = respuesta;

        await vm.CrearCommand.ExecuteAsync(null);

        vm.MensajeError.Should().Contain(mensaje);
        vm.MuestraFormulario.Should().BeTrue();
        vm.Clave.Should().BeNull();
        eventos.Should().BeEmpty();
        vm.EstaOcupado.Should().BeFalse();
        (await auth.EstaConfiguradoAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Corregido_el_error_el_acceso_se_crea_y_el_mensaje_desaparece()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, _, _, _) = CrearInicio(db);
        vm.Contrasena = "granjas2026";
        vm.Confirmacion = "otra";
        vm.Respuesta = "Peña";
        await vm.CrearCommand.ExecuteAsync(null);
        vm.MensajeError.Should().NotBeNull();

        vm.Confirmacion = "granjas2026";
        await vm.CrearCommand.ExecuteAsync(null);

        vm.MensajeError.Should().BeNull();
        vm.MuestraClave.Should().BeTrue();
    }

    // ---- Configurar la recuperación en una instalación existente ---------------------------------------------------------

    [Fact]
    public async Task Configurar_la_recuperacion_exige_la_contrasena_actual_y_entrega_la_clave()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = CrearAuth(db);
        await auth.CrearContrasenaAsync("granjas2026", "granjas2026"); // instalación antigua
        var vm = new ConfigurarRecuperacionViewModel(auth, Reloj, new DialogoFalso());
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;
        vm.PreguntaSecreta = AuthService.PreguntasSecretas[5];
        vm.Respuesta = "Sancocho";
        vm.ContrasenaActual = "equivocada";

        await vm.GenerarCommand.ExecuteAsync(null);

        vm.MensajeError.Should().Contain("contraseña actual no es correcta");
        vm.MuestraFormulario.Should().BeTrue();
        (await auth.ObtenerEstadoDeRecuperacionAsync()).Configurada.Should().BeFalse();

        vm.ContrasenaActual = "granjas2026";
        await vm.GenerarCommand.ExecuteAsync(null);

        vm.MensajeError.Should().BeNull();
        vm.MuestraClave.Should().BeTrue();
        vm.ContrasenaActual.Should().BeEmpty();
        (await auth.ObtenerEstadoDeRecuperacionAsync()).Should().Be(new EstadoDeRecuperacion(true, AuthService.PreguntasSecretas[5]));

        vm.ListoCommand.CanExecute(null).Should().BeFalse("falta confirmar que guardó la clave");
        vm.Clave!.Confirmada = true;
        vm.ListoCommand.Execute(null);
        cierres.Should().Equal(true);

        await auth.RestablecerConRespuestaAsync("SANCOCHO", "nueva2027", "nueva2027");
        (await auth.VerificarAsync("nueva2027")).Should().BeTrue();
    }
}
