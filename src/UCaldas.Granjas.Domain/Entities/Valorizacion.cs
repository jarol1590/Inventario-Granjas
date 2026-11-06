namespace UCaldas.Granjas.Domain.Entities;

/// <summary>Vigencia anual de la matriz de valorización. Solo una puede estar activa.</summary>
public class VigenciaValorizacion
{
    public int Id { get; set; }
    public int Anio { get; set; }
    public bool Activa { get; set; }
    public string? Observaciones { get; set; }
}

/// <summary>
/// Fila de la matriz de tarifas: valor base por categoría, rango de edad (inclusivo en ambos extremos),
/// sexo, orientación y exigencia de partos.
/// </summary>
public class TablaValorizacionBase
{
    public int Id { get; set; }
    public int VigenciaId { get; set; }
    public int CategoriaId { get; set; }

    /// <summary>Nulo = "Todas" las orientaciones.</summary>
    public int? OrientacionId { get; set; }

    public int EdadMinimaMeses { get; set; }
    public int EdadMaximaMeses { get; set; }

    /// <summary>Macho, Hembra o Ambos.</summary>
    public string Sexo { get; set; } = string.Empty;

    /// <summary>Si es true la fila solo aplica a animales con al menos un parto.</summary>
    public bool RequierePartos { get; set; }

    public decimal ValorEstandar { get; set; }
}
