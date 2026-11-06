using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

public class EditarSalidaTests
{
    // ---- Ventas ----------------------------------------------------------------------------------------------------------

    /// <summary>Tres vacas en Tesorito; se venden A-1 (6 M) y A-2 (3,75 M) en REM-1 bajo el oficio OF-1.</summary>
    private static async Task<EscenarioKardex> EscenarioConVentaAsync()
    {
        var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 3_750_000m);
        await e.AltaVacaAsync("A-3", valor: 4_000_000m);
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 6_500_000m), ("A-2", 4_000_000m)));
        return e;
    }

    private static async Task<VentaDto> EdicionAsync(EscenarioKardex e, params (string Chapeta, decimal Precio)[] animales)
    {
        var id = (await e.RemisionAsync("REM-1")).Id;
        var dto = await e.VentaAsync("REM-1", "OF-1", "RC-1", null, animales);
        dto.RemisionId = id;
        return dto;
    }

    [Fact]
    public async Task Cambia_precios_comprador_recibo_y_observaciones_sin_tocar_el_costo_sistema()
    {
        await using var e = await EscenarioConVentaAsync();
        var dto = await EdicionAsync(e, ("A-1", 7_000_000m), ("A-2", 4_200_000m));
        dto.Comprador = "Frigorífico Caldas";
        dto.ReciboCaja = "RC-1B";
        dto.Observaciones = "Corregida";
        dto.Animales[0].ObservacionesEspecificas = "Peso 520 kg";

        var resultado = await e.Kardex.EditarVentaAsync(dto);

        resultado.TotalVenta.Should().Be(11_200_000m);
        resultado.TotalCostoSistema.Should().Be(9_750_000m);
        var remision = await e.RemisionAsync("REM-1");
        remision.TerceroEntidad.Should().Be("Frigorífico Caldas");
        remision.ReciboCaja.Should().Be("RC-1B");
        remision.Observaciones.Should().Be("Corregida");
        var detalle = (await e.DetallesAsync("REM-1")).Single(d => d.AnimalId == (e.IdAsync("A-1").Result));
        detalle.ValorTotalTransaccion.Should().Be(7_000_000m);
        detalle.ObservacionesEspecificas.Should().Be("Peso 520 kg");
        detalle.CostoSistemaLibros.Should().Be(6_000_000m);
        (await e.MovimientosAsync("A-1")).Single(m => m.TipoMovimiento == TipoMovimiento.Venta).CostoAsociado.Should().Be(-6_000_000m);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Quita_un_animal_de_la_venta_y_lo_deja_disponible()
    {
        await using var e = await EscenarioConVentaAsync();
        var dto = await EdicionAsync(e, ("A-1", 6_500_000m));

        var resultado = await e.Kardex.EditarVentaAsync(dto);

        resultado.Animales.Should().Be(1);
        resultado.TotalCostoSistema.Should().Be(6_000_000m);
        (await e.AnimalAsync("A-2")).Estado.Should().Be(EstadoAnimal.Disponible);
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Vendido);
        (await e.MovimientosAsync("A-2")).Should().ContainSingle("solo queda su entrada");
        (await e.DetallesAsync("REM-1")).Should().ContainSingle();
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Agrega_un_animal_disponible_a_la_venta()
    {
        await using var e = await EscenarioConVentaAsync();
        var dto = await EdicionAsync(e, ("A-1", 6_500_000m), ("A-2", 4_000_000m), ("A-3", 4_500_000m));

        var resultado = await e.Kardex.EditarVentaAsync(dto);

        resultado.Animales.Should().Be(3);
        resultado.TotalCostoSistema.Should().Be(13_750_000m);
        resultado.TotalVenta.Should().Be(15_000_000m);
        (await e.AnimalAsync("A-3")).Estado.Should().Be(EstadoAnimal.Vendido);
        (await e.MovimientosAsync("A-3")).Single(m => m.TipoMovimiento == TipoMovimiento.Venta).CostoAsociado.Should().Be(-4_000_000m);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task No_se_puede_agregar_un_animal_que_no_esta_disponible()
    {
        await using var e = await EscenarioConVentaAsync();
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-3"));
        var dto = await EdicionAsync(e, ("A-1", 6_500_000m), ("A-2", 4_000_000m), ("A-3", 4_500_000m));

        var act = () => e.Kardex.EditarVentaAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*A-3 no está disponible*");
        (await e.DetallesAsync("REM-1")).Should().HaveCount(2);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Cambia_a_otro_oficio_del_mismo_programa_y_rechaza_el_de_otro_programa()
    {
        await using var e = await EscenarioConVentaAsync();
        await e.Db.EjecutarAsync(
            "INSERT INTO OFICIO_VENTA (numero_oficio, programa_id, fecha_oficio) " +
            "SELECT 'OF-AVES', id, '2026-10-01 00:00:00' FROM PROGRAMA WHERE codigo = 'PRG-AVE'");
        var dto = await EdicionAsync(e, ("A-1", 6_500_000m), ("A-2", 4_000_000m));

        dto.NumeroOficio = "OF-2";
        await e.Kardex.EditarVentaAsync(dto);
        var remision = await e.RemisionAsync("REM-1");
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            (await uow.Oficios.GetByIdAsync(remision.OficioId!.Value))!.NumeroOficio.Should().Be("OF-2");
        }

        dto.NumeroOficio = "OF-AVES";
        var act = () => e.Kardex.EditarVentaAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*OF-AVES pertenece al programa*un solo programa*");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task No_permite_cambiar_la_granja_de_origen_de_una_salida_registrada()
    {
        await using var e = await EscenarioConVentaAsync();
        var dto = await EdicionAsync(e, ("A-1", 6_500_000m), ("A-2", 4_000_000m));
        dto.GranjaOrigenId = e.Ids.LaCruz;

        var act = () => e.Kardex.EditarVentaAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*No se puede cambiar la granja de origen*anule el documento*");
    }

    [Fact]
    public async Task Cambiar_la_fecha_mueve_todos_los_asientos_de_la_venta()
    {
        await using var e = await EscenarioConVentaAsync();
        var dto = await EdicionAsync(e, ("A-1", 6_500_000m), ("A-2", 4_000_000m));
        dto.FechaRemision = Datos.F("2026-10-28");

        await e.Kardex.EditarVentaAsync(dto);

        (await e.MovimientosDeRemisionAsync("REM-1")).Should().OnlyContain(m => m.FechaMovimiento == Datos.F("2026-10-28"));
        (await e.RemisionAsync("REM-1")).FechaRemision.Should().Be(Datos.F("2026-10-28"));
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Editar_la_venta_sigue_exigiendo_oficio_recibo_y_animales()
    {
        await using var e = await EscenarioConVentaAsync();
        var sinRecibo = await EdicionAsync(e, ("A-1", 6_500_000m), ("A-2", 4_000_000m));
        sinRecibo.ReciboCaja = " ";
        var sinAnimales = await EdicionAsync(e);
        var repetidos = await EdicionAsync(e, ("A-1", 6_500_000m), ("A-1", 6_500_000m));

        await ((Func<Task>)(() => e.Kardex.EditarVentaAsync(sinRecibo))).Should().ThrowAsync<ValidationException>().WithMessage("*recibo de caja*");
        await ((Func<Task>)(() => e.Kardex.EditarVentaAsync(sinAnimales))).Should().ThrowAsync<ValidationException>().WithMessage("*al menos un animal*");
        await ((Func<Task>)(() => e.Kardex.EditarVentaAsync(repetidos))).Should().ThrowAsync<ValidationException>().WithMessage("*repetidos*");
    }

    [Fact]
    public async Task No_se_puede_editar_con_el_numero_de_remision_de_otro_documento_ni_un_documento_de_otro_tipo()
    {
        await using var e = await EscenarioConVentaAsync();
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-3"));
        var repetido = await EdicionAsync(e, ("A-1", 6_500_000m), ("A-2", 4_000_000m));
        repetido.NumeroRemision = "REM-B-1";
        var otroTipo = await EdicionAsync(e, ("A-1", 6_500_000m), ("A-2", 4_000_000m));
        otroTipo.RemisionId = (await e.RemisionAsync("REM-B-1")).Id;

        await ((Func<Task>)(() => e.Kardex.EditarVentaAsync(repetido))).Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe un documento*");
        await ((Func<Task>)(() => e.Kardex.EditarVentaAsync(otroTipo))).Should().ThrowAsync<ValidationException>().WithMessage("*no es de tipo VENTA*");
    }

    // ---- Bajas -----------------------------------------------------------------------------------------------------------

    private static async Task<BajaDto> EdicionBajaAsync(EscenarioKardex e, string clase, params string[] chapetas)
    {
        var id = (await e.RemisionAsync("REM-B-1")).Id;
        var dto = await e.BajaAsync("REM-B-1", clase, null, chapetas);
        dto.RemisionId = id;
        return dto;
    }

    [Fact]
    public async Task Cambia_una_baja_de_muerte_a_hurto_y_actualiza_estado_y_asiento()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-1"));
        var dto = await EdicionBajaAsync(e, ClaseSalida.Hurto, "A-1");
        dto.DocumentoSoporte = "Denuncia 4471";
        dto.FechaBaja = Datos.F("2026-10-18");

        await e.Kardex.EditarBajaAsync(dto);

        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Hurtado);
        var asiento = (await e.MovimientosAsync("A-1")).Single(m => m.TipoMovimiento != TipoMovimiento.Nacimiento);
        asiento.TipoMovimiento.Should().Be(TipoMovimiento.Hurto);
        asiento.FechaMovimiento.Should().Be(Datos.F("2026-10-18"));
        var remision = await e.RemisionAsync("REM-B-1");
        remision.ClaseSalida.Should().Be(ClaseSalida.Hurto);
        remision.TerceroEntidad.Should().Be("Denuncia 4471");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Agrega_y_quita_animales_de_una_baja()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 3_750_000m);
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-1"));
        var dto = await EdicionBajaAsync(e, ClaseSalida.Muerte, "A-2"); // sale A-1, entra A-2

        var resultado = await e.Kardex.EditarBajaAsync(dto);

        resultado.Animales.Should().Be(1);
        resultado.TotalCostoSistema.Should().Be(3_750_000m);
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Disponible);
        (await e.AnimalAsync("A-2")).Estado.Should().Be(EstadoAnimal.Muerto);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Una_baja_editada_sigue_exigiendo_soporte_y_no_cambia_de_granja()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-1"));
        var sinSoporte = await EdicionBajaAsync(e, ClaseSalida.Muerte, "A-1");
        sinSoporte.DocumentoSoporte = "";
        var otraGranja = await EdicionBajaAsync(e, ClaseSalida.Muerte, "A-1");
        otraGranja.GranjaOrigenId = e.Ids.LaCruz;

        await ((Func<Task>)(() => e.Kardex.EditarBajaAsync(sinSoporte))).Should().ThrowAsync<ValidationException>().WithMessage("*documento de soporte*");
        await ((Func<Task>)(() => e.Kardex.EditarBajaAsync(otraGranja))).Should().ThrowAsync<ValidationException>().WithMessage("*No se puede cambiar la granja de origen*");
    }
}
