using System.ComponentModel.DataAnnotations;
using System.Text;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Valorizacion;

/// <summary>Par de filas de la matriz cuyos rangos de edad se cruzan.</summary>
public sealed record ConflictoDeTarifas(TablaValorizacionBase Primera, TablaValorizacionBase Segunda, int DesdeMes, int HastaMes);

/// <summary>La matriz no se puede guardar porque dos filas del mismo sexo e historia productiva se cruzan en edad.</summary>
public sealed class SolapamientoDeTarifasException : ValidationException
{
    public SolapamientoDeTarifasException(string mensaje, IReadOnlyList<ConflictoDeTarifas> conflictos)
        : base(mensaje)
    {
        Conflictos = conflictos;
    }

    public IReadOnlyList<ConflictoDeTarifas> Conflictos { get; }
}

public interface ITarifaValidationService
{
    /// <summary>Existe solapamiento si <c>Max(MinA, MinB) &lt;= Min(MaxA, MaxB)</c> entre filas comparables.</summary>
    bool HaySolapamiento(TablaValorizacionBase a, TablaValorizacionBase b);

    /// <summary>
    /// Valida la matriz completa que se va a guardar: datos de cada fila y ausencia de solapamientos.
    /// </summary>
    /// <exception cref="SolapamientoDeTarifasException">Si hay rangos de edad cruzados.</exception>
    /// <exception cref="ValidationException">Si alguna fila tiene datos inválidos.</exception>
    void ValidarMatriz(
        IReadOnlyCollection<TablaValorizacionBase> filas,
        IReadOnlyDictionary<int, CategoriaEtapa> categorias,
        IReadOnlyDictionary<int, OrientacionProductiva> orientaciones);

    /// <summary>Valida una fila nueva o editada frente a las existentes (la fila con el mismo id se ignora).</summary>
    void ValidarFila(
        TablaValorizacionBase candidata,
        IReadOnlyCollection<TablaValorizacionBase> existentes,
        IReadOnlyDictionary<int, CategoriaEtapa> categorias,
        IReadOnlyDictionary<int, OrientacionProductiva> orientaciones);
}

public sealed class TarifaValidationService : ITarifaValidationService
{
    public bool HaySolapamiento(TablaValorizacionBase a, TablaValorizacionBase b)
        => SonComparables(a, b)
           && Math.Max(a.EdadMinimaMeses, b.EdadMinimaMeses) <= Math.Min(a.EdadMaximaMeses, b.EdadMaximaMeses);

    public void ValidarMatriz(
        IReadOnlyCollection<TablaValorizacionBase> filas,
        IReadOnlyDictionary<int, CategoriaEtapa> categorias,
        IReadOnlyDictionary<int, OrientacionProductiva> orientaciones)
    {
        ArgumentNullException.ThrowIfNull(filas);

        var lista = filas.ToList();
        for (var i = 0; i < lista.Count; i++)
        {
            ValidarDatos(lista[i], i + 1, categorias, orientaciones);
        }

        var conflictos = new List<ConflictoDeTarifas>();
        for (var i = 0; i < lista.Count; i++)
        {
            for (var j = i + 1; j < lista.Count; j++)
            {
                if (HaySolapamiento(lista[i], lista[j]))
                {
                    conflictos.Add(new ConflictoDeTarifas(
                        lista[i],
                        lista[j],
                        Math.Max(lista[i].EdadMinimaMeses, lista[j].EdadMinimaMeses),
                        Math.Min(lista[i].EdadMaximaMeses, lista[j].EdadMaximaMeses)));
                }
            }
        }

        if (conflictos.Count > 0)
        {
            throw new SolapamientoDeTarifasException(MensajeDeSolapamiento(conflictos, categorias, orientaciones), conflictos);
        }
    }

    public void ValidarFila(
        TablaValorizacionBase candidata,
        IReadOnlyCollection<TablaValorizacionBase> existentes,
        IReadOnlyDictionary<int, CategoriaEtapa> categorias,
        IReadOnlyDictionary<int, OrientacionProductiva> orientaciones)
    {
        ArgumentNullException.ThrowIfNull(candidata);
        ArgumentNullException.ThrowIfNull(existentes);

        var resto = existentes.Where(e => candidata.Id == 0 || e.Id != candidata.Id).ToList();
        resto.Add(candidata);
        ValidarMatriz(resto, categorias, orientaciones);
    }

    /// <summary>
    /// Dos filas de una misma vigencia compiten solo si tienen el mismo sexo, la misma historia productiva (exigencia
    /// de partos) y la misma orientación ("Todas" cuenta como una orientación más). Las filas de orientación o sexo
    /// distintos pueden cruzarse: las resuelve el desempate por especificidad del <see cref="TarifaSelector"/>.
    /// </summary>
    private static bool SonComparables(TablaValorizacionBase a, TablaValorizacionBase b)
        => a.Sexo == b.Sexo
           && a.RequierePartos == b.RequierePartos
           && a.OrientacionId == b.OrientacionId;

    private static void ValidarDatos(
        TablaValorizacionBase fila,
        int numero,
        IReadOnlyDictionary<int, CategoriaEtapa> categorias,
        IReadOnlyDictionary<int, OrientacionProductiva> orientaciones)
    {
        var etiqueta = $"Fila {numero} de la matriz";

        Regla.Exigir(Sexo.EsDeTarifa(fila.Sexo), $"{etiqueta}: el sexo debe ser Macho, Hembra o Ambos.");
        Regla.Exigir(fila.EdadMinimaMeses >= 0, $"{etiqueta}: la edad mínima no puede ser negativa.");
        Regla.Exigir(
            fila.EdadMaximaMeses >= fila.EdadMinimaMeses,
            $"{etiqueta}: la edad máxima ({fila.EdadMaximaMeses}) no puede ser menor que la mínima ({fila.EdadMinimaMeses}).");
        Regla.Exigir(fila.ValorEstandar > 0, $"{etiqueta}: el valor base debe ser mayor que cero.");

        if (!categorias.TryGetValue(fila.CategoriaId, out var categoria))
        {
            throw Regla.Violada($"{etiqueta}: la categoría no existe.");
        }

        Regla.Exigir(
            fila.OrientacionId is null || orientaciones.ContainsKey(fila.OrientacionId.Value),
            $"{etiqueta}: la orientación no existe.");

        // Una categoría exclusiva de un sexo no puede tener tarifas para el otro (ni para "Ambos").
        Regla.Exigir(
            categoria.Sexo == Sexo.Ambos || categoria.Sexo == fila.Sexo,
            $"{etiqueta}: la categoría «{categoria.Nombre}» es de sexo {categoria.Sexo}, pero la fila indica {fila.Sexo}.");
    }

    private static string MensajeDeSolapamiento(
        IReadOnlyList<ConflictoDeTarifas> conflictos,
        IReadOnlyDictionary<int, CategoriaEtapa> categorias,
        IReadOnlyDictionary<int, OrientacionProductiva> orientaciones)
    {
        var sb = new StringBuilder();
        sb.AppendLine(conflictos.Count == 1
            ? "No se puede guardar: hay un solapamiento de edades entre dos tarifas del mismo sexo e historia productiva."
            : $"No se puede guardar: hay {conflictos.Count} solapamientos de edades entre tarifas del mismo sexo e historia productiva.");

        foreach (var c in conflictos)
        {
            var coincidencia = c.DesdeMes == c.HastaMes
                ? $"el mes {c.DesdeMes}"
                : $"los meses {c.DesdeMes} a {c.HastaMes}";
            sb.AppendLine($"• {Describir(c.Primera, categorias, orientaciones)}  se cruza con  {Describir(c.Segunda, categorias, orientaciones)}  en {coincidencia}.");
        }

        sb.Append("Ajuste las edades para que los rangos no se crucen (el máximo de una fila debe ser menor que el mínimo de la siguiente).");
        return sb.ToString();
    }

    private static string Describir(
        TablaValorizacionBase fila,
        IReadOnlyDictionary<int, CategoriaEtapa> categorias,
        IReadOnlyDictionary<int, OrientacionProductiva> orientaciones)
    {
        var categoria = categorias.TryGetValue(fila.CategoriaId, out var c) ? c.Nombre : $"categoría {fila.CategoriaId}";
        var orientacion = fila.OrientacionId is { } id && orientaciones.TryGetValue(id, out var o) ? o.Nombre : "todas las orientaciones";
        var partos = fila.RequierePartos ? "con partos" : "sin exigir partos";
        return $"[{categoria} · {fila.Sexo} · {orientacion} · {partos} · {fila.EdadMinimaMeses}–{fila.EdadMaximaMeses} meses]";
    }
}
