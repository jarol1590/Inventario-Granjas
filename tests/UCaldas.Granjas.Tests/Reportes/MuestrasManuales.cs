using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Services.Reportes;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Reportes;

/// <summary>Se omite salvo que exista la variable de entorno GRANJAS_MUESTRAS (carpeta de salida).</summary>
public sealed class MuestraManualFactAttribute : FactAttribute
{
    public const string Variable = "GRANJAS_MUESTRAS";

    public MuestraManualFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Herramienta de revisión visual: defina {Variable} con la carpeta donde guardar las muestras.";
        }
    }
}

/// <summary>
/// No es una prueba de comportamiento: genera informes de muestra con datos realistas y una firma simulada para
/// abrirlos en Excel y revisarlos a ojo. Uso: <c>$env:GRANJAS_MUESTRAS="C:\carpeta"; dotnet test --filter MuestrasManuales</c>.
/// </summary>
public class MuestrasManuales
{
    [MuestraManualFact]
    public async Task Genera_informes_de_muestra_para_revision_visual()
    {
        var carpeta = Environment.GetEnvironmentVariable(MuestraManualFactAttribute.Variable)!;
        Directory.CreateDirectory(carpeta);
        var firma = "data:image/png;base64," + Convert.ToBase64String(FirmaDePrueba.GenerarPng());

        await using (var e = await EscenariosDeReporte.CrearSeptiembreDeLaCruzAsync())
        {
            await EscenariosDeReporte.ConfigurarFirmaAsync(e, firma);
            await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 9, null, Path.Combine(carpeta, "kardex-septiembre-2026.xlsx")));
        }

        await using (var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync())
        {
            await EscenariosDeReporte.ConfigurarFirmaAsync(e, firma);
            await Motor(e).GenerarKardexFinancieroAsync(new KardexReporteRequest(2026, 10, null, Path.Combine(carpeta, "kardex-octubre-2026.xlsx")));
            await Motor(e).GenerarInventarioFisicoAsync(new InventarioFisicoRequest(
                null, null, null, EstadoAnimal.Disponible, Path.Combine(carpeta, "inventario-fisico.xlsx")));
            await Motor(e).GenerarHojaDeVidaAsync(await e.IdAsync("P-1"), Path.Combine(carpeta, "hoja-de-vida-P-1.xlsx"));
            await Motor(e).GenerarInventarioIndividualAsync(new InventarioIndividualRequest(null, null, Path.Combine(carpeta, "inventario-individual.xlsx")));
        }
    }

    [MuestraManualFact]
    public async Task Genera_el_kardex_de_aves_de_muestra()
    {
        var carpeta = Environment.GetEnvironmentVariable(MuestraManualFactAttribute.Variable)!;
        Directory.CreateDirectory(carpeta);
        var firma = "data:image/png;base64," + Convert.ToBase64String(FirmaDePrueba.GenerarPng());

        var e = await EscenarioKardex.CrearAsync("2026-11-06");
        await using var _ = e;
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, firma);
        var lotes = new LoteService(e.Db.Uow, e.Reloj);
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            var aves = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!;
            var etapa = (await uow.Categorias.GetByNombreAsync(aves.Id, NombresBase.CategoriaGeneral))!.Id;
            foreach (var (codigo, cantidad) in new[] { ("1", 1000), ("2", 800) })
            {
                var id = await lotes.RegistrarNuevoLoteAsync(new NuevoLoteDto
                {
                    ProgramaId = aves.Id, GranjaId = e.Ids.Montelindo, EtapaId = etapa, ClaseEntrada = ClaseEntrada.Compra, CodigoLote = codigo,
                    LineaRaza = "LBROWN", FechaIngreso = Datos.F("2026-10-25"), Cantidad = cantidad, PrecioUnitarioFactura = 15_000m,
                    Factura = "FAC-" + codigo, Proveedor = "Incubadora",
                });
                await lotes.RegistrarBajaAsync(id, 40, "Mortalidad", Datos.F("2026-11-04"), null);
            }
        }

        await Motor(e).GenerarKardexLotesAsync(new KardexLotesRequest(2026, 11, null, null, Path.Combine(carpeta, "movimientos-inventario-aves.xlsx")));
    }

    private static ExcelReportingEngine Motor(Kardex.EscenarioKardex e)
        => new(e.Db.Uow, new KardexReportBuilder(e.Db.Uow), new FichaService(e.Db.Uow, e.Reloj), e.Reloj);
}
