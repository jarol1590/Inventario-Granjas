using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace UCaldas.Granjas.Services.Reportes;

/// <summary>Ajustes al paquete .xlsx ya generado que ClosedXML no expone.</summary>
internal static class ExcelPaquete
{
    /// <summary>
    /// Le indica a Excel que no marque con el triángulo verde las celdas que son correctas a propósito: las chapetas
    /// numéricas guardadas como texto ("004") y las fórmulas de totales que difieren de sus vecinas. Solo afecta lo que
    /// se ve en pantalla: el contenido de las celdas no cambia.
    /// </summary>
    public static byte[] SuprimirAvisosDeCelda(byte[] xlsx)
    {
        using var memoria = new MemoryStream();
        memoria.Write(xlsx, 0, xlsx.Length);
        memoria.Position = 0;

        using (var documento = SpreadsheetDocument.Open(memoria, isEditable: true))
        {
            foreach (var parte in documento.WorkbookPart!.WorksheetParts)
            {
                var hoja = parte.Worksheet;
                if (hoja.GetFirstChild<IgnoredErrors>() is not null)
                {
                    continue;
                }

                var referencia = hoja.SheetDimension?.Reference?.Value ?? "A1:Z500";
                var ignorados = new IgnoredErrors(new IgnoredError
                {
                    SequenceOfReferences = new ListValue<StringValue> { InnerText = referencia },
                    NumberStoredAsText = true,
                    Formula = true,
                    FormulaRange = true,
                });

                // El esquema exige que <ignoredErrors> vaya antes de dibujos, tablas y extensiones.
                var siguiente = hoja.ChildElements.FirstOrDefault(e =>
                    e is Drawing or LegacyDrawing or LegacyDrawingHeaderFooter or DrawingHeaderFooter
                        or Picture or OleObjects or Controls or WebPublishItems or TableParts or WorksheetExtensionList);

                if (siguiente is not null)
                {
                    hoja.InsertBefore(ignorados, siguiente);
                }
                else
                {
                    hoja.Append(ignorados);
                }

                hoja.Save();
            }
        }

        return memoria.ToArray();
    }
}
