using UCaldas.Granjas.App.Infraestructura;

namespace UCaldas.Granjas.Tests.Presentacion;

public class FormatoTests
{
    [Theory]
    [InlineData(6_000_000, "$6.000.000")]
    [InlineData(150_000, "$150.000")]
    [InlineData(0, "$0")]
    [InlineData(-1_500, "-$1.500")]
    public void El_dinero_se_muestra_con_punto_de_miles_y_sin_decimales(decimal valor, string esperado)
        => Formato.Dinero(valor).Should().Be(esperado);

    [Theory]
    [InlineData(650_000, "+$650.000")]
    [InlineData(-120_000, "-$120.000")]
    [InlineData(0, "$0")]
    public void La_valorizacion_lleva_signo_explicito(decimal valor, string esperado)
        => Formato.DineroConSigno(valor).Should().Be(esperado);

    [Theory]
    [InlineData(1, "1 animal")]
    [InlineData(0, "0 animales")]
    [InlineData(2, "2 animales")]
    [InlineData(1_250, "1.250 animales")]
    public void Los_animales_concuerdan_en_singular_y_plural(int cantidad, string esperado)
        => Formato.Animales(cantidad).Should().Be(esperado);

    [Fact]
    public void Las_fechas_se_muestran_dia_mes_ano_y_la_hora_en_formato_de_12_horas()
    {
        Formato.Fecha(new DateTime(2026, 11, 3)).Should().Be("03/11/2026");
        Formato.FechaHora(new DateTime(2026, 11, 3, 8, 30, 0)).Should().Be("03/11/2026 08:30 AM");
        Formato.FechaHora(new DateTime(2026, 11, 3, 15, 5, 0)).Should().Be("03/11/2026 03:05 PM");
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2_048, "2 KB")]
    [InlineData(1_536, "1,5 KB")]
    [InlineData(5_242_880, "5 MB")]
    public void El_tamano_de_archivo_usa_la_unidad_adecuada(long bytes, string esperado)
        => Formato.Tamano(bytes).Should().Be(esperado);
}

public class FiltroMensualTests
{
    [Fact]
    public void Por_omision_va_de_enero_del_ano_actual_al_mes_actual()
    {
        var filtro = new FiltroMensual(new DateTime(2026, 11, 6));

        filtro.Desde.Should().Be(new DateTime(2026, 1, 1));
        filtro.Hasta.Should().Be(new DateTime(2026, 11, 30), "incluye el último día del mes final");
        filtro.Validar().Should().BeNull();
        filtro.Anios.Should().Contain(2026).And.BeInAscendingOrder();
    }

    [Fact]
    public void Febrero_de_un_ano_bisiesto_termina_el_29()
    {
        var filtro = new FiltroMensual(new DateTime(2028, 6, 1));

        filtro.MesHasta = FiltroMensual.Meses[1];

        filtro.Hasta.Should().Be(new DateTime(2028, 2, 29));
    }

    [Fact]
    public void Un_rango_invertido_se_detecta()
    {
        var filtro = new FiltroMensual(new DateTime(2026, 11, 6));

        filtro.MesDesde = FiltroMensual.Meses[11];
        filtro.AnioDesde = 2027;

        filtro.Validar().Should().Contain("mes inicial no puede ser posterior");
    }

    [Fact]
    public void Avisa_solo_cuando_el_usuario_cambia_un_combo_no_al_construirse()
    {
        var cambios = 0;
        var filtro = new FiltroMensual(new DateTime(2026, 11, 6));
        filtro.Cambio += () => cambios++;
        cambios.Should().Be(0);

        filtro.MesDesde = FiltroMensual.Meses[2];
        filtro.AnioHasta = 2025;

        cambios.Should().Be(2);
    }

    [Fact]
    public void Los_meses_se_listan_en_espanol_con_inicial_mayuscula()
    {
        FiltroMensual.Meses.Select(m => m.Nombre).Should().Equal(
            "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre");
        FiltroMensual.Meses.Select(m => m.Numero).Should().Equal(Enumerable.Range(1, 12));
    }
}

public class BarraDePestanasTests
{
    private static readonly Pestana Uno = new("1", "Uno", "contenido-1");
    private static readonly Pestana Dos = new("2", "Dos", "contenido-2");
    private static readonly Pestana Tres = new("3", "Tres", "contenido-3");

    // Las reversiones se difieren al contexto de la interfaz; en la prueba no hay ninguno y se ejecutan en línea.
    private static void SinContexto(Action accion)
    {
        var anterior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            accion();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(anterior);
        }
    }

    [Fact]
    public void Elegir_otra_pestana_la_confirma_y_avisa()
    {
        SinContexto(() =>
        {
            var barra = new BarraDePestanas([Uno, Dos, Tres], Uno);
            var confirmadas = new List<Pestana>();
            barra.CambioConfirmado += confirmadas.Add;

            barra.Seleccionada = Dos;

            barra.Seleccionada.Should().Be(Dos);
            confirmadas.Should().Equal(Dos);
        });
    }

    [Fact]
    public void Si_quien_la_posee_rechaza_el_cambio_se_restaura_la_pestana_anterior_sin_avisar()
    {
        SinContexto(() =>
        {
            var barra = new BarraDePestanas([Uno, Dos, Tres], Uno) { PuedeCambiar = _ => false };
            var confirmadas = new List<Pestana>();
            barra.CambioConfirmado += confirmadas.Add;

            barra.Seleccionada = Dos;

            barra.Seleccionada.Should().Be(Uno);
            confirmadas.Should().BeEmpty();
        });
    }

    [Fact]
    public void Una_seleccion_nula_del_ListBox_restaura_la_pestana_actual()
    {
        SinContexto(() =>
        {
            var barra = new BarraDePestanas([Uno, Dos], Dos);

            barra.Seleccionada = null;

            barra.Seleccionada.Should().Be(Dos);
        });
    }

    [Fact]
    public void Seleccionar_sin_notificar_no_consulta_ni_dispara_eventos()
    {
        SinContexto(() =>
        {
            var consultas = 0;
            var barra = new BarraDePestanas([Uno, Dos], Uno) { PuedeCambiar = _ => { consultas++; return false; } };
            var confirmadas = new List<Pestana>();
            barra.CambioConfirmado += confirmadas.Add;

            barra.SeleccionarSinNotificar(Dos);

            barra.Seleccionada.Should().Be(Dos);
            consultas.Should().Be(0);
            confirmadas.Should().BeEmpty();
        });
    }

    [Fact]
    public void Una_pestana_deshabilitada_conserva_su_ayuda()
    {
        var pestana = new Pestana("📦", "SUMINISTROS", null, Habilitada: false, Ayuda: "Módulo fuera del alcance de esta versión.");

        pestana.Habilitada.Should().BeFalse();
        pestana.Ayuda.Should().Contain("fuera del alcance");
    }
}
