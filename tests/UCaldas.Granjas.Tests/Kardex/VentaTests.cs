using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

public class VentaTests
{
    [Fact]
    public async Task Registra_una_venta_con_oficio_remision_y_recibo_de_caja()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("0940-26", valor: 6_000_000m);
        await e.AltaVacaAsync("0941-26", valor: 3_750_000m);
        var dto = await e.VentaAsync("REM-0012", "OF-120", "RC-8841", null, ("0940-26", 3_750_000m), ("0941-26", 3_900_000m));

        var resultado = await e.Kardex.RegistrarVentaAsync(dto);

        resultado.Animales.Should().Be(2);
        resultado.TotalCostoSistema.Should().Be(9_750_000m, "costo en libros: 6.000.000 + 3.750.000");
        resultado.TotalVenta.Should().Be(7_650_000m, "valor venta real: 3.750.000 + 3.900.000");
        resultado.OficioId.Should().NotBeNull();

        var remision = await e.RemisionAsync("REM-0012");
        remision.TipoMovimiento.Should().Be(TipoRemision.Venta);
        remision.ClaseSalida.Should().Be(ClaseSalida.Venta);
        remision.ReciboCaja.Should().Be("RC-8841");
        remision.OficioId.Should().Be(resultado.OficioId);
        remision.GranjaOrigenId.Should().Be(e.Ids.Tesorito);
        remision.GranjaDestinoId.Should().BeNull();
        remision.TerceroEntidad.Should().Be("Frigorífico Manizales");
        remision.FechaRemision.Should().Be(Datos.F("2026-10-20"));

        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            var oficio = (await uow.Oficios.GetByNumeroAsync("OF-120"))!;
            oficio.ProgramaId.Should().Be(e.Ids.ProgramaGanaderia);
            oficio.FechaOficio.Should().Be(Datos.F("2026-10-15"));
        }

        var detalles = await e.DetallesAsync("REM-0012");
        detalles.Should().HaveCount(2);
        detalles.Sum(d => d.CostoSistemaLibros).Should().Be(9_750_000m);
        detalles.Sum(d => d.ValorTotalTransaccion ?? 0m).Should().Be(7_650_000m);

        (await e.AnimalAsync("0940-26")).Estado.Should().Be(EstadoAnimal.Vendido);
        (await e.AnimalAsync("0941-26")).Estado.Should().Be(EstadoAnimal.Vendido);

        var asiento = (await e.MovimientosAsync("0940-26")).Single(m => m.TipoMovimiento == TipoMovimiento.Venta);
        asiento.CostoAsociado.Should().Be(-6_000_000m, "la salida resta el costo en libros");
        asiento.GranjaOrigenId.Should().Be(e.Ids.Tesorito);
        asiento.GranjaDestinoId.Should().BeNull();
        asiento.FechaMovimiento.Should().Be(Datos.F("2026-10-20"));
        asiento.RemisionId.Should().Be(remision.Id);

        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_oficio_existente_se_reutiliza_para_varias_remisiones()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.AltaVacaAsync("A-2");

        var primera = await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-123", "RC-1", null, ("A-1", 1_000_000m)));
        var segunda = await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-2", "of-123", "RC-2", null, ("A-2", 1_000_000m)));

        segunda.OficioId.Should().Be(primera.OficioId);
        (await e.ContarAsync("OFICIO_VENTA")).Should().Be(1);
        (await e.RemisionAsync("REM-1")).ReciboCaja.Should().Be("RC-1");
        (await e.RemisionAsync("REM-2")).ReciboCaja.Should().Be("RC-2", "cada remisión lleva su propio recibo de caja");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_oficio_solo_puede_tener_remisiones_de_un_solo_programa()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.Db.EjecutarAsync(
            "INSERT INTO OFICIO_VENTA (numero_oficio, programa_id, fecha_oficio) " +
            "SELECT 'OF-9', id, '2026-10-01 00:00:00' FROM PROGRAMA WHERE codigo = 'PRG-AVE'");

        var oficioDeAves = await e.VentaAsync("REM-1", "OF-9", "RC-1", null, ("A-1", 1_000_000m));
        var act = () => e.Kardex.RegistrarVentaAsync(oficioDeAves);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*pertenece al programa «Avicultura (Aves)»*un solo programa*");
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Disponible);
    }

    [Fact]
    public async Task Los_animales_de_la_venta_deben_ser_del_programa_del_oficio()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        var ave = await e.Db.EscalarAsync<int>("SELECT id FROM PROGRAMA WHERE codigo = 'PRG-AVE'");
        var dto = await e.VentaAsync("REM-1", "OF-NUEVO", "RC-1", null, ("A-1", 1_000_000m));
        dto.ProgramaId = ave;

        var act = () => e.Kardex.RegistrarVentaAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*A-1 no pertenece al programa del oficio OF-NUEVO*");
        (await e.ContarAsync("OFICIO_VENTA")).Should().Be(0, "el oficio recién creado se revierte con la transacción");
        (await e.ContarAsync("REMISION")).Should().Be(0);
    }

    [Theory]
    [InlineData("oficio", "*número de oficio*")]
    [InlineData("recibo", "*recibo de caja*")]
    [InlineData("remision", "*número de remisión*")]
    public async Task La_venta_se_rechaza_sin_oficio_recibo_o_remision_y_no_cambia_nada(string campo, string mensaje)
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        var dto = await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 1_000_000m));
        switch (campo)
        {
            case "oficio": dto.NumeroOficio = ""; break;
            case "recibo": dto.ReciboCaja = " "; break;
            case "remision": dto.NumeroRemision = ""; break;
        }

        var act = () => e.Kardex.RegistrarVentaAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Disponible);
        (await e.ContarAsync("REMISION")).Should().Be(0);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_oficio_nuevo_exige_su_fecha()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        var dto = await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 1_000_000m));
        dto.FechaOficio = default;

        var act = () => e.Kardex.RegistrarVentaAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*es nuevo: indique su fecha*");
    }

    [Fact]
    public async Task Rechaza_animales_vendidos_de_otra_granja_o_inexistentes_sin_dejar_nada()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.AltaVacaAsync("B-1", granja: e.Ids.LaCruz);
        var vendido = await e.AltaVacaAsync("C-1");
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-0", "OF-0", "RC-0", null, ("C-1", 1_000_000m)));
        var remisionesAntes = await e.ContarAsync("REMISION");

        var yaVendido = await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("C-1", 1_000_000m));
        var otraGranja = await e.VentaAsync("REM-2", "OF-1", "RC-2", null, ("B-1", 1_000_000m));
        var inexistente = await e.VentaAsync("REM-3", "OF-1", "RC-3", null, ("A-1", 1_000_000m));
        inexistente.Animales[0].AnimalId = 99999;

        await ((Func<Task>)(() => e.Kardex.RegistrarVentaAsync(yaVendido))).Should().ThrowAsync<ValidationException>().WithMessage("*C-1 no está disponible (estado: Vendido)*");
        await ((Func<Task>)(() => e.Kardex.RegistrarVentaAsync(otraGranja))).Should().ThrowAsync<ValidationException>().WithMessage("*B-1 no se encuentra en la granja de origen*");
        await ((Func<Task>)(() => e.Kardex.RegistrarVentaAsync(inexistente))).Should().ThrowAsync<ValidationException>().WithMessage("*99999 no existe*");

        (await e.ContarAsync("REMISION")).Should().Be(remisionesAntes);
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Disponible);
        vendido.Id.Should().BeGreaterThan(0);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task La_remision_no_puede_repetir_el_numero_de_otro_documento()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.AltaVacaAsync("A-2");
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-0012", "OF-1", "RC-1", null, ("A-1", 1_000_000m)));

        var act = async () => await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("rem-0012", "OF-1", "RC-2", null, ("A-2", 1_000_000m)));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe un documento con el número rem-0012*");
    }

    [Fact]
    public async Task El_costo_sistema_es_el_valor_contable_vigente_despues_de_valorizar()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaTerneraAsync("T-1", nacimiento: "2026-07-15", valor: 150_000m);
        await e.Aging.ValorizarAlCorteAsync(Datos.F("2026-10-31")); // cumple 3 meses: 150.000 → 800.000

        var resultado = await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("T-1", 900_000m)));

        resultado.TotalCostoSistema.Should().Be(800_000m);
        resultado.TotalVenta.Should().Be(900_000m);
        (await e.MovimientosAsync("T-1")).Single(m => m.TipoMovimiento == TipoMovimiento.Venta).CostoAsociado.Should().Be(-800_000m);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Una_venta_ya_registrada_no_se_puede_volver_a_registrar_como_nueva()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        var dto = await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 1_000_000m));
        dto.RemisionId = 5;

        var act = () => e.Kardex.RegistrarVentaAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*use la edición*");
    }

    [Fact]
    public async Task Una_venta_inactiva_la_granja_de_origen_se_rechaza()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.Db.EjecutarAsync("UPDATE GRANJA SET activa = 0 WHERE codigo = 'G-01'");

        var act = async () => await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 1_000_000m)));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*inactiva*");
    }
}
