using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;
using UCaldas.Granjas.Tests.Presentacion;

namespace UCaldas.Granjas.Tests.Consultas;

/// <summary>El histórico de compras cambia con el programa y la granja elegidos (animales individuales y lotes).</summary>
public class ComprasPorProgramaTests
{
    private static readonly DateTime Desde = new(2026, 10, 1);
    private static readonly DateTime Hasta = new(2026, 11, 30);

    private sealed record Escenario(EscenarioKardex E, Programa Equinos, Programa Aves, ConsultasService Consultas, int IdLote);

    /// <summary>Bovinos: FAC-BOV (2 animales, Tesorito). Equinos: FAC-EQ (2 caballos, La Cruz). Avícola: lote L-AVE-1 comprado en Montelindo.</summary>
    private static async Task<Escenario> CrearAsync()
    {
        var e = await EscenarioKardex.CrearAsync();
        var equinos = await new CatalogoService(e.Db.Uow).GuardarProgramaAsync(
            new Programa { Codigo = "PRG-EQU", Nombre = "Equinos", TipoInventario = TipoInventario.Individual }, [e.Ids.Tesorito, e.Ids.LaCruz]);
        int general, etapaAve;
        Programa aves;
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            general = (await uow.Categorias.GetByNombreAsync(equinos.Id, NombresBase.CategoriaGeneral))!.Id;
            aves = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!;
            etapaAve = (await uow.Categorias.GetByNombreAsync(aves.Id, NombresBase.CategoriaGeneral))!.Id;
        }

        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-BOV", e.AnimalComprado("BOV-1", Sexo.Hembra, "2026-06-01", 900_000m), e.AnimalComprado("BOV-2", Sexo.Macho, "2026-06-01", 900_000m)));
        var compraEquino = e.Compra("FAC-EQ", e.AnimalComprado("EQ-1", Sexo.Macho, "2020-01-01", 5_000_000m), e.AnimalComprado("EQ-2", Sexo.Hembra, "2020-01-01", 4_000_000m));
        compraEquino.GranjaDestinoId = e.Ids.LaCruz;
        foreach (var animal in compraEquino.Animales)
        {
            animal.OrientacionId = 0;
            animal.CategoriaId = general;
        }

        await e.Kardex.RegistrarCompraAsync(compraEquino);

        var idLote = await new LoteService(e.Db.Uow, e.Reloj).RegistrarNuevoLoteAsync(new NuevoLoteDto
        {
            ProgramaId = aves.Id, GranjaId = e.Ids.Montelindo, EtapaId = etapaAve, ClaseEntrada = ClaseEntrada.Compra, CodigoLote = "L-AVE-1",
            Ubicacion = "Galpón 1", LineaRaza = "Hy-Line", FechaIngreso = Datos.F("2026-10-25"), Cantidad = 1000, PrecioUnitarioFactura = 15_000m,
            Factura = "FAC-AVE", OrdenCompra = "OC-AVE", Proveedor = "Incubadora Andina",
        });
        return new Escenario(e, equinos, aves, new ConsultasService(e.Db.Uow, e.Reloj), idLote);
    }

    [Fact]
    public async Task Cada_programa_individual_muestra_solo_sus_facturas_y_sin_programa_se_ven_todas()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var ids = s.E.Ids;

        var bovinos = await s.Consultas.ListarComprasAsync(new CompraFiltro(null, Desde, Hasta, ProgramaId: ids.ProgramaGanaderia));
        var equinos = await s.Consultas.ListarComprasAsync(new CompraFiltro(null, Desde, Hasta, ProgramaId: s.Equinos.Id));
        var todas = await s.Consultas.ListarComprasAsync(new CompraFiltro(null, Desde, Hasta));

        bovinos.Items.Select(i => i.NumeroFactura).Should().Equal("FAC-BOV");
        equinos.Items.Select(i => i.NumeroFactura).Should().Equal("FAC-EQ");
        equinos.Resumen.Should().Be(new ResumenCompras(1, 2, 9_000_000m));
        todas.Items.Select(i => i.NumeroFactura).Should().BeEquivalentTo("FAC-BOV", "FAC-EQ", "FAC-AVE");

        var fila = equinos.Items.Single();
        (fila.Identificador, fila.CategoriaEtapa, fila.ValorUnitario, fila.LoteId).Should().Be(("EQ-1, EQ-2", "General", 4_500_000m, (int?)null));
    }

    [Fact]
    public async Task Con_una_granja_elegida_se_muestran_solo_las_compras_de_esa_granja_en_cada_programa()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var ids = s.E.Ids;

        (await s.Consultas.ListarComprasAsync(new CompraFiltro(ids.LaCruz, Desde, Hasta, ProgramaId: s.Equinos.Id))).Items.Should().ContainSingle();
        (await s.Consultas.ListarComprasAsync(new CompraFiltro(ids.Tesorito, Desde, Hasta, ProgramaId: s.Equinos.Id))).Items.Should().BeEmpty();
        (await s.Consultas.ListarComprasAsync(new CompraFiltro(ids.Tesorito, Desde, Hasta, ProgramaId: ids.ProgramaGanaderia))).Items.Should().ContainSingle();
        (await s.Consultas.ListarComprasAsync(new CompraFiltro(ids.LaCruz, Desde, Hasta, ProgramaId: ids.ProgramaGanaderia))).Items.Should().BeEmpty();
        (await s.Consultas.ListarComprasAsync(new CompraFiltro(ids.Montelindo, Desde, Hasta, ProgramaId: s.Aves.Id))).Items.Should().ContainSingle();
        (await s.Consultas.ListarComprasAsync(new CompraFiltro(ids.Tesorito, Desde, Hasta, ProgramaId: s.Aves.Id))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_programa_de_lotes_muestra_los_lotes_comprados_con_su_cantidad_y_valor()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        var r = await s.Consultas.ListarComprasAsync(new CompraFiltro(null, Desde, Hasta, ProgramaId: s.Aves.Id));

        var fila = r.Items.Single();
        (fila.LoteId, fila.NumeroFactura, fila.OrdenCompra, fila.Proveedor, fila.GranjaDestino).Should().Be((s.IdLote, "FAC-AVE", "OC-AVE", "Incubadora Andina", "Montelindo"));
        (fila.Identificador, fila.CategoriaEtapa, fila.Cantidad, fila.ValorUnitario, fila.TotalFactura).Should().Be(("L-AVE-1", "General", 1000, 15_000m, 15_000_000m));
        r.Resumen.Should().Be(new ResumenCompras(1, 1000, 15_000_000m));

        (await s.Consultas.ListarComprasAsync(new CompraFiltro(null, new DateTime(2026, 11, 1), Hasta, ProgramaId: s.Aves.Id))).Items.Should().BeEmpty("fuera del rango de fechas");
        (await s.Consultas.ListarComprasAsync(new CompraFiltro(null, Desde, Hasta, "ave", ProgramaId: s.Aves.Id))).Items.Should().ContainSingle("la búsqueda por factura también aplica a lotes");
        (await s.Consultas.ListarComprasAsync(new CompraFiltro(null, Desde, Hasta, "zzz", ProgramaId: s.Aves.Id))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Una_compra_anulada_sin_animales_se_conserva_en_el_programa_bovino()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        await s.E.Kardex.RegistrarCompraAsync(s.E.Compra("FAC-X", s.E.AnimalComprado("BOV-9", Sexo.Hembra, "2026-06-01", 500_000m)));
        await s.E.Kardex.AnularAsync((await s.E.RemisionAsync("FAC-X")).Id, "error");
        var filtro = new CompraFiltro(null, Desde, Hasta, IncluirAnuladas: true, ProgramaId: s.E.Ids.ProgramaGanaderia);

        (await s.Consultas.ListarComprasAsync(filtro)).Items.Should().ContainSingle(i => i.Anulada && i.NumeroFactura == "FAC-X");
        (await s.Consultas.ListarComprasAsync(filtro with { ProgramaId = s.Equinos.Id })).Items.Select(i => i.NumeroFactura).Should().Equal("FAC-EQ");
    }

    [Fact]
    public async Task La_pantalla_de_compras_se_actualiza_al_cambiar_el_programa_y_la_granja_del_contexto()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var contexto = new ContextoGlobal { ProgramaId = s.E.Ids.ProgramaGanaderia };
        var formularios = new FormulariosFalso();
        var vm = new ComprasViewModel(s.Consultas, formularios, new NavegadorFalso(), contexto, s.E.Reloj, new DialogoFalso());

        await vm.CargarAsync();
        vm.Compras.Select(c => c.NumeroFactura).Should().Equal("FAC-BOV");
        (vm.TotalCompras, vm.AnimalesComprados).Should().Be((1, 2));

        contexto.ProgramaId = s.Equinos.Id;
        await vm.CargarAsync();
        vm.Compras.Select(c => c.NumeroFactura).Should().Equal("FAC-EQ");
        vm.ValorTotalTexto.Should().Be("$9.000.000");

        contexto.ProgramaId = s.Aves.Id;
        await vm.CargarAsync();
        (vm.TotalCompras, vm.AnimalesComprados, vm.ValorTotalTexto).Should().Be((1, 1000, "$15.000.000"));

        contexto.GranjaId = s.E.Ids.Tesorito;
        await vm.CargarAsync();
        vm.Compras.Should().BeEmpty();
        (vm.TotalCompras, vm.AnimalesComprados, vm.ValorTotalTexto).Should().Be((0, 0, "$0"));
    }

    [Fact]
    public async Task El_detalle_de_una_fila_abre_la_factura_y_el_de_un_lote_abre_su_ficha()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var contexto = new ContextoGlobal { ProgramaId = s.Aves.Id };
        var formularios = new FormulariosFalso { Resultado = false };
        var vm = new ComprasViewModel(s.Consultas, formularios, new NavegadorFalso(), contexto, s.E.Reloj, new DialogoFalso());
        await vm.CargarAsync();

        await vm.VerDetalleCommand.ExecuteAsync(vm.Compras[0]);
        contexto.ProgramaId = s.E.Ids.ProgramaGanaderia;
        await vm.CargarAsync();
        await vm.VerDetalleCommand.ExecuteAsync(vm.Compras[0]);

        formularios.Llamadas.Should().Equal($"compralote:{s.IdLote}", $"compra:{vm.Compras[0].RemisionId}");
    }
}