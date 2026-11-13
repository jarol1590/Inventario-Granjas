namespace UCaldas.Granjas.Domain.Entities;

/// <summary>Lote de animales vivos que se maneja en grupo (aves, peces, cerdos). Esquema v2.3.</summary>
public class LotePoblacion
{
    public int Id { get; set; }
    public string CodigoLote { get; set; } = string.Empty;
    public int ProgramaId { get; set; }
    public int GranjaId { get; set; }

    /// <summary>Etapa productiva actual (una <see cref="CategoriaEtapa"/> del programa).</summary>
    public int EtapaId { get; set; }

    /// <summary>COMPRA, NACIMIENTO, TRASLADO o DESDOBLE.</summary>
    public string ClaseEntrada { get; set; } = string.Empty;

    /// <summary>Galpón, estanque, corral...</summary>
    public string Ubicacion { get; set; } = string.Empty;

    public string LineaRaza { get; set; } = string.Empty;

    /// <summary>N.º de factura, chapeta de la madre o código del lote padre (desdoble).</summary>
    public string? OrigenReferencia { get; set; }

    public string? Proveedor { get; set; }
    public string? OrdenCompra { get; set; }
    public DateTime FechaNacimientoIngreso { get; set; }

    /// <summary>false = estanque sin conteo de individuos: las cantidades no aplican.</summary>
    public bool ControlNumericoPoblacion { get; set; } = true;

    public int CantidadInicial { get; set; }
    public int CantidadActual { get; set; }
    public int MortandadAcumulada { get; set; }

    /// <summary>Inmutable: $0 si nació en la granja.</summary>
    public decimal ValorCompraUnitario { get; set; }

    /// <summary>Dinámico: el valor de referencia de la etapa actual.</summary>
    public decimal ValorContableUnitario { get; set; }

    /// <summary>cantidad_actual × valor_contable_unitario (en un lote sin conteo, el valor del lote entero).</summary>
    public decimal ValorTotalContable { get; set; }

    public string? Observaciones { get; set; }

    /// <summary>Activo o Finalizado (cuando no queda ningún animal).</summary>
    public string Estado { get; set; } = "Activo";

    /// <summary>Operaciones hechas sobre el lote (etapa, desdoble, baja, venta, traslado). Con 0 aún se corrigen cantidad y precio de la compra (v2.4).</summary>
    public int Operaciones { get; set; }

    /// <summary>La compra del lote se anuló: ya no cuenta (v2.4).</summary>
    public bool Anulado { get; set; }
}
/// <summary>Genealogía del animal: tipo de concepción y padre.</summary>
public class DatosGenealogia
{
    public int Id { get; set; }
    public int AnimalId { get; set; }

    /// <summary>true = inseminación artificial; false = monta directa.</summary>
    public bool EsInseminacion { get; set; }

    public string? RazaPadreInseminacion { get; set; }
    public string? NombrePadreInseminacion { get; set; }

    /// <summary>Padre registrado en el sistema (monta directa).</summary>
    public int? PadreId { get; set; }
}
