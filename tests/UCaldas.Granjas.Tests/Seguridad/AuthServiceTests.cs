using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Seguridad;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Seguridad;

public class AuthServiceTests
{
    private static AuthService Crear(BaseDeDatosDePrueba db) => new(db.Uow, new RelojFijo(Datos.F("2026-11-06")));

    [Fact]
    public async Task El_primer_arranque_no_esta_configurado_y_ninguna_contrasena_verifica()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);

        (await auth.EstaConfiguradoAsync()).Should().BeFalse();
        (await auth.VerificarAsync("cualquiera")).Should().BeFalse();
    }

    [Fact]
    public async Task Crear_la_contrasena_guarda_un_hash_y_deja_la_configuracion_inicial()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);

        await auth.CrearContrasenaAsync("Granjas2026", "Granjas2026");

        (await auth.EstaConfiguradoAsync()).Should().BeTrue();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var configuracion = (await uow.Configuracion.GetSistemaAsync())!;
        configuracion.PasswordHash.Should().StartWith("PBKDF2-SHA256$").And.NotContain("Granjas2026");
        configuracion.BackupAutomaticoSalir.Should().BeTrue();
        configuracion.RutaBackupPredeterminada.Should().Be(Rutas.CarpetaRespaldosPredeterminada);
        configuracion.UltimoCierreValorizacion.Should().Be(Datos.F("2026-10-31"), "la valorización parte del último fin de mes ya terminado");
    }

    [Fact]
    public async Task Solo_la_contrasena_correcta_permite_ingresar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);
        await auth.CrearContrasenaAsync("Granjas2026", "Granjas2026");

        (await auth.VerificarAsync("Granjas2026")).Should().BeTrue();
        (await auth.VerificarAsync("granjas2026")).Should().BeFalse("distingue mayúsculas");
        (await auth.VerificarAsync("Granjas2026 ")).Should().BeFalse();
        (await auth.VerificarAsync("")).Should().BeFalse();
        (await auth.VerificarAsync(null!)).Should().BeFalse();
    }

    [Fact]
    public void Dos_hashes_de_la_misma_contrasena_son_distintos_por_la_sal_pero_ambos_verifican()
    {
        var a = AuthService.GenerarHash("clave");
        var b = AuthService.GenerarHash("clave");

        a.Should().NotBe(b);
        AuthService.CoincideConHash("clave", a).Should().BeTrue();
        AuthService.CoincideConHash("clave", b).Should().BeTrue();
        AuthService.CoincideConHash("otra", a).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("PBKDF2-SHA256$200000$solo-tres")]
    [InlineData("MD5$200000$AAAA$BBBB")]
    [InlineData("PBKDF2-SHA256$no-numero$AAAA$BBBB")]
    [InlineData("PBKDF2-SHA256$200000$no-es-base64!!$BBBB")]
    [InlineData("PBKDF2-SHA256$0$AAAA$BBBB")]
    public void Un_hash_malformado_nunca_verifica_y_nunca_lanza(string almacenado)
    {
        AuthService.CoincideConHash("clave", almacenado).Should().BeFalse();
    }

    [Fact]
    public void Un_hash_alterado_no_verifica()
    {
        var hash = AuthService.GenerarHash("clave");
        var partes = hash.Split('$');
        var alterado = $"{partes[0]}${partes[1]}${partes[2]}${Convert.ToBase64String(new byte[32])}";

        AuthService.CoincideConHash("clave", alterado).Should().BeFalse();
    }

    [Theory]
    [InlineData("abc", "abc", "*al menos 4 caracteres*")]
    [InlineData("", "", "*al menos 4 caracteres*")]
    [InlineData("Granjas2026", "granjas2026", "*confirmación no coincide*")]
    public async Task Rechaza_contrasenas_cortas_o_con_confirmacion_distinta(string contrasena, string confirmacion, string mensaje)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).CrearContrasenaAsync(contrasena, confirmacion);

        await act.Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
        (await Crear(db).EstaConfiguradoAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Un_PIN_de_cuatro_digitos_es_valido()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);

        await auth.CrearContrasenaAsync("1234", "1234");

        (await auth.VerificarAsync("1234")).Should().BeTrue();
    }

    [Fact]
    public async Task La_contrasena_no_se_puede_crear_dos_veces()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);
        await auth.CrearContrasenaAsync("Granjas2026", "Granjas2026");

        var act = () => auth.CrearContrasenaAsync("Otra1234", "Otra1234");

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*ya fue creada*");
        (await auth.VerificarAsync("Granjas2026")).Should().BeTrue();
    }

    [Fact]
    public async Task Cambiar_la_contrasena_exige_la_actual_y_la_nueva_reemplaza_a_la_anterior()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);
        await auth.CrearContrasenaAsync("Granjas2026", "Granjas2026");

        var incorrecta = () => auth.CambiarContrasenaAsync("mal", "Nueva2027", "Nueva2027");
        await incorrecta.Should().ThrowAsync<ValidationException>().WithMessage("*contraseña actual no es correcta*");

        var corta = () => auth.CambiarContrasenaAsync("Granjas2026", "abc", "abc");
        await corta.Should().ThrowAsync<ValidationException>().WithMessage("*al menos 4 caracteres*");

        await auth.CambiarContrasenaAsync("Granjas2026", "Nueva2027", "Nueva2027");

        (await auth.VerificarAsync("Nueva2027")).Should().BeTrue();
        (await auth.VerificarAsync("Granjas2026")).Should().BeFalse();
    }

    [Fact]
    public async Task Cambiar_la_contrasena_conserva_el_resto_de_la_configuracion()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var auth = Crear(db);
        await auth.CrearContrasenaAsync("Granjas2026", "Granjas2026");
        await db.EjecutarAsync("UPDATE CONFIGURACION_SISTEMA SET backup_automatico_salir = 0, ruta_backup_predeterminada = 'D:\\Mis respaldos'");

        await auth.CambiarContrasenaAsync("Granjas2026", "Nueva2027", "Nueva2027");

        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var configuracion = (await uow.Configuracion.GetSistemaAsync())!;
        configuracion.BackupAutomaticoSalir.Should().BeFalse();
        configuracion.RutaBackupPredeterminada.Should().Be(@"D:\Mis respaldos");
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM CONFIGURACION_SISTEMA")).Should().Be(1);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_antes_de_crearla_falla()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).CambiarContrasenaAsync("a", "Nueva2027", "Nueva2027");

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*Aún no se ha creado una contraseña*");
    }
}
