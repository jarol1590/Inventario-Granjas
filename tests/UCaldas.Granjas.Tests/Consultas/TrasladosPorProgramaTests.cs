using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;
using UCaldas.Granjas.Tests.Presentacion;

namespace UCaldas.Granjas.Tests.Consultas;

/// <summary>Los traslados se consultan por programa y por granja (origen o destino), también los de lotes.</summary>
public class TrasladosPorProgramaTests
{
    private static readonly DateTime Desde = new(2026, 10, 1);
    private static readonly DateTime Hasta = new(2026, 11, 30);

    private sealed record Escenario(EscenarioKardex E, Programa Aves, ConsultasService Consultas);

    /// <summary>Traslado de 2 bovinos Tesorito → La Cruz (REM-BOV) y de 300 aves (lote L-1) Montelindo → Tesorito (TRA-L1).</summary>
    private static async Task<Escenario> CrearAsync()
    {
        var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 3_750_000m);
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-BOV", e.Ids.Tesorito, e.Ids.LaCruz, "A-1", "A-2"));

        var lotes = new LoteService(e.Db.Uow, e.Reloj);
        Programa aves;
        int etapa;
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            aves = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!;
            etapa = (await uow.Categorias.GetByNombreAsync(aves.Id, NombresBase.CategoriaGeneral))!.Id;
        }

        var loteId = await lotes.RegistrarNuevoLoteAsync(new NuevoLoteDto
        {
            ProgramaId = aves.Id, GranjaId = e.Ids.Montelindo, EtapaId = etapa, ClaseEntrada = ClaseEntrada.Compra, CodigoLote = "L-1",
            Ubicacion = "Galpón 1", LineaRaza = "Hy-Line", FechaIngreso = Datos.F("2026-10-25"), Cantidad = 1000, PrecioUnitarioFactura = 15_000m,
            Factura = "FAC-L1", OrdenCompra = "OC", Proveedor = "Incubadora",
        });
        await e.Kardex.RegistrarTrasladoLoteAsync(new TrasladoLoteDto
        {
            NumeroRemision = "TRA-L1", FechaTraslado = Datos.F("2026-11-05"), GranjaOrigenId = e.Ids.Montelindo, GranjaDestinoId = e.Ids.Tesorito,
            Lotes = [new TrasladoLoteLineaDto { LoteId = loteId, Cantidad = 300, CodigoNuevoLote = "L-1-T", UbicacionDestino = "Galpón T1" }],
        });
        return new Escenario(e, aves, new ConsultasService(e.Db.Uow, e.Reloj));
    }

    [Fact]
    public async Task Los_traslados_se_separan_por_programa_y_sin_programa_se_ven_todos()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        var bovinos = await s.Consultas.ListarTrasladosAsync(new TrasladoFiltro(null, Desde, Hasta, ProgramaId: s.E.Ids.ProgramaGanaderia));
        bovinos.Items.Select(i => (i.NumeroRemision, i.Cantidad)).Should().Equal(("REM-BOV", 2));

        var aves = await s.Consultas.ListarTrasladosAsync(new TrasladoFiltro(null, Desde, Hasta, ProgramaId: s.Aves.Id));
        aves.Items.Select(i => (i.NumeroRemision, i.Cantidad)).Should().Equal([("TRA-L1", 300)], "la cantidad de un traslado de lotes son cabezas");
        (aves.Resumen.TrasladosRealizados, aves.Resumen.AnimalesMovidos).Should().Be((1, 300));

        (await s.Consultas.ListarTrasladosAsync(new TrasladoFiltro(null, Desde, Hasta))).Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Con_una_granja_elegida_cada_programa_muestra_solo_los_traslados_donde_participa_esa_granja()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var ids = s.E.Ids;

        (await s.Consultas.ListarTrasladosAsync(new TrasladoFiltro(ids.Montelindo, Desde, Hasta, ProgramaId: s.Aves.Id))).Items.Should().ContainSingle("origen");
        (await s.Consultas.ListarTrasladosAsync(new TrasladoFiltro(ids.Tesorito, Desde, Hasta, ProgramaId: s.Aves.Id))).Items.Should().ContainSingle("destino");
        (await s.Consultas.ListarTrasladosAsync(new TrasladoFiltro(ids.LaCruz, Desde, Hasta, ProgramaId: s.Aves.Id))).Items.Should().BeEmpty();
        (await s.Consultas.ListarTrasladosAsync(new TrasladoFiltro(ids.LaCruz, Desde, Hasta, ProgramaId: ids.ProgramaGanaderia))).Items.Should().ContainSingle();
        (await s.Consultas.ListarTrasladosAsync(new TrasladoFiltro(ids.Montelindo, Desde, Hasta, ProgramaId: ids.ProgramaGanaderia))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task La_busqueda_de_texto_tambien_encuentra_traslados_por_codigo_de_lote()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        (await s.Consultas.ListarTrasladosAsync(new TrasladoFiltro(null, Desde, Hasta, Texto: "l-1"))).Items.Select(i => i.NumeroRemision).Should().Equal("TRA-L1");
        (await s.Consultas.ListarTrasladosAsync(new TrasladoFiltro(null, Desde, Hasta, Texto: "a-1"))).Items.Select(i => i.NumeroRemision).Should().Equal("REM-BOV");
    }

    [Fact]
    public async Task La_pantalla_de_traslados_se_actualiza_al_cambiar_el_programa_y_la_granja_del_contexto()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var contexto = new ContextoGlobal { ProgramaId = s.E.Ids.ProgramaGanaderia };
        var vm = new TrasladosViewModel(s.Consultas, s.E.Kardex, new FormulariosFalso(), new NavegadorFalso(), contexto, s.E.Reloj, new DialogoFalso());

        await vm.CargarAsync();
        vm.Traslados.Select(x => x.NumeroRemision).Should().Equal("REM-BOV");

        contexto.ProgramaId = s.Aves.Id;
        await vm.CargarAsync();
        vm.Traslados.Select(x => x.NumeroRemision).Should().Equal("TRA-L1");

        contexto.GranjaId = s.E.Ids.LaCruz;
        await vm.CargarAsync();
        vm.Traslados.Should().BeEmpty();
    }
}
