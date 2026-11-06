using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Animales;

public class AnimalServiceTests
{
    private static AnimalService Crear(EscenarioKardex e) => new(e.Db.Uow, e.Aging, e.Reloj);

    private static async Task<AnimalEdicionDto> EdicionDeAsync(EscenarioKardex e, string chapeta)
    {
        var a = await e.AnimalAsync(chapeta);
        return new AnimalEdicionDto
        {
            AnimalId = a.Id, ChapetaAmarilla = a.ChapetaAmarilla, ChapetaRoja = a.ChapetaRoja, Nombre = a.Nombre, Sexo = a.Sexo,
            FechaNacimiento = a.FechaNacimiento, Raza = a.Raza, PesoActualKg = a.PesoActualKg, OrientacionId = a.OrientacionId,
            NumeroPartos = a.NumeroPartos, ExentoValorizacion = a.ExentoValorizacion, MadreIdentificacion = a.MadreIdentificacion,
        };
    }

    [Fact]
    public async Task Corrige_los_datos_de_la_ficha_sin_tocar_los_precios_ni_la_ubicacion()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("K-1", Sexo.Hembra, "2022-03-01", 3_225_970m, partos: 1)));
        var antes = await e.AnimalAsync("K-1");
        var dto = await EdicionDeAsync(e, "K-1");
        dto.ChapetaRoja = "K-1R";
        dto.Nombre = "  Lucero ";
        dto.Raza = "Normando";
        dto.PesoActualKg = 512.5m;
        dto.NumeroPartos = 2;
        dto.ExentoValorizacion = true;
        dto.MadreIdentificacion = "044";

        await Crear(e).ActualizarAsync(dto);

        var despues = await e.AnimalAsync("K-1");
        despues.ChapetaRoja.Should().Be("K-1R");
        despues.Nombre.Should().Be("Lucero");
        despues.Raza.Should().Be("Normando");
        despues.PesoActualKg.Should().Be(512.5m);
        despues.NumeroPartos.Should().Be(2);
        despues.ExentoValorizacion.Should().BeTrue();
        despues.MadreIdentificacion.Should().Be("044");
        despues.ValorCompraOrigen.Should().Be(antes.ValorCompraOrigen, "el costo de origen es inmutable");
        despues.ValorContableActual.Should().Be(antes.ValorContableActual, "el valor lo recalcula el cierre, no la ficha");
        despues.GranjaId.Should().Be(antes.GranjaId);
        despues.ClaseEntrada.Should().Be(ClaseEntrada.Compra);
        despues.FechaCompra.Should().Be(antes.FechaCompra);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task El_cambio_de_orientacion_o_nacimiento_queda_para_el_proximo_cierre()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-11-30");
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        var dto = await EdicionDeAsync(e, "A-1");
        dto.OrientacionId = e.Ids.Carne;

        await Crear(e).ActualizarAsync(dto);

        (await e.AnimalAsync("A-1")).ValorContableActual.Should().Be(6_000_000m, "todavía no se ha valorizado");
        await e.Aging.ValorizarAlCorteAsync(Datos.F("2026-11-30"));
        (await e.AnimalAsync("A-1")).ValorContableActual.Should().Be(4_000_000m, "el cierre aplica la tarifa de carne");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task La_chapeta_no_puede_repetirse_pero_si_conservarse()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.AltaVacaAsync("A-2");
        var dto = await EdicionDeAsync(e, "A-1");

        dto.ChapetaAmarilla = "a-2";
        await ((Func<Task>)(() => Crear(e).ActualizarAsync(dto))).Should().ThrowAsync<ValidationException>().WithMessage("*a-2 ya está registrada en otro animal*");

        dto.ChapetaAmarilla = "A-1";
        dto.Nombre = "Sin cambios de chapeta";
        await Crear(e).ActualizarAsync(dto);
        (await e.AnimalAsync("A-1")).Nombre.Should().Be("Sin cambios de chapeta");

        dto.ChapetaAmarilla = "A-1B";
        await Crear(e).ActualizarAsync(dto);
        (await e.ExisteAnimalAsync("A-1B")).Should().BeTrue();
    }

    [Fact]
    public async Task Valida_los_datos_de_la_ficha()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("K-1", Sexo.Hembra, "2022-03-01", 3_000_000m, partos: 1)));
        var servicio = Crear(e);

        async Task Rechaza(Action<AnimalEdicionDto> cambio, string mensaje)
        {
            var dto = await EdicionDeAsync(e, "K-1");
            cambio(dto);
            await ((Func<Task>)(() => servicio.ActualizarAsync(dto))).Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
        }

        await Rechaza(d => d.ChapetaAmarilla = " ", "*chapeta amarilla es obligatorio*");
        await Rechaza(d => d.Sexo = Sexo.Ambos, "*sexo debe ser Macho o Hembra*");
        await Rechaza(d => d.Raza = "", "*raza es obligatorio*");
        await Rechaza(d => d.FechaNacimiento = Datos.F("2026-11-07"), "*no puede ser futura*");
        await Rechaza(d => d.FechaNacimiento = default, "*es obligatoria*");
        await Rechaza(d => d.FechaNacimiento = Datos.F("2026-10-23"), "*posterior a la fecha de compra*");
        await Rechaza(d => d.PesoActualKg = -1, "*peso no puede ser negativo*");
        await Rechaza(d => d.NumeroPartos = -1, "*partos no puede ser negativo*");
        await Rechaza(d => { d.Sexo = Sexo.Macho; d.NumeroPartos = 1; }, "*macho no puede tener partos*");
        await Rechaza(d => d.OrientacionId = 999, "*orientación productiva no existe*");
        await ((Func<Task>)(() => servicio.ActualizarAsync(new AnimalEdicionDto { AnimalId = 9999, ChapetaAmarilla = "X", Sexo = Sexo.Hembra, FechaNacimiento = Datos.F("2026-01-01"), Raza = "H", OrientacionId = 1 })))
            .Should().ThrowAsync<ValidationException>().WithMessage("*animal no existe*");
    }

    [Fact]
    public async Task Los_animales_que_ya_salieron_tambien_se_pueden_corregir()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-1"));
        var dto = await EdicionDeAsync(e, "A-1");
        dto.Nombre = "Corregida";

        await Crear(e).ActualizarAsync(dto);

        var animal = await e.AnimalAsync("A-1");
        animal.Nombre.Should().Be("Corregida");
        animal.Estado.Should().Be(EstadoAnimal.Muerto);
        await e.AsegurarInvarianteAsync();
    }

    // ---- Vista previa de la valoración ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(Sexo.Hembra, 0, "Terneros", 150_000)]
    [InlineData(Sexo.Macho, 0, "Terneros", 160_000)]
    public async Task La_vista_previa_de_un_recien_nacido_muestra_categoria_y_valor_de_la_matriz(string sexo, int partos, string categoria, int valor)
    {
        await using var e = await EscenarioKardex.CrearAsync();

        var resultado = await Crear(e).PrevisualizarValoracionAsync(sexo, e.Ids.Lacteo, partos, Datos.F("2026-11-06"));

        resultado.CategoriaNombre.Should().Be(categoria);
        resultado.Valor.Should().Be(valor);
        resultado.SinTarifa.Should().BeFalse();
    }

    [Fact]
    public async Task La_vista_previa_indica_Sin_tarifa_y_rechaza_fechas_futuras_o_sexo_invalido()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var servicio = Crear(e);

        var sinTarifa = await servicio.PrevisualizarValoracionAsync(Sexo.Macho, e.Ids.Lacteo, 0, Datos.F("2025-09-01"));
        sinTarifa.SinTarifa.Should().BeTrue();
        sinTarifa.CategoriaNombre.Should().Be(NombresBase.CategoriaSinClasificar);

        await ((Func<Task>)(() => servicio.PrevisualizarValoracionAsync(Sexo.Hembra, e.Ids.Lacteo, 0, Datos.F("2026-12-01")))).Should().ThrowAsync<ValidationException>().WithMessage("*futura*");
        await ((Func<Task>)(() => servicio.PrevisualizarValoracionAsync("", e.Ids.Lacteo, 0, Datos.F("2026-11-01")))).Should().ThrowAsync<ValidationException>().WithMessage("*sexo*");
    }
}
