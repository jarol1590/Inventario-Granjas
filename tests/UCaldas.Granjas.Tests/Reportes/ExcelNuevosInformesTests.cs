using ClosedXML.Excel;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Services.Reportes;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Reportes;

/// <summary>Observaciones en el inventario físico-financiero, inventario físico individual y Kardex mensual de lotes.</summary>
public class ExcelNuevosInformesTests
{
    private static ExcelReportingEngine Motor(EscenarioKardex e)
        => new(e.Db.Uow, new KardexReportBuilder(e.Db.Uow), new FichaService(e.Db.Uow, e.Reloj), e.Reloj);

    private static string Ruta(EscenarioKardex e, string nombre) => Path.Combine(e.Db.Carpeta, "informes", nombre);

    private static bool TieneTexto(IXLWorksheet hoja, string texto)
        => hoja.CellsUsed().Any(c => c.GetString().Equals(texto, StringComparison.OrdinalIgnoreCase));

    private static async Task<EscenarioKardex> CrearInventarioAsync()
    {
        var e = await EscenarioKardex.CrearAsync("2026-11-06");
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 4_000_000m, orientacion: NombresBase.OrientacionCarne);
        await e.AltaTerneraAsync("T-1", valor: 800_000m);
        await e.AltaVacaAsync("S-1", valor: 5_000_000m);
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("S-1", 5_500_000m)));
        await e.Db.EjecutarAsync("UPDATE ANIMAL SET observaciones = 'Revisar pezuña' WHERE chapeta_amarilla = 'A-1'");
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, EscenariosDeReporte.PngBase64);
        return e;
    }

    // ---- Físico-financiero: columna I ----------------------------------------------------------------------------------

    [Fact]
    public async Task El_inventario_fisico_financiero_agrega_la_columna_Observaciones_y_amplia_los_titulos_hasta_la_I()
    {
        await using var e = await CrearInventarioAsync();

        var ruta = await Motor(e).GenerarInventarioFisicoAsync(
            new InventarioFisicoRequest(e.Ids.Tesorito, null, null, EstadoAnimal.Disponible, Ruta(e, "fisico.xlsx")));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("Tesorito");
        h.Cell("H5").GetString().Should().Be("Valorización acumulada");
        h.Cell("I5").GetString().Should().Be("Observaciones");
        h.Cell("A2").MergedRange().RangeAddress.ToString().Should().Be("A2:I2");
        var fila = h.CellsUsed().Single(c => c.Address.ColumnNumber == 4 && c.GetString() == "A-1").Address.RowNumber;
        h.Cell(fila, 9).GetString().Should().Be("Revisar pezuña");
        var sinObservacion = h.CellsUsed().Single(c => c.Address.ColumnNumber == 4 && c.GetString() == "A-2").Address.RowNumber;
        h.Cell(sinObservacion, 9).GetString().Should().BeEmpty();
    }

    // ---- Inventario físico individual ----------------------------------------------------------------------------------

    [Fact]
    public async Task El_inventario_individual_trae_las_seis_columnas_solo_de_animales_disponibles_y_la_firma()
    {
        await using var e = await CrearInventarioAsync();

        var ruta = await Motor(e).GenerarInventarioIndividualAsync(new InventarioIndividualRequest(e.Ids.Tesorito, null, Ruta(e, "individual.xlsx")));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("Tesorito");
        h.Cell("A2").GetString().Should().Be("INVENTARIO FISICO INDIVIDUAL POR ANIMAL");
        string[] encabezados = ["CLASE DE ENTRADA", "FECHA NACIMIENTO", "SEXO", "CHAPETA AMARILLA", "PESO", "OBSERVACIONES"];
        for (var i = 0; i < encabezados.Length; i++)
        {
            h.Cell(5, i + 1).GetString().Should().Be(encabezados[i]);
        }

        // Disponibles ordenados por chapeta: A-1, A-2, T-1 (S-1 está vendida).
        h.Cell("D6").GetString().Should().Be("A-1");
        h.Cell("D7").GetString().Should().Be("A-2");
        h.Cell("D8").GetString().Should().Be("T-1");
        h.Cell("D9").GetString().Should().BeEmpty("la vaca vendida no aparece");
        var a1 = await e.AnimalAsync("A-1");
        h.Cell("A6").GetString().Should().Be(a1.ClaseEntrada);
        h.Cell("B6").GetDateTime().Should().Be(a1.FechaNacimiento.Date);
        h.Cell("B6").Style.NumberFormat.Format.Should().Be("dd/MM/yyyy");
        h.Cell("C6").GetString().Should().Be(a1.Sexo);
        h.Cell("D6").Style.Font.Bold.Should().BeTrue();
        h.Cell("E6").GetDouble().Should().Be((double)a1.PesoActualKg);
        h.Cell("E6").Style.NumberFormat.Format.Should().Be("#,##0.0");
        h.Cell("F6").GetString().Should().Be("Revisar pezuña");
        h.Cell("F7").GetString().Should().BeEmpty();
        TieneTexto(h, "PAULA VALENCIA ARANGO").Should().BeTrue("lleva el bloque de firma institucional");
    }

    [Fact]
    public async Task El_inventario_individual_sin_filtro_de_granja_trae_una_hoja_por_granja_activa()
    {
        await using var e = await CrearInventarioAsync();

        var ruta = await Motor(e).GenerarInventarioIndividualAsync(new InventarioIndividualRequest(null, null, Ruta(e, "individual-todas.xlsx")));

        using var libro = new XLWorkbook(ruta);
        libro.Worksheets.Select(w => w.Name).Should().Contain("Tesorito").And.Contain("La Cruz").And.Contain("Montelindo");
        TieneTexto(libro.Worksheet("La Cruz"), "Sin semovientes disponibles para los filtros seleccionados.").Should().BeTrue();
    }

    // ---- Kardex mensual de lotes ---------------------------------------------------------------------------------------

    private sealed record EntornoLotes(EscenarioKardex E, LoteService Lotes, Programa Aves, int LoteId);

    /// <summary>Lote L-1 de 1.000 aves a $15.000 (ingreso 25/10/2026), con una baja el 04/11 y una venta de 400 el 05/11.</summary>
    private static async Task<EntornoLotes> CrearLotesAsync()
    {
        var e = await EscenarioKardex.CrearAsync("2026-11-06");
        var lotes = new LoteService(e.Db.Uow, e.Reloj);
        Programa aves;
        int etapa;
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            aves = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!;
            etapa = (await uow.Categorias.GetByNombreAsync(aves.Id, NombresBase.CategoriaGeneral))!.Id;
        }

        var id = await lotes.RegistrarNuevoLoteAsync(new NuevoLoteDto
        {
            ProgramaId = aves.Id, GranjaId = e.Ids.Montelindo, EtapaId = etapa, ClaseEntrada = ClaseEntrada.Compra, CodigoLote = "1",
            LineaRaza = "LBROWN", FechaIngreso = Datos.F("2026-10-25"), Cantidad = 1000, PrecioUnitarioFactura = 15_000m,
            Factura = "FAC-1", Proveedor = "Incubadora",
        });
        await lotes.RegistrarBajaAsync(id, 58, "Mortalidad", Datos.F("2026-11-04"), "calor");
        await e.Kardex.RegistrarVentaLoteAsync(new VentaLoteDto
        {
            NumeroOficio = "OF-1", FechaOficio = Datos.F("2026-11-05"), ProgramaId = aves.Id, NumeroRemision = "REM-L1",
            FechaRemision = Datos.F("2026-11-05"), ReciboCaja = "RC-1", GranjaOrigenId = e.Ids.Montelindo, Comprador = "Frigorífico",
            Lotes = [new VentaLoteLineaDto { LoteId = id, Cantidad = 400, PesoKg = 900m, ValorVenta = 20_000_000m }],
        });
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, EscenariosDeReporte.PngBase64);
        return new EntornoLotes(e, lotes, aves, id);
    }

    [Fact]
    public async Task El_kardex_de_aves_arma_el_bloque_del_lote_con_sus_13_columnas_y_formulas()
    {
        var s = await CrearLotesAsync();
        await using var _ = s.E;
        // Valorización fechada dentro del mes (+$1.000.000 por cambio de etapa).
        await s.E.Db.EjecutarAsync(
            $"INSERT INTO MOVIMIENTO_INVENTARIO (lote_id, tipo_movimiento, fecha_movimiento, granja_destino_id, costo_asociado, cantidad, observaciones) " +
            $"VALUES ({s.LoteId}, 'Valorizacion', '2026-11-03', {s.E.Ids.Montelindo}, 1000000, 1000, 'Valorización por cambio de etapa: General -> Postura')");
        await s.E.Db.EjecutarAsync($"UPDATE LOTE_POBLACION SET valor_total_contable = valor_total_contable + 1000000 WHERE id = {s.LoteId}");

        var ruta = await Motor(s.E).GenerarKardexLotesAsync(new KardexLotesRequest(2026, 11, null, s.Aves.Id, Ruta(s.E, "aves.xlsx")));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheets.Single();
        h.Cell("A1").GetString().Should().Be("MOVIMIENTOS INVENTARIO AVES");
        h.Cell("A2").GetString().Should().Be("NOVIEMBRE 2026");
        h.Cell("A4").GetString().Should().Be("COMPRADAS OCTUBRE 25 DE 2026");
        h.Cell("A5").GetString().Should().Be("LBROWN");
        h.Cell("A6").GetString().Should().Be("LOTE # 1");
        h.Cell("A7").GetString().Should().Be("VIENEN");
        h.Cell("C7").GetString().Should().Be("VENTAS — COSTO EN LIBROS");
        h.Cell("F7").GetString().Should().Be("VENTAS — VALOR COMERCIAL");
        h.Cell("I7").GetString().Should().Be("MUERTES / MORTALIDAD");
        h.Cell("K7").GetString().Should().Be("VALORIZACIÓN");
        h.Cell("L7").GetString().Should().Be("SALDO FINAL");
        string[] sub = ["Cant.", "Valor", "Cant.", "Costo", "Vr", "Cant.", "Valor Venta", "Vr.Venta", "No.", "Valor", "Etapas", "Cant.", "Valor"];
        for (var i = 0; i < sub.Length; i++)
        {
            h.Cell(8, i + 1).GetString().Should().Be(sub[i]);
        }

        // Vienen (cierre de octubre): 1.000 cabezas a $15.000 = $15.000.000 (el lote entró en octubre).
        (h.Cell("A9").GetDouble(), h.Cell("B9").GetDouble()).Should().Be((1000d, 15_000_000d), "el cierre de octubre: lo comprado, antes de la valorización de noviembre");
        // Venta: 400 a costo de libros $15.000 y a $50.000 comerciales.
        (h.Cell("C9").GetDouble(), h.Cell("D9").GetDouble(), h.Cell("E9").GetDouble()).Should().Be((400d, 15_000d, 6_000_000d));
        (h.Cell("F9").GetDouble(), h.Cell("G9").GetDouble(), h.Cell("H9").GetDouble()).Should().Be((400d, 50_000d, 20_000_000d));
        h.Cell("E9").FormulaA1.Should().Be("C9*D9");
        h.Cell("H9").FormulaA1.Should().Be("F9*G9");
        // Mortalidad: 58 × $15.000 y valorización del mes.
        (h.Cell("I9").GetDouble(), h.Cell("J9").GetDouble(), h.Cell("K9").GetDouble()).Should().Be((58d, 870_000d, 1_000_000d));
        // Saldo final con las fórmulas del documento.
        h.Cell("L9").FormulaA1.Should().Be("A9-C9-I9");
        h.Cell("M9").FormulaA1.Should().Be("B9-E9-J9+K9");
        (h.Cell("L9").GetDouble(), h.Cell("M9").GetDouble()).Should().Be((542d, 9_130_000d));

        // El saldo final coincide con el estado real del lote.
        var lote = (await s.Lotes.ObtenerAsync(s.LoteId)).Lote;
        (lote.CantidadActual, lote.ValorTotalContable).Should().Be((542, 9_130_000m));

        // Subtotal del lote y total general.
        h.Cell("A10").FormulaA1.Should().Be("A9");
        h.Cell("L10").FormulaA1.Should().Be("A10-C10-I10");
        (h.Cell("L10").GetDouble(), h.Cell("M10").GetDouble()).Should().Be((542d, 9_130_000d));
        h.Cell("A12").GetString().Should().Be("TOTAL GENERAL INVENTARIO AVES");
        h.Cell("L12").FormulaA1.Should().Be("L10");
        (h.Cell("L12").GetDouble(), h.Cell("M12").GetDouble()).Should().Be((542d, 9_130_000d));
        TieneTexto(h, "PAULA VALENCIA ARANGO").Should().BeTrue("lleva el bloque de firma institucional");
    }

    [Fact]
    public async Task El_kardex_de_aves_de_un_mes_posterior_a_los_movimientos_parte_del_saldo_actual()
    {
        var s = await CrearLotesAsync();
        await using var _ = s.E;

        var ruta = await Motor(s.E).GenerarKardexLotesAsync(new KardexLotesRequest(2026, 12, null, s.Aves.Id, Ruta(s.E, "aves-dic.xlsx")));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheets.Single();
        h.Cell("A2").GetString().Should().Be("DICIEMBRE 2026");
        (h.Cell("A9").GetDouble(), h.Cell("B9").GetDouble()).Should().Be((542d, 8_130_000d), "viene el saldo con que cerró noviembre");
        (h.Cell("C9").GetString(), h.Cell("I9").GetDouble(), h.Cell("K9").GetDouble()).Should().Be(("", 0d, 0d), "no hay movimientos en diciembre");
        (h.Cell("L9").GetDouble(), h.Cell("M9").GetDouble()).Should().Be((542d, 8_130_000d));
    }

    [Fact]
    public async Task El_kardex_de_aves_de_un_mes_anterior_al_ingreso_no_trae_lotes()
    {
        var s = await CrearLotesAsync();
        await using var _ = s.E;

        var ruta = await Motor(s.E).GenerarKardexLotesAsync(new KardexLotesRequest(2026, 9, null, s.Aves.Id, Ruta(s.E, "aves-sep.xlsx")));

        using var libro = new XLWorkbook(ruta);
        TieneTexto(libro.Worksheets.Single(), "Sin lotes con saldo ni movimientos en el mes.").Should().BeTrue();
    }

    [Fact]
    public async Task El_kardex_de_aves_filtra_por_granja()
    {
        var s = await CrearLotesAsync();
        await using var _ = s.E;

        var ruta = await Motor(s.E).GenerarKardexLotesAsync(new KardexLotesRequest(2026, 11, s.E.Ids.Tesorito, s.Aves.Id, Ruta(s.E, "aves-tesorito.xlsx")));

        using var libro = new XLWorkbook(ruta);
        TieneTexto(libro.Worksheets.Single(), "Sin lotes con saldo ni movimientos en el mes.").Should().BeTrue("el lote está en Montelindo");
    }

    [Fact]
    public async Task Los_asientos_de_lotes_no_entran_al_kardex_financiero_de_bovinos()
    {
        var s = await CrearLotesAsync();
        await using var _ = s.E;

        var granjas = await new KardexReportBuilder(s.E.Db.Uow).ConstruirAsync(2026, 11, s.E.Ids.Montelindo);

        var k = granjas.Single();
        (k.Muertes.Count, k.Valorizacion).Should().Be((0, 0m), "la baja del lote va en el Kardex de lotes");
    }
}
