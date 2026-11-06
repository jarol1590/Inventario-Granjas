using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

public class NacimientoTests
{
    [Fact]
    public async Task Registra_un_nacimiento_con_costo_de_origen_cero_y_categoria_y_valor_de_la_matriz()
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var resultado = await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("0940-26"));

        resultado.Categoria.Should().Be(NombresBase.CategoriaTerneros);
        resultado.ValorInicial.Should().Be(150_000m);
        resultado.SinTarifa.Should().BeFalse();
        resultado.MadreActualizada.Should().BeFalse();

        var animal = await e.AnimalAsync("0940-26");
        animal.ClaseEntrada.Should().Be(ClaseEntrada.Nacimiento);
        animal.ValorCompraOrigen.Should().Be(0m, "un nacido en la granja no tiene costo de compra");
        animal.FechaCompra.Should().BeNull();
        animal.Estado.Should().Be(EstadoAnimal.Disponible);
        animal.ValorContableActual.Should().Be(150_000m);
        animal.CategoriaId.Should().Be(e.Ids.Categoria(NombresBase.CategoriaTerneros));
        animal.GranjaId.Should().Be(e.Ids.Tesorito);
        animal.PesoActualKg.Should().Be(38m);
        animal.MadreIdentificacion.Should().Be("044");
        animal.NumeroPartos.Should().Be(0);
        animal.ExentoValorizacion.Should().BeFalse();
        animal.FechaRegistro.Should().Be(Datos.F("2026-11-06"));

        var asiento = (await e.MovimientosAsync("0940-26")).Should().ContainSingle().Subject;
        asiento.TipoMovimiento.Should().Be(TipoMovimiento.Nacimiento);
        asiento.FechaMovimiento.Should().Be(Datos.F("2026-11-06"));
        asiento.GranjaDestinoId.Should().Be(e.Ids.Tesorito);
        asiento.GranjaOrigenId.Should().BeNull();
        asiento.CostoAsociado.Should().Be(150_000m);
        asiento.RemisionId.Should().BeNull("los nacimientos no llevan remisión");

        await using var uow = await e.Db.Uow.OpenReadOnlyAsync();
        var genealogia = await uow.Genealogias.GetByAnimalIdAsync(animal.Id);
        genealogia.Should().NotBeNull();
        genealogia!.EsInseminacion.Should().BeFalse();
        genealogia.PadreId.Should().BeNull();

        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task El_macho_recien_nacido_entra_con_160_mil()
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var resultado = await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("0941-26", Sexo.Macho));

        resultado.ValorInicial.Should().Be(160_000m);
        (await e.AnimalAsync("0941-26")).Sexo.Should().Be(Sexo.Macho);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_nacimiento_con_madre_registrada_le_suma_un_parto()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("044", nacimiento: "2023-03-01", valor: 3_200_000m, partos: 0);

        var primero = await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("0940-26"));
        (await e.AnimalAsync("044")).NumeroPartos.Should().Be(1);
        var segundo = await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("0942-26"));

        primero.MadreActualizada.Should().BeTrue();
        segundo.MadreActualizada.Should().BeTrue();
        (await e.AnimalAsync("044")).NumeroPartos.Should().Be(2);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Si_la_madre_no_esta_en_el_sistema_solo_se_guarda_su_chapeta()
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var resultado = await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("0940-26", madre: "SIN-REGISTRO"));

        resultado.MadreActualizada.Should().BeFalse();
        (await e.AnimalAsync("0940-26")).MadreIdentificacion.Should().Be("SIN-REGISTRO");
    }

    [Fact]
    public async Task La_madre_registrada_debe_ser_hembra_y_haber_nacido_antes_que_la_cria()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaDirectaAsync(Datos.Animal(e.Ids, "M-1", Sexo.Macho, "2022-01-01", NombresBase.CategoriaReproductor, 7_000_000m));
        await e.AltaVacaAsync("H-1", nacimiento: "2026-11-01", valor: 150_000m, partos: 0);

        var comoMadreMacho = () => e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("0940-26", madre: "M-1"));
        var madreMasJoven = () => e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("0941-26", nacimiento: "2026-10-20", madre: "H-1"));

        await comoMadreMacho.Should().ThrowAsync<ValidationException>().WithMessage("*registrada como macho*");
        await madreMasJoven.Should().ThrowAsync<ValidationException>().WithMessage("*no puede haber nacido después de la cría*");
        (await e.ExisteAnimalAsync("0940-26")).Should().BeFalse();
    }

    [Fact]
    public async Task La_inseminacion_guarda_raza_y_nombre_del_padre_sin_padre_registrado()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = e.Nacimiento("0940-26");
        dto.EsInseminacion = true;
        dto.RazaPadre = "Jersey";
        dto.NombrePadre = "Pajilla 4471";
        dto.PadreChapeta = "IGNORADO"; // en inseminación no aplica

        await e.Kardex.RegistrarNacimientoAsync(dto);

        var animal = await e.AnimalAsync("0940-26");
        await using var uow = await e.Db.Uow.OpenReadOnlyAsync();
        var genealogia = (await uow.Genealogias.GetByAnimalIdAsync(animal.Id))!;
        genealogia.EsInseminacion.Should().BeTrue();
        genealogia.RazaPadreInseminacion.Should().Be("Jersey");
        genealogia.NombrePadreInseminacion.Should().Be("Pajilla 4471");
        genealogia.PadreId.Should().BeNull();
    }

    [Fact]
    public async Task La_inseminacion_sin_raza_del_padre_se_rechaza()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = e.Nacimiento("0940-26");
        dto.EsInseminacion = true;

        var act = () => e.Kardex.RegistrarNacimientoAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*raza del padre*");
        (await e.ContarAsync("ANIMAL")).Should().Be(0);
    }

    [Fact]
    public async Task La_monta_directa_asocia_al_padre_registrado()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var toro = await e.AltaDirectaAsync(Datos.Animal(e.Ids, "T-1", Sexo.Macho, "2021-01-01", NombresBase.CategoriaReproductor, 7_000_000m));
        var dto = e.Nacimiento("0940-26");
        dto.PadreChapeta = "T-1";

        await e.Kardex.RegistrarNacimientoAsync(dto);

        await using var uow = await e.Db.Uow.OpenReadOnlyAsync();
        var animal = await e.AnimalAsync("0940-26");
        (await uow.Genealogias.GetByAnimalIdAsync(animal.Id))!.PadreId.Should().Be(toro.Id);
        (await uow.Genealogias.EsPadreAsync(toro.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task El_padre_debe_estar_registrado_y_ser_macho()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("V-1");
        var desconocido = e.Nacimiento("0940-26");
        desconocido.PadreChapeta = "NO-EXISTE";
        var hembra = e.Nacimiento("0941-26");
        hembra.PadreChapeta = "V-1";

        var a = () => e.Kardex.RegistrarNacimientoAsync(desconocido);
        var b = () => e.Kardex.RegistrarNacimientoAsync(hembra);

        await a.Should().ThrowAsync<ValidationException>().WithMessage("*padre NO-EXISTE no está registrado*");
        await b.Should().ThrowAsync<ValidationException>().WithMessage("*registrado como hembra*");
    }

    [Fact]
    public async Task Un_nacimiento_registrado_con_fecha_antigua_se_valora_con_la_edad_de_hoy_y_se_asienta_en_su_fecha()
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var resultado = await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("0900-24", nacimiento: "2024-11-06"));

        resultado.Categoria.Should().Be(NombresBase.CategoriaNovillaVientre);
        resultado.ValorInicial.Should().Be(3_200_000m);
        (await e.MovimientosAsync("0900-24")).Single().FechaMovimiento.Should().Be(Datos.F("2024-11-06"));
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Sin_tarifa_aplicable_el_nacido_entra_en_cero_como_Sin_clasificar()
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var resultado = await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("0800-25", Sexo.Macho, nacimiento: "2025-08-01"));

        resultado.SinTarifa.Should().BeTrue();
        resultado.Categoria.Should().Be(NombresBase.CategoriaSinClasificar);
        resultado.ValorInicial.Should().Be(0m);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Rechaza_una_chapeta_ya_registrada_ignorando_mayusculas()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("004C"));

        var act = () => e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("004c"));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*004c ya está registrada*");
        (await e.ContarAsync("ANIMAL")).Should().Be(1);
    }

    [Fact]
    public async Task Rechaza_una_granja_inactiva_o_inexistente_y_una_orientacion_inexistente_sin_dejar_nada()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Db.EjecutarAsync("UPDATE GRANJA SET activa = 0 WHERE codigo = 'G-02'");
        var inactiva = e.Nacimiento("0940-26", granja: e.Ids.LaCruz);
        var inexistente = e.Nacimiento("0941-26", granja: 999);
        var sinOrientacion = e.Nacimiento("0942-26");
        sinOrientacion.OrientacionId = 999;

        var a = () => e.Kardex.RegistrarNacimientoAsync(inactiva);
        var b = () => e.Kardex.RegistrarNacimientoAsync(inexistente);
        var c = () => e.Kardex.RegistrarNacimientoAsync(sinOrientacion);

        await a.Should().ThrowAsync<ValidationException>().WithMessage("*inactiva*");
        await b.Should().ThrowAsync<ValidationException>().WithMessage("*no existe*");
        await c.Should().ThrowAsync<ValidationException>().WithMessage("*orientación productiva no existe*");
        (await e.ContarAsync("ANIMAL")).Should().Be(0);
        (await e.ContarAsync("MOVIMIENTO_INVENTARIO")).Should().Be(0);
    }

    [Fact]
    public async Task Los_datos_de_texto_se_guardan_sin_espacios_sobrantes()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = e.Nacimiento("  0940-26  ");
        dto.Nombre = "  Lucero ";
        dto.ChapetaRoja = "   ";
        dto.Raza = " Holstein ";

        await e.Kardex.RegistrarNacimientoAsync(dto);

        var animal = await e.AnimalAsync("0940-26");
        animal.Nombre.Should().Be("Lucero");
        animal.ChapetaRoja.Should().BeNull();
        animal.Raza.Should().Be("Holstein");
    }
}
