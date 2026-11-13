using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Lotes;

public class LoteServiceTests
{
    private sealed class Entorno : IAsyncDisposable
    {
        public required BaseDeDatosDePrueba Db { get; init; }
        public required IdsBase Ids { get; init; }
        public required LoteService Servicio { get; init; }
        public required int ProgramaId { get; init; }
        public required int Cria { get; init; }
        public required int Postura { get; init; }
        public required int General { get; init; }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();

        public async Task<LotePoblacion> LoteAsync(int id) => (await Servicio.ObtenerAsync(id)).Lote;

        public NuevoLoteDto Compra(string codigo = "L-AVE-01", int cantidad = 1000, decimal precio = 15_000m) => new()
        {
            ProgramaId = ProgramaId, GranjaId = Ids.Montelindo, EtapaId = Cria, ClaseEntrada = ClaseEntrada.Compra,
            CodigoLote = codigo, Ubicacion = "Galpón 1", LineaRaza = "Hy-Line Brown", FechaIngreso = Datos.F("2026-11-02"),
            Cantidad = cantidad, PrecioUnitarioFactura = precio, Factura = "FAC-77", OrdenCompra = "OC-5", Proveedor = "Incubadora Andina",
        };
    }

    private static async Task<Entorno> CrearAsync()
    {
        var db = await BaseDeDatosDePrueba.CrearAsync();
        IdsBase ids;
        await using (var uow = await db.Uow.OpenReadOnlyAsync())
        {
            ids = await Datos.IdsBaseAsync(uow);
        }

        // Etapas del programa de avicultura con su valor de referencia por cabeza.
        var tarifas = new TarifaProgramaService(db.Uow);
        int programaId;
        await using (var uow = await db.Uow.OpenReadOnlyAsync())
        {
            programaId = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!.Id;
        }

        var filas = (await tarifas.ListarAsync(programaId)).ToList();
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = "Cría", UnidadCobro = "UNIDAD", ValorReferencia = 15_000m });
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = "Postura", UnidadCobro = "UNIDAD", ValorReferencia = 40_000m });
        await tarifas.GuardarAsync(programaId, filas);

        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        var categorias = await lectura.Categorias.GetByProgramaAsync(programaId);
        return new Entorno
        {
            Db = db, Ids = ids, ProgramaId = programaId,
            Servicio = new LoteService(db.Uow, new RelojFijo(Datos.F("2026-11-06"))),
            Cria = categorias.Single(c => c.Nombre == "Cría").Id,
            Postura = categorias.Single(c => c.Nombre == "Postura").Id,
            General = categorias.Single(c => c.Nombre == "General").Id,
        };
    }

    // ---- Ficha: datos descriptivos ------------------------------------------------------------------------------------

    private static ActualizarLoteDto Datos_(int id, LotePoblacion l) => new()
    {
        LoteId = id, CodigoLote = l.CodigoLote, Ubicacion = l.Ubicacion, LineaRaza = l.LineaRaza, FechaIngreso = l.FechaNacimientoIngreso,
        OrigenReferencia = l.OrigenReferencia, Proveedor = l.Proveedor, OrdenCompra = l.OrdenCompra, Observaciones = l.Observaciones,
        GranjaId = l.GranjaId, EtapaId = l.EtapaId, CantidadInicial = l.CantidadInicial, MortandadAcumulada = l.MortandadAcumulada,
        ValorContableUnitario = l.ValorContableUnitario,
    };

    [Fact]
    public async Task Actualizar_los_datos_del_lote_no_toca_cantidades_ni_valores()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());
        var dto = Datos_(id, await e.LoteAsync(id));
        dto.CodigoLote = "L-AVE-99";
        dto.Ubicacion = "Galpón 4";
        dto.Proveedor = "Otra incubadora";
        dto.Observaciones = "  Nota libre  ";

        await e.Servicio.ActualizarDatosAsync(dto);

        var lote = await e.LoteAsync(id);
        (lote.CodigoLote, lote.Ubicacion, lote.Proveedor, lote.Observaciones).Should().Be(("L-AVE-99", "Galpón 4", "Otra incubadora", "Nota libre"));
        (lote.CantidadActual, lote.ValorCompraUnitario, lote.ValorTotalContable, lote.GranjaId, lote.EtapaId)
            .Should().Be((1000, 15_000m, 15_000_000m, e.Ids.Montelindo, e.Cria));
    }

    [Fact]
    public async Task Actualizar_los_datos_rechaza_codigo_repetido_pero_permite_conservar_el_propio()
    {
        await using var e = await CrearAsync();
        var uno = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra("L-1"));
        await e.Servicio.RegistrarNuevoLoteAsync(e.Compra("L-2"));
        var dto = Datos_(uno, await e.LoteAsync(uno));

        await e.Servicio.ActualizarDatosAsync(dto);   // mismo código: válido
        dto.CodigoLote = "l-2";

        var repetido = () => e.Servicio.ActualizarDatosAsync(dto);
        (await repetido.Should().ThrowAsync<ValidationException>()).WithMessage("*Ya existe un lote con el código*");
    }

    [Theory]
    [InlineData("codigo")]
    [InlineData("fecha")]
    [InlineData("proveedor")]
    public async Task Actualizar_los_datos_valida_lo_obligatorio(string campo)
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());
        var dto = Datos_(id, await e.LoteAsync(id));
        switch (campo)
        {
            case "codigo": dto.CodigoLote = " "; break;
            case "fecha": dto.FechaIngreso = Datos.F("2026-11-20"); break;
            default: dto.Proveedor = ""; break;
        }

        var accion = () => e.Servicio.ActualizarDatosAsync(dto);
        await accion.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Actualizar_poblacion_etapa_granja_y_valor_recalcula_actual_estado_y_total_sin_tocar_el_valor_de_compra()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());
        await e.Servicio.DesdoblarAsync(id, 100, "L-AVE-01B", "Galpón 2");   // inicial 900, actual 900
        var dto = Datos_(id, await e.LoteAsync(id));
        dto.CantidadInicial = 950;
        dto.MortandadAcumulada = 50;
        dto.EtapaId = e.Postura;
        dto.ValorContableUnitario = 40_000m;
        dto.GranjaId = e.Ids.Tesorito;

        // El programa de avicultura solo corre en Montelindo: la granja se rechaza.
        await ((Func<Task>)(() => e.Servicio.ActualizarDatosAsync(dto))).Should().ThrowAsync<ValidationException>().WithMessage("*no se ejecuta en la granja*");
        dto.GranjaId = e.Ids.Montelindo;

        await e.Servicio.ActualizarDatosAsync(dto);

        var lote = await e.LoteAsync(id);
        (lote.CantidadInicial, lote.MortandadAcumulada, lote.CantidadActual).Should().Be((950, 50, 900), "la actual se mueve lo que cambian inicial (+50) y bajas (−50): 900 + 50 − 50");
        (lote.EtapaId, lote.ValorContableUnitario, lote.ValorTotalContable, lote.ValorCompraUnitario).Should().Be((e.Postura, 40_000m, 36_000_000m, 15_000m));
        lote.Observaciones.Should().Contain("Corrección desde la ficha").And.Contain("población").And.Contain("etapa").And.Contain("valor contable");
    }

    [Fact]
    public async Task Actualizar_la_poblacion_rechaza_bajas_mayores_y_deja_el_lote_finalizado_sin_cabezas()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());
        var dto = Datos_(id, await e.LoteAsync(id));

        dto.MortandadAcumulada = 2000;
        await ((Func<Task>)(() => e.Servicio.ActualizarDatosAsync(dto))).Should().ThrowAsync<ValidationException>().WithMessage("*bajas*");

        dto.MortandadAcumulada = 1000;   // todas las cabezas dadas de baja
        await e.Servicio.ActualizarDatosAsync(dto);
        var lote = await e.LoteAsync(id);
        (lote.CantidadActual, lote.Estado, lote.ValorTotalContable).Should().Be((0, EstadoLote.Finalizado, 0m));

        dto.MortandadAcumulada = 400;   // se corrige y vuelve a estar activo
        await e.Servicio.ActualizarDatosAsync(dto);
        (await e.LoteAsync(id)).Estado.Should().Be(EstadoLote.Activo);
    }

    [Fact]
    public async Task Un_lote_con_la_compra_anulada_no_se_actualiza()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());
        var dto = Datos_(id, await e.LoteAsync(id));
        await e.Servicio.AnularCompraAsync(id, null);

        var accion = () => e.Servicio.ActualizarDatosAsync(dto);
        (await accion.Should().ThrowAsync<ValidationException>()).WithMessage("*anulada*");
    }

    // ---- Nuevo lote ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_lote_comprado_guarda_la_factura_como_valor_de_compra_inmutable_y_como_valor_contable_inicial()
    {
        await using var e = await CrearAsync();

        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());

        var lote = await e.LoteAsync(id);
        lote.ClaseEntrada.Should().Be(ClaseEntrada.Compra);
        (lote.CantidadInicial, lote.CantidadActual, lote.MortandadAcumulada).Should().Be((1000, 1000, 0));
        (lote.ValorCompraUnitario, lote.ValorContableUnitario, lote.ValorTotalContable).Should().Be((15_000m, 15_000m, 15_000_000m));
        (lote.OrigenReferencia, lote.Proveedor, lote.OrdenCompra).Should().Be(("FAC-77", "Incubadora Andina", "OC-5"));
        lote.Estado.Should().Be(EstadoLote.Activo);
    }

    [Fact]
    public async Task Un_lote_nacido_en_la_granja_entra_con_compra_cero_y_el_valor_de_referencia_de_su_etapa()
    {
        await using var e = await CrearAsync();
        var dto = e.Compra("L-AVE-02", 500);
        dto.ClaseEntrada = ClaseEntrada.Nacimiento;
        dto.Factura = dto.OrdenCompra = dto.Proveedor = null;
        dto.PrecioUnitarioFactura = 999m;   // se ignora en los nacimientos
        dto.OrigenReferencia = "Acta de incubación 12";

        var lote = await e.LoteAsync(await e.Servicio.RegistrarNuevoLoteAsync(dto));

        (lote.ValorCompraUnitario, lote.ValorContableUnitario, lote.ValorTotalContable).Should().Be((0m, 15_000m, 7_500_000m));
        (lote.OrigenReferencia, lote.Proveedor).Should().Be(("Acta de incubación 12", null));
    }

    [Fact]
    public async Task Un_estanque_sin_control_numerico_no_lleva_cantidades_y_su_valor_total_es_el_del_lote()
    {
        await using var e = await CrearAsync();
        var dto = e.Compra("L-PEC-01", 0, 3_000_000m);
        dto.ControlNumericoPoblacion = false;

        var id = await e.Servicio.RegistrarNuevoLoteAsync(dto);
        var lote = await e.LoteAsync(id);

        (lote.CantidadInicial, lote.CantidadActual).Should().Be((0, 0));
        lote.ValorTotalContable.Should().Be(3_000_000m);
        var baja = () => e.Servicio.RegistrarBajaAsync(id, 1, "Mortandad", Datos.F("2026-11-05"), null);
        await baja.Should().ThrowAsync<ValidationException>().WithMessage("*no lleva control numérico*");
        var desdoble = () => e.Servicio.DesdoblarAsync(id, 1, "L-PEC-02", "Estanque 2");
        await desdoble.Should().ThrowAsync<ValidationException>().WithMessage("*sin control numérico*");
    }

    [Theory]
    [InlineData("codigo", "")]
    [InlineData("cantidad", "0")]
    [InlineData("factura", "")]
    [InlineData("precio", "0")]
    [InlineData("fecha", "futura")]
    [InlineData("repetido", "")]
    public async Task Los_datos_invalidos_de_un_nuevo_lote_se_rechazan(string campo, string valor)
    {
        await using var e = await CrearAsync();
        await e.Servicio.RegistrarNuevoLoteAsync(e.Compra("L-EXISTE"));
        var dto = e.Compra("L-NUEVO");
        switch (campo)
        {
            case "codigo": dto.CodigoLote = valor; break;
            case "cantidad": dto.Cantidad = int.Parse(valor); break;
            case "factura": dto.Factura = valor; break;
            case "precio": dto.PrecioUnitarioFactura = decimal.Parse(valor); break;
            case "fecha": dto.FechaIngreso = Datos.F("2026-11-20"); break;
            case "repetido": dto.CodigoLote = "l-existe"; break;
        }

        var act = () => e.Servicio.RegistrarNuevoLoteAsync(dto);

        await act.Should().ThrowAsync<ValidationException>();
        (await e.Servicio.ListarAsync(e.ProgramaId)).Should().HaveCount(1);
    }

    [Fact]
    public async Task La_etapa_debe_ser_del_programa_y_el_programa_debe_manejarse_por_lotes()
    {
        await using var e = await CrearAsync();
        var ajena = e.Compra();
        ajena.EtapaId = e.Ids.Categoria(NombresBase.CategoriaTerneros);
        var individual = e.Compra();
        individual.ProgramaId = e.Ids.ProgramaGanaderia;

        await ((Func<Task>)(() => e.Servicio.RegistrarNuevoLoteAsync(ajena))).Should().ThrowAsync<ValidationException>().WithMessage("*no pertenece al programa*");
        await ((Func<Task>)(() => e.Servicio.RegistrarNuevoLoteAsync(individual))).Should().ThrowAsync<ValidationException>().WithMessage("*no se maneja por lotes*");
    }

    // ---- Cambio de etapa ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cambiar_de_etapa_recalcula_el_valor_con_la_referencia_y_reporta_el_ajuste()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra(cantidad: 1000, precio: 15_000m));

        var resultado = await e.Servicio.CambiarEtapaAsync(id, e.Postura, "Galpón 3");

        // Δ = (40.000 − 15.000) × 1.000
        (resultado.ValorAnteriorLote, resultado.ValorNuevoLote, resultado.Delta).Should().Be((15_000_000m, 40_000_000m, 25_000_000m));
        resultado.SinValorDeReferencia.Should().BeFalse();
        var lote = await e.LoteAsync(id);
        (lote.EtapaId, lote.Ubicacion, lote.ValorContableUnitario, lote.ValorTotalContable).Should().Be((e.Postura, "Galpón 3", 40_000m, 40_000_000m));
        lote.ValorCompraUnitario.Should().Be(15_000m, "el valor de compra no cambia nunca");
        lote.Observaciones.Should().Contain("Cambio de etapa a «Postura»");
    }

    [Fact]
    public async Task Una_etapa_sin_valor_de_referencia_conserva_el_valor_del_lote_y_lo_avisa()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());

        var resultado = await e.Servicio.CambiarEtapaAsync(id, e.General, null);   // «General» vale 0

        resultado.SinValorDeReferencia.Should().BeTrue();
        resultado.Delta.Should().Be(0m);
        (await e.LoteAsync(id)).ValorTotalContable.Should().Be(15_000_000m);
    }

    [Fact]
    public async Task Sin_cambio_de_etapa_ni_de_ubicacion_no_hay_nada_que_hacer()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());

        var act = () => e.Servicio.CambiarEtapaAsync(id, e.Cria, null);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*otra etapa*ubicación*");
    }

    // ---- Desdoble ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_desdoble_divide_el_lote_sin_cambiar_el_saldo_financiero_total()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra(cantidad: 1000, precio: 15_000m));
        await e.Servicio.RegistrarBajaAsync(id, 100, "Mortandad", Datos.F("2026-11-05"), null);
        var antes = (await e.Servicio.ListarAsync(e.ProgramaId)).Sum(d => d.Lote.ValorTotalContable);

        var nuevoId = await e.Servicio.DesdoblarAsync(id, 300, "L-AVE-01B", "Galpón 2");

        var origen = await e.LoteAsync(id);
        var nuevo = await e.LoteAsync(nuevoId);
        (origen.CantidadActual, origen.ValorTotalContable).Should().Be((600, 9_000_000m));
        (nuevo.ClaseEntrada, nuevo.OrigenReferencia, nuevo.Ubicacion).Should().Be((ClaseEntrada.Desdoble, "L-AVE-01", "Galpón 2"));
        (nuevo.CantidadInicial, nuevo.CantidadActual, nuevo.MortandadAcumulada).Should().Be((300, 300, 0));
        (nuevo.ValorCompraUnitario, nuevo.ValorContableUnitario, nuevo.EtapaId, nuevo.LineaRaza, nuevo.FechaNacimientoIngreso)
            .Should().Be((15_000m, 15_000m, origen.EtapaId, origen.LineaRaza, origen.FechaNacimientoIngreso));
        (await e.Servicio.ListarAsync(e.ProgramaId)).Sum(d => d.Lote.ValorTotalContable).Should().Be(antes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000)]
    [InlineData(1500)]
    public async Task El_desdoble_exige_una_cantidad_positiva_y_menor_a_la_poblacion_actual(int cantidad)
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());

        var act = () => e.Servicio.DesdoblarAsync(id, cantidad, "L-X", "Galpón 2");

        await act.Should().ThrowAsync<ValidationException>();
        (await e.Servicio.ListarAsync(e.ProgramaId)).Should().HaveCount(1);
    }

    // ---- Bajas ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Una_baja_descuenta_cabezas_acumula_mortandad_y_recalcula_el_valor()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra(cantidad: 1000, precio: 15_000m));

        await e.Servicio.RegistrarBajaAsync(id, 58, "Mortandad", Datos.F("2026-11-05"), "calor");

        var lote = await e.LoteAsync(id);
        (lote.CantidadActual, lote.MortandadAcumulada, lote.ValorTotalContable).Should().Be((942, 58, 14_130_000m));
        lote.Estado.Should().Be(EstadoLote.Activo);
        lote.Observaciones.Should().Contain("[05/11/2026] Mortandad: 58 cabezas (calor)");
    }

    [Fact]
    public async Task Cuando_no_queda_ninguna_cabeza_el_lote_pasa_a_Finalizado_y_ya_no_admite_operaciones()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra(cantidad: 10));

        await e.Servicio.RegistrarBajaAsync(id, 10, "Descarte sanitario", Datos.F("2026-11-05"), null);

        var lote = await e.LoteAsync(id);
        (lote.Estado, lote.CantidadActual, lote.ValorTotalContable).Should().Be((EstadoLote.Finalizado, 0, 0m));
        var otra = () => e.Servicio.RegistrarBajaAsync(id, 1, "Mortandad", Datos.F("2026-11-05"), null);
        await otra.Should().ThrowAsync<ValidationException>().WithMessage("*finalizado*");
        var etapa = () => e.Servicio.CambiarEtapaAsync(id, e.Postura, null);
        await etapa.Should().ThrowAsync<ValidationException>().WithMessage("*finalizado*");
    }

    [Theory]
    [InlineData(0, "Mortandad", "2026-11-05")]
    [InlineData(1001, "Mortandad", "2026-11-05")]
    [InlineData(5, "Robo", "2026-11-05")]
    [InlineData(5, "Mortandad", "2026-11-20")]
    [InlineData(5, "Mortandad", "2026-10-01")]
    public async Task Las_bajas_invalidas_se_rechazan_sin_cambiar_el_lote(int cantidad, string causa, string fecha)
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());

        var act = () => e.Servicio.RegistrarBajaAsync(id, cantidad, causa, Datos.F(fecha), null);

        await act.Should().ThrowAsync<ValidationException>();
        (await e.LoteAsync(id)).CantidadActual.Should().Be(1000);
    }

    // ---- Resumen y aislamiento ------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_resumen_suma_poblacion_mortandad_y_valor_de_los_lotes_activos()
    {
        await using var e = await CrearAsync();
        var uno = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra("L-1", 1000, 15_000m));
        await e.Servicio.RegistrarNuevoLoteAsync(e.Compra("L-2", 1500, 10_000m));
        await e.Servicio.RegistrarBajaAsync(uno, 50, "Mortandad", Datos.F("2026-11-05"), null);

        var resumen = ResumenLotes.De((await e.Servicio.ListarAsync(e.ProgramaId)).Select(d => d.Lote));

        (resumen.PoblacionViva, resumen.MortandadAcumulada, resumen.PoblacionInicial, resumen.LotesActivos).Should().Be((2450, 50, 2500, 2));
        resumen.ValorContable.Should().Be(14_250_000m + 15_000_000m);
        resumen.PorcentajeMortandad.Should().Be(2m);
        ResumenLotes.De([]).PorcentajeMortandad.Should().Be(0m);
    }

    [Fact]
    public async Task Los_lotes_no_escriben_en_el_Kardex_ni_alteran_la_invariante_de_los_animales()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());
        await e.Servicio.CambiarEtapaAsync(id, e.Postura, null);
        await e.Servicio.RegistrarBajaAsync(id, 5, "Hurto", Datos.F("2026-11-05"), null);
        await e.Servicio.DesdoblarAsync(id, 100, "L-AVE-01B", "Galpón 2");

        (await e.Db.EscalarAsync<long>("SELECT COUNT(*) FROM MOVIMIENTO_INVENTARIO")).Should().Be(0, "el Kardex de lotes todavía no existe");
        (await e.Db.EscalarAsync<long>("SELECT COUNT(*) FROM REMISION")).Should().Be(0);
    }

    // ---- Editar y anular la compra de un lote -------------------------------------------------------------------------------

    private static EditarCompraLoteDto Edicion(LotePoblacion l) => new()
    {
        LoteId = l.Id, Factura = l.OrigenReferencia!, OrdenCompra = l.OrdenCompra!, Proveedor = l.Proveedor!, FechaIngreso = l.FechaNacimientoIngreso,
        GranjaId = l.GranjaId, Ubicacion = l.Ubicacion, LineaRaza = l.LineaRaza, Cantidad = l.CantidadInicial, PrecioUnitarioFactura = l.ValorCompraUnitario,
        Observaciones = l.Observaciones,
    };

    [Fact]
    public async Task Sin_operaciones_se_corrigen_la_factura_los_datos_la_cantidad_y_el_precio_y_se_recalcula_el_valor()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra(cantidad: 1000, precio: 15_000m));
        var dto = Edicion(await e.LoteAsync(id));
        dto.Factura = "FAC-CORREGIDA";
        dto.Proveedor = "Otro proveedor";
        dto.Ubicacion = "Galpón 5";
        dto.Cantidad = 1200;
        dto.PrecioUnitarioFactura = 14_000m;

        await e.Servicio.EditarCompraAsync(dto);

        var lote = await e.LoteAsync(id);
        (lote.OrigenReferencia, lote.Proveedor, lote.Ubicacion).Should().Be(("FAC-CORREGIDA", "Otro proveedor", "Galpón 5"));
        (lote.CantidadInicial, lote.CantidadActual, lote.ValorCompraUnitario, lote.ValorContableUnitario, lote.ValorTotalContable)
            .Should().Be((1200, 1200, 14_000m, 14_000m, 16_800_000m));
    }

    [Fact]
    public async Task Con_operaciones_tambien_se_corrigen_la_cantidad_y_el_precio_conservando_las_bajas()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());
        await e.Servicio.RegistrarBajaAsync(id, 10, "Mortandad", Datos.F("2026-11-05"), null);

        var dto = Edicion(await e.LoteAsync(id));
        dto.Cantidad = 1200;
        dto.PrecioUnitarioFactura = 16_000m;
        await e.Servicio.EditarCompraAsync(dto);

        var lote = await e.LoteAsync(id);
        (lote.CantidadInicial, lote.MortandadAcumulada, lote.CantidadActual).Should().Be((1200, 10, 1190));
        (lote.ValorCompraUnitario, lote.ValorContableUnitario, lote.ValorTotalContable).Should().Be((16_000m, 16_000m, 19_040_000m));

        var pocas = Edicion(await e.LoteAsync(id));
        pocas.Cantidad = 5;   // menos que las bajas
        await ((Func<Task>)(() => e.Servicio.EditarCompraAsync(pocas))).Should().ThrowAsync<ValidationException>().WithMessage("*bajas*");
    }

    [Fact]
    public async Task Cambiar_el_precio_de_un_lote_ya_revalorizado_no_toca_su_valor_contable()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());
        await e.Servicio.CambiarEtapaAsync(id, e.Postura, null);   // valor contable 40.000

        var dto = Edicion(await e.LoteAsync(id));
        dto.PrecioUnitarioFactura = 12_000m;
        await e.Servicio.EditarCompraAsync(dto);

        var lote = await e.LoteAsync(id);
        (lote.ValorCompraUnitario, lote.ValorContableUnitario, lote.ValorTotalContable).Should().Be((12_000m, 40_000m, 40_000_000m));
    }

    [Fact]
    public async Task Cada_operacion_suma_una_operacion_al_lote()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());

        await e.Servicio.CambiarEtapaAsync(id, e.Postura, null);
        await e.Servicio.DesdoblarAsync(id, 100, "L-AVE-01B", "Galpón 2");
        await e.Servicio.RegistrarBajaAsync(id, 5, "Mortandad", Datos.F("2026-11-05"), null);

        (await e.LoteAsync(id)).Operaciones.Should().Be(3);
    }

    [Theory]
    [InlineData("factura")]
    [InlineData("precio")]
    [InlineData("fecha")]
    public async Task Los_datos_invalidos_de_la_correccion_se_rechazan_sin_cambiar_el_lote(string campo)
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());
        var dto = Edicion(await e.LoteAsync(id));
        if (campo == "factura") dto.Factura = " ";
        if (campo == "precio") dto.PrecioUnitarioFactura = 0;
        if (campo == "fecha") dto.FechaIngreso = Datos.F("2026-12-31");

        await ((Func<Task>)(() => e.Servicio.EditarCompraAsync(dto))).Should().ThrowAsync<ValidationException>();
        (await e.LoteAsync(id)).OrigenReferencia.Should().Be("FAC-77");
    }

    [Fact]
    public async Task Anular_la_compra_de_un_lote_sin_operaciones_lo_saca_de_los_listados_y_consume_su_codigo()
    {
        await using var e = await CrearAsync();
        var id = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra());

        await e.Servicio.AnularCompraAsync(id, "factura equivocada");

        var lote = await e.LoteAsync(id);
        (lote.Anulado, lote.Estado).Should().Be((true, EstadoLote.Finalizado));
        lote.Observaciones.Should().Contain("Compra anulada: factura equivocada");
        (await e.Servicio.ListarAsync(e.ProgramaId)).Should().BeEmpty();
        (await e.Servicio.ListarAsync(e.ProgramaId, incluirAnulados: true)).Should().ContainSingle();
        await ((Func<Task>)(() => e.Servicio.RegistrarNuevoLoteAsync(e.Compra()))).Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe un lote*");
        await ((Func<Task>)(() => e.Servicio.AnularCompraAsync(id, null))).Should().ThrowAsync<ValidationException>().WithMessage("*ya fue anulada*");
        await ((Func<Task>)(() => e.Servicio.RegistrarBajaAsync(id, 1, "Mortandad", Datos.F("2026-11-05"), null))).Should().ThrowAsync<ValidationException>().WithMessage("*anulada*");
    }

    [Fact]
    public async Task Las_bajas_y_los_cambios_de_etapa_no_impiden_anular_la_compra_pero_el_desdoble_si()
    {
        await using var e = await CrearAsync();
        var conBajas = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra("L-A"));
        await e.Servicio.CambiarEtapaAsync(conBajas, e.Postura, null);
        await e.Servicio.RegistrarBajaAsync(conBajas, 5, "Mortandad", Datos.F("2026-11-05"), null);
        await e.Servicio.AnularCompraAsync(conBajas, null);
        (await e.LoteAsync(conBajas)).Anulado.Should().BeTrue();

        var desdoblado = await e.Servicio.RegistrarNuevoLoteAsync(e.Compra("L-B"));
        await e.Servicio.DesdoblarAsync(desdoblado, 100, "L-B2", "Galpón 2");
        await ((Func<Task>)(() => e.Servicio.AnularCompraAsync(desdoblado, null))).Should().ThrowAsync<ValidationException>().WithMessage("*desdoblado, vendido o trasladado*");
    }

    [Fact]
    public async Task No_se_anula_la_compra_de_un_lote_que_no_fue_comprado()
    {
        await using var e = await CrearAsync();
        var dto = e.Compra("L-NAC", 100);
        dto.ClaseEntrada = ClaseEntrada.Nacimiento;
        var nacido = await e.Servicio.RegistrarNuevoLoteAsync(dto);
        await ((Func<Task>)(() => e.Servicio.AnularCompraAsync(nacido, null))).Should().ThrowAsync<ValidationException>().WithMessage("*no ingresó por compra*");
    }
}
