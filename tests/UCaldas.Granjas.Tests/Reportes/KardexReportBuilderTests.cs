using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Reportes;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Reportes;

public class KardexReportBuilderTests
{
    [Fact]
    public async Task Reproduce_las_cifras_del_Kardex_oficial_de_La_Cruz_de_septiembre_2026()
    {
        await using var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync();

        var kardex = (await new KardexReportBuilder(e.Db.Uow).ConstruirAsync(2026, 9, e.Ids.LaCruz)).Single();

        kardex.GranjaNombre.Should().Be("La Cruz");
        kardex.NombreDelMes.Should().Be("SEPTIEMBRE");
        kardex.SaldoAnteriorCantidad.Should().Be(99);
        kardex.SaldoAnteriorValor.Should().Be(65_947_815m);
        kardex.Nacimientos.Select(n => n.Chapeta).Should().Equal(EscenariosDeReporte.ChapetasDeSeptiembre);
        kardex.Nacimientos.Should().OnlyContain(n => n.Valor == 52_500m);
        kardex.Nacimientos.Sum(n => n.Valor).Should().Be(472_500m);
        kardex.Ventas.Should().BeEmpty();
        kardex.Compras.Should().BeEmpty();
        kardex.Muertes.Should().BeEmpty();
        kardex.Llegados.Should().BeEmpty();
        kardex.Traslados.Should().BeEmpty();
        kardex.Valorizacion.Should().Be(-450_620m);
        kardex.SaldoFinalCantidad.Should().Be(108);
        kardex.SaldoFinalValor.Should().Be(65_969_695m);
    }

    private static Task<EscenarioKardex> CrearOctubreAsync() => EscenariosDeReporte.CrearOctubreDeTesoritoAsync();

    [Fact]
    public async Task Arma_todas_las_secciones_del_Kardex_de_una_granja_en_un_mes()
    {
        await using var e = await CrearOctubreAsync();

        var tesorito = (await new KardexReportBuilder(e.Db.Uow).ConstruirAsync(2026, 10, e.Ids.Tesorito)).Single();

        tesorito.SaldoAnteriorCantidad.Should().Be(4);
        tesorito.SaldoAnteriorValor.Should().Be(13_900_000m, "6.000.000 + 3.750.000 + 4.000.000 + 150.000");
        tesorito.Ventas.Should().ContainSingle().Which.Should().Be(new LineaVenta("A-1", 6_000_000m, 6_500_000m));
        tesorito.Compras.Should().BeEquivalentTo(new[]
        {
            new LineaCompra("P-1", 6_000_000m, 3_200_000m),
            new LineaCompra("P-2", 800_000m, 150_000m),
        });
        tesorito.Nacimientos.Should().ContainSingle().Which.Should().Be(new LineaValor("N-1", 150_000m));
        tesorito.Muertes.Should().ContainSingle().Which.Should().Be(new LineaValor("A-2", 3_750_000m));
        tesorito.Traslados.Should().ContainSingle().Which.Should().Be(new LineaValor("A-3", 4_000_000m));
        tesorito.Llegados.Should().BeEmpty();
        tesorito.Valorizacion.Should().Be(650_000m);
        tesorito.SaldoFinalCantidad.Should().Be(4);
        tesorito.SaldoFinalValor.Should().Be(7_750_000m);

        // A-3 llegó a La Cruz con 4 M y el cierre de octubre la valorizó a 6 M (vaca lechera con partos): ese delta
        // pertenece a La Cruz, que es donde estaba el animal al cierre, y no a Tesorito.
        var laCruz = (await new KardexReportBuilder(e.Db.Uow).ConstruirAsync(2026, 10, e.Ids.LaCruz)).Single();
        laCruz.Llegados.Should().ContainSingle().Which.Should().Be(new LineaValor("A-3", 4_000_000m));
        laCruz.SaldoAnteriorCantidad.Should().Be(0);
        laCruz.Valorizacion.Should().Be(2_000_000m);
        laCruz.SaldoFinalCantidad.Should().Be(1);
        laCruz.SaldoFinalValor.Should().Be(6_000_000m);
    }

    [Fact]
    public async Task El_saldo_final_de_cada_granja_es_el_valor_de_sus_animales_disponibles()
    {
        await using var e = await CrearOctubreAsync();

        var granjas = await new KardexReportBuilder(e.Db.Uow).ConstruirAsync(2026, 10, null);

        await using var uow = await e.Db.Uow.OpenReadOnlyAsync();
        var animales = await uow.Animales.GetByEstadoAsync(EstadoAnimal.Disponible);
        foreach (var granja in granjas)
        {
            var delaGranja = animales.Where(a => a.GranjaId == granja.GranjaId).ToList();
            granja.SaldoFinalCantidad.Should().Be(delaGranja.Count, granja.GranjaNombre);
            granja.SaldoFinalValor.Should().Be(delaGranja.Sum(a => a.ValorContableActual), granja.GranjaNombre);
        }
    }

    [Fact]
    public async Task El_saldo_inicial_de_un_mes_es_el_saldo_final_del_anterior()
    {
        await using var e = await CrearOctubreAsync();
        var constructor = new KardexReportBuilder(e.Db.Uow);

        var octubre = (await constructor.ConstruirAsync(2026, 10, e.Ids.Tesorito)).Single();
        var noviembre = (await constructor.ConstruirAsync(2026, 11, e.Ids.Tesorito)).Single();
        var septiembre = (await constructor.ConstruirAsync(2026, 9, e.Ids.Tesorito)).Single();

        noviembre.SaldoAnteriorCantidad.Should().Be(octubre.SaldoFinalCantidad);
        noviembre.SaldoAnteriorValor.Should().Be(octubre.SaldoFinalValor);
        septiembre.SaldoFinalValor.Should().Be(octubre.SaldoAnteriorValor);
        noviembre.Ventas.Should().BeEmpty("los movimientos de octubre no se repiten en noviembre");
        noviembre.SaldoFinalValor.Should().Be(noviembre.SaldoAnteriorValor);
    }

    [Fact]
    public async Task Los_movimientos_del_ultimo_y_del_primer_dia_del_mes_caen_en_su_mes()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-11-06");
        await e.AltaVacaAsync("A-1", valor: 1_000_000m);
        await e.AltaVacaAsync("A-2", valor: 2_000_000m);
        await e.AltaVacaAsync("A-3", valor: 3_000_000m);
        var fin = await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 1_000_000m));
        fin.FechaRemision = Datos.F("2026-09-30");
        var inicio = await e.VentaAsync("REM-2", "OF-1", "RC-2", null, ("A-2", 2_000_000m));
        inicio.FechaRemision = Datos.F("2026-10-01");
        await e.Kardex.RegistrarVentaAsync(fin);
        await e.Kardex.RegistrarVentaAsync(inicio);

        var constructor = new KardexReportBuilder(e.Db.Uow);
        var septiembre = (await constructor.ConstruirAsync(2026, 9, e.Ids.Tesorito)).Single();
        var octubre = (await constructor.ConstruirAsync(2026, 10, e.Ids.Tesorito)).Single();

        septiembre.Ventas.Select(v => v.Chapeta).Should().Equal("A-1");
        octubre.Ventas.Select(v => v.Chapeta).Should().Equal("A-2");
        octubre.SaldoAnteriorCantidad.Should().Be(2);
        octubre.SaldoFinalCantidad.Should().Be(1);
    }

    [Fact]
    public async Task Una_baja_por_hurto_se_agrupa_con_las_muertes()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 3_000_000m);
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Hurto, null, "A-1"));
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-2", ClaseSalida.DescarteSanitario, null, "A-2"));

        var kardex = (await new KardexReportBuilder(e.Db.Uow).ConstruirAsync(2026, 10, e.Ids.Tesorito)).Single();

        kardex.Muertes.Select(m => m.Chapeta).Should().Equal("A-1", "A-2");
        kardex.Muertes.Sum(m => m.Valor).Should().Be(9_000_000m);
        kardex.SaldoFinalCantidad.Should().Be(0);
    }

    [Fact]
    public async Task Todas_las_granjas_incluye_las_activas_y_omite_las_inactivas_sin_movimientos()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Db.EjecutarAsync("UPDATE GRANJA SET activa = 0 WHERE codigo = 'G-03'");

        var granjas = await new KardexReportBuilder(e.Db.Uow).ConstruirAsync(2026, 10, null);

        granjas.Select(g => g.GranjaNombre).Should().Equal("Tesorito", "La Cruz");
    }

    [Theory]
    [InlineData(1999, 5, "*año*")]
    [InlineData(2026, 0, "*mes*")]
    [InlineData(2026, 13, "*mes*")]
    public async Task Rechaza_periodos_invalidos(int anio, int mes, string mensaje)
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var act = () => new KardexReportBuilder(e.Db.Uow).ConstruirAsync(anio, mes, null);

        await act.Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
    }

    [Fact]
    public async Task Rechaza_una_granja_inexistente()
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var act = () => new KardexReportBuilder(e.Db.Uow).ConstruirAsync(2026, 10, 999);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*granja seleccionada no existe*");
    }
}
