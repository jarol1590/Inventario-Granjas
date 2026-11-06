using System.Globalization;
using ClosedXML.Excel;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Animales;

namespace UCaldas.Granjas.Services.Reportes;

/// <param name="GranjaId">Nulo = todas las granjas (una pestaña por granja y una de consolidado general).</param>
public sealed record KardexReporteRequest(int Anio, int Mes, int? GranjaId, string RutaDestino);

/// <param name="Estado">Nulo = todos los estados.</param>
public sealed record InventarioFisicoRequest(int? GranjaId, int? ProgramaId, int? CategoriaId, string? Estado, string RutaDestino);

/// <summary>Genera los informes institucionales en Excel (.xlsx) con el bloque de firma configurado.</summary>
public interface IExcelReportingEngine
{
    /// <summary>Kardex financiero mensual de hatos por granja, con la estructura del informe a la División Financiera.</summary>
    /// <returns>Ruta completa del archivo generado.</returns>
    Task<string> GenerarKardexFinancieroAsync(KardexReporteRequest request, CancellationToken ct = default);

    /// <summary>Inventario físico y financiero por granja, agrupado por categoría con subtotales y gran total.</summary>
    Task<string> GenerarInventarioFisicoAsync(InventarioFisicoRequest request, CancellationToken ct = default);

    /// <summary>Hoja de vida individual de un bovino: datos, doble valoración, genealogía e historial.</summary>
    Task<string> GenerarHojaDeVidaAsync(int animalId, string rutaDestino, CancellationToken ct = default);
}

public sealed class ExcelReportingEngine(
    IUnitOfWorkFactory fabrica,
    IKardexReportBuilder kardex,
    IFichaService fichas,
    IReloj reloj) : IExcelReportingEngine
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    // Columnas de la hoja de cada granja (plantilla del Kardex de septiembre 2026).
    private const int ColSaldoCant = 1, ColSaldoValor = 2;
    private const int ColVentaNo = 3, ColVentaCosto = 4, ColVentaValor = 5;
    private const int ColCompraNo = 6, ColCompraCosto = 7, ColCompraValor = 8;
    private const int ColNacimientoNo = 9, ColNacimientoValor = 10;
    private const int ColMuerteNo = 11, ColMuerteCosto = 12;
    private const int ColLlegadoNo = 13, ColLlegadoValor = 14;
    private const int ColTrasladoNo = 15, ColTrasladoValor = 16;
    private const int ColValorizacion = 17;
    private const int ColFinalCant = 18, ColFinalValor = 19;
    private const int UltimaColumnaKardex = 19;

    // ---- Kardex financiero ---------------------------------------------------------------------------------------

    public async Task<string> GenerarKardexFinancieroAsync(KardexReporteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var granjas = await kardex.ConstruirAsync(request.Anio, request.Mes, request.GranjaId, ct);
        Regla.Exigir(granjas.Count > 0, "No hay granjas para generar el informe.");
        var firma = await LeerFirmaAsync(ct);

        using var libro = new XLWorkbook();
        var nombres = new NombresDeHoja();
        foreach (var granja in granjas)
        {
            EscribirHojaKardex(libro.Worksheets.Add(nombres.Siguiente(granja.GranjaNombre)), granja, firma);
        }

        if (request.GranjaId is null)
        {
            EscribirConsolidado(libro.Worksheets.Add(nombres.Siguiente("Consolidado General")), granjas, firma);
        }

        return Guardar(libro, request.RutaDestino);
    }

    private void EscribirHojaKardex(IXLWorksheet hoja, KardexGranjaMensual k, ConfiguracionFirma? firma)
    {
        const int grupos = 5, subtitulos = 6, primeraFila = 7;

        ExcelEstilos.Titulo(hoja, 1, 1, UltimaColumnaKardex, (firma?.EntidadFacultad ?? "FACULTAD DE CIENCIAS AGROPECUARIAS").ToUpperInvariant(), 11);
        ExcelEstilos.Titulo(hoja, 2, 1, UltimaColumnaKardex, $"MOVIMIENTOS INVENTARIO GANADO — GRANJA {k.GranjaNombre.ToUpperInvariant()}", 14);
        ExcelEstilos.Titulo(hoja, 3, 1, UltimaColumnaKardex, $"{k.NombreDelMes} {k.Anio}", 12);

        Grupo(hoja, grupos, ColSaldoCant, ColSaldoValor, "Saldo mes anterior");
        Grupo(hoja, grupos, ColVentaNo, ColVentaValor, "Ventas");
        Grupo(hoja, grupos, ColCompraNo, ColCompraValor, "Compras");
        Grupo(hoja, grupos, ColNacimientoNo, ColNacimientoValor, "Nacimientos");
        Grupo(hoja, grupos, ColMuerteNo, ColMuerteCosto, "Muertes / Bajas");
        Grupo(hoja, grupos, ColLlegadoNo, ColLlegadoValor, "Llegados otras granjas");
        Grupo(hoja, grupos, ColTrasladoNo, ColTrasladoValor, "Traslados");
        Grupo(hoja, grupos, ColValorizacion, ColValorizacion, "Valorización");
        Grupo(hoja, grupos, ColFinalCant, ColFinalValor, "Saldo");

        string[] subencabezados =
        [
            "Cant.", "Valor", "No.", "Costo Sistema", "Vr. Venta", "No.", "Costo Sistema", "Vr. Compra", "No.", "Valor",
            "No.", "Costo", "No.", "Valor", "No.", "Valor", "Etapas", "Cant.", "Valor",
        ];
        for (var i = 0; i < subencabezados.Length; i++)
        {
            hoja.Cell(subtitulos, i + 1).Value = subencabezados[i];
        }

        ExcelEstilos.Encabezado(hoja.Range(grupos, 1, subtitulos, UltimaColumnaKardex));

        // Detalle: cada animal en la columna de su movimiento; la primera fila lleva además saldos y valorización.
        var filas = Math.Max(1, new[] { k.Ventas.Count, k.Compras.Count, k.Nacimientos.Count, k.Muertes.Count, k.Llegados.Count, k.Traslados.Count }.Max());
        var ultimaFila = primeraFila + filas - 1;
        var totales = ultimaFila + 1;

        for (var i = 0; i < filas; i++)
        {
            var r = primeraFila + i;
            if (i < k.Ventas.Count)
            {
                ExcelEstilos.Texto(hoja.Cell(r, ColVentaNo), k.Ventas[i].Chapeta);
                hoja.Cell(r, ColVentaCosto).Value = k.Ventas[i].CostoSistema;
                hoja.Cell(r, ColVentaValor).Value = k.Ventas[i].ValorVenta;
            }

            if (i < k.Compras.Count)
            {
                ExcelEstilos.Texto(hoja.Cell(r, ColCompraNo), k.Compras[i].Chapeta);
                hoja.Cell(r, ColCompraCosto).Value = k.Compras[i].CostoSistema;
                hoja.Cell(r, ColCompraValor).Value = k.Compras[i].ValorCompra;
            }

            if (i < k.Nacimientos.Count)
            {
                ExcelEstilos.Texto(hoja.Cell(r, ColNacimientoNo), k.Nacimientos[i].Chapeta);
                hoja.Cell(r, ColNacimientoValor).Value = k.Nacimientos[i].Valor;
            }

            if (i < k.Muertes.Count)
            {
                ExcelEstilos.Texto(hoja.Cell(r, ColMuerteNo), k.Muertes[i].Chapeta);
                hoja.Cell(r, ColMuerteCosto).Value = k.Muertes[i].Valor;
            }

            if (i < k.Llegados.Count)
            {
                ExcelEstilos.Texto(hoja.Cell(r, ColLlegadoNo), k.Llegados[i].Chapeta);
                hoja.Cell(r, ColLlegadoValor).Value = k.Llegados[i].Valor;
            }

            if (i < k.Traslados.Count)
            {
                ExcelEstilos.Texto(hoja.Cell(r, ColTrasladoNo), k.Traslados[i].Chapeta);
                hoja.Cell(r, ColTrasladoValor).Value = k.Traslados[i].Valor;
            }
        }

        hoja.Cell(primeraFila, ColSaldoCant).Value = k.SaldoAnteriorCantidad;
        hoja.Cell(primeraFila, ColSaldoValor).Value = k.SaldoAnteriorValor;
        hoja.Cell(primeraFila, ColValorizacion).Value = k.Valorizacion;
        hoja.Cell(primeraFila, ColFinalCant).Value = k.SaldoFinalCantidad;
        hoja.Cell(primeraFila, ColFinalValor).Value = k.SaldoFinalValor;

        // Totales con fórmulas: el saldo final se recalcula a partir de las columnas, así el libro es auditable en Excel.
        string L(int columna) => XLHelper.GetColumnLetterFromNumber(columna);
        string Rango(int columna) => $"{L(columna)}{primeraFila}:{L(columna)}{ultimaFila}";

        hoja.Cell(totales, ColSaldoCant).FormulaA1 = $"{L(ColSaldoCant)}{primeraFila}";
        hoja.Cell(totales, ColSaldoValor).FormulaA1 = $"{L(ColSaldoValor)}{primeraFila}";
        foreach (var columna in new[] { ColVentaNo, ColCompraNo, ColNacimientoNo, ColMuerteNo, ColLlegadoNo, ColTrasladoNo })
        {
            hoja.Cell(totales, columna).FormulaA1 = $"COUNTA({Rango(columna)})";
        }

        foreach (var columna in new[]
                 {
                     ColVentaCosto, ColVentaValor, ColCompraCosto, ColCompraValor, ColNacimientoValor,
                     ColMuerteCosto, ColLlegadoValor, ColTrasladoValor, ColValorizacion,
                 })
        {
            hoja.Cell(totales, columna).FormulaA1 = $"SUM({Rango(columna)})";
        }

        var t = totales;
        hoja.Cell(t, ColFinalCant).FormulaA1 =
            $"{L(ColSaldoCant)}{t}+{L(ColCompraNo)}{t}+{L(ColNacimientoNo)}{t}+{L(ColLlegadoNo)}{t}" +
            $"-{L(ColVentaNo)}{t}-{L(ColMuerteNo)}{t}-{L(ColTrasladoNo)}{t}";
        hoja.Cell(t, ColFinalValor).FormulaA1 =
            $"{L(ColSaldoValor)}{t}+{L(ColCompraCosto)}{t}+{L(ColNacimientoValor)}{t}+{L(ColLlegadoValor)}{t}" +
            $"-{L(ColVentaCosto)}{t}-{L(ColMuerteCosto)}{t}-{L(ColTrasladoValor)}{t}+{L(ColValorizacion)}{t}";

        // Formato.
        var tabla = hoja.Range(grupos, 1, totales, UltimaColumnaKardex);
        ExcelEstilos.Bordes(tabla);
        ExcelEstilos.Encabezado(hoja.Range(grupos, 1, subtitulos, UltimaColumnaKardex));
        ExcelEstilos.Totales(hoja.Range(totales, 1, totales, UltimaColumnaKardex));

        foreach (var columna in new[] { ColSaldoCant, ColFinalCant })
        {
            hoja.Range(primeraFila, columna, totales, columna).Style.NumberFormat.Format = ExcelEstilos.FormatoEntero;
        }

        foreach (var columna in new[]
                 {
                     ColSaldoValor, ColVentaCosto, ColVentaValor, ColCompraCosto, ColCompraValor, ColNacimientoValor,
                     ColMuerteCosto, ColLlegadoValor, ColTrasladoValor, ColValorizacion, ColFinalValor,
                 })
        {
            hoja.Range(primeraFila, columna, totales, columna).Style.NumberFormat.Format = ExcelEstilos.FormatoMoneda;
        }

        foreach (var columna in new[] { ColVentaNo, ColCompraNo, ColNacimientoNo, ColMuerteNo, ColLlegadoNo, ColTrasladoNo })
        {
            var celda = hoja.Cell(totales, columna);
            celda.Style.NumberFormat.Format = ExcelEstilos.FormatoEntero;
            celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        AnchosDelKardex(hoja);
        hoja.SheetView.FreezeRows(subtitulos);
        ExcelEstilos.ConfigurarImpresion(hoja);

        var siguiente = FirmaBlockWriter.Escribir(hoja, totales + 2, 1, 6, firma);
        PieDeGeneracion(hoja, siguiente + 1);
    }

    private static void AnchosDelKardex(IXLWorksheet hoja)
    {
        double[] anchos = [9, 17, 12, 15, 15, 12, 15, 15, 12, 15, 12, 15, 12, 15, 12, 15, 17, 9, 17];
        for (var i = 0; i < anchos.Length; i++)
        {
            hoja.Column(i + 1).Width = anchos[i];
        }

        hoja.Row(5).Height = 22;
        hoja.Row(6).Height = 22;
    }

    private static void Grupo(IXLWorksheet hoja, int fila, int primera, int ultima, string texto)
    {
        if (ultima > primera)
        {
            hoja.Range(fila, primera, fila, ultima).Merge();
        }

        hoja.Cell(fila, primera).Value = texto;
    }

    // ---- Consolidado general ----------------------------------------------------------------------------------------

    private void EscribirConsolidado(IXLWorksheet hoja, IReadOnlyList<KardexGranjaMensual> granjas, ConfiguracionFirma? firma)
    {
        const int ultima = 20, grupos = 5, subtitulos = 6, primeraFila = 7;
        var primero = granjas[0];

        ExcelEstilos.Titulo(hoja, 1, 1, ultima, (firma?.EntidadFacultad ?? "FACULTAD DE CIENCIAS AGROPECUARIAS").ToUpperInvariant(), 11);
        ExcelEstilos.Titulo(hoja, 2, 1, ultima, "MOVIMIENTOS INVENTARIO GANADO — CONSOLIDADO GENERAL", 14);
        ExcelEstilos.Titulo(hoja, 3, 1, ultima, $"{primero.NombreDelMes} {primero.Anio}", 12);

        Grupo(hoja, grupos, 1, 1, "Granja");
        Grupo(hoja, grupos, 2, 3, "Saldo mes anterior");
        Grupo(hoja, grupos, 4, 6, "Ventas");
        Grupo(hoja, grupos, 7, 9, "Compras");
        Grupo(hoja, grupos, 10, 11, "Nacimientos");
        Grupo(hoja, grupos, 12, 13, "Muertes / Bajas");
        Grupo(hoja, grupos, 14, 15, "Llegados otras granjas");
        Grupo(hoja, grupos, 16, 17, "Traslados");
        Grupo(hoja, grupos, 18, 18, "Valorización");
        Grupo(hoja, grupos, 19, 20, "Saldo");

        string[] sub =
        [
            string.Empty, "Cant.", "Valor", "No.", "Costo Sistema", "Vr. Venta", "No.", "Costo Sistema", "Vr. Compra",
            "No.", "Valor", "No.", "Costo", "No.", "Valor", "No.", "Valor", "Etapas", "Cant.", "Valor",
        ];
        for (var i = 0; i < sub.Length; i++)
        {
            hoja.Cell(subtitulos, i + 1).Value = sub[i];
        }

        for (var i = 0; i < granjas.Count; i++)
        {
            var g = granjas[i];
            var r = primeraFila + i;
            hoja.Cell(r, 1).Value = g.GranjaNombre;
            hoja.Cell(r, 2).Value = g.SaldoAnteriorCantidad;
            hoja.Cell(r, 3).Value = g.SaldoAnteriorValor;
            hoja.Cell(r, 4).Value = g.Ventas.Count;
            hoja.Cell(r, 5).Value = g.Ventas.Sum(v => v.CostoSistema);
            hoja.Cell(r, 6).Value = g.Ventas.Sum(v => v.ValorVenta);
            hoja.Cell(r, 7).Value = g.Compras.Count;
            hoja.Cell(r, 8).Value = g.Compras.Sum(c => c.CostoSistema);
            hoja.Cell(r, 9).Value = g.Compras.Sum(c => c.ValorCompra);
            hoja.Cell(r, 10).Value = g.Nacimientos.Count;
            hoja.Cell(r, 11).Value = g.Nacimientos.Sum(n => n.Valor);
            hoja.Cell(r, 12).Value = g.Muertes.Count;
            hoja.Cell(r, 13).Value = g.Muertes.Sum(m => m.Valor);
            hoja.Cell(r, 14).Value = g.Llegados.Count;
            hoja.Cell(r, 15).Value = g.Llegados.Sum(l => l.Valor);
            hoja.Cell(r, 16).Value = g.Traslados.Count;
            hoja.Cell(r, 17).Value = g.Traslados.Sum(t => t.Valor);
            hoja.Cell(r, 18).Value = g.Valorizacion;
            hoja.Cell(r, 19).Value = g.SaldoFinalCantidad;
            hoja.Cell(r, 20).Value = g.SaldoFinalValor;
        }

        var ultimaFila = primeraFila + granjas.Count - 1;
        var totales = ultimaFila + 1;
        hoja.Cell(totales, 1).Value = "TOTAL GENERAL";
        for (var columna = 2; columna <= ultima; columna++)
        {
            var letra = XLHelper.GetColumnLetterFromNumber(columna);
            hoja.Cell(totales, columna).FormulaA1 = $"SUM({letra}{primeraFila}:{letra}{ultimaFila})";
        }

        ExcelEstilos.Bordes(hoja.Range(grupos, 1, totales, ultima));
        ExcelEstilos.Encabezado(hoja.Range(grupos, 1, subtitulos, ultima));
        ExcelEstilos.Totales(hoja.Range(totales, 1, totales, ultima));

        int[] conteos = [2, 4, 7, 10, 12, 14, 16, 19];
        foreach (var columna in Enumerable.Range(2, ultima - 1))
        {
            hoja.Range(primeraFila, columna, totales, columna).Style.NumberFormat.Format =
                conteos.Contains(columna) ? ExcelEstilos.FormatoEntero : ExcelEstilos.FormatoMoneda;
        }

        hoja.Column(1).Width = 22;
        for (var columna = 2; columna <= ultima; columna++)
        {
            hoja.Column(columna).Width = conteos.Contains(columna) ? 9 : 16;
        }

        hoja.Row(grupos).Height = 22;
        hoja.Row(subtitulos).Height = 22;
        hoja.SheetView.FreezeRows(subtitulos);
        ExcelEstilos.ConfigurarImpresion(hoja);

        var siguiente = FirmaBlockWriter.Escribir(hoja, totales + 2, 1, 5, firma);
        PieDeGeneracion(hoja, siguiente + 1);
    }

    // ---- Inventario físico y financiero -------------------------------------------------------------------------------

    public async Task<string> GenerarInventarioFisicoAsync(InventarioFisicoRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var granjas = (await uow.Granjas.GetAllAsync(ct)).OrderBy(g => g.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id);
        var animales = await uow.Animales.GetAllAsync(ct);
        var movimientos = await uow.Movimientos.GetAllAsync(ct);
        var firma = await uow.Configuracion.GetFirmaAsync(ct);

        if (request.GranjaId is { } granjaId)
        {
            granjas = granjas.Where(g => g.Id == granjaId).ToList();
            Regla.Exigir(granjas.Count == 1, "La granja seleccionada no existe.");
        }

        // Valorización acumulada de cada animal: suma de los deltas de etapa registrados desde su ingreso.
        var variacion = movimientos
            .Where(m => m is { TipoMovimiento: TipoMovimiento.Valorizacion, AnimalId: not null })
            .GroupBy(m => m.AnimalId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.CostoAsociado));

        bool Coincide(Animal a, Granja g)
            => a.GranjaId == g.Id
               && (request.Estado is null || a.Estado == request.Estado)
               && (request.CategoriaId is null || a.CategoriaId == request.CategoriaId)
               && (request.ProgramaId is null || categorias.GetValueOrDefault(a.CategoriaId)?.ProgramaId == request.ProgramaId);

        using var libro = new XLWorkbook();
        var nombres = new NombresDeHoja();
        foreach (var granja in granjas)
        {
            var delaGranja = animales.Where(a => Coincide(a, granja)).ToList();
            if (delaGranja.Count == 0 && request.GranjaId is null && !granja.Activa)
            {
                continue;
            }

            EscribirInventario(libro.Worksheets.Add(nombres.Siguiente(granja.Nombre)), granja, delaGranja, categorias, variacion, request.Estado, firma);
        }

        Regla.Exigir(libro.Worksheets.Count > 0, "No hay granjas para generar el informe.");
        return Guardar(libro, request.RutaDestino);
    }

    private void EscribirInventario(
        IXLWorksheet hoja,
        Granja granja,
        IReadOnlyList<Animal> animales,
        IReadOnlyDictionary<int, CategoriaEtapa> categorias,
        IReadOnlyDictionary<int, decimal> variacion,
        string? estado,
        ConfiguracionFirma? firma)
    {
        const int ultima = 8;
        ExcelEstilos.Titulo(hoja, 1, 1, ultima, (firma?.EntidadFacultad ?? "FACULTAD DE CIENCIAS AGROPECUARIAS").ToUpperInvariant(), 11);
        ExcelEstilos.Titulo(hoja, 2, 1, ultima, $"INVENTARIO FÍSICO - FINANCIERO DE SEMOVIENTES — GRANJA {granja.Nombre.ToUpperInvariant()}", 14);
        ExcelEstilos.Titulo(
            hoja, 3, 1, ultima,
            $"Estado: {estado ?? "todos"} · Corte al {reloj.Hoy.ToString("dd/MM/yyyy", Cultura)}", 11, negrita: false);

        string[] encabezados =
        [
            "Clase de entrada", "Fecha de nacimiento", "Sexo", "Chapeta amarilla", "Chapeta roja",
            "Precio animal (costo de compra)", "Precio sistema (categoría actual)", "Valorización acumulada",
        ];
        const int filaEncabezado = 5;
        for (var i = 0; i < encabezados.Length; i++)
        {
            hoja.Cell(filaEncabezado, i + 1).Value = encabezados[i];
        }

        ExcelEstilos.Encabezado(hoja.Range(filaEncabezado, 1, filaEncabezado, ultima));
        hoja.Row(filaEncabezado).Height = 32;

        var fila = filaEncabezado + 1;
        var subtotales = new List<int>();
        if (animales.Count == 0)
        {
            hoja.Range(fila, 1, fila, ultima).Merge();
            hoja.Cell(fila, 1).Value = "Sin semovientes para los filtros seleccionados.";
            hoja.Cell(fila, 1).Style.Font.Italic = true;
            fila++;
        }

        foreach (var grupo in animales.GroupBy(a => a.CategoriaId).OrderBy(g => g.Key))
        {
            var nombreCategoria = categorias.TryGetValue(grupo.Key, out var c) ? c.Nombre : $"Categoría {grupo.Key}";
            var cabecera = hoja.Range(fila, 1, fila, ultima);
            cabecera.Merge();
            cabecera.Style.Fill.BackgroundColor = ExcelEstilos.GrisSuave;
            cabecera.Style.Font.Bold = true;
            hoja.Cell(fila, 1).Value = nombreCategoria.ToUpperInvariant();
            fila++;

            var primeraDelGrupo = fila;
            foreach (var animal in grupo.OrderBy(a => a.ChapetaAmarilla, StringComparer.OrdinalIgnoreCase))
            {
                hoja.Cell(fila, 1).Value = animal.ClaseEntrada;
                hoja.Cell(fila, 2).Value = animal.FechaNacimiento.Date;
                hoja.Cell(fila, 2).Style.NumberFormat.Format = ExcelEstilos.FormatoFecha;
                hoja.Cell(fila, 3).Value = animal.Sexo;
                ExcelEstilos.Texto(hoja.Cell(fila, 4), animal.ChapetaAmarilla);
                if (!string.IsNullOrWhiteSpace(animal.ChapetaRoja))
                {
                    ExcelEstilos.Texto(hoja.Cell(fila, 5), animal.ChapetaRoja);
                }

                hoja.Cell(fila, 6).Value = animal.ValorCompraOrigen;
                hoja.Cell(fila, 7).Value = animal.ValorContableActual;
                hoja.Cell(fila, 8).Value = variacion.GetValueOrDefault(animal.Id);
                fila++;
            }

            var ultimaDelGrupo = fila - 1;
            hoja.Cell(fila, 1).Value = $"Subtotal {nombreCategoria}";
            hoja.Cell(fila, 2).FormulaA1 = $"COUNTA(D{primeraDelGrupo}:D{ultimaDelGrupo})";
            hoja.Cell(fila, 2).Style.NumberFormat.Format = ExcelEstilos.FormatoCabezas;
            foreach (var columna in new[] { 6, 7, 8 })
            {
                var letra = XLHelper.GetColumnLetterFromNumber(columna);
                hoja.Cell(fila, columna).FormulaA1 = $"SUM({letra}{primeraDelGrupo}:{letra}{ultimaDelGrupo})";
            }

            var filaSubtotal = hoja.Range(fila, 1, fila, ultima);
            filaSubtotal.Style.Font.Bold = true;
            filaSubtotal.Style.Fill.BackgroundColor = ExcelEstilos.AzulClaro;
            subtotales.Add(fila);
            fila++;
        }

        var ultimaTabla = fila - 1;
        if (subtotales.Count > 0)
        {
            hoja.Cell(fila, 1).Value = $"TOTAL SEDE {granja.Nombre.ToUpperInvariant()}";
            hoja.Cell(fila, 2).FormulaA1 = string.Join("+", subtotales.Select(s => $"B{s}"));
            hoja.Cell(fila, 2).Style.NumberFormat.Format = ExcelEstilos.FormatoSemovientes;
            foreach (var columna in new[] { 6, 7, 8 })
            {
                var letra = XLHelper.GetColumnLetterFromNumber(columna);
                hoja.Cell(fila, columna).FormulaA1 = string.Join("+", subtotales.Select(s => $"{letra}{s}"));
            }

            ExcelEstilos.Totales(hoja.Range(fila, 1, fila, ultima));
            ultimaTabla = fila;
        }

        ExcelEstilos.Bordes(hoja.Range(filaEncabezado, 1, ultimaTabla, ultima));
        ExcelEstilos.Encabezado(hoja.Range(filaEncabezado, 1, filaEncabezado, ultima));
        foreach (var columna in new[] { 6, 7, 8 })
        {
            hoja.Range(filaEncabezado + 1, columna, ultimaTabla, columna).Style.NumberFormat.Format = ExcelEstilos.FormatoMoneda;
        }

        double[] anchos = [30, 18, 10, 17, 15, 22, 22, 20];
        for (var i = 0; i < anchos.Length; i++)
        {
            hoja.Column(i + 1).Width = anchos[i];
        }

        hoja.SheetView.FreezeRows(filaEncabezado);
        ExcelEstilos.ConfigurarImpresion(hoja);

        var siguiente = FirmaBlockWriter.Escribir(hoja, ultimaTabla + 2, 1, 4, firma);
        PieDeGeneracion(hoja, siguiente + 1);
    }

    // ---- Hoja de vida -------------------------------------------------------------------------------------------------

    public async Task<string> GenerarHojaDeVidaAsync(int animalId, string rutaDestino, CancellationToken ct = default)
    {
        var ficha = await fichas.ObtenerAsync(animalId, ct);
        var firma = await LeerFirmaAsync(ct);
        var a = ficha.Animal;

        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Hoja de vida");
        const int ultima = 7;

        ExcelEstilos.Titulo(hoja, 1, 1, ultima, (firma?.EntidadFacultad ?? "FACULTAD DE CIENCIAS AGROPECUARIAS").ToUpperInvariant(), 11);
        ExcelEstilos.Titulo(hoja, 2, 1, ultima, "HOJA DE VIDA DEL BOVINO", 14);
        ExcelEstilos.Titulo(
            hoja, 3, 1, ultima,
            string.IsNullOrWhiteSpace(a.Nombre) ? a.ChapetaAmarilla : $"{a.ChapetaAmarilla} — {a.Nombre}", 12);

        var fila = 5;
        fila = Seccion(hoja, fila, ultima, "Datos básicos");
        fila = Par(hoja, fila, "Chapeta amarilla", a.ChapetaAmarilla, "Chapeta roja", a.ChapetaRoja);
        fila = Par(hoja, fila, "Sexo", a.Sexo, "Raza", a.Raza);
        fila = Par(hoja, fila, "Fecha de nacimiento", a.FechaNacimiento.Date, "Edad (meses)", ficha.EdadMeses);
        fila = Par(hoja, fila, "Orientación productiva", ficha.OrientacionNombre, "Ubicación actual", ficha.GranjaNombre);
        fila = Par(hoja, fila, "Peso actual (kg)", a.PesoActualKg, "Estado", a.Estado);
        fila = Par(hoja, fila, "Clase de entrada", a.ClaseEntrada, "Fecha de compra", a.FechaCompra?.Date);
        fila += 1;

        fila = Seccion(hoja, fila, ultima, "Valorización");
        fila = Par(hoja, fila, "Valor compra (origen, inmutable)", a.ValorCompraOrigen, "Valor contable actual", a.ValorContableActual, ExcelEstilos.FormatoMonedaConCero, ExcelEstilos.FormatoMonedaConCero);
        fila = Par(hoja, fila, "Categoría", ficha.CategoriaNombre, "Valorización acumulada", ficha.VariacionAcumulada, null, ExcelEstilos.FormatoMonedaConCero);
        fila = Par(hoja, fila, "Partos", a.NumeroPartos, "Exento de valorización masiva", a.ExentoValorizacion ? "Sí" : "No");
        fila += 1;

        fila = Seccion(hoja, fila, ultima, "Genealogía");
        var concepcion = ficha.Genealogia is null ? "—" : ficha.Genealogia.EsInseminacion ? "Inseminación artificial" : "Monta directa";
        var padre = ficha.Genealogia is null
            ? "—"
            : ficha.Genealogia.EsInseminacion
                ? $"{ficha.Genealogia.NombrePadreInseminacion ?? "(sin nombre)"} · raza {ficha.Genealogia.RazaPadreInseminacion ?? "—"}"
                : ficha.PadreChapeta ?? "—";
        fila = Par(hoja, fila, "Madre", a.MadreIdentificacion ?? "—", "Tipo de concepción", concepcion);
        fila = Par(hoja, fila, "Padre", padre, string.Empty, string.Empty);
        fila += 1;

        fila = Seccion(hoja, fila, ultima, "Historial");
        string[] encabezados = ["Fecha", "Evento", "Granja origen", "Granja destino", "N.º de remisión", "Valor", "Detalle"];
        for (var i = 0; i < encabezados.Length; i++)
        {
            hoja.Cell(fila, i + 1).Value = encabezados[i];
        }

        ExcelEstilos.Encabezado(hoja.Range(fila, 1, fila, ultima));
        var primera = fila + 1;
        fila = primera;
        foreach (var evento in ficha.Historial)
        {
            hoja.Cell(fila, 1).Value = evento.Fecha.Date;
            hoja.Cell(fila, 1).Style.NumberFormat.Format = ExcelEstilos.FormatoFecha;
            hoja.Cell(fila, 2).Value = evento.Tipo;
            hoja.Cell(fila, 3).Value = evento.GranjaOrigen ?? string.Empty;
            hoja.Cell(fila, 4).Value = evento.GranjaDestino ?? string.Empty;
            ExcelEstilos.Texto(hoja.Cell(fila, 5), evento.NumeroRemision ?? string.Empty);
            if (evento.Valor is { } valor)
            {
                hoja.Cell(fila, 6).Value = valor;
                hoja.Cell(fila, 6).Style.NumberFormat.Format = ExcelEstilos.FormatoMoneda;
            }

            hoja.Cell(fila, 7).Value = evento.Descripcion;
            fila++;
        }

        ExcelEstilos.Bordes(hoja.Range(primera - 1, 1, Math.Max(fila - 1, primera), ultima));

        double[] anchos = [36, 22, 18, 30, 18, 18, 46];
        for (var i = 0; i < anchos.Length; i++)
        {
            hoja.Column(i + 1).Width = anchos[i];
        }

        ExcelEstilos.ConfigurarImpresion(hoja);
        var siguiente = FirmaBlockWriter.Escribir(hoja, fila + 1, 1, 3, firma);
        PieDeGeneracion(hoja, siguiente + 1);

        return Guardar(libro, rutaDestino);
    }

    private static int Seccion(IXLWorksheet hoja, int fila, int ultima, string titulo)
    {
        var rango = hoja.Range(fila, 1, fila, ultima);
        rango.Merge();
        rango.Style.Fill.BackgroundColor = ExcelEstilos.AzulInstitucional;
        rango.Style.Font.FontColor = XLColor.White;
        rango.Style.Font.Bold = true;
        hoja.Cell(fila, 1).Value = titulo;
        return fila + 1;
    }

    /// <summary>
    /// Dos pares etiqueta/valor en una fila (columnas A-B y D-E). Los números y fechas se escriben como valores reales
    /// de Excel (no como texto) con el formato indicado.
    /// </summary>
    private static int Par(
        IXLWorksheet hoja, int fila, string etiqueta1, object? valor1, string etiqueta2, object? valor2,
        string? formato1 = null, string? formato2 = null)
    {
        hoja.Cell(fila, 1).Value = etiqueta1;
        hoja.Cell(fila, 1).Style.Font.Bold = true;
        EscribirValor(hoja.Cell(fila, 2), valor1, formato1);
        hoja.Range(fila, 2, fila, 3).Merge();
        if (!string.IsNullOrEmpty(etiqueta2))
        {
            hoja.Cell(fila, 4).Value = etiqueta2;
            hoja.Cell(fila, 4).Style.Font.Bold = true;
            EscribirValor(hoja.Cell(fila, 5), valor2, formato2);
            hoja.Range(fila, 5, fila, 7).Merge();
        }

        return fila + 1;
    }

    private static void EscribirValor(IXLCell celda, object? valor, string? formato)
    {
        switch (valor)
        {
            case null:
                ExcelEstilos.Texto(celda, "—");
                break;
            case string texto:
                ExcelEstilos.Texto(celda, string.IsNullOrWhiteSpace(texto) ? "—" : texto);
                break;
            case DateTime fecha:
                celda.Value = fecha;
                celda.Style.NumberFormat.Format = formato ?? ExcelEstilos.FormatoFecha;
                break;
            case int entero:
                celda.Value = entero;
                celda.Style.NumberFormat.Format = formato ?? "#,##0";
                break;
            case decimal numero:
                celda.Value = numero;
                // Dos decimales fijos: "#,##0.##" deja el separador colgando en los enteros ("400,").
                celda.Style.NumberFormat.Format = formato ?? "#,##0.00";
                break;
            default:
                ExcelEstilos.Texto(celda, valor.ToString() ?? "—");
                break;
        }

        celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
    }

    // ---- Comunes ------------------------------------------------------------------------------------------------------

    private async Task<ConfiguracionFirma?> LeerFirmaAsync(CancellationToken ct)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return await uow.Configuracion.GetFirmaAsync(ct);
    }

    private void PieDeGeneracion(IXLWorksheet hoja, int fila)
    {
        var celda = hoja.Cell(fila, 1);
        celda.Value = $"Generado el {reloj.Ahora.ToString("dd/MM/yyyy HH:mm", Cultura)} · Sistema Unificado de Gestión de Granjas";
        celda.Style.Font.FontSize = 8;
        celda.Style.Font.Italic = true;
        celda.Style.Font.FontColor = ExcelEstilos.GrisTexto;
    }

    private static string Guardar(XLWorkbook libro, string rutaDestino)
    {
        Regla.ExigirTexto(rutaDestino, "La ruta del archivo");
        var destino = Path.GetFullPath(Path.HasExtension(rutaDestino) ? rutaDestino : rutaDestino + ".xlsx");
        Regla.Exigir(destino.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase), "El informe debe guardarse como archivo .xlsx.");

        // Se serializa en memoria y se escribe un temporal que luego reemplaza al destino: si algo falla (disco lleno,
        // archivo abierto en Excel) el informe anterior queda intacto y no se deja un archivo a medias.
        var temporal = destino + ".tmp";
        try
        {
            libro.RecalculateAllFormulas();
            using var memoria = new MemoryStream();
            libro.SaveAs(memoria);
            var paquete = ExcelPaquete.SuprimirAvisosDeCelda(memoria.ToArray());

            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.WriteAllBytes(temporal, paquete);
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

            throw Regla.Violada(
                $"No se pudo guardar el informe en «{destino}». Si el archivo está abierto en Excel, ciérrelo e inténtelo de nuevo. ({ex.Message})");
        }

        return destino;
    }

    /// <summary>Nombres de pestaña válidos para Excel: sin caracteres prohibidos, máximo 31 y sin repetirse.</summary>
    private sealed class NombresDeHoja
    {
        private readonly HashSet<string> _usados = new(StringComparer.OrdinalIgnoreCase);

        public string Siguiente(string deseado)
        {
            var limpio = new string(deseado.Select(c => "[]:*?/\\".Contains(c) ? '-' : c).ToArray()).Trim();
            if (limpio.Length == 0)
            {
                limpio = "Hoja";
            }

            limpio = limpio.Length > 31 ? limpio[..31] : limpio;
            var candidato = limpio;
            for (var n = 2; !_usados.Add(candidato); n++)
            {
                var sufijo = $" ({n})";
                candidato = limpio[..Math.Min(limpio.Length, 31 - sufijo.Length)] + sufijo;
            }

            return candidato;
        }
    }
}
