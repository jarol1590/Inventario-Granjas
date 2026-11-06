using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Reportes;

/// <summary>Imagen de firma ya decodificada y reconocida como PNG o JPG.</summary>
internal sealed record ImagenFirma(byte[] Datos, XLPictureFormat Formato);

/// <summary>
/// Inserta al final de una hoja el bloque institucional de firma: imagen manuscrita digitalizada sobre la línea,
/// y debajo nombre, cargo, facultad y teléfono de <see cref="ConfiguracionFirma"/>.
/// Si la imagen no existe o no se puede leer, el bloque de texto se escribe igual.
/// </summary>
internal static partial class FirmaBlockWriter
{
    private const int FilasDeImagen = 3;
    private const double AltoFilaImagen = 24;

    [GeneratedRegex("^[A-Za-z0-9+/=\\r\\n]+$")]
    private static partial Regex PatronBase64();

    /// <summary>Escribe el bloque desde <paramref name="fila"/> y devuelve la siguiente fila libre.</summary>
    /// <param name="columna">Primera columna del bloque.</param>
    /// <param name="ancho">Cantidad de columnas que ocupa la línea de firma.</param>
    public static int Escribir(IXLWorksheet hoja, int fila, int columna, int ancho, ConfiguracionFirma? firma)
    {
        if (firma is null)
        {
            return fila;
        }

        ancho = Math.Max(ancho, 1);
        var filaImagen = fila + 1;
        for (var i = 0; i < FilasDeImagen; i++)
        {
            hoja.Row(filaImagen + i).Height = AltoFilaImagen;
        }

        var imagen = CargarImagen(firma.RutaImagenFirma);
        if (imagen is not null)
        {
            using var flujo = new MemoryStream(imagen.Datos);
            hoja.AddPicture(flujo, imagen.Formato)
                .MoveTo(hoja.Cell(filaImagen, columna))
                .WithSize(190, (int)(AltoFilaImagen * FilasDeImagen) - 4);
        }

        var filaLinea = filaImagen + FilasDeImagen;
        var linea = hoja.Range(filaLinea, columna, filaLinea, columna + ancho - 1);
        linea.Merge();
        linea.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        linea.Style.Border.TopBorderColor = XLColor.Black;

        var actual = filaLinea;
        actual = EscribirLinea(hoja, actual + 1, columna, ancho, firma.NombreAutoridad.ToUpperInvariant(), negrita: true);
        actual = EscribirLinea(hoja, actual, columna, ancho, firma.CargoAutoridad, negrita: false);
        actual = EscribirLinea(hoja, actual, columna, ancho, firma.EntidadFacultad, negrita: false);
        if (!string.IsNullOrWhiteSpace(firma.Telefono))
        {
            actual = EscribirLinea(hoja, actual, columna, ancho, $"Tel. {firma.Telefono.Trim()}", negrita: false);
        }

        return actual;
    }

    private static int EscribirLinea(IXLWorksheet hoja, int fila, int columna, int ancho, string texto, bool negrita)
    {
        var rango = hoja.Range(fila, columna, fila, columna + ancho - 1);
        rango.Merge();
        rango.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        rango.Style.Font.Bold = negrita;
        hoja.Cell(fila, columna).Value = texto;
        return fila + 1;
    }

    /// <summary>
    /// Lee la imagen desde una ruta de archivo, un data-URI o Base64 puro. Devuelve <c>null</c> si no existe, está
    /// corrupta o no es PNG/JPG (nunca lanza: la firma es opcional para generar el informe).
    /// </summary>
    internal static ImagenFirma? CargarImagen(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var texto = valor.Trim();
        byte[]? bytes;
        try
        {
            if (texto.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var coma = texto.IndexOf(',');
                bytes = coma < 0 ? null : Convert.FromBase64String(texto[(coma + 1)..]);
            }
            else if (File.Exists(texto))
            {
                bytes = File.ReadAllBytes(texto);
            }
            else if (texto.Length > 40 && PatronBase64().IsMatch(texto))
            {
                bytes = Convert.FromBase64String(texto);
            }
            else
            {
                bytes = null;
            }
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        if (bytes is null || bytes.Length < 8)
        {
            return null;
        }

        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return new ImagenFirma(bytes, XLPictureFormat.Png);
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return new ImagenFirma(bytes, XLPictureFormat.Jpeg);
        }

        return null;
    }
}
