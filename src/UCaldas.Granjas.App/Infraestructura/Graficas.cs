using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Construye las series y ejes de LiveCharts2 para el tablero, con la paleta institucional.</summary>
public static class Graficas
{
    private static readonly SKColor AzulInstitucional = SKColor.Parse("#1B365D");
    private static readonly SKColor AzulClaro = SKColor.Parse("#86AEE8");
    private static readonly SKColor Gris = SKColor.Parse("#7B8794");
    private static readonly SKColor Texto = SKColor.Parse("#1F2937");
    private static readonly SKColor Separador = SKColor.Parse("#E1E6EE");

    /// <summary>Barras horizontales con el valor al final de cada barra (animales por granja o por categoría).</summary>
    public static (ISeries[] Series, Axis[] EjesX, Axis[] EjesY) BarrasHorizontales(IReadOnlyList<PuntoGrafica> puntos)
    {
        var series = new ISeries[]
        {
            new RowSeries<double>
            {
                Values = puntos.Select(p => (double)p.Valor).ToArray(),
                Fill = new SolidColorPaint(AzulInstitucional),
                MaxBarWidth = 26,
                Padding = 6,
                DataLabelsPaint = new SolidColorPaint(Texto),
                DataLabelsSize = 13,
                DataLabelsPosition = DataLabelsPosition.End,
                DataLabelsFormatter = punto => punto.Coordinate.PrimaryValue.ToString("N0"),
            },
        };

        var ejesX = new[] { EjeNumerico() };
        var ejesY = new[]
        {
            new Axis
            {
                Labels = puntos.Select(p => p.Etiqueta).ToArray(),
                LabelsPaint = new SolidColorPaint(Texto),
                TextSize = 13,
                IsInverted = true,
                SeparatorsPaint = null,
            },
        };

        return (series, ejesX, ejesY);
    }

    /// <summary>Columnas por tipo de movimiento: entradas (azul), salidas (celeste) y traslados (gris).</summary>
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
            DataLabelsPaint = new SolidColorPaint(Texto),
            DataLabelsSize = 13,
            DataLabelsPosition = DataLabelsPosition.End,
            DataLabelsFormatter = punto => punto.Coordinate.PrimaryValue.ToString("N0"),
        };

        var series = new ISeries[]
        {
            Serie("Entradas", AzulInstitucional, Valores(puntos, "Nacimientos", "Compras")),
            Serie("Salidas", AzulClaro, Valores(puntos, "Ventas", "Bajas")),
            Serie("Traslados", Gris, Valores(puntos, "Traslados")),
        };

        var ejesX = new[]
        {
            new Axis
            {
                Labels = puntos.Select(p => p.Etiqueta).ToArray(),
                LabelsPaint = new SolidColorPaint(Texto),
                TextSize = 12,
                SeparatorsPaint = null,
            },
        };

        return (series, ejesX, [EjeNumerico()]);
    }

    private static Axis EjeNumerico() => new()
    {
        MinLimit = 0,
        MinStep = 1,
        LabelsPaint = new SolidColorPaint(Texto),
        TextSize = 12,
        SeparatorsPaint = new SolidColorPaint(Separador) { StrokeThickness = 1 },
        Labeler = valor => valor.ToString("N0"),
    };
}
