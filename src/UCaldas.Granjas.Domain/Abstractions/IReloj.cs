namespace UCaldas.Granjas.Domain.Abstractions;

/// <summary>Fuente de la fecha actual. Existe para poder fijar "hoy" en las pruebas.</summary>
public interface IReloj
{
    /// <summary>Fecha y hora locales actuales.</summary>
    DateTime Ahora { get; }

    /// <summary>Fecha local actual sin hora.</summary>
    DateTime Hoy => Ahora.Date;
}

public sealed class RelojSistema : IReloj
{
    public DateTime Ahora => DateTime.Now;
}
