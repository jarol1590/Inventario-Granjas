using ClosedXML.Excel;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Comercial;

namespace UCaldas.Granjas.Services.Reportes;

public sealed partial class ExcelReportingEngine
{
    // ---- Remisión y recibo de caja en Excel (módulo Remisiones) -----------------------------------------------------------

    private const int UltimaColumnaDocumento = 6;

    public async Task<string> GenerarRemisionAsync(int remisionId, string rutaDestino, CancellationToken ct = default)
    {
        var vista = await new RemisionesQueryService(fabrica).ObtenerAsync(remisionId, ct);
        var (firma, config) = await LeerFirmaYConfiguracionAsync(ct);
        var r = vista.Remision;
        var esVenta = r.TipoMovimiento == TipoRemision.Venta;

        using var libro = ExcelEstilos.NuevoLibro();
        var hoja = libro.Worksheets.Add("Remisión");
        var fila = CabeceraDeDocumento(
            hoja, firma, $"REMISIÓN N° {r.NumeroRemision}" + (r.Anulada ? " (ANULADA)" : string.Empty),
            esVenta ? "Remisión de venta" : "Remisión de traslado entre granjas");

        fila = DatoDeDocumento(hoja, fila, "Fecha", r.FechaRemision.ToString("dd/MM/yyyy", Cultura), "Programa", vista.Programa);
        fila = DatoDeDocumento(hoja, fila, "Granja de origen", vista.GranjaOrigen, esVenta ? "Oficio" : "Granja de destino", esVenta ? vista.Oficio?.NumeroOficio ?? "—" : vista.GranjaDestino ?? "—");
        if (esVenta)
        {
            fila = DatoDeDocumento(hoja, fila, "Comprador", vista.Comprador, "NIT / Cédula", vista.CompradorNit ?? "—");
            fila = DatoDeDocumento(hoja, fila, "Recibo de caja", vista.Recibo is null ? r.ReciboCaja ?? "—" : $"{vista.Recibo.NumeroRecibo} {vista.Recibo.Prefijo}", string.Empty, string.Empty);
        }
        else if (vista.Recibo is not null)
        {
            fila = DatoDeDocumento(hoja, fila, "Recibo de caja", $"{vista.Recibo.NumeroRecibo} {vista.Recibo.Prefijo}", string.Empty, string.Empty);
        }

        fila = TablaDeLineasDeDocumento(hoja, fila + 1, vista.Lineas, soloValores: !esVenta && vista.Lineas.All(l => l.ValorTotal == 0m));

        var totales = vista.Totales;
        if (esVenta)
        {
            fila = ResumenDeDocumento(hoja, fila, "Subtotal", totales.Subtotal);
            if (totales.ValorDescuento > 0)
            {
                fila = ResumenDeDocumento(hoja, fila, $"Descuento ({r.DescuentoPorcentaje:0.##} %)", -totales.ValorDescuento);
            }

            fila = ResumenDeDocumento(hoja, fila, "TOTAL REMISIÓN", totales.Total, destacado: true);
        }
        else if (vista.Recibo is not null)
        {
            fila = ResumenDeDocumento(hoja, fila, "Valor del traslado", vista.Recibo.TotalPagado, destacado: true);
        }

        if (!string.IsNullOrWhiteSpace(r.Observaciones))
        {
            fila = NotaDeDocumento(hoja, fila + 1, "Observaciones: " + r.Observaciones.Trim());
        }

        return CerrarDocumento(libro, hoja, fila, firma, config, rutaDestino);
    }

    public async Task<string> GenerarReciboAsync(int remisionId, string rutaDestino, CancellationToken ct = default)
    {
        var vista = await new RemisionesQueryService(fabrica).ObtenerAsync(remisionId, ct);
        var recibo = vista.Recibo ?? throw Regla.Violada($"La remisión {vista.Remision.NumeroRemision} todavía no tiene recibo de caja SP.");
        var (firma, config) = await LeerFirmaYConfiguracionAsync(ct);
        var r = vista.Remision;

        using var libro = ExcelEstilos.NuevoLibro();
        var hoja = libro.Worksheets.Add("Recibo de caja");
        var fila = CabeceraDeDocumento(
            hoja, firma, $"RECIBO DE CAJA No. {recibo.NumeroRecibo} {recibo.Prefijo}" + (recibo.Anulado ? " (ANULADO)" : string.Empty),
            config.FacultadDepto.Length > 0 ? config.FacultadDepto : "Facultad de Ciencias Agropecuarias");

        var recibiDe = r.TipoMovimiento == TipoRemision.Venta
            ? vista.Comprador + (vista.CompradorNit is null ? string.Empty : $" (NIT/CC {vista.CompradorNit})")
            : $"Granja {vista.GranjaOrigen} → {vista.GranjaDestino}";
        fila = DatoDeDocumento(hoja, fila, "Fecha", recibo.Fecha.ToString("dd/MM/yyyy", Cultura), string.Empty, string.Empty);
        fila = DatoDeDocumento(hoja, fila, "Recibí de", recibiDe, string.Empty, string.Empty);
        fila = DatoDeDocumento(hoja, fila, "La suma de", recibo.TotalLetras, string.Empty, string.Empty);
        fila = DatoDeDocumento(
            hoja, fila, "Por concepto de",
            $"Remisión {r.NumeroRemision}" + (vista.Oficio is null ? string.Empty : $" · Oficio {vista.Oficio.NumeroOficio}") + $" · {vista.Programa}",
            string.Empty, string.Empty);

        fila = TablaDeLineasDeDocumento(hoja, fila + 1, vista.Lineas, soloValores: false);
        fila = ResumenDeDocumento(hoja, fila, "Subtotal", vista.Totales.Subtotal);
        if (r.DescuentoPorcentaje > 0)
        {
            fila = ResumenDeDocumento(hoja, fila, $"Descuento ({r.DescuentoPorcentaje:0.##} %)", -vista.Totales.ValorDescuento);
        }

        fila = ResumenDeDocumento(hoja, fila, "TOTAL RECIBO", recibo.TotalPagado, destacado: true);

        fila = DatoDeDocumento(hoja, fila + 1, "Forma de pago", RemisionesQueryService.TextoFormaPago(recibo.FormaPago, recibo.BancoConsignacion), string.Empty, string.Empty);
        var cuenta = $"la cuenta {recibo.TipoCuenta ?? config.TipoCuenta} No. {recibo.NumeroCuenta ?? config.NumeroCuenta} de {recibo.BancoConsignacion ?? config.EntidadBancaria}.";
        if (recibo.FormaPago == FormaPago.Consignacion)
        {
            fila = NotaDeDocumento(hoja, fila, $"Consignación a {cuenta}");
        }
        else if (recibo.FormaPago == FormaPago.Transferencia)
        {
            fila = NotaDeDocumento(hoja, fila, $"Transferencia electrónica a {cuenta}");
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

            fila = NotaDeDocumento(hoja, fila, string.Join(" · ", detalle));
        }

        if (!string.IsNullOrWhiteSpace(recibo.Observaciones))
        {
            fila = NotaDeDocumento(hoja, fila, "Observaciones: " + recibo.Observaciones.Trim());
        }

        return CerrarDocumento(libro, hoja, fila, firma, config, rutaDestino);
    }

    private async Task<(ConfiguracionFirma? Firma, ConfiguracionRemisiones Config)> LeerFirmaYConfiguracionAsync(CancellationToken ct)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return (await uow.Configuracion.GetFirmaAsync(ct), await uow.ConfiguracionRemisiones.GetAsync(ct));
    }

    private static int CabeceraDeDocumento(IXLWorksheet hoja, ConfiguracionFirma? firma, string titulo, string subtitulo)
    {
        ExcelEstilos.Titulo(hoja, 1, 1, UltimaColumnaDocumento, (firma?.EntidadFacultad ?? "FACULTAD DE CIENCIAS AGROPECUARIAS").ToUpperInvariant(), 11);
        ExcelEstilos.Titulo(hoja, 2, 1, UltimaColumnaDocumento, titulo, 14);
        ExcelEstilos.Titulo(hoja, 3, 1, UltimaColumnaDocumento, subtitulo, 11, negrita: false);

        double[] anchos = [16, 40, 14, 14, 18, 18];
        for (var i = 0; i < anchos.Length; i++)
        {
            hoja.Column(i + 1).Width = anchos[i];
        }

        return 5;
    }

    /// <summary>Un dato (o dos) por fila: etiqueta en negrita y valor combinado hasta la última columna cuando no hay segundo par.</summary>
    private static int DatoDeDocumento(IXLWorksheet hoja, int fila, string etiqueta1, string valor1, string etiqueta2, string valor2)
    {
        hoja.Cell(fila, 1).Value = etiqueta1;
        hoja.Cell(fila, 1).Style.Font.Bold = true;
        var tienePar = !string.IsNullOrEmpty(etiqueta2);
        hoja.Range(fila, 2, fila, tienePar ? 3 : UltimaColumnaDocumento).Merge();
        ExcelEstilos.Texto(hoja.Cell(fila, 2), valor1);
        hoja.Cell(fila, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        hoja.Cell(fila, 2).Style.Alignment.WrapText = true;
        if (tienePar)
        {
            hoja.Cell(fila, 4).Value = etiqueta2;
            hoja.Cell(fila, 4).Style.Font.Bold = true;
            hoja.Range(fila, 5, fila, UltimaColumnaDocumento).Merge();
            ExcelEstilos.Texto(hoja.Cell(fila, 5), valor2);
            hoja.Cell(fila, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        return fila + 1;
    }

    private static int TablaDeLineasDeDocumento(IXLWorksheet hoja, int fila, IReadOnlyList<LineaRemision> lineas, bool soloValores)
    {
        string[] encabezados = ["Código", "Descripción", "Unidad", "Cantidad", "Valor unitario", "Valor total"];
        for (var i = 0; i < encabezados.Length; i++)
        {
            hoja.Cell(fila, i + 1).Value = encabezados[i];
        }

        ExcelEstilos.Encabezado(hoja.Range(fila, 1, fila, UltimaColumnaDocumento));
        var primera = fila;
        fila++;
        foreach (var l in lineas)
        {
            ExcelEstilos.Texto(hoja.Cell(fila, 1), l.Codigo);
            hoja.Cell(fila, 2).Value = l.Descripcion + (l.CodigoLoteDestino is null ? string.Empty : $" → lote {l.CodigoLoteDestino}");
            hoja.Cell(fila, 3).Value = l.Unidad;
            hoja.Cell(fila, 4).Value = l.Cantidad;
            hoja.Cell(fila, 4).Style.NumberFormat.Format = ExcelEstilos.FormatoNumero;
            if (soloValores)
            {
                hoja.Cell(fila, 5).Value = "—";
                hoja.Cell(fila, 6).Value = "—";
                hoja.Range(fila, 5, fila, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
            else
            {
                hoja.Cell(fila, 5).Value = l.ValorUnitario;
                hoja.Cell(fila, 6).Value = l.ValorTotal;
                hoja.Range(fila, 5, fila, 6).Style.NumberFormat.Format = ExcelEstilos.FormatoMoneda;
            }

            fila++;
        }

        ExcelEstilos.Bordes(hoja.Range(primera, 1, fila - 1, UltimaColumnaDocumento));
        return fila;
    }

    private static int ResumenDeDocumento(IXLWorksheet hoja, int fila, string etiqueta, decimal valor, bool destacado = false)
    {
        hoja.Range(fila, 1, fila, 5).Merge();
        hoja.Cell(fila, 1).Value = etiqueta;
        hoja.Cell(fila, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        hoja.Cell(fila, 6).Value = valor;
        hoja.Cell(fila, 6).Style.NumberFormat.Format = ExcelEstilos.FormatoMoneda;
        if (destacado)
        {
            ExcelEstilos.Totales(hoja.Range(fila, 1, fila, UltimaColumnaDocumento));
        }

        return fila + 1;
    }

    private static int NotaDeDocumento(IXLWorksheet hoja, int fila, string texto)
    {
        var rango = hoja.Range(fila, 1, fila, UltimaColumnaDocumento);
        rango.Merge();
        hoja.Cell(fila, 1).Value = texto;
        hoja.Cell(fila, 1).Style.Alignment.WrapText = true;
        hoja.Cell(fila, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        hoja.Row(fila).Height = Math.Max(16, 16 * Math.Ceiling(texto.Length / 110d));
        return fila + 1;
    }

    private string CerrarDocumento(XLWorkbook libro, IXLWorksheet hoja, int fila, ConfiguracionFirma? firma, ConfiguracionRemisiones config, string rutaDestino)
    {
        ExcelEstilos.ConfigurarImpresion(hoja);
        var siguiente = FirmaBlockWriter.Escribir(hoja, fila + 2, 1, 3, firma);
        if (firma is not null && !string.IsNullOrWhiteSpace(config.JefeTesoreria))
        {
            siguiente++;
            hoja.Cell(siguiente, 1).Value = $"Jefe de Tesorería: {config.JefeTesoreria.ToUpperInvariant()}";
            hoja.Cell(siguiente, 1).Style.Font.Bold = true;
            siguiente++;
        }

        PieDeGeneracion(hoja, siguiente + 1);
        return Guardar(libro, rutaDestino);
    }
}
