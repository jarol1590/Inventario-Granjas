using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Comercial;

namespace UCaldas.Granjas.Services.Reportes;

/// <summary>PDF del módulo Remisiones: informe de ventas por programas, remisión y recibo de caja SP.</summary>
public interface IPdfDocumentService
{
    /// <summary>Mismo contenido que el informe de Excel de ventas por programas.</summary>
    Task<string> GenerarVentasPorProgramaAsync(VentasReporteRequest request, CancellationToken ct = default);

    /// <summary>Remisión impresa: cabecera, líneas, totales y firma.</summary>
    Task<string> GenerarRemisionAsync(int remisionId, string rutaDestino, CancellationToken ct = default);

    /// <summary>Recibo de caja SP de la remisión: «recibí de», suma en letras, forma de pago, detalle y firmas.</summary>
    Task<string> GenerarReciboAsync(int remisionId, string rutaDestino, CancellationToken ct = default);
}

public sealed class PdfDocumentService(IUnitOfWorkFactory fabrica, IReloj reloj) : IPdfDocumentService
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");
    private static readonly Color Azul = new(0, 62, 112);
    private static readonly Color Verde = new(102, 163, 130);
    private static readonly Color Gris = new(244, 247, 245);
    private const string Fuente = "Times New Roman";

    static PdfDocumentService()
    {
        // Las fuentes salen de las instaladas en Windows (Times New Roman, como los informes de Excel).
        GlobalFontSettings.FontResolver ??= new FuentesDeWindows();
    }

    /// <summary>Resuelve las tipografías desde la carpeta de fuentes de Windows (Times New Roman y, si falta, Arial).</summary>
    private sealed class FuentesDeWindows : IFontResolver
    {
        private static readonly string[][] Archivos =
        [
            ["times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"],
            ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"],
        ];

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            var indice = (isBold ? 1 : 0) + (isItalic ? 2 : 0);
            var carpeta = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var familia in Archivos)
            {
                if (File.Exists(Path.Combine(carpeta, familia[indice])))
                {
                    return new FontResolverInfo(familia[indice]);
                }
            }

            return null;
        }

        public byte[]? GetFont(string faceName)
            => File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), faceName));
    }

    // ---- Ventas por programas ---------------------------------------------------------------------------------------------

    public async Task<string> GenerarVentasPorProgramaAsync(VentasReporteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ventas = await new RemisionesQueryService(fabrica).ObtenerVentasAsync(request.Desde, request.Hasta, request.ProgramaId, ct);
        var (firma, _) = await LeerConfiguracionAsync(ct);
        string? programa = null;
        if (request.ProgramaId is { } id)
        {
            await using var uow = await fabrica.OpenReadOnlyAsync(ct);
            programa = (await uow.Programas.GetByIdAsync(id, ct))?.Nombre ?? throw Regla.Violada("El programa seleccionado no existe.");
        }

        var doc = NuevoDocumento(horizontal: true);
        var seccion = doc.LastSection;
        Cabecera(seccion, firma, "INFORME DE VENTAS POR PROGRAMAS",
            $"Del {ventas.Desde.ToString("dd/MM/yyyy", Cultura)} al {ventas.Hasta.ToString("dd/MM/yyyy", Cultura)} · Programa: {programa ?? "Todos los programas"}");

        decimal totalGeneral = 0m;
        if (ventas.Lineas.Count == 0)
        {
            Texto(seccion, "Sin ventas en el rango seleccionado.", cursiva: true);
        }

        foreach (var grupo in ventas.Lineas.GroupBy(l => (l.ProgramaId, l.Programa)).OrderBy(g => g.Key.Programa, StringComparer.CurrentCultureIgnoreCase))
        {
            var titulo = seccion.AddParagraph(grupo.Key.Programa.ToUpperInvariant());
            titulo.Format.Font.Bold = true;
            titulo.Format.Font.Size = 11;
            titulo.Format.SpaceBefore = Unit.FromPoint(10);
            titulo.Format.SpaceAfter = Unit.FromPoint(3);

            var tabla = NuevaTabla(seccion, [2.1, 1.9, 2.2, 1.9, 4.4, 5.6, 1.6, 1.6, 2.4, 2.6]);
            Fila(tabla, ["Fecha", "N° Oficio", "N° Remisión", "Recibo SP", "Convenio / Comprador", "Descripción ítem", "Unidad", "Cantidad", "Valor unitario", "Valor total"], encabezado: true);
            foreach (var l in grupo.OrderBy(x => x.Fecha).ThenBy(x => x.Remision, StringComparer.OrdinalIgnoreCase))
            {
                Fila(tabla, [l.Fecha.ToString("dd/MM/yyyy", Cultura), l.Oficio ?? "—", l.Remision, l.Recibo ?? "—", l.Comprador, l.Item, l.Unidad,
                    l.Cantidad.ToString("#,##0.##", Cultura), Dinero(l.ValorUnitario), Dinero(l.ValorTotal)], derecha: [7, 8, 9]);
            }

            var subtotal = grupo.Sum(l => l.ValorTotal);
            var descuento = grupo.Select(l => l.RemisionId).Distinct().Sum(rid => ventas.DescuentoPorRemision.GetValueOrDefault(rid));
            Fila(tabla, ["", "", "", "", "", $"Subtotal {grupo.Key.Programa}", "", "", "", Dinero(subtotal)], resaltada: true, derecha: [9]);
            if (descuento > 0)
            {
                Fila(tabla, ["", "", "", "", "", "Menos descuentos", "", "", "", Dinero(-descuento)], derecha: [9]);
                Fila(tabla, ["", "", "", "", "", $"Total {grupo.Key.Programa}", "", "", "", Dinero(subtotal - descuento)], resaltada: true, derecha: [9]);
            }

            totalGeneral += subtotal - descuento;
        }

        if (ventas.Lineas.Count > 0)
        {
            var total = seccion.AddParagraph($"TOTAL GENERAL RECAUDADO: {Dinero(totalGeneral)}");
            total.Format.Font.Bold = true;
            total.Format.Font.Size = 12;
            total.Format.Font.Color = Azul;
            total.Format.Alignment = ParagraphAlignment.Right;
            total.Format.SpaceBefore = Unit.FromPoint(12);
        }

        BloqueDeFirma(seccion, firma);
        Pie(seccion);
        return Guardar(doc, request.RutaDestino);
    }

    // ---- Remisión ---------------------------------------------------------------------------------------------------------

    public async Task<string> GenerarRemisionAsync(int remisionId, string rutaDestino, CancellationToken ct = default)
    {
        var vista = await new RemisionesQueryService(fabrica).ObtenerAsync(remisionId, ct);
        var (firma, config) = await LeerConfiguracionAsync(ct);
        var r = vista.Remision;
        var esVenta = r.TipoMovimiento == TipoRemision.Venta;

        var doc = NuevoDocumento(horizontal: false);
        var seccion = doc.LastSection;
        Cabecera(seccion, firma, $"REMISIÓN N° {r.NumeroRemision}" + (r.Anulada ? " (ANULADA)" : string.Empty), esVenta ? "Remisión de venta" : "Remisión de traslado entre granjas");

        var datos = NuevaTabla(seccion, [4.2, 5.8, 3.4, 4.6], bordes: false);
        Fila(datos, ["Fecha", r.FechaRemision.ToString("dd/MM/yyyy", Cultura), "Programa", vista.Programa], negritaEnPares: true);
        Fila(datos, ["Granja de origen", vista.GranjaOrigen, esVenta ? "Oficio" : "Granja de destino", esVenta ? vista.Oficio?.NumeroOficio ?? "—" : vista.GranjaDestino ?? "—"], negritaEnPares: true);
        if (esVenta)
        {
            Fila(datos, ["Comprador", vista.Comprador, "NIT / Cédula", vista.CompradorNit ?? "—"], negritaEnPares: true);
            Fila(datos, ["Recibo de caja", vista.Recibo is null ? r.ReciboCaja ?? "—" : $"{vista.Recibo.NumeroRecibo} {vista.Recibo.Prefijo}", "", ""], negritaEnPares: true);
        }
        else if (vista.Recibo is not null)
        {
            Fila(datos, ["Recibo de caja", $"{vista.Recibo.NumeroRecibo} {vista.Recibo.Prefijo}", "", ""], negritaEnPares: true);
        }

        seccion.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
        TablaDeLineas(seccion, vista.Lineas, soloValores: !esVenta && vista.Lineas.All(l => l.ValorTotal == 0m));

        var totales = vista.Totales;
        if (esVenta)
        {
            Resumen(seccion, "Subtotal", Dinero(totales.Subtotal));
            if (totales.ValorDescuento > 0)
            {
                Resumen(seccion, $"Descuento ({r.DescuentoPorcentaje:0.##} %)", Dinero(-totales.ValorDescuento));
            }

            Resumen(seccion, "TOTAL REMISIÓN", Dinero(totales.Total), destacado: true);
        }
        else if (vista.Recibo is not null)
        {
            Resumen(seccion, "Valor del traslado", Dinero(vista.Recibo.TotalPagado), destacado: true);
        }

        if (!string.IsNullOrWhiteSpace(r.Observaciones))
        {
            Texto(seccion, "Observaciones: " + r.Observaciones.Trim(), pequeno: true);
        }

        BloqueDeFirma(seccion, firma, config);
        Pie(seccion);
        return Guardar(doc, rutaDestino);
    }

    // ---- Recibo de caja ------------------------------------------------------------------------------------------------------

    public async Task<string> GenerarReciboAsync(int remisionId, string rutaDestino, CancellationToken ct = default)
    {
        var vista = await new RemisionesQueryService(fabrica).ObtenerAsync(remisionId, ct);
        var recibo = vista.Recibo ?? throw Regla.Violada($"La remisión {vista.Remision.NumeroRemision} todavía no tiene recibo de caja SP.");
        var (firma, config) = await LeerConfiguracionAsync(ct);
        var r = vista.Remision;

        var doc = NuevoDocumento(horizontal: false);
        var seccion = doc.LastSection;
        Cabecera(seccion, firma, $"RECIBO DE CAJA No. {recibo.NumeroRecibo} {recibo.Prefijo}" + (recibo.Anulado ? " (ANULADO)" : string.Empty),
            config.FacultadDepto.Length > 0 ? config.FacultadDepto : "Facultad de Ciencias Agropecuarias");

        var datos = NuevaTabla(seccion, [4.2, 13.8], bordes: false);
        Fila(datos, ["Fecha", recibo.Fecha.ToString("dd/MM/yyyy", Cultura)], negritaEnPares: true);
        Fila(datos, ["Recibí de", r.TipoMovimiento == TipoRemision.Venta ? $"{vista.Comprador}" + (vista.CompradorNit is null ? string.Empty : $" (NIT/CC {vista.CompradorNit})") : $"Granja {vista.GranjaOrigen} → {vista.GranjaDestino}"], negritaEnPares: true);
        Fila(datos, ["La suma de", recibo.TotalLetras], negritaEnPares: true);
        Fila(datos, ["Por concepto de", $"Remisión {r.NumeroRemision}" + (vista.Oficio is null ? string.Empty : $" · Oficio {vista.Oficio.NumeroOficio}") + $" · {vista.Programa}"], negritaEnPares: true);

        seccion.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
        TablaDeLineas(seccion, vista.Lineas, soloValores: false);
        Resumen(seccion, "Subtotal", Dinero(vista.Totales.Subtotal));
        if (r.DescuentoPorcentaje > 0)
        {
            Resumen(seccion, $"Descuento ({r.DescuentoPorcentaje:0.##} %)", Dinero(-vista.Totales.ValorDescuento));
        }

        Resumen(seccion, "TOTAL RECIBO", Dinero(recibo.TotalPagado), destacado: true);

        var forma = seccion.AddParagraph("Forma de pago: " + RemisionesQueryService.TextoFormaPago(recibo.FormaPago, recibo.BancoConsignacion));
        forma.Format.Font.Bold = true;
        forma.Format.SpaceBefore = Unit.FromPoint(10);
        if (recibo.FormaPago == FormaPago.Consignacion)
        {
            Texto(seccion, $"Consignación a la cuenta {config.TipoCuenta} No. {config.NumeroCuenta} de {config.EntidadBancaria}.", pequeno: true);
        }
        else if (recibo.FormaPago == FormaPago.EfectivoCheque)
        {
            var detalle = new List<string>();
            if (recibo.EsEfectivo)
            {
                detalle.Add("Efectivo");
            }

            if (!string.IsNullOrWhiteSpace(recibo.Cheque1Num))
            {
                detalle.Add($"Cheque 1: {recibo.Cheque1Num}");
            }

            if (!string.IsNullOrWhiteSpace(recibo.Cheque2Num))
            {
                detalle.Add($"Cheque 2: {recibo.Cheque2Num}");
            }

            Texto(seccion, string.Join(" · ", detalle), pequeno: true);
        }

        if (!string.IsNullOrWhiteSpace(recibo.Observaciones))
        {
            Texto(seccion, "Observaciones: " + recibo.Observaciones.Trim(), pequeno: true);
        }

        BloqueDeFirma(seccion, firma, config);
        Pie(seccion);
        return Guardar(doc, rutaDestino);
    }

    // ---- Construcción del documento ---------------------------------------------------------------------------------------------

    private async Task<(ConfiguracionFirma? Firma, ConfiguracionRemisiones Config)> LeerConfiguracionAsync(CancellationToken ct)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return (await uow.Configuracion.GetFirmaAsync(ct), await uow.ConfiguracionRemisiones.GetAsync(ct));
    }

    private static Document NuevoDocumento(bool horizontal)
    {
        var doc = new Document();
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = Fuente;
        normal.Font.Size = 9.5;
        var seccion = doc.AddSection();
        seccion.PageSetup.PageFormat = PageFormat.Letter;
        seccion.PageSetup.Orientation = horizontal ? Orientation.Landscape : Orientation.Portrait;
        seccion.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        seccion.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        seccion.PageSetup.TopMargin = Unit.FromCentimeter(1.6);
        seccion.PageSetup.BottomMargin = Unit.FromCentimeter(1.6);
        return doc;
    }

    private static void Cabecera(Section seccion, ConfiguracionFirma? firma, string titulo, string subtitulo)
    {
        var entidad = seccion.AddParagraph((firma?.EntidadFacultad ?? "FACULTAD DE CIENCIAS AGROPECUARIAS").ToUpperInvariant());
        entidad.Format.Alignment = ParagraphAlignment.Center;
        entidad.Format.Font.Size = 10;
        entidad.Format.Font.Color = Verde;
        entidad.Format.Font.Bold = true;

        var t = seccion.AddParagraph(titulo);
        t.Format.Alignment = ParagraphAlignment.Center;
        t.Format.Font.Size = 15;
        t.Format.Font.Bold = true;
        t.Format.Font.Color = Azul;
        t.Format.SpaceBefore = Unit.FromPoint(2);

        var s = seccion.AddParagraph(subtitulo);
        s.Format.Alignment = ParagraphAlignment.Center;
        s.Format.Font.Size = 10;
        s.Format.SpaceAfter = Unit.FromPoint(10);
    }

    private static Table NuevaTabla(Section seccion, double[] anchosCm, bool bordes = true)
    {
        var tabla = seccion.AddTable();
        tabla.Style = StyleNames.Normal;
        tabla.Borders.Color = new Color(143, 163, 153);
        tabla.Borders.Width = bordes ? 0.5 : 0;
        tabla.Rows.LeftIndent = 0;
        foreach (var ancho in anchosCm)
        {
            tabla.AddColumn(Unit.FromCentimeter(ancho));
        }

        return tabla;
    }

    private static void Fila(
        Table tabla, string[] celdas, bool encabezado = false, bool resaltada = false, int[]? derecha = null, bool negritaEnPares = false)
    {
        var fila = tabla.AddRow();
        fila.VerticalAlignment = VerticalAlignment.Center;
        fila.Format.SpaceBefore = Unit.FromPoint(1.5);
        fila.Format.SpaceAfter = Unit.FromPoint(1.5);
        if (encabezado)
        {
            fila.Shading.Color = Azul;
            fila.Format.Font.Color = Colors.White;
            fila.Format.Font.Bold = true;
            fila.Format.Alignment = ParagraphAlignment.Center;
            fila.HeadingFormat = true;
        }
        else if (resaltada)
        {
            fila.Shading.Color = Gris;
            fila.Format.Font.Bold = true;
        }

        for (var i = 0; i < celdas.Length; i++)
        {
            var p = fila.Cells[i].AddParagraph(celdas[i]);
            if (derecha is not null && derecha.Contains(i))
            {
                p.Format.Alignment = ParagraphAlignment.Right;
            }

            if (negritaEnPares && i % 2 == 0)
            {
                p.Format.Font.Bold = true;
            }
        }
    }

    private static void TablaDeLineas(Section seccion, IReadOnlyList<LineaRemision> lineas, bool soloValores)
    {
        var tabla = NuevaTabla(seccion, [2.6, 6.6, 1.8, 2.0, 2.4, 2.6]);
        Fila(tabla, ["Código", "Descripción", "Unidad", "Cantidad", "Valor unitario", "Valor total"], encabezado: true);
        foreach (var l in lineas)
        {
            Fila(
                tabla,
                [l.Codigo, l.Descripcion + (l.CodigoLoteDestino is null ? string.Empty : $" → lote {l.CodigoLoteDestino}"), l.Unidad, l.Cantidad.ToString("#,##0.##", Cultura),
                    soloValores ? "—" : Dinero(l.ValorUnitario), soloValores ? "—" : Dinero(l.ValorTotal)],
                derecha: [3, 4, 5]);
        }
    }

    private static void Resumen(Section seccion, string etiqueta, string valor, bool destacado = false)
    {
        var p = seccion.AddParagraph($"{etiqueta}:  {valor}");
        p.Format.Alignment = ParagraphAlignment.Right;
        p.Format.SpaceBefore = Unit.FromPoint(3);
        p.Format.Font.Bold = destacado;
        p.Format.Font.Size = destacado ? 12 : 10;
        if (destacado)
        {
            p.Format.Font.Color = Azul;
        }
    }

    private static void Texto(Section seccion, string texto, bool pequeno = false, bool cursiva = false)
    {
        var p = seccion.AddParagraph(texto);
        p.Format.Font.Size = pequeno ? 9 : 10;
        p.Format.Font.Italic = cursiva;
        p.Format.SpaceBefore = Unit.FromPoint(4);
    }

    /// <summary>Firma manuscrita digitalizada (si existe) sobre la línea y debajo la autoridad firmante; en recibos y remisiones, también tesorería.</summary>
    private static void BloqueDeFirma(Section seccion, ConfiguracionFirma? firma, ConfiguracionRemisiones? config = null)
    {
        if (firma is null)
        {
            return;
        }

        var espacio = seccion.AddParagraph();
        espacio.Format.SpaceBefore = Unit.FromPoint(22);
        if (FirmaBlockWriter.CargarImagen(firma.RutaImagenFirma) is { } imagen)
        {
            var img = seccion.AddImage("base64:" + Convert.ToBase64String(imagen.Datos));
            img.LockAspectRatio = true;
            img.Height = Unit.FromCentimeter(1.6);
        }

        var tabla = NuevaTabla(seccion, config is null ? [8.0] : [8.0, 8.0], bordes: false);
        var fila = tabla.AddRow();
        fila.Borders.Top.Width = 0.75;
        fila.Borders.Top.Color = Colors.Black;
        var autoridad = fila.Cells[0].AddParagraph(firma.NombreAutoridad.ToUpperInvariant());
        autoridad.Format.Font.Bold = true;
        fila.Cells[0].AddParagraph(firma.CargoAutoridad);
        fila.Cells[0].AddParagraph(firma.EntidadFacultad);
        if (!string.IsNullOrWhiteSpace(firma.Telefono))
        {
            fila.Cells[0].AddParagraph($"Tel. {firma.Telefono}");
        }

        if (config is not null && !string.IsNullOrWhiteSpace(config.JefeTesoreria))
        {
            var tesoreria = fila.Cells[1].AddParagraph(config.JefeTesoreria.ToUpperInvariant());
            tesoreria.Format.Font.Bold = true;
            fila.Cells[1].AddParagraph("Jefe de Tesorería");
        }
    }

    private void Pie(Section seccion)
    {
        var p = seccion.AddParagraph($"Generado el {reloj.Ahora.ToString("dd/MM/yyyy HH:mm", Cultura)} · Sistema Unificado de Gestión de Granjas");
        p.Format.Font.Size = 8;
        p.Format.Font.Italic = true;
        p.Format.Font.Color = new Color(91, 101, 115);
        p.Format.SpaceBefore = Unit.FromPoint(14);
    }

    private static string Dinero(decimal valor) => valor.ToString("$#,##0;-$#,##0;$0", Cultura);

    private static string Guardar(Document doc, string rutaDestino)
    {
        Regla.ExigirTexto(rutaDestino, "La ruta del archivo");
        var destino = Path.GetFullPath(Path.HasExtension(rutaDestino) ? rutaDestino : rutaDestino + ".pdf");
        Regla.Exigir(destino.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase), "El documento debe guardarse como archivo .pdf.");

        var temporal = destino + ".tmp";
        try
        {
            var renderer = new PdfDocumentRenderer { Document = doc };
            renderer.RenderDocument();
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            renderer.PdfDocument.Save(temporal);
            File.Move(temporal, destino, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporal);
            }
            catch (IOException)
            {
            }

            throw Regla.Violada($"No se pudo guardar el PDF en «{destino}». Si el archivo está abierto, ciérrelo e inténtelo de nuevo. ({ex.Message})");
        }

        return destino;
    }
}
