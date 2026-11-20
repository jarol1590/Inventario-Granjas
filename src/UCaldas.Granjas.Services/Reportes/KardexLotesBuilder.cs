using System.Globalization;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Reportes;

/// <summary>Línea de venta de un lote tomada de su remisión (no anulada).</summary>
internal sealed record VentaDeLote(int LoteId, DateTime Fecha, int Cantidad, decimal CostoEnLibros, decimal ValorVenta);

/// <summary>Una venta del mes dentro del bloque del lote: costo en libros y valor comercial, totales y por cabeza.</summary>
internal sealed record FilaVentaLote(int Cantidad, decimal CostoUnitario, decimal CostoTotal, decimal ValorVentaUnitario, decimal ValorVentaTotal);

/// <summary>Bloque mensual de un lote en el Kardex de lotes (una tabla de 13 columnas en el Excel).</summary>
internal sealed record BloqueLote(
    LotePoblacion Lote,
    IReadOnlyList<string> Cabecera,
    int VienenCantidad,
    decimal VienenValor,
    IReadOnlyList<FilaVentaLote> Ventas,
    int MuertesCantidad,
    decimal MuertesValor,
    decimal Valorizacion);

/// <summary>
/// Arma los bloques del Kardex mensual de lotes. El saldo «Vienen» (cierre del mes anterior) se reconstruye hacia atrás desde
/// el estado actual del lote deshaciendo lo que ocurrió después del mes: así los lotes anteriores a los asientos de lote
/// arrancan desde su saldo actual y la historia fechada (bajas, valorizaciones, ventas) cuenta desde que se registra.
/// Los traslados parciales, los desdobles y las correcciones manuales de la ficha no tienen columna ni fecha propia.
/// </summary>
internal static class KardexLotesBuilder
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    public static IReadOnlyList<BloqueLote> Construir(
        int anio,
        int mes,
        IEnumerable<LotePoblacion> lotes,
        IReadOnlyList<MovimientoInventario> movimientos,
        IReadOnlyList<VentaDeLote> ventas)
    {
        var inicio = new DateTime(anio, mes, 1);
        var fin = inicio.AddMonths(1).AddDays(-1);
        var bloques = new List<BloqueLote>();

        foreach (var lote in lotes.Where(l => !l.Anulado && l.FechaNacimientoIngreso.Date <= fin).OrderBy(l => l.CodigoLote, StringComparer.OrdinalIgnoreCase))
        {
            var movs = movimientos.Where(m => m.LoteId == lote.Id).ToList();
            var bajas = movs.Where(m => m.TipoMovimiento is TipoMovimiento.Muerte or TipoMovimiento.Hurto).ToList();
            var valorizaciones = movs.Where(m => m.TipoMovimiento == TipoMovimiento.Valorizacion).ToList();
            var ventasLote = ventas.Where(v => v.LoteId == lote.Id).ToList();

            var bajasMes = bajas.Where(m => EnMes(m.FechaMovimiento, inicio, fin)).ToList();
            var valorizacionesMes = valorizaciones.Where(m => EnMes(m.FechaMovimiento, inicio, fin)).ToList();
            var ventasMes = ventasLote.Where(v => EnMes(v.Fecha, inicio, fin)).ToList();

            var bajasDespues = bajas.Where(m => m.FechaMovimiento.Date > fin).ToList();
            var valorizacionesDespues = valorizaciones.Where(m => m.FechaMovimiento.Date > fin).ToList();
            var ventasDespues = ventasLote.Where(v => v.Fecha.Date > fin).ToList();

            // Cierre del mes = estado actual + lo que salió después − lo que se valorizó después.
            var cantidadCierre = lote.CantidadActual + ventasDespues.Sum(v => v.Cantidad) + bajasDespues.Sum(m => m.Cantidad);
            var valorCierre = lote.ValorTotalContable
                              + ventasDespues.Sum(v => v.CostoEnLibros)
                              + bajasDespues.Sum(m => -m.CostoAsociado)
                              - valorizacionesDespues.Sum(m => m.CostoAsociado);

            var muertesCantidad = bajasMes.Sum(m => m.Cantidad);
            var muertesValor = bajasMes.Sum(m => -m.CostoAsociado);
            var valorizacion = valorizacionesMes.Sum(m => m.CostoAsociado);

            var vienenCantidad = cantidadCierre + ventasMes.Sum(v => v.Cantidad) + muertesCantidad;
            var vienenValor = valorCierre + ventasMes.Sum(v => v.CostoEnLibros) + muertesValor - valorizacion;

            var hayMovimiento = bajasMes.Count > 0 || valorizacionesMes.Count > 0 || ventasMes.Count > 0;
            if (!hayMovimiento && vienenCantidad == 0 && vienenValor == 0)
            {
                continue;   // lote ya cerrado antes del mes, sin saldo ni movimiento
            }

            bloques.Add(new BloqueLote(
                lote,
                Cabecera(lote),
                vienenCantidad,
                vienenValor,
                ventasMes.OrderBy(v => v.Fecha).Select(Fila).ToList(),
                muertesCantidad,
                muertesValor,
                valorizacion));
        }

        return bloques;
    }

    /// <summary>«COMPRADAS SEPTIEMBRE 26 DE 2024», línea o raza y «LOTE # código».</summary>
    internal static IReadOnlyList<string> Cabecera(LotePoblacion lote)
    {
        var verbo = lote.ClaseEntrada switch
        {
            ClaseEntrada.Compra => "COMPRADAS",
            ClaseEntrada.Nacimiento => "NACIDAS",
            ClaseEntrada.Desdoble => "DESDOBLADAS",
            _ => "TRASLADADAS",
        };
        var fecha = lote.FechaNacimientoIngreso.Date;
        var enLetras = $"{fecha.ToString("MMMM", Cultura).ToUpperInvariant()} {fecha.Day} DE {fecha.Year}";
        return [$"{verbo} {enLetras}", lote.LineaRaza.ToUpperInvariant(), $"LOTE # {lote.CodigoLote}"];
    }

    private static FilaVentaLote Fila(VentaDeLote v)
        => v.Cantidad > 0
            ? new FilaVentaLote(v.Cantidad, v.CostoEnLibros / v.Cantidad, v.CostoEnLibros, v.ValorVenta / v.Cantidad, v.ValorVenta)
            : new FilaVentaLote(0, 0m, v.CostoEnLibros, 0m, v.ValorVenta);   // lote sin conteo: solo valores

    private static bool EnMes(DateTime fecha, DateTime inicio, DateTime fin) => fecha.Date >= inicio && fecha.Date <= fin;
}
