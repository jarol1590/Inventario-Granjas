using ClosedXML.Excel;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Comercial;

namespace UCaldas.Granjas.Services.Reportes;

/// <param name="Desde">Primer día del rango (incluido).</param>
/// <param name="Hasta">Último día del rango (incluido).</param>
/// <param name="ProgramaId">Nulo = todos los programas.</param>
public sealed record VentasReporteRequest(DateTime Desde, DateTime Hasta, int? ProgramaId, string RutaDestino);

public sealed partial class ExcelReportingEngine
{
    // ---- Ventas por programas (módulo Remisiones) -----------------------------------------------------------------------

    public async Task<string> GenerarVentasPorProgramaAsync(VentasReporteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ventas = await new RemisionesQueryService(fabrica).ObtenerVentasAsync(request.Desde, request.Hasta, request.ProgramaId, ct);
        var firma = await LeerFirmaAsync(ct);
        string? programa = null;
        if (request.ProgramaId is { } id)
        {
            await using var uow = await fabrica.OpenReadOnlyAsync(ct);
            programa = (await uow.Programas.GetByIdAsync(id, ct))?.Nombre ?? throw Regla.Violada("El programa seleccionado no existe.");
        }

        using var libro = ExcelEstilos.NuevoLibro();
        EscribirVentasPorPrograma(libro.Worksheets.Add("Ventas por programas"), ventas, programa, firma);
        return Guardar(libro, request.RutaDestino);
    }

    private void EscribirVentasPorPrograma(IXLWorksheet hoja, VentasInforme ventas, string? programa, ConfiguracionFirma? firma)
    {
        const int ultima = 10, filaEncabezado = 5;
        ExcelEstilos.Titulo(hoja, 1, 1, ultima, (firma?.EntidadFacultad ?? "FACULTAD DE CIENCIAS AGROPECUARIAS").ToUpperInvariant(), 11);
        ExcelEstilos.Titulo(hoja, 2, 1, ultima, "INFORME DE VENTAS POR PROGRAMAS", 14);
        ExcelEstilos.Titulo(
            hoja, 3, 1, ultima,
            $"Del {ventas.Desde.ToString("dd/MM/yyyy", Cultura)} al {ventas.Hasta.ToString("dd/MM/yyyy", Cultura)} · Programa: {programa ?? "Todos los programas"}",
            11, negrita: false);

        string[] encabezados =
            ["Fecha", "N° Oficio", "N° Remisión", "Recibo SP", "Convenio / Comprador", "Descripción ítem", "Unidad", "Cantidad", "Valor unitario", "Valor total"];
        for (var i = 0; i < encabezados.Length; i++)
        {
            hoja.Cell(filaEncabezado, i + 1).Value = encabezados[i];
        }

        ExcelEstilos.Encabezado(hoja.Range(filaEncabezado, 1, filaEncabezado, ultima));
        hoja.Row(filaEncabezado).Height = 28;

        var fila = filaEncabezado + 1;
        var totalesPrograma = new List<int>();
        if (ventas.Lineas.Count == 0)
        {
            hoja.Range(fila, 1, fila, ultima).Merge();
            hoja.Cell(fila, 1).Value = "Sin ventas en el rango seleccionado.";
            hoja.Cell(fila, 1).Style.Font.Italic = true;
            fila++;
        }

        foreach (var grupo in ventas.Lineas.GroupBy(l => (l.ProgramaId, l.Programa)).OrderBy(g => g.Key.Programa, StringComparer.CurrentCultureIgnoreCase))
        {
            var cabecera = hoja.Range(fila, 1, fila, ultima);
            cabecera.Merge();
            cabecera.Style.Fill.BackgroundColor = ExcelEstilos.GrisSuave;
            cabecera.Style.Font.Bold = true;
            hoja.Cell(fila, 1).Value = grupo.Key.Programa.ToUpperInvariant();
            fila++;

            var primera = fila;
            foreach (var l in grupo.OrderBy(x => x.Fecha).ThenBy(x => x.Remision, StringComparer.OrdinalIgnoreCase))
            {
                hoja.Cell(fila, 1).Value = l.Fecha.Date;
                hoja.Cell(fila, 1).Style.NumberFormat.Format = ExcelEstilos.FormatoFecha;
                ExcelEstilos.Texto(hoja.Cell(fila, 2), l.Oficio ?? "—");
                ExcelEstilos.Texto(hoja.Cell(fila, 3), l.Remision);
                ExcelEstilos.Texto(hoja.Cell(fila, 4), l.Recibo ?? "—");
                hoja.Cell(fila, 5).Value = l.Comprador;
                hoja.Cell(fila, 6).Value = l.Item;
                hoja.Cell(fila, 7).Value = l.Unidad;
                hoja.Cell(fila, 8).Value = l.Cantidad;
                hoja.Cell(fila, 8).Style.NumberFormat.Format = "#,##0.##";
                hoja.Cell(fila, 9).Value = l.ValorUnitario;
                hoja.Cell(fila, 10).Value = l.ValorTotal;
                fila++;
            }

            var ultimaLinea = fila - 1;
            hoja.Cell(fila, 1).Value = $"Subtotal {grupo.Key.Programa}";
            hoja.Cell(fila, 10).FormulaA1 = $"SUM(J{primera}:J{ultimaLinea})";
            var subtotal = fila;
            ResaltarSubtotal(hoja, fila, ultima);
            fila++;

            var descuento = grupo.Select(l => l.RemisionId).Distinct().Sum(rid => ventas.DescuentoPorRemision.GetValueOrDefault(rid));
            if (descuento > 0)
            {
                hoja.Cell(fila, 1).Value = "Menos descuentos";
                hoja.Cell(fila, 10).Value = -descuento;
                fila++;
                hoja.Cell(fila, 1).Value = $"Total {grupo.Key.Programa}";
                hoja.Cell(fila, 10).FormulaA1 = $"J{subtotal}+J{fila - 1}";
                ResaltarSubtotal(hoja, fila, ultima);
                subtotal = fila;
                fila++;
            }

            totalesPrograma.Add(subtotal);
        }

        var ultimaTabla = fila - 1;
        if (totalesPrograma.Count > 0)
        {
            hoja.Cell(fila, 1).Value = "TOTAL GENERAL RECAUDADO";
            hoja.Cell(fila, 10).FormulaA1 = string.Join("+", totalesPrograma.Select(t => $"J{t}"));
            ExcelEstilos.Totales(hoja.Range(fila, 1, fila, ultima));
            ultimaTabla = fila;
        }

        ExcelEstilos.Bordes(hoja.Range(filaEncabezado, 1, ultimaTabla, ultima));
        ExcelEstilos.Encabezado(hoja.Range(filaEncabezado, 1, filaEncabezado, ultima));
        foreach (var columna in new[] { 9, 10 })
        {
            hoja.Range(filaEncabezado + 1, columna, ultimaTabla, columna).Style.NumberFormat.Format = ExcelEstilos.FormatoMoneda;
        }

        double[] anchos = [12, 12, 14, 12, 30, 38, 10, 11, 16, 18];
        for (var i = 0; i < anchos.Length; i++)
        {
            hoja.Column(i + 1).Width = anchos[i];
        }

        hoja.SheetView.FreezeRows(filaEncabezado);
        ExcelEstilos.ConfigurarImpresion(hoja);
        var siguiente = FirmaBlockWriter.Escribir(hoja, ultimaTabla + 2, 1, 4, firma);
        PieDeGeneracion(hoja, siguiente + 1);
    }

    private static void ResaltarSubtotal(IXLWorksheet hoja, int fila, int ultimaColumna)
    {
        var rango = hoja.Range(fila, 1, fila, ultimaColumna);
        rango.Style.Font.Bold = true;
        rango.Style.Fill.BackgroundColor = ExcelEstilos.VerdeClaro;
    }
}
