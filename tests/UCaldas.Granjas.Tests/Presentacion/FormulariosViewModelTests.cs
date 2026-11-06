using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>Formularios modales: nacimiento, compra, salida y traslado, con los servicios reales y la base temporal.</summary>
public class FormulariosViewModelTests
{
    private static ConsultasService Consultas(EscenarioKardex e) => new(e.Db.Uow, e.Reloj);

    private static async Task<AnimalSeleccionable> SeleccionableAsync(EscenarioKardex e, int granjaId, string chapeta)
        => (await Consultas(e).BuscarDisponiblesAsync(granjaId, chapeta)).Single(a => a.ChapetaAmarilla == chapeta);

    // ---- Nacimiento ---------------------------------------------------------------------------------------------------

    private static NacimientoViewModel CrearNacimiento(EscenarioKardex e, DialogoFalso dialogos, ContextoGlobal? contexto = null)
        => new(e.Kardex, new AnimalService(e.Db.Uow, e.Aging, e.Reloj), new CatalogoService(e.Db.Uow), contexto ?? new ContextoGlobal(), e.Reloj, dialogos);

    [Fact]
    public async Task El_nacimiento_muestra_en_vivo_la_categoria_y_el_valor_inicial_de_la_matriz()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var vm = CrearNacimiento(e, new DialogoFalso());

        (await vm.InicializarAsync()).Should().BeTrue();

        vm.GranjaSeleccionada!.Nombre.Should().Be("Tesorito");
        vm.OrientacionSeleccionada!.Nombre.Should().Be("Lácteo", "Tesorito sugiere la orientación láctea");
        vm.CategoriaInicial.Should().Be("Terneros");
        vm.ValorInicial.Should().Be("$150.000");

        vm.EsHembra = false;
        await Esperar.HastaAsync(() => vm.ValorInicial == "$160.000");
        vm.EsMacho.Should().BeTrue();

        vm.FechaNacimiento = Datos.F("2026-08-01"); // 3 meses → Terneros 3-7 meses, 800 mil
        await Esperar.HastaAsync(() => vm.ValorInicial == "$800.000");
    }

    [Fact]
    public async Task El_nacimiento_toma_la_granja_de_los_filtros_globales()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var vm = CrearNacimiento(e, new DialogoFalso(), new ContextoGlobal { GranjaId = e.Ids.Montelindo });

        await vm.InicializarAsync();

        vm.GranjaSeleccionada!.Nombre.Should().Be("Montelindo");
        vm.OrientacionSeleccionada!.Nombre.Should().Be("Carne/Levante", "Montelindo sugiere la orientación de carne");
    }

    [Fact]
    public async Task Guardar_un_nacimiento_registra_la_cria_con_valor_de_origen_cero_y_suma_un_parto_a_la_madre()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("0940-26", partos: 2);
        var dialogos = new DialogoFalso();
        var vm = CrearNacimiento(e, dialogos);
        await vm.InicializarAsync();
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;
        vm.ChapetaAmarilla = "N-5";
        vm.Nombre = "Brisa";
        vm.Raza = "Holstein";
        vm.PesoAlNacerKg = 38m;
        vm.MadreIdentificacion = "0940-26";

        await vm.GuardarCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        var cria = await e.AnimalAsync("N-5");
        cria.ValorCompraOrigen.Should().Be(0m);
        cria.ValorContableActual.Should().Be(150_000m);
        cria.ClaseEntrada.Should().Be(ClaseEntrada.Nacimiento);
        (await e.AnimalAsync("0940-26")).NumeroPartos.Should().Be(3);
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("Terneros").And.Contain("$150.000").And.Contain("Se sumó un parto a la madre");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_nacimiento_incompleto_muestra_el_mensaje_del_servicio_y_la_ventana_no_se_cierra()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dialogos = new DialogoFalso();
        var vm = CrearNacimiento(e, dialogos);
        await vm.InicializarAsync();
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;

        await vm.GuardarCommand.ExecuteAsync(null); // sin chapeta, raza ni madre

        dialogos.Advertencias.Should().ContainSingle().Which.Should().Contain("chapeta amarilla");
        cierres.Should().BeEmpty();
        (await e.ContarAsync("ANIMAL")).Should().Be(0);
    }

    [Fact]
    public async Task La_inseminacion_exige_la_raza_del_padre()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dialogos = new DialogoFalso();
        var vm = CrearNacimiento(e, dialogos);
        await vm.InicializarAsync();
        vm.ChapetaAmarilla = "N-6";
        vm.Raza = "Holstein";
        vm.MadreIdentificacion = "044";
        vm.EsInseminacion = true;
        vm.EsMontaDirecta.Should().BeFalse();

        await vm.GuardarCommand.ExecuteAsync(null);

        dialogos.Advertencias.Should().ContainSingle().Which.Should().Contain("padre");
    }

    // ---- Compra -------------------------------------------------------------------------------------------------------

    private static CompraEditorViewModel CrearCompra(EscenarioKardex e, DialogoFalso dialogos)
        => new(e.Kardex, Consultas(e), new CatalogoService(e.Db.Uow), new ContextoGlobal(), e.Reloj, dialogos);

    private static void LlenarCompra(CompraEditorViewModel vm, string factura, string chapeta, decimal precio)
    {
        vm.NumeroFactura = factura;
        vm.Proveedor = "Ganadería La Soledad";
        var fila = vm.Animales[0];
        fila.ChapetaAmarilla = chapeta;
        fila.Sexo = Sexo.Hembra;
        fila.FechaNacimiento = Datos.F("2022-03-01");
        fila.Raza = "Holstein";
        fila.NumeroPartos = 1;
        fila.PrecioFactura = precio;
    }

    [Fact]
    public async Task Una_compra_nueva_calcula_el_total_en_vivo_y_registra_el_costo_de_origen()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dialogos = new DialogoFalso();
        var vm = CrearCompra(e, dialogos);
        (await vm.InicializarNuevaAsync()).Should().BeTrue();
        vm.Modo.Should().Be(ModoDocumento.Nuevo);
        vm.Titulo.Should().Be("Nueva compra");
        vm.Animales.Should().HaveCount(1, "arranca con una fila vacía");
        LlenarCompra(vm, "FAC-9", "C-9", 3_225_970m);
        vm.AgregarAnimalCommand.Execute(null);
        vm.Animales[1].PrecioFactura = 774_030m;

        vm.Total.Should().Be(4_000_000m);
        vm.TotalTexto.Should().Be("$4.000.000");
        vm.CantidadTexto.Should().Be("Total animales: 2");

        vm.QuitarAnimalCommand.Execute(vm.Animales[1]);
        vm.TotalTexto.Should().Be("$3.225.970");

        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;
        await vm.GuardarCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        var animal = await e.AnimalAsync("C-9");
        animal.ValorCompraOrigen.Should().Be(3_225_970m, "el valor de la factura queda como costo de origen");
        animal.ValorContableActual.Should().Be(6_000_000m, "el valor contable sale de la matriz (vaca lechera)");
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("1 animal por $3.225.970");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Una_compra_sin_factura_se_rechaza_con_el_mensaje_del_servicio()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dialogos = new DialogoFalso();
        var vm = CrearCompra(e, dialogos);
        await vm.InicializarNuevaAsync();
        LlenarCompra(vm, string.Empty, "C-9", 3_000_000m);
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;

        await vm.GuardarCommand.ExecuteAsync(null);

        dialogos.Advertencias.Should().ContainSingle();
        cierres.Should().BeEmpty();
        (await e.ContarAsync("ANIMAL")).Should().Be(0);
    }

    [Fact]
    public async Task Una_compra_existente_se_abre_en_detalle_se_edita_y_se_guarda()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("P-1", Sexo.Hembra, "2022-03-01", 3_200_000m, partos: 1)));
        var remisionId = (await e.RemisionAsync("FAC-1")).Id;
        var dialogos = new DialogoFalso();
        var vm = CrearCompra(e, dialogos);

        (await vm.InicializarExistenteAsync(remisionId)).Should().BeTrue();

        vm.Modo.Should().Be(ModoDocumento.Detalle);
        vm.EsLectura.Should().BeTrue();
        vm.EsEditable.Should().BeFalse();
        vm.Titulo.Should().Be("Detalle de la compra FAC-1");
        vm.GuardarCommand.CanExecute(null).Should().BeFalse("en modo consulta no se guarda");
        vm.EditarCompraCommand.CanExecute(null).Should().BeTrue();
        vm.AnularCommand.CanExecute(null).Should().BeTrue();
        vm.Animales.Should().ContainSingle().Which.PrecioFactura.Should().Be(3_200_000m);

        vm.EditarCompraCommand.Execute(null);
        vm.Modo.Should().Be(ModoDocumento.Edicion);
        vm.EsEditable.Should().BeTrue();
        vm.Titulo.Should().Be("Editar compra FAC-1");
        vm.Animales[0].PrecioFactura = 3_300_000m;
        await vm.GuardarCommand.ExecuteAsync(null);

        (await e.AnimalAsync("P-1")).ValorCompraOrigen.Should().Be(3_300_000m);
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("Compra actualizada");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Anular_una_compra_pide_confirmacion_y_motivo_y_deja_el_numero_consumido()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("P-1", Sexo.Hembra, "2022-03-01", 3_200_000m, partos: 1)));
        var remisionId = (await e.RemisionAsync("FAC-1")).Id;
        var dialogos = new DialogoFalso { RespuestaConfirmar = true, RespuestaPedir = "Factura duplicada" };
        var vm = CrearCompra(e, dialogos);
        await vm.InicializarExistenteAsync(remisionId);
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;

        await vm.AnularCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        (await e.RemisionAsync("FAC-1")).Anulada.Should().BeTrue();
        dialogos.Confirmaciones.Should().ContainSingle().Which.Should().Contain("número de factura queda consumido");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Si_el_usuario_no_confirma_la_anulacion_no_se_hace_nada()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Kardex.RegistrarCompraAsync(e.Compra("FAC-1", e.AnimalComprado("P-1", Sexo.Hembra, "2022-03-01", 3_200_000m, partos: 1)));
        var remisionId = (await e.RemisionAsync("FAC-1")).Id;
        var dialogos = new DialogoFalso { RespuestaConfirmar = false, RespuestaPedir = "x" };
        var vm = CrearCompra(e, dialogos);
        await vm.InicializarExistenteAsync(remisionId);

        await vm.AnularCommand.ExecuteAsync(null);

        (await e.RemisionAsync("FAC-1")).Anulada.Should().BeFalse();
    }

    // ---- Salida -------------------------------------------------------------------------------------------------------

    private static SalidaEditorViewModel CrearSalida(EscenarioKardex e, DialogoFalso dialogos, FormulariosFalso formularios)
        => new(e.Kardex, Consultas(e), new CatalogoService(e.Db.Uow), formularios, new ContextoGlobal(), e.Reloj, dialogos);

    [Fact]
    public async Task La_clase_de_salida_decide_si_se_piden_oficio_y_recibo()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var vm = CrearSalida(e, new DialogoFalso(), new FormulariosFalso());
        await vm.InicializarNuevaAsync();

        vm.EsVenta.Should().BeTrue();
        vm.EtiquetaTotal.Should().Be("Total venta");
        vm.PuedeCambiarClase.Should().BeTrue();

        vm.ClaseSeleccionada = vm.Clases.Single(c => c.Valor == ClaseSalida.Muerte);

        vm.EsVenta.Should().BeFalse();
        vm.EsBaja.Should().BeTrue();
        vm.EtiquetaTotal.Should().Be("Valor en libros");
        vm.Clases.Select(c => c.Valor).Should().Equal(ClaseSalida.Venta, ClaseSalida.Muerte, ClaseSalida.Hurto, ClaseSalida.DescarteSanitario);
    }

    [Fact]
    public async Task Una_venta_sin_recibo_de_caja_se_rechaza_y_con_todos_los_datos_se_registra()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        var formularios = new FormulariosFalso();
        formularios.Seleccion.Add(await SeleccionableAsync(e, e.Ids.Tesorito, "A-1"));
        var dialogos = new DialogoFalso();
        var vm = CrearSalida(e, dialogos, formularios);
        await vm.InicializarNuevaAsync();
        await vm.AgregarAnimalCommand.ExecuteAsync(null);
        vm.Animales.Should().ContainSingle();
        vm.NumeroRemision = "REM-1";
        vm.NumeroOficio = "OF-1";
        vm.Comprador = "Frigorífico Manizales";
        vm.Animales[0].PrecioVenta = 6_500_000m;
        vm.TotalTexto.Should().Be("$6.500.000");
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;

        await vm.GuardarCommand.ExecuteAsync(null); // falta el recibo de caja

        dialogos.Advertencias.Should().ContainSingle();
        cierres.Should().BeEmpty();
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Disponible);

        vm.ReciboCaja = "RC-1";
        await vm.GuardarCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Vendido);
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("Venta registrada: 1 animal por $6.500.000").And.Contain("$6.000.000");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Una_baja_por_muerte_solo_lleva_remision_y_documento_de_soporte()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-2", valor: 3_750_000m);
        var formularios = new FormulariosFalso();
        formularios.Seleccion.Add(await SeleccionableAsync(e, e.Ids.Tesorito, "A-2"));
        var dialogos = new DialogoFalso();
        var vm = CrearSalida(e, dialogos, formularios);
        await vm.InicializarNuevaAsync();
        vm.ClaseSeleccionada = vm.Clases.Single(c => c.Valor == ClaseSalida.Muerte);
        await vm.AgregarAnimalCommand.ExecuteAsync(null);
        vm.NumeroRemision = "REM-B-1";
        vm.DocumentoSoporte = "Acta Sanitaria N°4";
        vm.TotalTexto.Should().Be("$3.750.000", "las bajas muestran el valor en libros");

        await vm.GuardarCommand.ExecuteAsync(null);

        (await e.AnimalAsync("A-2")).Estado.Should().Be(EstadoAnimal.Muerto);
        var remision = await e.RemisionAsync("REM-B-1");
        remision.OficioId.Should().BeNull();
        remision.ReciboCaja.Should().BeNull();
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("Baja registrada: 1 animal");
    }

    [Fact]
    public async Task El_selector_no_repite_animales_ya_agregados_y_cancelarlo_no_cambia_la_lista()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1");
        var formularios = new FormulariosFalso();
        formularios.Seleccion.Add(await SeleccionableAsync(e, e.Ids.Tesorito, "A-1"));
        var vm = CrearSalida(e, new DialogoFalso(), formularios);
        await vm.InicializarNuevaAsync();

        await vm.AgregarAnimalCommand.ExecuteAsync(null);
        await vm.AgregarAnimalCommand.ExecuteAsync(null);
        vm.Animales.Should().ContainSingle("el selector recibe los ya agregados como excluidos");
        formularios.Consultas.Should().OnlyContain(c => c.GranjaId == e.Ids.Tesorito);

        formularios.Cancelar = true;
        await vm.AgregarAnimalCommand.ExecuteAsync(null);
        vm.Animales.Should().ContainSingle();
        vm.CantidadTexto.Should().Be("Total Animales: 1");
    }

    [Fact]
    public async Task Una_venta_existente_se_abre_en_detalle_con_sus_precios_y_se_puede_anular()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        await e.Kardex.RegistrarVentaAsync(await e.VentaAsync("REM-1", "OF-1", "RC-1", null, ("A-1", 6_500_000m)));
        var remisionId = (await e.RemisionAsync("REM-1")).Id;
        var dialogos = new DialogoFalso { RespuestaConfirmar = true, RespuestaPedir = "Venta mal digitada" };
        var vm = CrearSalida(e, dialogos, new FormulariosFalso());

        (await vm.InicializarExistenteAsync(remisionId, ClaseSalida.Venta)).Should().BeTrue();

        vm.Modo.Should().Be(ModoDocumento.Detalle);
        vm.Titulo.Should().Be("Detalle de la salida REM-1");
        vm.NumeroOficio.Should().Be("OF-1");
        vm.ReciboCaja.Should().Be("RC-1");
        vm.PuedeCambiarClase.Should().BeFalse("una venta no se convierte en baja");
        vm.Animales.Should().ContainSingle().Which.PrecioVenta.Should().Be(6_500_000m);
        vm.TotalTexto.Should().Be("$6.500.000");

        await vm.AnularCommand.ExecuteAsync(null);

        (await e.RemisionAsync("REM-1")).Anulada.Should().BeTrue();
        (await e.AnimalAsync("A-1")).Estado.Should().Be(EstadoAnimal.Disponible);
        await e.AsegurarInvarianteAsync();
    }

    // ---- Traslado -----------------------------------------------------------------------------------------------------

    private static TrasladoEditorViewModel CrearTraslado(EscenarioKardex e, DialogoFalso dialogos, FormulariosFalso formularios)
        => new(e.Kardex, new CatalogoService(e.Db.Uow), formularios, new ContextoGlobal(), e.Reloj, dialogos);

    [Fact]
    public async Task El_traslado_propone_origen_y_destino_distintos_y_mueve_los_animales_con_su_valor()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-3", valor: 4_000_000m);
        var formularios = new FormulariosFalso();
        formularios.Seleccion.Add(await SeleccionableAsync(e, e.Ids.Tesorito, "A-3"));
        var dialogos = new DialogoFalso();
        var vm = CrearTraslado(e, dialogos, formularios);
        (await vm.InicializarAsync()).Should().BeTrue();
        vm.GranjaOrigen!.Nombre.Should().Be("Tesorito");
        vm.GranjaDestino!.Id.Should().NotBe(vm.GranjaOrigen.Id);
        vm.GranjaDestino = vm.Granjas.Single(g => g.Id == e.Ids.LaCruz);

        await vm.AgregarALaListaCommand.ExecuteAsync(null);
        vm.Animales.Should().ContainSingle();
        vm.CantidadTexto.Should().Be("Total Animales a Mover: 1");
        vm.ValorTexto.Should().Be("$4.000.000");
        vm.NumeroRemision = "REM-T-1";
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;
        await vm.ConfirmarCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        var animal = await e.AnimalAsync("A-3");
        animal.GranjaId.Should().Be(e.Ids.LaCruz);
        animal.ValorContableActual.Should().Be(4_000_000m);
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("1 animal de Tesorito a La Cruz");
        (await e.MovimientosDeRemisionAsync("REM-T-1")).Should().HaveCount(2, "un traslado genera dos asientos");
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Un_traslado_a_la_misma_granja_se_rechaza_con_el_mensaje_del_servicio()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-3");
        var formularios = new FormulariosFalso();
        formularios.Seleccion.Add(await SeleccionableAsync(e, e.Ids.Tesorito, "A-3"));
        var dialogos = new DialogoFalso();
        var vm = CrearTraslado(e, dialogos, formularios);
        await vm.InicializarAsync();
        vm.GranjaDestino = vm.GranjaOrigen;
        await vm.AgregarALaListaCommand.ExecuteAsync(null);
        vm.NumeroRemision = "REM-T-1";

        await vm.ConfirmarCommand.ExecuteAsync(null);

        dialogos.Advertencias.Should().ContainSingle();
        (await e.AnimalAsync("A-3")).GranjaId.Should().Be(e.Ids.Tesorito);
    }

    [Fact]
    public async Task El_traslado_usa_la_orientacion_sugerida_de_la_sede_destino_y_se_puede_cambiar_por_animal()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-3");
        var formularios = new FormulariosFalso();
        formularios.Seleccion.Add(await SeleccionableAsync(e, e.Ids.Tesorito, "A-3"));
        var vm = CrearTraslado(e, new DialogoFalso(), formularios);
        await vm.InicializarAsync();
        vm.GranjaDestino = vm.Granjas.Single(g => g.Id == e.Ids.Montelindo); // sugiere Carne/Levante

        await vm.AgregarALaListaCommand.ExecuteAsync(null);

        vm.Animales.Single().OrientacionDestinoId.Should().Be(e.Ids.Carne);
        vm.Animales.Single().Animal.Orientacion.Should().Be("Lácteo", "la orientación de origen no cambia");

        vm.Animales.Single().OrientacionDestinoId = e.Ids.Lacteo;
        vm.Animales.Single().OrientacionDestinoId.Should().Be(e.Ids.Lacteo);
    }
}
