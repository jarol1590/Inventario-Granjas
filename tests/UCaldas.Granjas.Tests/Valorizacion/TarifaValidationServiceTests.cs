using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Valorizacion;
using static UCaldas.Granjas.Tests.Infraestructura.Matriz2026;

namespace UCaldas.Granjas.Tests.Valorizacion;

public class TarifaValidationServiceTests
{
    private readonly TarifaValidationService _servicio = new();

    private void Validar(IReadOnlyCollection<TablaValorizacionBase> filas)
        => _servicio.ValidarMatriz(filas, Categorias(), Orientaciones());

    // ---- Fórmula: existe solapamiento si Max(MinA, MinB) <= Min(MaxA, MaxB) ----------------------------------

    [Theory]
    [InlineData(0, 5, 3, 8, true)]    // cruce parcial
    [InlineData(0, 5, 5, 10, true)]   // se tocan en un mes: rangos cerrados, hay cruce
    [InlineData(0, 5, 6, 10, false)]  // consecutivos sin tocarse
    [InlineData(0, 10, 2, 4, true)]   // uno contiene al otro
    [InlineData(2, 4, 0, 10, true)]   // ídem, invertido
    [InlineData(2, 4, 2, 4, true)]    // idénticos
    [InlineData(0, 0, 0, 0, true)]    // un solo mes
    [InlineData(0, 2, 3, 7, false)]
    public void Detecta_el_solapamiento_de_rangos_de_edad(int minA, int maxA, int minB, int maxB, bool esperado)
    {
        var a = Fila(1, Terneros, null, minA, maxA, Sexo.Hembra, false, 100);
        var b = Fila(2, Terneros, null, minB, maxB, Sexo.Hembra, false, 100);

        _servicio.HaySolapamiento(a, b).Should().Be(esperado);
        _servicio.HaySolapamiento(b, a).Should().Be(esperado, "la comparación es simétrica");
    }

    [Fact]
    public void Filas_que_difieren_en_sexo_partos_u_orientacion_pueden_cruzarse()
    {
        var baseA = Fila(1, Vacas, Lacteo, 0, 50, Sexo.Hembra, false, 100);

        _servicio.HaySolapamiento(baseA, Fila(2, Vacas, Lacteo, 0, 50, Sexo.Macho, false, 100)).Should().BeFalse("distinto sexo");
        _servicio.HaySolapamiento(baseA, Fila(3, Vacas, Lacteo, 0, 50, Sexo.Hembra, true, 100)).Should().BeFalse("distinta historia productiva");
        _servicio.HaySolapamiento(baseA, Fila(4, Vacas, Carne, 0, 50, Sexo.Hembra, false, 100)).Should().BeFalse("otra orientación");
        _servicio.HaySolapamiento(baseA, Fila(5, Vacas, null, 0, 50, Sexo.Hembra, false, 100)).Should().BeFalse("'Todas' se resuelve por desempate");
        _servicio.HaySolapamiento(baseA, Fila(6, Vacas, Lacteo, 0, 50, Sexo.Ambos, false, 100)).Should().BeFalse("'Ambos' se resuelve por desempate");
        _servicio.HaySolapamiento(baseA, Fila(7, Vacas, Lacteo, 10, 20, Sexo.Hembra, false, 100)).Should().BeTrue();
    }

    // ---- Matriz completa ----------------------------------------------------------------------------------------

    [Fact]
    public void La_matriz_oficial_2026_es_valida()
    {
        var act = () => Validar(Filas().ToList());

        act.Should().NotThrow();
    }

    [Fact]
    public void Una_matriz_vacia_es_valida()
    {
        var act = () => Validar([]);

        act.Should().NotThrow();
    }

    [Fact]
    public void Un_solapamiento_bloquea_el_guardado_y_describe_las_dos_filas_en_conflicto()
    {
        var filas = Filas().ToList();
        filas.Add(Fila(10, Vacas, Lacteo, 30, 40, Sexo.Hembra, true, 5_500_000));

        var act = () => Validar(filas);

        var excepcion = act.Should().Throw<SolapamientoDeTarifasException>().Which;
        excepcion.Should().BeAssignableTo<ValidationException>();
        excepcion.Conflictos.Should().ContainSingle();
        excepcion.Conflictos[0].DesdeMes.Should().Be(30);
        excepcion.Conflictos[0].HastaMes.Should().Be(40);
        excepcion.Message.Should().Contain("Vacas General").And.Contain("24–999").And.Contain("30–40")
            .And.Contain("Lácteo").And.Contain("meses 30 a 40").And.Contain("con partos");
    }

    [Fact]
    public void Informa_todos_los_solapamientos_y_no_solo_el_primero()
    {
        var filas = new[]
        {
            Fila(1, Terneros, null, 0, 10, Sexo.Hembra, false, 100),
            Fila(2, Terneros, null, 5, 15, Sexo.Hembra, false, 100),
            Fila(3, Terneros, null, 8, 20, Sexo.Hembra, false, 100),
        };

        var act = () => Validar(filas);

        var conflictos = act.Should().Throw<SolapamientoDeTarifasException>().Which.Conflictos;
        conflictos.Should().HaveCount(3);
        conflictos.Select(c => (c.DesdeMes, c.HastaMes)).Should().BeEquivalentTo(new[] { (5, 10), (8, 10), (8, 15) });
    }

    [Fact]
    public void Si_el_cruce_es_de_un_solo_mes_el_mensaje_lo_dice_en_singular()
    {
        var filas = new[]
        {
            Fila(1, Terneros, null, 0, 5, Sexo.Hembra, false, 100),
            Fila(2, Terneros, null, 5, 9, Sexo.Hembra, false, 100),
        };

        var act = () => Validar(filas);

        act.Should().Throw<SolapamientoDeTarifasException>().WithMessage("*en el mes 5.*");
    }

    // ---- Datos de cada fila ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10, 5, 100, Sexo.Hembra, "*edad máxima*menor que la mínima*")]
    [InlineData(-1, 5, 100, Sexo.Hembra, "*edad mínima no puede ser negativa*")]
    [InlineData(0, 5, 0, Sexo.Hembra, "*valor base debe ser mayor que cero*")]
    [InlineData(0, 5, -50, Sexo.Hembra, "*valor base debe ser mayor que cero*")]
    [InlineData(0, 5, 100, "Otro", "*sexo debe ser Macho, Hembra o Ambos*")]
    public void Rechaza_filas_con_datos_invalidos(int min, int max, int valor, string sexo, string mensaje)
    {
        var fila = Fila(1, Terneros, null, min, max, sexo, false, valor);

        var act = () => Validar([fila]);

        act.Should().Throw<ValidationException>().WithMessage(mensaje);
    }

    [Fact]
    public void Rechaza_una_categoria_o_una_orientacion_inexistentes()
    {
        var sinCategoria = Fila(1, 999, null, 0, 5, Sexo.Ambos, false, 100);
        var sinOrientacion = Fila(2, Terneros, 999, 0, 5, Sexo.Ambos, false, 100);

        var a = () => Validar([sinCategoria]);
        var b = () => Validar([sinOrientacion]);

        a.Should().Throw<ValidationException>().WithMessage("*categoría no existe*");
        b.Should().Throw<ValidationException>().WithMessage("*orientación no existe*");
    }

    [Theory]
    [InlineData(Novilla, Sexo.Macho)]
    [InlineData(Novilla, Sexo.Ambos)]
    [InlineData(Reproductor, Sexo.Hembra)]
    public void Una_categoria_exclusiva_de_un_sexo_no_acepta_tarifas_de_otro(int categoria, string sexo)
    {
        var fila = Fila(1, categoria, null, 0, 5, sexo, false, 100);

        var act = () => Validar([fila]);

        act.Should().Throw<ValidationException>().WithMessage("*es de sexo*pero la fila indica*");
    }

    [Fact]
    public void El_mensaje_identifica_el_numero_de_la_fila_invalida()
    {
        var filas = new[]
        {
            Fila(1, Terneros, null, 0, 5, Sexo.Ambos, false, 100),
            Fila(2, Levante, null, 9, 3, Sexo.Ambos, false, 100),
        };

        var act = () => Validar(filas);

        act.Should().Throw<ValidationException>().WithMessage("Fila 2 de la matriz*");
    }

    // ---- Una sola fila nueva o editada --------------------------------------------------------------------------

    [Fact]
    public void ValidarFila_ignora_la_propia_fila_al_editarla()
    {
        var existentes = Filas().ToList();
        var editada = Fila(7, Vacas, Lacteo, 24, 999, Sexo.Hembra, true, 6_500_000);

        var act = () => _servicio.ValidarFila(editada, existentes, Categorias(), Orientaciones());

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidarFila_detecta_el_cruce_de_una_fila_nueva_con_las_existentes()
    {
        var existentes = Filas().ToList();
        var nueva = Fila(0, Vacas, Lacteo, 100, 200, Sexo.Hembra, true, 5_000_000);

        var act = () => _servicio.ValidarFila(nueva, existentes, Categorias(), Orientaciones());

        act.Should().Throw<SolapamientoDeTarifasException>().Which.Conflictos.Should().ContainSingle();
    }

    [Fact]
    public void ValidarFila_detecta_que_una_edicion_provoca_un_cruce()
    {
        var existentes = Filas().ToList();
        var editada = Fila(5, Novilla, Lacteo, 12, 30, Sexo.Hembra, false, 3_200_000);
        existentes.Add(Fila(11, Novilla, Lacteo, 25, 40, Sexo.Hembra, false, 3_000_000));

        var act = () => _servicio.ValidarFila(editada, existentes, Categorias(), Orientaciones());

        act.Should().Throw<SolapamientoDeTarifasException>();
    }
}
