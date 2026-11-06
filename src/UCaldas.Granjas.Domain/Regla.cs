using System.ComponentModel.DataAnnotations;

namespace UCaldas.Granjas.Domain;

/// <summary>
/// Atajos para validar reglas de negocio. Toda violación lanza <see cref="ValidationException"/>
/// con un mensaje en español pensado para mostrarse tal cual al usuario.
/// </summary>
public static class Regla
{
    public static void Exigir(bool condicion, string mensaje)
    {
        if (!condicion)
        {
            throw new ValidationException(mensaje);
        }
    }

    public static void ExigirTexto(string? valor, string nombreCampo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ValidationException($"{nombreCampo} es obligatorio.");
        }
    }

    /// <summary>Lanza siempre; útil en ramas que ya detectaron la violación.</summary>
    public static Exception Violada(string mensaje) => new ValidationException(mensaje);
}
