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
        tarifas.Should().HaveCount(10);
        tarifas.Sum(t => t.ValorEstandar).Should().Be(26_910_000m);

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
    public async Task La_matriz_2026_no_deja_huecos_machos_de_12_a_17_meses_siguen_como_Levante_y_las_novillas_como_novillas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var tarifas = await uow.Tarifas.GetByVigenciaAsync((await uow.Vigencias.GetActivaAsync())!.Id);

        var levanteMachos = tarifas.Single(t => t.CategoriaId == ids.Categoria(NombresBase.CategoriaLevante) && t.Sexo == Sexo.Macho);
        levanteMachos.EdadMinimaMeses.Should().Be(12);
        levanteMachos.EdadMaximaMeses.Should().Be(17);
        levanteMachos.OrientacionId.Should().BeNull();
        levanteMachos.RequierePartos.Should().BeFalse();
        levanteMachos.ValorEstandar.Should().Be(1_200_000m, "mantiene el valor de Levante");

        var novillas = tarifas.Where(t => t.CategoriaId == ids.Categoria(NombresBase.CategoriaNovillaVientre)).ToList();
        novillas.Should().HaveCount(2).And.OnlyContain(t => t.EdadMinimaMeses == 12 && t.EdadMaximaMeses == 999 && !t.RequierePartos);
    }

    // ---- Migración de la versión 1 (esquema v2.1) a la 2 (v2.2) ----------------------------------------------------

    /// <summary>Deja la base como la dejaba la versión 1: sin las columnas de recuperación y con los huecos de la matriz.</summary>
    private static async Task DegradarAVersion1Async(BaseDeDatosDePrueba db)
    {
        await db.EjecutarAsync("DELETE FROM TABLA_VALORIZACION_BASE WHERE sexo = 'Macho' AND edad_minima_meses = 12 AND edad_maxima_meses = 17");
        await db.EjecutarAsync("UPDATE TABLA_VALORIZACION_BASE SET edad_maxima_meses = 24 WHERE sexo = 'Hembra' AND requiere_partos = 0 AND edad_minima_meses = 12");
        foreach (var columna in new[] { "pregunta_secreta", "respuesta_hash", "recovery_key_hash" })
        {
            await db.EjecutarAsync($"ALTER TABLE CONFIGURACION_SISTEMA DROP COLUMN {columna}");
        }

        await db.EjecutarAsync("PRAGMA user_version = 1");
    }

    [Fact]
    public async Task Una_base_de_la_version_1_se_migra_completando_la_matriz_y_agregando_las_columnas_de_recuperacion()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await db.EjecutarAsync("INSERT INTO CONFIGURACION_SISTEMA (password_hash) VALUES ('hash-existente')");
        await DegradarAVersion1Async(db);
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM TABLA_VALORIZACION_BASE")).Should().Be(9);

        await new DatabaseInitializer(db.Conexiones).InitializeAsync();

        (await db.EscalarAsync<long>("PRAGMA user_version")).Should().Be(2);
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var tarifas = await uow.Tarifas.GetByVigenciaAsync((await uow.Vigencias.GetActivaAsync())!.Id);
        tarifas.Should().HaveCount(10);
        var machos = tarifas.Single(t => t.CategoriaId == ids.Categoria(NombresBase.CategoriaLevante) && t.Sexo == Sexo.Macho);
        (machos.EdadMinimaMeses, machos.EdadMaximaMeses, machos.ValorEstandar).Should().Be((12, 17, 1_200_000m));
        tarifas.Where(t => t.CategoriaId == ids.Categoria(NombresBase.CategoriaNovillaVientre))
            .Should().HaveCount(2).And.OnlyContain(t => t.EdadMaximaMeses == 999);

        // Lo que ya existía queda intacto y las columnas nuevas nacen vacías.
        var configuracion = (await uow.Configuracion.GetSistemaAsync())!;
        configuracion.PasswordHash.Should().Be("hash-existente");
        configuracion.PreguntaSecreta.Should().BeNull();
        configuracion.RespuestaHash.Should().BeNull();
        configuracion.RecoveryKeyHash.Should().BeNull();
        (await uow.Granjas.GetAllAsync()).Should().HaveCount(3);

        // Idempotente: una segunda ejecución no cambia nada.
        await new DatabaseInitializer(db.Conexiones).InitializeAsync();
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM TABLA_VALORIZACION_BASE")).Should().Be(10);
    }

    [Fact]
    public async Task La_migracion_no_pisa_una_matriz_que_el_usuario_ya_edito()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await DegradarAVersion1Async(db);
        // El usuario ya alargó la novilla Lácteo hasta los 30 meses y bajó el Reproductor a 12 meses (ya cubre a los machos de 12 a 17).
        await db.EjecutarAsync(
            "UPDATE TABLA_VALORIZACION_BASE SET edad_maxima_meses = 30 WHERE sexo = 'Hembra' AND edad_minima_meses = 12 " +
            "AND orientacion_id = (SELECT id FROM ORIENTACION_PRODUCTIVA WHERE nombre = 'Lácteo')");
        await db.EjecutarAsync("UPDATE TABLA_VALORIZACION_BASE SET edad_minima_meses = 12 WHERE sexo = 'Macho' AND edad_minima_meses = 18");

        await new DatabaseInitializer(db.Conexiones).InitializeAsync();

        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var tarifas = await uow.Tarifas.GetByVigenciaAsync((await uow.Vigencias.GetActivaAsync())!.Id);
        tarifas.Should().HaveCount(9, "no se agrega la fila de Levante porque ya hay otra que cubre esas edades");
        var novillas = tarifas.Where(t => t.CategoriaId == ids.Categoria(NombresBase.CategoriaNovillaVientre)).ToList();
        novillas.Single(t => t.OrientacionId == ids.Lacteo).EdadMaximaMeses.Should().Be(30, "la edición del usuario se respeta");
        novillas.Single(t => t.OrientacionId == ids.Carne).EdadMaximaMeses.Should().Be(999, "la que seguía como la sembró el sistema sí se completa");
    }

    [Fact]
    public async Task Restaurar_un_respaldo_de_la_version_1_lo_migra_de_inmediato()
    {
        await using var origen = await BaseDeDatosDePrueba.CrearAsync();
        await DegradarAVersion1Async(origen);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var copia = Path.Combine(Path.GetTempPath(), "SistemaGranjasPruebas", Guid.NewGuid().ToString("N") + "_v1.db");
        File.Copy(origen.Ruta, copia);
        await using var destino = await BaseDeDatosDePrueba.CrearAsync();
        var mantenimiento = new SqliteDatabaseMaintenance(destino.Conexiones);

        try
        {
            (await mantenimiento.EsBaseValidaAsync(copia)).Should().BeTrue("un respaldo de la versión anterior sigue siendo válido");

            await mantenimiento.ReemplazarBaseAsync(copia);

            (await destino.EscalarAsync<long>("PRAGMA user_version")).Should().Be(DatabaseInitializer.VersionEsquemaActual);
            (await destino.EscalarAsync<long>("SELECT COUNT(*) FROM TABLA_VALORIZACION_BASE")).Should().Be(10);
            (await destino.EscalarAsync<long>("SELECT COUNT(*) FROM pragma_table_info('CONFIGURACION_SISTEMA') WHERE name = 'recovery_key_hash'")).Should().Be(1);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(copia);
        }
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
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM TABLA_VALORIZACION_BASE")).Should().Be(10);
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
