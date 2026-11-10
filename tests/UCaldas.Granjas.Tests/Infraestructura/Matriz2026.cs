using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Tests.Infraestructura;

/// <summary>
/// La tabla de precios 2026 (con los huecos ya completados) con identificadores ficticios, para probar la selección y la validación
/// sin base de datos.
/// </summary>
public static class Matriz2026
{
    public const int Terneros = 1;
    public const int Levante = 2;
    public const int Novilla = 3;
    public const int Vacas = 4;
    public const int Reproductor = 5;

    public const int Lacteo = 10;
    public const int Carne = 20;

    public static IReadOnlyList<TablaValorizacionBase> Filas() =>
    [
        Fila(1, Terneros, null, 0, 2, Sexo.Hembra, false, 150_000),
        Fila(2, Terneros, null, 0, 2, Sexo.Macho, false, 160_000),
        Fila(3, Terneros, null, 3, 7, Sexo.Ambos, false, 800_000),
        Fila(4, Levante, null, 8, 11, Sexo.Ambos, false, 1_200_000),
        Fila(5, Novilla, Lacteo, 12, 999, Sexo.Hembra, false, 3_200_000),   // siguen siendo novillas mientras no tengan partos
        Fila(6, Novilla, Carne, 12, 999, Sexo.Hembra, false, 3_200_000),
        Fila(7, Vacas, Lacteo, 24, 999, Sexo.Hembra, true, 6_000_000),
        Fila(8, Vacas, Carne, 24, 999, Sexo.Hembra, true, 4_000_000),
        Fila(9, Reproductor, null, 18, 999, Sexo.Macho, false, 7_000_000),
        Fila(IdLevanteMachos12a17, Levante, null, 12, 17, Sexo.Macho, false, 1_200_000), // los machos siguen como Levante hasta ser Reproductor
    ];

    /// <summary>Id de la fila que completa el hueco de los machos de 12 a 17 meses (para poder quitarla en las pruebas).</summary>
    public const int IdLevanteMachos12a17 = 10;

    public static IReadOnlyDictionary<int, CategoriaEtapa> Categorias() => new Dictionary<int, CategoriaEtapa>
    {
        [Terneros] = new() { Id = Terneros, Nombre = "Terneros", Sexo = Sexo.Ambos },
        [Levante] = new() { Id = Levante, Nombre = "Levante", Sexo = Sexo.Ambos },
        [Novilla] = new() { Id = Novilla, Nombre = "Novilla Vientre", Sexo = Sexo.Hembra },
        [Vacas] = new() { Id = Vacas, Nombre = "Vacas General", Sexo = Sexo.Hembra },
        [Reproductor] = new() { Id = Reproductor, Nombre = "Reproductor", Sexo = Sexo.Macho },
    };

    public static IReadOnlyDictionary<int, OrientacionProductiva> Orientaciones() => new Dictionary<int, OrientacionProductiva>
    {
        [Lacteo] = new() { Id = Lacteo, Nombre = "Lácteo" },
        [Carne] = new() { Id = Carne, Nombre = "Carne/Levante" },
    };

    public static TablaValorizacionBase Fila(
        int id, int categoria, int? orientacion, int min, int max, string sexo, bool partos, decimal valor) => new()
    {
        Id = id,
        VigenciaId = 1,
        CategoriaId = categoria,
        OrientacionId = orientacion,
        EdadMinimaMeses = min,
        EdadMaximaMeses = max,
        Sexo = sexo,
        RequierePartos = partos,
        ValorEstandar = valor,
    };
}
