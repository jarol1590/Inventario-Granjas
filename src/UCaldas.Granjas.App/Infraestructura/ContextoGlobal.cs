namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Filtros globales de la ventana principal (Granja y Programa). Nulo = todas.</summary>
public sealed class ContextoGlobal
{
    public int? GranjaId { get; set; }

    public int? ProgramaId { get; set; }
}
