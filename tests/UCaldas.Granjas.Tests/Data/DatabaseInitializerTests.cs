using UCaldas.Granjas.Data;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Data;

public class DatabaseInitializerTests
{
    private static readonly string[] TablasEsperadas =
    [
        "ANIMAL", "CATEGORIA_ETAPA", "CONFIGURACION_FIRMA", "CONFIGURACION_SISTEMA", "DATOS_GENEALOGIA", "GRANJA",
        "GRANJA_PROGRAMA", "LOTE_POBLACION", "MOVIMIENTO_INVENTARIO", "OFICIO_VENTA", "ORIENTACION_PRODUCTIVA",
        "PROGRAMA", "REMISION", "REMISION_DETALLE", "TABLA_VALORIZACION_BASE", "VIGENCIA_VALORIZACION",
    ];

    [Fact]
    public async Task Crea_las_16_tablas_del_esquema()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var tablas = await db.ConsultarAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

        tablas.Should().Equal(TablasEsperadas);
    }

    [Fact]
    public async Task Siembra_las_tres_granjas_con_su_orientacion_sugerida()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);

        var granjas = await uow.Granjas.GetAllAsync();

        granjas.Select(g => (g.Codigo, g.Nombre, g.Activa)).Should().Equal(
            ("G-01", "Tesorito", true), ("G-02", "La Cruz", true), ("G-03", "Montelindo", true));
        granjas.Single(g => g.Codigo == "G-01").OrientacionId.Should().Be(ids.Lacteo);
        granjas.Single(g => g.Codigo == "G-02").OrientacionId.Should().BeNull("La Cruz es un hato mixto sin sugerencia");
        granjas.Single(g => g.Codigo == "G-03").OrientacionId.Should().Be(ids.Carne);
    }

    [Fact]
    public async Task Solo_existen_las_orientaciones_Lacteo_y_Carne()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();

        var orientaciones = await uow.Orientaciones.GetAllAsync();

        orientaciones.Select(o => o.Nombre).Should().Equal(NombresBase.OrientacionLactea, NombresBase.OrientacionCarne);
    }

    [Fact]
    public async Task Siembra_los_programas_y_su_asociacion_con_las_granjas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);

        var programas = await uow.Programas.GetAllAsync();
        var ave = programas.Single(p => p.Codigo == "PRG-AVE");

        programas.Select(p => (p.Codigo, p.TipoInventario)).Should().Equal(
            ("PRG-GAN", TipoInventario.Individual), ("PRG-AVE", TipoInventario.Lote));
        (await uow.GranjaProgramas.GetGranjaIdsAsync(ids.ProgramaGanaderia))
            .Should().BeEquivalentTo(new[] { ids.Tesorito, ids.LaCruz, ids.Montelindo });
        (await uow.GranjaProgramas.GetGranjaIdsAsync(ave.Id)).Should().Equal(ids.Montelindo);
    }

    [Fact]
    public async Task Siembra_las_categorias_incluida_Sin_clasificar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);

        var categorias = await uow.Categorias.GetByProgramaAsync(ids.ProgramaGanaderia);

        categorias.Select(c => (c.Nombre, c.Sexo)).Should().Equal(
            ("Terneros", "Ambos"), ("Levante", "Ambos"), ("Novilla Vientre", "Hembra"),
            ("Vacas General", "Hembra"), ("Reproductor", "Macho"), ("Sin clasificar", "Ambos"));
    }

    [Fact]
    public async Task Siembra_la_matriz_2026_oficial_en_la_vigencia_activa()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);

        var vigencia = await uow.Vigencias.GetActivaAsync();
        var tarifas = await uow.Tarifas.GetByVigenciaAsync(vigencia!.Id);

        vigencia.Anio.Should().Be(2026);
        tarifas.Should().HaveCount(9);
        tarifas.Sum(t => t.ValorEstandar).Should().Be(25_710_000m);

        var reproductor = tarifas.Single(t => t.CategoriaId == ids.Categoria(NombresBase.CategoriaReproductor));
        reproductor.Sexo.Should().Be(Sexo.Macho);
        reproductor.EdadMinimaMeses.Should().Be(18);
        reproductor.EdadMaximaMeses.Should().Be(999);
        reproductor.OrientacionId.Should().BeNull("el Reproductor aplica a todas las orientaciones");
        reproductor.RequierePartos.Should().BeFalse();
        reproductor.ValorEstandar.Should().Be(7_000_000m);

        var vacas = tarifas.Where(t => t.CategoriaId == ids.Categoria(NombresBase.CategoriaVacasGeneral)).ToList();
        vacas.Should().OnlyContain(t => t.RequierePartos && t.EdadMinimaMeses == 24 && t.EdadMaximaMeses == 999);
        vacas.Single(t => t.OrientacionId == ids.Lacteo).ValorEstandar.Should().Be(6_000_000m);
        vacas.Single(t => t.OrientacionId == ids.Carne).ValorEstandar.Should().Be(4_000_000m);

        var terneros = tarifas.Where(t => t.CategoriaId == ids.Categoria(NombresBase.CategoriaTerneros)).ToList();
        terneros.Single(t => t.Sexo == Sexo.Hembra).ValorEstandar.Should().Be(150_000m);
        terneros.Single(t => t.Sexo == Sexo.Macho).ValorEstandar.Should().Be(160_000m);
    }

    [Fact]
    public async Task No_siembra_la_configuracion_del_sistema_para_que_el_primer_arranque_cree_la_contrasena()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();

        (await uow.Configuracion.GetSistemaAsync()).Should().BeNull();

        var firma = await uow.Configuracion.GetFirmaAsync();
        firma.Should().NotBeNull();
        firma!.EntidadFacultad.Should().Be("FACULTAD DE CIENCIAS AGROPECUARIAS");
        firma.RutaImagenFirma.Should().BeNull();
    }

    [Fact]
    public async Task Es_idempotente_y_no_toca_los_datos_existentes()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await db.EjecutarAsync("INSERT INTO GRANJA (codigo, nombre) VALUES ('G-99', 'Prueba')");

        await new DatabaseInitializer(db.Conexiones).InitializeAsync();

        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(4);
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM TABLA_VALORIZACION_BASE")).Should().Be(9);
        (await db.EscalarAsync<long>("PRAGMA user_version")).Should().Be(DatabaseInitializer.VersionEsquemaActual);
    }

    [Fact]
    public async Task Rechaza_una_version_de_esquema_desconocida_sin_modificar_datos()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await db.EjecutarAsync("PRAGMA user_version = 99");

        var act = () => new DatabaseInitializer(db.Conexiones).InitializeAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*versión de esquema desconocida*");
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(3);
    }

    [Fact]
    public async Task Crea_la_carpeta_de_la_base_si_no_existe()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "SistemaGranjasPruebas", Guid.NewGuid().ToString("N"), "subcarpeta", "otra");
        var conexiones = new SqliteConnectionFactory(Path.Combine(carpeta, "granjas.db"));

        try
        {
            await new DatabaseInitializer(conexiones).InitializeAsync();

            File.Exists(conexiones.DatabasePath).Should().BeTrue();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetFullPath(Path.Combine(carpeta, "..", "..")), recursive: true);
        }
    }
}
