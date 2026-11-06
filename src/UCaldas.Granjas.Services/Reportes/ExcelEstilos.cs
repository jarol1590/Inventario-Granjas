using ClosedXML.Excel;

namespace UCaldas.Granjas.Services.Reportes;

/// <summary>Estilos institucionales compartidos por los informes de Excel (azul institucional #1B365D).</summary>
internal static class ExcelEstilos
{
    public static readonly XLColor AzulInstitucional = XLColor.FromHtml("#1B365D");
    public static readonly XLColor AzulClaro = XLColor.FromHtml("#DCE6F2");
    public static readonly XLColor GrisSuave = XLColor.FromHtml("#F4F6F9");
    public static readonly XLColor GrisTexto = XLColor.FromHtml("#5B6573");
    public static readonly XLColor Borde = XLColor.FromHtml("#8A97A8");

    /// <summary>
    /// Pesos sin decimales (el peso colombiano no se maneja en centavos; el valor exacto queda en la celda) y el cero
    /// como "-", igual que la plantilla institucional.
    /// </summary>
    public const string FormatoMoneda = "\"$\"#,##0;-\"$\"#,##0;\"-\"";

    public const string FormatoEntero = "#,##0;-#,##0;\"-\"";

    /// <summary>Moneda que muestra el cero como "$0" (para datos puntuales como el costo de compra de un nacido).</summary>
    public const string FormatoMonedaConCero = "\"$\"#,##0";

    /// <summary>Cantidad con unidad en singular o plural según corresponda ("1 cabeza", "3 cabezas").</summary>
    public const string FormatoCabezas = "[=1]#,##0\" cabeza\";#,##0\" cabezas\"";

    public const string FormatoSemovientes = "[=1]#,##0\" semoviente\";#,##0\" semovientes\"";

    public const string FormatoFecha = "dd/MM/yyyy";

    public static void Encabezado(IXLRange rango)
    {
        rango.Style.Fill.BackgroundColor = AzulInstitucional;
        rango.Style.Font.FontColor = XLColor.White;
        rango.Style.Font.Bold = true;
        rango.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        rango.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        rango.Style.Alignment.WrapText = true;
        Bordes(rango);
    }

    public static void Bordes(IXLRange rango)
    {
        rango.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        rango.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        rango.Style.Border.OutsideBorderColor = Borde;
        rango.Style.Border.InsideBorderColor = Borde;
    }

    public static void Totales(IXLRange rango)
    {
        rango.Style.Font.Bold = true;
        rango.Style.Fill.BackgroundColor = AzulClaro;
        rango.Style.Border.TopBorder = XLBorderStyleValues.Double;
        rango.Style.Border.TopBorderColor = AzulInstitucional;
    }

    /// <summary>Escribe un título centrado sobre un rango combinado.</summary>
    public static void Titulo(IXLWorksheet hoja, int fila, int columnaInicial, int columnaFinal, string texto, double tamano, bool negrita = true)
    {
        var rango = hoja.Range(fila, columnaInicial, fila, columnaFinal);
        rango.Merge();
        rango.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        rango.Style.Font.Bold = negrita;
        rango.Style.Font.FontSize = tamano;
        rango.Style.Font.FontColor = AzulInstitucional;
        hoja.Cell(fila, columnaInicial).Value = texto;
    }

    /// <summary>Celda de texto: evita que Excel convierta chapetas como "004" o "14-sep" en números o fechas.</summary>
    public static void Texto(IXLCell celda, string texto)
    {
        celda.Style.NumberFormat.Format = "@";
        celda.Value = texto;
    }

    public static void ConfigurarImpresion(IXLWorksheet hoja)
    {
        hoja.ShowGridLines = false;
        hoja.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        hoja.PageSetup.PaperSize = XLPaperSize.LetterPaper;
        hoja.PageSetup.FitToPages(1, 0);
        hoja.PageSetup.Margins.Left = 0.4;
        hoja.PageSetup.Margins.Right = 0.4;
        hoja.PageSetup.Margins.Top = 0.5;
        hoja.PageSetup.Margins.Bottom = 0.5;
    }
}
