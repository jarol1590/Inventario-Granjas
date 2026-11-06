using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

public class AnularTests
{
    [Fact]
    public async Task Anular_una_venta_devuelve_los_animales_al_inventario_y_deja_el_documento_marcado()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 3_750_000m);
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 6_500_000m), ("A-2", 4_000_000m)));
        var remisionId = (await e.RemisionAsync("REM-1")).Id;

        await e.Kardex.AnularAsync(remisionId, "El comprador desistió");

        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Disponible);
        (await e.AnimalAsync("A-2")).Estado.Should().Be(EstadoAnimal.Disponible);
        (await e.MovimientosAsync("A-1")).Should().ContainSingle("solo queda el asiento de entrada");
        (await e.DetallesAsync("REM-1")).Should().BeEmpty();
        (await e.MovimientosDeRemisionAsync("REM-1")).Should().BeEmpty();

        var remision = await e.RemisionAsync("REM-1");
        remision.Anulada.Should().BeTrue();
        remision.Observaciones.Should().Contain("ANULADA").And.Contain("El comprador desistió").And.Contain("Venta de prueba");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task El_numero_de_un_documento_anulado_queda_consumido_pero_los_animales_se_pueden_vender_de_nuevo()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 1_000_000m)));
        await e.Kardex.AnularAsync((await e.RemisionAsync("REM-1")).Id);

        var mismoNumero = async () => await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-2", null, ("A-1", 1_000_000m)));
        await mismoNumero.Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe un documento con el número REM-1*");

        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-2", "OF-1", "RC-2", null, ("A-1", 1_100_000m)));
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Vendido);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Anular_una_baja_devuelve_el_animal_a_disponible()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Hurto, null, "A-1"));

        await e.Kardex.AnularAsync((await e.RemisionAsync("REM-B-1")).Id);

        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Disponible);
        (await e.MovimientosAsync("A-1")).Should().ContainSingle();
        (await e.RemisionAsync("REM-B-1")).Anulada.Should().BeTrue();
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Anular_un_traslado_devuelve_los_animales_a_su_granja_y_a_su_orientacion()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        var dto = await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.Montelindo, "A-1");
        dto.Animales[0].OrientacionDestinoId = e.Ids.Carne;
        await e.Kardex.RegistrarTrasladoAsync(dto);

        await e.Kardex.AnularAsync((await e.RemisionAsync("REM-T-1")).Id);

        var animal = await e.AnimalAsync("A-1");
        animal.GranjaId.Should().Be(e.Ids.Tesorito);
        animal.OrientacionId.Should().Be(e.Ids.Lacteo);
        (await e.MovimientosAsync("A-1")).Should().ContainSingle();
        (await e.MovimientosDeRemisionAsync("REM-T-1")).Should().BeEmpty();
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Anular_un_traslado_con_movimientos_posteriores_se_rechaza()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-11-30");
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "A-1"));
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-2", e.Ids.LaCruz, e.Ids.Montelindo, "A-1"));

        var act = () => e.Kardex.AnularAsync(e.RemisionAsync("REM-T-1").Result.Id);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*movimientos posteriores*Traslado*anular el traslado*");
        (await e.AnimalAsync("A-1")).GranjaId.Should().Be(e.Ids.Montelindo);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Anular_una_compra_elimina_los_animales_y_libera_sus_chapetas()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarCompraAsync(e.Compra(
            "FAC-1",
            e.AnimalComprado("A-1", Sexo.Hembra, "2022-03-01", 3_000_000m, partos: 1),
            e.AnimalComprado("A-2", Sexo.Hembra, "2026-06-20", 150_000m)));

        await e.Kardex.AnularAsync((await e.RemisionAsync("FAC-1")).Id, "Factura equivocada");

        (await e.ContarAsync("ANIMAL")).Should().Be(0);
        (await e.ContarAsync("MOVIMIENTO_INVENTARIO")).Should().Be(0);
        (await e.ContarAsync("REMISION_DETALLE")).Should().Be(0);
        (await e.RemisionAsync("FAC-1")).Anulada.Should().BeTrue();

        // Las chapetas quedan libres para registrar la compra correcta con otra factura.
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-2", e.AnimalComprado("A-1", Sexo.Hembra, "2022-03-01", 3_100_000m, partos: 1)));
        (await e.AnimalAsync("A-1")).ValorCompraOrigen.Should().Be(3_100_000m);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task No_se_puede_anular_una_compra_si_un_animal_ya_tiene_movimientos_posteriores()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("A-1", Sexo.Hembra, "2022-03-01", 3_000_000m, partos: 1)));
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 6_000_000m)));

        var act = () => e.Kardex.AnularAsync(e.RemisionAsync("FAC-1").Result.Id);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*A-1 ya tiene movimientos posteriores*Venta*anular la compra*");
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Vendido);
        (await e.RemisionAsync("FAC-1")).Anulada.Should().BeFalse();
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task No_se_puede_anular_una_compra_de_un_animal_que_es_padre_de_otro()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("T-1", Sexo.Macho, "2021-01-01", 7_000_000m)));
        var cria = e.Nacimiento("0940-26");
        cria.PadreChapeta = "T-1";
        await e.Kardex.RegistrarNacimientoAsync(cria);

        var act = () => e.Kardex.AnularAsync(e.RemisionAsync("FAC-1").Result.Id);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*T-1 figura como padre de otro animal*");
        (await e.ExisteAnimalAsync("T-1")).Should().BeTrue();
    }

    [Fact]
    public async Task Anular_dos_veces_o_un_documento_inexistente_falla()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-1"));
        var id = (await e.RemisionAsync("REM-B-1")).Id;
        await e.Kardex.AnularAsync(id);

        var otraVez = () => e.Kardex.AnularAsync(id);
        var inexistente = () => e.Kardex.AnularAsync(99999);

        await otraVez.Should().ThrowAsync<ValidationException>().WithMessage("*ya está anulado*");
        await inexistente.Should().ThrowAsync<ValidationException>().WithMessage("*no existe*");
    }

    [Fact]
    public async Task Un_documento_anulado_no_se_puede_editar()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 1_000_000m)));
        var id = (await e.RemisionAsync("REM-1")).Id;
        await e.Kardex.AnularAsync(id);
        var dto = await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 1_200_000m));
        dto.RemisionId = id;

        var act = () => e.Kardex.EditarVentaAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*ya está anulado*");
    }
}
