using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Reportes;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Reportes;

public class ExcelKardexTests
{
    private static ExcelReportingEngine Motor(EscenarioKardex e)
        => new(e.Db.Uow, new KardexReportBuilder(e.Db.Uow), new FichaService(e.Db.Uow, e.Reloj), e.Reloj);

    private static string Ruta(EscenarioKardex e, string nombre = "kardex.xlsx") => Path.Combine(e.Db.Carpeta, "informes", nombre);

    private static IEnumerable<string> Textos(IXLWorksheet hoja, int desdeFila = 1)
        => hoja.CellsUsed().Where(c => c.Address.RowNumber >= desdeFila).Select(c => c.GetString());

    // ---- Estructura y cifras --------------------------------------------------------------------------------------

    [Fact]
    public async Task Genera_una_pestana_por_granja_y_una_de_consolidado_general()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();

        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, null, Ruta(e)));

        File.Exists(ruta).Should().BeTrue();
        using var libro = new XLWorkbook(ruta);
        libro.Worksheets.Select(h => h.Name).Should().Equal("Tesorito", "La Cruz", "Montelindo", "Consolidado General");
    }

    [Fact]
    public async Task La_hoja_de_La_Cruz_replica_la_plantilla_oficial_de_septiembre_con_sus_cifras()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, null, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("La Cruz");

        h.Cell("A2").GetString().Should().Be("MOVIMIENTOS INVENTARIO GANADO — GRANJA LA CRUZ");
        h.Cell("A3").GetString().Should().Be("SEPTIEMBRE 2026");

        // Encabezados de la plantilla.
        h.Cell("A5").GetString().Should().Be("Saldo mes anterior");
        h.Cell("C5").GetString().Should().Be("Ventas");
        h.Cell("F5").GetString().Should().Be("Compras");
        h.Cell("I5").GetString().Should().Be("Nacimientos");
        h.Cell("K5").GetString().Should().Be("Muertes / Bajas");
        h.Cell("M5").GetString().Should().Be("Llegados otras granjas");
        h.Cell("O5").GetString().Should().Be("Traslados");
        h.Cell("Q5").GetString().Should().Be("Valorización");
        h.Cell("R5").GetString().Should().Be("Saldo");
        h.Cell("D6").GetString().Should().Be("Costo Sistema");
        h.Cell("E6").GetString().Should().Be("Vr. Venta");
        h.Cell("H6").GetString().Should().Be("Vr. Compra");
        h.Cell("Q6").GetString().Should().Be("Etapas");

        // Primera fila: saldo anterior, valorización y saldo final (como en la plantilla).
        h.Cell("A7").GetDouble().Should().Be(99);
        h.Cell("B7").GetDouble().Should().Be(65_947_815);
        h.Cell("Q7").GetDouble().Should().Be(-450_620);
        h.Cell("R7").GetDouble().Should().Be(108);
        h.Cell("S7").GetDouble().Should().Be(65_969_695);

        // Los nueve nacimientos, cada uno con su chapeta y su valor.
        for (var i = 0; i < 9; i++)
        {
            h.Cell(7 + i, 9).GetString().Should().Be(EscenariosDeReporte.ChapetasDeSeptiembre[i]);
            h.Cell(7 + i, 10).GetDouble().Should().Be(52_500);
        }

        // Fila de totales con fórmulas auditables en Excel.
        h.Cell("I16").FormulaA1.Should().Be("COUNTA(I7:I15)");
        h.Cell("J16").FormulaA1.Should().Be("SUM(J7:J15)");
        h.Cell("I16").GetDouble().Should().Be(9);
        h.Cell("J16").GetDouble().Should().Be(472_500);
        h.Cell("A16").GetDouble().Should().Be(99);
        h.Cell("B16").GetDouble().Should().Be(65_947_815);
        h.Cell("Q16").GetDouble().Should().Be(-450_620);
        h.Cell("R16").GetDouble().Should().Be(108, "el saldo final se recalcula con fórmula a partir de las columnas");
        h.Cell("S16").GetDouble().Should().Be(65_969_695);
        h.Cell("R16").FormulaA1.Should().Contain("A16").And.Contain("I16");
    }

    [Fact]
    public async Task El_saldo_final_recalculado_con_formulas_coincide_con_el_del_libro_en_cada_hoja()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, null, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        foreach (var nombre in new[] { "Tesorito", "La Cruz", "Montelindo" })
        {
            var h = libro.Worksheet(nombre);
            var totales = h.LastRowUsed(XLCellsUsedOptions.Contents)!;
            var filaTotales = Enumerable.Range(7, 40).First(r => h.Cell(r, 18).FormulaA1.Length > 0);
            h.Cell(filaTotales, 18).GetDouble().Should().Be(h.Cell(7, 18).GetDouble(), $"saldo final (cantidad) de {nombre}");
            h.Cell(filaTotales, 19).GetDouble().Should().Be(h.Cell(7, 19).GetDouble(), $"saldo final (valor) de {nombre}");
            totales.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task La_hoja_de_consolidado_suma_las_granjas_con_formulas()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, null, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("Consolidado General");

        h.Cell("A2").GetString().Should().Contain("CONSOLIDADO GENERAL");
        h.Cell("A7").GetString().Should().Be("Tesorito");
        h.Cell("A8").GetString().Should().Be("La Cruz");
        h.Cell("A9").GetString().Should().Be("Montelindo");
        h.Cell("A10").GetString().Should().Be("TOTAL GENERAL");
        h.Cell("B8").GetDouble().Should().Be(99);
        h.Cell("J8").GetDouble().Should().Be(9, "nacimientos de La Cruz");
        h.Cell("B10").FormulaA1.Should().Be("SUM(B7:B9)");
        h.Cell("B10").GetDouble().Should().Be(99);
        h.Cell("C10").GetDouble().Should().Be(65_947_815);
        h.Cell("J10").GetDouble().Should().Be(9);
        h.Cell("K10").GetDouble().Should().Be(472_500);
        h.Cell("R10").GetDouble().Should().Be(-450_620);
        h.Cell("S10").GetDouble().Should().Be(108);
        h.Cell("T10").GetDouble().Should().Be(65_969_695);
    }

    [Fact]
    public async Task Una_sola_granja_genera_solo_su_pestana_sin_consolidado()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();

        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        libro.Worksheets.Select(h => h.Name).Should().Equal("La Cruz");
    }

    // ---- Formato ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Aplica_el_azul_institucional_los_bordes_y_el_formato_de_moneda()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("La Cruz");

        foreach (var celda in new[] { "A5", "C5", "D6", "S6" })
        {
            h.Cell(celda).Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#1B365D"), celda);
            h.Cell(celda).Style.Font.FontColor.Should().Be(XLColor.White, celda);
            h.Cell(celda).Style.Font.Bold.Should().BeTrue(celda);
        }

        h.Cell("B7").Style.NumberFormat.Format.Should().Contain("$");
        h.Cell("S7").Style.NumberFormat.Format.Should().Contain("$");
        h.Cell("A7").Style.NumberFormat.Format.Should().NotContain("$", "las cantidades no llevan signo de moneda");
        h.Cell("J7").Style.Border.BottomBorder.Should().Be(XLBorderStyleValues.Thin);
        h.Cell("A16").Style.Font.Bold.Should().BeTrue("la fila de totales va en negrilla");
        h.PageSetup.PageOrientation.Should().Be(XLPageOrientation.Landscape);
    }

    [Fact]
    public async Task Los_ceros_se_muestran_como_guion_y_la_cuadricula_queda_oculta()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("La Cruz");

        h.Cell("D16").Style.NumberFormat.Format.Should().Contain("\"-\"", "un total en cero se ve como '-', igual que la plantilla");
        h.Cell("A16").Style.NumberFormat.Format.Should().Contain("\"-\"");
        h.ShowGridLines.Should().BeFalse();
    }

    [Fact]
    public async Task El_paquete_le_pide_a_Excel_que_no_marque_con_triangulo_las_celdas_correctas()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, EscenariosDeReporte.PngBase64);
        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, null, Ruta(e)));

        using var zip = System.IO.Compression.ZipFile.OpenRead(ruta);
        var hojas = zip.Entries.Where(x => x.FullName.StartsWith("xl/worksheets/sheet") && x.FullName.EndsWith(".xml")).ToList();
        hojas.Should().HaveCount(4);
        foreach (var entrada in hojas)
        {
            using var lector = new StreamReader(entrada.Open());
            var xml = await lector.ReadToEndAsync();

            // El XML puede llevar prefijo de espacio de nombres (<x:ignoredErrors>), por eso se busca con expresión regular.
            var ignorados = System.Text.RegularExpressions.Regex.Match(xml, "<(\\w+:)?ignoredErrors[ >]");
            var dibujo = System.Text.RegularExpressions.Regex.Match(xml, "<(\\w+:)?drawing[ >]");
            ignorados.Success.Should().BeTrue(entrada.FullName);
            xml.Should().Contain("numberStoredAsText=\"1\"");
            dibujo.Success.Should().BeTrue("cada hoja lleva la imagen de firma");
            ignorados.Index.Should().BeLessThan(dibujo.Index, "el esquema exige este orden o Excel pide reparar el archivo");
        }
    }

    [Fact]
    public async Task Las_chapetas_se_guardan_como_texto_para_que_Excel_no_las_convierta()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-10-31");
        await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("004", nacimiento: "2026-10-06"));
        await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("14-sep", nacimiento: "2026-10-07"));
        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 10, e.Ids.Tesorito, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("Tesorito");

        h.Cell("I7").Value.IsText.Should().BeTrue();
        h.Cell("I7").GetString().Should().Be("004");
        h.Cell("I8").GetString().Should().Be("14-sep");
    }

    // ---- Bloque de firma --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("data")]
    [InlineData("base64")]
    [InlineData("archivo")]
    public async Task El_bloque_de_firma_incrusta_la_imagen_y_los_datos_de_la_autoridad_en_cada_hoja(string origen)
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        var imagen = origen switch
        {
            "data" => "data:image/png;base64," + EscenariosDeReporte.PngBase64,
            "base64" => EscenariosDeReporte.PngBase64,
            _ => Path.Combine(e.Db.Carpeta, "firma.png"),
        };
        if (origen == "archivo")
        {
            await File.WriteAllBytesAsync(imagen, Convert.FromBase64String(EscenariosDeReporte.PngBase64));
        }

        await EscenariosDeReporte.ConfigurarFirmaAsync(e, imagen);
        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, null, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        foreach (var hoja in libro.Worksheets)
        {
            hoja.Pictures.Count.Should().Be(1, $"la firma debe estar incrustada en «{hoja.Name}»");
            var abajo = Textos(hoja, desdeFila: 11).ToList();
            abajo.Should().Contain("PAULA VALENCIA ARANGO").And.Contain("DECANO(A)")
                .And.Contain("FACULTAD DE CIENCIAS AGROPECUARIAS").And.Contain("Tel. 8781586");
        }
    }

    [Fact]
    public async Task El_bloque_de_firma_incluye_la_imagen_justo_encima_de_la_linea_de_texto()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, EscenariosDeReporte.PngBase64);
        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("La Cruz");
        var filaNombre = h.CellsUsed().First(c => c.GetString() == "PAULA VALENCIA ARANGO").Address.RowNumber;
        var imagen = h.Pictures.Single();

        imagen.TopLeftCell.Address.RowNumber.Should().BeLessThan(filaNombre, "la firma va sobre el nombre");
        imagen.TopLeftCell.Address.RowNumber.Should().BeGreaterThan(16, "y debajo de la tabla de totales");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\no-existe\firma.png")]
    [InlineData("esto no es una imagen")]
    [InlineData("data:image/png;base64,@@@@")]
    public async Task Sin_imagen_valida_el_bloque_de_texto_se_escribe_igual(string? imagen)
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, imagen);

        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("La Cruz");
        h.Pictures.Count.Should().Be(0);
        Textos(h, desdeFila: 17).Should().Contain("PAULA VALENCIA ARANGO").And.Contain("DECANO(A)");
    }

    [Fact]
    public async Task Un_archivo_que_no_es_png_ni_jpg_se_ignora_aunque_exista()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        var falso = Path.Combine(e.Db.Carpeta, "firma.png");
        await File.WriteAllTextAsync(falso, "no soy una imagen, soy texto");
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, falso);

        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        libro.Worksheet("La Cruz").Pictures.Count.Should().Be(0);
    }

    [Fact]
    public async Task La_firma_acepta_una_imagen_jpg()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        // Cabecera JPEG mínima (FF D8 FF E0 ...): suficiente para que se reconozca el formato.
        var jpg = Path.Combine(e.Db.Carpeta, "firma.jpg");
        var datos = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/yQALCAABAAEBAREA/8wABgAQEAX/2gAIAQEAAD8A0s8g/9k=");
        await File.WriteAllBytesAsync(jpg, datos);
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, jpg);

        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        libro.Worksheet("La Cruz").Pictures.Count.Should().Be(1);
    }

    // ---- Guardado ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Agrega_la_extension_crea_la_carpeta_y_sobrescribe_un_informe_anterior()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        var destino = Path.Combine(e.Db.Carpeta, "nueva", "carpeta", "informe");

        var primera = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, destino));
        var segunda = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, destino));

        primera.Should().EndWith("informe.xlsx");
        segunda.Should().Be(primera);
        File.Exists(primera).Should().BeTrue();
        File.Exists(primera + ".tmp").Should().BeFalse("no deben quedar archivos temporales");
    }

    [Fact]
    public async Task Rechaza_una_extension_que_no_sea_xlsx()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();

        var act = () => Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, null, Path.Combine(e.Db.Carpeta, "kardex.xls")));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*archivo .xlsx*");
    }

    [Fact]
    public async Task Si_el_archivo_esta_abierto_en_Excel_informa_claramente_y_no_deja_basura()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        var ruta = Ruta(e);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        await using var bloqueo = new FileStream(ruta, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var act = () => Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, null, ruta));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*No se pudo guardar el informe*abierto en Excel*");
        File.Exists(ruta + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task Los_nombres_de_pestana_se_depuran_y_nunca_se_repiten()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Db.EjecutarAsync("UPDATE GRANJA SET nombre = 'Sede [A]/B:C*D?' WHERE codigo = 'G-01'");
        await e.Db.EjecutarAsync("UPDATE GRANJA SET nombre = 'Sede -A-/B-C-D-' WHERE codigo = 'G-02'");
        await e.Db.EjecutarAsync("UPDATE GRANJA SET nombre = 'Una granja con un nombre larguísimo que excede los treinta y un caracteres' WHERE codigo = 'G-03'");

        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 10, null, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        var nombres = libro.Worksheets.Select(h => h.Name).ToList();
        nombres.Should().OnlyHaveUniqueItems();
        nombres.Should().OnlyContain(n => n.Length <= 31 && n.IndexOfAny("[]:*?/\\".ToCharArray()) < 0);
        nombres.Should().HaveCount(4);
    }

    [Fact]
    public async Task Sin_configuracion_de_firma_el_informe_se_genera_sin_bloque_de_firma()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();
        await e.Db.EjecutarAsync("DELETE FROM CONFIGURACION_FIRMA");

        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        var h = libro.Worksheet("La Cruz");
        h.Pictures.Count.Should().Be(0);
        Textos(h).Should().NotContain("PAULA VALENCIA ARANGO");
        h.Cell("A2").GetString().Should().Contain("GRANJA LA CRUZ");
    }

    [Fact]
    public async Task El_pie_indica_cuando_se_genero_el_informe()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();

        var ruta = await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, e.Ids.LaCruz, Ruta(e)));

        using var libro = new XLWorkbook(ruta);
        Textos(libro.Worksheet("La Cruz")).Should().Contain(t => t.StartsWith("Generado el 08/10/2026"));
    }
}
