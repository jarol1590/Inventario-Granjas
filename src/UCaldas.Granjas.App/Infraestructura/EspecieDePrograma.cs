using System.Globalization;
using System.Text;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>
/// Cómo se presenta un programa de inventario individual en las pestañas de Animales: nombre de la especie, plural y
/// singular para los textos de conteo, e ícono. Todo se deduce del nombre del programa para que un programa nuevo
/// (equinos, caprinos...) aparezca bien sin tocar el código: "Ganadería (Bovinos)" → pestaña «Bovinos».
/// </summary>
public sealed record EspecieDePrograma(string Titulo, string Singular, string Plural, string Icono)
{
    private const string AnimalSingular = "Animal";
    private const string AnimalPlural = "Animales";
    private const string IconoPredeterminado = "🐾";

    // Primera coincidencia gana. Las palabras clave van sin tildes y en minúscula.
    private static readonly (string[] Claves, string Icono)[] Iconos =
    [
        (["bovin", "ganad", "vacun", "bufal"], "🐄"),
        (["equin", "caball", "mular"], "🐴"),
        (["capr", "cabr"], "🐐"),
        (["ovin", "oveja", "ovej"], "🐑"),
        (["porcin", "cerd", "porcic"], "🐖"),
        (["avic", "gallin", "pollo", "aves"], "🐔"),
        (["pisc", "pez", "peces", "truch", "tilapia"], "🐟"),
        (["conej", "cuy", "cuyes"], "🐇"),
        (["abej", "apicul"], "🐝"),
    ];

    public static EspecieDePrograma De(Programa programa)
    {
        ArgumentNullException.ThrowIfNull(programa);

        var nombre = string.IsNullOrWhiteSpace(programa.Nombre) ? programa.Codigo : programa.Nombre.Trim();
        var abre = nombre.IndexOf('(');
        var cierra = nombre.LastIndexOf(')');
        var especie = abre >= 0 && cierra > abre + 1 ? nombre[(abre + 1)..cierra].Trim() : nombre;
        if (especie.Length == 0)
        {
            especie = nombre;
        }

        // Una sola palabra en plural ("Bovinos") sirve para los dos números; si no, se habla de "animales".
        var esPlural = especie.Length > 3 && !especie.Contains(' ') && especie.EndsWith('s');
        var plural = esPlural ? especie : AnimalPlural;
        var singular = esPlural ? Singularizar(especie) : AnimalSingular;

        return new EspecieDePrograma(especie, singular, plural, IconoDe($"{nombre} {programa.Codigo}"));
    }

    private static string Singularizar(string palabra)
    {
        // Peces → Pez, Lombrices → Lombriz; en los demás casos basta quitar la «s» (Bovinos → Bovino, Aves → Ave).
        if (palabra.EndsWith("ces", StringComparison.OrdinalIgnoreCase))
        {
            return palabra[..^3] + (char.IsUpper(palabra[^3]) ? "Z" : "z");
        }

        return palabra[..^1];
    }

    private static string IconoDe(string texto)
    {
        var normalizado = SinTildes(texto).ToLowerInvariant();
        foreach (var (claves, icono) in Iconos)
        {
            if (claves.Any(normalizado.Contains))
            {
                return icono;
            }
        }

        return IconoPredeterminado;
    }

    private static string SinTildes(string texto)
    {
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
