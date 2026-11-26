namespace UCaldas.Granjas.Domain.Entities;

/// <summary>Oficio de venta: documento formal asignado a un solo programa productivo (ej. OFGAN-26001).</summary>
public class OficioVenta
{
    public int Id { get; set; }
    public string NumeroOficio { get; set; } = string.Empty;
    public int ProgramaId { get; set; }
    public DateTime FechaOficio { get; set; }
    public string? Observaciones { get; set; }

    /// <summary>Un oficio cerrado no admite remisiones nuevas; se puede reabrir (extensión v2.8).</summary>
    public bool Cerrado { get; set; }

    public DateTime? FechaCierre { get; set; }
}

/// <summary>
/// Remisión: documento universal de movimiento (VENTA, TRASLADO, COMPRA, BAJA).
/// Solo la VENTA lleva Oficio y Recibo de Caja; el resto los rechaza (también lo garantiza la base de datos).
/// En una COMPRA, <see cref="NumeroRemision"/> es el número de factura / remisión del proveedor.
/// </summary>
public class Remision
{
    public int Id { get; set; }
    public string NumeroRemision { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty;
    public int? OficioId { get; set; }
    public string? ReciboCaja { get; set; }
    public int? GranjaOrigenId { get; set; }
    public int? GranjaDestinoId { get; set; }
    public DateTime FechaRemision { get; set; }

    /// <summary>Comprador o proveedor.</summary>
    public string? TerceroEntidad { get; set; }

    /// <summary>Observación general del documento (no confundir con las observaciones específicas de cada animal).</summary>
    public string? Observaciones { get; set; }

    /// <summary>Orden de compra (solo COMPRA; extensión v2.1).</summary>
    public string? OrdenCompra { get; set; }

    /// <summary>NIT del proveedor (solo COMPRA; extensión v2.1).</summary>
    public string? TerceroNit { get; set; }

    /// <summary>VENTA, MUERTE, HURTO, DESCARTE_SANITARIO o TRASLADO (extensión v2.1).</summary>
    public string? ClaseSalida { get; set; }

    /// <summary>Documento anulado: conserva su número pero revirtió todos sus efectos (extensión v2.1).</summary>
    public bool Anulada { get; set; }

    /// <summary>Programa de la remisión: el del oficio en una venta, el elegido en un traslado (extensión v2.8).</summary>
    public int? ProgramaId { get; set; }

    /// <summary>CONVENIO u OTRO (solo ventas; extensión v2.8).</summary>
    public string? TipoComprador { get; set; }

    public int? ConvenioId { get; set; }
    public int? ClienteId { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DescuentoPorcentaje { get; set; }
    public decimal ValorDescuento { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Línea de una remisión: un animal (o lote) con su peso, valor de la transacción y costo en libros.</summary>
public class RemisionDetalle
{
    public int Id { get; set; }
    public int RemisionId { get; set; }
    public int? AnimalId { get; set; }
    public int? LoteId { get; set; }
    public decimal? PesoMomentoKg { get; set; }

    /// <summary>Precio real pactado (venta) o de factura (compra).</summary>
    public decimal? ValorUnitarioTransaccion { get; set; }

    public decimal? ValorTotalTransaccion { get; set; }

    /// <summary>Valor en libros del animal en el momento del movimiento ("Costo Sistema").</summary>
    public decimal CostoSistemaLibros { get; set; }

    public string? ObservacionesEspecificas { get; set; }

    /// <summary>Peso en báscula de una venta por kilogramo (extensión v2.3).</summary>
    public decimal? PesoVendidoKg { get; set; }

    /// <summary>1 en animales; N en lotes (extensión v2.3).</summary>
    public int Cantidad { get; set; } = 1;

    /// <summary>Lote creado por un traslado parcial de lotes (extensión v2.4).</summary>
    public int? LoteDestinoId { get; set; }

    /// <summary>Orientación antes del traslado (extensión v2.1).</summary>
    public int? OrientacionOrigenId { get; set; }

    /// <summary>Orientación después del traslado (extensión v2.1).</summary>
    public int? OrientacionDestinoId { get; set; }

    /// <summary>Línea de producto (hortalizas, huevos...) en lugar de animal o lote (extensión v2.8).</summary>
    public int? ProductoId { get; set; }

    public string? DescripcionItem { get; set; }
    public string? UnidadMedida { get; set; }

    /// <summary>Cantidad de la línea de producto (admite decimales: kg, cubetas).</summary>
    public decimal? CantidadProducto { get; set; }
}

/// <summary>
/// Fila del libro del Kardex. <see cref="CostoAsociado"/> lleva signo (+entradas, −salidas, ±valorización).
/// Exactamente una de las dos granjas está informada: es la granja cuyo saldo afecta el movimiento.
/// </summary>
public class MovimientoInventario
{
    public int Id { get; set; }
    public int? AnimalId { get; set; }
    public int? LoteId { get; set; }
    public int? RemisionId { get; set; }
    public string TipoMovimiento { get; set; } = string.Empty;
    public DateTime FechaMovimiento { get; set; }
    public int? GranjaOrigenId { get; set; }
    public int? GranjaDestinoId { get; set; }
    public decimal CostoAsociado { get; set; }

    /// <summary>1 en animales; N en lotes (extensión v2.3).</summary>
    public int Cantidad { get; set; } = 1;

    /// <summary>Motivo del asiento (extensión v2.7); p. ej. «Valorización por cambio de etapa» en un lote.</summary>
    public string? Observaciones { get; set; }
}
