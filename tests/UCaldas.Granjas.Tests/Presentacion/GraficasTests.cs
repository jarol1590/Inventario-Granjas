using LiveChartsCore.SkiaSharpView;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.Tests.Presentacion;

public class GraficasTests
{
    [Fact]
    public void La_tipografia_Roboto_esta_incrustada_y_cada_pedido_devuelve_una_nueva_independiente()
    {
        using var primera = Graficas.NuevaTipografia();
        using var segunda = Graficas.NuevaTipografia();

        primera.Should().NotBeNull("Recursos/Fuentes/Roboto-Regular.ttf debe ir incrustado en la aplicación");
        primera!.FamilyName.Should().Be("Roboto");
        segunda.Should().NotBeNull().And.NotBeSameAs(primera);

        // LiveCharts libera la tipografía de cada pincel al descartar una gráfica: eso no puede afectar a las demás.
        primera.Dispose();
        segunda!.FamilyName.Should().Be("Roboto");
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(10, 12)]
    [InlineData(100, 115)]
    public void El_eje_deja_aire_sobre_el_valor_mayor_y_nunca_baja_de_cuatro(double maximo, double esperado)
        => Graficas.TopeDelEje(maximo).Should().Be(esperado);

    [Fact]
    public void Con_todos_los_valores_en_cero_los_ejes_numericos_siguen_mostrando_un_rango_visible()
    {
        var granjas = new[] { new PuntoGrafica("Tesorito", 0), new PuntoGrafica("La Cruz", 0) };
        var movimientos = new[] { new PuntoGrafica("Nacimientos", 0), new PuntoGrafica("Compras", 0) };

        var (_, ejesX, _) = Graficas.BarrasHorizontales(granjas);
        var (series, _, ejesY) = Graficas.ColumnasDeMovimientos(movimientos);

        ejesX.Single().MinLimit.Should().Be(0);
        ejesX.Single().MaxLimit.Should().Be(Graficas.TopeMinimoDelEje);
        ejesY.Single().MaxLimit.Should().Be(Graficas.TopeMinimoDelEje);
        series.Should().HaveCount(3, "entradas, salidas y traslados");
    }

    [Fact]
    public void Una_categoria_sin_valor_escribe_el_cero_junto_a_su_nombre_y_las_demas_no()
    {
        Graficas.Etiquetar(new PuntoGrafica("Ventas", 0)).Should().Be("Ventas (0)");
        Graficas.Etiquetar(new PuntoGrafica("Compras", 2)).Should().Be("Compras");
    }

    [Fact]
    public void En_las_columnas_el_cero_va_en_una_segunda_linea_para_que_las_etiquetas_no_se_pisen()
    {
        Graficas.Etiquetar(new PuntoGrafica("Ventas", 0), enDosLineas: true).Should().Be("Ventas\n(0)");
        Graficas.Etiquetar(new PuntoGrafica("Compras", 2), enDosLineas: true).Should().Be("Compras");

        var (_, ejesX, _) = Graficas.ColumnasDeMovimientos([new PuntoGrafica("Nacimientos", 0), new PuntoGrafica("Compras", 3)]);
        ejesX.Single().Labels.Should().Equal("Nacimientos\n(0)", "Compras");
    }

    [Fact]
    public void Una_lista_vacia_de_puntos_no_rompe_la_construccion_de_las_graficas()
    {
        var act = () =>
        {
            Graficas.BarrasHorizontales([]);
            Graficas.ColumnasDeMovimientos([]);
        };

        act.Should().NotThrow();
    }
}
