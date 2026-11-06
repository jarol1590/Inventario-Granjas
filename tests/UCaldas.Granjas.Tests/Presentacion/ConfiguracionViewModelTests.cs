using System.IO.Compression;
using UCaldas.Granjas.App.ViewModels.Configuracion;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Respaldos;
using UCaldas.Granjas.Services.Seguridad;
using UCaldas.Granjas.Data;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>Pestañas "Respaldos / Backup" y "Granjas y Programas" de Configuración.</summary>
public class ConfiguracionViewModelTests
{
    private static readonly DateTime Hoy = Datos.F("2026-11-06");

    private sealed class EntornoDeRespaldos : IAsyncDisposable
    {
        public required BaseDeDatosDePrueba Db { get; init; }

        public required string Carpeta { get; init; }

        public required RespaldosViewModel Vm { get; init; }

        public required DialogoFalso Dialogos { get; init; }

        public required NavegadorFalso Navegador { get; init; }

        public required AuthService Auth { get; init; }

        public required CatalogoService Catalogo { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (Directory.Exists(Carpeta))
            {
                Directory.Delete(Carpeta, recursive: true);
            }
        }
    }

    private static async Task<EntornoDeRespaldos> CrearRespaldosAsync()
    {
        var db = await BaseDeDatosDePrueba.CrearAsync();
        var reloj = new RelojFijo(Hoy);
        var auth = new AuthService(db.Uow, reloj);
        await auth.CrearContrasenaAsync("granjas2026", "granjas2026");

        var carpeta = Path.Combine(Path.GetTempPath(), "SistemaGranjasPruebas", "respaldos-" + Guid.NewGuid().ToString("N"));
        var catalogo = new CatalogoService(db.Uow);
        await catalogo.GuardarPreferenciasDeRespaldoAsync(false, carpeta);

        var dialogos = new DialogoFalso();
        var navegador = new NavegadorFalso();
        var respaldos = new BackupService(db.Uow, new SqliteDatabaseMaintenance(db.Conexiones), reloj);
        return new EntornoDeRespaldos
        {
            Db = db,
            Carpeta = carpeta,
            Dialogos = dialogos,
            Navegador = navegador,
            Auth = auth,
            Catalogo = catalogo,
            Vm = new RespaldosViewModel(respaldos, catalogo, auth, navegador, dialogos),
        };
    }

    // ---- Respaldos ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Al_abrir_muestra_las_preferencias_y_que_aun_no_hay_respaldos()
    {
        await using var entorno = await CrearRespaldosAsync();

        await entorno.Vm.CargarAsync();

        entorno.Vm.RespaldoAutomatico.Should().BeFalse();
        entorno.Vm.CarpetaRespaldos.Should().Be(entorno.Carpeta);
        entorno.Vm.UltimoRespaldoTexto.Should().Be("Aún no hay respaldos");
        entorno.Vm.UbicacionTexto.Should().Contain("No hay respaldos");
    }

    [Fact]
    public async Task Crear_un_respaldo_genera_un_zip_valido_y_actualiza_el_ultimo_respaldo()
    {
        await using var entorno = await CrearRespaldosAsync();
        await entorno.Vm.CargarAsync();

        await entorno.Vm.CrearRespaldoCommand.ExecuteAsync(null);

        var zip = Directory.GetFiles(entorno.Carpeta, "backup_*.zip").Should().ContainSingle().Subject;
        using (var archivo = ZipFile.OpenRead(zip))
        {
            archivo.Entries.Select(x => x.Name).Should().Contain(BackupService.NombreBaseEnZip);
        }

        entorno.Vm.UltimoRespaldoTexto.Should().NotBe("Aún no hay respaldos");
        entorno.Vm.UbicacionTexto.Should().Contain(zip);
        entorno.Dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain(zip);
    }

    [Fact]
    public async Task Activar_el_respaldo_automatico_se_guarda_en_la_configuracion()
    {
        await using var entorno = await CrearRespaldosAsync();
        await entorno.Vm.CargarAsync();

        entorno.Vm.RespaldoAutomatico = true;

        await Esperar.HastaAsync(async () => (await entorno.Catalogo.GetSistemaAsync()).BackupAutomaticoSalir);
        (await entorno.Catalogo.GetSistemaAsync()).RutaBackupPredeterminada.Should().Be(entorno.Carpeta);
    }

    [Fact]
    public async Task Cambiar_la_carpeta_guarda_la_nueva_ruta_y_no_hace_nada_si_se_cancela()
    {
        await using var entorno = await CrearRespaldosAsync();
        await entorno.Vm.CargarAsync();
        var otra = Path.Combine(entorno.Carpeta, "otra");

        entorno.Dialogos.CarpetaAElegir = null;
        await entorno.Vm.CambiarCarpetaCommand.ExecuteAsync(null);
        entorno.Vm.CarpetaRespaldos.Should().Be(entorno.Carpeta);

        entorno.Dialogos.CarpetaAElegir = otra;
        await entorno.Vm.CambiarCarpetaCommand.ExecuteAsync(null);

        entorno.Vm.CarpetaRespaldos.Should().Be(otra);
        (await entorno.Catalogo.GetSistemaAsync()).RutaBackupPredeterminada.Should().Be(otra);
    }

    [Fact]
    public async Task Restaurar_pide_confirmacion_y_recarga_el_sistema()
    {
        await using var entorno = await CrearRespaldosAsync();
        await entorno.Vm.CargarAsync();
        await entorno.Vm.CrearRespaldoCommand.ExecuteAsync(null);
        var zip = Directory.GetFiles(entorno.Carpeta, "backup_*.zip").Single();
        entorno.Dialogos.Informaciones.Clear();

        // Sin archivo elegido no pasa nada.
        entorno.Dialogos.RutaAAbrir = null;
        await entorno.Vm.RestaurarCommand.ExecuteAsync(null);
        entorno.Navegador.Recargas.Should().Be(0);

        // Archivo elegido pero el usuario no confirma.
        entorno.Dialogos.RutaAAbrir = zip;
        entorno.Dialogos.RespuestaConfirmar = false;
        await entorno.Vm.RestaurarCommand.ExecuteAsync(null);
        entorno.Navegador.Recargas.Should().Be(0);

        // Confirma: restaura y recarga.
        entorno.Dialogos.RespuestaConfirmar = true;
        await entorno.Vm.RestaurarCommand.ExecuteAsync(null);

        entorno.Navegador.Recargas.Should().Be(1);
        entorno.Dialogos.Confirmaciones.Should().OnlyContain(m => m.Contains("reemplazará TODA la información"));
        Directory.GetFiles(entorno.Carpeta, "antes_de_restaurar_*.zip").Should().ContainSingle("antes de restaurar se guarda la base actual");
        entorno.Dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("restauró correctamente");
        (await entorno.Auth.VerificarAsync("granjas2026")).Should().BeTrue("la base restaurada sigue siendo usable");
    }

    [Fact]
    public async Task Restaurar_un_archivo_que_no_es_un_respaldo_se_rechaza_sin_tocar_la_base()
    {
        await using var entorno = await CrearRespaldosAsync();
        Directory.CreateDirectory(entorno.Carpeta);
        var falso = Path.Combine(entorno.Carpeta, "no-es-un-respaldo.zip");
        await File.WriteAllTextAsync(falso, "esto no es un zip");
        entorno.Dialogos.RutaAAbrir = falso;
        entorno.Dialogos.RespuestaConfirmar = true;

        await entorno.Vm.RestaurarCommand.ExecuteAsync(null);

        entorno.Dialogos.Advertencias.Should().ContainSingle();
        entorno.Navegador.Recargas.Should().Be(0);
        (await entorno.Auth.VerificarAsync("granjas2026")).Should().BeTrue();
    }

    [Fact]
    public async Task Compactar_informa_el_tamano_antes_y_despues()
    {
        await using var entorno = await CrearRespaldosAsync();

        await entorno.Vm.CompactarCommand.ExecuteAsync(null);

        entorno.Dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("Antes:").And.Contain("Después:");
    }

    [Fact]
    public async Task Cambiar_la_contrasena_valida_la_actual_la_confirmacion_y_limpia_los_campos()
    {
        await using var entorno = await CrearRespaldosAsync();
        var vm = entorno.Vm;

        vm.ContrasenaActual = "incorrecta";
        vm.ContrasenaNueva = vm.ContrasenaConfirmacion = "nueva-clave";
        await vm.CambiarContrasenaCommand.ExecuteAsync(null);
        entorno.Dialogos.UltimaAdvertencia.Should().Contain("contraseña actual no es correcta");

        vm.ContrasenaActual = "granjas2026";
        vm.ContrasenaConfirmacion = "otra-distinta";
        await vm.CambiarContrasenaCommand.ExecuteAsync(null);
        entorno.Dialogos.UltimaAdvertencia.Should().Contain("confirmación no coincide");

        vm.ContrasenaConfirmacion = "nueva-clave";
        await vm.CambiarContrasenaCommand.ExecuteAsync(null);

        vm.ContrasenaActual.Should().BeEmpty();
        vm.ContrasenaNueva.Should().BeEmpty();
        vm.ContrasenaConfirmacion.Should().BeEmpty();
        (await entorno.Auth.VerificarAsync("nueva-clave")).Should().BeTrue();
        (await entorno.Auth.VerificarAsync("granjas2026")).Should().BeFalse();
        entorno.Dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("se cambió correctamente");
    }

    // ---- Granjas y programas -----------------------------------------------------------------------------------------

    private static (GranjasProgramasViewModel Vm, DialogoFalso Dialogos, NavegadorFalso Navegador, CatalogoService Catalogo) CrearGranjas(BaseDeDatosDePrueba db)
    {
        var catalogo = new CatalogoService(db.Uow);
        var dialogos = new DialogoFalso();
        var navegador = new NavegadorFalso();
        return (new GranjasProgramasViewModel(catalogo, navegador, dialogos), dialogos, navegador, catalogo);
    }

    [Fact]
    public async Task Granjas_y_programas_se_listan_con_su_estado_y_tipo()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, _, _, _) = CrearGranjas(db);

        await vm.CargarAsync();

        vm.Granjas.Select(g => (g.Codigo, g.Nombre, g.Estado)).Should().Equal(("G-01", "Tesorito", "Activa"), ("G-02", "La Cruz", "Activa"), ("G-03", "Montelindo", "Activa"));
        vm.Programas.Select(p => (p.Codigo, p.Tipo)).Should().Equal(("PRG-AVE", "Lote (Población)"), ("PRG-GAN", "Individual (Chapeta)"));
    }

    [Fact]
    public async Task Agregar_una_granja_abre_el_editor_y_refresca_la_lista_y_los_filtros_globales()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, navegador, _) = CrearGranjas(db);
        await vm.CargarAsync();
        dialogos.AlMostrar = ventana =>
        {
            var editor = (GranjaEditorViewModel)ventana;
            editor.EsNueva.Should().BeTrue();
            editor.Titulo.Should().Be("Agregar nueva granja");
            editor.Codigo = "G-04";
            editor.Nombre = "Granja Norte";
            Esperar.Ahora(() => editor.GuardarCommand.ExecuteAsync(null));
            return true;
        };

        await vm.AgregarGranjaCommand.ExecuteAsync(null);

        vm.Granjas.Select(g => g.Nombre).Should().Contain("Granja Norte");
        navegador.RecargasDeFiltros.Should().Be(1);
    }

    [Fact]
    public async Task El_editor_de_granja_cierra_al_guardar_y_rechaza_codigos_repetidos()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var dialogos = new DialogoFalso();
        var catalogo = new CatalogoService(db.Uow);
        var orientaciones = await catalogo.GetOrientacionesAsync();
        var editor = new GranjaEditorViewModel(catalogo, dialogos, orientaciones, existente: null);
        var cierres = new List<bool>();
        editor.CierreSolicitado += cierres.Add;
        editor.Orientaciones.Select(o => o.Nombre).Should().Equal("(Sin orientación sugerida)", "Lácteo", "Carne/Levante");

        editor.Codigo = "g-01";
        editor.Nombre = "Otra";
        await editor.GuardarCommand.ExecuteAsync(null);
        dialogos.UltimaAdvertencia.Should().Contain("Ya existe una granja con el código");
        cierres.Should().BeEmpty();

        editor.Codigo = "G-05";
        editor.OrientacionSeleccionada = editor.Orientaciones[2];
        await editor.GuardarCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        (await catalogo.GetGranjasAsync()).Single(g => g.Codigo == "G-05").OrientacionId.Should().NotBeNull();
    }

    [Fact]
    public async Task Editar_una_granja_existente_conserva_su_identidad()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, _, catalogo) = CrearGranjas(db);
        await vm.CargarAsync();
        dialogos.AlMostrar = ventana =>
        {
            var editor = (GranjaEditorViewModel)ventana;
            editor.Titulo.Should().Be("Editar granja");
            editor.Codigo.Should().Be("G-02");
            editor.Observaciones = "Hato mixto actualizado";
            Esperar.Ahora(() => editor.GuardarCommand.ExecuteAsync(null));
            return true;
        };

        await vm.EditarGranjaCommand.ExecuteAsync(vm.Granjas.Single(g => g.Codigo == "G-02"));

        (await catalogo.GetGranjasAsync()).Should().HaveCount(3);
        (await catalogo.GetGranjasAsync()).Single(g => g.Codigo == "G-02").Observaciones.Should().Be("Hato mixto actualizado");
    }

    [Fact]
    public async Task Quitar_una_granja_con_historia_se_rechaza_y_una_nueva_sin_historia_se_elimina()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        var catalogo = new CatalogoService(e.Db.Uow);
        var dialogos = new DialogoFalso { RespuestaConfirmar = true };
        var navegador = new NavegadorFalso();
        var vm = new GranjasProgramasViewModel(catalogo, navegador, dialogos);
        await vm.CargarAsync();

        await vm.QuitarGranjaCommand.ExecuteAsync(vm.Granjas.Single(g => g.Codigo == "G-01"));
        dialogos.UltimaAdvertencia.Should().Contain("Inactívela");
        vm.Granjas.Should().HaveCount(3);
        navegador.RecargasDeFiltros.Should().Be(0);

        await vm.QuitarGranjaCommand.ExecuteAsync(vm.Granjas.Single(g => g.Codigo == "G-02"));

        vm.Granjas.Select(g => g.Codigo).Should().Equal("G-01", "G-03");
        navegador.RecargasDeFiltros.Should().Be(1);
        dialogos.Confirmaciones.Should().OnlyContain(m => m.Contains("Solo se puede quitar una granja sin"));
    }

    [Fact]
    public async Task El_editor_de_programa_asocia_las_granjas_marcadas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, navegador, catalogo) = CrearGranjas(db);
        await vm.CargarAsync();
        dialogos.AlMostrar = ventana =>
        {
            var editor = (ProgramaEditorViewModel)ventana;
            editor.EsNuevo.Should().BeTrue();
            editor.Tipos.Select(t => t.Nombre).Should().Equal("Individual (Chapeta)", "Lote (Población)");
            editor.Granjas.Should().HaveCount(3).And.OnlyContain(g => !g.Seleccionada);
            editor.Codigo = "PRG-PIS";
            editor.Nombre = "Piscicultura";
            editor.TipoSeleccionado = editor.Tipos[1];
            editor.Granjas.Single(g => g.Granja.Codigo == "G-03").Seleccionada = true;
            Esperar.Ahora(() => editor.GuardarCommand.ExecuteAsync(null));
            return true;
        };

        await vm.AgregarProgramaCommand.ExecuteAsync(null);

        var programa = vm.Programas.Single(p => p.Codigo == "PRG-PIS").Programa;
        programa.TipoInventario.Should().Be(TipoInventario.Lote);
        (await catalogo.GetGranjaIdsDeProgramaAsync(programa.Id)).Should().ContainSingle();
        navegador.RecargasDeFiltros.Should().Be(1);
    }
}
