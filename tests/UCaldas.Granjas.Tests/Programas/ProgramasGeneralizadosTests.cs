using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Programas;

/// <summary>Programas generalizados: categoría «General», valores de referencia y el camino sin valorización biológica.</summary>
public class ProgramasGeneralizadosTests
{
    private static async Task<Programa> CrearEquinosAsync(EscenarioKardex e, string unidad = "UNIDAD")
        => await new CatalogoService(e.Db.Uow).GuardarProgramaAsync(
            new Programa { Codigo = "PRG-EQU", Nombre = "Equinos", TipoInventario = TipoInventario.Individual, UnidadVentaDefault = unidad },
            [e.Ids.Tesorito]);

    private static async Task<int> IdGeneralAsync(EscenarioKardex e, int programaId)
    {
        await using var uow = await e.Db.Uow.OpenReadOnlyAsync();
        return (await uow.Categorias.GetByNombreAsync(programaId, NombresBase.CategoriaGeneral))!.Id;
    }

    // ---- Creación de programas ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_programa_nuevo_nace_sin_valorizacion_biologica_y_con_su_categoria_General()
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var equinos = await CrearEquinosAsync(e, "KG");

        equinos.UsaValorizacionBiologica.Should().BeFalse();
        await using var uow = await e.Db.Uow.OpenReadOnlyAsync();
        var guardado = (await uow.Programas.GetByIdAsync(equinos.Id))!;
        (guardado.UsaValorizacionBiologica, guardado.UnidadVentaDefault).Should().Be((false, "KG"));
        var categorias = await uow.Categorias.GetByProgramaAsync(equinos.Id);
        categorias.Should().ContainSingle().Which.Nombre.Should().Be("General");
        var tarifa = (await uow.TarifasPrograma.GetByProgramaAsync(equinos.Id)).Should().ContainSingle().Subject;
        (tarifa.NombreCategoria, tarifa.UnidadCobro, tarifa.ValorReferencia, tarifa.EsDefault, tarifa.CategoriaId)
            .Should().Be(("General", "KG", 0m, true, categorias[0].Id));
    }

    [Fact]
    public async Task Solo_la_ganaderia_sembrada_usa_la_valorizacion_biologica_y_editar_un_programa_no_cambia_eso()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var catalogo = new CatalogoService(e.Db.Uow);
        var equinos = await CrearEquinosAsync(e);

        // Aunque alguien intente marcarlo desde fuera, el servicio no lo concede.
        equinos.UsaValorizacionBiologica = true;
        equinos.Nombre = "Equinos y mulares";
        await catalogo.GuardarProgramaAsync(equinos, [e.Ids.Tesorito]);

        var programas = await catalogo.GetProgramasAsync();
        programas.Single(p => p.Codigo == "PRG-EQU").UsaValorizacionBiologica.Should().BeFalse();
        programas.Single(p => p.Codigo == "PRG-GAN").UsaValorizacionBiologica.Should().BeTrue();
    }

    [Fact]
    public async Task La_unidad_base_de_venta_debe_ser_UNIDAD_o_KG()
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var act = () => CrearEquinosAsync(e, "LITROS");

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*UNIDAD*KG*");
    }

    [Fact]
    public async Task Un_programa_nuevo_sin_inventario_se_puede_eliminar_con_sus_categorias_y_uno_con_animales_no()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var catalogo = new CatalogoService(e.Db.Uow);
        var vacio = await CrearEquinosAsync(e);

        await catalogo.EliminarProgramaAsync(vacio.Id);

        (await e.ContarAsync("TARIFA_CATEGORIA_PROGRAMA WHERE programa_id = " + vacio.Id)).Should().Be(0);
        (await e.ContarAsync("CATEGORIA_ETAPA WHERE programa_id = " + vacio.Id)).Should().Be(0);

        var equinos = await CrearEquinosAsync(e);
        var compra = e.Compra("FAC-EQ", e.AnimalComprado("CAB-1", Sexo.Macho, "2020-01-01", 5_000_000m));
        compra.Animales[0].OrientacionId = 0;
        compra.Animales[0].CategoriaId = await IdGeneralAsync(e, equinos.Id);
        await e.Kardex.RegistrarCompraAsync(compra);

        var act = () => catalogo.EliminarProgramaAsync(equinos.Id);
        await act.Should().ThrowAsync<ValidationException>();
    }

    // ---- Tarifas de referencia ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Se_agregan_renombran_y_valoran_categorias_y_cada_una_tiene_su_CategoriaEtapa()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var equinos = await CrearEquinosAsync(e);
        var servicio = new TarifaProgramaService(e.Db.Uow);

        var filas = (await servicio.ListarAsync(equinos.Id)).ToList();
        filas[0].ValorReferencia = 100_000m;
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = "Potro", UnidadCobro = "UNIDAD", ValorReferencia = 2_500_000m });
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = "Yegua", UnidadCobro = "KG", ValorReferencia = 4_000m });
        await servicio.GuardarAsync(equinos.Id, filas);

        var guardadas = await servicio.ListarAsync(equinos.Id);
        guardadas.Select(f => (f.NombreCategoria, f.UnidadCobro, f.ValorReferencia, f.EsDefault)).Should().Equal(
            ("General", "UNIDAD", 100_000m, true), ("Potro", "UNIDAD", 2_500_000m, false), ("Yegua", "KG", 4_000m, false));
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            (await uow.Categorias.GetByProgramaAsync(equinos.Id)).Select(c => c.Nombre).Should().Equal("General", "Potro", "Yegua");
        }

        // Renombrar una categoría actualiza también su CategoriaEtapa; quitar una la elimina.
        var potro = guardadas.Single(f => f.NombreCategoria == "Potro");
        potro.NombreCategoria = "Potrillo";
        await servicio.GuardarAsync(equinos.Id, guardadas.Where(f => f.NombreCategoria != "Yegua").ToList());

        await using var lectura = await e.Db.Uow.OpenReadOnlyAsync();
        (await lectura.Categorias.GetByProgramaAsync(equinos.Id)).Select(c => c.Nombre).Should().Equal("General", "Potrillo");
        (await servicio.ValorDeReferenciaAsync(potro.CategoriaId)).Should().Be(2_500_000m);
    }

    [Theory]
    [InlineData("", "UNIDAD", 0, "*nombre*")]
    [InlineData("Potro", "LITROS", 0, "*UNIDAD o KG*")]
    [InlineData("Potro", "UNIDAD", -1, "*negativo*")]
    [InlineData("General", "UNIDAD", 0, "*repetida*")]
    [InlineData("Sin clasificar", "UNIDAD", 0, "*reservado*")]
    public async Task Las_filas_invalidas_se_rechazan_sin_guardar_nada(string nombre, string unidad, int valor, string mensaje)
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var equinos = await CrearEquinosAsync(e);
        var servicio = new TarifaProgramaService(e.Db.Uow);
        var filas = (await servicio.ListarAsync(equinos.Id)).ToList();
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = nombre, UnidadCobro = unidad, ValorReferencia = valor });

        var act = () => servicio.GuardarAsync(equinos.Id, filas);

        await act.Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
        (await servicio.ListarAsync(equinos.Id)).Should().HaveCount(1);
    }

    [Fact]
    public async Task La_categoria_General_no_se_borra_ni_se_renombra_y_una_en_uso_tampoco_se_elimina()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var equinos = await CrearEquinosAsync(e);
        var servicio = new TarifaProgramaService(e.Db.Uow);
        var general = (await servicio.ListarAsync(equinos.Id)).Single();

        var sinGeneral = () => servicio.GuardarAsync(equinos.Id, []);
        await sinGeneral.Should().ThrowAsync<ValidationException>().WithMessage("*predeterminada*no se puede eliminar*");

        general.NombreCategoria = "Otra";
        var renombrar = () => servicio.GuardarAsync(equinos.Id, [general]);
        await renombrar.Should().ThrowAsync<ValidationException>().WithMessage("*no se puede renombrar*");
        general.NombreCategoria = "General";

        // Una categoría con un animal registrado no se elimina.
        var filas = (await servicio.ListarAsync(equinos.Id)).ToList();
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = "Potro", UnidadCobro = "UNIDAD", ValorReferencia = 1m });
        await servicio.GuardarAsync(equinos.Id, filas);
        var potro = (await servicio.ListarAsync(equinos.Id)).Single(f => f.NombreCategoria == "Potro");
        var compra = e.Compra("FAC-P", e.AnimalComprado("POT-1", Sexo.Macho, "2025-01-01", 900_000m));
        compra.Animales[0].OrientacionId = 0;
        compra.Animales[0].CategoriaId = potro.CategoriaId;
        await e.Kardex.RegistrarCompraAsync(compra);

        var sinPotro = (await servicio.ListarAsync(equinos.Id)).Where(f => f.NombreCategoria != "Potro").ToList();
        var quitar = () => servicio.GuardarAsync(equinos.Id, sinPotro);
        await quitar.Should().ThrowAsync<ValidationException>().WithMessage("*tiene animales o lotes*");
    }

    [Fact]
    public async Task Las_tarifas_de_la_ganaderia_no_se_editan_como_valores_de_referencia()
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var act = () => new TarifaProgramaService(e.Db.Uow).GuardarAsync(e.Ids.ProgramaGanaderia, []);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*matriz de valorización*");
    }

    // ---- Camino B: animales de programas sin valorización biológica ---------------------------------------------------

    [Fact]
    public async Task La_compra_de_un_animal_no_bovino_entra_al_costo_de_la_factura_sin_pasar_por_la_matriz()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var equinos = await CrearEquinosAsync(e);
        var compra = e.Compra("FAC-EQ-1", e.AnimalComprado("CAB-1", Sexo.Macho, "2020-01-01", 5_000_000m));
        compra.Animales[0].OrientacionId = 0;           // la orientación Lácteo/Carne es solo de bovinos
        compra.Animales[0].CategoriaId = await IdGeneralAsync(e, equinos.Id);

        var resultado = await e.Kardex.RegistrarCompraAsync(compra);

        resultado.SinTarifa.Should().BeEmpty("no usa la matriz: no puede quedar «sin tarifa»");
        resultado.TotalCostoSistema.Should().Be(5_000_000m);
        var caballo = await e.AnimalAsync("CAB-1");
        (caballo.ValorCompraOrigen, caballo.ValorContableActual).Should().Be((5_000_000m, 5_000_000m));
        caballo.CategoriaId.Should().Be(compra.Animales[0].CategoriaId);
        caballo.OrientacionId.Should().BeGreaterThan(0, "la columna es obligatoria: se completa con la primera orientación");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task El_nacimiento_de_un_animal_no_bovino_toma_el_valor_de_referencia_de_su_categoria()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var equinos = await CrearEquinosAsync(e);
        var servicio = new TarifaProgramaService(e.Db.Uow);
        var filas = (await servicio.ListarAsync(equinos.Id)).ToList();
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = "Potro", UnidadCobro = "UNIDAD", ValorReferencia = 2_500_000m });
        await servicio.GuardarAsync(equinos.Id, filas);
        var potro = (await servicio.ListarAsync(equinos.Id)).Single(f => f.NombreCategoria == "Potro");
        var general = (await servicio.ListarAsync(equinos.Id)).Single(f => f.EsDefault);

        var dto = e.Nacimiento("POT-01");
        dto.OrientacionId = 0;
        dto.CategoriaId = potro.CategoriaId;
        var resultado = await e.Kardex.RegistrarNacimientoAsync(dto);
        var sinValor = e.Nacimiento("POT-02");
        sinValor.OrientacionId = 0;
        sinValor.CategoriaId = general.CategoriaId;
        await e.Kardex.RegistrarNacimientoAsync(sinValor);

        resultado.Categoria.Should().Be("Potro");
        resultado.ValorInicial.Should().Be(2_500_000m);
        resultado.SinTarifa.Should().BeFalse();
        var animal = await e.AnimalAsync("POT-01");
        (animal.ValorCompraOrigen, animal.ValorContableActual).Should().Be((0m, 2_500_000m), "nació en la granja: compra $0, valor de la categoría");
        (await e.AnimalAsync("POT-02")).ValorContableActual.Should().Be(0m, "«General» vale 0 hasta que se configure");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_nacimiento_sin_orientacion_ni_categoria_se_sigue_rechazando()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = e.Nacimiento("BOV-01");
        dto.OrientacionId = 0;

        var act = () => e.Kardex.RegistrarNacimientoAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*orientación*");
    }

    [Fact]
    public async Task El_cierre_de_valorizacion_solo_procesa_bovinos_y_deja_intacto_el_valor_de_los_demas()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var equinos = await CrearEquinosAsync(e);
        var compra = e.Compra("FAC-EQ-2", e.AnimalComprado("CAB-2", Sexo.Hembra, "2018-01-01", 7_000_000m));
        compra.Animales[0].OrientacionId = 0;
        compra.Animales[0].CategoriaId = await IdGeneralAsync(e, equinos.Id);
        await e.Kardex.RegistrarCompraAsync(compra);
        await e.AltaTerneraAsync("TER-1", nacimiento: "2026-04-01", valor: 150_000m);   // ya no es ternera por edad: la matriz la mueve

        var resumen = await e.Aging.ValorizarAlCorteAsync(Datos.F("2026-11-05"));

        resumen.AnimalesEvaluados.Should().Be(1, "el caballo no entra al AgingEngine");
        var caballo = await e.AnimalAsync("CAB-2");
        caballo.ValorContableActual.Should().Be(7_000_000m);
        (await e.MovimientosAsync("CAB-2")).Should().ContainSingle("solo su asiento de compra: ninguna valorización");
        (await e.AnimalAsync("TER-1")).ValorContableActual.Should().NotBe(150_000m);
        await e.AsegurarInvarianteAsync();
    }
}