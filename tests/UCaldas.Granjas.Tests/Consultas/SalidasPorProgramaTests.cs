using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;
using UCaldas.Granjas.Tests.Presentacion;

namespace UCaldas.Granjas.Tests.Consultas;

/// <summary>Las salidas se consultan por programa y por granja de origen (o por todas).</summary>
public class SalidasPorProgramaTests
{
    private static readonly DateTime Desde = new(2026, 10, 1);
    private static readonly DateTime Hasta = new(2026, 11, 30);

    private sealed record Escenario(EscenarioKardex E, Programa Equinos, ConsultasService Consultas);

    /// <summary>Bovino BOV-2 muerto en Tesorito; caballo EQ-2 muerto en La Cruz.</summary>
    private static async Task<Escenario> CrearAsync()
    {
        var e = await EscenarioKardex.CrearAsync();
        var equinos = await new CatalogoService(e.Db.Uow).GuardarProgramaAsync(
            new Programa { Codigo = "PRG-EQU", Nombre = "Equinos", TipoInventario = TipoInventario.Individual }, [e.Ids.Tesorito, e.Ids.LaCruz]);
        int general;
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            general = (await uow.Categorias.GetByNombreAsync(equinos.Id, NombresBase.CategoriaGeneral))!.Id;
        }

        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-BOV", e.AnimalComprado("BOV-1", Sexo.Hembra, "2026-06-01", 900_000m), e.AnimalComprado("BOV-2", Sexo.Macho, "2026-06-01", 900_000m)));
        var compra = e.Compra("FAC-EQ", e.AnimalComprado("EQ-1", Sexo.Macho, "2020-01-01", 5_000_000m), e.AnimalComprado("EQ-2", Sexo.Hembra, "2020-01-01", 4_000_000m));
        compra.GranjaDestinoId = e.Ids.LaCruz;
        foreach (var a in compra.Animales)
        {
            a.OrientacionId = 0;
            a.CategoriaId = general;
        }

        await e.Kardex.RegistrarCompraAsync(compra);
        await e.Kardex.RegistrarBajaAsync(new BajaDto
        {
            NumeroRemision = "BAJ-BOV", FechaBaja = Datos.F("2026-11-03"), GranjaOrigenId = e.Ids.Tesorito, ClaseSalida = ClaseSalida.Muerte,
            DocumentoSoporte = "Acta 1", Animales = [new BajaAnimalDto { AnimalId = await e.IdAsync("BOV-2") }],
        });
        await e.Kardex.RegistrarBajaAsync(new BajaDto
        {
            NumeroRemision = "BAJ-EQ", FechaBaja = Datos.F("2026-11-04"), GranjaOrigenId = e.Ids.LaCruz, ClaseSalida = ClaseSalida.Muerte,
            DocumentoSoporte = "Acta 2", Animales = [new BajaAnimalDto { AnimalId = await e.IdAsync("EQ-2") }],
        });
        return new Escenario(e, equinos, new ConsultasService(e.Db.Uow, e.Reloj));
    }

    [Fact]
    public async Task Las_salidas_se_separan_por_programa_y_sin_programa_se_ven_todas()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        (await s.Consultas.ListarSalidasAsync(new SalidaFiltro(null, Desde, Hasta, ProgramaId: s.E.Ids.ProgramaGanaderia))).Items.Select(i => i.NumeroRemision).Should().Equal("BAJ-BOV");
        (await s.Consultas.ListarSalidasAsync(new SalidaFiltro(null, Desde, Hasta, ProgramaId: s.Equinos.Id))).Items.Select(i => i.NumeroRemision).Should().Equal("BAJ-EQ");
        (await s.Consultas.ListarSalidasAsync(new SalidaFiltro(null, Desde, Hasta))).Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Con_una_granja_elegida_cada_programa_muestra_solo_las_salidas_de_esa_granja()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var ids = s.E.Ids;

        (await s.Consultas.ListarSalidasAsync(new SalidaFiltro(ids.LaCruz, Desde, Hasta, ProgramaId: s.Equinos.Id))).Items.Should().ContainSingle();
        (await s.Consultas.ListarSalidasAsync(new SalidaFiltro(ids.Tesorito, Desde, Hasta, ProgramaId: s.Equinos.Id))).Items.Should().BeEmpty();
        (await s.Consultas.ListarSalidasAsync(new SalidaFiltro(ids.Tesorito, Desde, Hasta, ProgramaId: ids.ProgramaGanaderia))).Items.Should().ContainSingle();
        (await s.Consultas.ListarSalidasAsync(new SalidaFiltro(ids.LaCruz, Desde, Hasta, ProgramaId: ids.ProgramaGanaderia))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task La_pantalla_de_salidas_se_actualiza_al_cambiar_el_programa_y_la_granja_del_contexto()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var contexto = new ContextoGlobal { ProgramaId = s.E.Ids.ProgramaGanaderia };
        var vm = new SalidasViewModel(s.Consultas, new FormulariosFalso(), new NavegadorFalso(), contexto, s.E.Reloj, new DialogoFalso());

        await vm.CargarAsync();
        vm.Salidas.Select(x => x.NumeroRemision).Should().Equal("BAJ-BOV");

        contexto.ProgramaId = s.Equinos.Id;
        await vm.CargarAsync();
        vm.Salidas.Select(x => x.NumeroRemision).Should().Equal("BAJ-EQ");

        contexto.GranjaId = s.E.Ids.Tesorito;
        await vm.CargarAsync();
        vm.Salidas.Should().BeEmpty();
    }
}