namespace UCaldas.Granjas.Domain.Abstractions;

/// <summary>Operaciones de mantenimiento sobre el archivo de la base de datos (respaldos, restauración, compactación).</summary>
public interface IDatabaseMaintenance
{
    /// <summary>Ruta del archivo de la base de datos en uso.</summary>
    string RutaBaseDatos { get; }

    /// <summary>
    /// Copia consistente y compactada de la base en <paramref name="destino"/> (<c>VACUUM INTO</c>): es segura aunque la
    /// aplicación esté usando la base y el archivo resultante no arrastra espacio libre. El destino no debe existir.
    /// </summary>
    Task CopiarConsistenteAsync(string destino, CancellationToken ct = default);

    /// <summary>Compacta la base (<c>VACUUM</c>) y devuelve su tamaño en bytes antes y después.</summary>
    Task<(long Antes, long Despues)> CompactarAsync(CancellationToken ct = default);

    /// <summary>¿El archivo es una base del sistema íntegra y de una versión de esquema compatible?</summary>
    Task<bool> EsBaseValidaAsync(string ruta, CancellationToken ct = default);

    /// <summary>Reemplaza la base en uso por la indicada, liberando antes cualquier conexión abierta.</summary>
    Task ReemplazarBaseAsync(string origen, CancellationToken ct = default);
}
