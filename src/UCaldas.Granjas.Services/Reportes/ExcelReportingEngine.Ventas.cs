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

    private const int UltimaColumnaVentas = 10, FilaEncabezadoVentas = 5;

    private static readonly string[] EncabezadosVentas =
        ["Oficio", "Remisión", "Recibo de caja", "Cliente", "Año", "Mes", "Producto", "Cantidad (unidad)", "Precio Unitario", "Total"];

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

        // Un libro con una hoja por programa (subproductos, animales y lotes) y, al final, el consolidado.
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

        EscribirConsolidadoDeVentas(libro.Worksheets.Add(NombreDeHoja("Consolidado", libro)), grupos.Select(g => (g.Key.Programa, Lineas: g.ToList())).ToList(), ventas, firma);
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

    private void TitulosDeVentas(IXLWorksheet hoja, string titulo, VentasInforme ventas, ConfiguracionFirma? firma)
    {
        ExcelEstilos.Titulo(hoja, 1, 1, UltimaColumnaVentas, (firma?.EntidadFacultad ?? "FACULTAD DE CIENCIAS AGROPECUARIAS").ToUpperInvariant(), 11);
        ExcelEstilos.Titulo(hoja, 2, 1, UltimaColumnaVentas, titulo, 14);
        ExcelEstilos.Titulo(
            hoja, 3, 1, UltimaColumnaVentas,
            $"Del {ventas.Desde.ToString("dd/MM/yyyy", Cultura)} al {ventas.Hasta.ToString("dd/MM/yyyy", Cultura)}",
            11, negrita: false);

        for (var i = 0; i < EncabezadosVentas.Length; i++)
        {
            hoja.Cell(FilaEncabezadoVentas, i + 1).Value = EncabezadosVentas[i];
        }

        ExcelEstilos.Encabezado(hoja.Range(FilaEncabezadoVentas, 1, FilaEncabezadoVentas, UltimaColumnaVentas));
        hoja.Row(FilaEncabezadoVentas).Height = 28;
    }

    private static void EscribirLineaDeVenta(IXLWorksheet hoja, int fila, VentaInformeLinea l)
    {
        ExcelEstilos.Texto(hoja.Cell(fila, 1), l.Oficio ?? "-");
        ExcelEstilos.Texto(hoja.Cell(fila, 2), l.Remision);
        ExcelEstilos.Texto(hoja.Cell(fila, 3), l.Recibo ?? "-");
        hoja.Cell(fila, 4).Value = l.Comprador;
        hoja.Cell(fila, 5).Value = l.Fecha.Year;
        hoja.Cell(fila, 5).Style.NumberFormat.Format = ExcelEstilos.FormatoAnio;
        hoja.Cell(fila, 6).Value = Cultura.TextInfo.ToTitleCase(Cultura.DateTimeFormat.GetMonthName(l.Fecha.Month));
        hoja.Cell(fila, 7).Value = l.Item;
        hoja.Cell(fila, 8).Value = l.Cantidad;
        hoja.Cell(fila, 8).Style.NumberFormat.Format = ExcelEstilos.FormatoNumero;
        hoja.Cell(fila, 9).Value = l.ValorUnitario;
        hoja.Cell(fila, 10).Value = l.ValorTotal;
    }

    private static void FormatoDeVentas(IXLWorksheet hoja, int ultimaTabla)
    {
        ExcelEstilos.Bordes(hoja.Range(FilaEncabezadoVentas, 1, ultimaTabla, UltimaColumnaVentas));
        ExcelEstilos.Encabezado(hoja.Range(FilaEncabezadoVentas, 1, FilaEncabezadoVentas, UltimaColumnaVentas));
        foreach (var columna in new[] { 9, 10 })
        {
            hoja.Range(FilaEncabezadoVentas + 1, columna, ultimaTabla, columna).Style.NumberFormat.Format = ExcelEstilos.FormatoMoneda;
        }

        double[] anchos = [14, 12, 14, 32, 8, 14, 30, 18, 16, 18];
        for (var i = 0; i < anchos.Length; i++)
        {
            hoja.Column(i + 1).Width = anchos[i];
        }

        hoja.SheetView.FreezeRows(FilaEncabezadoVentas);
        ExcelEstilos.ConfigurarImpresion(hoja);
    }

    private void EscribirVentasDePrograma(IXLWorksheet hoja, string programa, IReadOnlyList<VentaInformeLinea> lineas, VentasInforme ventas, ConfiguracionFirma? firma)
    {
        TitulosDeVentas(hoja, $"INFORME DE VENTAS - {programa.ToUpperInvariant()}", ventas, firma);

        var fila = FilaEncabezadoVentas + 1;
        int ultimaTabla;
        if (lineas.Count == 0)
        {
            hoja.Range(fila, 1, fila, UltimaColumnaVentas).Merge();
            hoja.Cell(fila, 1).Value = "Sin ventas en el rango seleccionado.";
            hoja.Cell(fila, 1).Style.Font.Italic = true;
            ultimaTabla = fila;
        }
        else
        {
            var primera = fila;
            foreach (var l in lineas.OrderBy(x => x.Fecha).ThenBy(x => x.Remision, StringComparer.OrdinalIgnoreCase))
            {
                EscribirLineaDeVenta(hoja, fila, l);
                fila++;
            }

            hoja.Cell(fila, 1).Value = "Subtotal";
            hoja.Cell(fila, 8).FormulaA1 = $"SUM(H{primera}:H{fila - 1})";
            hoja.Cell(fila, 10).FormulaA1 = $"SUM(J{primera}:J{fila - 1})";
            ultimaTabla = fila;
        }

        FormatoDeVentas(hoja, ultimaTabla);
        if (lineas.Count > 0)
        {
            hoja.Cell(ultimaTabla, 8).Style.NumberFormat.Format = ExcelEstilos.FormatoNumero;
            ExcelEstilos.Totales(hoja.Range(ultimaTabla, 1, ultimaTabla, UltimaColumnaVentas));
        }

        var siguiente = FirmaBlockWriter.Escribir(hoja, ultimaTabla + 2, 1, 4, firma);
        PieDeGeneracion(hoja, siguiente + 1);
    }

    /// <summary>
    /// Hoja final del libro: reúne las ventas de todos los programas, agrupadas por programa y, dentro de cada uno, por oficio,
    /// con el subtotal de cada programa y el total general (suma de los totales de todos los programas).
    /// </summary>
    private void EscribirConsolidadoDeVentas(
        IXLWorksheet hoja, IReadOnlyList<(string Programa, List<VentaInformeLinea> Lineas)> programas, VentasInforme ventas, ConfiguracionFirma? firma)
    {
        TitulosDeVentas(hoja, "INFORME DE VENTAS - CONSOLIDADO DE PROGRAMAS", ventas, firma);

        var fila = FilaEncabezadoVentas + 1;
        if (programas.Count == 0)
        {
            hoja.Range(fila, 1, fila, UltimaColumnaVentas).Merge();
            hoja.Cell(fila, 1).Value = "Sin ventas en el rango seleccionado.";
            hoja.Cell(fila, 1).Style.Font.Italic = true;
            FormatoDeVentas(hoja, fila);
            var vacio = FirmaBlockWriter.Escribir(hoja, fila + 2, 1, 4, firma);
            PieDeGeneracion(hoja, vacio + 1);
            return;
        }

        var subtotalesDePrograma = new List<int>();
        var filasDeSubtotalOficio = new List<int>();
        foreach (var (programa, lineas) in programas)
        {
            var cabecera = hoja.Range(fila, 1, fila, UltimaColumnaVentas);
            cabecera.Merge();
            cabecera.Style.Fill.BackgroundColor = ExcelEstilos.GrisSuave;
            cabecera.Style.Font.Bold = true;
            hoja.Cell(fila, 1).Value = $"PROGRAMA: {programa.ToUpperInvariant()}";
            fila++;

            var subtotalesDeOficio = new List<int>();
            var porOficio = lineas
                .GroupBy(l => l.Oficio ?? string.Empty)
                .OrderBy(g => g.Key.Length == 0 ? 1 : 0)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var oficio in porOficio)
            {
                var primera = fila;
                foreach (var l in oficio.OrderBy(x => x.Fecha).ThenBy(x => x.Remision, StringComparer.OrdinalIgnoreCase))
                {
                    EscribirLineaDeVenta(hoja, fila, l);
                    fila++;
                }

                hoja.Cell(fila, 1).Value = $"Subtotal oficio {(oficio.Key.Length == 0 ? "(sin oficio)" : oficio.Key)}";
                hoja.Cell(fila, 8).FormulaA1 = $"SUM(H{primera}:H{fila - 1})";
                hoja.Cell(fila, 10).FormulaA1 = $"SUM(J{primera}:J{fila - 1})";
                filasDeSubtotalOficio.Add(fila);
                subtotalesDeOficio.Add(fila);
                fila++;
            }

            hoja.Cell(fila, 1).Value = $"Subtotal {programa}";
            hoja.Cell(fila, 8).FormulaA1 = string.Join("+", subtotalesDeOficio.Select(s => $"H{s}"));
            hoja.Cell(fila, 10).FormulaA1 = string.Join("+", subtotalesDeOficio.Select(s => $"J{s}"));
            subtotalesDePrograma.Add(fila);
            fila++;
        }

        hoja.Cell(fila, 1).Value = "TOTAL GENERAL";
        hoja.Cell(fila, 10).FormulaA1 = string.Join("+", subtotalesDePrograma.Select(s => $"J{s}"));
        var ultimaTabla = fila;

        FormatoDeVentas(hoja, ultimaTabla);
        foreach (var f in filasDeSubtotalOficio)
        {
            hoja.Range(f, 1, f, UltimaColumnaVentas).Style.Font.Bold = true;
            hoja.Cell(f, 8).Style.NumberFormat.Format = ExcelEstilos.FormatoNumero;
        }

        foreach (var f in subtotalesDePrograma)
        {
            hoja.Range(f, 1, f, UltimaColumnaVentas).Style.Font.Bold = true;
            hoja.Range(f, 1, f, UltimaColumnaVentas).Style.Fill.BackgroundColor = ExcelEstilos.VerdeClaro;
            hoja.Cell(f, 8).Style.NumberFormat.Format = ExcelEstilos.FormatoNumero;
        }

        ExcelEstilos.Totales(hoja.Range(ultimaTabla, 1, ultimaTabla, UltimaColumnaVentas));
        var siguiente = FirmaBlockWriter.Escribir(hoja, ultimaTabla + 2, 1, 4, firma);
        PieDeGeneracion(hoja, siguiente + 1);
    }
}
