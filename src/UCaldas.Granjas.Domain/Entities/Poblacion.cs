namespace UCaldas.Granjas.Domain.Entities;

/// <summary>Lote o población (aves, peces...). La interfaz de lotes queda fuera del Beta; la entidad existe por el esquema.</summary>
public class LotePoblacion
{
    public int Id { get; set; }
    public string CodigoLote { get; set; } = string.Empty;
    public int ProgramaId { get; set; }
    public int GranjaId { get; set; }

    /// <summary>Galpón, estanque, era...</summary>
    public string Ubicacion { get; set; } = string.Empty;

    public int CantidadInicial { get; set; }
    public int CantidadActual { get; set; }
    public int MortandadAcumulada { get; set; }
    public decimal ValorUnitario { get; set; }
    public decimal ValorTotal { get; set; }
    public DateTime FechaIngreso { get; set; }
    public string Estado { get; set; } = "Activo";
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
