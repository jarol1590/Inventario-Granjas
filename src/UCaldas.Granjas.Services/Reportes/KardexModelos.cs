namespace UCaldas.Granjas.Services.Reportes;

/// <summary>Línea de ventas: costo en libros ("Costo Sistema") y valor real de la venta.</summary>
public sealed record LineaVenta(string Chapeta, decimal CostoSistema, decimal ValorVenta);

/// <summary>Línea de compras: costo en libros ("Costo Sistema") y valor de la factura.</summary>
public sealed record LineaCompra(string Chapeta, decimal CostoSistema, decimal ValorCompra);

/// <summary>Línea de un movimiento valorado en un solo monto (nacimiento, muerte, traslado).</summary>
public sealed record LineaValor(string Chapeta, decimal Valor);

/// <summary>
/// Kardex financiero de una granja en un mes, con la estructura del informe institucional: saldo del mes anterior,
/// entradas (compras, nacimientos, llegados de otras granjas), salidas (ventas, muertes/bajas, traslados),
/// valorización por etapas y saldo final. Todos los valores salen del libro de movimientos.
/// </summary>
public sealed class KardexGranjaMensual
{
    public required int GranjaId { get; init; }

    public required string GranjaNombre { get; init; }

    public required int Anio { get; init; }

    public required int Mes { get; init; }

    public int SaldoAnteriorCantidad { get; init; }

    public decimal SaldoAnteriorValor { get; init; }

    public IReadOnlyList<LineaVenta> Ventas { get; init; } = [];

    public IReadOnlyList<LineaCompra> Compras { get; init; } = [];

    public IReadOnlyList<LineaValor> Nacimientos { get; init; } = [];

    /// <summary>Muertes, hurtos y descartes sanitarios.</summary>
    public IReadOnlyList<LineaValor> Muertes { get; init; } = [];

    /// <summary>Entradas por traslado desde otras granjas.</summary>
    public IReadOnlyList<LineaValor> Llegados { get; init; } = [];

    /// <summary>Salidas por traslado hacia otras granjas.</summary>
    public IReadOnlyList<LineaValor> Traslados { get; init; } = [];

    /// <summary>Suma de las valorizaciones por etapa del mes (puede ser negativa).</summary>
    public decimal Valorizacion { get; init; }

    public int SaldoFinalCantidad { get; init; }

    public decimal SaldoFinalValor { get; init; }

    public string NombreDelMes => new DateTime(Anio, Mes, 1).ToString("MMMM", System.Globalization.CultureInfo.GetCultureInfo("es-CO")).ToUpperInvariant();
}
