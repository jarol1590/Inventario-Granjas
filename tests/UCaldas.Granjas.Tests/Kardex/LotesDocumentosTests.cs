using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

/// <summary>Venta y traslado de lotes: documentos editables y anulables que no escriben en el libro del Kardex.</summary>
public class LotesDocumentosTests
{
    private sealed record Entorno(EscenarioKardex E, LoteService Lotes, Programa Aves, int LoteId, ConsultasService Consultas);

    private static async Task<Entorno> CrearAsync(int cantidad = 1000)
    {
        var e = await EscenarioKardex.CrearAsync();
        var lotes = new LoteService(e.Db.Uow, e.Reloj);
        Programa aves;
        int etapa;
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            aves = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!;
            etapa = (await uow.Categorias.GetByNombreAsync(aves.Id, NombresBase.CategoriaGeneral))!.Id;
        }

        var id = await lotes.RegistrarNuevoLoteAsync(NuevoLote("L-1", e.Ids.Montelindo, aves.Id, etapa, cantidad));
        return new Entorno(e, lotes, aves, id, new ConsultasService(e.Db.Uow, e.Reloj));
    }

    private static NuevoLoteDto NuevoLote(string codigo, int granja, int programa, int etapa, int cantidad) => new()
    {
        ProgramaId = programa, GranjaId = granja, EtapaId = etapa, ClaseEntrada = ClaseEntrada.Compra, CodigoLote = codigo, Ubicacion = "Galpón 1",
        LineaRaza = "Hy-Line", FechaIngreso = Datos.F("2026-10-25"), Cantidad = cantidad, PrecioUnitarioFactura = 15_000m,
        Factura = "FAC-" + codigo, OrdenCompra = "OC", Proveedor = "Incubadora",
    };

    private static VentaLoteDto Venta(Entorno s, int cantidad = 400, decimal peso = 900m, decimal valor = 20_000_000m, string remision = "REM-L1") => new()
    {
        NumeroOficio = "OF-1", FechaOficio = Datos.F("2026-11-05"), ProgramaId = s.Aves.Id, NumeroRemision = remision, FechaRemision = Datos.F("2026-11-05"),
        ReciboCaja = "RC-1", GranjaOrigenId = s.E.Ids.Montelindo, Comprador = "Frigorífico del Norte",
        Lotes = [new VentaLoteLineaDto { LoteId = s.LoteId, Cantidad = cantidad, PesoKg = peso, ValorVenta = valor }],
    };

    private static async Task<LotePoblacion> LoteAsync(Entorno s, int? id = null)
        => (await s.Lotes.ObtenerAsync(id ?? s.LoteId)).Lote;

    // ---- Venta ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Una_venta_de_lote_crea_oficio_y_remision_descuenta_cabezas_y_no_toca_el_libro()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        var r = await s.E.Kardex.RegistrarVentaLoteAsync(Venta(s));

        (r.Animales, r.TotalVenta, r.TotalCostoSistema).Should().Be((400, 20_000_000m, 6_000_000m));
        var remision = await s.E.RemisionAsync("REM-L1");
        (remision.TipoMovimiento, remision.ReciboCaja, remision.GranjaOrigenId, remision.OficioId).Should().Be(("VENTA", "RC-1", s.E.Ids.Montelindo, r.OficioId));
        var detalle = (await s.E.DetallesAsync("REM-L1")).Single();
        (detalle.LoteId, detalle.AnimalId, detalle.Cantidad, detalle.PesoVendidoKg, detalle.CostoSistemaLibros).Should().Be((s.LoteId, (int?)null, 400, 900m, 6_000_000m));
        var lote = await LoteAsync(s);
        (lote.CantidadActual, lote.ValorTotalContable, lote.Estado, lote.Operaciones).Should().Be((600, 9_000_000m, EstadoLote.Activo, 1));
        (await s.E.ContarAsync("MOVIMIENTO_INVENTARIO")).Should().Be(0, "el Kardex de lotes todavía no existe");
    }

    [Fact]
    public async Task Vender_todas_las_cabezas_finaliza_el_lote()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        await s.E.Kardex.RegistrarVentaLoteAsync(Venta(s, cantidad: 1000));

        (await LoteAsync(s)).Estado.Should().Be(EstadoLote.Finalizado);
    }

    [Theory]
    [InlineData("exceso")]
    [InlineData("peso")]
    [InlineData("recibo")]
    [InlineData("granja")]
    [InlineData("valor")]
    public async Task Las_ventas_invalidas_se_rechazan_sin_cambiar_el_lote(string caso)
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var dto = Venta(s);
        if (caso == "exceso") dto.Lotes[0].Cantidad = 5000;
        if (caso == "peso") dto.Lotes[0].PesoKg = null;
        if (caso == "recibo") dto.ReciboCaja = "";
        if (caso == "granja") dto.GranjaOrigenId = s.E.Ids.Tesorito;
        if (caso == "valor") dto.Lotes[0].ValorVenta = 0;

        await ((Func<Task>)(() => s.E.Kardex.RegistrarVentaLoteAsync(dto))).Should().ThrowAsync<ValidationException>();

        (await LoteAsync(s)).CantidadActual.Should().Be(1000);
        (await s.E.ContarAsync("REMISION")).Should().Be(0);
    }

    [Fact]
    public async Task Editar_una_venta_de_lote_revierte_y_reaplica_las_cabezas()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var r = await s.E.Kardex.RegistrarVentaLoteAsync(Venta(s));
        var dto = (await s.Consultas.ObtenerVentaLoteAsync(r.RemisionId)).Dto;
        dto.Lotes[0].Cantidad = 700;
        dto.Lotes[0].ValorVenta = 35_000_000m;
        dto.Comprador = "Otro comprador";

        var editada = await s.E.Kardex.EditarVentaLoteAsync(dto);

        (editada.Animales, editada.TotalVenta).Should().Be((700, 35_000_000m));
        var lote = await LoteAsync(s);
        (lote.CantidadActual, lote.Operaciones).Should().Be((300, 1));
        (await s.E.RemisionAsync("REM-L1")).TerceroEntidad.Should().Be("Otro comprador");
        (await s.E.DetallesAsync("REM-L1")).Should().ContainSingle().Which.Cantidad.Should().Be(700);
    }

    [Fact]
    public async Task Anular_una_venta_de_lote_devuelve_las_cabezas_y_reactiva_el_lote()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var r = await s.E.Kardex.RegistrarVentaLoteAsync(Venta(s, cantidad: 1000));

        await s.E.Kardex.AnularAsync(r.RemisionId, "error de captura");

        var lote = await LoteAsync(s);
        (lote.CantidadActual, lote.Estado, lote.Operaciones, lote.ValorTotalContable).Should().Be((1000, EstadoLote.Activo, 0, 15_000_000m));
        (await s.E.RemisionAsync("REM-L1")).Anulada.Should().BeTrue();
        await ((Func<Task>)(() => s.E.Kardex.AnularAsync(r.RemisionId))).Should().ThrowAsync<ValidationException>().WithMessage("*ya está anulado*");
        // El número de la remisión anulada queda consumido.
        await ((Func<Task>)(() => s.E.Kardex.RegistrarVentaLoteAsync(Venta(s)))).Should().ThrowAsync<ValidationException>();
    }

    // ---- Traslado ----------------------------------------------------------------------------------------------------

    private static TrasladoLoteDto Traslado(Entorno s, int cantidad, string? codigoNuevo = null) => new()
    {
        NumeroRemision = "TRA-L1", FechaTraslado = Datos.F("2026-11-05"), GranjaOrigenId = s.E.Ids.Montelindo, GranjaDestinoId = s.E.Ids.Tesorito,
        Lotes = [new TrasladoLoteLineaDto { LoteId = s.LoteId, Cantidad = cantidad, CodigoNuevoLote = codigoNuevo, UbicacionDestino = "Galpón T1" }],
    };

    [Fact]
    public async Task Un_traslado_total_cambia_el_lote_de_granja_y_anularlo_lo_devuelve_con_su_ubicacion()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        var r = await s.E.Kardex.RegistrarTrasladoLoteAsync(Traslado(s, 1000));

        var lote = await LoteAsync(s);
        (lote.GranjaId, lote.Ubicacion, lote.CantidadActual, lote.Operaciones).Should().Be((s.E.Ids.Tesorito, "Galpón T1", 1000, 1));
        (await s.E.ContarAsync("MOVIMIENTO_INVENTARIO")).Should().Be(0);

        await s.E.Kardex.AnularAsync(r.RemisionId);

        lote = await LoteAsync(s);
        (lote.GranjaId, lote.Ubicacion, lote.Operaciones).Should().Be((s.E.Ids.Montelindo, "Galpón 1", 0));
    }

    [Fact]
    public async Task Un_traslado_parcial_crea_el_lote_destino_y_editarlo_o_anularlo_lo_reajusta()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var r = await s.E.Kardex.RegistrarTrasladoLoteAsync(Traslado(s, 300, "L-1-T"));

        var origen = await LoteAsync(s);
        var destinoId = (await s.Lotes.ListarAsync(s.Aves.Id)).Single(d => d.Lote.CodigoLote == "L-1-T").Lote.Id;
        var destino = await LoteAsync(s, destinoId);
        (origen.CantidadActual, origen.ValorTotalContable).Should().Be((700, 10_500_000m));
        (destino.ClaseEntrada, destino.GranjaId, destino.CantidadActual, destino.OrigenReferencia, destino.ValorContableUnitario)
            .Should().Be((ClaseEntrada.Traslado, s.E.Ids.Tesorito, 300, "L-1", 15_000m));

        var dto = (await s.Consultas.ObtenerTrasladoLoteAsync(r.RemisionId)).Dto;
        dto.Lotes[0].Cantidad = 500;
        dto.Lotes[0].CodigoNuevoLote = "L-1-T2";
        await s.E.Kardex.EditarTrasladoLoteAsync(dto);
        (await LoteAsync(s)).CantidadActual.Should().Be(500);
        (await s.Lotes.ListarAsync(s.Aves.Id)).Select(d => d.Lote.CodigoLote).Should().BeEquivalentTo("L-1", "L-1-T2");

        await s.E.Kardex.AnularAsync(r.RemisionId);
        var restaurado = await LoteAsync(s);
        (restaurado.CantidadActual, restaurado.CantidadInicial, restaurado.Operaciones).Should().Be((1000, 1000, 0));
        (await s.Lotes.ListarAsync(s.Aves.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task No_se_anula_un_traslado_parcial_si_el_lote_destino_ya_tuvo_operaciones()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var r = await s.E.Kardex.RegistrarTrasladoLoteAsync(Traslado(s, 300, "L-1-T"));
        var destinoId = (await s.Lotes.ListarAsync(s.Aves.Id)).Single(d => d.Lote.CodigoLote == "L-1-T").Lote.Id;
        await s.Lotes.RegistrarBajaAsync(destinoId, 5, "Mortandad", Datos.F("2026-11-06"), null);

        await ((Func<Task>)(() => s.E.Kardex.AnularAsync(r.RemisionId))).Should().ThrowAsync<ValidationException>().WithMessage("*ya tuvo operaciones*");

        (await LoteAsync(s)).CantidadActual.Should().Be(700);
    }

    [Theory]
    [InlineData("mismaGranja")]
    [InlineData("sinCodigo")]
    [InlineData("exceso")]
    public async Task Los_traslados_invalidos_se_rechazan(string caso)
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var dto = Traslado(s, 300, "L-1-T");
        if (caso == "mismaGranja") dto.GranjaDestinoId = dto.GranjaOrigenId;
        if (caso == "sinCodigo") dto.Lotes[0].CodigoNuevoLote = null;
        if (caso == "exceso") dto.Lotes[0].Cantidad = 5000;

        await ((Func<Task>)(() => s.E.Kardex.RegistrarTrasladoLoteAsync(dto))).Should().ThrowAsync<ValidationException>();

        (await LoteAsync(s)).CantidadActual.Should().Be(1000);
    }

    // ---- Consultas -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Las_salidas_de_lotes_se_listan_por_programa_con_sus_cabezas_y_el_detalle_es_de_lotes()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var venta = await s.E.Kardex.RegistrarVentaLoteAsync(Venta(s));
        await s.E.Kardex.RegistrarTrasladoLoteAsync(Traslado(s, 100, "L-1-T"));
        var desde = new DateTime(2026, 10, 1);
        var hasta = new DateTime(2026, 11, 30);

        var aves = await s.Consultas.ListarSalidasAsync(new SalidaFiltro(null, desde, hasta, ProgramaId: s.Aves.Id));
        var bovinos = await s.Consultas.ListarSalidasAsync(new SalidaFiltro(null, desde, hasta, ProgramaId: s.E.Ids.ProgramaGanaderia));
        var porGranja = await s.Consultas.ListarSalidasAsync(new SalidaFiltro(s.E.Ids.Tesorito, desde, hasta, ProgramaId: s.Aves.Id));

        aves.Items.Select(i => (i.NumeroRemision, i.Cantidad)).Should().BeEquivalentTo(new[] { ("REM-L1", 400), ("TRA-L1", 100) });
        bovinos.Items.Should().BeEmpty();
        porGranja.Items.Should().BeEmpty("las salidas salieron de Montelindo");
        (await s.Consultas.EsDocumentoDeLotesAsync(venta.RemisionId)).Should().BeTrue();
        (await s.Consultas.ObtenerVentaLoteAsync(venta.RemisionId)).Lotes.Single().Codigo.Should().Be("L-1");
    }
}