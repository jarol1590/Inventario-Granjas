using UCaldas.Granjas.Domain.Constants;

namespace UCaldas.Granjas.Domain.Entities;

/// <summary>
/// Semoviente (bovino) individual identificado por chapeta amarilla.
/// Maneja el doble sistema de precios: <see cref="ValorCompraOrigen"/> (inmutable) y
/// <see cref="ValorContableActual"/> (dinámico, asignado por el AgingEngine).
/// </summary>
public class Animal
{
    public int Id { get; set; }
    public string ChapetaAmarilla { get; set; } = string.Empty;
    public string? ChapetaRoja { get; set; }
    public string? Nombre { get; set; }
    public string Sexo { get; set; } = string.Empty;
    public DateTime FechaNacimiento { get; set; }

    /// <summary>Nulo si el animal nació en la granja.</summary>
    public DateTime? FechaCompra { get; set; }

    public string ClaseEntrada { get; set; } = string.Empty;
    public string Raza { get; set; } = string.Empty;

    /// <summary>Chapeta de la madre (texto libre; puede o no existir en el sistema).</summary>
    public string? MadreIdentificacion { get; set; }

    public decimal PesoActualKg { get; set; }

    /// <summary>La orientación pertenece al animal, no a la granja.</summary>
    public int OrientacionId { get; set; }

    /// <summary>Costo real de adquisición en factura. Inmutable; $0 si nació en la granja.</summary>
    public decimal ValorCompraOrigen { get; set; }

    /// <summary>Valor en libros asignado por la matriz de valorización vigente.</summary>
    public decimal ValorContableActual { get; set; }

    public int GranjaId { get; set; }
    public int CategoriaId { get; set; }
    public string Estado { get; set; } = EstadoAnimal.Disponible;

    /// <summary>Partos que ha tenido la hembra (extensión v2.1). Alimenta <c>requiere_partos</c> de la matriz.</summary>
    public int NumeroPartos { get; set; }

    /// <summary>Si es true, la valorización actualiza la categoría pero conserva el valor contable (extensión v2.1).</summary>
    public bool ExentoValorizacion { get; set; }

    /// <summary>Momento en que se registró en el sistema (extensión v2.1).</summary>
    public DateTime FechaRegistro { get; set; }
}
