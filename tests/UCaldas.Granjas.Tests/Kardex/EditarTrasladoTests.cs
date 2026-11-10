using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

public class EditarTrasladoTests
{
    /// <summary>
    /// Cuatro vacas en Tesorito (A-1 6 M, A-2 3,75 M, A-3 4 M) y una en La Cruz (B-1). REM-T-1 (24/10/2026) lleva
    /// A-1 y A-2 de Tesorito a La Cruz.
    /// </summary>
    private static async Task<EscenarioKardex> EscenarioConTrasladoAsync(string hoy = "2026-11-06")
    {
        var e = await EscenarioKardex.CrearAsync(hoy);
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 3_750_000m);
        await e.AltaVacaAsync("A-3", valor: 4_000_000m);
        await e.AltaVacaAsync("B-1", valor: 5_000_000m, granja: e.Ids.LaCruz);
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "A-1", "A-2"));
        return e;
    }

    /// <summary>La edición de REM-T-1 con los animales indicados (mismo origen y destino que el original).</summary>
    private static async Task<TrasladoDto> EdicionAsync(EscenarioKardex e, params string[] chapetas)
    {
        var id = (await e.RemisionAsync("REM-T-1")).Id;
        var dto = await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, chapetas);
        dto.RemisionId = id;
        return dto;
    }

    [Fact]
    public async Task Cambia_numero_fecha_y_observaciones_y_mueve_todos_los_asientos()
    {
        await using var e = await EscenarioConTrasladoAsync();
        var dto = await EdicionAsync(e, "A-1", "A-2");
        dto.NumeroRemision = "REM-T-1B";
        dto.FechaTraslado = Datos.F("2026-10-28");
        dto.Observaciones = "Corregido";
        dto.Animales[0].ObservacionesEspecificas = "Cojea de la pata izquierda";

        var resultado = await e.Kardex.EditarTrasladoAsync(dto);

        resultado.Animales.Should().Be(2);
        resultado.TotalValor.Should().Be(9_750_000m);
        var remision = await e.RemisionAsync("REM-T-1B");
        remision.FechaRemision.Should().Be(Datos.F("2026-10-28"));
        remision.Observaciones.Should().Be("Corregido");
        (await e.MovimientosDeRemisionAsync("REM-T-1B")).Should().HaveCount(4).And.OnlyContain(m => m.FechaMovimiento == Datos.F("2026-10-28"));
        var idA1 = await e.IdAsync("A-1");
        var detalle = (await e.DetallesAsync("REM-T-1B")).Single(d => d.AnimalId == idA1);
        detalle.ObservacionesEspecificas.Should().Be("Cojea de la pata izquierda");
        detalle.CostoSistemaLibros.Should().Be(6_000_000m, "el costo en libros no cambia al editar");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Cambia_la_granja_destino_de_todos_los_animales_y_de_su_asiento_de_entrada()
    {
        await using var e = await EscenarioConTrasladoAsync();
        var dto = await EdicionAsync(e, "A-1", "A-2");
        dto.GranjaDestinoId = e.Ids.Montelindo;

        await e.Kardex.EditarTrasladoAsync(dto);

        (await e.RemisionAsync("REM-T-1")).GranjaDestinoId.Should().Be(e.Ids.Montelindo);
        (await e.AnimalAsync("A-1")).GranjaId.Should().Be(e.Ids.Montelindo);
        (await e.AnimalAsync("A-2")).GranjaId.Should().Be(e.Ids.Montelindo);
        var asientos = await e.MovimientosDeRemisionAsync("REM-T-1");
        asientos.Where(m => m.GranjaDestinoId is not null).Should().HaveCount(2).And.OnlyContain(m => m.GranjaDestinoId == e.Ids.Montelindo);
        asientos.Where(m => m.GranjaOrigenId is not null).Should().HaveCount(2).And.OnlyContain(m => m.GranjaOrigenId == e.Ids.Tesorito);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Cambia_la_orientacion_de_un_animal_en_la_sede_destino_sin_perder_la_de_origen()
    {
        await using var e = await EscenarioConTrasladoAsync();
        var dto = await EdicionAsync(e, "A-1", "A-2");
        dto.Animales[1].OrientacionDestinoId = e.Ids.Carne;

        await e.Kardex.EditarTrasladoAsync(dto);

        var a2 = await e.AnimalAsync("A-2");
        a2.OrientacionId.Should().Be(e.Ids.Carne);
        var detalle = (await e.DetallesAsync("REM-T-1")).Single(d => d.AnimalId == a2.Id);
        detalle.OrientacionOrigenId.Should().Be(e.Ids.Lacteo);
        detalle.OrientacionDestinoId.Should().Be(e.Ids.Carne);
        (await e.AnimalAsync("A-1")).OrientacionId.Should().Be(e.Ids.Lacteo, "A-1 no cambió");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Quita_un_animal_del_traslado_y_lo_devuelve_a_su_granja_y_a_su_orientacion()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 3_750_000m);
        var original = await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.Montelindo, "A-1", "A-2");
        original.Animales[1].OrientacionDestinoId = e.Ids.Carne;
        await e.Kardex.RegistrarTrasladoAsync(original);
        var dto = await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.Montelindo, "A-1");
        dto.RemisionId = (await e.RemisionAsync("REM-T-1")).Id;

        var resultado = await e.Kardex.EditarTrasladoAsync(dto);

        resultado.Animales.Should().Be(1);
        resultado.TotalValor.Should().Be(6_000_000m);
        var a2 = await e.AnimalAsync("A-2");
        a2.GranjaId.Should().Be(e.Ids.Tesorito);
        a2.OrientacionId.Should().Be(e.Ids.Lacteo, "vuelve a la orientación que tenía en el origen");
        (await e.MovimientosAsync("A-2")).Should().ContainSingle("solo queda su asiento de entrada");
        (await e.DetallesAsync("REM-T-1")).Should().ContainSingle();
        (await e.MovimientosDeRemisionAsync("REM-T-1")).Should().HaveCount(2);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Agrega_un_animal_disponible_del_origen_al_traslado()
    {
        await using var e = await EscenarioConTrasladoAsync();
        var dto = await EdicionAsync(e, "A-1", "A-2", "A-3");
        dto.Animales[2].OrientacionDestinoId = e.Ids.Carne;

        var resultado = await e.Kardex.EditarTrasladoAsync(dto);

        resultado.Animales.Should().Be(3);
        resultado.TotalValor.Should().Be(13_750_000m);
        var a3 = await e.AnimalAsync("A-3");
        a3.GranjaId.Should().Be(e.Ids.LaCruz);
        a3.OrientacionId.Should().Be(e.Ids.Carne);
        var asientos = (await e.MovimientosAsync("A-3")).Where(m => m.TipoMovimiento == TipoMovimiento.Traslado).ToList();
        asientos.Should().HaveCount(2);
        asientos.Sum(m => m.CostoAsociado).Should().Be(0m);
        (await e.MovimientosDeRemisionAsync("REM-T-1")).Should().HaveCount(6);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task No_se_puede_agregar_un_animal_de_otra_granja_ni_uno_que_no_esta_disponible()
    {
        await using var e = await EscenarioConTrasladoAsync();
        await e.AltaVacaAsync("C-1");
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "C-1"));
        var deOtraGranja = await EdicionAsync(e, "A-1", "A-2", "B-1");
        var noDisponible = await EdicionAsync(e, "A-1", "A-2", "C-1");

        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(deOtraGranja))).Should().ThrowAsync<ValidationException>().WithMessage("*B-1 no se encuentra en la granja de origen*");
        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(noDisponible))).Should().ThrowAsync<ValidationException>().WithMessage("*C-1 no está disponible*");
        (await e.DetallesAsync("REM-T-1")).Should().HaveCount(2);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task No_permite_cambiar_la_granja_de_origen_de_un_traslado_registrado()
    {
        await using var e = await EscenarioConTrasladoAsync();
        var dto = await EdicionAsync(e, "A-1", "A-2");
        dto.GranjaOrigenId = e.Ids.Montelindo;

        var act = () => e.Kardex.EditarTrasladoAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*No se puede cambiar la granja de origen*anule el documento*");
    }

    [Fact]
    public async Task Con_movimientos_posteriores_solo_se_pueden_cambiar_datos_que_no_tocan_el_libro()
    {
        await using var e = await EscenarioConTrasladoAsync("2026-11-30");
        // A-1 se vuelve a trasladar de La Cruz a Montelindo: ya hay un movimiento posterior al de REM-T-1.
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-2", e.Ids.LaCruz, e.Ids.Montelindo, "A-1"));

        var cambioDeDestino = await EdicionAsync(e, "A-1", "A-2");
        cambioDeDestino.GranjaDestinoId = e.Ids.Montelindo;
        var quitaAlAnimal = await EdicionAsync(e, "A-2");
        var soloObservaciones = await EdicionAsync(e, "A-1", "A-2");
        soloObservaciones.Observaciones = "Solo cambia el texto";
        soloObservaciones.Animales[0].ObservacionesEspecificas = "Con guía sanitaria";

        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(cambioDeDestino))).Should().ThrowAsync<ValidationException>()
            .WithMessage("*A-1 ya tiene movimientos posteriores*cambiar el destino*");
        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(quitaAlAnimal))).Should().ThrowAsync<ValidationException>()
            .WithMessage("*A-1 ya tiene movimientos posteriores*quitarlo del traslado*");
        (await e.AnimalAsync("A-1")).GranjaId.Should().Be(e.Ids.Montelindo);

        await e.Kardex.EditarTrasladoAsync(soloObservaciones);

        (await e.RemisionAsync("REM-T-1")).Observaciones.Should().Be("Solo cambia el texto");
        var idA1 = await e.IdAsync("A-1");
        (await e.DetallesAsync("REM-T-1")).Single(d => d.AnimalId == idA1).ObservacionesEspecificas.Should().Be("Con guía sanitaria");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_traslado_anulado_no_se_puede_editar()
    {
        await using var e = await EscenarioConTrasladoAsync();
        var dto = await EdicionAsync(e, "A-1", "A-2");
        await e.Kardex.AnularAsync(dto.RemisionId!.Value);

        var act = () => e.Kardex.EditarTrasladoAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*ya está anulado*");
    }

    [Fact]
    public async Task Editar_sigue_exigiendo_destino_distinto_numero_libre_y_al_menos_un_animal()
    {
        await using var e = await EscenarioConTrasladoAsync();
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-3"));
        var mismoDestino = await EdicionAsync(e, "A-1", "A-2");
        mismoDestino.GranjaDestinoId = e.Ids.Tesorito;
        var numeroAjeno = await EdicionAsync(e, "A-1", "A-2");
        numeroAjeno.NumeroRemision = "REM-B-1";
        var sinAnimales = await EdicionAsync(e);
        var repetidos = await EdicionAsync(e, "A-1", "A-1");

        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(mismoDestino))).Should().ThrowAsync<ValidationException>().WithMessage("*origen y la de destino deben ser distintas*");
        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(numeroAjeno))).Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe un documento*");
        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(sinAnimales))).Should().ThrowAsync<ValidationException>().WithMessage("*al menos un animal*");
        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(repetidos))).Should().ThrowAsync<ValidationException>().WithMessage("*repetidos*");
    }

    [Fact]
    public async Task Exige_indicar_el_traslado_y_que_el_documento_sea_un_traslado()
    {
        await using var e = await EscenarioConTrasladoAsync();
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-3"));
        var sinId = await EdicionAsync(e, "A-1", "A-2");
        sinId.RemisionId = null;
        var otroTipo = await EdicionAsync(e, "A-1", "A-2");
        otroTipo.RemisionId = (await e.RemisionAsync("REM-B-1")).Id;

        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(sinId))).Should().ThrowAsync<ValidationException>().WithMessage("*Falta indicar el traslado*");
        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(otroTipo))).Should().ThrowAsync<ValidationException>().WithMessage("*no es de tipo TRASLADO*");
    }

    [Fact]
    public async Task Registrar_un_traslado_que_ya_tiene_identificador_se_rechaza()
    {
        await using var e = await EscenarioConTrasladoAsync();
        var dto = await EdicionAsync(e, "A-1", "A-2");
        dto.NumeroRemision = "REM-T-9";

        var act = () => e.Kardex.RegistrarTrasladoAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*ya está registrado*edición*");
    }

    [Fact]
    public async Task Una_sede_destino_inactiva_solo_estorba_si_se_la_elige_de_nuevo_como_destino()
    {
        await using var e = await EscenarioConTrasladoAsync();
        await e.Db.EjecutarAsync("UPDATE GRANJA SET activa = 0 WHERE codigo = 'G-02'"); // La Cruz, destino del traslado
        await e.Db.EjecutarAsync("UPDATE GRANJA SET activa = 0 WHERE codigo = 'G-03'"); // Montelindo
        var soloTexto = await EdicionAsync(e, "A-1", "A-2");
        soloTexto.Observaciones = "La sede ya está inactiva";
        var haciaInactiva = await EdicionAsync(e, "A-1", "A-2");
        haciaInactiva.GranjaDestinoId = e.Ids.Montelindo;

        await e.Kardex.EditarTrasladoAsync(soloTexto);
        await ((Func<Task>)(() => e.Kardex.EditarTrasladoAsync(haciaInactiva))).Should().ThrowAsync<ValidationException>().WithMessage("*destino*inactiva*");

        (await e.RemisionAsync("REM-T-1")).Observaciones.Should().Be("La sede ya está inactiva");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Si_un_animal_agregado_no_existe_no_se_guarda_ningun_cambio_del_traslado()
    {
        await using var e = await EscenarioConTrasladoAsync();
        var dto = await EdicionAsync(e, "A-1", "A-3");   // quita A-2 y agrega A-3...
        dto.Animales.Add(new TrasladoAnimalDto { AnimalId = 99999 }); // ...pero el tercero no existe
        dto.NumeroRemision = "REM-T-1X";
        dto.Observaciones = "No debe guardarse";

        var act = () => e.Kardex.EditarTrasladoAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*99999 no existe*");
        var remision = await e.RemisionAsync("REM-T-1");
        remision.Observaciones.Should().Be("Traslado de prueba");
        (await e.AnimalAsync("A-2")).GranjaId.Should().Be(e.Ids.LaCruz, "A-2 sigue en el traslado");
        (await e.AnimalAsync("A-3")).GranjaId.Should().Be(e.Ids.Tesorito);
        (await e.DetallesAsync("REM-T-1")).Should().HaveCount(2);
        await e.AsegurarInvarianteAsync();
    }
}
