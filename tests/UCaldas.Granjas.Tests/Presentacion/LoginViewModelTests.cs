using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.Services.Seguridad;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Presentacion;

public class LoginViewModelTests
{
    private static (LoginViewModel Vm, AuthService Auth, List<string> Eventos) Crear(BaseDeDatosDePrueba db)
    {
        var auth = new AuthService(db.Uow, new RelojFijo(Datos.F("2026-11-06")));
        var vm = new LoginViewModel(auth, new DialogoFalso());
        var eventos = new List<string>();
        vm.IngresoExitoso += () => eventos.Add("ingreso");
        return (vm, auth, eventos);
    }

    [Fact]
    public async Task Con_la_base_recien_creada_pide_crear_la_contrasena()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, _, _) = Crear(db);

        await vm.InicializarAsync();

        vm.EsPrimerArranque.Should().BeTrue();
        vm.TextoBoton.Should().Contain("CREAR");
        vm.Instruccion.Should().Contain("cree la contraseña");
    }

    [Fact]
    public async Task Crear_la_contrasena_con_confirmacion_distinta_muestra_el_error_y_no_ingresa()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, eventos) = Crear(db);
        await vm.InicializarAsync();
        vm.Contrasena = "clave1";
        vm.Confirmacion = "clave2";

        await vm.AceptarCommand.ExecuteAsync(null);

        vm.MensajeError.Should().Be("La confirmación no coincide con la contraseña.");
        eventos.Should().BeEmpty();
        (await auth.EstaConfiguradoAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Una_contrasena_demasiado_corta_se_rechaza_con_su_mensaje()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, _, eventos) = Crear(db);
        await vm.InicializarAsync();
        vm.Contrasena = vm.Confirmacion = "abc";

        await vm.AceptarCommand.ExecuteAsync(null);

        vm.MensajeError.Should().Contain($"al menos {AuthService.LongitudMinima} caracteres");
        eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task Crear_una_contrasena_valida_la_guarda_y_deja_ingresar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, eventos) = Crear(db);
        await vm.InicializarAsync();
        vm.Contrasena = vm.Confirmacion = "granjas2026";

        await vm.AceptarCommand.ExecuteAsync(null);

        eventos.Should().Equal("ingreso");
        vm.MensajeError.Should().BeNull();
        (await auth.EstaConfiguradoAsync()).Should().BeTrue();
        (await auth.VerificarAsync("granjas2026")).Should().BeTrue();
    }

    [Fact]
    public async Task Despues_del_primer_uso_solo_pide_la_contrasena()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, _) = Crear(db);
        await auth.CrearContrasenaAsync("granjas2026", "granjas2026");

        await vm.InicializarAsync();

        vm.EsPrimerArranque.Should().BeFalse();
        vm.TextoBoton.Should().Be("INGRESAR");
    }

    [Fact]
    public async Task Una_contrasena_incorrecta_muestra_el_mensaje_limpia_el_campo_y_no_ingresa()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, auth, eventos) = Crear(db);
        await auth.CrearContrasenaAsync("granjas2026", "granjas2026");
        await vm.InicializarAsync();
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
        var (vm, auth, eventos) = Crear(db);
        await auth.CrearContrasenaAsync("granjas2026", "granjas2026");
        await vm.InicializarAsync();
        vm.Contrasena = "granjas2026";

        await vm.AceptarCommand.ExecuteAsync(null);

        eventos.Should().Equal("ingreso");
        vm.MensajeError.Should().BeNull();
    }
}
