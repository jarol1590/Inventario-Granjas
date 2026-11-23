using ClosedXML.Excel;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Comercial;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Reportes;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Reportes;

/// <summary>Informe de Ventas por Programas (Excel y PDF), remisión y recibo en PDF.</summary>
public class VentasPorProgramaTests
{
    private sealed record Escenario(EscenarioKardex E, int RemisionGanado, int RemisionHuevos, int ProgramaAves, int ProgramaGanaderia);

    private static ExcelReportingEngine Motor(EscenarioKardex e)
        => new(e.Db.Uow, new KardexReportBuilder(e.Db.Uow), new FichaService(e.Db.Uow, e.Reloj), e.Reloj);

    private static string Ruta(EscenarioKardex e, string nombre) => Path.Combine(e.Db.Carpeta, "informes", nombre);

    /// <summary>Dos ventas de ganado (una con 10 % de descuento), una de huevos, una venta anulada y un traslado.</summary>
    private static async Task<Escenario> CrearAsync()
    {
        var e = await EscenarioKardex.CrearAsync("2026-11-06");
        var catalogo = new CatalogoComercialService(e.Db.Uow);
        var oficios = new OficioService(e.Db.Uow, e.Reloj);
        var convenio = await catalogo.GuardarConvenioAsync(new Convenio { Nombre = "Convenio Frigocaldas", Nit = "900.1", Direccion = "x", Telefono = "1" });
        int aves;
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            aves = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!.Id;
        }

        await oficios.CrearAsync(new OficioDto { NumeroOficio = "OF-1", ProgramaId = e.Ids.ProgramaGanaderia, Fecha = Datos.F("2026-10-15") });
        await oficios.CrearAsync(new OficioDto { NumeroOficio = "OF-2", ProgramaId = aves, Fecha = Datos.F("2026-10-15") });
        var comercial = new DatosComercialesDto { TipoComprador = TipoComprador.Convenio, ConvenioId = convenio.Id };
        var pago = new ReciboCajaDto { Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.Consignacion, BancoConsignacion = "DAVIVIENDA" };

        foreach (var chapeta in new[] { "G-1", "G-2", "G-3" })
        {
            await e.AltaVacaAsync(chapeta, valor: 5_000_000m);
        }

        var v1 = await e.VentaAsync("REM-1", "OF-1", string.Empty, null, ("G-1", 6_000_000m));
        v1.FechaRemision = Datos.F("2026-11-04");
        v1.Comprador = convenio.Nombre;
        v1.ReciboCaja = string.Empty;
        v1.Comercial = comercial;
        v1.Recibo = pago;
        var r1 = await e.Kardex.RegistrarVentaAsync(v1);

        var v2 = await e.VentaAsync("REM-2", "OF-1", string.Empty, null, ("G-2", 4_000_000m));
        v2.FechaRemision = Datos.F("2026-10-28");
        v2.Comprador = convenio.Nombre;
        v2.ReciboCaja = string.Empty;
        v2.Comercial = comercial;
        v2.Recibo = new ReciboCajaDto { Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.Consignacion, BancoConsignacion = "DAVIVIENDA", DescuentoPorcentaje = 10m };
        await e.Kardex.RegistrarVentaAsync(v2);

        var anulada = await e.VentaAsync("REM-3", "OF-1", string.Empty, null, ("G-3", 3_000_000m));
        anulada.Comprador = convenio.Nombre;
        anulada.ReciboCaja = string.Empty;
        anulada.Comercial = comercial;
        anulada.Recibo = pago;
        await e.Kardex.AnularAsync((await e.Kardex.RegistrarVentaAsync(anulada)).RemisionId, "error");

        var producto = await catalogo.GuardarProductoAsync(new CatalogoProducto { Codigo = "HUE-30", ProgramaId = aves, Nombre = "Huevo cubeta x30", UnidadMedida = "Cubeta", PrecioUnitario = 18_000m });
        var huevos = await e.Kardex.RegistrarRemisionProductosAsync(new RemisionProductosDto
        {
            Tipo = TipoRemision.Venta, ProgramaId = aves, NumeroOficio = "OF-2", NumeroRemision = "REM-4", Fecha = Datos.F("2026-11-02"),
            GranjaOrigenId = e.Ids.Montelindo, Comprador = convenio.Nombre, Comercial = comercial, Recibo = pago,
            Lineas = [new LineaProductoDto { ProductoId = producto.Id, UnidadMedida = "Cubeta", Cantidad = 12.5m, ValorUnitario = 18_000m }],
        });

        await e.AltaVacaAsync("T-1", valor: 5_000_000m);
        var traslado = await e.TrasladoAsync("REM-T1", e.Ids.Tesorito, e.Ids.Montelindo, "T-1");
        traslado.Recibo = new ReciboCajaDto { Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.TrasladoInterno };
        await e.Kardex.RegistrarTrasladoAsync(traslado);

        await EscenariosDeReporte.ConfigurarFirmaAsync(e, EscenariosDeReporte.PngBase64);
        return new Escenario(e, r1.RemisionId, huevos.RemisionId, aves, e.Ids.ProgramaGanaderia);
    }

    private static bool TieneTexto(IXLWorksheet hoja, string texto)
        => hoja.CellsUsed().Any(c => c.GetString().Equals(texto, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task El_informe_de_ventas_agrupa_por_programa_con_subtotales_descuentos_total_y_firma()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        var ruta = await Motor(s.E).GenerarVentasPorProgramaAsync(new VentasReporteRequest(Datos.F("2026-10-01"), Datos.F("2026-11-30"), null, Ruta(s.E, "ventas.xlsx")));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("Ventas por programas");
        h.Cell("A2").GetString().Should().Be("INFORME DE VENTAS POR PROGRAMAS");
        h.Cell("A3").GetString().Should().Be("Del 01/10/2026 al 30/11/2026 · Programa: Todos los programas");
        string[] encabezados = ["Fecha", "N° Oficio", "N° Remisión", "Recibo SP", "Convenio / Comprador", "Descripción ítem", "Unidad", "Cantidad", "Valor unitario", "Valor total"];
        for (var i = 0; i < encabezados.Length; i++)
        {
            h.Cell(5, i + 1).GetString().Should().Be(encabezados[i]);
        }

        // Avicultura primero (orden alfabético): una línea de huevos.
        h.Cell("A6").GetString().Should().Be("AVICULTURA (AVES)");
        (h.Cell("C7").GetString(), h.Cell("B7").GetString(), h.Cell("F7").GetString(), h.Cell("H7").GetDouble(), h.Cell("J7").GetDouble())
            .Should().Be(("REM-4", "OF-2", "Huevo cubeta x30", 12.5, 225_000d));
        h.Cell("A8").GetString().Should().Be("Subtotal Avicultura (Aves)");
        h.Cell("J8").GetDouble().Should().Be(225_000);

        // Ganadería: REM-2 (28/10) y REM-1 (04/11), con 10 % de descuento en REM-2; la anulada y el traslado no salen.
        h.Cell("A9").GetString().Should().Be("GANADERÍA (BOVINOS)");
        h.Cell("C10").GetString().Should().Be("REM-2");
        h.Cell("C11").GetString().Should().Be("REM-1");
        h.Cell("J10").GetDouble().Should().Be(4_000_000);
        h.Cell("J11").GetDouble().Should().Be(6_000_000);
        h.Cell("A12").GetString().Should().Be("Subtotal Ganadería (Bovinos)");
        h.Cell("J12").FormulaA1.Should().Be("SUM(J10:J11)");
        h.Cell("J12").GetDouble().Should().Be(10_000_000);
        h.Cell("A13").GetString().Should().Be("Menos descuentos");
        h.Cell("J13").GetDouble().Should().Be(-400_000);
        h.Cell("A14").GetString().Should().Be("Total Ganadería (Bovinos)");
        h.Cell("J14").GetDouble().Should().Be(9_600_000);
        h.Cell("A15").GetString().Should().Be("TOTAL GENERAL RECAUDADO");
        h.Cell("J15").GetDouble().Should().Be(9_825_000);
        TieneTexto(h, "REM-3").Should().BeFalse("la venta anulada no cuenta");
        TieneTexto(h, "REM-T1").Should().BeFalse("el traslado no es una venta");
        TieneTexto(h, "PAULA VALENCIA ARANGO").Should().BeTrue("lleva el bloque de firma institucional");
    }

    [Fact]
    public async Task El_informe_de_ventas_filtra_por_rango_de_fechas_y_por_programa()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        var soloAves = await Motor(s.E).GenerarVentasPorProgramaAsync(new VentasReporteRequest(Datos.F("2026-10-01"), Datos.F("2026-11-30"), s.ProgramaAves, Ruta(s.E, "aves.xlsx")));
        var soloOctubre = await Motor(s.E).GenerarVentasPorProgramaAsync(new VentasReporteRequest(Datos.F("2026-10-01"), Datos.F("2026-10-31"), null, Ruta(s.E, "octubre.xlsx")));
        var vacio = await Motor(s.E).GenerarVentasPorProgramaAsync(new VentasReporteRequest(Datos.F("2026-01-01"), Datos.F("2026-01-31"), null, Ruta(s.E, "enero.xlsx")));

        using (var libro = new XLWorkbook(soloAves))
        {
            var h = libro.Worksheets.Single();
            h.Cell("A3").GetString().Should().EndWith("Programa: Avicultura (Aves)");
            TieneTexto(h, "GANADERÍA (BOVINOS)").Should().BeFalse();
            h.Cell("J9").GetDouble().Should().Be(225_000, "el total general es solo el de avicultura");
        }

        using (var libro = new XLWorkbook(soloOctubre))
        {
            var h = libro.Worksheets.Single();
            TieneTexto(h, "REM-2").Should().BeTrue();
            TieneTexto(h, "REM-1").Should().BeFalse("REM-1 es de noviembre");
            TieneTexto(h, "REM-4").Should().BeFalse("REM-4 es de noviembre");
        }

        using (var libro = new XLWorkbook(vacio))
        {
            TieneTexto(libro.Worksheets.Single(), "Sin ventas en el rango seleccionado.").Should().BeTrue();
        }
    }

    [Fact]
    public async Task El_informe_de_ventas_rechaza_un_rango_invertido_o_un_programa_inexistente()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        var invertido = () => Motor(s.E).GenerarVentasPorProgramaAsync(new VentasReporteRequest(Datos.F("2026-11-30"), Datos.F("2026-11-01"), null, Ruta(s.E, "x.xlsx")));
        var inexistente = () => Motor(s.E).GenerarVentasPorProgramaAsync(new VentasReporteRequest(Datos.F("2026-10-01"), Datos.F("2026-11-30"), 9999, Ruta(s.E, "y.xlsx")));

        await invertido.Should().ThrowAsync<System.ComponentModel.DataAnnotations.ValidationException>().WithMessage("*fecha inicial*");
        await inexistente.Should().ThrowAsync<System.ComponentModel.DataAnnotations.ValidationException>().WithMessage("*no existe*");
    }

    // ---- PDF ---------------------------------------------------------------------------------------------------------------

    private static PdfDocumentService Pdf(EscenarioKardex e) => new(e.Db.Uow, e.Reloj);

    private static async Task ExigirPdfValidoAsync(string ruta)
    {
        File.Exists(ruta).Should().BeTrue();
        var bytes = await File.ReadAllBytesAsync(ruta);
        bytes.Length.Should().BeGreaterThan(1_500);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        File.Exists(ruta + ".tmp").Should().BeFalse("no queda el temporal");
    }

    [Fact]
    public async Task El_informe_de_ventas_tambien_sale_en_PDF()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        var ruta = await Pdf(s.E).GenerarVentasPorProgramaAsync(new VentasReporteRequest(Datos.F("2026-10-01"), Datos.F("2026-11-30"), null, Ruta(s.E, "ventas.pdf")));
        var vacio = await Pdf(s.E).GenerarVentasPorProgramaAsync(new VentasReporteRequest(Datos.F("2026-01-01"), Datos.F("2026-01-31"), null, Ruta(s.E, "vacio.pdf")));

        await ExigirPdfValidoAsync(ruta);
        await ExigirPdfValidoAsync(vacio);
    }

    [Fact]
    public async Task La_remision_y_el_recibo_se_imprimen_en_PDF_de_ventas_traslados_y_productos()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var traslado = (await new RemisionesQueryService(s.E.Db.Uow).ListarAsync(new RemisionFiltro(Tipo: TipoRemision.Traslado))).Single();

        foreach (var (id, nombre) in new[] { (s.RemisionGanado, "ganado"), (s.RemisionHuevos, "huevos"), (traslado.Id, "traslado") })
        {
            await ExigirPdfValidoAsync(await Pdf(s.E).GenerarRemisionAsync(id, Ruta(s.E, $"remision-{nombre}.pdf")));
            await ExigirPdfValidoAsync(await Pdf(s.E).GenerarReciboAsync(id, Ruta(s.E, $"recibo-{nombre}.pdf")));
        }
    }

    [Fact]
    public async Task No_se_imprime_el_recibo_de_una_remision_que_no_lo_tiene()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        await s.E.AltaVacaAsync("L-1", valor: 5_000_000m);
        var legado = await s.E.Kardex.RegistrarVentaAsync(await s.E.VentaAsync("REM-OLD", "OF-OLD", "RC-1", null, ("L-1", 5_100_000m)));
        await s.E.Db.EjecutarAsync("DELETE FROM RECIBO_CAJA");   // un documento al que, por algún motivo, le falta el recibo

        var act = () => Pdf(s.E).GenerarReciboAsync(legado.RemisionId, Ruta(s.E, "sin-recibo.pdf"));

        await act.Should().ThrowAsync<System.ComponentModel.DataAnnotations.ValidationException>().WithMessage("*todavía no tiene recibo*");
    }
}
