using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Comercial;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Comercial;

/// <summary>Módulo Remisiones: oficios cerrables, recibo de caja SP atómico y editable, convenios y remisiones de productos.</summary>
public class ModuloRemisionesTests
{
    private sealed class Entorno : IAsyncDisposable
    {
        public required EscenarioKardex E { get; init; }
        public required CatalogoComercialService Catalogo { get; init; }
        public required OficioService Oficios { get; init; }
        public required RemisionesQueryService Consultas { get; init; }
        public required ReciboCajaService Recibos { get; init; }
        public required Convenio Convenio { get; init; }
        public required int ProgramaAves { get; init; }

        public ValueTask DisposeAsync() => E.DisposeAsync();

        public DatosComercialesDto Comprador => new() { TipoComprador = TipoComprador.Convenio, ConvenioId = Convenio.Id };

        public static ReciboCajaDto Pago(decimal descuento = 0m) => new()
        {
            Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.Consignacion, BancoConsignacion = "DAVIVIENDA", DescuentoPorcentaje = descuento,
        };

        public async Task<VentaDto> VentaConReciboAsync(string remision, string oficio, params (string Chapeta, decimal Precio)[] animales)
        {
            var dto = await E.VentaAsync(remision, string.Empty, string.Empty, null, animales);
            dto.NumeroOficio = oficio;
            dto.Comprador = Convenio.Nombre;
            dto.ReciboCaja = string.Empty;
            dto.Comercial = Comprador;
            dto.Recibo = Pago();
            return dto;
        }

        public async Task<ReciboCaja?> ReciboDeAsync(int remisionId)
        {
            await using var uow = await E.Db.Uow.OpenReadOnlyAsync();
            return await uow.Recibos.GetByRemisionAsync(remisionId);
        }

        public async Task<Remision> RemisionAsync(int id)
        {
            await using var uow = await E.Db.Uow.OpenReadOnlyAsync();
            return (await uow.Remisiones.GetByIdAsync(id))!;
        }

        public async Task<int> CrearOficioAsync(string numero, int? programa = null)
            => await Oficios.CrearAsync(new OficioDto { NumeroOficio = numero, ProgramaId = programa ?? E.Ids.ProgramaGanaderia, Fecha = Datos.F("2026-10-15") });
    }

    private static async Task<Entorno> CrearAsync()
    {
        var e = await EscenarioKardex.CrearAsync("2026-11-06");
        var catalogo = new CatalogoComercialService(e.Db.Uow);
        var convenio = await catalogo.GuardarConvenioAsync(new Convenio
        {
            Nombre = "Convenio Frigocaldas", Nit = "900.123.456-1", Direccion = "Km 5 vía al Magdalena", Telefono = "8781586", CorreoElectronico = "convenio@frigo.co",
        });
        int aves;
        await using (var uow = await e.Db.Uow.OpenReadOnlyAsync())
        {
            aves = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!.Id;
        }

        await e.AltaVacaAsync("S-1", valor: 5_000_000m);
        await e.AltaVacaAsync("S-2", valor: 4_000_000m);
        return new Entorno
        {
            E = e, Catalogo = catalogo, Oficios = new OficioService(e.Db.Uow, e.Reloj), Consultas = new RemisionesQueryService(e.Db.Uow),
            Recibos = new ReciboCajaService(e.Db.Uow, e.Reloj), Convenio = convenio, ProgramaAves = aves,
        };
    }

    // ---- Número en letras -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "CERO PESOS M/CTE")]
    [InlineData(1, "UN PESO M/CTE")]
    [InlineData(21, "VEINTIÚN PESOS M/CTE")]
    [InlineData(100, "CIEN PESOS M/CTE")]
    [InlineData(420_000, "CUATROCIENTOS VEINTE MIL PESOS M/CTE")]
    [InlineData(1_000_000, "UN MILLÓN DE PESOS M/CTE")]
    [InlineData(2_500_000, "DOS MILLONES QUINIENTOS MIL PESOS M/CTE")]
    [InlineData(31_000, "TREINTA Y UN MIL PESOS M/CTE")]
    [InlineData(5_500_000, "CINCO MILLONES QUINIENTOS MIL PESOS M/CTE")]
    [InlineData(1_001_001, "UN MILLÓN MIL UN PESOS M/CTE")]
    public void El_total_se_convierte_a_letras_en_espanol(int valor, string esperado)
        => NumeroALetras.Pesos(valor).Should().Be(esperado);

    [Fact]
    public void Los_centavos_se_escriben_como_fraccion()
        => NumeroALetras.Pesos(1_500.50m).Should().Be("UN MIL QUINIENTOS PESOS CON 50/100 M/CTE".Replace("UN MIL", "MIL"));

    // ---- Convenios y clientes ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Los_convenios_se_crean_editan_e_inactivan_y_el_NIT_no_se_repite()
    {
        await using var s = await CrearAsync();

        var otro = () => s.Catalogo.GuardarConvenioAsync(new Convenio { Nombre = "Otro", Nit = "900.123.456-1", Direccion = "x", Telefono = "1" });
        await otro.Should().ThrowAsync<ValidationException>().WithMessage("*NIT*");

        s.Convenio.Direccion = "Nueva dirección";
        var editado = await s.Catalogo.GuardarConvenioAsync(s.Convenio);
        editado.Id.Should().Be(s.Convenio.Id);
        await s.Catalogo.CambiarEstadoConvenioAsync(s.Convenio.Id, false);

        (await s.Catalogo.ListarConveniosAsync(soloActivos: true)).Should().BeEmpty();
        (await s.Catalogo.ListarConveniosAsync()).Single().Direccion.Should().Be("Nueva dirección");
    }

    [Theory]
    [InlineData("nombre")]
    [InlineData("nit")]
    [InlineData("direccion")]
    [InlineData("telefono")]
    [InlineData("correo")]
    public async Task Un_convenio_exige_sus_datos_y_un_correo_valido(string campo)
    {
        await using var s = await CrearAsync();
        var c = new Convenio { Nombre = "Convenio B", Nit = "800.5", Direccion = "Calle 1", Telefono = "123" };
        if (campo == "nombre") c.Nombre = "";
        if (campo == "nit") c.Nit = " ";
        if (campo == "direccion") c.Direccion = "";
        if (campo == "telefono") c.Telefono = "";
        if (campo == "correo") c.CorreoElectronico = "sin-arroba";

        await ((Func<Task>)(() => s.Catalogo.GuardarConvenioAsync(c))).Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Los_telefonos_de_convenio_cliente_y_datos_institucionales_solo_aceptan_mas_y_numeros()
    {
        await using var s = await CrearAsync();
        var convenio = new Convenio { Nombre = "Convenio C", Nit = "800.9", Direccion = "Calle 1", Telefono = "300-123" };
        await ((Func<Task>)(() => s.Catalogo.GuardarConvenioAsync(convenio))).Should().ThrowAsync<ValidationException>().WithMessage("*formato no válido*");
        convenio.Telefono = "+573001234567";
        await s.Catalogo.GuardarConvenioAsync(convenio);

        await ((Func<Task>)(() => s.Catalogo.GuardarClienteAsync(new Cliente { NombreRazonSocial = "X", NitCc = "1", Telefono = "(6) 12" })))
            .Should().ThrowAsync<ValidationException>().WithMessage("*formato no válido*");
    }

    [Fact]
    public async Task Los_clientes_y_productos_validan_sus_datos_y_no_repiten_codigo_ni_NIT()
    {
        await using var s = await CrearAsync();
        await s.Catalogo.GuardarClienteAsync(new Cliente { NombreRazonSocial = "Fruver", NitCc = "900.412.889-1" });
        await s.Catalogo.GuardarProductoAsync(new CatalogoProducto { Codigo = "HUE-30", ProgramaId = s.ProgramaAves, Nombre = "Huevo cubeta x30", UnidadMedida = "Cubeta", PrecioUnitario = 18_000m });

        var cliente = () => s.Catalogo.GuardarClienteAsync(new Cliente { NombreRazonSocial = "Otro", NitCc = "900.412.889-1" });
        var codigo = () => s.Catalogo.GuardarProductoAsync(new CatalogoProducto { Codigo = "hue-30", ProgramaId = s.ProgramaAves, Nombre = "Otro", UnidadMedida = "Kg" });
        var precio = () => s.Catalogo.GuardarProductoAsync(new CatalogoProducto { Codigo = "HUE-Q", ProgramaId = s.ProgramaAves, Nombre = "Huevo quebrado", UnidadMedida = "Kg", PrecioUnitario = -1 });

        await cliente.Should().ThrowAsync<ValidationException>().WithMessage("*NIT*");
        await codigo.Should().ThrowAsync<ValidationException>().WithMessage("*código*");
        await precio.Should().ThrowAsync<ValidationException>().WithMessage("*precio*");
        (await s.Catalogo.ListarProductosAsync(s.ProgramaAves)).Should().ContainSingle().Which.Codigo.Should().Be("HUE-30");
    }

    [Fact]
    public async Task Los_datos_institucionales_no_aceptan_un_consecutivo_menor_al_ya_emitido()
    {
        await using var s = await CrearAsync();
        var oficio = await s.CrearOficioAsync("OF-1");
        await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m)));   // recibo 829
        var config = await s.Catalogo.GetConfiguracionAsync();
        config.UltimoConsecutivoRecibo.Should().Be(829);

        config.UltimoConsecutivoRecibo = 500;
        await ((Func<Task>)(() => s.Catalogo.GuardarConfiguracionAsync(config))).Should().ThrowAsync<ValidationException>().WithMessage("*829*");

        config.UltimoConsecutivoRecibo = 900;
        await s.Catalogo.GuardarConfiguracionAsync(config);
        (await s.Consultas.SiguientesConsecutivosAsync()).Recibo.Should().Be(901);
        _ = oficio;
    }

    // ---- Oficios: cierre y apertura ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_oficio_se_crea_con_programa_unico_y_su_numero_no_se_repite()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-123");

        var repetido = () => s.CrearOficioAsync("of-123");
        var sinPrograma = () => s.Oficios.CrearAsync(new OficioDto { NumeroOficio = "OF-9", ProgramaId = 0, Fecha = Datos.F("2026-10-15") });
        var futuro = () => s.Oficios.CrearAsync(new OficioDto { NumeroOficio = "OF-9", ProgramaId = s.E.Ids.ProgramaGanaderia, Fecha = Datos.F("2026-12-01") });

        await repetido.Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe*");
        await sinPrograma.Should().ThrowAsync<ValidationException>();
        await futuro.Should().ThrowAsync<ValidationException>().WithMessage("*futura*");
    }

    [Fact]
    public async Task Un_oficio_cerrado_no_admite_remisiones_nuevas_y_reabrirlo_lo_permite()
    {
        await using var s = await CrearAsync();
        var id = await s.CrearOficioAsync("OF-1");
        await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m)));

        await s.Oficios.CerrarAsync(id);
        var cerrado = await s.Oficios.ObtenerAsync(id);
        (cerrado.Oficio.Cerrado, cerrado.Oficio.FechaCierre).Should().Be((true, Datos.F("2026-11-06")));
        var nueva = () => s.E.Kardex.RegistrarVentaAsync(s.VentaConReciboAsync("REM-2", "OF-1", ("S-2", 4_500_000m)).GetAwaiter().GetResult());
        await nueva.Should().ThrowAsync<ValidationException>().WithMessage("*OF-1*cerrado*");
        (await s.E.AnimalAsync("S-2")).Estado.Should().Be(EstadoAnimal.Disponible, "el rechazo no toca el inventario");
        (await s.E.ContarAsync("REMISION")).Should().Be(1);

        await s.Oficios.ReabrirAsync(id);
        await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-2", "OF-1", ("S-2", 4_500_000m)));
        (await s.Oficios.ObtenerAsync(id)).Remisiones.Should().Be(2);
    }

    [Fact]
    public async Task Cerrar_o_abrir_dos_veces_se_rechaza()
    {
        await using var s = await CrearAsync();
        var id = await s.CrearOficioAsync("OF-1");

        await ((Func<Task>)(() => s.Oficios.ReabrirAsync(id))).Should().ThrowAsync<ValidationException>().WithMessage("*ya está abierto*");
        await s.Oficios.CerrarAsync(id);
        await ((Func<Task>)(() => s.Oficios.CerrarAsync(id))).Should().ThrowAsync<ValidationException>().WithMessage("*ya está cerrado*");
    }

    [Fact]
    public async Task Lo_ya_emitido_en_un_oficio_cerrado_sigue_editable_y_anulable()
    {
        await using var s = await CrearAsync();
        var id = await s.CrearOficioAsync("OF-1");
        var venta = await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m));
        var r = await s.E.Kardex.RegistrarVentaAsync(venta);
        await s.Oficios.CerrarAsync(id);

        venta.RemisionId = r.RemisionId;
        venta.Animales[0].PrecioVenta = 6_000_000m;
        await s.E.Kardex.EditarVentaAsync(venta);
        (await s.ReciboDeAsync(r.RemisionId))!.TotalPagado.Should().Be(6_000_000m);

        await s.E.Kardex.AnularAsync(r.RemisionId, "error de digitación");
        (await s.RemisionAsync(r.RemisionId)).Anulada.Should().BeTrue();
    }

    [Fact]
    public async Task Una_remision_de_otro_oficio_cerrado_no_se_puede_mover_a_el_pero_si_editar_en_el_suyo()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        var cerradoId = await s.CrearOficioAsync("OF-2");
        var venta = await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m));
        var r = await s.E.Kardex.RegistrarVentaAsync(venta);
        await s.Oficios.CerrarAsync(cerradoId);

        venta.RemisionId = r.RemisionId;
        venta.NumeroOficio = "OF-2";
        await ((Func<Task>)(() => s.E.Kardex.EditarVentaAsync(venta))).Should().ThrowAsync<ValidationException>().WithMessage("*cerrado*");
    }

    [Fact]
    public async Task El_resumen_del_oficio_cuenta_sus_remisiones_vigentes_y_lo_vendido()
    {
        await using var s = await CrearAsync();
        var id = await s.CrearOficioAsync("OF-1");
        await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m)));
        var segunda = await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-2", "OF-1", ("S-2", 4_500_000m)));

        (await s.Oficios.ObtenerAsync(id)).Should().Match<OficioResumen>(o => o.Remisiones == 2 && o.TotalVendido == 10_000_000m);
        await s.E.Kardex.AnularAsync(segunda.RemisionId);
        (await s.Oficios.ObtenerAsync(id)).Should().Match<OficioResumen>(o => o.Remisiones == 1 && o.TotalVendido == 5_500_000m);
    }

    [Fact]
    public async Task Un_oficio_solo_cambia_de_programa_si_no_tiene_remisiones()
    {
        await using var s = await CrearAsync();
        var vacio = await s.CrearOficioAsync("OF-1");
        var usado = await s.CrearOficioAsync("OF-2");
        await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-2", ("S-1", 5_500_000m)));

        await s.Oficios.ActualizarAsync(new OficioDto { OficioId = vacio, NumeroOficio = "OF-1", ProgramaId = s.ProgramaAves, Fecha = Datos.F("2026-10-16"), Observaciones = "ajuste" });
        var cambio = () => s.Oficios.ActualizarAsync(new OficioDto { OficioId = usado, NumeroOficio = "OF-2", ProgramaId = s.ProgramaAves, Fecha = Datos.F("2026-10-15") });

        (await s.Oficios.ObtenerAsync(vacio)).Oficio.ProgramaId.Should().Be(s.ProgramaAves);
        await cambio.Should().ThrowAsync<ValidationException>().WithMessage("*ya tiene remisiones*");
    }

    // ---- Recibo de caja SP atómico ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Una_venta_graba_remision_recibo_y_consecutivo_en_una_sola_transaccion()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");

        var r = await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m)));

        var recibo = (await s.ReciboDeAsync(r.RemisionId))!;
        (recibo.NumeroRecibo, recibo.Prefijo, recibo.FormaPago, recibo.TotalPagado, recibo.Anulado).Should().Be((829, "SP", FormaPago.Consignacion, 5_500_000m, false));
        recibo.TotalLetras.Should().Be("CINCO MILLONES QUINIENTOS MIL PESOS M/CTE");
        var remision = await s.RemisionAsync(r.RemisionId);
        (remision.ReciboCaja, remision.TipoComprador, remision.ConvenioId, remision.TerceroEntidad, remision.TerceroNit)
            .Should().Be(("829 SP", TipoComprador.Convenio, s.Convenio.Id, "Convenio Frigocaldas", "900.123.456-1"));
        (remision.Subtotal, remision.ValorDescuento, remision.Total, remision.ProgramaId).Should().Be((5_500_000m, 0m, 5_500_000m, s.E.Ids.ProgramaGanaderia));
        (await s.Catalogo.GetConfiguracionAsync()).UltimoConsecutivoRecibo.Should().Be(829);
        (await s.E.AnimalAsync("S-1")).Estado.Should().Be(EstadoAnimal.Vendido);
        await s.E.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task El_consecutivo_es_correlativo_y_el_descuento_rebaja_el_total_del_recibo()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m)));
        var segunda = await s.VentaConReciboAsync("REM-2", "OF-1", ("S-2", 4_500_000m));
        segunda.Recibo!.DescuentoPorcentaje = 10m;

        var r = await s.E.Kardex.RegistrarVentaAsync(segunda);

        var recibo = (await s.ReciboDeAsync(r.RemisionId))!;
        (recibo.NumeroRecibo, recibo.TotalPagado).Should().Be((830, 4_050_000m));
        var remision = await s.RemisionAsync(r.RemisionId);
        (remision.Subtotal, remision.DescuentoPorcentaje, remision.ValorDescuento, remision.Total).Should().Be((4_500_000m, 10m, 450_000m, 4_050_000m));
    }

    [Fact]
    public async Task Si_algo_falla_no_queda_ni_remision_ni_recibo_ni_se_consume_el_consecutivo()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        var dto = await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m));
        dto.Recibo!.FormaPago = FormaPago.Consignacion;
        dto.Recibo.BancoConsignacion = "";   // el recibo es inválido: debe deshacer también la remisión y el animal vendido

        await ((Func<Task>)(() => s.E.Kardex.RegistrarVentaAsync(dto))).Should().ThrowAsync<ValidationException>().WithMessage("*entidad bancaria*");

        (await s.E.ContarAsync("REMISION")).Should().Be(0);
        (await s.E.ContarAsync("RECIBO_CAJA")).Should().Be(0);
        (await s.E.AnimalAsync("S-1")).Estado.Should().Be(EstadoAnimal.Disponible);
        (await s.Catalogo.GetConfiguracionAsync()).UltimoConsecutivoRecibo.Should().Be(828);
    }

    [Theory]
    [InlineData("efectivo")]
    [InlineData("forma")]
    [InlineData("descuento")]
    [InlineData("fecha")]
    [InlineData("comprador")]
    public async Task Los_datos_invalidos_del_recibo_o_del_comprador_se_rechazan(string caso)
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        var dto = await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m));
        if (caso == "efectivo") dto.Recibo = new ReciboCajaDto { Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.EfectivoCheque };
        if (caso == "forma") dto.Recibo!.FormaPago = FormaPago.TrasladoInterno;
        if (caso == "descuento") dto.Recibo!.DescuentoPorcentaje = 120m;
        if (caso == "fecha") dto.Recibo!.Fecha = Datos.F("2026-12-31");
        if (caso == "comprador") dto.Comercial = new DatosComercialesDto { TipoComprador = TipoComprador.Otro };

        await ((Func<Task>)(() => s.E.Kardex.RegistrarVentaAsync(dto))).Should().ThrowAsync<ValidationException>();

        (await s.E.ContarAsync("RECIBO_CAJA")).Should().Be(0);
        (await s.E.AnimalAsync("S-1")).Estado.Should().Be(EstadoAnimal.Disponible);
    }

    [Fact]
    public async Task El_pago_en_efectivo_o_con_cheque_guarda_los_numeros_de_cheque()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        var dto = await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m));
        dto.Recibo = new ReciboCajaDto { Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.EfectivoCheque, EsEfectivo = true, Cheque1Num = " 1234 ", Cheque2Num = "5678", BancoConsignacion = "no aplica" };

        var r = await s.E.Kardex.RegistrarVentaAsync(dto);

        var recibo = (await s.ReciboDeAsync(r.RemisionId))!;
        (recibo.EsEfectivo, recibo.Cheque1Num, recibo.Cheque2Num, recibo.BancoConsignacion).Should().Be((true, "1234", "5678", (string?)null));
    }

    [Fact]
    public async Task Una_remision_de_traslado_lleva_recibo_interno_sin_oficio()
    {
        await using var s = await CrearAsync();
        var traslado = await s.E.TrasladoAsync("REM-T1", s.E.Ids.Tesorito, s.E.Ids.Montelindo, "S-1");
        traslado.Recibo = new ReciboCajaDto { Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.TrasladoInterno, TotalPagado = 5_000_000m };

        var r = await s.E.Kardex.RegistrarTrasladoAsync(traslado);

        var recibo = (await s.ReciboDeAsync(r.RemisionId))!;
        (recibo.NumeroRecibo, recibo.FormaPago, recibo.TotalPagado).Should().Be((829, FormaPago.TrasladoInterno, 5_000_000m));
        var remision = await s.RemisionAsync(r.RemisionId);
        (remision.OficioId, remision.ReciboCaja, remision.ProgramaId, remision.Total).Should().Be(((int?)null, (string?)null, (int?)s.E.Ids.ProgramaGanaderia, 5_000_000m));
        (await s.E.AnimalAsync("S-1")).GranjaId.Should().Be(s.E.Ids.Montelindo);
        await s.E.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task El_recibo_de_un_traslado_solo_admite_traslado_interno()
    {
        await using var s = await CrearAsync();
        var traslado = await s.E.TrasladoAsync("REM-T1", s.E.Ids.Tesorito, s.E.Ids.Montelindo, "S-1");
        traslado.Recibo = Entorno.Pago();

        await ((Func<Task>)(() => s.E.Kardex.RegistrarTrasladoAsync(traslado))).Should().ThrowAsync<ValidationException>().WithMessage("*Traslado interno*");

        (await s.E.AnimalAsync("S-1")).GranjaId.Should().Be(s.E.Ids.Tesorito);
    }

    // ---- Edición ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Editar_la_venta_recalcula_el_recibo_y_conserva_su_numero()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        var venta = await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m));
        var r = await s.E.Kardex.RegistrarVentaAsync(venta);

        venta.RemisionId = r.RemisionId;
        venta.Animales.Add(new VentaAnimalDto { AnimalId = await s.E.IdAsync("S-2"), PrecioVenta = 4_500_000m });
        venta.Recibo = Entorno.Pago(5m);
        await s.E.Kardex.EditarVentaAsync(venta);

        var recibo = (await s.ReciboDeAsync(r.RemisionId))!;
        (recibo.NumeroRecibo, recibo.TotalPagado).Should().Be((829, 9_500_000m));
        (await s.E.ContarAsync("RECIBO_CAJA")).Should().Be(1);
        (await s.RemisionAsync(r.RemisionId)).ReciboCaja.Should().Be("829 SP");
        (await s.Catalogo.GetConfiguracionAsync()).UltimoConsecutivoRecibo.Should().Be(829, "editar no consume otro consecutivo");
    }

    [Fact]
    public async Task El_recibo_se_edita_por_si_solo_despues_de_guardado_y_su_total_sigue_a_la_remision()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        var r = await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m)));

        var editado = await s.Recibos.GuardarAsync(r.RemisionId, new ReciboCajaDto
        {
            Fecha = Datos.F("2026-11-05"), FormaPago = FormaPago.EfectivoCheque, Cheque1Num = "9999", DescuentoPorcentaje = 10m, Observaciones = " Pagó con cheque ",
        });

        (editado.NumeroRecibo, editado.FormaPago, editado.Cheque1Num, editado.BancoConsignacion, editado.TotalPagado, editado.Observaciones)
            .Should().Be((829, FormaPago.EfectivoCheque, "9999", (string?)null, 4_950_000m, "Pagó con cheque"));
        editado.TotalLetras.Should().Be("CUATRO MILLONES NOVECIENTOS CINCUENTA MIL PESOS M/CTE");
        var remision = await s.RemisionAsync(r.RemisionId);
        (remision.DescuentoPorcentaje, remision.ValorDescuento, remision.Total).Should().Be((10m, 550_000m, 4_950_000m));
    }

    [Fact]
    public async Task Anular_la_remision_anula_su_recibo_y_el_numero_no_se_reutiliza()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        var r = await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m)));

        await s.E.Kardex.AnularAsync(r.RemisionId, "venta cancelada");

        (await s.ReciboDeAsync(r.RemisionId))!.Anulado.Should().BeTrue();
        await ((Func<Task>)(() => s.Recibos.GuardarAsync(r.RemisionId, Entorno.Pago()))).Should().ThrowAsync<ValidationException>().WithMessage("*anulada*");
        var siguiente = await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-2", "OF-1", ("S-2", 4_500_000m)));
        (await s.ReciboDeAsync(siguiente.RemisionId))!.NumeroRecibo.Should().Be(830);
    }

    [Fact]
    public async Task Una_venta_de_Sistema_Granjas_sin_recibo_recibe_su_recibo_SP_y_se_edita_despues()
    {
        await using var s = await CrearAsync();
        var legado = await s.E.VentaAsync("REM-OLD", "OF-OLD", "RC-77", null, ("S-1", 5_500_000m));
        var r = await s.E.Kardex.RegistrarVentaAsync(legado);
        var antes = await s.Consultas.ObtenerAsync(r.RemisionId);
        (antes.Recibo!.NumeroRecibo, antes.Recibo.Observaciones, antes.EsDelModulo, antes.Estado, antes.Remision.ReciboCaja, antes.Clase)
            .Should().Be((829, "Recibo anterior: RC-77", true, EstadoRemision.Pagada, "829 SP", "ANIMALES"));

        var recibo = await s.Recibos.GuardarAsync(r.RemisionId, Entorno.Pago(), s.Comprador);

        var despues = await s.Consultas.ObtenerAsync(r.RemisionId);
        (recibo.NumeroRecibo, despues.Remision.ReciboCaja, despues.Remision.ConvenioId).Should().Be((829, "829 SP", s.Convenio.Id));
    }

    // ---- Consultas ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task La_bandeja_lista_ventas_y_traslados_con_su_estado_oficio_y_recibo()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m)));
        var traslado = await s.E.TrasladoAsync("REM-T1", s.E.Ids.Tesorito, s.E.Ids.Montelindo, "S-2");
        traslado.Recibo = new ReciboCajaDto { Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.TrasladoInterno };
        await s.E.Kardex.RegistrarTrasladoAsync(traslado);

        var todas = await s.Consultas.ListarAsync(new RemisionFiltro());
        var ventas = await s.Consultas.ListarAsync(new RemisionFiltro(Tipo: TipoRemision.Venta));
        var porEstado = await s.Consultas.ListarAsync(new RemisionFiltro(Estado: EstadoRemision.TrasladoInterno));
        var porTexto = await s.Consultas.ListarAsync(new RemisionFiltro(Texto: "829"));

        todas.Select(f => (f.Numero, f.Tipo, f.Oficio, f.Recibo, f.Estado)).Should().BeEquivalentTo(
            new[]
            {
                ("REM-1", TipoRemision.Venta, (string?)"OF-1", (string?)"829 SP", EstadoRemision.Pagada),
                ("REM-T1", TipoRemision.Traslado, (string?)null, (string?)"830 SP", EstadoRemision.TrasladoInterno),
            });
        ventas.Should().ContainSingle().Which.ClienteODestino.Should().Be("Convenio Frigocaldas");
        porEstado.Should().ContainSingle().Which.ClienteODestino.Should().Be("Destino: Montelindo");
        porTexto.Should().ContainSingle().Which.Numero.Should().Be("REM-1");
        (await s.Consultas.ListarAsync(new RemisionFiltro(GranjaId: s.E.Ids.Montelindo))).Should().ContainSingle().Which.Numero.Should().Be("REM-T1", "la granja destino también cuenta");
        var resumen = await s.Consultas.ResumirAsync(Datos.F("2026-10-25"), null, null);
        (resumen.RemisionesDelMes, resumen.VentasDelMes).Should().Be((1, 5_500_000m), "el KPI del módulo cuenta solo ventas; los traslados se manejan en Sistema Granjas");
    }

    [Fact]
    public async Task La_bandeja_oculta_las_anuladas_salvo_que_se_pidan_y_el_detalle_trae_las_lineas()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        var r = await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m)));
        var vista = await s.Consultas.ObtenerAsync(r.RemisionId);
        vista.Lineas.Should().ContainSingle().Which.Should().Match<LineaRemision>(l => l.Clase == ClaseDeLinea.Animal && l.Codigo == "S-1" && l.ValorTotal == 5_500_000m);
        (vista.Clase, vista.Totales.Total, vista.Programa).Should().Be(("ANIMALES", 5_500_000m, "Ganadería (Bovinos)"));

        await s.E.Kardex.AnularAsync(r.RemisionId);

        (await s.Consultas.ListarAsync(new RemisionFiltro())).Should().BeEmpty();
        (await s.Consultas.ListarAsync(new RemisionFiltro(IncluirAnuladas: true))).Single().Estado.Should().Be(EstadoRemision.Anulada);
    }

    [Fact]
    public async Task La_lista_de_recibos_resume_lo_recaudado_por_forma_de_pago()
    {
        await using var s = await CrearAsync();
        await s.CrearOficioAsync("OF-1");
        await s.E.Kardex.RegistrarVentaAsync(await s.VentaConReciboAsync("REM-1", "OF-1", ("S-1", 5_500_000m)));
        var efectivo = await s.VentaConReciboAsync("REM-2", "OF-1", ("S-2", 4_500_000m));
        efectivo.Recibo = new ReciboCajaDto { Fecha = Datos.F("2026-11-06"), FormaPago = FormaPago.EfectivoCheque, EsEfectivo = true };
        await s.E.Kardex.RegistrarVentaAsync(efectivo);

        var resumen = await s.Consultas.ResumirRecibosAsync();
        var lista = await s.Consultas.ListarRecibosAsync(texto: "REM-2");

        (resumen.Consignacion, resumen.EfectivoCheque, resumen.UltimoConsecutivo).Should().Be((5_500_000m, 4_500_000m, "830 SP"));
        lista.Should().ContainSingle().Which.FormaPagoTexto.Should().Be("Efectivo / Cheque");
        (await s.Consultas.SiguientesConsecutivosAsync()).Should().Be(("REM-3", 831));
    }

    // ---- Remisiones de productos -------------------------------------------------------------------------------------------

    private static async Task<(int ProductoId, int OficioId)> PrepararProductosAsync(Entorno s)
    {
        var producto = await s.Catalogo.GuardarProductoAsync(new CatalogoProducto
        {
            Codigo = "HUE-30", ProgramaId = s.ProgramaAves, Nombre = "Huevo cubeta x30", UnidadMedida = "Cubeta", PrecioUnitario = 18_000m,
        });
        var oficio = await s.CrearOficioAsync("OF-HUE", s.ProgramaAves);
        return (producto.Id, oficio);
    }

    private static RemisionProductosDto VentaProductos(Entorno s, int? productoId, string numero = "REM-P1", decimal cantidad = 10m, decimal valor = 18_000m) => new()
    {
        Tipo = TipoRemision.Venta, ProgramaId = s.ProgramaAves, NumeroOficio = "OF-HUE", NumeroRemision = numero, Fecha = Datos.F("2026-11-05"),
        GranjaOrigenId = s.E.Ids.Montelindo, Comprador = s.Convenio.Nombre, Comercial = s.Comprador, Recibo = Entorno.Pago(),
        Lineas = [new LineaProductoDto { ProductoId = productoId, Descripcion = "", UnidadMedida = "Cubeta", Cantidad = cantidad, ValorUnitario = valor }],
    };

    [Fact]
    public async Task Una_venta_de_productos_no_mueve_inventario_y_lleva_oficio_y_recibo()
    {
        await using var s = await CrearAsync();
        var (productoId, _) = await PrepararProductosAsync(s);

        var movimientosAntes = await s.E.ContarAsync("MOVIMIENTO_INVENTARIO");

        var r = await s.E.Kardex.RegistrarRemisionProductosAsync(VentaProductos(s, productoId));

        (r.Lineas, r.Total).Should().Be((1, 180_000m));
        var vista = await s.Consultas.ObtenerAsync(r.RemisionId);
        (vista.Clase, vista.Estado, vista.Remision.ReciboCaja, vista.Recibo!.TotalPagado, vista.Programa).Should().Be(("PRODUCTOS", EstadoRemision.Pagada, "829 SP", 180_000m, "Avicultura (Aves)"));
        vista.Lineas.Single().Should().Match<LineaRemision>(l => l.Clase == ClaseDeLinea.Producto && l.Descripcion == "Huevo cubeta x30" && l.Cantidad == 10m && l.ValorUnitario == 18_000m);
        (await s.E.ContarAsync("MOVIMIENTO_INVENTARIO")).Should().Be(movimientosAntes, "los productos no mueven inventario");
        await s.E.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Una_remision_de_productos_admite_cantidades_decimales_y_se_edita_y_anula()
    {
        await using var s = await CrearAsync();
        var (productoId, _) = await PrepararProductosAsync(s);
        var dto = VentaProductos(s, productoId, cantidad: 12.5m, valor: 3_000m);
        var r = await s.E.Kardex.RegistrarRemisionProductosAsync(dto);
        r.Total.Should().Be(37_500m);

        dto.RemisionId = r.RemisionId;
        dto.Lineas[0].Cantidad = 20m;
        dto.Recibo = Entorno.Pago(10m);
        var editada = await s.E.Kardex.EditarRemisionProductosAsync(dto);

        editada.Total.Should().Be(54_000m);
        (await s.ReciboDeAsync(r.RemisionId))!.Should().Match<ReciboCaja>(x => x.NumeroRecibo == 829 && x.TotalPagado == 54_000m);
        (await s.E.ContarAsync("REMISION_DETALLE")).Should().Be(1, "editar reemplaza las líneas");

        await s.E.Kardex.AnularAsync(r.RemisionId, "devolución");
        (await s.RemisionAsync(r.RemisionId)).Anulada.Should().BeTrue();
        (await s.ReciboDeAsync(r.RemisionId))!.Anulado.Should().BeTrue();
        await ((Func<Task>)(() => s.E.Kardex.EditarRemisionProductosAsync(dto))).Should().ThrowAsync<ValidationException>().WithMessage("*anulada*");
    }

    [Fact]
    public async Task Una_venta_de_productos_exige_oficio_existente_abierto_y_del_mismo_programa()
    {
        await using var s = await CrearAsync();
        var (productoId, oficioId) = await PrepararProductosAsync(s);
        var otroPrograma = await s.CrearOficioAsync("OF-GAN");

        var inexistente = VentaProductos(s, productoId);
        inexistente.NumeroOficio = "OF-NO";
        var ajeno = VentaProductos(s, productoId);
        ajeno.NumeroOficio = "OF-GAN";
        await s.Oficios.CerrarAsync(oficioId);
        var cerrado = VentaProductos(s, productoId);

        await ((Func<Task>)(() => s.E.Kardex.RegistrarRemisionProductosAsync(inexistente))).Should().ThrowAsync<ValidationException>().WithMessage("*no existe*");
        await ((Func<Task>)(() => s.E.Kardex.RegistrarRemisionProductosAsync(ajeno))).Should().ThrowAsync<ValidationException>().WithMessage("*otro programa*");
        await ((Func<Task>)(() => s.E.Kardex.RegistrarRemisionProductosAsync(cerrado))).Should().ThrowAsync<ValidationException>().WithMessage("*cerrado*");
        (await s.E.ContarAsync("REMISION")).Should().Be(0);
        _ = otroPrograma;
    }

    [Fact]
    public async Task Un_producto_de_otro_programa_o_con_datos_invalidos_se_rechaza()
    {
        await using var s = await CrearAsync();
        var (productoId, _) = await PrepararProductosAsync(s);
        var ganaderia = await s.Catalogo.GuardarProductoAsync(new CatalogoProducto { Codigo = "LEC-1", ProgramaId = s.E.Ids.ProgramaGanaderia, Nombre = "Leche", UnidadMedida = "Litro", PrecioUnitario = 2_000m });

        var ajeno = VentaProductos(s, ganaderia.Id);
        var sinCantidad = VentaProductos(s, productoId, cantidad: 0m);
        var sinValor = VentaProductos(s, productoId, valor: 0m);

        await ((Func<Task>)(() => s.E.Kardex.RegistrarRemisionProductosAsync(ajeno))).Should().ThrowAsync<ValidationException>().WithMessage("*no pertenece al programa*");
        await ((Func<Task>)(() => s.E.Kardex.RegistrarRemisionProductosAsync(sinCantidad))).Should().ThrowAsync<ValidationException>().WithMessage("*cantidad*");
        await ((Func<Task>)(() => s.E.Kardex.RegistrarRemisionProductosAsync(sinValor))).Should().ThrowAsync<ValidationException>().WithMessage("*valor unitario*");
        (await s.E.ContarAsync("RECIBO_CAJA")).Should().Be(0);
    }

    [Fact]
    public async Task Un_traslado_de_productos_se_rechaza_porque_los_traslados_son_de_Sistema_Granjas()
    {
        await using var s = await CrearAsync();
        var (productoId, _) = await PrepararProductosAsync(s);
        var dto = new RemisionProductosDto
        {
            Tipo = TipoRemision.Traslado, ProgramaId = s.ProgramaAves, NumeroRemision = "REM-P2", Fecha = Datos.F("2026-11-05"),
            GranjaOrigenId = s.E.Ids.Montelindo, GranjaDestinoId = s.E.Ids.Tesorito,
            Recibo = new ReciboCajaDto { Fecha = Datos.F("2026-11-05"), FormaPago = FormaPago.TrasladoInterno },
            Lineas = [new LineaProductoDto { ProductoId = productoId, UnidadMedida = "Cubeta", Cantidad = 5m, ValorUnitario = 18_000m }],
        };

        await ((Func<Task>)(() => s.E.Kardex.RegistrarRemisionProductosAsync(dto))).Should().ThrowAsync<ValidationException>().WithMessage("*solo registra ventas de subproductos*");
        (await s.E.ContarAsync("REMISION")).Should().Be(0);
    }

    [Fact]
    public async Task Una_linea_de_subproducto_escrita_a_mano_no_necesita_producto_de_catalogo_pero_si_descripcion()
    {
        await using var s = await CrearAsync();
        var (_, oficio) = await PrepararProductosAsync(s);
        var libre = VentaProductos(s, null, valor: 7_500m);
        libre.Lineas[0].Descripcion = "Gallinaza seca";
        libre.Lineas[0].UnidadMedida = "Bulto";
        var sinDescripcion = VentaProductos(s, null, valor: 7_500m);
        sinDescripcion.NumeroRemision = "REM-P9";
        sinDescripcion.Lineas[0].Descripcion = "  ";

        await ((Func<Task>)(() => s.E.Kardex.RegistrarRemisionProductosAsync(sinDescripcion))).Should().ThrowAsync<ValidationException>().WithMessage("*descripción*");
        var r = await s.E.Kardex.RegistrarRemisionProductosAsync(libre);

        var vista = await s.Consultas.ObtenerAsync(r.RemisionId);
        (vista.Clase, vista.Lineas.Single().ProductoId, vista.Lineas.Single().Descripcion, vista.Totales.Total).Should().Be(("PRODUCTOS", (int?)null, "Gallinaza seca", libre.Lineas[0].Cantidad * 7_500m));
        (await s.Consultas.ListarAsync(new RemisionFiltro(Clase: "ANIMALES"))).Should().BeEmpty();
        (await s.Consultas.ListarAsync(new RemisionFiltro(Clase: "PRODUCTOS"))).Should().ContainSingle();
        oficio.Should().BeGreaterThan(0);
    }

    // ---- Programas de productos ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_programa_de_productos_no_aparece_entre_los_programas_de_animales_ni_crea_categoria_general()
    {
        await using var s = await CrearAsync();
        var catalogo = new CatalogoService(s.E.Db.Uow);

        var hortalizas = await catalogo.GuardarProgramaAsync(
            new Programa { Codigo = "PRG-HOR", Nombre = "Hortalizas Clima Frío", TipoInventario = TipoInventario.Individual, ManejaInventarioAnimal = false },
            [s.E.Ids.Tesorito]);

        hortalizas.ManejaInventarioAnimal.Should().BeFalse();
        (await catalogo.GetProgramasPorTipoAsync(TipoInventario.Individual)).Select(p => p.Codigo).Should().Equal("PRG-GAN");
        (await catalogo.GetProgramasAsync()).Select(p => p.Codigo).Should().Contain("PRG-HOR");
        await using var uow = await s.E.Db.Uow.OpenReadOnlyAsync();
        (await uow.Categorias.GetByProgramaAsync(hortalizas.Id)).Should().BeEmpty("un programa de productos no tiene categorías ni tarifas");
        (await uow.Programas.GetByIdAsync(hortalizas.Id))!.ManejaInventarioAnimal.Should().BeFalse("el indicador se conserva al guardar");

        var guardado = await catalogo.GuardarProgramaAsync(hortalizas, [s.E.Ids.Tesorito]);
        guardado.ManejaInventarioAnimal.Should().BeFalse();
        await s.Catalogo.GuardarProductoAsync(new CatalogoProducto { Codigo = "HOR-04", ProgramaId = hortalizas.Id, Nombre = "Tomate chonto", UnidadMedida = "Kg", PrecioUnitario = 3_000m });
        (await s.Catalogo.ListarProductosAsync(hortalizas.Id)).Should().ContainSingle();
    }
}
