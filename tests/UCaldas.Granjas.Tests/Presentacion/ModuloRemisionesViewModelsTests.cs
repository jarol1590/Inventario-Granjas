using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.App.ViewModels.Remisiones;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Comercial;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Services.Reportes;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>Doble de <see cref="IFormulariosRemisiones"/>: responde al recibo con lo que la prueba indique y registra las ventanas pedidas.</summary>
public sealed class FormulariosRemisionesFalso : IFormulariosRemisiones
{
    public List<string> Llamadas { get; } = [];

    public ReciboCajaDto? ReciboAEntregar { get; set; }

    public ReciboContexto? UltimoContexto { get; private set; }

    public bool Resultado { get; set; } = true;

    public static ReciboCajaDto Pago(DateTime fecha, decimal descuento = 0m) => new()
    {
        Fecha = fecha, FormaPago = FormaPago.Consignacion, BancoConsignacion = "DAVIVIENDA", NumeroCuenta = "123456", TipoCuenta = "Ahorros", DescuentoPorcentaje = descuento,
    };

    /// <summary>Doble cuyo recibo se acepta con una consignación fechada <paramref name="fecha"/>.</summary>
    public static FormulariosRemisionesFalso ConPago(DateTime fecha) => new() { ReciboAEntregar = Pago(fecha) };

    public Task<bool> NuevaRemisionAsync(int? oficioId = null) => Registrar($"remision:nueva:{oficioId}");

    public Task<bool> AbrirRemisionAsync(int remisionId) => Registrar($"remision:{remisionId}");

    public Task<(ReciboCajaDto Recibo, DatosComercialesDto? Comprador)?> CapturarReciboAsync(ReciboContexto contexto)
    {
        UltimoContexto = contexto;
        (ReciboCajaDto, DatosComercialesDto?)? r = ReciboAEntregar is null ? null : (ReciboAEntregar, null);
        return Task.FromResult(r);
    }

    public Task<bool> EditarReciboAsync(int remisionId) => Registrar($"recibo:{remisionId}");

    public Task<bool> NuevoOficioAsync(int? programaId = null) => Registrar("oficio:nuevo");

    public Task<bool> EditarOficioAsync(OficioResumen oficio) => Registrar($"oficio:{oficio.Oficio.Id}");

    public Task<bool> EditarConvenioAsync(Convenio? existente) => Registrar("convenio");

    public Task<bool> EditarProductoAsync(CatalogoProducto? existente, int? programaId) => Registrar("producto");

    public Task<Cliente?> NuevoClienteAsync() => Task.FromResult<Cliente?>(null);

    private Task<bool> Registrar(string llamada)
    {
        Llamadas.Add(llamada);
        return Task.FromResult(Resultado);
    }
}

public class ModuloRemisionesViewModelsTests
{
    private sealed class Entorno : IAsyncDisposable
    {
        public required EscenarioKardex E { get; init; }
        public required Convenio Convenio { get; init; }
        public required int Aves { get; init; }
        public required int Producto { get; init; }
        public DialogoFalso Dialogos { get; } = new();
        public FormulariosRemisionesFalso Formularios { get; } = new();
        public FormulariosFalso FormulariosAnimales { get; } = new();
        public ContextoGlobal Contexto { get; } = new();

        public ValueTask DisposeAsync() => E.DisposeAsync();

        public OficioService Oficios => new(E.Db.Uow, E.Reloj);

        public RemisionesQueryService Consultas => new(E.Db.Uow);

        public ExcelReportingEngine Motor => new(E.Db.Uow, new KardexReportBuilder(E.Db.Uow), new FichaService(E.Db.Uow, E.Reloj), E.Reloj);

        public RemisionEditorViewModel Editor() => new(
            E.Kardex, Consultas, Oficios, new CatalogoService(E.Db.Uow), new CatalogoComercialService(E.Db.Uow),
            Formularios, Motor, Contexto, E.Reloj, Dialogos);

        public static ReciboCajaDto Pago(decimal descuento = 0m) => new()
        {
            Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.Consignacion, BancoConsignacion = "DAVIVIENDA", NumeroCuenta = "123456", TipoCuenta = "Ahorros", DescuentoPorcentaje = descuento,
        };
    }

    private static async Task<Entorno> CrearAsync()
    {
        var e = await EscenarioKardex.CrearAsync("2026-11-06");
        var catalogo = new CatalogoComercialService(e.Db.Uow);
        var convenio = await catalogo.GuardarConvenioAsync(new Convenio { Nombre = "Convenio Frigocaldas", Nit = "900.1", Direccion = "x", Telefono = "1" });
        int aves;
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            aves = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!.Id;
        }

        var producto = await catalogo.GuardarProductoAsync(new CatalogoProducto { Codigo = "HUE-30", ProgramaId = aves, Nombre = "Huevo cubeta x30", UnidadMedida = "Cubeta", PrecioUnitario = 18_000m });
        await new OficioService(e.Db.Uow, e.Reloj).CrearAsync(new OficioDto { NumeroOficio = "OF-HUE", ProgramaId = aves, Fecha = Datos.F("2026-10-15") });
        await e.AltaVacaAsync("S-1", valor: 5_000_000m);
        return new Entorno { E = e, Convenio = convenio, Aves = aves, Producto = producto.Id };
    }

    // ---- Oficios: cerrar y abrir ---------------------------------------------------------------------------------------------

    private static OficiosViewModel Oficios(Entorno s)
        => new(s.Oficios, s.Consultas, s.Formularios, s.Contexto, s.Dialogos);

    [Fact]
    public async Task Oficios_lista_los_oficios_y_permite_cerrar_y_abrir_con_confirmacion()
    {
        await using var s = await CrearAsync();
        var vm = Oficios(s);
        await vm.CargarAsync();
        vm.Oficios.Should().ContainSingle();
        (vm.Seleccionado!.Oficio.NumeroOficio, vm.EstaCerrado, vm.TextoCerrarAbrir).Should().Be(("OF-HUE", false, "🔒  CERRAR OFICIO"));
        vm.AgregarRemisionCommand.CanExecute(null).Should().BeTrue();

        s.Dialogos.RespuestaConfirmar = false;
        await vm.CerrarOAbrirCommand.ExecuteAsync(null);
        vm.EstaCerrado.Should().BeFalse("el usuario no confirmó");

        s.Dialogos.RespuestaConfirmar = true;
        await vm.CerrarOAbrirCommand.ExecuteAsync(null);
        (vm.EstaCerrado, vm.TextoCerrarAbrir).Should().Be((true, "🔓  ABRIR OFICIO"));
        vm.AgregarRemisionCommand.CanExecute(null).Should().BeFalse("un oficio cerrado no admite remisiones nuevas");
        vm.TituloDetalle.Should().Contain("OFICIO CERRADO");

        await vm.CerrarOAbrirCommand.ExecuteAsync(null);
        (vm.EstaCerrado, vm.AgregarRemisionCommand.CanExecute(null)).Should().Be((false, true));
    }

    [Fact]
    public async Task Oficios_abre_la_nueva_remision_del_oficio_y_recarga_si_se_guardo()
    {
        await using var s = await CrearAsync();
        var vm = Oficios(s);
        await vm.CargarAsync();

        await vm.AgregarRemisionCommand.ExecuteAsync(null);
        await vm.NuevoOficioCommand.ExecuteAsync(null);
        await vm.EditarOficioCommand.ExecuteAsync(null);

        s.Formularios.Llamadas.Should().Equal($"remision:nueva:{vm.Seleccionado!.Oficio.Id}", "oficio:nuevo", $"oficio:{vm.Seleccionado.Oficio.Id}");
    }

    [Fact]
    public async Task Nuevo_oficio_propone_el_siguiente_numero_y_lo_guarda()
    {
        await using var s = await CrearAsync();
        var vm = new NuevoOficioViewModel(s.Oficios, new CatalogoService(s.E.Db.Uow), s.E.Reloj, s.Dialogos);
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;

        (await vm.InicializarAsync(s.Aves)).Should().BeTrue();
        var yy = s.E.Reloj.Ahora.ToString("yy");
        vm.NumeroOficio.Should().Be($"OFAVI-{yy}001", "ningún oficio existente sigue el formato nuevo");
        vm.ProgramaSeleccionado!.Id.Should().Be(s.Aves);

        // Con otro programa cambia el prefijo y se vuelve a proponer.
        vm.ProgramaSeleccionado = vm.Programas.First(p => p.Id != s.Aves);
        await Task.Delay(200);
        vm.NumeroOficio.Should().StartWith("OF").And.EndWith($"-{yy}001").And.NotStartWith("OFAVI");

        vm.NumeroOficio = "OF-200";
        await vm.GuardarCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        (await s.Oficios.ListarAsync()).Select(o => o.Oficio.NumeroOficio).Should().Contain("OF-200");
    }

    [Fact]
    public async Task Nuevo_oficio_muestra_el_error_de_negocio_sin_cerrar()
    {
        await using var s = await CrearAsync();
        var vm = new NuevoOficioViewModel(s.Oficios, new CatalogoService(s.E.Db.Uow), s.E.Reloj, s.Dialogos);
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;
        await vm.InicializarAsync(s.Aves);
        vm.NumeroOficio = "of-hue";

        await vm.GuardarCommand.ExecuteAsync(null);

        s.Dialogos.UltimaAdvertencia.Should().Contain("Ya existe");
        cierres.Should().BeEmpty();
    }

    // ---- Editor de remisión ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task La_nueva_remision_arranca_con_el_siguiente_consecutivo_y_el_programa_del_oficio()
    {
        await using var s = await CrearAsync();
        var vm = s.Editor();

        (await vm.InicializarNuevaAsync()).Should().BeTrue();

        (vm.Modo, vm.NumeroRemision, vm.OficioSeleccionado!.Oficio.NumeroOficio, vm.ProgramaId).Should().Be((ModoDocumento.Nuevo, "REM-1", "OF-HUE", s.Aves));
        vm.ProgramaNombre.Should().Contain("Avicultura", "el programa es el del oficio");
        vm.ProductosDisponibles.Should().ContainSingle("las sugerencias son el catálogo del programa");
        vm.Lineas.Should().ContainSingle("arranca con una fila vacía para escribir");
    }

    [Fact]
    public async Task Una_linea_del_catalogo_propone_unidad_y_precio_y_se_guarda_con_su_recibo()
    {
        await using var s = await CrearAsync();
        var vm = s.Editor();
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;
        await vm.InicializarNuevaAsync();
        vm.GranjaOrigen = vm.Granjas.Single(g => g.Id == s.E.Ids.Montelindo);

        var linea = vm.Lineas.Single();
        linea.Producto = vm.ProductosDisponibles.Single();
        (linea.Descripcion, linea.Unidad, linea.ValorUnitario).Should().Be(("Huevo cubeta x30", "Cubeta", 18_000m));
        linea.Cantidad = 10m;
        vm.TotalTexto.Should().Be("Total remisión: $180.000");
        s.Formularios.ReciboAEntregar = Entorno.Pago(10m);

        await vm.ContinuarAReciboCommand.ExecuteAsync(null);

        s.Formularios.UltimoContexto.Should().Match<ReciboContexto>(c => c.EsVenta && c.NumeroRecibo == "829 SP" && c.Subtotal == 180_000m && c.Comprador == "Convenio Frigocaldas");
        cierres.Should().Equal(true);
        s.Dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("REM-1").And.Contain("829 SP");
        var fila = (await s.Consultas.ListarAsync(new RemisionFiltro())).Single();
        (fila.Numero, fila.Clase, fila.Total, fila.Recibo, fila.Estado).Should().Be(("REM-1", "PRODUCTOS", 162_000m, "829 SP", EstadoRemision.Pagada));
    }

    [Fact]
    public async Task Una_linea_escrita_a_mano_se_guarda_como_item_libre_sin_producto_de_catalogo()
    {
        await using var s = await CrearAsync();
        var vm = s.Editor();
        await vm.InicializarNuevaAsync();
        vm.GranjaOrigen = vm.Granjas.Single(g => g.Id == s.E.Ids.Montelindo);
        var linea = vm.Lineas.Single();
        linea.Descripcion = "Gallinaza seca";
        linea.Unidad = "Bulto";
        linea.Cantidad = 20m;
        linea.ValorUnitario = 7_500m;
        vm.AgregarLineaCommand.Execute(null);   // una fila en blanco no cuenta al guardar
        vm.Lineas.Should().HaveCount(2);
        s.Formularios.ReciboAEntregar = Entorno.Pago();

        await vm.ContinuarAReciboCommand.ExecuteAsync(null);

        var vista = await s.Consultas.ObtenerAsync((await s.Consultas.ListarAsync(new RemisionFiltro())).Single().Id);
        vista.Clase.Should().Be("PRODUCTOS");
        vista.Lineas.Should().ContainSingle().Which.Should().Match<LineaRemision>(l => l.ProductoId == null && l.Descripcion == "Gallinaza seca" && l.Unidad == "Bulto" && l.ValorTotal == 150_000m);
        vista.Recibo!.TotalPagado.Should().Be(150_000m);
    }

    [Fact]
    public async Task Cancelar_el_recibo_no_guarda_nada()
    {
        await using var s = await CrearAsync();
        var vm = s.Editor();
        await vm.InicializarNuevaAsync();
        vm.GranjaOrigen = vm.Granjas.Single(g => g.Id == s.E.Ids.Montelindo);
        vm.Lineas.Single().Producto = vm.ProductosDisponibles.Single();
        s.Formularios.ReciboAEntregar = null;

        await vm.ContinuarAReciboCommand.ExecuteAsync(null);

        (await s.E.ContarAsync("REMISION")).Should().Be(0);
        (await s.E.ContarAsync("RECIBO_CAJA")).Should().Be(0);
    }

    [Fact]
    public async Task Sin_lineas_o_sin_comprador_la_remision_no_continua()
    {
        await using var s = await CrearAsync();
        var vm = s.Editor();
        await vm.InicializarNuevaAsync();
        vm.GranjaOrigen = vm.Granjas.Single(g => g.Id == s.E.Ids.Montelindo);

        await vm.ContinuarAReciboCommand.ExecuteAsync(null);
        s.Dialogos.UltimaAdvertencia.Should().Contain("al menos un ítem", "la fila en blanco no cuenta");

        vm.Lineas.Single().Producto = vm.ProductosDisponibles.Single();
        vm.Convenio = null;
        await vm.ContinuarAReciboCommand.ExecuteAsync(null);
        s.Dialogos.UltimaAdvertencia.Should().Contain("convenio");
        s.Formularios.UltimoContexto.Should().BeNull("no se llegó al recibo");
    }

    [Fact]
    public async Task Una_remision_guardada_se_abre_en_detalle_se_edita_y_se_anula()
    {
        await using var s = await CrearAsync();
        var r = await RegistrarVentaDeHuevosAsync(s);

        var vm = s.Editor();
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;
        (await vm.InicializarExistenteAsync(r.RemisionId)).Should().BeTrue();

        (vm.Modo, vm.EsLectura, vm.PuedeEditarODesechar, vm.NumeroRemision, vm.Lineas.Count).Should().Be((ModoDocumento.Detalle, true, true, "REM-9", 1));
        vm.ReciboTexto.Should().Contain("829 SP");
        vm.EstadoTexto.Should().Be(EstadoRemision.Pagada);

        vm.EditarCommand.Execute(null);
        (vm.Modo, vm.EsEditable).Should().Be((ModoDocumento.Edicion, true));
        vm.Lineas.Single().Cantidad = 20m;
        s.Formularios.ReciboAEntregar = Entorno.Pago();
        await vm.ContinuarAReciboCommand.ExecuteAsync(null);
        (await s.Consultas.ObtenerAsync(r.RemisionId)).Should().Match<RemisionVista>(v => v.Totales.Total == 360_000m && v.Recibo!.NumeroRecibo == 829);
        cierres.Should().Equal(true);

        var anulador = s.Editor();
        var cierresAnulador = new List<bool>();
        anulador.CierreSolicitado += cierresAnulador.Add;
        await anulador.InicializarExistenteAsync(r.RemisionId);
        s.Dialogos.RespuestaPedir = "error de digitación";
        await anulador.AnularCommand.ExecuteAsync(null);
        cierresAnulador.Should().Equal(true);
        (await s.Consultas.ObtenerAsync(r.RemisionId)).Estado.Should().Be(EstadoRemision.Anulada);
    }

    [Fact]
    public async Task Imprimir_la_remision_y_el_recibo_genera_los_excel_y_los_abre()
    {
        await using var s = await CrearAsync();
        var r = await RegistrarVentaDeHuevosAsync(s);
        var vm = s.Editor();
        await vm.InicializarExistenteAsync(r.RemisionId);
        var carpeta = Path.Combine(s.E.Db.Carpeta, "excel");
        s.Dialogos.RutaAGuardar = Path.Combine(carpeta, "remision.xlsx");
        await vm.ImprimirRemisionCommand.ExecuteAsync(null);
        s.Dialogos.RutaAGuardar = Path.Combine(carpeta, "recibo.xlsx");
        await vm.ImprimirReciboCommand.ExecuteAsync(null);

        s.Dialogos.NombresSugeridos.Should().Equal("Remision_REM-9.xlsx", "Recibo_829_SP.xlsx");
        s.Dialogos.Abiertos.Should().HaveCount(2);
        File.Exists(Path.Combine(carpeta, "remision.xlsx")).Should().BeTrue();
        File.Exists(Path.Combine(carpeta, "recibo.xlsx")).Should().BeTrue();
    }

    private static async Task<ProductosResultado> RegistrarVentaDeHuevosAsync(Entorno s) => await s.E.Kardex.RegistrarRemisionProductosAsync(new RemisionProductosDto
    {
        Tipo = TipoRemision.Venta, ProgramaId = s.Aves, NumeroOficio = "OF-HUE", NumeroRemision = "REM-9", Fecha = Datos.F("2026-11-05"),
        GranjaOrigenId = s.E.Ids.Montelindo, Comprador = s.Convenio.Nombre, Comercial = new DatosComercialesDto { ConvenioId = s.Convenio.Id },
        Recibo = Entorno.Pago(),
        Lineas = [new LineaProductoDto { ProductoId = s.Producto, UnidadMedida = "Cubeta", Cantidad = 10m, ValorUnitario = 18_000m }],
    });

    // ---- Bandeja, recibos y recibo de caja ----------------------------------------------------------------------------------------

    [Fact]
    public async Task La_bandeja_de_remisiones_muestra_las_filas_los_KPI_y_abre_el_detalle_y_el_recibo()
    {
        await using var s = await CrearAsync();
        await RegistrarVentaDeHuevosAsync(s);
        s.Contexto.ProgramaId = s.Aves;
        var vm = new RemisionesViewModel(s.Consultas, s.Formularios, s.Motor, s.Contexto, s.E.Reloj, s.Dialogos);
        vm.Rango.MesDesde = FiltroMensual.Meses[10];

        await vm.CargarAsync();

        vm.Remisiones.Should().ContainSingle().Which.Should().Match<RemisionFila>(f => f.Numero == "REM-9" && f.Estado == EstadoRemision.Pagada);
        (vm.RemisionesDelMes, vm.VentasDelMesTexto).Should().Be((1, "$180.000"));
        vm.VerDetalleCommand.CanExecute(null).Should().BeFalse("no hay selección");

        vm.Seleccionada = vm.Remisiones[0];
        await vm.VerDetalleCommand.ExecuteAsync(null);
        await vm.GenerarReciboCommand.ExecuteAsync(null);
        await vm.NuevaRemisionCommand.ExecuteAsync(null);

        s.Formularios.Llamadas.Should().Equal($"remision:{vm.Seleccionada.Id}", $"recibo:{vm.Seleccionada.Id}", "remision:nueva:");

        vm.Estado = vm.Estados.Single(e => e.Valor == EstadoRemision.Anulada);
        await Esperar.HastaAsync(() => vm.Remisiones.Count == 0);
    }

    [Fact]
    public async Task La_bandeja_muestra_tambien_las_ventas_de_animales_de_Sistema_Granjas_y_filtra_por_clase()
    {
        await using var s = await CrearAsync();
        await RegistrarVentaDeHuevosAsync(s);
        var venta = await s.E.VentaAsync("REM-A", "OF-A", "RC-5", null, ("S-1", 5_500_000m));
        venta.FechaRemision = Datos.F("2026-11-04");
        venta.FechaOficio = Datos.F("2026-11-04");
        await s.E.Kardex.RegistrarVentaAsync(venta);
        var vm = new RemisionesViewModel(s.Consultas, s.Formularios, s.Motor, s.Contexto, s.E.Reloj, s.Dialogos);
        vm.Rango.MesDesde = FiltroMensual.Meses[10];

        await vm.CargarAsync();

        vm.Clases.Select(c => c.Nombre).Should().Equal("Todas", "Subproductos", "Animales", "Lotes");
        vm.Remisiones.Should().HaveCount(2);
        vm.Remisiones.Single(f => f.Numero == "REM-A").Should().Match<RemisionFila>(f => f.ClaseTexto == "Animales" && f.Recibo == "830 SP" && f.EsDelModulo);

        vm.Clase = vm.Clases.Single(c => c.Valor == "ANIMALES");
        await Esperar.HastaAsync(() => vm.Remisiones.Count == 1);
        vm.Remisiones.Single().Numero.Should().Be("REM-A");

        vm.Clase = vm.Clases.Single(c => c.Valor == "PRODUCTOS");
        await Esperar.HastaAsync(() => vm.Remisiones.Count == 1 && vm.Remisiones[0].Numero == "REM-9");

        (vm.RemisionesDelMes, vm.VentasDelMesTexto).Should().Be((2, "$5.680.000"), "el KPI suma las dos ventas: 180.000 + 5.500.000");
    }

    [Fact]
    public async Task El_editor_del_modulo_rechaza_una_venta_de_animales_que_se_edita_en_Sistema_Granjas()
    {
        await using var s = await CrearAsync();
        var venta = await s.E.VentaAsync("REM-A", "OF-A", "RC-5", null, ("S-1", 5_500_000m));
        var r = await s.E.Kardex.RegistrarVentaAsync(venta);
        var vm = s.Editor();

        (await vm.InicializarExistenteAsync(r.RemisionId)).Should().BeFalse();

        s.Dialogos.UltimaAdvertencia.Should().Contain("Sistema Granjas");
    }

    [Fact]
    public async Task Recibos_lista_los_emitidos_resume_el_recaudo_y_los_edita_o_reimprime()
    {
        await using var s = await CrearAsync();
        var r = await RegistrarVentaDeHuevosAsync(s);
        var vm = new RecibosCajaViewModel(s.Consultas, s.Formularios, s.Motor, new NavegadorFalso(), s.Dialogos);

        await vm.CargarAsync();

        var fila = vm.Recibos.Should().ContainSingle().Subject;
        (fila.Etiqueta, fila.RemisionNumero, vm.ConsignacionTexto, vm.UltimoTexto).Should().Be(("829 SP", "REM-9", "$180.000", "829 SP"));

        vm.Seleccionado = fila;
        await vm.AbrirReciboCommand.ExecuteAsync(null);
        s.Formularios.Llamadas.Should().Equal($"recibo:{r.RemisionId}");

        s.Dialogos.RutaAGuardar = Path.Combine(s.E.Db.Carpeta, "excel", "recibo.xlsx");
        await vm.ReimprimirCommand.ExecuteAsync(null);
        s.Dialogos.NombresSugeridos.Should().Equal("Recibo_829_SP.xlsx");
        File.Exists(s.Dialogos.RutaAGuardar).Should().BeTrue();
    }

    [Fact]
    public async Task El_recibo_calcula_descuento_total_y_letras_y_valida_la_fecha()
    {
        await using var s = await CrearAsync();
        var contexto = new ReciboContexto(true, "829 SP", "REM-1", "Avicultura", "Convenio", null, [], 420_000m, 0m);
        var vm = new ReciboCajaViewModel(new ReciboCajaService(s.E.Db.Uow, s.E.Reloj), new CatalogoComercialService(s.E.Db.Uow), s.E.Reloj, s.Dialogos, contexto);
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;
        (await vm.InicializarAsync()).Should().BeTrue();
        vm.Banco.Should().Contain("DAVIVIENDA");
        vm.NumeroCuenta.Should().NotBeNullOrWhiteSpace();
        vm.TiposCuenta.Should().Equal("Ahorros", "Corriente");
        vm.TipoCuenta.Should().Be("Ahorros");

        (vm.Total, vm.TotalLetras).Should().Be((420_000m, "CUATROCIENTOS VEINTE MIL PESOS M/CTE"));
        vm.Descuento = 10m;
        (vm.ValorDescuento, vm.Total, vm.TotalTexto).Should().Be((42_000m, 378_000m, "$378.000"));

        vm.Fecha = Datos.F("2026-12-31");
        await vm.AceptarCommand.ExecuteAsync(null);
        s.Dialogos.UltimaAdvertencia.Should().Contain("futura");
        cierres.Should().BeEmpty();

        vm.Fecha = Datos.F("2026-11-06");
        await vm.AceptarCommand.ExecuteAsync(null);
        cierres.Should().Equal(true);
        vm.Resultado.Should().Match<ReciboCajaDto>(r => r.FormaPago == FormaPago.Consignacion && r.DescuentoPorcentaje == 10m && r.TipoCuenta == "Ahorros" && !string.IsNullOrEmpty(r.NumeroCuenta));
    }

    [Fact]
    public async Task El_recibo_de_un_traslado_es_interno_y_su_valor_es_editable()
    {
        await using var s = await CrearAsync();
        var contexto = new ReciboContexto(false, "829 SP", "REM-T1", "Ganadería", "Traslado a Tesorito", null, [], 0m, 5_000_000m);
        var vm = new ReciboCajaViewModel(new ReciboCajaService(s.E.Db.Uow, s.E.Reloj), new CatalogoComercialService(s.E.Db.Uow), s.E.Reloj, s.Dialogos, contexto);
        await vm.InicializarAsync();

        (vm.EsTraslado, vm.Total).Should().Be((true, 5_000_000m));
        vm.TotalTraslado = 0m;
        await vm.AceptarCommand.ExecuteAsync(null);

        vm.Resultado.Should().Match<ReciboCajaDto>(r => r.FormaPago == FormaPago.TrasladoInterno && r.TotalPagado == 0m);
    }

    // ---- Informes y configuración ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_informe_de_ventas_se_genera_en_Excel()
    {
        await using var s = await CrearAsync();
        await RegistrarVentaDeHuevosAsync(s);
        var motor = new ExcelReportingEngine(s.E.Db.Uow, new KardexReportBuilder(s.E.Db.Uow), new FichaService(s.E.Db.Uow, s.E.Reloj), s.E.Reloj);
        s.Dialogos.RespuestaConfirmar = false;
        var vm = new InformesRemisionesViewModel(
            motor, new CatalogoService(s.E.Db.Uow), s.Contexto, s.E.Reloj, s.Dialogos);
        await vm.CargarAsync();
        vm.Programas.Select(p => p.Nombre).Should().StartWith("Todos los programas");
        vm.Desde = Datos.F("2026-10-01");
        vm.Hasta = Datos.F("2026-11-30");

        var excel = Path.Combine(s.E.Db.Carpeta, "informes", "ventas.xlsx");
        s.Dialogos.RutaAGuardar = excel;
        await vm.GenerarCommand.ExecuteAsync(null);

        File.Exists(excel).Should().BeTrue();
        s.Dialogos.NombresSugeridos.Should().Equal("Ventas_por_Programas_20261001_20261130.xlsx");
        vm.UltimoArchivo.Should().Be(excel);
    }

    [Fact]
    public async Task El_informe_de_ventas_empieza_en_todos_los_programas_aunque_el_combo_global_tenga_uno()
    {
        await using var s = await CrearAsync();
        s.Contexto.ProgramaId = s.Aves;
        var vm = new InformesRemisionesViewModel(s.Motor, new CatalogoService(s.E.Db.Uow), s.Contexto, s.E.Reloj, s.Dialogos);

        await vm.CargarAsync();

        vm.Programa!.Id.Should().BeNull();
        vm.Programa.Nombre.Should().Be("Todos los programas");
    }

    [Fact]
    public async Task El_informe_de_ventas_exige_el_rango_de_fechas()
    {
        await using var s = await CrearAsync();
        var motor = new ExcelReportingEngine(s.E.Db.Uow, new KardexReportBuilder(s.E.Db.Uow), new FichaService(s.E.Db.Uow, s.E.Reloj), s.E.Reloj);
        var vm = new InformesRemisionesViewModel(
            motor, new CatalogoService(s.E.Db.Uow), s.Contexto, s.E.Reloj, s.Dialogos) { Desde = null };

        await vm.GenerarCommand.ExecuteAsync(null);

        s.Dialogos.UltimaAdvertencia.Should().Contain("rango de fechas");
    }

    [Fact]
    public async Task La_configuracion_lista_convenios_y_productos_cambia_su_estado_y_guarda_los_datos_institucionales()
    {
        await using var s = await CrearAsync();
        var comercial = new CatalogoComercialService(s.E.Db.Uow);

        var convenios = new ConveniosTabViewModel(comercial, s.Formularios, s.Dialogos);
        await convenios.CargarAsync();
        convenios.Convenios.Should().ContainSingle();
        convenios.Seleccionado = convenios.Convenios[0];
        await convenios.CambiarEstadoCommand.ExecuteAsync(null);
        convenios.Convenios.Single().Activo.Should().BeFalse();
        convenios.TextoEstado.Should().Be("Activar");
        await convenios.AgregarCommand.ExecuteAsync(null);
        s.Formularios.Llamadas.Should().Contain("convenio");

        var productos = new ProductosTabViewModel(comercial, new CatalogoService(s.E.Db.Uow), s.Formularios, s.Dialogos);
        await productos.CargarAsync();
        await Esperar.HastaAsync(() => productos.Productos.Count == 1);
        productos.Productos[0].Programa.Should().Be("Avicultura (Aves)");

        var datos = new DatosInstitucionalesViewModel(comercial, s.Dialogos);
        await datos.CargarAsync();
        datos.UltimoConsecutivo.Should().Be(828);
        datos.JefeTesoreria = "Luz Marina Gómez";
        datos.UltimoConsecutivo = 900;
        await datos.GuardarCommand.ExecuteAsync(null);
        (await comercial.GetConfiguracionAsync()).Should().Match<ConfiguracionRemisiones>(c => c.JefeTesoreria == "Luz Marina Gómez" && c.UltimoConsecutivoRecibo == 900);
    }

    [Fact]
    public async Task Programas_lleva_a_la_administracion_de_programas_de_Configuracion()
    {
        await using var s = await CrearAsync();
        var navegador = new NavegadorFalso();
        var vm = new ProgramasTabViewModel(new CatalogoService(s.E.Db.Uow), navegador, s.Dialogos);

        await vm.CargarAsync();
        vm.AdministrarProgramasCommand.Execute(null);

        vm.Programas.Select(p => p.Programa.Codigo).Should().Contain(["PRG-GAN", "PRG-AVE"]);
        navegador.Destinos.Should().Equal(ShellViewModel.ClaveConfiguracion);
    }
}
