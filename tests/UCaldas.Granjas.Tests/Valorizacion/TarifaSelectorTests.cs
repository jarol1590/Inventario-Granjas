using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;
using static UCaldas.Granjas.Tests.Infraestructura.Matriz2026;

namespace UCaldas.Granjas.Tests.Valorizacion;

public class TarifaSelectorTests
{
    // ---- La tabla de precios oficial 2026 -------------------------------------------------------------------

    [Theory]
    [InlineData(Sexo.Hembra, Lacteo, 0, 0, Terneros, 150_000)]
    [InlineData(Sexo.Hembra, Carne, 0, 2, Terneros, 150_000)]
    [InlineData(Sexo.Macho, Lacteo, 0, 0, Terneros, 160_000)]
    [InlineData(Sexo.Macho, Carne, 0, 2, Terneros, 160_000)]
    [InlineData(Sexo.Hembra, Lacteo, 0, 3, Terneros, 800_000)]
    [InlineData(Sexo.Macho, Carne, 0, 7, Terneros, 800_000)]
    [InlineData(Sexo.Hembra, Lacteo, 0, 8, Levante, 1_200_000)]
    [InlineData(Sexo.Macho, Lacteo, 0, 11, Levante, 1_200_000)]
    [InlineData(Sexo.Hembra, Lacteo, 0, 12, Novilla, 3_200_000)]
    [InlineData(Sexo.Hembra, Carne, 0, 24, Novilla, 3_200_000)]   // sin partos, la vaca no aplica aunque tenga 24 meses
    [InlineData(Sexo.Hembra, Lacteo, 0, 25, Novilla, 3_200_000)]  // novillas de más de 24 meses sin parto: mantienen la clasificación
    [InlineData(Sexo.Hembra, Carne, 0, 100, Novilla, 3_200_000)]
    [InlineData(Sexo.Macho, Lacteo, 0, 12, Levante, 1_200_000)]   // machos de 12 a 17 meses: mantienen la clasificación Levante
    [InlineData(Sexo.Macho, Carne, 0, 14, Levante, 1_200_000)]
    [InlineData(Sexo.Macho, Carne, 0, 17, Levante, 1_200_000)]
    [InlineData(Sexo.Hembra, Lacteo, 1, 24, Vacas, 6_000_000)]    // con parto gana "Vacas" aunque "Novilla" también cubra el mes 24
    [InlineData(Sexo.Hembra, Carne, 2, 60, Vacas, 4_000_000)]
    [InlineData(Sexo.Hembra, Lacteo, 1, 20, Novilla, 3_200_000)]  // con parto pero menor de 24 meses: sigue como Novilla
    [InlineData(Sexo.Macho, Lacteo, 0, 18, Reproductor, 7_000_000)]
    [InlineData(Sexo.Macho, Carne, 0, 100, Reproductor, 7_000_000)]
    public void Asigna_la_categoria_y_el_valor_de_la_tabla_oficial(
        string sexo, int orientacion, int partos, int edad, int categoriaEsperada, int valorEsperado)
    {
        var tarifa = TarifaSelector.Seleccionar(Filas(), sexo, orientacion, partos, edad);

        tarifa.Should().NotBeNull();
        tarifa!.CategoriaId.Should().Be(categoriaEsperada);
        tarifa.ValorEstandar.Should().Be(valorEsperado);
    }

    [Theory]
    [InlineData(Sexo.Macho, Lacteo, 0, 1000)] // fuera de todo rango
    [InlineData(Sexo.Hembra, Carne, 0, 1000)]
    public void Devuelve_nulo_cuando_ninguna_tarifa_aplica(string sexo, int orientacion, int partos, int edad)
    {
        TarifaSelector.Seleccionar(Filas(), sexo, orientacion, partos, edad).Should().BeNull();
    }

    [Theory]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(17)]
    public void Sin_la_fila_de_Levante_para_machos_vuelve_el_hueco_de_12_a_17_meses(int edad)
    {
        // La matriz es editable: si el usuario quita esa fila, los machos de esas edades quedan sin tarifa.
        var sinLaFila = Filas().Where(f => f.Id != IdLevanteMachos12a17).ToList();

        TarifaSelector.Seleccionar(sinLaFila, Sexo.Macho, Lacteo, 0, edad).Should().BeNull();
        TarifaSelector.Seleccionar(sinLaFila, Sexo.Hembra, Lacteo, 0, edad).Should().NotBeNull("las hembras de esa edad siguen siendo novillas");
    }

    [Fact]
    public void Una_novilla_con_parto_pasa_a_Vacas_a_los_24_meses_y_una_sin_parto_sigue_como_novilla_para_siempre()
    {
        TarifaSelector.Seleccionar(Filas(), Sexo.Hembra, Lacteo, 1, 30)!.CategoriaId.Should().Be(Vacas);
        TarifaSelector.Seleccionar(Filas(), Sexo.Hembra, Lacteo, 0, 30)!.CategoriaId.Should().Be(Novilla);
        TarifaSelector.Seleccionar(Filas(), Sexo.Hembra, Carne, 0, 999)!.CategoriaId.Should().Be(Novilla);
    }

    // ---- Reglas de desempate ---------------------------------------------------------------------------------

    [Fact]
    public void Prefiere_la_orientacion_especifica_sobre_Todas()
    {
        var filas = new[]
        {
            Fila(1, Terneros, null, 0, 10, Sexo.Ambos, false, 100),
            Fila(2, Terneros, Lacteo, 0, 10, Sexo.Ambos, false, 200),
        };

        TarifaSelector.Seleccionar(filas, Sexo.Hembra, Lacteo, 0, 5)!.ValorEstandar.Should().Be(200);
        TarifaSelector.Seleccionar(filas, Sexo.Hembra, Carne, 0, 5)!.ValorEstandar.Should().Be(100, "para Carne solo aplica la genérica");
    }

    [Fact]
    public void La_orientacion_especifica_gana_aunque_su_edad_minima_sea_mayor()
    {
        var filas = new[]
        {
            Fila(1, Terneros, null, 0, 20, Sexo.Ambos, false, 100),
            Fila(2, Terneros, Lacteo, 10, 20, Sexo.Ambos, false, 200),
        };

        TarifaSelector.Seleccionar(filas, Sexo.Hembra, Lacteo, 0, 12)!.Id.Should().Be(2);
    }

    [Fact]
    public void Prefiere_la_fila_que_exige_partos_cuando_el_animal_los_tiene()
    {
        var filas = new[]
        {
            Fila(1, Novilla, null, 12, 100, Sexo.Hembra, false, 100),
            Fila(2, Vacas, null, 24, 100, Sexo.Hembra, true, 200),
        };

        TarifaSelector.Seleccionar(filas, Sexo.Hembra, Lacteo, 1, 30)!.Id.Should().Be(2);
        TarifaSelector.Seleccionar(filas, Sexo.Hembra, Lacteo, 0, 30)!.Id.Should().Be(1);
    }

    [Fact]
    public void Prefiere_el_sexo_especifico_sobre_Ambos()
    {
        var filas = new[]
        {
            Fila(1, Terneros, null, 0, 10, Sexo.Ambos, false, 100),
            Fila(2, Terneros, null, 0, 10, Sexo.Hembra, false, 200),
        };

        TarifaSelector.Seleccionar(filas, Sexo.Hembra, Lacteo, 0, 5)!.ValorEstandar.Should().Be(200);
        TarifaSelector.Seleccionar(filas, Sexo.Macho, Lacteo, 0, 5)!.ValorEstandar.Should().Be(100);
    }

    [Fact]
    public void A_igual_especificidad_prefiere_la_menor_edad_minima()
    {
        var filas = new[]
        {
            Fila(1, Terneros, null, 5, 20, Sexo.Ambos, false, 200),
            Fila(2, Terneros, null, 0, 10, Sexo.Ambos, false, 100),
        };

        TarifaSelector.Seleccionar(filas, Sexo.Hembra, Lacteo, 0, 7)!.ValorEstandar.Should().Be(100);
    }

    [Fact]
    public void Si_todo_empata_el_resultado_es_siempre_la_fila_de_menor_id()
    {
        var filas = new[]
        {
            Fila(7, Terneros, null, 0, 10, Sexo.Ambos, false, 700),
            Fila(3, Terneros, null, 0, 10, Sexo.Ambos, false, 300),
            Fila(5, Terneros, null, 0, 10, Sexo.Ambos, false, 500),
        };

        TarifaSelector.Seleccionar(filas, Sexo.Hembra, Lacteo, 0, 4)!.Id.Should().Be(3);
        TarifaSelector.Seleccionar(filas.Reverse(), Sexo.Hembra, Lacteo, 0, 4)!.Id.Should().Be(3);
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(24, true)]
    [InlineData(25, false)]
    public void Los_limites_del_rango_de_edad_son_inclusivos(int edad, bool aplica)
    {
        var filas = new[] { Fila(1, Novilla, null, 12, 24, Sexo.Hembra, false, 100) };

        (TarifaSelector.Seleccionar(filas, Sexo.Hembra, Lacteo, 0, edad) is not null).Should().Be(aplica);
    }

    [Fact]
    public void Un_animal_sin_partos_nunca_recibe_una_fila_que_los_exige()
    {
        var filas = new[] { Fila(1, Vacas, null, 0, 999, Sexo.Hembra, true, 100) };

        TarifaSelector.Seleccionar(filas, Sexo.Hembra, Lacteo, 0, 40).Should().BeNull();
        TarifaSelector.Seleccionar(filas, Sexo.Hembra, Lacteo, 1, 40).Should().NotBeNull();
    }

    [Fact]
    public void Una_tabla_vacia_no_asigna_nada()
    {
        TarifaSelector.Seleccionar([], Sexo.Hembra, Lacteo, 0, 5).Should().BeNull();
    }
}
