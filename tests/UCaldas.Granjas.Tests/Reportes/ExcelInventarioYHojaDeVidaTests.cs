using ClosedXML.Excel;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Reportes;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Reportes;

public class ExcelInventarioYHojaDeVidaTests
{
    private static ExcelReportingEngine Motor(EscenarioKardex e)
        => new(e.Db.Uow, new KardexReportBuilder(e.Db.Uow), new FichaService(e.Db.Uow, e.Reloj), e.Reloj);

    private static string Ruta(EscenarioKardex e, string nombre) => Path.Combine(e.Db.Carpeta, "informes", nombre);

    /// <summary>Tesorito con 2 terneros... no: 1 ternera, 2 vacas de nacimiento, 1 vaca comprada y 1 vaca vendida.</summary>
    private static async Task<EscenarioKardex> CrearInventarioAsync()
    {
        var e = await EscenarioKardex.CrearAsync("2026-11-06");
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 4_000_000m, orientacion: NombresBase.OrientacionCarne);
        await e.AltaTerneraAsync("T-1", valor: 800_000m);
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("P-1", Sexo.Hembra, "2022-03-01", 3_200_000m, partos: 1)));
        await e.AltaVacaAsync("S-1", valor: 5_000_000m);
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("S-1", 5_500_000m)));
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, EscenariosDeReporte.PngBase64);
        return e;
    }

    [Fact]
    public async Task El_inventario_agrupa_por_categoria_con_subtotales_y_total_de_la_sede()
    {
        await using var e = await CrearInventarioAsync();

        var ruta = await Motor(e).GenerarInventarioFisicoAsync(
            new InventarioFisicoRequest(e.Ids.Tesorito, null, null, EstadoAnimal.Disponible, Ruta(e, "inventario.xlsx")));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("Tesorito");
        h.Cell("A2").GetString().Should().Contain("GRANJA TESORITO");
        h.Cell("A3").GetString().Should().Be("Estado: Disponible · Corte al 06/11/2026");

        string[] encabezados =
        [
            "Clase de entrada", "Fecha de nacimiento", "Sexo", "Chapeta amarilla", "Chapeta roja",
            "Precio animal (costo de compra)", "Precio sistema (categoría actual)", "Valorización acumulada",
        ];
        for (var i = 0; i < encabezados.Length; i++)
        {
            h.Cell(5, i + 1).GetString().Should().Be(encabezados[i]);
        }

        // Terneros: una ternera.
        h.Cell("A6").GetString().Should().Be("TERNEROS");
        h.Cell("D7").GetString().Should().Be("T-1");
        h.Cell("A8").GetString().Should().Be("Subtotal Terneros");
        h.Cell("B8").GetDouble().Should().Be(1);
        h.Cell("G8").GetDouble().Should().Be(800_000);

        // Vacas General: A-1, A-2 y la comprada P-1, en orden de chapeta.
        h.Cell("A9").GetString().Should().Be("VACAS GENERAL");
        h.Cell("D10").GetString().Should().Be("A-1");
        h.Cell("D11").GetString().Should().Be("A-2");
        h.Cell("D12").GetString().Should().Be("P-1");
        h.Cell("A12").GetString().Should().Be("COMPRA");
        h.Cell("F12").GetDouble().Should().Be(3_200_000, "el precio de factura (costo de origen)");
        h.Cell("G12").GetDouble().Should().Be(6_000_000, "el precio del sistema sale de la matriz");
        h.Cell("A13").GetString().Should().Be("Subtotal Vacas General");
        h.Cell("B13").GetDouble().Should().Be(3);
        h.Cell("F13").GetDouble().Should().Be(3_200_000);
        h.Cell("G13").GetDouble().Should().Be(16_000_000);

        // Total de la sede.
        h.Cell("A14").GetString().Should().Be("TOTAL SEDE TESORITO");
        h.Cell("B14").GetDouble().Should().Be(4);
        h.Cell("B14").FormulaA1.Should().Be("B8+B13");
        h.Cell("G14").GetDouble().Should().Be(16_800_000);
        h.Cell("B14").Style.NumberFormat.Format.Should().Contain("semovientes");
        h.Cell("G12").Style.NumberFormat.Format.Should().Contain("$");
        h.Cell("B7").Style.NumberFormat.Format.Should().Be("dd/MM/yyyy");

        h.Cell("A5").Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#1B365D"));
        h.Pictures.Count.Should().Be(1);
        h.CellsUsed().Select(c => c.GetString()).Should().Contain("PAULA VALENCIA ARANGO");
    }

    [Fact]
    public async Task El_filtro_de_estado_incluye_los_vendidos_solo_si_se_pide()
    {
        await using var e = await CrearInventarioAsync();

        var disponibles = await Motor(e).GenerarInventarioFisicoAsync(
            new InventarioFisicoRequest(e.Ids.Tesorito, null, null, EstadoAnimal.Disponible, Ruta(e, "a.xlsx")));
        var vendidos = await Motor(e).GenerarInventarioFisicoAsync(
            new InventarioFisicoRequest(e.Ids.Tesorito, null, null, EstadoAnimal.Vendido, Ruta(e, "b.xlsx")));
        var todos = await Motor(e).GenerarInventarioFisicoAsync(
            new InventarioFisicoRequest(e.Ids.Tesorito, null, null, null, Ruta(e, "c.xlsx")));

        using (var libro = new XLWorkbook(disponibles))
        {
            libro.Worksheet("Tesorito").CellsUsed().Select(c => c.GetString()).Should().NotContain("S-1");
        }

        using (var libro = new XLWorkbook(vendidos))
        {
            var h = libro.Worksheet("Tesorito");
            h.CellsUsed().Select(c => c.GetString()).Should().Contain("S-1").And.NotContain("A-1");
            h.Cell("A3").GetString().Should().Contain("Estado: Vendido");
        }

        using (var libro = new XLWorkbook(todos))
        {
            var h = libro.Worksheet("Tesorito");
            h.CellsUsed().Select(c => c.GetString()).Should().Contain("S-1").And.Contain("A-1");
            h.Cell("A3").GetString().Should().Contain("Estado: todos");
            h.CellsUsed().Single(c => c.GetString().StartsWith("TOTAL SEDE")).WorksheetRow().Cell(2).GetDouble().Should().Be(5);
        }
    }

    [Fact]
    public async Task El_filtro_de_categoria_y_de_programa_reducen_el_inventario()
    {
        await using var e = await CrearInventarioAsync();
        var aves = await e.Db.EscalarAsync<int>("SELECT id FROM PROGRAMA WHERE codigo = 'PRG-AVE'");

        var soloTerneros = await Motor(e).GenerarInventarioFisicoAsync(new InventarioFisicoRequest(
            e.Ids.Tesorito, null, e.Ids.Categoria(NombresBase.CategoriaTerneros), EstadoAnimal.Disponible, Ruta(e, "a.xlsx")));
        var deAves = await Motor(e).GenerarInventarioFisicoAsync(new InventarioFisicoRequest(
            e.Ids.Tesorito, aves, null, EstadoAnimal.Disponible, Ruta(e, "b.xlsx")));

        using (var libro = new XLWorkbook(soloTerneros))
        {
            var textos = libro.Worksheet("Tesorito").CellsUsed().Select(c => c.GetString()).ToList();
            textos.Should().Contain("T-1").And.NotContain("A-1").And.NotContain("VACAS GENERAL");
        }

        using (var libro = new XLWorkbook(deAves))
        {
            var h = libro.Worksheet("Tesorito");
            h.Cell("A6").GetString().Should().Be("Sin semovientes para los filtros seleccionados.");
            h.CellsUsed().Select(c => c.GetString()).Should().NotContain(t => t.StartsWith("TOTAL SEDE"));
        }
    }

    [Fact]
    public async Task Todas_las_granjas_genera_una_pestana_por_cada_granja_activa()
    {
        await using var e = await CrearInventarioAsync();

        var ruta = await Motor(e).GenerarInventarioFisicoAsync(
            new InventarioFisicoRequest(null, null, null, EstadoAnimal.Disponible, Ruta(e, "todas.xlsx")));

        using var libro = new XLWorkbook(ruta);
        libro.Worksheets.Select(h => h.Name).Should().Equal("Tesorito", "La Cruz", "Montelindo");
        libro.Worksheet("La Cruz").Cell("A6").GetString().Should().Be("Sin semovientes para los filtros seleccionados.");
    }

    [Fact]
    public async Task La_valorizacion_acumulada_suma_los_deltas_registrados_del_animal()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-10-31");
        await e.AltaTerneraAsync("T-1", nacimiento: "2026-07-15", valor: 150_000m);
        await e.Aging.ValorizarAlCorteAsync(Datos.F("2026-10-31")); // 150.000 → 800.000

        var ruta = await Motor(e).GenerarInventarioFisicoAsync(
            new InventarioFisicoRequest(e.Ids.Tesorito, null, null, EstadoAnimal.Disponible, Ruta(e, "v.xlsx")));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("Tesorito");
        h.Cell("G7").GetDouble().Should().Be(800_000);
        h.Cell("H7").GetDouble().Should().Be(650_000);
        h.Cell("H8").GetDouble().Should().Be(650_000);
    }

    // ---- Hoja de vida --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task La_hoja_de_vida_muestra_datos_doble_valoracion_genealogia_e_historial()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-11-30");
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, EscenariosDeReporte.PngBase64);
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("K-1", Sexo.Hembra, "2022-03-01", 3_225_970m, partos: 1)));
        var traslado = await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.Montelindo, "K-1");
        traslado.Animales[0].OrientacionDestinoId = e.Ids.Carne; // Lácteo → Carne: al cierre baja de 6 M a 4 M
        await e.Kardex.RegistrarTrasladoAsync(traslado);
        await e.Aging.ValorizarAlCorteAsync(Datos.F("2026-11-30"));
        var animalId = await e.IdAsync("K-1");

        var ruta = await Motor(e).GenerarHojaDeVidaAsync(animalId, Ruta(e, "vida.xlsx"));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("Hoja de vida");
        var textos = h.CellsUsed().Select(c => c.GetString()).ToList();
        h.Cell("A2").GetString().Should().Be("HOJA DE VIDA DEL BOVINO");
        h.Cell("A3").GetString().Should().Be("K-1");
        textos.Should().Contain("Holstein").And.Contain("Montelindo").And.Contain("Carne/Levante").And.Contain("Vacas General");
        var numeros = h.CellsUsed().Where(c => c.Value.IsNumber).Select(c => c.GetDouble()).ToList();
        numeros.Should().Contain(3_225_970, "valor de compra (origen)")
            .And.Contain(4_000_000, "valor contable actual")
            .And.Contain(-2_000_000, "valorización acumulada");

        // Historial en orden cronológico: compra, traslado y valorización.
        var filaEncabezado = h.CellsUsed().First(c => c.GetString() == "N.º de remisión").Address.RowNumber;
        h.Cell(filaEncabezado + 1, 2).GetString().Should().Be("Compra");
        h.Cell(filaEncabezado + 1, 5).GetString().Should().Be("FAC-1");
        h.Cell(filaEncabezado + 2, 2).GetString().Should().Be("Traslado");
        h.Cell(filaEncabezado + 2, 3).GetString().Should().Be("Tesorito");
        h.Cell(filaEncabezado + 2, 4).GetString().Should().Be("Montelindo");
        h.Cell(filaEncabezado + 2, 5).GetString().Should().Be("REM-T-1");
        h.Cell(filaEncabezado + 3, 2).GetString().Should().Be("Valorizacion");
        h.Cell(filaEncabezado + 3, 6).GetDouble().Should().Be(-2_000_000);
        h.Cell(filaEncabezado + 4, 2).GetString().Should().BeEmpty("no hay más eventos");

        h.Pictures.Count.Should().Be(1);
        textos.Should().Contain("PAULA VALENCIA ARANGO");
    }

    [Fact]
    public async Task La_hoja_de_vida_de_un_nacido_muestra_costo_de_origen_cero_y_la_genealogia()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-11-06");
        await e.AltaDirectaAsync(Datos.Animal(e.Ids, "T-9", Sexo.Macho, "2021-01-01", NombresBase.CategoriaReproductor, 7_000_000m));
        var dto = e.Nacimiento("0940-26");
        dto.Nombre = "Lucero";
        dto.PadreChapeta = "T-9";
        await e.Kardex.RegistrarNacimientoAsync(dto);

        var ruta = await Motor(e).GenerarHojaDeVidaAsync(await e.IdAsync("0940-26"), Ruta(e, "vida.xlsx"));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("Hoja de vida");
        var textos = h.CellsUsed().Select(c => c.GetString()).ToList();
        h.Cell("A3").GetString().Should().Be("0940-26 — Lucero");
        textos.Should().Contain("Monta directa").And.Contain("T-9").And.Contain("044");
        var valorCompra = h.CellsUsed().First(c => c.GetString() == "Valor compra (origen, inmutable)").CellRight();
        valorCompra.GetDouble().Should().Be(0, "un nacido en la granja no tiene costo de compra");
        valorCompra.Style.NumberFormat.Format.Should().Be("\"$\"#,##0", "se muestra como $0 y no como guion");
    }

    [Fact]
    public async Task La_ficha_de_un_animal_inexistente_se_rechaza()
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var act = () => Motor(e).GenerarHojaDeVidaAsync(99999, Ruta(e, "x.xlsx"));

        await act.Should().ThrowAsync<System.ComponentModel.DataAnnotations.ValidationException>().WithMessage("*animal no existe*");
    }
}
