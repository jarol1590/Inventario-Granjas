using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

public class CompraTests
{
    [Fact]
    public async Task Registra_una_compra_masiva_con_factura_inmutable_y_costo_sistema_de_la_matriz()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = e.Compra(
            "FAC-99420",
            e.AnimalComprado("0940-26", Sexo.Hembra, "2022-03-01", 3_225_970m, partos: 1), // 56 meses con parto: Vacas Lácteo
            e.AnimalComprado("0941-26", Sexo.Macho, "2025-09-01", 500_000m),                 // 14 meses: hueco de la matriz
            e.AnimalComprado("0950-26", Sexo.Hembra, "2026-06-20", 150_000m));               // 4 meses: Terneros 3–7

        var resultado = await e.Kardex.RegistrarCompraAsync(dto);

        resultado.AnimalIds.Should().HaveCount(3);
        resultado.TotalFactura.Should().Be(3_875_970m);
        resultado.TotalCostoSistema.Should().Be(7_300_000m, "6.000.000 + 500.000 (sin tarifa: parte del precio de factura) + 800.000");
        resultado.SinTarifa.Should().Equal("0941-26");

        var remision = await e.RemisionAsync("FAC-99420");
        remision.TipoMovimiento.Should().Be(TipoRemision.Compra);
        remision.OrdenCompra.Should().Be("OC-2026-018");
        remision.TerceroEntidad.Should().Be("Ganadería La Soledad");
        remision.TerceroNit.Should().Be("890.123.456-1");
        remision.GranjaDestinoId.Should().Be(e.Ids.Tesorito);
        remision.GranjaOrigenId.Should().BeNull();
        remision.FechaRemision.Should().Be(Datos.F("2026-10-22"));
        remision.OficioId.Should().BeNull();
        remision.ReciboCaja.Should().BeNull();
        remision.Anulada.Should().BeFalse();

        var vaca = await e.AnimalAsync("0940-26");
        vaca.ClaseEntrada.Should().Be(ClaseEntrada.Compra);
        vaca.ValorCompraOrigen.Should().Be(3_225_970m, "la factura se guarda como costo de origen");
        vaca.ValorContableActual.Should().Be(6_000_000m, "el valor contable sale de la matriz, no de la factura");
        vaca.CategoriaId.Should().Be(e.Ids.Categoria(NombresBase.CategoriaVacasGeneral));
        vaca.FechaCompra.Should().Be(Datos.F("2026-10-22"));
        vaca.GranjaId.Should().Be(e.Ids.Tesorito);
        vaca.NumeroPartos.Should().Be(1);

        var macho = await e.AnimalAsync("0941-26");
        macho.ValorContableActual.Should().Be(500_000m);
        macho.ValorCompraOrigen.Should().Be(500_000m);
        macho.CategoriaId.Should().Be(e.Ids.Categoria(NombresBase.CategoriaSinClasificar));

        (await e.AnimalAsync("0950-26")).ValorContableActual.Should().Be(800_000m);

        var detalles = await e.DetallesAsync("FAC-99420");
        detalles.Should().HaveCount(3);
        detalles.Sum(d => d.ValorTotalTransaccion ?? 0m).Should().Be(3_875_970m);
        detalles.Sum(d => d.CostoSistemaLibros).Should().Be(7_300_000m);

        var asiento = (await e.MovimientosAsync("0940-26")).Should().ContainSingle().Subject;
        asiento.TipoMovimiento.Should().Be(TipoMovimiento.Compra);
        asiento.CostoAsociado.Should().Be(6_000_000m);
        asiento.FechaMovimiento.Should().Be(Datos.F("2026-10-22"));
        asiento.GranjaDestinoId.Should().Be(e.Ids.Tesorito);
        asiento.RemisionId.Should().Be(remision.Id);

        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Una_compra_individual_a_otra_granja_asienta_en_la_granja_destino()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = e.Compra("FAC-1", e.AnimalComprado("0940-26", Sexo.Hembra, "2026-06-20", 150_000m));
        dto.GranjaDestinoId = e.Ids.Montelindo;

        await e.Kardex.RegistrarCompraAsync(dto);

        (await e.AnimalAsync("0940-26")).GranjaId.Should().Be(e.Ids.Montelindo);
        (await e.MovimientosAsync("0940-26")).Single().GranjaDestinoId.Should().Be(e.Ids.Montelindo);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Los_animales_de_carne_reciben_la_tarifa_de_carne()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = e.Compra("FAC-1", e.AnimalComprado("0940-26", Sexo.Hembra, "2021-06-01", 2_500_000m, partos: 3, carne: true));

        await e.Kardex.RegistrarCompraAsync(dto);

        (await e.AnimalAsync("0940-26")).ValorContableActual.Should().Be(4_000_000m);
    }

    [Fact]
    public async Task El_numero_de_factura_no_se_puede_repetir_ni_siquiera_con_otras_mayusculas()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-99420", e.AnimalComprado("A-1", Sexo.Hembra, "2026-06-20", 150_000m)));

        var act = () => e.Kardex.RegistrarCompraAsync(e.Compra("fac-99420", e.AnimalComprado("A-2", Sexo.Hembra, "2026-06-20", 150_000m)));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe un documento con el número fac-99420*");
        (await e.ContarAsync("ANIMAL")).Should().Be(1);
    }

    [Fact]
    public async Task Una_chapeta_ya_registrada_rechaza_toda_la_compra_sin_dejar_nada_a_medias()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaTerneraAsync("0941-26");
        var dto = e.Compra(
            "FAC-1",
            e.AnimalComprado("0940-26", Sexo.Hembra, "2026-06-20", 150_000m),
            e.AnimalComprado("0941-26", Sexo.Hembra, "2026-06-20", 150_000m));

        var act = () => e.Kardex.RegistrarCompraAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*0941-26 ya está registrada*");
        (await e.ExisteAnimalAsync("0940-26")).Should().BeFalse("la primera chapeta tampoco debe haberse guardado");
        (await e.ContarAsync("REMISION")).Should().Be(0);
        (await e.ContarAsync("ANIMAL")).Should().Be(1);
    }

    [Fact]
    public async Task Rechaza_granja_inactiva_y_orientacion_inexistente()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Db.EjecutarAsync("UPDATE GRANJA SET activa = 0 WHERE codigo = 'G-03'");
        var inactiva = e.Compra("FAC-1", e.AnimalComprado("A-1", Sexo.Hembra, "2026-06-20", 150_000m));
        inactiva.GranjaDestinoId = e.Ids.Montelindo;
        var sinOrientacion = e.Compra("FAC-2", e.AnimalComprado("A-2", Sexo.Hembra, "2026-06-20", 150_000m));
        sinOrientacion.Animales[0].OrientacionId = 999;

        var a = () => e.Kardex.RegistrarCompraAsync(inactiva);
        var b = () => e.Kardex.RegistrarCompraAsync(sinOrientacion);

        await a.Should().ThrowAsync<ValidationException>().WithMessage("*inactiva*");
        await b.Should().ThrowAsync<ValidationException>().WithMessage("*orientación productiva no existe*");
        (await e.ContarAsync("REMISION")).Should().Be(0);
    }

    [Fact]
    public async Task No_admite_animales_sin_fecha_de_nacimiento()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var animal = e.AnimalComprado("A-1", Sexo.Hembra, "2026-06-20", 150_000m);
        animal.FechaNacimiento = default;

        var act = () => e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", animal));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*fecha de nacimiento es obligatoria*");
    }

    [Fact]
    public async Task Una_compra_ya_registrada_no_se_puede_volver_a_registrar_como_nueva()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = e.Compra("FAC-1", e.AnimalComprado("A-1", Sexo.Hembra, "2026-06-20", 150_000m));
        dto.RemisionId = 77;

        var act = () => e.Kardex.RegistrarCompraAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*use la edición*");
    }

    [Fact]
    public async Task El_valor_de_compra_de_origen_sobrevive_a_la_valorizacion_mensual()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-11-30");
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("A-1", Sexo.Hembra, "2026-07-20", 400_000m)));

        // A-1 tenía 4 meses al registrarse (Terneros 3–7: 800.000). Se avanza hasta que cambia de tramo
        // (8 meses al 31/03/2027: Levante 1.200.000) para comprobar que la factura no se toca.
        e.Reloj.Ahora = Datos.F("2027-03-31");
        await e.Aging.ValorizarAlCorteAsync(Datos.F("2027-03-31"));

        var animal = await e.AnimalAsync("A-1");
        animal.ValorContableActual.Should().Be(1_200_000m);
        animal.ValorCompraOrigen.Should().Be(400_000m, "el costo de origen es inmutable");
        await e.AsegurarInvarianteAsync();
    }
}
