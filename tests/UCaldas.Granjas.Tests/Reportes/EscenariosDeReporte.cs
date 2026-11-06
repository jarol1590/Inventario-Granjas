using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Reportes;

public static class EscenariosDeReporte
{
    /// <summary>PNG de 1×1 píxel: suficiente para que el libro de Excel incruste una imagen de firma.</summary>
    public const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    public static readonly string[] ChapetasDeSeptiembre = ["304-6", "306-6", "308-6", "309-6", "310-6", "311-6", "314-6", "312-6", "313-6"];

    /// <summary>
    /// Reproduce las cifras reales del Kardex "Ganado" de La Cruz de septiembre de 2026:
    /// 99 animales por $65.947.815 al inicio, 9 nacimientos de $52.500, valorización de −$450.620
    /// y saldo final de 108 animales por $65.969.695.
    /// </summary>
    public static async Task<EscenarioKardex> CrearSeptiembreDeLaCruzAsync()
    {
        var e = await EscenarioKardex.CrearAsync("2026-10-08");
        await using var uow = await e.Db.Uow.BeginAsync();

        for (var i = 1; i <= 99; i++)
        {
            var valor = i == 99 ? 679_815m : 666_000m;
            var animal = Datos.Animal(e.Ids, $"A-{i:000}", Sexo.Hembra, "2020-01-10", NombresBase.CategoriaVacasGeneral, valor,
                granjaId: e.Ids.LaCruz, registro: "2026-08-15");
            await Datos.AltaAsync(uow, animal, Datos.F("2026-08-15"));
        }

        foreach (var chapeta in ChapetasDeSeptiembre)
        {
            var cria = Datos.Animal(e.Ids, chapeta, Sexo.Hembra, "2026-09-10", NombresBase.CategoriaTerneros, 52_500m,
                granjaId: e.Ids.LaCruz, registro: "2026-09-10");
            await Datos.AltaAsync(uow, cria, Datos.F("2026-09-10"));
        }

        var primera = (await uow.Animales.GetByChapetaAsync("A-001"))!;
        await uow.Movimientos.InsertAsync(new MovimientoInventario
        {
            AnimalId = primera.Id,
            TipoMovimiento = TipoMovimiento.Valorizacion,
            FechaMovimiento = Datos.F("2026-09-30"),
            GranjaDestinoId = e.Ids.LaCruz,
            CostoAsociado = -450_620m,
        });
        await uow.Animales.ActualizarValoracionAsync(primera.Id, primera.CategoriaId, primera.ValorContableActual - 450_620m);

        await uow.CommitAsync();
        return e;
    }

    /// <summary>
    /// Octubre de 2026 en Tesorito con todos los tipos de movimiento: compra, nacimiento, venta, muerte, traslado
    /// (a La Cruz) y valorización. Los demás meses y granjas no se mezclan.
    /// </summary>
    public static async Task<EscenarioKardex> CrearOctubreDeTesoritoAsync()
    {
        var e = await EscenarioKardex.CrearAsync("2026-10-31");
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 3_750_000m);
        await e.AltaVacaAsync("A-3", valor: 4_000_000m);
        await e.AltaTerneraAsync("T-1", nacimiento: "2026-07-15", valor: 150_000m); // se valoriza a 800 mil al cierre de octubre

        await e.Kardex.RegistrarCompraAsync(e.Compra(
            "FAC-1",
            e.AnimalComprado("P-1", Sexo.Hembra, "2022-03-01", 3_200_000m, partos: 1),
            e.AnimalComprado("P-2", Sexo.Hembra, "2026-06-20", 150_000m)));
        await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("N-1", nacimiento: "2026-10-06"));
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 6_500_000m)));
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-2"));
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "A-3"));
        await e.Aging.ValorizarAlCorteAsync(Datos.F("2026-10-31"));
        return e;
    }

    /// <summary>Guarda la configuración de firma (con o sin imagen) para los informes.</summary>
    public static async Task ConfigurarFirmaAsync(EscenarioKardex e, string? imagen)
    {
        await using var uow = await e.Db.Uow.BeginAsync();
        await uow.Configuracion.GuardarFirmaAsync(new ConfiguracionFirma
        {
            NombreAutoridad = "Paula Valencia Arango",
            CargoAutoridad = "DECANO(A)",
            EntidadFacultad = "FACULTAD DE CIENCIAS AGROPECUARIAS",
            Telefono = "8781586",
            RutaImagenFirma = imagen,
        });
        await uow.CommitAsync();
    }
}
