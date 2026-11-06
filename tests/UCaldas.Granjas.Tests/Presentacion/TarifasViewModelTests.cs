using UCaldas.Granjas.App.ViewModels.Configuracion;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>Pestaña de tarifas: el solapamiento se bloquea al guardar y al activar, y nada se guarda a medias.</summary>
public class TarifasViewModelTests
{
    private static (TarifasViewModel Vm, DialogoFalso Dialogos, VigenciaService Servicio) Crear(BaseDeDatosDePrueba db)
    {
        var reloj = new RelojFijo(Datos.F("2026-11-06"));
        var servicio = new VigenciaService(db.Uow, new TarifaValidationService());
        var dialogos = new DialogoFalso();
        var vm = new TarifasViewModel(servicio, new CatalogoService(db.Uow), new AgingEngine(db.Uow, reloj), reloj, dialogos);
        return (vm, dialogos, servicio);
    }

    private static TarifasViewModel CrearConEscenario(EscenarioKardex e, DialogoFalso dialogos)
        => new(new VigenciaService(e.Db.Uow, new TarifaValidationService()), new CatalogoService(e.Db.Uow), e.Aging, e.Reloj, dialogos);

    private static async Task<int> IdVigenciaAsync(BaseDeDatosDePrueba db, int anio)
        => await db.EscalarAsync<int>("SELECT id FROM VIGENCIA_VALORIZACION WHERE anio = @anio", new { anio });

    [Fact]
    public async Task Al_abrir_muestra_la_vigencia_activa_con_su_matriz_y_sin_cambios_pendientes()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, _, _) = Crear(db);

        await vm.CargarAsync();

        vm.VigenciaSeleccionada.Should().NotBeNull();
        vm.VigenciaSeleccionada!.Texto.Should().Be("Año 2026 (Activa)");
        vm.Filas.Should().HaveCount(9);
        vm.HayCambios.Should().BeFalse();
        vm.TituloTabla.Should().Be("TABLA DE TARIFAS VIGENTES (AÑO 2026)");
        vm.Categorias.Select(c => c.Nombre).Should().NotContain(NombresBase.CategoriaSinClasificar);
        vm.Orientaciones.Select(o => o.Nombre).Should().Equal("Todas", "Lácteo", "Carne/Levante");
        vm.GuardarCommand.CanExecute(null).Should().BeFalse("no hay cambios");
        vm.ActivarCommand.CanExecute(null).Should().BeFalse("la vigencia ya está activa");
    }

    [Fact]
    public async Task Editar_una_fila_marca_cambios_y_habilita_Guardar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, _, _) = Crear(db);
        await vm.CargarAsync();

        vm.Filas[0].ValorBase = 175_000m;

        vm.HayCambios.Should().BeTrue();
        vm.TituloTabla.Should().Contain("con cambios sin guardar");
        vm.GuardarCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Un_solapamiento_de_edades_se_bloquea_con_su_mensaje_y_no_se_guarda_nada()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, _) = Crear(db);
        await vm.CargarAsync();
        vm.AgregarFilaCommand.Execute(null);
        var nueva = vm.Filas[^1];
        nueva.CategoriaId = vm.Filas[0].CategoriaId; // Terneros
        nueva.Sexo = Sexo.Hembra;
        nueva.OrientacionClave = TarifaFilaViewModel.TodasLasOrientaciones;
        nueva.EdadMinimaMeses = 1;   // se cruza con Terneros Hembra 0-2
        nueva.EdadMaximaMeses = 5;
        nueva.ValorBase = 100_000m;

        await vm.GuardarCommand.ExecuteAsync(null);

        dialogos.Advertencias.Should().ContainSingle().Which.Should().Contain("solapamiento");
        dialogos.Informaciones.Should().BeEmpty("no se mostró el aviso de éxito");
        vm.HayCambios.Should().BeTrue("los cambios siguen en pantalla para corregirlos");
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM TABLA_VALORIZACION_BASE WHERE vigencia_id = @id", new { id = await IdVigenciaAsync(db, 2026) }))
            .Should().Be(9, "la matriz guardada quedó intacta");
    }

    [Fact]
    public async Task Corregido_el_solapamiento_la_matriz_se_guarda_y_se_recarga()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, _) = Crear(db);
        await vm.CargarAsync();
        vm.AgregarFilaCommand.Execute(null);
        var nueva = vm.Filas[^1];
        nueva.CategoriaId = vm.Filas[0].CategoriaId;
        nueva.Sexo = Sexo.Hembra;
        nueva.EdadMinimaMeses = 1;
        nueva.EdadMaximaMeses = 5;
        nueva.ValorBase = 100_000m;
        await vm.GuardarCommand.ExecuteAsync(null);
        dialogos.Advertencias.Should().HaveCount(1);

        nueva.EdadMinimaMeses = 40;   // fuera de los rangos existentes de Terneros
        nueva.EdadMaximaMeses = 50;
        await vm.GuardarCommand.ExecuteAsync(null);

        dialogos.Advertencias.Should().HaveCount(1, "no hubo nuevos errores");
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("se guardaron correctamente");
        vm.HayCambios.Should().BeFalse();
        vm.Filas.Should().HaveCount(10);
    }

    [Fact]
    public async Task Los_valores_invalidos_tambien_se_bloquean()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, _) = Crear(db);
        await vm.CargarAsync();
        vm.Filas[0].ValorBase = 0m;

        await vm.GuardarCommand.ExecuteAsync(null);

        dialogos.Advertencias.Should().ContainSingle();
        vm.HayCambios.Should().BeTrue();
    }

    [Fact]
    public async Task El_reajuste_porcentual_actualiza_los_valores_y_deja_los_cambios_para_revisar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, _) = Crear(db);
        await vm.CargarAsync();
        var antes = vm.Filas.Select(f => f.ValorBase).ToList();
        dialogos.RespuestaPedir = "10";
        dialogos.RespuestaConfirmar = true;

        vm.ReajustarCommand.Execute(null);

        vm.Filas.Select(f => f.ValorBase).Should().Equal(antes.Select(v => Math.Round(v * 1.10m, 0)));
        vm.HayCambios.Should().BeTrue();
        dialogos.Confirmaciones.Should().ContainSingle().Which.Should().Contain("+10 %");
    }

    [Fact]
    public async Task El_reajuste_se_cancela_si_el_usuario_no_confirma_o_escribe_algo_invalido()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, _) = Crear(db);
        await vm.CargarAsync();
        var antes = vm.Filas.Select(f => f.ValorBase).ToList();

        dialogos.RespuestaPedir = "abc";
        vm.ReajustarCommand.Execute(null);
        dialogos.UltimaAdvertencia.Should().Contain("porcentaje no es válido");

        dialogos.RespuestaPedir = "5";
        dialogos.RespuestaConfirmar = false;
        vm.ReajustarCommand.Execute(null);

        vm.Filas.Select(f => f.ValorBase).Should().Equal(antes);
        vm.HayCambios.Should().BeFalse();
    }

    [Fact]
    public async Task Clonar_el_ano_crea_una_vigencia_inactiva_con_las_mismas_tarifas_y_se_puede_activar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, servicio) = Crear(db);
        await vm.CargarAsync();
        dialogos.RespuestaPedir = "2027";

        await vm.ClonarCommand.ExecuteAsync(null);

        vm.VigenciaSeleccionada!.Anio.Should().Be(2027);
        vm.VigenciaSeleccionada.Activa.Should().BeFalse();
        vm.Filas.Should().HaveCount(9);
        vm.TituloTabla.Should().Be("TABLA DE TARIFAS (BORRADOR) (AÑO 2027)");
        vm.ActivarCommand.CanExecute(null).Should().BeTrue();

        dialogos.RespuestaConfirmar = true;
        await vm.ActivarCommand.ExecuteAsync(null);

        (await servicio.ListarAsync()).Single(v => v.Activa).Anio.Should().Be(2027);
        vm.VigenciaSeleccionada!.Activa.Should().BeTrue();
        vm.ActivarCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task No_se_puede_activar_una_vigencia_con_solapamientos_y_la_activa_actual_se_conserva()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, servicio) = Crear(db);
        await vm.CargarAsync();
        dialogos.RespuestaPedir = "2027";
        await vm.ClonarCommand.ExecuteAsync(null);
        vm.Filas[1].Sexo = Sexo.Ambos;     // Terneros Macho 0-2 pasa a "Ambos" 0-5 y se cruza con Terneros "Ambos" 3-7
        vm.Filas[1].EdadMinimaMeses = 0;
        vm.Filas[1].EdadMaximaMeses = 5;
        dialogos.RespuestaConfirmar = true;

        await vm.ActivarCommand.ExecuteAsync(null);

        dialogos.Advertencias.Should().NotBeEmpty();
        (await servicio.ListarAsync()).Single(v => v.Activa).Anio.Should().Be(2026);
    }

    [Fact]
    public async Task Crear_una_lista_de_un_nuevo_ano_pide_el_ano_y_lo_valida()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, servicio) = Crear(db);
        await vm.CargarAsync();

        dialogos.RespuestaPedir = "dos mil";
        await vm.CrearNuevoAnioCommand.ExecuteAsync(null);
        dialogos.UltimaAdvertencia.Should().Contain("cuatro cifras");

        dialogos.RespuestaPedir = "2026";
        await vm.CrearNuevoAnioCommand.ExecuteAsync(null);
        dialogos.UltimaAdvertencia.Should().Contain("Ya existe una vigencia");

        dialogos.RespuestaPedir = "2028";
        await vm.CrearNuevoAnioCommand.ExecuteAsync(null);
        (await servicio.ListarAsync()).Select(v => v.Anio).Should().Equal(2028, 2026);
        vm.VigenciaSeleccionada!.Anio.Should().Be(2028);
        vm.Filas.Should().BeEmpty("la vigencia nueva nace vacía");
    }

    [Fact]
    public async Task Quitar_una_fila_la_elimina_de_la_lista_y_marca_cambios()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, _, _) = Crear(db);
        await vm.CargarAsync();

        vm.QuitarFilaCommand.Execute(vm.Filas[^1]);

        vm.Filas.Should().HaveCount(8);
        vm.HayCambios.Should().BeTrue();
    }

    [Fact]
    public async Task Valorizar_a_una_fecha_aplica_la_matriz_vigente_y_resume_los_cambios()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaTerneraAsync("T-1", nacimiento: "2026-07-15", valor: 150_000m); // con 3 meses la matriz le asigna 800 mil
        var dialogos = new DialogoFalso { RespuestaPedir = "06/11/2026", RespuestaConfirmar = true };
        var vm = CrearConEscenario(e, dialogos);

        await vm.ValorizarAFechaCommand.ExecuteAsync(null);

        (await e.AnimalAsync("T-1")).ValorContableActual.Should().Be(800_000m);
        dialogos.Informaciones.Should().ContainSingle().Which
            .Should().Contain("Valorización al 06/11/2026").And.Contain("Animales evaluados: 1").And.Contain("+$650.000");
        dialogos.Confirmaciones.Should().ContainSingle().Which.Should().Contain("06/11/2026");
        await e.AsegurarInvarianteAsync();
    }

    [Theory]
    [InlineData("31/02/2026", "no es válida")]
    [InlineData("07/11/2026", "fecha futura")]
    [InlineData("30/10/2026", "anterior al último cierre")]
    public async Task Valorizar_a_una_fecha_rechaza_fechas_invalidas_futuras_o_anteriores_al_ultimo_cierre(string fecha, string mensaje)
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaTerneraAsync("T-1", nacimiento: "2026-07-15", valor: 150_000m);
        var dialogos = new DialogoFalso { RespuestaPedir = fecha, RespuestaConfirmar = true };
        var vm = CrearConEscenario(e, dialogos);

        await vm.ValorizarAFechaCommand.ExecuteAsync(null);

        dialogos.Advertencias.Should().ContainSingle().Which.Should().Contain(mensaje);
        dialogos.Informaciones.Should().BeEmpty();
        (await e.AnimalAsync("T-1")).ValorContableActual.Should().Be(150_000m, "nada se valorizó");
    }

    [Fact]
    public async Task Valorizar_a_una_fecha_se_cancela_si_el_usuario_cierra_el_cuadro_o_no_confirma()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaTerneraAsync("T-1", nacimiento: "2026-07-15", valor: 150_000m);
        var dialogos = new DialogoFalso { RespuestaPedir = null, RespuestaConfirmar = true };
        var vm = CrearConEscenario(e, dialogos);

        await vm.ValorizarAFechaCommand.ExecuteAsync(null);
        dialogos.RespuestaPedir = "06/11/2026";
        dialogos.RespuestaConfirmar = false;
        await vm.ValorizarAFechaCommand.ExecuteAsync(null);

        (await e.AnimalAsync("T-1")).ValorContableActual.Should().Be(150_000m);
        dialogos.Informaciones.Should().BeEmpty();
        dialogos.Advertencias.Should().BeEmpty();
    }

    [Fact]
    public async Task Salir_con_cambios_sin_guardar_pide_confirmacion()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (vm, dialogos, _) = Crear(db);
        await vm.CargarAsync();
        vm.PuedeSalir().Should().BeTrue("sin cambios no se pregunta");
        dialogos.Confirmaciones.Should().BeEmpty();

        vm.Filas[0].ValorBase = 200_000m;
        dialogos.RespuestaConfirmar = false;
        vm.PuedeSalir().Should().BeFalse();

        dialogos.RespuestaConfirmar = true;
        vm.PuedeSalir().Should().BeTrue();
        dialogos.Confirmaciones.Should().HaveCount(2).And.OnlyContain(m => m.Contains("sin guardar"));
    }
}
