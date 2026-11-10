using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;
using UCaldas.Granjas.Tests.Reportes;

namespace UCaldas.Granjas.Tests.Consultas;

public class ConsultasServiceTests
{
    private static ConsultasService Crear(EscenarioKardex e) => new(e.Db.Uow, e.Reloj);

    private static readonly DateTime Octubre1 = new(2026, 10, 1);
    private static readonly DateTime Octubre31 = new(2026, 10, 31);

    // ---- Animales ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Por_defecto_lista_solo_los_disponibles_con_sus_datos_y_el_resumen()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();

        var r = await Crear(e).BuscarAnimalesAsync(new AnimalFiltro());

        r.Items.Select(i => i.ChapetaAmarilla).Should().Equal("A-3", "N-1", "P-1", "P-2", "T-1");
        r.Resumen.Cantidad.Should().Be(5);
        r.Resumen.Hembras.Should().Be(5);
        r.Resumen.Machos.Should().Be(0);
        r.Resumen.ValorContableTotal.Should().Be(13_750_000m);

        var t1 = r.Items.Single(i => i.ChapetaAmarilla == "T-1");
        t1.GranjaNombre.Should().Be("Tesorito");
        t1.Orientacion.Should().Be("Lácteo");
        t1.Categoria.Should().Be("Terneros");
        t1.EdadMeses.Should().Be(3, "nació el 15/07/2026 y hoy es 31/10/2026");
        t1.ValorContable.Should().Be(800_000m);
        t1.Valorizacion.Should().Be(650_000m, "variación acumulada por el cierre de octubre");
        t1.SinTarifa.Should().BeFalse();

        var p1 = r.Items.Single(i => i.ChapetaAmarilla == "P-1");
        p1.ValorCompraOrigen.Should().Be(3_200_000m);
        p1.ValorContable.Should().Be(6_000_000m);
        r.Resumen.ValorCompraTotal.Should().Be(3_350_000m);
    }

    [Fact]
    public async Task Filtra_por_granja_estado_orientacion_sexo_categoria_raza_y_texto()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var servicio = Crear(e);

        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(GranjaId: e.Ids.LaCruz))).Items.Select(i => i.ChapetaAmarilla).Should().Equal("A-3");
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Estado: EstadoAnimal.Vendido))).Items.Select(i => i.ChapetaAmarilla).Should().Equal("A-1");
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Estado: EstadoAnimal.Muerto))).Items.Select(i => i.ChapetaAmarilla).Should().Equal("A-2");
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Estado: null))).Items.Should().HaveCount(7);
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(OrientacionId: e.Ids.Carne))).Items.Should().BeEmpty();
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Sexo: Sexo.Macho))).Items.Should().BeEmpty();
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(CategoriaId: e.Ids.Categoria(NombresBase.CategoriaVacasGeneral)))).Items.Select(i => i.ChapetaAmarilla).Should().Equal("A-3", "P-1");
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Raza: "holstein"))).Items.Should().HaveCount(5);
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Raza: "Jersey"))).Items.Should().BeEmpty();
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Texto: "p-"))).Items.Select(i => i.ChapetaAmarilla).Should().Equal("P-1", "P-2");
    }

    [Fact]
    public async Task La_busqueda_por_texto_tambien_mira_la_chapeta_roja_y_el_nombre()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var vaca = await e.AltaVacaAsync("0940-26");
        await e.Db.EjecutarAsync("UPDATE ANIMAL SET chapeta_roja = '0940-ROJA', nombre = 'Lucero' WHERE id = @id", new { id = vaca.Id });
        await e.AltaVacaAsync("0941-26");
        var servicio = Crear(e);

        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Texto: "roja"))).Items.Should().ContainSingle();
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Texto: "lucer"))).Items.Should().ContainSingle();
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Texto: "  094  "))).Items.Should().HaveCount(2);
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(Texto: "zzz"))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task El_filtro_de_programa_usa_la_categoria_del_animal()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        var aves = await e.Db.EscalarAsync<int>("SELECT id FROM PROGRAMA WHERE codigo = 'PRG-AVE'");
        var servicio = Crear(e);

        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(ProgramaId: e.Ids.ProgramaGanaderia))).Items.Should().ContainSingle();
        (await servicio.BuscarAnimalesAsync(new AnimalFiltro(ProgramaId: aves))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Marca_los_animales_Sin_tarifa_y_calcula_edad_cero_si_la_fecha_es_futura()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Db.QuitarFilaDeLevanteParaMachosAsync(); // una matriz editada con un hueco
        await e.Kardex.RegistrarNacimientoAsync(e.Nacimiento("0800-25", Sexo.Macho, nacimiento: "2025-08-01"));

        var item = (await Crear(e).BuscarAnimalesAsync(new AnimalFiltro())).Items.Single();

        item.SinTarifa.Should().BeTrue();
        item.Categoria.Should().Be(NombresBase.CategoriaSinClasificar);
        item.EdadMeses.Should().Be(15);
    }

    [Fact]
    public async Task Buscar_disponibles_devuelve_solo_los_de_esa_granja_y_filtra_por_texto()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var servicio = Crear(e);

        var tesorito = await servicio.BuscarDisponiblesAsync(e.Ids.Tesorito);
        var filtrados = await servicio.BuscarDisponiblesAsync(e.Ids.Tesorito, "p-");

        tesorito.Select(a => a.ChapetaAmarilla).Should().Equal("N-1", "P-1", "P-2", "T-1");
        filtrados.Select(a => a.ChapetaAmarilla).Should().Equal("P-1", "P-2");
        var p1 = filtrados.Single(a => a.ChapetaAmarilla == "P-1");
        p1.Categoria.Should().Be("Vacas General");
        p1.Orientacion.Should().Be("Lácteo");
        p1.ValorContable.Should().Be(6_000_000m);
        (await servicio.BuscarDisponiblesAsync(e.Ids.Montelindo)).Should().BeEmpty();
    }

    // ---- Compras ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lista_las_compras_con_cantidad_total_y_resumen()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var servicio = Crear(e);

        var r = await servicio.ListarComprasAsync(new CompraFiltro(null, Octubre1, Octubre31));

        var compra = r.Items.Should().ContainSingle().Subject;
        compra.NumeroFactura.Should().Be("FAC-1");
        compra.OrdenCompra.Should().Be("OC-2026-018");
        compra.Proveedor.Should().Be("Ganadería La Soledad");
        compra.Nit.Should().Be("890.123.456-1");
        compra.GranjaDestino.Should().Be("Tesorito");
        compra.Cantidad.Should().Be(2);
        compra.TotalFactura.Should().Be(3_350_000m);
        r.Resumen.Should().Be(new ResumenCompras(1, 2, 3_350_000m));

        (await servicio.ListarComprasAsync(new CompraFiltro(null, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)))).Items.Should().BeEmpty();
        (await servicio.ListarComprasAsync(new CompraFiltro(e.Ids.LaCruz, Octubre1, Octubre31))).Items.Should().BeEmpty();
        (await servicio.ListarComprasAsync(new CompraFiltro(null, Octubre1, Octubre31, Factura: "fac-"))).Items.Should().ContainSingle();
        (await servicio.ListarComprasAsync(new CompraFiltro(null, Octubre1, Octubre31, Factura: "nada"))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task El_rango_de_fechas_es_inclusivo_en_ambos_extremos()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var servicio = Crear(e);

        (await servicio.ListarComprasAsync(new CompraFiltro(null, new DateTime(2026, 10, 22), new DateTime(2026, 10, 22)))).Items.Should().ContainSingle();
        (await servicio.ListarComprasAsync(new CompraFiltro(null, new DateTime(2026, 10, 23), new DateTime(2026, 10, 31)))).Items.Should().BeEmpty();
        (await servicio.ListarComprasAsync(new CompraFiltro(null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 21)))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Las_compras_anuladas_se_ocultan_salvo_que_se_pidan_y_no_cuentan_en_el_resumen()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-10-31");
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("A-1", Sexo.Hembra, "2026-06-20", 150_000m)));
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-2", e.AnimalComprado("A-2", Sexo.Hembra, "2026-06-20", 150_000m)));
        await e.Kardex.AnularAsync((await e.RemisionAsync("FAC-1")).Id);
        var servicio = Crear(e);

        var visibles = await servicio.ListarComprasAsync(new CompraFiltro(null, Octubre1, Octubre31));
        var todas = await servicio.ListarComprasAsync(new CompraFiltro(null, Octubre1, Octubre31, IncluirAnuladas: true));

        visibles.Items.Select(i => i.NumeroFactura).Should().Equal("FAC-2");
        todas.Items.Should().HaveCount(2);
        todas.Items.Single(i => i.NumeroFactura == "FAC-1").Anulada.Should().BeTrue();
        todas.Resumen.Should().Be(new ResumenCompras(1, 1, 150_000m));
    }

    [Fact]
    public async Task Obtener_una_compra_devuelve_un_comando_listo_para_editar()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var id = (await e.RemisionAsync("FAC-1")).Id;

        var editable = await Crear(e).ObtenerCompraAsync(id);

        editable.Anulada.Should().BeFalse();
        var dto = editable.Dto;
        dto.RemisionId.Should().Be(id);
        dto.NumeroFactura.Should().Be("FAC-1");
        dto.OrdenCompra.Should().Be("OC-2026-018");
        dto.Proveedor.Should().Be("Ganadería La Soledad");
        dto.ProveedorNit.Should().Be("890.123.456-1");
        dto.GranjaDestinoId.Should().Be(e.Ids.Tesorito);
        dto.FechaCompra.Should().Be(Datos.F("2026-10-22"));
        dto.Animales.Select(a => a.ChapetaAmarilla).Should().Equal("P-1", "P-2");
        dto.Animales.Should().OnlyContain(a => a.AnimalId != null);
        dto.Animales.Single(a => a.ChapetaAmarilla == "P-1").PrecioFactura.Should().Be(3_200_000m);
        dto.Animales.Single(a => a.ChapetaAmarilla == "P-1").NumeroPartos.Should().Be(1);

        // El comando obtenido se puede reenviar tal cual al servicio de edición.
        await e.Kardex.EditarCompraAsync(dto);
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Obtener_un_documento_que_no_es_del_tipo_pedido_o_no_existe_se_rechaza()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var servicio = Crear(e);
        var venta = (await e.RemisionAsync("REM-1")).Id;
        var compra = (await e.RemisionAsync("FAC-1")).Id;

        await ((Func<Task>)(() => servicio.ObtenerCompraAsync(venta))).Should().ThrowAsync<ValidationException>().WithMessage("*no es una compra*");
        await ((Func<Task>)(() => servicio.ObtenerVentaAsync(compra))).Should().ThrowAsync<ValidationException>().WithMessage("*no es una venta*");
        await ((Func<Task>)(() => servicio.ObtenerBajaAsync(compra))).Should().ThrowAsync<ValidationException>().WithMessage("*no es una baja*");
        await ((Func<Task>)(() => servicio.ObtenerCompraAsync(9999))).Should().ThrowAsync<ValidationException>().WithMessage("*no existe*");
    }

    // ---- Salidas ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lista_ventas_bajas_y_traslados_con_su_resumen()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();

        var r = await Crear(e).ListarSalidasAsync(new SalidaFiltro(null, Octubre1, Octubre31));

        r.Items.Select(i => i.NumeroRemision).Should().Equal("REM-T-1", "REM-1", "REM-B-1");
        var venta = r.Items.Single(i => i.NumeroRemision == "REM-1");
        venta.Clase.Should().Be(ClaseSalida.Venta);
        venta.NumeroOficio.Should().Be("OF-1");
        venta.ReciboCaja.Should().Be("RC-1");
        venta.Destino.Should().Be("Frigorífico Manizales");
        venta.Cantidad.Should().Be(1);
        venta.Total.Should().Be(6_500_000m, "valor de venta real");

        var baja = r.Items.Single(i => i.NumeroRemision == "REM-B-1");
        baja.Clase.Should().Be(ClaseSalida.Muerte);
        baja.NumeroOficio.Should().BeNull();
        baja.ReciboCaja.Should().BeNull();
        baja.Destino.Should().Be("Acta Sanitaria N°4");
        baja.Total.Should().Be(3_750_000m, "costo en libros");

        var traslado = r.Items.Single(i => i.NumeroRemision == "REM-T-1");
        traslado.Clase.Should().Be(ClaseSalida.Traslado);
        traslado.Destino.Should().Be("La Cruz");

        r.Resumen.Should().Be(new ResumenSalidas(RecaudoVentas: 6_500_000m, AnimalesRetirados: 2, BajasPorMuerteHurto: 1));
    }

    [Fact]
    public async Task Filtra_las_salidas_por_granja_chapeta_y_fecha()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var servicio = Crear(e);

        (await servicio.ListarSalidasAsync(new SalidaFiltro(e.Ids.LaCruz, Octubre1, Octubre31))).Items.Should().BeEmpty("La Cruz no tuvo salidas, solo una llegada");
        (await servicio.ListarSalidasAsync(new SalidaFiltro(e.Ids.Tesorito, Octubre1, Octubre31, Chapeta: "a-2"))).Items.Select(i => i.NumeroRemision).Should().Equal("REM-B-1");
        (await servicio.ListarSalidasAsync(new SalidaFiltro(null, Octubre1, Octubre31, Chapeta: "ZZZ"))).Items.Should().BeEmpty();
        (await servicio.ListarSalidasAsync(new SalidaFiltro(null, new DateTime(2026, 10, 16), new DateTime(2026, 10, 21)))).Items.Select(i => i.NumeroRemision).Should().Equal("REM-1");
    }

    [Fact]
    public async Task Obtener_una_venta_y_una_baja_devuelve_comandos_reenviables_a_la_edicion()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var servicio = Crear(e);

        var venta = await servicio.ObtenerVentaAsync((await e.RemisionAsync("REM-1")).Id);
        venta.Dto.NumeroOficio.Should().Be("OF-1");
        venta.Dto.ProgramaId.Should().Be(e.Ids.ProgramaGanaderia);
        venta.Dto.ReciboCaja.Should().Be("RC-1");
        venta.Dto.Comprador.Should().Be("Frigorífico Manizales");
        venta.Dto.Animales.Should().ContainSingle().Which.PrecioVenta.Should().Be(6_500_000m);
        venta.Animales.Should().ContainSingle().Which.ChapetaAmarilla.Should().Be("A-1");
        await e.Kardex.EditarVentaAsync(venta.Dto);

        var baja = await servicio.ObtenerBajaAsync((await e.RemisionAsync("REM-B-1")).Id);
        baja.Dto.ClaseSalida.Should().Be(ClaseSalida.Muerte);
        baja.Dto.DocumentoSoporte.Should().Be("Acta Sanitaria N°4");
        baja.Animales.Should().ContainSingle().Which.ChapetaAmarilla.Should().Be("A-2");
        await e.Kardex.EditarBajaAsync(baja.Dto);

        await e.AsegurarInvarianteAsync();
    }

    // ---- Traslados -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lista_los_traslados_y_filtra_por_granja_de_origen_o_destino_y_por_texto()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var servicio = Crear(e);

        var todos = await servicio.ListarTrasladosAsync(new TrasladoFiltro(null, Octubre1, Octubre31));
        var item = todos.Items.Should().ContainSingle().Subject;
        item.NumeroRemision.Should().Be("REM-T-1");
        item.Origen.Should().Be("Tesorito");
        item.Destino.Should().Be("La Cruz");
        item.Cantidad.Should().Be(1);
        todos.Resumen.Should().Be(new ResumenTraslados(1, 1));

        (await servicio.ListarTrasladosAsync(new TrasladoFiltro(e.Ids.Tesorito, Octubre1, Octubre31))).Items.Should().ContainSingle("origen");
        (await servicio.ListarTrasladosAsync(new TrasladoFiltro(e.Ids.LaCruz, Octubre1, Octubre31))).Items.Should().ContainSingle("destino");
        (await servicio.ListarTrasladosAsync(new TrasladoFiltro(e.Ids.Montelindo, Octubre1, Octubre31))).Items.Should().BeEmpty();
        (await servicio.ListarTrasladosAsync(new TrasladoFiltro(null, Octubre1, Octubre31, Texto: "a-3"))).Items.Should().ContainSingle();
        (await servicio.ListarTrasladosAsync(new TrasladoFiltro(null, Octubre1, Octubre31, Texto: "rem-t"))).Items.Should().ContainSingle();
        (await servicio.ListarTrasladosAsync(new TrasladoFiltro(null, Octubre1, Octubre31, Texto: "otra"))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_traslado_anulado_no_cuenta_en_el_resumen()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-10-31");
        await e.AltaVacaAsync("A-1");
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "A-1"));
        await e.Kardex.AnularAsync((await e.RemisionAsync("REM-T-1")).Id);
        var servicio = Crear(e);

        (await servicio.ListarTrasladosAsync(new TrasladoFiltro(null, Octubre1, Octubre31))).Items.Should().BeEmpty();
        var incluidos = await servicio.ListarTrasladosAsync(new TrasladoFiltro(null, Octubre1, Octubre31, IncluirAnulados: true));
        incluidos.Items.Should().ContainSingle().Which.Anulado.Should().BeTrue();
        incluidos.Resumen.Should().Be(new ResumenTraslados(0, 0));
    }

    [Fact]
    public async Task Obtiene_un_traslado_con_la_orientacion_de_origen_y_el_costo_de_ese_momento()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-10-31");
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.AltaVacaAsync("A-2", valor: 3_750_000m);
        var dto = await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.Montelindo, "A-1", "A-2");
        dto.Animales[0].OrientacionDestinoId = e.Ids.Carne;
        dto.Animales[0].ObservacionesEspecificas = "Cojea";
        await e.Kardex.RegistrarTrasladoAsync(dto);
        // Después del traslado el animal sigue valorizándose: el detalle muestra lo que se trasladó, no el valor de hoy.
        await e.Db.EjecutarAsync("UPDATE ANIMAL SET valor_contable_actual = 9000000 WHERE chapeta_amarilla = 'A-1'");
        var id = (await e.RemisionAsync("REM-T-1")).Id;

        var traslado = await Crear(e).ObtenerTrasladoAsync(id);

        traslado.Anulada.Should().BeFalse();
        traslado.Dto.RemisionId.Should().Be(id);
        traslado.Dto.NumeroRemision.Should().Be("REM-T-1");
        traslado.Dto.FechaTraslado.Should().Be(Datos.F("2026-10-24"));
        traslado.Dto.GranjaOrigenId.Should().Be(e.Ids.Tesorito);
        traslado.Dto.GranjaDestinoId.Should().Be(e.Ids.Montelindo);
        traslado.Dto.Observaciones.Should().Be("Traslado de prueba");
        var idA1 = await e.IdAsync("A-1");
        var linea = traslado.Dto.Animales.Single(a => a.AnimalId == idA1);
        linea.OrientacionDestinoId.Should().Be(e.Ids.Carne);
        linea.ObservacionesEspecificas.Should().Be("Cojea");

        traslado.Animales.Select(a => a.ChapetaAmarilla).Should().Equal("A-1", "A-2");
        var a1 = traslado.Animales.Single(a => a.Id == idA1);
        a1.OrientacionId.Should().Be(e.Ids.Lacteo, "la orientación que tenía en la sede de origen");
        a1.Orientacion.Should().Be("Lácteo");
        a1.ValorContable.Should().Be(6_000_000m, "el costo en libros al momento del traslado");
    }

    [Fact]
    public async Task Obtener_un_traslado_falla_si_no_existe_o_el_documento_es_de_otro_tipo_y_marca_los_anulados()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-10-31");
        await e.AltaVacaAsync("A-1");
        await e.AltaVacaAsync("A-2");
        await e.Kardex.RegistrarBajaAsync(await e.BajaAsync("REM-B-1", ClaseSalida.Muerte, null, "A-2"));
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "A-1"));
        var traslado = (await e.RemisionAsync("REM-T-1")).Id;
        var baja = (await e.RemisionAsync("REM-B-1")).Id;
        var servicio = Crear(e);

        await ((Func<Task>)(() => servicio.ObtenerTrasladoAsync(99999))).Should().ThrowAsync<ValidationException>().WithMessage("*no existe*");
        await ((Func<Task>)(() => servicio.ObtenerTrasladoAsync(baja))).Should().ThrowAsync<ValidationException>().WithMessage("*no es un traslado*");

        await e.Kardex.AnularAsync(traslado);
        var anulado = await servicio.ObtenerTrasladoAsync(traslado);
        anulado.Anulada.Should().BeTrue();
        anulado.Animales.Should().BeEmpty("al anular se borran las líneas del documento");
    }

    // ---- Dashboard -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_dashboard_de_todas_las_granjas_resume_el_inventario_y_los_movimientos_del_mes()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();

        var d = await Crear(e).ObtenerDashboardAsync(null, null, new DateTime(2026, 10, 15));

        d.TotalAnimales.Should().Be(5);
        d.SaldoTotal.Should().Be(13_750_000m);
        d.NacimientosMes.Should().Be(1);
        d.SalidasMes.Should().Be(2, "una venta y una baja; el traslado no es una salida del inventario");
        d.PorGranja.Select(p => (p.Etiqueta, p.Valor)).Should().Equal(("Tesorito", 4m), ("La Cruz", 1m), ("Montelindo", 0m));
        d.MovimientosMes.Select(p => (p.Etiqueta, p.Valor)).Should().Equal(
            ("Nacimientos", 1m), ("Compras", 2m), ("Ventas", 1m), ("Bajas", 1m), ("Traslados", 1m));
    }

    [Fact]
    public async Task El_dashboard_de_una_granja_muestra_sus_animales_por_categoria()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();

        var d = await Crear(e).ObtenerDashboardAsync(e.Ids.Tesorito, null, new DateTime(2026, 10, 1));

        d.TotalAnimales.Should().Be(4);
        d.SaldoTotal.Should().Be(7_750_000m);
        // Se listan todas las categorías del programa (las vacías en cero) para que la gráfica nunca quede en blanco.
        d.PorCategoria.Select(p => (p.Etiqueta, p.Valor)).Should().Equal(
            ("Terneros", 3m), ("Levante", 0m), ("Novilla Vientre", 0m), ("Vacas General", 1m), ("Reproductor", 0m));
        d.NacimientosMes.Should().Be(1);
        d.SalidasMes.Should().Be(2);
    }

    [Fact]
    public async Task El_dashboard_muestra_Sin_clasificar_solo_si_hay_animales_en_esa_categoria()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");

        var sin = await Crear(e).ObtenerDashboardAsync(e.Ids.Tesorito, e.Ids.ProgramaGanaderia, new DateTime(2026, 11, 6));
        sin.PorCategoria.Select(p => p.Etiqueta).Should().NotContain(NombresBase.CategoriaSinClasificar);

        var animal = await e.AltaVacaAsync("S-1");
        await e.Db.EjecutarAsync(
            $"UPDATE ANIMAL SET categoria_id = {e.Ids.Categoria(NombresBase.CategoriaSinClasificar)} WHERE id = {animal.Id}");

        var con = await Crear(e).ObtenerDashboardAsync(e.Ids.Tesorito, e.Ids.ProgramaGanaderia, new DateTime(2026, 11, 6));
        con.PorCategoria.Should().Contain(p => p.Etiqueta == NombresBase.CategoriaSinClasificar && p.Valor == 1m);
    }

    [Fact]
    public async Task El_dashboard_de_un_programa_nuevo_sin_categorias_ni_animales_devuelve_puntos_en_cero()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        await e.Db.EjecutarAsync("INSERT INTO PROGRAMA (codigo, nombre, tipo_inventario) VALUES ('PRG-EQU', 'Caballar (Equinos)', 'INDIVIDUAL')");
        var equinos = await e.Db.EscalarAsync<int>("SELECT id FROM PROGRAMA WHERE codigo = 'PRG-EQU'");

        var todas = await Crear(e).ObtenerDashboardAsync(null, equinos, new DateTime(2026, 10, 15));
        var deTesorito = await Crear(e).ObtenerDashboardAsync(e.Ids.Tesorito, equinos, new DateTime(2026, 10, 15));

        todas.TotalAnimales.Should().Be(0);
        todas.PorGranja.Select(p => (p.Etiqueta, p.Valor)).Should().Equal(("Tesorito", 0m), ("La Cruz", 0m), ("Montelindo", 0m));
        todas.MovimientosMes.Should().HaveCount(5).And.OnlyContain(p => p.Valor == 0);
        deTesorito.PorCategoria.Select(p => (p.Etiqueta, p.Valor)).Should().Equal(("Sin categorías", 0m));
    }

    [Fact]
    public async Task El_dashboard_solo_cuenta_los_movimientos_del_mes_elegido()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();

        var noviembre = await Crear(e).ObtenerDashboardAsync(null, null, new DateTime(2026, 11, 20));

        noviembre.NacimientosMes.Should().Be(0);
        noviembre.SalidasMes.Should().Be(0);
        noviembre.MovimientosMes.Should().OnlyContain(p => p.Valor == 0);
        noviembre.TotalAnimales.Should().Be(5, "el inventario es el de hoy, no el del mes");
    }

    [Fact]
    public async Task El_dashboard_filtra_por_programa()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var aves = await e.Db.EscalarAsync<int>("SELECT id FROM PROGRAMA WHERE codigo = 'PRG-AVE'");

        var d = await Crear(e).ObtenerDashboardAsync(null, aves, new DateTime(2026, 10, 1));

        d.TotalAnimales.Should().Be(0);
        d.SaldoTotal.Should().Be(0m);
        d.NacimientosMes.Should().Be(0);
    }
}
