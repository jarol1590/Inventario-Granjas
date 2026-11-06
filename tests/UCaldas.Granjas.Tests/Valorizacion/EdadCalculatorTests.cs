using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Valorizacion;

public class EdadCalculatorTests
{
    [Theory]
    [InlineData("2026-01-15", "2026-01-15", 0)]
    [InlineData("2026-01-15", "2026-02-14", 0)]
    [InlineData("2026-01-15", "2026-02-15", 1)]
    [InlineData("2026-01-15", "2027-01-14", 11)]
    [InlineData("2026-01-15", "2027-01-15", 12)]
    // Nació un día 31: el mes se cumple el último día del mes siguiente aunque no exista el 31.
    [InlineData("2026-01-31", "2026-02-27", 0)]
    [InlineData("2026-01-31", "2026-02-28", 1)]
    [InlineData("2026-01-31", "2026-03-30", 1)]
    [InlineData("2026-01-31", "2026-03-31", 2)]
    // Nació un 29 de febrero (año bisiesto).
    [InlineData("2024-02-29", "2025-02-28", 12)]
    [InlineData("2024-02-29", "2025-03-01", 12)]
    [InlineData("2024-02-29", "2028-02-29", 48)]
    // Animales reales del reporte de hatos de La Cruz impreso el 8/10/2026 (el reporte muestra los meses con decimales).
    [InlineData("2017-06-04", "2026-10-08", 112)] // 004C CLAUDIA: 112
    [InlineData("2021-06-14", "2026-10-08", 63)]  // 108-1 CLARA: 63,8
    [InlineData("2026-04-11", "2026-10-08", 5)]   // 230-6: 5,9
    [InlineData("2026-10-07", "2026-10-08", 0)]   // 238-6: 0
    public void Calcula_los_meses_cumplidos(string nacimiento, string referencia, int esperado)
    {
        var meses = EdadCalculator.Meses(Datos.F(nacimiento), Datos.F(referencia));

        meses.Should().Be(esperado);
    }

    [Fact]
    public void Ignora_la_hora_del_dia()
    {
        var meses = EdadCalculator.Meses(new DateTime(2026, 1, 15, 23, 59, 0), new DateTime(2026, 2, 15, 0, 1, 0));

        meses.Should().Be(1);
    }

    [Fact]
    public void Rechaza_una_referencia_anterior_al_nacimiento()
    {
        var act = () => EdadCalculator.Meses(Datos.F("2026-10-08"), Datos.F("2026-10-07"));

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*anterior al nacimiento*");
    }

    [Theory]
    [InlineData("2026-02-10", "2026-02-28")]
    [InlineData("2024-02-01", "2024-02-29")]
    [InlineData("2026-12-31", "2026-12-31")]
    [InlineData("2026-04-01", "2026-04-30")]
    public void Obtiene_el_ultimo_dia_del_mes(string fecha, string esperado)
    {
        EdadCalculator.FinDeMes(Datos.F(fecha)).Should().Be(Datos.F(esperado));
    }
}
