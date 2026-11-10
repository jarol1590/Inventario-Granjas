using System.Windows;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Construye las series y ejes de LiveCharts2 para el tablero, con la paleta de la Facultad y la tipografía Roboto.</summary>
public static class Graficas
{
    // Paleta de la Facultad de Ciencias Agropecuarias: azul, verde y arena.
    private static readonly SKColor Azul = SKColor.Parse("#003E70");
    private static readonly SKColor Verde = SKColor.Parse("#66A382");
    private static readonly SKColor Arena = SKColor.Parse("#D5BB87");
    private static readonly SKColor Texto = SKColor.Parse("#1F2937");
    private static readonly SKColor Separador = SKColor.Parse("#E0E8E3");

    /// <summary>Rango mínimo del eje numérico: con todo en cero la gráfica se ve (0 a 4) en vez de colapsar.</summary>
    public const double TopeMinimoDelEje = 4;

    // Los bytes de Roboto se leen una sola vez, pero CADA pincel recibe su propia SKTypeface: LiveCharts libera la
    // tipografía del pincel al descartar una gráfica. Si se compartiera una sola, al salir de Inicio y volver (o al
    // cambiar un filtro) las gráficas nuevas usarían una tipografía ya liberada y quedarían en blanco.
    private static readonly Lazy<SKData?> DatosDeRoboto = new(CargarRoboto);

    private static SKData? CargarRoboto()
    {
        try
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack; // registra pack:// aunque no haya ventana WPF (pruebas)
            var recurso = Application.GetResourceStream(new Uri("pack://application:,,,/SistemaGranjas;component/Recursos/Fuentes/Roboto-Regular.ttf"));
            if (recurso is null)
            {
                return null;
            }

            using var origen = recurso.Stream;
            using var memoria = new MemoryStream();
            origen.CopyTo(memoria);
            return SKData.CreateCopy(memoria.ToArray());
        }
        catch (Exception)
        {
            return null; // se usa la tipografía predeterminada de Skia
        }
    }

    /// <summary>Tipografía Roboto nueva (propia de quien la pide), o nulo si no se pudo leer el recurso.</summary>
    public static SKTypeface? NuevaTipografia() => DatosDeRoboto.Value is { } datos ? SKTypeface.FromData(datos) : null;

    private static SolidColorPaint Pincel(SKColor color, float? grosor = null)
    {
        var pincel = new SolidColorPaint(color) { SKTypeface = NuevaTipografia() };
        if (grosor is { } valor)
        {
            pincel.StrokeThickness = valor;
        }

        return pincel;
    }

    /// <summary>Barras horizontales con el valor al final de cada barra (animales por granja o por categoría).</summary>
    public static (ISeries[] Series, Axis[] EjesX, Axis[] EjesY) BarrasHorizontales(IReadOnlyList<PuntoGrafica> puntos)
    {
        var series = new ISeries[]
        {
            new RowSeries<double>
            {
                Values = puntos.Select(p => (double)p.Valor).ToArray(),
                Fill = new SolidColorPaint(Azul),
                MaxBarWidth = 26,
                Padding = 6,
                DataLabelsPaint = Pincel(Texto),
                DataLabelsSize = 13,
                DataLabelsPosition = DataLabelsPosition.End,
                DataLabelsFormatter = punto => punto.Coordinate.PrimaryValue.ToString("N0"),
            },
        };

        var ejesX = new[] { EjeNumerico(Maximo(puntos)) };
        var ejesY = new[]
        {
            new Axis
            {
                Labels = puntos.Select(p => Etiquetar(p)).ToArray(),
                LabelsPaint = Pincel(Texto),
                TextSize = 13,
                IsInverted = true,
                SeparatorsPaint = null,
            },
        };

        return (series, ejesX, ejesY);
    }

    /// <summary>Columnas por tipo de movimiento: entradas (azul), salidas (verde) y traslados (arena).</summary>
    public static (ISeries[] Series, Axis[] EjesX, Axis[] EjesY) ColumnasDeMovimientos(IReadOnlyList<PuntoGrafica> puntos)
    {
        static double?[] Valores(IReadOnlyList<PuntoGrafica> todos, params string[] etiquetas)
            => todos.Select(p => etiquetas.Contains(p.Etiqueta) ? (double?)p.Valor : null).ToArray();

        ColumnSeries<double?> Serie(string nombre, SKColor color, double?[] valores) => new()
        {
            Name = nombre,
            Values = valores,
            Fill = new SolidColorPaint(color),
            MaxBarWidth = 38,
            IgnoresBarPosition = true,
            DataLabelsPaint = Pincel(Texto),
            DataLabelsSize = 13,
            DataLabelsPosition = DataLabelsPosition.End,
            DataLabelsFormatter = punto => punto.Coordinate.PrimaryValue.ToString("N0"),
        };

        var series = new ISeries[]
        {
            Serie("Entradas", Azul, Valores(puntos, "Nacimientos", "Compras")),
            Serie("Salidas", Verde, Valores(puntos, "Ventas", "Bajas")),
            Serie("Traslados", Arena, Valores(puntos, "Traslados")),
        };

        var ejesX = new[]
        {
            new Axis
            {
                Labels = puntos.Select(p => Etiquetar(p, enDosLineas: true)).ToArray(),
                LabelsPaint = Pincel(Texto),
                TextSize = 12,
                SeparatorsPaint = null,
            },
        };

        return (series, ejesX, [EjeNumerico(Maximo(puntos))]);
    }

    /// <summary>
    /// Nombre de la categoría en el eje. LiveCharts no pinta la etiqueta de una barra de tamaño cero, así que el cero se
    /// escribe junto al nombre ("Ventas (0)"): una gráfica vacía se ve como un cero explícito y no como un error.
    /// Con <paramref name="enDosLineas"/> el cero va debajo del nombre ("Ventas" / "(0)"): en el eje horizontal de las
    /// columnas las etiquetas comparten el ancho de la gráfica y, en una sola línea, se pisarían entre sí.
    /// </summary>
    public static string Etiquetar(PuntoGrafica punto, bool enDosLineas = false)
        => punto.Valor == 0 ? $"{punto.Etiqueta}{(enDosLineas ? "\n" : " ")}(0)" : punto.Etiqueta;

    private static double Maximo(IReadOnlyList<PuntoGrafica> puntos) => puntos.Count == 0 ? 0 : puntos.Max(p => (double)p.Valor);

    /// <summary>Límite superior del eje: algo de aire sobre el valor mayor (para la etiqueta de la barra) y nunca menos de 4.</summary>
    public static double TopeDelEje(double maximo) => Math.Max(TopeMinimoDelEje, Math.Ceiling(maximo * 1.15));

    private static Axis EjeNumerico(double maximo) => new()
    {
        MinLimit = 0,
        MaxLimit = TopeDelEje(maximo),
        MinStep = 1,
        LabelsPaint = Pincel(Texto),
        TextSize = 12,
        SeparatorsPaint = Pincel(Separador, 1),
        Labeler = valor => valor.ToString("N0"),
    };
}
