using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

public class BajaYTrasladoTests
{
    // ---- Bajas ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ClaseSalida.Muerte, EstadoAnimal.Muerto, TipoMovimiento.Muerte)]
    [InlineData(ClaseSalida.Hurto, EstadoAnimal.Hurtado, TipoMovimiento.Hurto)]
    [InlineData(ClaseSalida.DescarteSanitario, EstadoAnimal.Muerto, TipoMovimiento.Muerte)]
    public async Task Una_baja_solo_lleva_remision_cambia_el_estado_y_descuenta_el_costo_en_libros(
        string clase, string estadoEsperado, string movimientoEsperado)
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("0940-26", valor: 6_000_000m);
        await e.AltaVacaAsync("0941-26", valor: 3_750_000m);

        var resultado = await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", clase, null, "0940-26"));

        resultado.Animales.Should().Be(1);
        resultado.TotalCostoSistema.Should().Be(6_000_000m);
        resultado.TotalVenta.Should().Be(0m);
        resultado.OficioId.Should().BeNull();

        var remision = await e.RemisionAsync("REM-B-1");
        remision.TipoMovimiento.Should().Be(TipoRemision.Baja);
        remision.ClaseSalida.Should().Be(clase);
        remision.OficioId.Should().BeNull("las bajas no llevan oficio");
        remision.ReciboCaja.Should().BeNull("las bajas no llevan recibo de caja");
        remision.TerceroEntidad.Should().Be("Acta Sanitaria N°4");
        remision.GranjaOrigenId.Should().Be(e.Ids.Tesorito);

        (await e.AnimalAsync("0940-26")).Estado.Should().Be(estadoEsperado);
        (await e.AnimalAsync("0941-26")).Estado.Should().Be(EstadoAnimal.Disponible);

        var asiento = (await e.MovimientosAsync("0940-26")).Single(m => m.TipoMovimiento == movimientoEsperado);
        asiento.CostoAsociado.Should().Be(-6_000_000m);
        asiento.GranjaOrigenId.Should().Be(e.Ids.Tesorito);
        asiento.FechaMovimiento.Should().Be(Datos.F("2026-10-15"));

        var detalle = (await e.DetallesAsync("REM-B-1")).Single();
        detalle.CostoSistemaLibros.Should().Be(6_000_000m);
        detalle.ValorTotalTransaccion.Should().BeNull("una baja no tiene precio de venta");

        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Una_baja_rechaza_animales_no_disponibles_o_de_otra_granja()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.AltaVacaAsync("B-1", granja: e.Ids.LaCruz);
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-0", ClaseSalida.Muerte, null, "A-1"));

        var yaBaja = await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-1");
        var otraGranja = await e.BajaAsync("REM-B-2", ClaseSalida.Hurto, null, "B-1");

        await ((Func<Task>)(() => e.Kardex.RegistrarBajaAsync(yaBaja))).Should().ThrowAsync<ValidationException>().WithMessage("*A-1 no está disponible (estado: Muerto)*");
        await ((Func<Task>)(() => e.Kardex.RegistrarBajaAsync(otraGranja))).Should().ThrowAsync<ValidationException>().WithMessage("*B-1 no se encuentra en la granja de origen*");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Una_baja_de_varios_animales_es_atomica()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.AltaVacaAsync("A-2");
        var dto = await e.BajaAsync("REM-B-1", ClaseSalida.Hurto, null, "A-1", "A-2");
        dto.Animales[1].AnimalId = 99999; // el segundo animal no existe

        var act = () => e.Kardex.RegistrarBajaAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*99999 no existe*");
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Disponible);
        (await e.ContarAsync("REMISION")).Should().Be(0);
        await e.AsegurarInvarianteAsync();
    }

    // ---- Traslados -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_traslado_mueve_al_animal_y_registra_la_salida_en_el_origen_y_la_entrada_en_el_destino()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("0940-26", valor: 6_000_000m);
        await e.AltaVacaAsync("0941-26", valor: 3_750_000m);
        await e.AltaVacaAsync("0942-26", valor: 4_000_000m); // se queda en Tesorito
        var antes = await e.AnimalAsync("0940-26");

        var resultado = await e.Kardex.RegistrarTrasladoAsync(
            await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "0940-26", "0941-26"));

        resultado.Animales.Should().Be(2);
        resultado.TotalValor.Should().Be(9_750_000m);

        var remision = await e.RemisionAsync("REM-T-1");
        remision.TipoMovimiento.Should().Be(TipoRemision.Traslado);
        remision.ClaseSalida.Should().Be(ClaseSalida.Traslado);
        remision.OficioId.Should().BeNull("el traslado no lleva oficio");
        remision.ReciboCaja.Should().BeNull("el traslado no lleva recibo de caja");
        remision.GranjaOrigenId.Should().Be(e.Ids.Tesorito);
        remision.GranjaDestinoId.Should().Be(e.Ids.LaCruz);

        var despues = await e.AnimalAsync("0940-26");
        despues.GranjaId.Should().Be(e.Ids.LaCruz);
        despues.Estado.Should().Be(EstadoAnimal.Disponible, "un traslado entre granjas no saca al animal del inventario");
        despues.ChapetaAmarilla.Should().Be(antes.ChapetaAmarilla);
        despues.ValorCompraOrigen.Should().Be(antes.ValorCompraOrigen);
        despues.ValorContableActual.Should().Be(antes.ValorContableActual);
        (await e.AnimalAsync("0942-26")).GranjaId.Should().Be(e.Ids.Tesorito);

        // Registro dual: dos asientos por animal con signos opuestos, cada uno en su granja.
        var asientos = (await e.MovimientosAsync("0940-26")).Where(m => m.TipoMovimiento == TipoMovimiento.Traslado).ToList();
        asientos.Should().HaveCount(2);
        var salida = asientos.Single(m => m.GranjaOrigenId is not null);
        var entrada = asientos.Single(m => m.GranjaDestinoId is not null);
        salida.GranjaOrigenId.Should().Be(e.Ids.Tesorito);
        salida.GranjaDestinoId.Should().BeNull();
        salida.CostoAsociado.Should().Be(-6_000_000m);
        entrada.GranjaDestinoId.Should().Be(e.Ids.LaCruz);
        entrada.GranjaOrigenId.Should().BeNull();
        entrada.CostoAsociado.Should().Be(6_000_000m);
        asientos.Should().OnlyContain(m => m.RemisionId == remision.Id && m.FechaMovimiento == Datos.F("2026-10-24"));

        (await e.MovimientosDeRemisionAsync("REM-T-1")).Should().HaveCount(4, "dos animales × (salida + entrada)");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_traslado_conserva_la_orientacion_por_defecto_y_la_cambia_si_se_confirma()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 6_000_000m);
        var dto = await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.Montelindo, "A-1", "A-2");
        dto.Animales[1].OrientacionDestinoId = e.Ids.Carne; // A-2 pasa de Lácteo a Carne/Levante

        await e.Kardex.RegistrarTrasladoAsync(dto);

        (await e.AnimalAsync("A-1")).OrientacionId.Should().Be(e.Ids.Lacteo);
        var a2 = await e.AnimalAsync("A-2");
        a2.OrientacionId.Should().Be(e.Ids.Carne);
        a2.ValorContableActual.Should().Be(6_000_000m, "el valor se reajusta en el próximo cierre, no al trasladar");

        var detalles = await e.DetallesAsync("REM-T-1");
        var d1 = detalles.Single(d => d.AnimalId == a2.Id);
        d1.OrientacionOrigenId.Should().Be(e.Ids.Lacteo);
        d1.OrientacionDestinoId.Should().Be(e.Ids.Carne);
        var d0 = detalles.Single(d => d.AnimalId != a2.Id);
        d0.OrientacionOrigenId.Should().Be(e.Ids.Lacteo);
        d0.OrientacionDestinoId.Should().Be(e.Ids.Lacteo);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Tras_trasladar_el_siguiente_cierre_revalora_con_la_orientacion_nueva()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-11-30");
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        var dto = await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.Montelindo, "A-1");
        dto.Animales[0].OrientacionDestinoId = e.Ids.Carne;
        await e.Kardex.RegistrarTrasladoAsync(dto);

        var resumen = await e.Aging.ValorizarAlCorteAsync(Datos.F("2026-11-30"));

        resumen.DeltaTotal.Should().Be(-2_000_000m, "de Vaca Lácteo (6 M) a Vaca Carne (4 M)");
        (await e.AnimalAsync("A-1")).ValorContableActual.Should().Be(4_000_000m);
        // El delta cae en la granja donde está ahora el animal.
        var delta = (await e.MovimientosAsync("A-1")).Single(m => m.TipoMovimiento == TipoMovimiento.Valorizacion);
        delta.GranjaDestinoId.Should().Be(e.Ids.Montelindo);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task El_origen_y_el_destino_deben_ser_distintos()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");

        var act = async () => await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.Tesorito, "A-1"));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*origen y la de destino deben ser distintas*");
    }

    [Fact]
    public async Task Un_traslado_rechaza_animales_que_no_estan_en_el_origen_o_no_estan_disponibles_y_destinos_inactivos()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.AltaVacaAsync("B-1", granja: e.Ids.LaCruz);
        await e.AltaVacaAsync("C-1");
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-0", ClaseSalida.Muerte, null, "C-1"));

        var noEstaEnOrigen = await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.Montelindo, "B-1");
        var noDisponible = await e.TrasladoAsync("REM-T-2", e.Ids.Tesorito, e.Ids.Montelindo, "C-1");

        await ((Func<Task>)(() => e.Kardex.RegistrarTrasladoAsync(noEstaEnOrigen))).Should().ThrowAsync<ValidationException>().WithMessage("*B-1 no se encuentra en la granja de origen*");
        await ((Func<Task>)(() => e.Kardex.RegistrarTrasladoAsync(noDisponible))).Should().ThrowAsync<ValidationException>().WithMessage("*C-1 no está disponible*");

        // Solo ahora se inactiva el destino: la granja se valida antes que los animales.
        await e.Db.EjecutarAsync("UPDATE GRANJA SET activa = 0 WHERE codigo = 'G-03'");
        var destinoInactivo = await e.TrasladoAsync("REM-T-3", e.Ids.Tesorito, e.Ids.Montelindo, "A-1");
        await ((Func<Task>)(() => e.Kardex.RegistrarTrasladoAsync(destinoInactivo))).Should().ThrowAsync<ValidationException>().WithMessage("*destino*inactiva*");
        (await e.AnimalAsync("A-1")).GranjaId.Should().Be(e.Ids.Tesorito);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_traslado_con_numero_de_remision_repetido_o_orientacion_inexistente_no_deja_nada()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.AltaVacaAsync("A-2");
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "A-1"));
        var repetido = await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "A-2");
        var sinOrientacion = await e.TrasladoAsync("REM-T-2", e.Ids.Tesorito, e.Ids.LaCruz, "A-2");
        sinOrientacion.Animales[0].OrientacionDestinoId = 999;

        await ((Func<Task>)(() => e.Kardex.RegistrarTrasladoAsync(repetido))).Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe un documento*");
        await ((Func<Task>)(() => e.Kardex.RegistrarTrasladoAsync(sinOrientacion))).Should().ThrowAsync<ValidationException>().WithMessage("*orientación productiva no existe*");
        (await e.AnimalAsync("A-2")).GranjaId.Should().Be(e.Ids.Tesorito);
        (await e.ContarAsync("REMISION")).Should().Be(1);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_animal_comprado_trasladado_conserva_su_valor_de_compra_y_su_fecha_de_compra()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("K-1", Sexo.Hembra, "2022-03-01", 3_225_970m, partos: 1)));

        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "K-1"));

        var animal = await e.AnimalAsync("K-1");
        animal.ValorCompraOrigen.Should().Be(3_225_970m);
        animal.FechaCompra.Should().Be(Datos.F("2026-10-22"));
        animal.ClaseEntrada.Should().Be(ClaseEntrada.Compra);
        await e.AsegurarInvarianteAsync();
    }
}
