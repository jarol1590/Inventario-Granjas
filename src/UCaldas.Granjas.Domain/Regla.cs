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

    /// <summary>Un teléfono vacío se acepta (la obligatoriedad se exige aparte); si trae texto solo
    /// puede llevar el símbolo + al inicio y dígitos.</summary>
    public static void ExigirTelefono(string? valor, string nombreCampo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return;
        }

        if (!TelefonoValido(valor.Trim()))
        {
            throw new ValidationException($"{nombreCampo} tiene un formato no válido: solo se permite el símbolo + al inicio y números.");
        }
    }

    /// <summary>Varios teléfonos separados por coma; cada uno cumple <see cref="ExigirTelefono"/>.</summary>
    public static void ExigirTelefonos(string? valor, string nombreCampo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return;
        }

        foreach (var parte in valor.Split(','))
        {
            if (!TelefonoValido(parte.Trim()))
            {
                throw new ValidationException($"{nombreCampo} tiene un formato no válido: cada número solo puede llevar el símbolo + al inicio y dígitos, separados por comas.");
            }
        }
    }

    private static bool TelefonoValido(string texto)
    {
        var digitos = texto.StartsWith('+') ? texto[1..] : texto;
        return digitos.Length > 0 && digitos.All(c => c is >= '0' and <= '9');
    }

    /// <summary>Lanza siempre; útil en ramas que ya detectaron la violación.</summary>
    public static Exception Violada(string mensaje) => new ValidationException(mensaje);
}
