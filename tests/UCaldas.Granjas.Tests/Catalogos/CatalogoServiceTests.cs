using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;
using UCaldas.Granjas.Tests.Reportes;

namespace UCaldas.Granjas.Tests.Catalogos;

public class CatalogoServiceTests
{
    private static CatalogoService Crear(BaseDeDatosDePrueba db) => new(db.Uow);

    // ---- Listas -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lista_las_granjas_programas_orientaciones_y_categorias_sembradas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var servicio = Crear(db);

        (await servicio.GetGranjasAsync()).Select(g => g.Nombre).Should().Equal("Tesorito", "La Cruz", "Montelindo");
        (await servicio.GetProgramasAsync()).Select(p => p.Codigo).Should().Equal("PRG-AVE", "PRG-GAN");
        (await servicio.GetOrientacionesAsync()).Select(o => o.Nombre).Should().Equal("Lácteo", "Carne/Levante");
        (await servicio.GetCategoriasAsync()).Should().HaveCount(7, "las 6 de ganadería más «General» de avicultura");
    }

    [Fact]
    public async Task Los_programas_se_pueden_consultar_por_tipo_de_inventario()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await db.EjecutarAsync(
            "INSERT INTO PROGRAMA (codigo, nombre, tipo_inventario) VALUES ('PRG-EQU', 'Equinos', 'INDIVIDUAL'), ('PRG-PEC', 'Peces', 'LOTE')");
        var servicio = Crear(db);

        (await servicio.GetProgramasPorTipoAsync(TipoInventario.Individual)).Select(p => p.Codigo).Should().Equal("PRG-EQU", "PRG-GAN");
        (await servicio.GetProgramasPorTipoAsync(TipoInventario.Lote)).Select(p => p.Codigo).Should().Equal("PRG-AVE", "PRG-PEC");
        (await servicio.GetProgramasPorTipoAsync("OTRO")).Should().BeEmpty();
    }

    [Fact]
    public async Task Las_categorias_se_pueden_consultar_por_programa()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        await db.EjecutarAsync(
            "INSERT INTO CATEGORIA_ETAPA (nombre, sexo, programa_id) SELECT 'Potro', 'Macho', id FROM PROGRAMA WHERE codigo = 'PRG-AVE'");
        var servicio = Crear(db);

        var ganaderia = await servicio.GetCategoriasDeProgramaAsync(ids.ProgramaGanaderia);
        ganaderia.Select(c => c.Nombre).Should().BeEquivalentTo(
            NombresBase.CategoriaTerneros, NombresBase.CategoriaLevante, NombresBase.CategoriaNovillaVientre,
            NombresBase.CategoriaVacasGeneral, NombresBase.CategoriaReproductor, NombresBase.CategoriaSinClasificar);
        ganaderia.Should().OnlyContain(c => c.ProgramaId == ids.ProgramaGanaderia);

        var aves = (await servicio.GetProgramasAsync()).Single(p => p.Codigo == "PRG-AVE");
        (await servicio.GetCategoriasDeProgramaAsync(aves.Id)).Select(c => c.Nombre).Should().Equal("General", "Potro");
        (await servicio.GetCategoriasDeProgramaAsync(99999)).Should().BeEmpty();
    }

    [Fact]
    public async Task Las_granjas_inactivas_se_pueden_ocultar_de_los_selectores()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await db.EjecutarAsync("UPDATE GRANJA SET activa = 0 WHERE codigo = 'G-03'");

        (await Crear(db).GetGranjasAsync(soloActivas: true)).Select(g => g.Codigo).Should().Equal("G-01", "G-02");
        (await Crear(db).GetGranjasAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task Los_programas_de_una_granja_dependen_de_su_asociacion()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var servicio = Crear(db);

        (await servicio.GetProgramasDeGranjaAsync(ids.Tesorito)).Select(p => p.Codigo).Should().Equal("PRG-GAN");
        (await servicio.GetProgramasDeGranjaAsync(ids.Montelindo)).Select(p => p.Codigo).Should().Equal("PRG-AVE", "PRG-GAN");
    }

    // ---- Razas y líneas por programa ---------------------------------------------------------------------------------

    private static async Task<(CatalogoService Servicio, int Ganaderia, int Aves)> RazasAsync(BaseDeDatosDePrueba db)
    {
        var servicio = Crear(db);
        var programas = await servicio.GetProgramasAsync();
        return (servicio, programas.Single(p => p.Codigo == "PRG-GAN").Id, programas.Single(p => p.Codigo == "PRG-AVE").Id);
    }

    [Fact]
    public async Task Cada_programa_lista_solo_sus_razas_con_la_de_por_defecto_primero()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (servicio, ganaderia, aves) = await RazasAsync(db);

        var bovinos = (await servicio.GetRazasDeProgramaAsync(ganaderia)).Select(r => r.Nombre).ToList();
        bovinos.First().Should().Be(NombresBase.RazaPorDefecto);
        bovinos.Should().Contain("Holstein").And.NotContain("Hy-Line Brown");
        bovinos.Skip(1).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        (await servicio.GetRazasDeProgramaAsync(aves)).Select(r => r.Nombre).Should().Contain("Hy-Line Brown").And.NotContain("Holstein");
        (await servicio.GetRazasDeProgramaAsync(99999)).Should().BeEmpty();
    }

    [Fact]
    public async Task Un_programa_nuevo_nace_con_la_raza_por_defecto_y_al_eliminarlo_se_van_sus_razas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var servicio = Crear(db);

        var equinos = await servicio.GuardarProgramaAsync(
            new Programa { Codigo = "PRG-EQU", Nombre = "Equinos", TipoInventario = TipoInventario.Individual }, []);

        var razas = await servicio.GetRazasDeProgramaAsync(equinos.Id);
        var unica = razas.Should().ContainSingle().Subject;
        (unica.Nombre, unica.EsDefault, unica.Activo).Should().Be(("No Especificada / Cruce", true, true));

        await servicio.EliminarProgramaAsync(equinos.Id);
        (await db.EscalarAsync<long>($"SELECT COUNT(*) FROM RAZA_LINEA_PROGRAMA WHERE programa_id = {equinos.Id}")).Should().Be(0);
    }

    [Fact]
    public async Task Agregar_una_raza_la_normaliza_rechaza_repetidas_y_reactiva_las_inactivas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (servicio, ganaderia, aves) = await RazasAsync(db);

        var nueva = await servicio.AgregarRazaAsync(ganaderia, "  Wagyu ");
        (nueva.Nombre, nueva.EsDefault, nueva.Activo).Should().Be(("Wagyu", false, true));
        (await servicio.GetRazasDeProgramaAsync(aves)).Select(r => r.Nombre).Should().NotContain("Wagyu", "las razas son por programa");
        await servicio.AgregarRazaAsync(aves, "wagyu");   // mismo nombre en otro programa: permitido

        await ((Func<Task>)(() => servicio.AgregarRazaAsync(ganaderia, "WAGYU"))).Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe la raza o línea*");
        await ((Func<Task>)(() => servicio.AgregarRazaAsync(ganaderia, "  "))).Should().ThrowAsync<ValidationException>().WithMessage("*nombre de la raza*");

        await servicio.CambiarEstadoRazaAsync(nueva.Id, activo: false);
        (await servicio.GetRazasDeProgramaAsync(ganaderia)).Select(r => r.Nombre).Should().NotContain("Wagyu");
        (await servicio.GetRazasDeProgramaAsync(ganaderia, soloActivas: false)).Should().Contain(r => r.Nombre == "Wagyu" && !r.Activo);

        var reactivada = await servicio.AgregarRazaAsync(ganaderia, "Wagyu");
        (reactivada.Id, reactivada.Activo).Should().Be((nueva.Id, true));
    }

    [Fact]
    public async Task La_raza_por_defecto_no_se_puede_inactivar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (servicio, ganaderia, _) = await RazasAsync(db);
        var porDefecto = (await servicio.GetRazasDeProgramaAsync(ganaderia)).Single(r => r.EsDefault);

        await ((Func<Task>)(() => servicio.CambiarEstadoRazaAsync(porDefecto.Id, activo: false))).Should().ThrowAsync<ValidationException>().WithMessage("*por defecto*");
        await ((Func<Task>)(() => servicio.CambiarEstadoRazaAsync(99999, activo: false))).Should().ThrowAsync<ValidationException>().WithMessage("*no existe*");
        (await servicio.GetRazasDeProgramaAsync(ganaderia)).Should().Contain(r => r.EsDefault);
    }

    // ---- Granjas ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Crea_una_granja_nueva_con_datos_normalizados()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var granja = await Crear(db).GuardarGranjaAsync(new Granja { Codigo = " G-04 ", Nombre = " Granja Norte ", Activa = true, Observaciones = "  " });

        granja.Id.Should().BeGreaterThan(0);
        granja.Codigo.Should().Be("G-04");
        granja.Nombre.Should().Be("Granja Norte");
        granja.Observaciones.Should().BeNull();
        (await Crear(db).GetGranjasAsync()).Should().HaveCount(4);
    }

    [Theory]
    [InlineData("g-01", "Otra", "*Ya existe una granja con el código g-01*")]
    [InlineData("G-09", "TESORITO", "*Ya existe una granja con el nombre TESORITO*")]
    [InlineData("", "Otra", "*código de la granja es obligatorio*")]
    [InlineData("G-09", "  ", "*nombre de la granja es obligatorio*")]
    public async Task Rechaza_granjas_duplicadas_o_incompletas(string codigo, string nombre, string mensaje)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).GuardarGranjaAsync(new Granja { Codigo = codigo, Nombre = nombre });

        await act.Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
        (await Crear(db).GetGranjasAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task Editar_una_granja_permite_conservar_su_propio_codigo_y_nombre()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var servicio = Crear(db);
        var tesorito = (await servicio.GetGranjasAsync()).Single(g => g.Codigo == "G-01");

        tesorito.Observaciones = "Sede principal actualizada";
        await servicio.GuardarGranjaAsync(tesorito);

        (await servicio.GetGranjasAsync()).Single(g => g.Codigo == "G-01").Observaciones.Should().Be("Sede principal actualizada");
    }

    [Fact]
    public async Task No_se_puede_inactivar_una_granja_con_inventario_disponible_pero_si_sin_el_y_se_puede_reactivar()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var servicio = Crear(e.Db);
        await e.AltaVacaAsync("A-1");
        await e.AltaVacaAsync("A-2");
        var granjas = await servicio.GetGranjasAsync();
        var tesorito = granjas.Single(g => g.Codigo == "G-01");
        var laCruz = granjas.Single(g => g.Codigo == "G-02");

        tesorito.Activa = false;
        var conInventario = () => servicio.GuardarGranjaAsync(tesorito);
        await conInventario.Should().ThrowAsync<ValidationException>().WithMessage("*No se puede inactivar la granja Tesorito: tiene 2 semovientes*");

        laCruz.Activa = false;
        await servicio.GuardarGranjaAsync(laCruz);
        (await servicio.GetGranjasAsync(soloActivas: true)).Select(g => g.Codigo).Should().Equal("G-01", "G-03");

        laCruz.Activa = true;
        await servicio.GuardarGranjaAsync(laCruz);
        (await servicio.GetGranjasAsync(soloActivas: true)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Una_granja_sin_historia_se_elimina_con_sus_asociaciones_y_una_con_historia_no()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var servicio = Crear(e.Db);
        await e.AltaVacaAsync("A-1");

        await servicio.EliminarGranjaAsync(e.Ids.LaCruz);
        var conHistoria = () => servicio.EliminarGranjaAsync(e.Ids.Tesorito);

        (await servicio.GetGranjasAsync()).Select(g => g.Codigo).Should().Equal("G-01", "G-03");
        await conHistoria.Should().ThrowAsync<ValidationException>().WithMessage("*Inactívela*");
        (await e.ContarAsync("GRANJA_PROGRAMA")).Should().Be(3, "se quitaron las asociaciones de La Cruz");
    }

    [Fact]
    public async Task La_orientacion_sugerida_de_una_granja_debe_existir()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).GuardarGranjaAsync(new Granja { Codigo = "G-09", Nombre = "Nueva", OrientacionId = 999 });

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*orientación sugerida no existe*");
    }

    // ---- Programas --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Crea_un_programa_y_lo_asocia_a_varias_granjas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var servicio = Crear(db);

        var programa = await servicio.GuardarProgramaAsync(
            new Programa { Codigo = " PRG-PIS ", Nombre = "Piscicultura", TipoInventario = TipoInventario.Lote }, [ids.Montelindo, ids.Tesorito]);

        programa.Codigo.Should().Be("PRG-PIS");
        (await servicio.GetGranjaIdsDeProgramaAsync(programa.Id)).Should().BeEquivalentTo(new[] { ids.Montelindo, ids.Tesorito });

        // Editar reemplaza las asociaciones.
        await servicio.GuardarProgramaAsync(programa, [ids.LaCruz]);
        (await servicio.GetGranjaIdsDeProgramaAsync(programa.Id)).Should().Equal(ids.LaCruz);
    }

    [Theory]
    [InlineData("prg-gan", TipoInventario.Individual, "*Ya existe un programa con el código*")]
    [InlineData("PRG-X", "OTRO", "*INDIVIDUAL*LOTE*")]
    [InlineData("", TipoInventario.Lote, "*código del programa es obligatorio*")]
    public async Task Rechaza_programas_duplicados_o_con_datos_invalidos(string codigo, string tipo, string mensaje)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).GuardarProgramaAsync(new Programa { Codigo = codigo, Nombre = "X", TipoInventario = tipo }, []);

        await act.Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
    }

    [Fact]
    public async Task Un_programa_con_categorias_no_se_elimina_pero_uno_nuevo_si()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var servicio = Crear(db);
        var gan = (await servicio.GetProgramasAsync()).Single(p => p.Codigo == "PRG-GAN");
        var nuevo = await servicio.GuardarProgramaAsync(new Programa { Codigo = "PRG-X", Nombre = "X", TipoInventario = TipoInventario.Lote }, []);

        var conCategorias = () => servicio.EliminarProgramaAsync(gan.Id);
        await servicio.EliminarProgramaAsync(nuevo.Id);

        await conCategorias.Should().ThrowAsync<ValidationException>().WithMessage("*tiene categorías, oficios o lotes*");
        (await servicio.GetProgramasAsync()).Select(p => p.Codigo).Should().Equal("PRG-AVE", "PRG-GAN");
    }

    [Fact]
    public async Task Una_granja_inexistente_en_la_asociacion_se_rechaza()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).GuardarProgramaAsync(new Programa { Codigo = "PRG-X", Nombre = "X", TipoInventario = TipoInventario.Lote }, [999]);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*granjas seleccionadas no existe*");
    }

    // ---- Firma ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task La_firma_sembrada_trae_valores_de_relleno_y_se_puede_configurar_con_imagen()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var servicio = Crear(db);
        (await servicio.GetFirmaAsync()).NombreAutoridad.Should().Be("NOMBRE DE LA AUTORIDAD");

        await servicio.GuardarFirmaAsync(new ConfiguracionFirma
        {
            NombreAutoridad = " Paula Valencia Arango ", CargoAutoridad = "DECANO(A)", EntidadFacultad = "FACULTAD DE CIENCIAS AGROPECUARIAS",
            Telefono = " 8781586 ", RutaImagenFirma = "data:image/png;base64," + EscenariosDeReporte.PngBase64,
        });

        var firma = await servicio.GetFirmaAsync();
        firma.NombreAutoridad.Should().Be("Paula Valencia Arango");
        firma.Telefono.Should().Be("8781586");
        firma.RutaImagenFirma.Should().StartWith("data:image/png;base64,");
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM CONFIGURACION_FIRMA")).Should().Be(1);
    }

    [Theory]
    [InlineData("", "DECANO", "FACULTAD", "*nombre de la autoridad es obligatorio*")]
    [InlineData("Paula", "", "FACULTAD", "*cargo es obligatorio*")]
    [InlineData("Paula", "DECANO", " ", "*departamento o facultad es obligatorio*")]
    public async Task La_firma_exige_nombre_cargo_y_facultad(string nombre, string cargo, string facultad, string mensaje)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).GuardarFirmaAsync(new ConfiguracionFirma { NombreAutoridad = nombre, CargoAutoridad = cargo, EntidadFacultad = facultad });

        await act.Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
    }

    [Theory]
    [InlineData("esto no es una imagen")]
    [InlineData(@"C:\no-existe\firma.png")]
    [InlineData("data:image/png;base64,@@@@")]
    public async Task Una_imagen_de_firma_invalida_se_rechaza_y_no_cambia_la_firma(string imagen)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).GuardarFirmaAsync(new ConfiguracionFirma
        {
            NombreAutoridad = "Paula", CargoAutoridad = "DECANO", EntidadFacultad = "FACULTAD", RutaImagenFirma = imagen,
        });

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*no es un archivo PNG o JPG válido*");
        (await Crear(db).GetFirmaAsync()).NombreAutoridad.Should().Be("NOMBRE DE LA AUTORIDAD");
    }

    [Fact]
    public async Task Quitar_la_firma_es_guardar_sin_imagen()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var servicio = Crear(db);
        await servicio.GuardarFirmaAsync(new ConfiguracionFirma
        {
            NombreAutoridad = "Paula", CargoAutoridad = "DECANO", EntidadFacultad = "FACULTAD", RutaImagenFirma = "data:image/png;base64," + EscenariosDeReporte.PngBase64,
        });

        var firma = await servicio.GetFirmaAsync();
        firma.RutaImagenFirma = null;
        await servicio.GuardarFirmaAsync(firma);

        (await servicio.GetFirmaAsync()).RutaImagenFirma.Should().BeNull();
    }

    // ---- Preferencias del sistema -------------------------------------------------------------------------------------

    [Fact]
    public async Task Guarda_las_preferencias_de_respaldo_y_usa_la_carpeta_predeterminada_si_va_vacia()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using (var uow = await db.Uow.BeginAsync())
        {
            await Datos.GuardarConfiguracionAsync(uow);
            await uow.CommitAsync();
        }

        var servicio = Crear(db);

        await servicio.GuardarPreferenciasDeRespaldoAsync(false, @" D:\Respaldos ");
        var personalizada = await servicio.GetSistemaAsync();
        personalizada.BackupAutomaticoSalir.Should().BeFalse();
        personalizada.RutaBackupPredeterminada.Should().Be(@"D:\Respaldos");

        await servicio.GuardarPreferenciasDeRespaldoAsync(true, "  ");
        var predeterminada = await servicio.GetSistemaAsync();
        predeterminada.BackupAutomaticoSalir.Should().BeTrue();
        predeterminada.RutaBackupPredeterminada.Should().Be(Rutas.CarpetaRespaldosPredeterminada);
    }

    [Fact]
    public async Task Sin_contrasena_creada_las_preferencias_no_se_pueden_leer_ni_guardar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        await ((Func<Task>)(() => Crear(db).GetSistemaAsync())).Should().ThrowAsync<ValidationException>().WithMessage("*aún no tiene contraseña*");
        await ((Func<Task>)(() => Crear(db).GuardarPreferenciasDeRespaldoAsync(true, null))).Should().ThrowAsync<ValidationException>().WithMessage("*aún no tiene contraseña*");
    }
}
