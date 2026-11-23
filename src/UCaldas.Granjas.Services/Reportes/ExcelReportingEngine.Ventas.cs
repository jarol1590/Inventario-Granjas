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

        // Un libro con una hoja por programa (subproductos, animales y lotes).
        var grupos = ventas.Lineas
            .GroupBy(l => (l.ProgramaId, l.Programa))
            .OrderBy(g => g.Key.Programa, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        using var libro = ExcelEstilos.NuevoLibro();
        if (grupos.Count == 0)
        {
            EscribirVentasDePrograma(libro.Worksheets.Add("Ventas"), programa ?? "Todos los programas", [], ventas, firma);
        }

        foreach (var grupo in grupos)
        {
            EscribirVentasDePrograma(libro.Worksheets.Add(NombreDeHoja(grupo.Key.Programa, libro)), grupo.Key.Programa, grupo.ToList(), ventas, firma);
        }

        return Guardar(libro, request.RutaDestino);
    }

    /// <summary>Nombre de hoja válido en Excel: sin caracteres prohibidos, máximo 31 y único dentro del libro.</summary>
    private static string NombreDeHoja(string programa, XLWorkbook libro)
    {
        var limpio = new string(programa.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim();
        limpio = limpio.Length == 0 ? "Programa" : limpio[..Math.Min(31, limpio.Length)];
        var nombre = limpio;
        for (var n = 2; libro.Worksheets.Any(h => h.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase)); n++)
        {
            nombre = $"{limpio[..Math.Min(limpio.Length, 28)]} {n}";
        }

        return nombre;
    }

    private void EscribirVentasDePrograma(IXLWorksheet hoja, string programa, IReadOnlyList<VentaInformeLinea> lineas, VentasInforme ventas, ConfiguracionFirma? firma)
    {
        const int ultima = 8, filaEncabezado = 5;
        ExcelEstilos.Titulo(hoja, 1, 1, ultima, (firma?.EntidadFacultad ?? "FACULTAD DE CIENCIAS AGROPECUARIAS").ToUpperInvariant(), 11);
        ExcelEstilos.Titulo(hoja, 2, 1, ultima, $"INFORME DE VENTAS - {programa.ToUpperInvariant()}", 14);
        ExcelEstilos.Titulo(
            hoja, 3, 1, ultima,
            $"Del {ventas.Desde.ToString("dd/MM/yyyy", Cultura)} al {ventas.Hasta.ToString("dd/MM/yyyy", Cultura)}",
            11, negrita: false);

        string[] encabezados = ["Remisión", "Cliente", "Año", "Mes", "Producto", "Cantidad (unidad)", "Precio Unitario", "Total"];
        for (var i = 0; i < encabezados.Length; i++)
        {
            hoja.Cell(filaEncabezado, i + 1).Value = encabezados[i];
        }

        ExcelEstilos.Encabezado(hoja.Range(filaEncabezado, 1, filaEncabezado, ultima));
        hoja.Row(filaEncabezado).Height = 28;

        var fila = filaEncabezado + 1;
        int ultimaTabla;
        if (lineas.Count == 0)
        {
            hoja.Range(fila, 1, fila, ultima).Merge();
            hoja.Cell(fila, 1).Value = "Sin ventas en el rango seleccionado.";
            hoja.Cell(fila, 1).Style.Font.Italic = true;
            ultimaTabla = fila;
        }
        else
        {
            var primera = fila;
            foreach (var l in lineas.OrderBy(x => x.Fecha).ThenBy(x => x.Remision, StringComparer.OrdinalIgnoreCase))
            {
                ExcelEstilos.Texto(hoja.Cell(fila, 1), l.Remision);
                hoja.Cell(fila, 2).Value = l.Comprador;
                hoja.Cell(fila, 3).Value = l.Fecha.Year;
                hoja.Cell(fila, 3).Style.NumberFormat.Format = "0";
                hoja.Cell(fila, 4).Value = Cultura.TextInfo.ToTitleCase(Cultura.DateTimeFormat.GetMonthName(l.Fecha.Month));
                hoja.Cell(fila, 5).Value = l.Item;
                hoja.Cell(fila, 6).Value = l.Cantidad;
                hoja.Cell(fila, 6).Style.NumberFormat.Format = "#,##0.##";
                hoja.Cell(fila, 7).Value = l.ValorUnitario;
                hoja.Cell(fila, 8).Value = l.ValorTotal;
                fila++;
            }

            hoja.Cell(fila, 1).Value = "Subtotal";
            hoja.Cell(fila, 6).FormulaA1 = $"SUM(F{primera}:F{fila - 1})";
            hoja.Cell(fila, 8).FormulaA1 = $"SUM(H{primera}:H{fila - 1})";
            ultimaTabla = fila;
        }

        ExcelEstilos.Bordes(hoja.Range(filaEncabezado, 1, ultimaTabla, ultima));
        ExcelEstilos.Encabezado(hoja.Range(filaEncabezado, 1, filaEncabezado, ultima));
        foreach (var columna in new[] { 7, 8 })
        {
            hoja.Range(filaEncabezado + 1, columna, ultimaTabla, columna).Style.NumberFormat.Format = ExcelEstilos.FormatoMoneda;
        }

        if (lineas.Count > 0)
        {
            hoja.Cell(ultimaTabla, 6).Style.NumberFormat.Format = "#,##0.##";
            ExcelEstilos.Totales(hoja.Range(ultimaTabla, 1, ultimaTabla, ultima));
        }

        double[] anchos = [12, 32, 8, 14, 30, 18, 16, 18];
        for (var i = 0; i < anchos.Length; i++)
        {
            hoja.Column(i + 1).Width = anchos[i];
        }

        hoja.SheetView.FreezeRows(filaEncabezado);
        ExcelEstilos.ConfigurarImpresion(hoja);
        var siguiente = FirmaBlockWriter.Escribir(hoja, ultimaTabla + 2, 1, 4, firma);
        PieDeGeneracion(hoja, siguiente + 1);
    }
}
