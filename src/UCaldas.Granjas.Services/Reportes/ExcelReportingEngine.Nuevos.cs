using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Reportes;

/// <param name="GranjaId">Nulo = todas las granjas (una pestaña por granja).</param>
/// <param name="ProgramaId">Nulo = todos los programas individuales.</param>
public sealed record InventarioIndividualRequest(int? GranjaId, int? ProgramaId, string RutaDestino);

/// <param name="GranjaId">Nulo = todas las granjas.</param>
/// <param name="ProgramaId">Programa por lotes (p. ej. Avicultura). Nulo, o uno que no sea por lotes = una pestaña por cada programa por lotes.</param>
public sealed record KardexLotesRequest(int Anio, int Mes, int? GranjaId, int? ProgramaId, string RutaDestino);

public sealed partial class ExcelReportingEngine
{
    // ---- Inventario físico individual por animal -----------------------------------------------------------------------

    public async Task<string> GenerarInventarioIndividualAsync(InventarioIndividualRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var granjas = (await uow.Granjas.GetAllAsync(ct)).OrderBy(g => g.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id);
        var animales = await uow.Animales.GetAllAsync(ct);
        var firma = await uow.Configuracion.GetFirmaAsync(ct);

        if (request.GranjaId is { } granjaId)
        {
            granjas = granjas.Where(g => g.Id == granjaId).ToList();
            Regla.Exigir(granjas.Count == 1, "La granja seleccionada no existe.");
        }

        using var libro = ExcelEstilos.NuevoLibro();
        var nombres = new NombresDeHoja();
        foreach (var granja in granjas)
        {
            var delaGranja = animales
                .Where(a => a.GranjaId == granja.Id
                            && a.Estado == EstadoAnimal.Disponible
                            && (request.ProgramaId is null || categorias.GetValueOrDefault(a.CategoriaId)?.ProgramaId == request.ProgramaId))
                .OrderBy(a => a.ChapetaAmarilla, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (delaGranja.Count == 0 && request.GranjaId is null && !granja.Activa)
            {
                continue;
            }

            EscribirInventarioIndividual(libro.Worksheets.Add(nombres.Siguiente(granja.Nombre)), granja, delaGranja, firma);
        }

        Regla.Exigir(libro.Worksheets.Count > 0, "No hay granjas para generar el informe.");
        return Guardar(libro, request.RutaDestino);
    }

    private void EscribirInventarioIndividual(IXLWorksheet hoja, Granja granja, IReadOnlyList<Animal> animales, ConfiguracionFirma? firma)
    {
        const int ultima = 6, filaEncabezado = 5;
        ExcelEstilos.Titulo(hoja, 1, 1, ultima, (firma?.EntidadFacultad ?? "FACULTAD DE CIENCIAS AGROPECUARIAS").ToUpperInvariant(), 11);
        ExcelEstilos.Titulo(hoja, 2, 1, ultima, "INVENTARIO FISICO INDIVIDUAL POR ANIMAL", 14);
        ExcelEstilos.Titulo(
            hoja, 3, 1, ultima,
            $"GRANJA {granja.Nombre.ToUpperInvariant()} · Corte al {reloj.Hoy.ToString("dd/MM/yyyy", Cultura)}", 11, negrita: false);

        string[] encabezados = ["CLASE DE ENTRADA", "FECHA NACIMIENTO", "SEXO", "CHAPETA AMARILLA", "PESO", "OBSERVACIONES"];
        for (var i = 0; i < encabezados.Length; i++)
        {
            hoja.Cell(filaEncabezado, i + 1).Value = encabezados[i];
        }

        hoja.Row(filaEncabezado).Height = 24;
        var fila = filaEncabezado + 1;
        if (animales.Count == 0)
        {
            hoja.Range(fila, 1, fila, ultima).Merge();
            hoja.Cell(fila, 1).Value = "Sin semovientes disponibles para los filtros seleccionados.";
            hoja.Cell(fila, 1).Style.Font.Italic = true;
            fila++;
        }

        foreach (var animal in animales)
        {
            hoja.Cell(fila, 1).Value = animal.ClaseEntrada;
            hoja.Cell(fila, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            hoja.Cell(fila, 2).Value = animal.FechaNacimiento.Date;
            hoja.Cell(fila, 2).Style.NumberFormat.Format = ExcelEstilos.FormatoFecha;
            hoja.Cell(fila, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            hoja.Cell(fila, 3).Value = animal.Sexo;
            hoja.Cell(fila, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ExcelEstilos.Texto(hoja.Cell(fila, 4), animal.ChapetaAmarilla);
            hoja.Cell(fila, 4).Style.Font.Bold = true;
            hoja.Cell(fila, 5).Value = animal.PesoActualKg;
            hoja.Cell(fila, 5).Style.NumberFormat.Format = "#,##0.0";
            if (!string.IsNullOrWhiteSpace(animal.Observaciones))
            {
                hoja.Cell(fila, 6).Value = animal.Observaciones;
            }

            hoja.Cell(fila, 6).Style.Alignment.WrapText = true;
            hoja.Cell(fila, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            fila++;
        }

        var ultimaTabla = fila - 1;
        ExcelEstilos.Bordes(hoja.Range(filaEncabezado, 1, ultimaTabla, ultima));
        ExcelEstilos.Encabezado(hoja.Range(filaEncabezado, 1, filaEncabezado, ultima));

        double[] anchos = [20, 18, 12, 20, 12, 50];
        for (var i = 0; i < anchos.Length; i++)
        {
            hoja.Column(i + 1).Width = anchos[i];
        }

        hoja.SheetView.FreezeRows(filaEncabezado);
        ExcelEstilos.ConfigurarImpresion(hoja);

        var siguiente = FirmaBlockWriter.Escribir(hoja, ultimaTabla + 2, 1, 4, firma);
        PieDeGeneracion(hoja, siguiente + 1);
    }

    // ---- Kardex mensual de lotes (aves) --------------------------------------------------------------------------------

    public async Task<string> GenerarKardexLotesAsync(KardexLotesRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Regla.Exigir(request.Mes is >= 1 and <= 12 && request.Anio is >= 2000 and <= 2200, "El mes o el año del informe no son válidos.");

        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var porLotes = (await uow.Programas.GetAllAsync(ct)).Where(p => p.TipoInventario == TipoInventario.Lote).OrderBy(p => p.Nombre).ToList();
        Regla.Exigir(porLotes.Count > 0, "No hay programas manejados por lotes para generar el informe.");
        var programas = porLotes.Where(p => p.Id == request.ProgramaId).ToList();
        if (programas.Count == 0)
        {
            programas = porLotes;
        }

        var lotes = (await uow.Lotes.GetAllAsync(ct)).Where(l => request.GranjaId is null || l.GranjaId == request.GranjaId).ToList();
        var movimientos = (await uow.Movimientos.GetAllAsync(ct)).Where(m => m.LoteId is not null).ToList();
        var ventasPorRemision = (await uow.Remisiones.GetAllAsync(ct))
            .Where(r => r.TipoMovimiento == TipoRemision.Venta && !r.Anulada)
            .ToDictionary(r => r.Id, r => r.FechaRemision);
        var ventas = (await uow.RemisionDetalles.GetAllAsync(ct))
            .Where(d => d.LoteId is not null && ventasPorRemision.ContainsKey(d.RemisionId))
            .Select(d => new VentaDeLote(d.LoteId!.Value, ventasPorRemision[d.RemisionId], d.Cantidad, d.CostoSistemaLibros, d.ValorTotalTransaccion ?? 0m))
            .ToList();
        var firma = await uow.Configuracion.GetFirmaAsync(ct);

        using var libro = ExcelEstilos.NuevoLibro();
        var nombres = new NombresDeHoja();
        foreach (var programa in programas)
        {
            var bloques = KardexLotesBuilder.Construir(
                request.Anio, request.Mes, lotes.Where(l => l.ProgramaId == programa.Id), movimientos, ventas);
            EscribirKardexLotes(libro.Worksheets.Add(nombres.Siguiente(programa.Nombre)), programa, request.Anio, request.Mes, bloques, firma);
        }

        return Guardar(libro, request.RutaDestino);
    }

    /// <summary>«Avicultura (Aves)» → «AVES»; sin paréntesis, el nombre del programa en mayúsculas.</summary>
    internal static string EspecieEnMayusculas(Programa programa)
    {
        var coincidencia = Regex.Match(programa.Nombre, @"\(([^)]+)\)");
        return (coincidencia.Success ? coincidencia.Groups[1].Value : programa.Nombre).Trim().ToUpperInvariant();
    }

    private void EscribirKardexLotes(IXLWorksheet hoja, Programa programa, int anio, int mes, IReadOnlyList<BloqueLote> bloques, ConfiguracionFirma? firma)
    {
        const int ultima = 13;
        var especie = EspecieEnMayusculas(programa);
        var nombreMes = new DateTime(anio, mes, 1).ToString("MMMM", Cultura).ToUpperInvariant();

        ExcelEstilos.Titulo(hoja, 1, 1, ultima, $"MOVIMIENTOS INVENTARIO {especie}", 14);
        ExcelEstilos.Titulo(hoja, 2, 1, ultima, $"{nombreMes} {anio}", 12);

        var fila = 4;
        var subtotales = new List<int>();
        if (bloques.Count == 0)
        {
            hoja.Range(fila, 1, fila, ultima).Merge();
            hoja.Cell(fila, 1).Value = "Sin lotes con saldo ni movimientos en el mes.";
            hoja.Cell(fila, 1).Style.Font.Italic = true;
            fila += 2;
        }

        foreach (var bloque in bloques)
        {
            fila = EscribirBloqueDeLote(hoja, fila, bloque, subtotales);
        }

        if (subtotales.Count > 0)
        {
            hoja.Range(fila, 1, fila, 11).Merge();
            hoja.Cell(fila, 1).Value = $"TOTAL GENERAL INVENTARIO {especie}";
            hoja.Cell(fila, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            hoja.Cell(fila, 12).FormulaA1 = string.Join("+", subtotales.Select(s => $"L{s}"));
            hoja.Cell(fila, 12).Style.NumberFormat.Format = ExcelEstilos.FormatoEntero;
            hoja.Cell(fila, 13).FormulaA1 = string.Join("+", subtotales.Select(s => $"M{s}"));
            hoja.Cell(fila, 13).Style.NumberFormat.Format = ExcelEstilos.FormatoMoneda;
            ExcelEstilos.Totales(hoja.Range(fila, 1, fila, ultima));
            fila++;
        }

        double[] anchos = [10, 17, 10, 14, 17, 10, 14, 17, 8, 15, 15, 10, 18, 26];
        for (var i = 0; i < anchos.Length; i++)
        {
            hoja.Column(i + 1).Width = anchos[i];
        }

        ExcelEstilos.ConfigurarImpresion(hoja);
        var siguiente = FirmaBlockWriter.Escribir(hoja, fila + 2, 1, 4, firma);
        PieDeGeneracion(hoja, siguiente + 1);
    }

    /// <summary>Escribe el bloque de un lote y devuelve la fila libre siguiente; la fila de subtotal se agrega a <paramref name="subtotales"/>.</summary>
    private static int EscribirBloqueDeLote(IXLWorksheet hoja, int fila, BloqueLote bloque, List<int> subtotales)
    {
        const int ultima = 13;
        foreach (var linea in bloque.Cabecera)
        {
            hoja.Range(fila, 1, fila, ultima).Merge();
            hoja.Cell(fila, 1).Value = linea;
            hoja.Cell(fila, 1).Style.Font.Bold = true;
            hoja.Cell(fila, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            fila++;
        }

        // Dos filas de encabezado: grupos combinados y subtítulos.
        void Grupo(int desde, int hasta, string texto)
        {
            if (hasta > desde)
            {
                hoja.Range(fila, desde, fila, hasta).Merge();
            }

            hoja.Cell(fila, desde).Value = texto;
        }

        Grupo(1, 2, "VIENEN");
        Grupo(3, 5, "VENTAS — COSTO EN LIBROS");
        Grupo(6, 8, "VENTAS — VALOR COMERCIAL");
        Grupo(9, 10, "MUERTES / MORTALIDAD");
        Grupo(11, 11, "VALORIZACIÓN");
        Grupo(12, 13, "SALDO FINAL");
        string[] sub = ["Cant.", "Valor", "Cant.", "Costo", "Vr", "Cant.", "Valor Venta", "Vr.Venta", "No.", "Valor", "Etapas", "Cant.", "Valor"];
        for (var i = 0; i < sub.Length; i++)
        {
            hoja.Cell(fila + 1, i + 1).Value = sub[i];
        }

        ExcelEstilos.Encabezado(hoja.Range(fila, 1, fila + 1, ultima));
        fila += 2;

        var conteo = bloque.Lote.ControlNumericoPoblacion;
        var primera = fila;
        var filas = Math.Max(1, bloque.Ventas.Count);
        for (var i = 0; i < filas; i++)
        {
            var r = fila + i;
            if (i == 0)
            {
                if (conteo)
                {
                    hoja.Cell(r, 1).Value = bloque.VienenCantidad;
                }

                hoja.Cell(r, 2).Value = bloque.VienenValor;
                if (conteo)
                {
                    hoja.Cell(r, 9).Value = bloque.MuertesCantidad;
                }

                hoja.Cell(r, 10).Value = bloque.MuertesValor;
                hoja.Cell(r, 11).Value = bloque.Valorizacion;
            }

            if (i < bloque.Ventas.Count)
            {
                var v = bloque.Ventas[i];
                if (conteo)
                {
                    hoja.Cell(r, 3).Value = v.Cantidad;
                    hoja.Cell(r, 4).Value = v.CostoUnitario;
                    hoja.Cell(r, 5).FormulaA1 = $"C{r}*D{r}";
                    hoja.Cell(r, 6).Value = v.Cantidad;
                    hoja.Cell(r, 7).Value = v.ValorVentaUnitario;
                    hoja.Cell(r, 8).FormulaA1 = $"F{r}*G{r}";
                }
                else
                {
                    hoja.Cell(r, 5).Value = v.CostoTotal;   // lote sin conteo: solo valores
                    hoja.Cell(r, 8).Value = v.ValorVentaTotal;
                }
            }

            // Saldo: la primera fila aplica la fórmula del documento; las siguientes descuentan cada venta adicional.
            if (i == 0)
            {
                hoja.Cell(r, 12).FormulaA1 = $"A{r}-C{r}-I{r}";
                hoja.Cell(r, 13).FormulaA1 = $"B{r}-E{r}-J{r}+K{r}";
            }
            else
            {
                hoja.Cell(r, 12).FormulaA1 = $"L{r - 1}-C{r}";
                hoja.Cell(r, 13).FormulaA1 = $"M{r - 1}-E{r}";
            }
        }

        var ultimaDato = fila + filas - 1;
        var subtotal = ultimaDato + 1;
        hoja.Cell(subtotal, 1).FormulaA1 = $"A{primera}";
        hoja.Cell(subtotal, 2).FormulaA1 = $"B{primera}";
        foreach (var columna in new[] { 3, 5, 6, 8, 9, 10, 11 })
        {
            var letra = XLHelper.GetColumnLetterFromNumber(columna);
            hoja.Cell(subtotal, columna).FormulaA1 = $"SUM({letra}{primera}:{letra}{ultimaDato})";
        }

        hoja.Cell(subtotal, 12).FormulaA1 = $"A{subtotal}-C{subtotal}-I{subtotal}";
        hoja.Cell(subtotal, 13).FormulaA1 = $"B{subtotal}-E{subtotal}-J{subtotal}+K{subtotal}";
        var filaSubtotal = hoja.Range(subtotal, 1, subtotal, ultima);
        filaSubtotal.Style.Font.Bold = true;
        filaSubtotal.Style.Fill.BackgroundColor = ExcelEstilos.VerdeClaro;
        hoja.Cell(subtotal, 14).Value = $"Subtotal lote {bloque.Lote.CodigoLote}";
        hoja.Cell(subtotal, 14).Style.Font.Italic = true;
        hoja.Cell(subtotal, 14).Style.Font.FontColor = ExcelEstilos.GrisTexto;

        ExcelEstilos.Bordes(hoja.Range(primera, 1, subtotal, ultima));
        foreach (var columna in new[] { 1, 3, 6, 9, 12 })
        {
            hoja.Range(primera, columna, subtotal, columna).Style.NumberFormat.Format = ExcelEstilos.FormatoEntero;
        }

        foreach (var columna in new[] { 2, 4, 5, 7, 8, 10, 11, 13 })
        {
            hoja.Range(primera, columna, subtotal, columna).Style.NumberFormat.Format = ExcelEstilos.FormatoMoneda;
        }

        subtotales.Add(subtotal);
        return subtotal + 2;
    }
}
