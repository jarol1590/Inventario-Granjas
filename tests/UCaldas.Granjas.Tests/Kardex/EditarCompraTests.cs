using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

public class EditarCompraTests
{
    /// <summary>Compra base: A-1 (vaca de 56 meses con parto, 6 M en libros) y A-2 (ternera de 4 meses, 800 mil).</summary>
    private static async Task<CompraDto> CompraBaseAsync(EscenarioKardex e)
    {
        var dto = e.Compra(
            "FAC-1",
            e.AnimalComprado("A-1", Sexo.Hembra, "2022-03-01", 3_000_000m, partos: 1),
            e.AnimalComprado("A-2", Sexo.Hembra, "2026-06-20", 150_000m));
        await e.Kardex.RegistrarCompraAsync(dto);
        return await ParaEditarAsync(e, dto);
    }

    /// <summary>Convierte el comando de alta en uno de edición: apunta a la remisión y a los animales ya creados.</summary>
    private static async Task<CompraDto> ParaEditarAsync(EscenarioKardex e, CompraDto dto)
    {
        dto.RemisionId = (await e.RemisionAsync(dto.NumeroFactura)).Id;
        foreach (var animal in dto.Animales.Where(a => a.AnimalId is null))
        {
            if (await e.ExisteAnimalAsync(animal.ChapetaAmarilla))
            {
                animal.AnimalId = await e.IdAsync(animal.ChapetaAmarilla);
            }
        }

        return dto;
    }

    [Fact]
    public async Task Corrige_el_precio_de_factura_sin_tocar_el_valor_contable_ni_el_libro()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        dto.Animales[0].PrecioFactura = 3_200_000m;

        var resultado = await e.Kardex.EditarCompraAsync(dto);

        resultado.TotalFactura.Should().Be(3_350_000m);
        resultado.TotalCostoSistema.Should().Be(6_800_000m, "el costo sistema sigue siendo el de la matriz: 6.000.000 + 800.000");
        var vaca = await e.AnimalAsync("A-1");
        vaca.ValorCompraOrigen.Should().Be(3_200_000m);
        vaca.ValorContableActual.Should().Be(6_000_000m);
        var detalle = (await e.DetallesAsync("FAC-1")).Single(d => d.AnimalId == vaca.Id);
        detalle.ValorUnitarioTransaccion.Should().Be(3_200_000m);
        detalle.ValorTotalTransaccion.Should().Be(3_200_000m);
        (await e.MovimientosAsync("A-1")).Single().CostoAsociado.Should().Be(6_000_000m);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Corrige_el_encabezado_y_la_fecha_mueve_los_asientos_y_la_fecha_de_compra_de_los_animales()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        dto.NumeroFactura = "FAC-1B";
        dto.Proveedor = "Hacienda El Paraíso";
        dto.ProveedorNit = "900.111.222-3";
        dto.OrdenCompra = "OC-2026-099";
        dto.Observaciones = "Corregida";
        dto.FechaCompra = Datos.F("2026-10-10");

        await e.Kardex.EditarCompraAsync(dto);

        var remision = await e.RemisionAsync("FAC-1B");
        remision.TerceroEntidad.Should().Be("Hacienda El Paraíso");
        remision.TerceroNit.Should().Be("900.111.222-3");
        remision.OrdenCompra.Should().Be("OC-2026-099");
        remision.Observaciones.Should().Be("Corregida");
        remision.FechaRemision.Should().Be(Datos.F("2026-10-10"));
        (await e.AnimalAsync("A-1")).FechaCompra.Should().Be(Datos.F("2026-10-10"));
        (await e.MovimientosAsync("A-2")).Single().FechaMovimiento.Should().Be(Datos.F("2026-10-10"));
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Agrega_y_quita_animales_reajustando_el_libro()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        dto.Animales.RemoveAt(1); // se quita A-2
        dto.Animales.Add(e.AnimalComprado("A-3", Sexo.Macho, "2025-09-01", 500_000m)); // se agrega A-3 (sin tarifa)

        var resultado = await e.Kardex.EditarCompraAsync(dto);

        (await e.ExisteAnimalAsync("A-2")).Should().BeFalse();
        (await e.ExisteAnimalAsync("A-3")).Should().BeTrue();
        resultado.AnimalIds.Should().HaveCount(2);
        resultado.TotalFactura.Should().Be(3_500_000m);
        resultado.TotalCostoSistema.Should().Be(6_500_000m, "6.000.000 de A-1 + 500.000 de A-3 (sin tarifa: parte de la factura)");
        resultado.SinTarifa.Should().Equal("A-3");
        (await e.DetallesAsync("FAC-1")).Select(d => d.CostoSistemaLibros).Should().BeEquivalentTo(new[] { 6_000_000m, 500_000m });
        (await e.MovimientosAsync("A-3")).Single().RemisionId.Should().Be((await e.RemisionAsync("FAC-1")).Id);
        (await e.ContarAsync("ANIMAL")).Should().Be(2);
        (await e.ContarAsync("MOVIMIENTO_INVENTARIO")).Should().Be(2);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_cambio_estructural_sin_movimientos_posteriores_revalora_y_ajusta_el_asiento_de_compra()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        dto.Animales[1].FechaNacimiento = Datos.F("2025-12-20"); // A-2 pasa de 4 a 10 meses: Levante 1.200.000

        var resultado = await e.Kardex.EditarCompraAsync(dto);

        var a2 = await e.AnimalAsync("A-2");
        a2.ValorContableActual.Should().Be(1_200_000m);
        a2.CategoriaId.Should().Be(e.Ids.Categoria(NombresBase.CategoriaLevante));
        a2.ValorCompraOrigen.Should().Be(150_000m);
        (await e.MovimientosAsync("A-2")).Single().CostoAsociado.Should().Be(1_200_000m);
        (await e.DetallesAsync("FAC-1")).Single(d => d.AnimalId == a2.Id).CostoSistemaLibros.Should().Be(1_200_000m);
        resultado.TotalCostoSistema.Should().Be(7_200_000m);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Cambiar_la_granja_destino_mueve_a_los_animales_y_sus_asientos()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        dto.GranjaDestinoId = e.Ids.Montelindo;

        await e.Kardex.EditarCompraAsync(dto);

        (await e.AnimalAsync("A-1")).GranjaId.Should().Be(e.Ids.Montelindo);
        (await e.AnimalAsync("A-2")).GranjaId.Should().Be(e.Ids.Montelindo);
        (await e.RemisionAsync("FAC-1")).GranjaDestinoId.Should().Be(e.Ids.Montelindo);
        (await e.MovimientosAsync("A-1")).Single().GranjaDestinoId.Should().Be(e.Ids.Montelindo);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Con_movimientos_posteriores_no_se_puede_cambiar_lo_estructural_pero_si_corregir_el_precio()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);

        // Un cierre posterior genera una valorización para A-2 (de 4 a 9 meses: Terneros → Levante).
        e.Reloj.Ahora = Datos.F("2027-03-31");
        await e.Aging.ValorizarAlCorteAsync(Datos.F("2027-03-31"));
        (await e.MovimientosAsync("A-2")).Should().Contain(m => m.TipoMovimiento == TipoMovimiento.Valorizacion);

        dto.Animales[1].Sexo = Sexo.Macho;
        var estructural = () => e.Kardex.EditarCompraAsync(dto);
        await estructural.Should().ThrowAsync<ValidationException>()
            .WithMessage("*A-2 ya tiene movimientos posteriores*Valorizacion*ficha del bovino*");

        dto.Animales[1].Sexo = Sexo.Hembra;
        dto.Animales[1].PrecioFactura = 175_000m;
        await e.Kardex.EditarCompraAsync(dto);

        var a2 = await e.AnimalAsync("A-2");
        a2.ValorCompraOrigen.Should().Be(175_000m);
        a2.ValorContableActual.Should().Be(1_200_000m, "el libro no se toca");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task No_se_puede_quitar_de_la_compra_un_animal_con_movimientos_posteriores()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 6_500_000m)));
        dto.Animales.RemoveAt(0); // se intenta quitar A-1, ya vendido

        var act = () => e.Kardex.EditarCompraAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*A-1 ya tiene movimientos posteriores*quitarlo de la compra*");
        (await e.ExisteAnimalAsync("A-1")).Should().BeTrue();
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Rechaza_un_numero_de_factura_ya_usado_por_otro_documento()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-2", e.AnimalComprado("B-1", Sexo.Hembra, "2026-06-20", 150_000m)));
        dto.NumeroFactura = "fac-2";

        var act = () => e.Kardex.EditarCompraAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe un documento con el número fac-2*");
    }

    [Fact]
    public async Task Rechaza_una_chapeta_usada_por_otro_animal_pero_acepta_conservar_la_propia()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        await e.AltaTerneraAsync("OTRA-1");
        dto.Animales[1].ChapetaAmarilla = "OTRA-1";

        var act = () => e.Kardex.EditarCompraAsync(dto);
        await act.Should().ThrowAsync<ValidationException>().WithMessage("*OTRA-1 ya está registrada*");

        dto.Animales[1].ChapetaAmarilla = "A-2";
        dto.Animales[1].Nombre = "Lucero";
        await e.Kardex.EditarCompraAsync(dto);
        (await e.AnimalAsync("A-2")).Nombre.Should().Be("Lucero");
    }

    [Fact]
    public async Task Un_animal_que_no_pertenece_a_la_compra_se_rechaza()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        var ajeno = await e.AltaTerneraAsync("AJENO-1");
        dto.Animales[1].AnimalId = ajeno.Id;

        var act = () => e.Kardex.EditarCompraAsync(dto);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*no pertenece a esta compra*");
    }

    [Fact]
    public async Task Editar_exige_indicar_la_compra_y_que_sea_de_tipo_compra()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        await e.AltaVacaAsync("V-1");
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("V-1", 1_000_000m)));

        // Cada aserción se espera antes de mutar el DTO: la lambda lo captura por referencia.
        dto.RemisionId = null;
        await ((Func<Task>)(() => e.Kardex.EditarCompraAsync(dto))).Should().ThrowAsync<ValidationException>().WithMessage("*Falta indicar la compra*");

        dto.RemisionId = (await e.RemisionAsync("REM-1")).Id;
        await ((Func<Task>)(() => e.Kardex.EditarCompraAsync(dto))).Should().ThrowAsync<ValidationException>().WithMessage("*no es de tipo COMPRA*");
    }

    [Fact]
    public async Task Una_edicion_fallida_no_deja_cambios_parciales()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dto = await CompraBaseAsync(e);
        dto.Proveedor = "Cambiado";
        dto.Animales[0].PrecioFactura = 9_999_999m;
        dto.Animales[1].OrientacionId = 999; // inválida: aborta toda la edición

        var act = () => e.Kardex.EditarCompraAsync(dto);

        await act.Should().ThrowAsync<ValidationException>();
        (await e.RemisionAsync("FAC-1")).TerceroEntidad.Should().Be("Ganadería La Soledad");
        (await e.AnimalAsync("A-1")).ValorCompraOrigen.Should().Be(3_000_000m);
        await e.AsegurarInvarianteAsync();
    }
}
