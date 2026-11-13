namespace UCaldas.Granjas.Services.Kardex;

// ---------------------------------------------------------------------------------------------------------------
// Comandos de entrada del KardexService. Las propiedades RemisionId / AnimalId solo se informan al EDITAR un documento.
// ---------------------------------------------------------------------------------------------------------------

/// <summary>Nacimiento de una cría en una granja de la Universidad.</summary>
public sealed class NacimientoDto
{
    public int GranjaId { get; set; }

    public DateTime FechaNacimiento { get; set; }

    public string ChapetaAmarilla { get; set; } = string.Empty;

    public string? ChapetaRoja { get; set; }

    public string? Nombre { get; set; }

    public string Sexo { get; set; } = string.Empty;

    public string Raza { get; set; } = string.Empty;

    /// <summary>Puede ser 0 en programas sin valorización biológica (la orientación solo aplica a bovinos).</summary>
    public int OrientacionId { get; set; }

    /// <summary>Categoría elegida en un programa sin valorización biológica (p. ej. "General"); nulo en bovinos.</summary>
    public int? CategoriaId { get; set; }

    public decimal PesoAlNacerKg { get; set; }

    /// <summary>Chapeta de la madre. Si existe en el sistema se valida y se le suma un parto.</summary>
    public string MadreIdentificacion { get; set; } = string.Empty;

    /// <summary>true = inseminación artificial (exige <see cref="RazaPadre"/>); false = monta directa.</summary>
    public bool EsInseminacion { get; set; }

    public string? RazaPadre { get; set; }

    public string? NombrePadre { get; set; }

    /// <summary>Monta directa: chapeta del reproductor (debe estar registrado). Opcional.</summary>
    public string? PadreChapeta { get; set; }
}

public sealed record NacimientoResultado(int AnimalId, string Categoria, decimal ValorInicial, bool SinTarifa, bool MadreActualizada);

public sealed class CompraAnimalDto
{
    /// <summary>Solo al editar: animal ya registrado por esta compra.</summary>
    public int? AnimalId { get; set; }

    public string ChapetaAmarilla { get; set; } = string.Empty;

    public string? ChapetaRoja { get; set; }

    public string? Nombre { get; set; }

    public string Sexo { get; set; } = string.Empty;

    public DateTime FechaNacimiento { get; set; }

    public string Raza { get; set; } = string.Empty;

    public decimal PesoKg { get; set; }

    public int OrientacionId { get; set; }

    /// <summary>Categoría elegida en un programa sin valorización biológica (p. ej. "General"); nulo en bovinos.</summary>
    public int? CategoriaId { get; set; }

    public int NumeroPartos { get; set; }

    /// <summary>Precio comercial de la factura: se guarda como <c>valor_compra_origen</c>.</summary>
    public decimal PrecioFactura { get; set; }

    public string? Observaciones { get; set; }
}

/// <summary>Compra de uno o varios animales amparada por una factura / remisión del proveedor.</summary>
public sealed class CompraDto
{
    /// <summary>Solo al editar.</summary>
    public int? RemisionId { get; set; }

    /// <summary>Número de factura / remisión de compra: se guarda como número de la remisión.</summary>
    public string NumeroFactura { get; set; } = string.Empty;

    public string? OrdenCompra { get; set; }

    public DateTime FechaCompra { get; set; }

    public int GranjaDestinoId { get; set; }

    public string Proveedor { get; set; } = string.Empty;

    public string? ProveedorNit { get; set; }

    public string? Observaciones { get; set; }

    public List<CompraAnimalDto> Animales { get; set; } = [];
}

public sealed record CompraResultado(
    int RemisionId,
    IReadOnlyList<int> AnimalIds,
    decimal TotalFactura,
    decimal TotalCostoSistema,
    IReadOnlyList<string> SinTarifa);

public sealed class VentaAnimalDto
{
    public int AnimalId { get; set; }

    /// <summary>Precio real pactado por el animal ("Valor Venta Real").</summary>
    public decimal PrecioVenta { get; set; }

    public decimal? PesoKg { get; set; }

    /// <summary>Observaciones del animal (distintas de la observación general del documento).</summary>
    public string? ObservacionesEspecificas { get; set; }
}

/// <summary>
/// Venta: Oficio de Venta (un solo programa) → Remisión → Recibo de Caja. Si el oficio ya existe se reutiliza
/// (un oficio agrupa varias remisiones); si no, se crea con <see cref="FechaOficio"/>.
/// </summary>
public sealed class VentaDto
{
    public int? RemisionId { get; set; }

    public string NumeroOficio { get; set; } = string.Empty;

    public DateTime FechaOficio { get; set; }

    public int ProgramaId { get; set; }

    public string? ObservacionesOficio { get; set; }

    public string NumeroRemision { get; set; } = string.Empty;

    public DateTime FechaRemision { get; set; }

    public string ReciboCaja { get; set; } = string.Empty;

    public int GranjaOrigenId { get; set; }

    public string Comprador { get; set; } = string.Empty;

    /// <summary>Observación general de la salida (a nivel de documento).</summary>
    public string? Observaciones { get; set; }

    public List<VentaAnimalDto> Animales { get; set; } = [];
}

public sealed class BajaAnimalDto
{
    public int AnimalId { get; set; }

    public string? ObservacionesEspecificas { get; set; }
}

/// <summary>Baja por muerte, hurto o descarte sanitario. Solo lleva Remisión: sin Oficio ni Recibo de Caja.</summary>
public sealed class BajaDto
{
    public int? RemisionId { get; set; }

    public string NumeroRemision { get; set; } = string.Empty;

    public DateTime FechaBaja { get; set; }

    public int GranjaOrigenId { get; set; }

    /// <summary>MUERTE, HURTO o DESCARTE_SANITARIO (ver <c>ClaseSalida.Bajas</c>).</summary>
    public string ClaseSalida { get; set; } = string.Empty;

    /// <summary>Acta sanitaria, denuncia u otro documento de soporte.</summary>
    public string DocumentoSoporte { get; set; } = string.Empty;

    public string? Observaciones { get; set; }

    public List<BajaAnimalDto> Animales { get; set; } = [];
}

public sealed record SalidaResultado(int RemisionId, int? OficioId, int Animales, decimal TotalCostoSistema, decimal TotalVenta);

public sealed class TrasladoAnimalDto
{
    public int AnimalId { get; set; }

    /// <summary>Orientación del animal en la sede destino; si es nula conserva la actual.</summary>
    public int? OrientacionDestinoId { get; set; }

    public string? ObservacionesEspecificas { get; set; }
}

/// <summary>Traslado entre granjas. Solo lleva Remisión: sin Oficio ni Recibo de Caja.</summary>
public sealed class TrasladoDto
{
    /// <summary>Solo al editar.</summary>
    public int? RemisionId { get; set; }

    public string NumeroRemision { get; set; } = string.Empty;

    public DateTime FechaTraslado { get; set; }

    public int GranjaOrigenId { get; set; }

    public int GranjaDestinoId { get; set; }

    public string? Observaciones { get; set; }

    public List<TrasladoAnimalDto> Animales { get; set; } = [];
}

public sealed record TrasladoResultado(int RemisionId, int Animales, decimal TotalValor);

// ---- Documentos de lotes (venta y traslado; sin asientos en el Kardex) ---------------------------------------------------

public sealed class VentaLoteLineaDto
{
    public int LoteId { get; set; }

    /// <summary>Cabezas vendidas (en un lote sin control numérico se vende el lote completo y se ignora).</summary>
    public int Cantidad { get; set; }

    /// <summary>Peso en báscula: obligatorio cuando el programa se cobra por kilogramo.</summary>
    public decimal? PesoKg { get; set; }

    /// <summary>Valor total pactado de la línea.</summary>
    public decimal ValorVenta { get; set; }

    public string? ObservacionesEspecificas { get; set; }
}

/// <summary>Venta de lotes: Oficio de Venta (un solo programa) → Remisión → Recibo de Caja.</summary>
public sealed class VentaLoteDto
{
    public int? RemisionId { get; set; }
    public string NumeroOficio { get; set; } = string.Empty;
    public DateTime FechaOficio { get; set; }
    public int ProgramaId { get; set; }
    public string? ObservacionesOficio { get; set; }
    public string NumeroRemision { get; set; } = string.Empty;
    public DateTime FechaRemision { get; set; }
    public string ReciboCaja { get; set; } = string.Empty;
    public int GranjaOrigenId { get; set; }
    public string Comprador { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public List<VentaLoteLineaDto> Lotes { get; set; } = [];
}

public sealed class TrasladoLoteLineaDto
{
    public int LoteId { get; set; }

    /// <summary>Cabezas a trasladar. Si son todas (o el lote no lleva conteo) el lote entero cambia de granja.</summary>
    public int Cantidad { get; set; }

    /// <summary>Traslado parcial: código del lote que se crea en la granja destino.</summary>
    public string? CodigoNuevoLote { get; set; }

    /// <summary>Ubicación en la granja destino (opcional en el traslado total).</summary>
    public string? UbicacionDestino { get; set; }

    public string? ObservacionesEspecificas { get; set; }
}

/// <summary>Traslado de lotes entre granjas. Solo lleva Remisión.</summary>
public sealed class TrasladoLoteDto
{
    public int? RemisionId { get; set; }
    public string NumeroRemision { get; set; } = string.Empty;
    public DateTime FechaTraslado { get; set; }
    public int GranjaOrigenId { get; set; }
    public int GranjaDestinoId { get; set; }
    public string? Observaciones { get; set; }
    public List<TrasladoLoteLineaDto> Lotes { get; set; } = [];
}