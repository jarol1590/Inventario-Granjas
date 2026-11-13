using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.App.ViewModels.Configuracion;
using UCaldas.Granjas.App.ViewModels.Lotes;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>ViewModels de programas generalizados (tarifas de referencia, nacimiento y compra no bovinos) y del módulo de lotes.</summary>
public class ProgramasYLotesViewModelsTests
{
    private static async Task<Programa> CrearEquinosAsync(EscenarioKardex e)
        => await new CatalogoService(e.Db.Uow).GuardarProgramaAsync(
            new Programa { Codigo = "PRG-EQU", Nombre = "Equinos", TipoInventario = TipoInventario.Individual }, [e.Ids.Tesorito]);

    private static async Task<int> AgregarCategoriaAsync(EscenarioKardex e, int programaId, string nombre, decimal valor)
    {
        var servicio = new TarifaProgramaService(e.Db.Uow);
        var filas = (await servicio.ListarAsync(programaId)).ToList();
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = nombre, UnidadCobro = "UNIDAD", ValorReferencia = valor });
        await servicio.GuardarAsync(programaId, filas);
        return (await servicio.ListarAsync(programaId)).Single(f => f.NombreCategoria == nombre).CategoriaId;
    }

    // ---- Tarifas simples ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task La_tabla_simple_carga_agrega_guarda_y_protege_la_categoria_General()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var equinos = await CrearEquinosAsync(e);
        var dialogos = new DialogoFalso();
        var vm = new TarifasSimplesViewModel(new TarifaProgramaService(e.Db.Uow), dialogos);

        await vm.CargarAsync(equinos);
        vm.Filas.Should().ContainSingle().Which.EsDefault.Should().BeTrue();
        vm.HayCambios.Should().BeFalse();
        vm.GuardarCommand.CanExecute(null).Should().BeFalse();

        vm.QuitarFilaCommand.Execute(vm.Filas[0]);
        dialogos.UltimaAdvertencia.Should().Contain("no se puede eliminar");
        vm.Filas.Should().HaveCount(1);

        vm.AgregarFilaCommand.Execute(null);
        vm.Filas[1].NombreCategoria = "Potro";
        vm.Filas[1].ValorReferencia = 2_500_000m;
        vm.HayCambios.Should().BeTrue();
        await vm.GuardarCommand.ExecuteAsync(null);

        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("se guardaron");
        vm.HayCambios.Should().BeFalse();
        vm.Filas.Select(f => f.NombreCategoria).Should().Equal("General", "Potro");
    }

    [Fact]
    public async Task Una_categoria_invalida_se_muestra_como_advertencia_y_el_reajuste_cambia_los_valores()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var equinos = await CrearEquinosAsync(e);
        await AgregarCategoriaAsync(e, equinos.Id, "Potro", 1_000_000m);
        var dialogos = new DialogoFalso { RespuestaPedir = "10" };
        var vm = new TarifasSimplesViewModel(new TarifaProgramaService(e.Db.Uow), dialogos);
        await vm.CargarAsync(equinos);

        vm.ReajustarCommand.Execute(null);
        vm.Filas.Single(f => f.NombreCategoria == "Potro").ValorReferencia.Should().Be(1_100_000m);

        vm.AgregarFilaCommand.Execute(null);   // sin nombre
        await vm.GuardarCommand.ExecuteAsync(null);
        dialogos.UltimaAdvertencia.Should().Contain("nombre");
    }

    [Fact]
    public async Task La_pantalla_de_tarifas_tiene_una_pestana_por_programa_y_la_ganaderia_conserva_la_matriz()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var dialogos = new DialogoFalso();
        var vm = new TarifasViewModel(new VigenciaService(e.Db.Uow, new TarifaValidationService()), new CatalogoService(e.Db.Uow), e.Aging, e.Reloj,
            new TarifaProgramaService(e.Db.Uow), dialogos);

        await vm.CargarAsync();

        vm.ProgramasTarifa.Select(p => p.Programa.Codigo).Should().Equal("PRG-AVE", "PRG-GAN");
        vm.ProgramaTarifa!.Programa.Codigo.Should().Be("PRG-GAN", "abre en la ganadería");
        (vm.EsBovino, vm.NoEsBovino).Should().Be((true, false));
        vm.Filas.Should().HaveCount(10);
        vm.Categorias.Should().NotContain(c => c.Nombre == "General", "la matriz por edades solo ofrece categorías bovinas");

        vm.ProgramaTarifa = vm.ProgramasTarifa.Single(p => p.Programa.Codigo == "PRG-AVE");
        await Esperar.HastaAsync(() => Task.FromResult(vm.Simples.Filas.Count == 1));

        (vm.EsBovino, vm.NoEsBovino).Should().Be((false, true));
        vm.Simples.Filas.Single().NombreCategoria.Should().Be("General");
    }

    // ---- Nacimiento y compra no bovinos -----------------------------------------------------------------------------------

    [Fact]
    public async Task El_nacimiento_de_un_equino_pide_categoria_oculta_la_orientacion_y_valora_con_la_referencia()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var equinos = await CrearEquinosAsync(e);
        await AgregarCategoriaAsync(e, equinos.Id, "Potro", 2_500_000m);
        var dialogos = new DialogoFalso();
        var vm = new NacimientoViewModel(e.Kardex, new AnimalService(e.Db.Uow, e.Aging, e.Reloj), new CatalogoService(e.Db.Uow),
            new ContextoGlobal { ProgramaId = equinos.Id }, e.Reloj, dialogos);

        (await vm.InicializarAsync()).Should().BeTrue();

        vm.ProgramaSeleccionado!.Codigo.Should().Be("PRG-EQU");
        (vm.EsBovino, vm.NoEsBovino).Should().Be((false, true));
        vm.Categorias.Select(c => c.Nombre).Should().Equal("General", "Potro");
        vm.CategoriaSeleccionada = vm.Categorias.Single(c => c.Nombre == "Potro");
        await Esperar.HastaAsync(() => Task.FromResult(vm.ValorInicial.Contains("2.500.000")));
        vm.ChapetaAmarilla = "EQ-01";
        vm.Raza = "Criollo colombiano";

        await vm.GuardarCommand.ExecuteAsync(null);

        dialogos.Errores.Should().BeEmpty();
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("Potro");
        var animal = await e.AnimalAsync("EQ-01");
        (animal.ValorCompraOrigen, animal.ValorContableActual).Should().Be((0m, 2_500_000m));
        await e.AsegurarInvarianteAsync();
    }

    [Fact]
    public async Task Cambiar_a_un_programa_bovino_en_el_nacimiento_vuelve_a_pedir_orientacion()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await CrearEquinosAsync(e);
        var vm = new NacimientoViewModel(e.Kardex, new AnimalService(e.Db.Uow, e.Aging, e.Reloj), new CatalogoService(e.Db.Uow),
            new ContextoGlobal(), e.Reloj, new DialogoFalso());
        await vm.InicializarAsync();

        vm.ProgramaSeleccionado!.Codigo.Should().Be("PRG-GAN", "sin filtro global se abre en la ganadería");
        vm.EsBovino.Should().BeTrue();
        vm.Programas.Select(p => p.Codigo).Should().Equal("PRG-EQU", "PRG-GAN").And.NotContain("PRG-AVE", "los lotes no son animales");
    }

    [Fact]
    public async Task La_compra_de_un_equino_toma_la_categoria_de_la_fila_y_entra_al_costo_de_la_factura()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var equinos = await CrearEquinosAsync(e);
        var dialogos = new DialogoFalso();
        var consultas = new ConsultasService(e.Db.Uow, e.Reloj);
        var vm = new CompraEditorViewModel(e.Kardex, consultas, new CatalogoService(e.Db.Uow), new LoteService(e.Db.Uow, e.Reloj), new ContextoGlobal { ProgramaId = equinos.Id }, e.Reloj, dialogos);

        (await vm.InicializarNuevaAsync()).Should().BeTrue();

        vm.NoEsBovino.Should().BeTrue();
        vm.Animales[0].CategoriaId.Should().BeGreaterThan(0, "la fila nace con la categoría General");
        vm.NumeroFactura = "FAC-EQ-9";
        vm.Proveedor = "Haras La Esperanza";
        var fila = vm.Animales[0];
        fila.ChapetaAmarilla = "EQ-77";
        fila.Raza = "Criollo";
        fila.FechaNacimiento = Datos.F("2022-05-01");
        fila.PrecioFactura = 6_500_000m;

        await vm.GuardarCommand.ExecuteAsync(null);

        dialogos.Advertencias.Should().BeEmpty();
        var animal = await e.AnimalAsync("EQ-77");
        (animal.ValorCompraOrigen, animal.ValorContableActual).Should().Be((6_500_000m, 6_500_000m));
        animal.CategoriaId.Should().Be(fila.CategoriaId);
        await e.AsegurarInvarianteAsync();

        // Al abrir la compra ya guardada se reconoce su programa.
        var remisionId = (await e.RemisionAsync("FAC-EQ-9")).Id;
        var detalle = new CompraEditorViewModel(e.Kardex, consultas, new CatalogoService(e.Db.Uow), new LoteService(e.Db.Uow, e.Reloj), new ContextoGlobal(), e.Reloj, new DialogoFalso());
        await detalle.InicializarExistenteAsync(remisionId);
        detalle.ProgramaSeleccionado!.Codigo.Should().Be("PRG-EQU");
        detalle.NoEsBovino.Should().BeTrue();
    }

    // ---- Lotes -------------------------------------------------------------------------------------------------------------

    private sealed class EntornoLotes : IAsyncDisposable
    {
        public required BaseDeDatosDePrueba Db { get; init; }
        public required IdsBase Ids { get; init; }
        public required Programa Aves { get; init; }
        public required LoteService Lotes { get; init; }
        public required CatalogoService Catalogo { get; init; }
        public required TarifaProgramaService Tarifas { get; init; }
        public required RelojFijo Reloj { get; init; }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private static async Task<EntornoLotes> CrearLotesAsync()
    {
        var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var aves = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!;
        var tarifas = new TarifaProgramaService(db.Uow);
        var filas = (await tarifas.ListarAsync(aves.Id)).ToList();
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = "Cría", UnidadCobro = "UNIDAD", ValorReferencia = 15_000m });
        filas.Add(new TarifaCategoriaPrograma { NombreCategoria = "Postura", UnidadCobro = "UNIDAD", ValorReferencia = 40_000m });
        await tarifas.GuardarAsync(aves.Id, filas);
        var reloj = new RelojFijo(Datos.F("2026-11-06"));
        return new EntornoLotes
        {
            Db = db, Ids = ids, Aves = aves, Reloj = reloj, Tarifas = tarifas,
            Lotes = new LoteService(db.Uow, reloj), Catalogo = new CatalogoService(db.Uow),
        };
    }

    private static NuevoLoteViewModel NuevoLote(EntornoLotes e, DialogoFalso dialogos)
        => new(e.Lotes, e.Catalogo, e.Tarifas, e.Reloj, dialogos, e.Aves);

    [Fact]
    public async Task Registrar_un_lote_comprado_desde_la_ventana_lo_crea_y_cierra()
    {
        await using var e = await CrearLotesAsync();
        var dialogos = new DialogoFalso();
        var vm = NuevoLote(e, dialogos);
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;
        (await vm.InicializarAsync()).Should().BeTrue();
        vm.Granjas.Select(g => g.Nombre).Should().Equal(["Montelindo"], "solo las granjas donde corre el programa");
        vm.Etapas.Select(x => x.Nombre).Should().Equal("General", "Cría", "Postura");

        vm.CodigoLote = "L-AVE-01";
        vm.Ubicacion = "Galpón 1";
        vm.LineaRaza = "Hy-Line Brown";
        vm.EtapaSeleccionada = vm.Etapas.Single(x => x.Nombre == "Cría");
        vm.Cantidad = 1000;
        vm.PrecioUnitario = 15_000m;
        vm.Factura = "FAC-1";
        vm.OrdenCompra = "OC-1";
        vm.Proveedor = "Incubadora";
        await vm.GuardarCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        (await e.Lotes.ListarAsync(e.Aves.Id)).Should().ContainSingle().Which.Lote.ValorTotalContable.Should().Be(15_000_000m);
    }

    [Fact]
    public async Task Un_nacimiento_muestra_el_valor_de_referencia_de_la_etapa_y_un_dato_invalido_se_advierte()
    {
        await using var e = await CrearLotesAsync();
        var dialogos = new DialogoFalso();
        var vm = NuevoLote(e, dialogos);
        await vm.InicializarAsync();
        vm.EsCompra.Should().BeTrue();

        vm.EsNacimiento = true;
        vm.EtapaSeleccionada = vm.Etapas.Single(x => x.Nombre == "Postura");
        await Esperar.HastaAsync(() => Task.FromResult(vm.ValorInicialTexto.Contains("40.000")));

        await vm.GuardarCommand.ExecuteAsync(null);   // faltan el código, la ubicación...
        dialogos.UltimaAdvertencia.Should().Contain("código");
        vm.SinControlNumerico = true;
        vm.LlevaConteo.Should().BeFalse();
    }

    [Fact]
    public async Task La_pestana_de_lotes_muestra_kpis_filtra_y_abre_las_ventanas()
    {
        await using var e = await CrearLotesAsync();
        var (uno, _) = await LoteBaseAsync(e);
        await e.Lotes.RegistrarBajaAsync(uno, 58, "Mortandad", Datos.F("2026-11-05"), null);
        var dialogos = new DialogoFalso();
        var shell = new LotesViewModel(e.Lotes, e.Catalogo, e.Tarifas, e.Reloj, dialogos);

        await shell.CargarAsync();

        shell.HayProgramas.Should().BeTrue();
        shell.MostrarFiltroDePrograma.Should().BeFalse();
        var tab = shell.ProgramaActivo!;
        tab.Lotes.Should().ContainSingle();
        tab.PoblacionTexto.Should().Contain("942");
        tab.MortandadTexto.Should().Contain("58").And.Contain("5,8");
        tab.SaldoTexto.Should().Be("$14.130.000");

        tab.Texto = "no-existe";
        tab.Lotes.Should().BeEmpty();
        tab.Texto = "galpón";
        tab.Lotes.Should().ContainSingle("busca también por ubicación");
        tab.EstadoSeleccionado = tab.Estados.Single(s => s.Valor == EstadoLote.Finalizado);
        tab.Lotes.Should().BeEmpty();
        tab.EstadoSeleccionado = tab.Estados[0];

        tab.VerFichaCommand.CanExecute(null).Should().BeFalse("no hay lote seleccionado");
        tab.Seleccionado = tab.Lotes[0];
        await tab.VerFichaCommand.ExecuteAsync(null);
        dialogos.Mostrados.Should().ContainSingle().Which.Should().BeOfType<FichaLoteViewModel>();

        dialogos.AlMostrar = _ => false;
        await tab.RegistrarBajaCommand.ExecuteAsync(null);
        await tab.CambiarEtapaCommand.ExecuteAsync(null);
        await tab.NuevoLoteCommand.ExecuteAsync(null);
        dialogos.Mostrados.Select(m => m.GetType()).Should().Equal(
            typeof(FichaLoteViewModel), typeof(BajaLoteViewModel), typeof(CambioEtapaDesdobleViewModel), typeof(NuevoLoteViewModel));
    }

    private static async Task<(int LoteId, LoteDetalle Detalle)> LoteBaseAsync(EntornoLotes e)
    {
        var etapa = (await e.Catalogo.GetCategoriasDeProgramaAsync(e.Aves.Id)).Single(c => c.Nombre == "Cría").Id;
        var id = await e.Lotes.RegistrarNuevoLoteAsync(new NuevoLoteDto
        {
            ProgramaId = e.Aves.Id, GranjaId = e.Ids.Montelindo, EtapaId = etapa, ClaseEntrada = ClaseEntrada.Compra, CodigoLote = "L-1",
            Ubicacion = "Galpón 1", LineaRaza = "Hy-Line", FechaIngreso = Datos.F("2026-11-02"), Cantidad = 1000, PrecioUnitarioFactura = 15_000m,
            Factura = "F", OrdenCompra = "O", Proveedor = "P",
        });
        return (id, await e.Lotes.ObtenerAsync(id));
    }

    [Fact]
    public async Task La_ventana_de_baja_recalcula_el_saldo_al_instante_y_registra_la_baja()
    {
        await using var e = await CrearLotesAsync();
        var (id, detalle) = await LoteBaseAsync(e);
        var dialogos = new DialogoFalso();
        var vm = new BajaLoteViewModel(e.Lotes, e.Reloj, dialogos, detalle);
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;

        vm.SaldoTexto.Should().Be("—");
        vm.Cantidad = 100;
        vm.SaldoTexto.Should().Contain("900").And.Contain("$13.500.000");
        vm.Cantidad = 1000;
        vm.SaldoTexto.Should().Contain("Finalizado");
        vm.Cantidad = 2000;
        vm.SaldoTexto.Should().Be("—");

        vm.Cantidad = 2000;
        await vm.GuardarCommand.ExecuteAsync(null);
        dialogos.UltimaAdvertencia.Should().Contain("solo tiene 1.000");
        cierres.Should().BeEmpty();

        vm.Cantidad = 40;
        await vm.GuardarCommand.ExecuteAsync(null);
        cierres.Should().Equal(true);
        (await e.Lotes.ObtenerAsync(id)).Lote.CantidadActual.Should().Be(960);
    }

    [Fact]
    public async Task La_ventana_de_etapa_y_desdoble_promueve_informa_el_ajuste_y_desdobla()
    {
        await using var e = await CrearLotesAsync();
        var (id, detalle) = await LoteBaseAsync(e);
        var dialogos = new DialogoFalso();
        var vm = new CambioEtapaDesdobleViewModel(e.Lotes, e.Catalogo, dialogos, detalle);
        await vm.InicializarAsync();
        vm.EtapaDestino!.Nombre.Should().Be("Cría", "parte de la etapa actual");
        vm.PuedeDesdoblar.Should().BeTrue();

        vm.EtapaDestino = vm.Etapas.Single(x => x.Nombre == "Postura");
        await vm.GuardarCommand.ExecuteAsync(null);
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("+$25.000.000");
        (await e.Lotes.ObtenerAsync(id)).Lote.ValorTotalContable.Should().Be(40_000_000m);

        var desdoble = new CambioEtapaDesdobleViewModel(e.Lotes, e.Catalogo, dialogos, await e.Lotes.ObtenerAsync(id));
        desdoble.EsDesdoble = true;
        desdoble.Cantidad = 300;
        desdoble.CodigoNuevoLote = "L-1B";
        desdoble.UbicacionNueva = "Galpón 2";
        await desdoble.GuardarCommand.ExecuteAsync(null);
        (await e.Lotes.ListarAsync(e.Aves.Id)).Should().HaveCount(2);
    }

    private static async Task<FichaLoteViewModel> FichaAsync(EntornoLotes e, int id, DialogoFalso? dialogos = null)
    {
        var ficha = new FichaLoteViewModel(e.Lotes, e.Catalogo, dialogos ?? new DialogoFalso(), await e.Lotes.ObtenerAsync(id));
        (await ficha.InicializarAsync()).Should().BeTrue();
        return ficha;
    }

    [Fact]
    public async Task La_ficha_del_lote_resume_sus_datos_y_anotaciones()
    {
        await using var e = await CrearLotesAsync();
        var (id, _) = await LoteBaseAsync(e);
        await e.Lotes.RegistrarBajaAsync(id, 12, "Mortandad", Datos.F("2026-11-05"), null);

        var ficha = await FichaAsync(e, id);

        ficha.Titulo.Should().Be("Ficha del lote L-1");
        (ficha.Entrada, ficha.GranjaSeleccionada!.Nombre, ficha.EtapaSeleccionada!.Nombre, ficha.EstadoTexto).Should().Be(("Compra", "Montelindo", "Cría", "Activo"));
        (ficha.CantidadInicial, ficha.Bajas, ficha.PoblacionActualTexto).Should().Be((1000, 12, "988"));
        (ficha.ValorCompra, ficha.ValorContable, ficha.ValorTotalTexto).Should().Be(("$15.000", 15_000m, "$14.820.000"));
        ficha.Anotaciones.Should().Contain("Mortandad: 12 cabezas");
        (ficha.HayCambios, ficha.EsEditable, ficha.EsCompra).Should().Be((false, true, true));
    }

    [Fact]
    public async Task La_ficha_del_lote_se_edita_y_guarda_solo_con_cambios()
    {
        await using var e = await CrearLotesAsync();
        var (id, _) = await LoteBaseAsync(e);
        var dialogos = new DialogoFalso();
        var ficha = await FichaAsync(e, id, dialogos);
        var cierres = new List<bool>();
        ficha.CierreSolicitado += cierres.Add;

        ficha.GuardarCambiosCommand.CanExecute(null).Should().BeFalse("no hay cambios");
        ficha.Ubicacion = "Galpón 7";
        ficha.Anotaciones = "Revisar ventilación";
        ficha.HayCambios.Should().BeTrue();
        ficha.GuardarCambiosCommand.CanExecute(null).Should().BeTrue();

        await ficha.GuardarCambiosCommand.ExecuteAsync(null);

        dialogos.Informaciones.Should().ContainSingle();
        (ficha.HayCambios, ficha.HuboCambios).Should().Be((false, true));
        var guardado = (await e.Lotes.ObtenerAsync(id)).Lote;
        (guardado.Ubicacion, guardado.Observaciones).Should().Be(("Galpón 7", "Revisar ventilación"));
        (guardado.CantidadActual, guardado.ValorTotalContable).Should().Be((1000, 15_000_000m), "no se tocó la población ni el valor");

        ficha.CodigoLote = "";
        await ficha.GuardarCambiosCommand.ExecuteAsync(null);
        dialogos.UltimaAdvertencia.Should().Contain("código del lote");

        ficha.CerrarCommand.Execute(null);
        cierres.Should().Equal(true);
    }

    [Fact]
    public async Task La_ficha_del_lote_edita_poblacion_etapa_y_valor_y_recalcula_en_vivo()
    {
        await using var e = await CrearLotesAsync();
        var (id, _) = await LoteBaseAsync(e);
        var dialogos = new DialogoFalso();
        var ficha = await FichaAsync(e, id, dialogos);

        ficha.CantidadInicial = 900;
        ficha.Bajas = 50;
        ficha.PoblacionActualTexto.Should().Be("850", "inicial 900 − bajas 50");
        ficha.ValorContable = 20_000m;
        ficha.ValorTotalTexto.Should().Be("$17.000.000");
        ficha.EtapaSeleccionada = ficha.Etapas.Single(x => x.Nombre == "Postura");
        ficha.HayCambios.Should().BeTrue();

        await ficha.GuardarCambiosCommand.ExecuteAsync(null);

        dialogos.Informaciones.Should().ContainSingle();
        var lote = (await e.Lotes.ObtenerAsync(id)).Lote;
        (lote.CantidadInicial, lote.MortandadAcumulada, lote.CantidadActual, lote.ValorContableUnitario, lote.ValorTotalContable)
            .Should().Be((900, 50, 850, 20_000m, 17_000_000m));
        lote.ValorCompraUnitario.Should().Be(15_000m, "el valor de compra no se edita desde la ficha");
        ficha.HayCambios.Should().BeFalse();

        ficha.CantidadInicial = 0;
        ficha.Bajas = 0;
        ficha.EstadoTexto.Should().Be(EstadoLote.Finalizado, "sin cabezas el lote queda finalizado");
    }

    [Fact]
    public async Task La_ficha_de_un_lote_con_la_compra_anulada_no_se_puede_guardar()
    {
        await using var e = await CrearLotesAsync();
        var (id, _) = await LoteBaseAsync(e);
        await e.Lotes.AnularCompraAsync(id, "error");

        var ficha = await FichaAsync(e, id);

        ficha.EsEditable.Should().BeFalse();
        ficha.Ubicacion = "Otro";
        ficha.GuardarCambiosCommand.CanExecute(null).Should().BeFalse();
    }

    // ---- Compra de un lote en la pantalla de detalle de compras -----------------------------------------------------------

    private static async Task<(CompraEditorViewModel Vm, DialogoFalso Dialogos, EntornoLotes E, int Id)> CompraDeLoteAsync()
    {
        var e = await CrearLotesAsync();
        var (id, _) = await LoteBaseAsync(e);
        var dialogos = new DialogoFalso();
        var vm = new CompraEditorViewModel(new KardexService(e.Db.Uow, new AgingEngine(e.Db.Uow, e.Reloj), new DocumentValidationService(), e.Reloj), new ConsultasService(e.Db.Uow, e.Reloj), e.Catalogo, e.Lotes, new ContextoGlobal(), e.Reloj, dialogos);
        return (vm, dialogos, e, id);
    }

    [Fact]
    public async Task El_detalle_de_la_compra_de_un_lote_usa_la_pantalla_de_compras_y_permite_editarla()
    {
        var (vm, dialogos, e, id) = await CompraDeLoteAsync();
        await using var _ = e;
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;

        (await vm.InicializarLoteAsync(id)).Should().BeTrue();

        (vm.EsLote, vm.EsIndividual, vm.Modo, vm.Titulo).Should().Be((true, false, ModoDocumento.Detalle, "Detalle de la compra F"));
        vm.NoEsBovino.Should().BeTrue();
        (vm.NumeroOrden, vm.Proveedor, vm.GranjaSeleccionada!.Nombre).Should().Be(("O", "P", "Montelindo"));
        var fila = vm.Lotes.Single();
        (fila.Codigo, fila.Etapa, fila.Cantidad, fila.PrecioUnitario, fila.PuedeCorregirCantidad).Should().Be(("L-1", "Cría", 1000, 15_000m, true));
        vm.TotalTexto.Should().Be("$15.000.000");
        vm.MostrarAgregarAnimal.Should().BeFalse();

        vm.EditarCompraCommand.Execute(null);
        vm.EsEditable.Should().BeTrue();
        fila.Cantidad = 1100;
        vm.TotalTexto.Should().Be("$16.500.000");
        vm.Proveedor = "Proveedor nuevo";
        await vm.GuardarCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        var lote = (await e.Lotes.ObtenerAsync(id)).Lote;
        (lote.CantidadInicial, lote.Proveedor).Should().Be((1100, "Proveedor nuevo"));
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("L-1");
    }

    [Fact]
    public async Task Con_bajas_la_compra_de_un_lote_sigue_editable_y_se_puede_quitar()
    {
        var (vm, dialogos, e, id) = await CompraDeLoteAsync();
        await using var _ = e;
        await e.Lotes.RegistrarBajaAsync(id, 10, "Mortandad", Datos.F("2026-11-05"), null);
        await vm.InicializarLoteAsync(id);
        vm.EditarCompraCommand.Execute(null);

        var fila = vm.Lotes.Single();
        fila.PuedeCorregirCantidad.Should().BeTrue();
        fila.Cantidad = 1200;
        fila.PrecioUnitario = 16_000m;
        await vm.GuardarCommand.ExecuteAsync(null);
        var lote = (await e.Lotes.ObtenerAsync(id)).Lote;
        (lote.CantidadInicial, lote.CantidadActual, lote.ValorCompraUnitario, lote.ValorTotalContable).Should().Be((1200, 1190, 16_000m, 19_040_000m));

        // Quitar el lote = anular la compra (bajas y etapa no lo impiden).
        await vm.InicializarLoteAsync(id);
        vm.EditarCompraCommand.Execute(null);
        vm.QuitarLoteCommand.CanExecute(null).Should().BeTrue();
        dialogos.RespuestaPedir = "error de digitación";
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;

        await vm.QuitarLoteCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        (await e.Lotes.ObtenerAsync(id)).Lote.Anulado.Should().BeTrue();
    }

    [Fact]
    public async Task Un_lote_desdoblado_no_se_puede_quitar_de_la_compra()
    {
        var (vm, dialogos, e, id) = await CompraDeLoteAsync();
        await using var _ = e;
        await e.Lotes.DesdoblarAsync(id, 300, "L-1B", "Galpón 2");
        await vm.InicializarLoteAsync(id);
        vm.EditarCompraCommand.Execute(null);
        dialogos.RespuestaPedir = "motivo";

        await vm.QuitarLoteCommand.ExecuteAsync(null);

        dialogos.UltimaAdvertencia.Should().Contain("desdoblado, vendido o trasladado");
        (await e.Lotes.ObtenerAsync(id)).Lote.Anulado.Should().BeFalse();
    }

    [Fact]
    public async Task Anular_desde_el_detalle_anula_la_compra_de_un_lote()
    {
        var (otro, dialogos2, e2, id2) = await CompraDeLoteAsync();
        await using var _2 = e2;
        await otro.InicializarLoteAsync(id2);
        dialogos2.RespuestaPedir = "error de digitación";
        var cierres = new List<bool>();
        otro.CierreSolicitado += cierres.Add;

        await otro.AnularCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        (await e2.Lotes.ObtenerAsync(id2)).Lote.Anulado.Should().BeTrue();
    }

    // ---- Salidas de lotes en las pantallas de salida y de traslado --------------------------------------------------------

    private static async Task<(EntornoLotes E, KardexService Kardex, ConsultasService Consultas, ContextoGlobal Contexto, int LoteId)> EntornoDeSalidasAsync()
    {
        var e = await CrearLotesAsync();
        var (id, _) = await LoteBaseAsync(e);
        var kardex = new KardexService(e.Db.Uow, new AgingEngine(e.Db.Uow, e.Reloj), new DocumentValidationService(), e.Reloj);
        return (e, kardex, new ConsultasService(e.Db.Uow, e.Reloj), new ContextoGlobal { ProgramaId = e.Aves.Id, GranjaId = e.Ids.Montelindo }, id);
    }

    [Fact]
    public async Task La_salida_de_un_programa_por_lotes_vende_lotes_y_el_detalle_se_edita_y_anula()
    {
        var (e, kardex, consultas, contexto, loteId) = await EntornoDeSalidasAsync();
        await using var _ = e;
        var dialogos = new DialogoFalso();
        var vm = new SalidaEditorViewModel(kardex, consultas, e.Catalogo, e.Lotes, new FormulariosFalso(), contexto, e.Reloj, dialogos);
        var cierres = new List<bool>();
        vm.CierreSolicitado += cierres.Add;

        (await vm.InicializarNuevaAsync()).Should().BeTrue();

        (vm.EsLote, vm.EsIndividual, vm.PuedeCambiarClase).Should().Be((true, false, false));
        vm.LotesDisponibles.Select(l => l.Codigo).Should().Equal("L-1");
        vm.AgregarLoteCommand.Execute(null);
        vm.Lotes.Should().ContainSingle();
        vm.LotesDisponibles.Should().BeEmpty();
        vm.CantidadTexto.Should().Be("Total cabezas: 1.000");

        var fila = vm.Lotes[0];
        fila.Cantidad = 400;
        fila.PesoKg = 900m;
        fila.ValorVenta = 20_000_000m;
        vm.NumeroOficio = "OF-9";
        vm.NumeroRemision = "REM-9";
        vm.ReciboCaja = "RC-9";
        vm.Comprador = "Frigorífico";
        await vm.GuardarCommand.ExecuteAsync(null);

        cierres.Should().Equal(true);
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("Venta de lotes registrada").And.Contain("400");
        (await e.Lotes.ObtenerAsync(loteId)).Lote.CantidadActual.Should().Be(600);

        // Detalle: la misma pantalla, editable y anulable.
        var remisionId = (await consultas.ListarSalidasAsync(new SalidaFiltro(null, new DateTime(2026, 10, 1), new DateTime(2026, 11, 30), ProgramaId: e.Aves.Id))).Items.Single().RemisionId;
        var detalle = new SalidaEditorViewModel(kardex, consultas, e.Catalogo, e.Lotes, new FormulariosFalso(), new ContextoGlobal(), e.Reloj, dialogos);
        await detalle.InicializarExistenteAsync(remisionId, ClaseSalida.Venta);
        (detalle.EsLote, detalle.Modo, detalle.NumeroRemision).Should().Be((true, ModoDocumento.Detalle, "REM-9"));
        detalle.Lotes.Single().Cantidad.Should().Be(400);

        detalle.EditarSalidaCommand.Execute(null);
        detalle.Lotes[0].Cantidad = 500;
        detalle.Lotes[0].ValorVenta = 25_000_000m;
        await detalle.GuardarCommand.ExecuteAsync(null);
        (await e.Lotes.ObtenerAsync(loteId)).Lote.CantidadActual.Should().Be(500);

        var otro = new SalidaEditorViewModel(kardex, consultas, e.Catalogo, e.Lotes, new FormulariosFalso(), new ContextoGlobal(), e.Reloj, dialogos);
        await otro.InicializarExistenteAsync(remisionId, ClaseSalida.Venta);
        dialogos.RespuestaPedir = "error";
        await otro.AnularCommand.ExecuteAsync(null);
        (await e.Lotes.ObtenerAsync(loteId)).Lote.CantidadActual.Should().Be(1000);
    }

    [Fact]
    public async Task El_traslado_de_un_programa_por_lotes_mueve_lotes_y_su_detalle_se_edita_y_anula()
    {
        var (e, kardex, consultas, contexto, loteId) = await EntornoDeSalidasAsync();
        await using var _ = e;
        var dialogos = new DialogoFalso();
        var vm = new TrasladoEditorViewModel(kardex, consultas, e.Catalogo, e.Lotes, new FormulariosFalso(), contexto, e.Reloj, dialogos);
        await vm.InicializarAsync();
        vm.EsLote.Should().BeTrue();
        vm.GranjaDestino = vm.Granjas.Single(g => g.Nombre == "Tesorito");
        vm.AgregarLoteCommand.Execute(null);
        var fila = vm.Lotes.Single();
        fila.Cantidad = 300;
        fila.EsParcial.Should().BeTrue();
        fila.CodigoNuevoLote = "L-1-T";
        vm.NumeroRemision = "TRA-9";

        await vm.ConfirmarCommand.ExecuteAsync(null);

        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("Traslado de lotes registrado");
        (await e.Lotes.ObtenerAsync(loteId)).Lote.CantidadActual.Should().Be(700);
        (await e.Lotes.ListarAsync(e.Aves.Id)).Should().HaveCount(2);

        var remisionId = (await consultas.ListarSalidasAsync(new SalidaFiltro(null, new DateTime(2026, 10, 1), new DateTime(2026, 11, 30), ProgramaId: e.Aves.Id))).Items.Single().RemisionId;
        var detalle = new TrasladoEditorViewModel(kardex, consultas, e.Catalogo, e.Lotes, new FormulariosFalso(), new ContextoGlobal(), e.Reloj, dialogos);
        await detalle.InicializarExistenteAsync(remisionId);
        (detalle.EsLote, detalle.Modo).Should().Be((true, ModoDocumento.Detalle));
        detalle.Lotes.Single().CodigoNuevoLote.Should().Be("L-1-T");

        dialogos.RespuestaPedir = "error";
        await detalle.AnularCommand.ExecuteAsync(null);
        (await e.Lotes.ObtenerAsync(loteId)).Lote.CantidadActual.Should().Be(1000);
        (await e.Lotes.ListarAsync(e.Aves.Id)).Should().ContainSingle();
    }

    private sealed class SeccionConAccionesRapidas : IConAccionesRapidas
    {
        public Task CargarAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task Las_acciones_rapidas_ofrecen_Nuevo_lote_solo_en_programas_de_lotes()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var catalogo = new CatalogoService(e.Db.Uow);
        var formularios = new FormulariosFalso();
        var shell = new ShellViewModel(null!, catalogo, null!, formularios, new ContextoGlobal(), new DialogoFalso())
        {
            Contenido = new SeccionConAccionesRapidas(),
        };
        var programas = await catalogo.GetProgramasAsync();

        shell.ProgramaSeleccionado = programas.Single(p => p.Codigo == "PRG-GAN");
        (shell.MostrarRegistroNacimiento, shell.MostrarNuevoLote).Should().Be((true, false));

        shell.ProgramaSeleccionado = programas.Single(p => p.Codigo == "PRG-AVE");
        (shell.MostrarRegistroNacimiento, shell.MostrarNuevoLote).Should().Be((false, true));

        await shell.RegistrarNuevoLoteCommand.ExecuteAsync(null);
        formularios.Llamadas.Should().Contain("lote:nuevo");
    }
}