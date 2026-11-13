using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;
using UCaldas.Granjas.Tests.Presentacion;

namespace UCaldas.Granjas.Tests.Consultas;

/// <summary>Inicio con un programa de lotes: las etapas del programa y las cabezas de sus lotes.</summary>
public class DashboardDeLotesTests
{
    private sealed record Entorno(EscenarioKardex E, LoteService Lotes, Programa Aves, ConsultasService Consultas, int Cria, int Postura);

    private static async Task<Entorno> CrearAsync()
    {
        var e = await EscenarioKardex.CrearAsync();
        Programa aves;
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            aves = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!;
        }

        var tarifas = new TarifaProgramaService(e.Db.Uow);
        var filas = (await tarifas.ListarAsync(aves.Id)).ToList();
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = "Cría", UnidadCobro = "UNIDAD", ValorReferencia = 15_000m });
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = "Postura", UnidadCobro = "UNIDAD", ValorReferencia = 40_000m });
        await tarifas.GuardarAsync(aves.Id, filas);

        await using var lectura = await e.Db.Uow.OpenReadOnlyAsync();
        var etapas = await lectura.Categorias.GetByProgramaAsync(aves.Id);
        return new Entorno(e, new LoteService(e.Db.Uow, e.Reloj), aves, new ConsultasService(e.Db.Uow, e.Reloj),
            etapas.Single(c => c.Nombre == "Cría").Id, etapas.Single(c => c.Nombre == "Postura").Id);
    }

    private static Task<int> LoteAsync(Entorno s, string codigo, int granja, int etapa, int cantidad, string clase = ClaseEntrada.Compra, bool sinConteo = false)
        => s.Lotes.RegistrarNuevoLoteAsync(new NuevoLoteDto
        {
            ProgramaId = s.Aves.Id, GranjaId = granja, EtapaId = etapa, ClaseEntrada = clase, CodigoLote = codigo, Ubicacion = "G1", LineaRaza = "Hy",
            FechaIngreso = Datos.F("2026-11-02"), ControlNumericoPoblacion = !sinConteo, Cantidad = cantidad, PrecioUnitarioFactura = 10_000m,
            Factura = clase == ClaseEntrada.Compra ? "F-" + codigo : null, OrdenCompra = clase == ClaseEntrada.Compra ? "OC" : null,
            Proveedor = clase == ClaseEntrada.Compra ? "P" : null,
        });

    [Fact]
    public async Task Un_programa_de_lotes_lista_todas_sus_etapas_en_cero_aunque_no_haya_lotes()
    {
        var s = await CrearAsync();
        await using var _ = s.E;

        var d = await s.Consultas.ObtenerDashboardAsync(null, s.Aves.Id, Datos.F("2026-11-06"));

        d.EsLotes.Should().BeTrue();
        d.PorCategoria.Select(p => (p.Etiqueta, p.Valor)).Should().Equal(("General", 0m), ("Cría", 0m), ("Postura", 0m));
        (d.TotalAnimales, d.SaldoTotal).Should().Be((0, 0m));
        d.MovimientosMes.Select(p => p.Etiqueta).Should().Equal("Nacimientos", "Compras", "Ventas", "Traslados");
    }

    [Fact]
    public async Task Las_barras_son_cabezas_vivas_por_etapa_y_por_granja_y_el_saldo_suma_el_valor_de_los_lotes()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var ids = s.E.Ids;
        await LoteAsync(s, "L-1", ids.Montelindo, s.Cria, 1000);
        await LoteAsync(s, "L-2", ids.Tesorito, s.Cria, 500);
        await LoteAsync(s, "L-3", ids.Montelindo, s.Postura, 200);
        await LoteAsync(s, "L-4", ids.Montelindo, s.Postura, 0, sinConteo: true);       // sin conteo: suma 1
        var baja = await LoteAsync(s, "L-5", ids.Montelindo, s.Cria, 50);
        await s.Lotes.RegistrarBajaAsync(baja, 50, "Mortandad", Datos.F("2026-11-05"), null);   // finalizado: no cuenta

        var todos = await s.Consultas.ObtenerDashboardAsync(null, s.Aves.Id, Datos.F("2026-11-06"));
        var montelindo = await s.Consultas.ObtenerDashboardAsync(ids.Montelindo, s.Aves.Id, Datos.F("2026-11-06"));

        todos.PorGranja.Select(p => (p.Etiqueta, p.Valor)).Should().Equal(("Tesorito", 500m), ("La Cruz", 0m), ("Montelindo", 1201m));
        todos.TotalAnimales.Should().Be(1701);
        todos.SaldoTotal.Should().Be(1000 * 10_000m + 500 * 10_000m + 200 * 10_000m + 10_000m);
        montelindo.PorCategoria.Select(p => (p.Etiqueta, p.Valor)).Should().Equal(("General", 0m), ("Cría", 1000m), ("Postura", 201m));
        montelindo.TotalAnimales.Should().Be(1201);
    }

    [Fact]
    public async Task Los_movimientos_del_mes_cuentan_compras_nacimientos_ventas_y_traslados_de_lotes()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        var ids = s.E.Ids;
        var compra = await LoteAsync(s, "L-C", ids.Montelindo, s.Cria, 1000);
        await LoteAsync(s, "L-N", ids.Montelindo, s.Cria, 300, ClaseEntrada.Nacimiento);
        await s.E.Kardex.RegistrarVentaLoteAsync(new VentaLoteDto
        {
            NumeroOficio = "OF-1", FechaOficio = Datos.F("2026-11-05"), ProgramaId = s.Aves.Id, NumeroRemision = "REM-1", FechaRemision = Datos.F("2026-11-05"),
            ReciboCaja = "RC", GranjaOrigenId = ids.Montelindo, Comprador = "X",
            Lotes = [new VentaLoteLineaDto { LoteId = compra, Cantidad = 400, PesoKg = 800m, ValorVenta = 9_000_000m }],
        });
        await s.E.Kardex.RegistrarTrasladoLoteAsync(new TrasladoLoteDto
        {
            NumeroRemision = "TRA-1", FechaTraslado = Datos.F("2026-11-05"), GranjaOrigenId = ids.Montelindo, GranjaDestinoId = ids.Tesorito,
            Lotes = [new TrasladoLoteLineaDto { LoteId = compra, Cantidad = 100, CodigoNuevoLote = "L-C-T" }],
        });

        var d = await s.Consultas.ObtenerDashboardAsync(null, s.Aves.Id, Datos.F("2026-11-06"));
        var otroMes = await s.Consultas.ObtenerDashboardAsync(null, s.Aves.Id, Datos.F("2026-10-15"));

        d.MovimientosMes.Select(p => (p.Etiqueta, p.Valor)).Should().Equal(("Nacimientos", 300m), ("Compras", 1000m), ("Ventas", 400m), ("Traslados", 100m));
        (d.NacimientosMes, d.SalidasMes).Should().Be((300, 400));
        otroMes.MovimientosMes.Should().OnlyContain(p => p.Valor == 0m);
    }

    [Fact]
    public async Task El_tablero_de_ganaderia_no_cambia_y_la_pantalla_de_inicio_cambia_sus_textos_segun_el_programa()
    {
        var s = await CrearAsync();
        await using var _ = s.E;
        await LoteAsync(s, "L-1", s.E.Ids.Montelindo, s.Cria, 1000);
        var contexto = new ContextoGlobal { ProgramaId = s.Aves.Id };
        var vm = new InicioViewModel(s.Consultas, contexto, s.E.Reloj, new DialogoFalso());

        await vm.CargarAsync();
        (vm.EtiquetaTotal, vm.EtiquetaSaldo, vm.TituloAnimales, vm.TotalAnimales).Should().Be(("Total Cabezas", "Saldo total lotes", "Cabezas por Granja", 1000));

        contexto.GranjaId = s.E.Ids.Montelindo;
        await vm.CargarAsync();
        vm.TituloAnimales.Should().Be("Cabezas por Etapa");
        vm.EjesAnimalesY.Single().Labels.Should().Equal("General (0)", "Cría", "Postura (0)");

        contexto.ProgramaId = s.E.Ids.ProgramaGanaderia;
        contexto.GranjaId = null;
        await vm.CargarAsync();
        (vm.EtiquetaTotal, vm.EtiquetaSaldo, vm.TituloAnimales).Should().Be(("Total Animales", "Saldo total", "Animales por Granja"));
        (await s.Consultas.ObtenerDashboardAsync(null, s.E.Ids.ProgramaGanaderia, Datos.F("2026-11-06"))).EsLotes.Should().BeFalse();
    }
}