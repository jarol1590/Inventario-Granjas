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

/// <summary>Informe de Ventas por Programas (Excel), remisión y recibo (Excel).</summary>
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
        var pago = new ReciboCajaDto { Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.Consignacion, BancoConsignacion = "DAVIVIENDA", NumeroCuenta = "123456", TipoCuenta = "Ahorros" };

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
        v2.Recibo = new ReciboCajaDto { Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.Consignacion, BancoConsignacion = "DAVIVIENDA", NumeroCuenta = "123456", TipoCuenta = "Ahorros", DescuentoPorcentaje = 10m };
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

        var leche = await catalogo.GuardarProductoAsync(new CatalogoProducto { Codigo = "LEC-L", ProgramaId = e.Ids.ProgramaGanaderia, Nombre = "Leche", UnidadMedida = "Litro", PrecioUnitario = 2_000m });
        await e.Kardex.RegistrarRemisionProductosAsync(new RemisionProductosDto
        {
            Tipo = TipoRemision.Venta, ProgramaId = e.Ids.ProgramaGanaderia, NumeroOficio = "OF-1", NumeroRemision = "REM-5", Fecha = Datos.F("2026-10-28"),
            GranjaOrigenId = e.Ids.Tesorito, Comprador = convenio.Nombre, Comercial = comercial, Recibo = pago,
            Lineas = [new LineaProductoDto { ProductoId = leche.Id, UnidadMedida = "Litro", Cantidad = 100m, ValorUnitario = 2_000m }],
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
        libro.Worksheets.Select(w => w.Name).Should().Equal("Avicultura (Aves)", "Ganadería (Bovinos)", "Consolidado");
        string[] encabezados = ["Oficio", "Remisión", "Recibo de caja", "Cliente", "Año", "Mes", "Producto", "Cantidad (unidad)", "Precio Unitario", "Total"];

        var aves = libro.Worksheet("Avicultura (Aves)");
        aves.Cell("A2").GetString().Should().Be("INFORME DE VENTAS - AVICULTURA (AVES)");
        for (var i = 0; i < encabezados.Length; i++)
        {
            aves.Cell(5, i + 1).GetString().Should().Be(encabezados[i]);
        }

        (aves.Cell("A6").GetString(), aves.Cell("B6").GetString(), aves.Cell("D6").GetString(), (int)aves.Cell("E6").GetDouble(), aves.Cell("F6").GetString(),
                aves.Cell("G6").GetString(), aves.Cell("H6").GetDouble(), aves.Cell("I6").GetDouble(), aves.Cell("J6").GetDouble())
            .Should().Be(("OF-2", "REM-4", "Convenio Frigocaldas", 2026, "Noviembre", "Huevo cubeta x30", 12.5, 18_000d, 225_000d));
        aves.Cell("C6").GetString().Should().EndWith(" SP", "lleva el número de su recibo de caja");
        aves.Cell("A7").GetString().Should().Be("Subtotal");
        aves.Cell("J7").FormulaA1.Should().Be("SUM(J6:J6)");
        aves.Cell("J7").GetDouble().Should().Be(225_000);

        // Ganadería: REM-2 y REM-5 (28/10) y REM-1 (04/11); la anulada y el traslado no salen.
        var ganado = libro.Worksheet("Ganadería (Bovinos)");
        ganado.Cell("A2").GetString().Should().Be("INFORME DE VENTAS - GANADERÍA (BOVINOS)");
        (ganado.Cell("A6").GetString(), ganado.Cell("B6").GetString(), ganado.Cell("H6").GetDouble(), ganado.Cell("J6").GetDouble()).Should().Be(("OF-1", "REM-2", 1d, 4_000_000d));
        (ganado.Cell("B7").GetString(), ganado.Cell("F7").GetString(), ganado.Cell("G7").GetString(), ganado.Cell("J7").GetDouble())
            .Should().Be(("REM-5", "Octubre", "Leche", 200_000d));
        (ganado.Cell("B8").GetString(), ganado.Cell("F8").GetString(), ganado.Cell("J8").GetDouble()).Should().Be(("REM-1", "Noviembre", 6_000_000d));
        ganado.Cell("J9").GetDouble().Should().Be(10_200_000);

        // Consolidado (última hoja): programas y oficios agrupados, subtotal por programa y total general.
        var consolidado = libro.Worksheet("Consolidado");
        consolidado.Cell("A2").GetString().Should().Be("INFORME DE VENTAS - CONSOLIDADO DE PROGRAMAS");
        consolidado.Cell("A6").GetString().Should().Be("PROGRAMA: AVICULTURA (AVES)");
        (consolidado.Cell("B7").GetString(), consolidado.Cell("A8").GetString(), consolidado.Cell("J8").GetDouble()).Should().Be(("REM-4", "Subtotal oficio OF-2", 225_000d));
        (consolidado.Cell("A9").GetString(), consolidado.Cell("J9").GetDouble()).Should().Be(("Subtotal Avicultura (Aves)", 225_000d));
        consolidado.Cell("A10").GetString().Should().Be("PROGRAMA: GANADERÍA (BOVINOS)");
        (consolidado.Cell("A14").GetString(), consolidado.Cell("J14").GetDouble()).Should().Be(("Subtotal oficio OF-1", 10_200_000d));
        (consolidado.Cell("A15").GetString(), consolidado.Cell("J15").GetDouble()).Should().Be(("Subtotal Ganadería (Bovinos)", 10_200_000d));
        (consolidado.Cell("A16").GetString(), consolidado.Cell("J16").GetDouble()).Should().Be(("TOTAL GENERAL", 10_425_000d));
        foreach (var hoja in libro.Worksheets)
        {
            TieneTexto(hoja, "REM-3").Should().BeFalse("la venta anulada no cuenta");
            TieneTexto(hoja, "REM-T1").Should().BeFalse("el traslado no es una venta");
            TieneTexto(hoja, "PAULA VALENCIA ARANGO").Should().BeTrue("lleva el bloque de firma institucional");
        }
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
            var h = libro.Worksheets.First();
            h.Name.Should().Be("Avicultura (Aves)");
            h.Cell("J7").GetDouble().Should().Be(225_000, "el total es solo el de avicultura");
            libro.Worksheets.Last().Name.Should().Be("Consolidado");
            libro.Worksheet("Consolidado").Cell("J10").GetDouble().Should().Be(225_000);
        }

        using (var libro = new XLWorkbook(soloOctubre))
        {
            var h = libro.Worksheets.First();
            h.Name.Should().Be("Ganadería (Bovinos)");
            TieneTexto(h, "REM-5").Should().BeTrue();
            TieneTexto(h, "REM-4").Should().BeFalse("REM-4 es de noviembre");
        }

        using (var libro = new XLWorkbook(vacio))
        {
            libro.Worksheets.Select(w => w.Name).Should().Equal("Ventas", "Consolidado");
            libro.Worksheets.All(w => TieneTexto(w, "Sin ventas en el rango seleccionado.")).Should().BeTrue();
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

    // ---- Remisión y recibo en Excel --------------------------------------------------------------------------------------------

    [Fact]
    public async Task La_remision_y_el_recibo_se_generan_en_Excel_de_ventas_traslados_y_productos()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var traslado = (await new RemisionesQueryService(s.E.Db.Uow).ListarAsync(new RemisionFiltro(Tipo: TipoRemision.Traslado))).Single();

        foreach (var (id, nombre) in new[] { (s.RemisionGanado, "ganado"), (s.RemisionHuevos, "huevos"), (traslado.Id, "traslado") })
        {
            var remision = await Motor(s.E).GenerarRemisionAsync(id, Ruta(s.E, $"remision-{nombre}.xlsx"));
            var recibo = await Motor(s.E).GenerarReciboAsync(id, Ruta(s.E, $"recibo-{nombre}.xlsx"));

            using var libroRemision = new XLWorkbook(remision);
            libroRemision.Worksheets.Single().Cell("A2").GetString().Should().StartWith("REMISIÓN N° REM-");
            using var libroRecibo = new XLWorkbook(recibo);
            libroRecibo.Worksheets.Single().Cell("A2").GetString().Should().StartWith("RECIBO DE CAJA No.");
            File.Exists(remision + ".tmp").Should().BeFalse("no queda el temporal");
        }
    }

    [Fact]
    public async Task La_remision_y_el_recibo_en_Excel_traen_totales_forma_de_pago_y_firma()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        using var remision = new XLWorkbook(await Motor(s.E).GenerarRemisionAsync(s.RemisionHuevos, Ruta(s.E, "huevos-remision.xlsx")));
        using var recibo = new XLWorkbook(await Motor(s.E).GenerarReciboAsync(s.RemisionHuevos, Ruta(s.E, "huevos-recibo.xlsx")));

        var hr = remision.Worksheets.Single();
        TieneTexto(hr, "TOTAL REMISIÓN").Should().BeTrue();
        hr.CellsUsed().Any(c => c.GetString() == "Huevo cubeta x30").Should().BeTrue();
        TieneTexto(hr, "PAULA VALENCIA ARANGO").Should().BeTrue("lleva el bloque de firma institucional");

        var hc = recibo.Worksheets.Single();
        TieneTexto(hc, "TOTAL RECIBO").Should().BeTrue();
        TieneTexto(hc, "Forma de pago").Should().BeTrue();
        hc.CellsUsed().Any(c => c.GetString().StartsWith("Consignación a la cuenta Ahorros No. 123456 de DAVIVIENDA")).Should().BeTrue();
    }

    [Fact]
    public async Task No_se_imprime_el_recibo_de_una_remision_que_no_lo_tiene()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        await s.E.AltaVacaAsync("L-1", valor: 5_000_000m);
        var legado = await s.E.Kardex.RegistrarVentaAsync(await s.E.VentaAsync("REM-OLD", "OF-OLD", "RC-1", null, ("L-1", 5_100_000m)));
        await s.E.Db.EjecutarAsync("DELETE FROM RECIBO_CAJA");   // un documento al que, por algún motivo, le falta el recibo

        var act = () => Motor(s.E).GenerarReciboAsync(legado.RemisionId, Ruta(s.E, "sin-recibo.xlsx"));

        await act.Should().ThrowAsync<System.ComponentModel.DataAnnotations.ValidationException>().WithMessage("*todavía no tiene recibo*");
    }
}
