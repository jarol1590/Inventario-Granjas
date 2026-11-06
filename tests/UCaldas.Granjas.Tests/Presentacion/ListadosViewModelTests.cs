using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Reportes;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;
using UCaldas.Granjas.Tests.Reportes;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>Pantallas de consulta (inicio, bovinos, compras, salidas, traslados, informes) sobre el escenario de octubre.</summary>
public class ListadosViewModelTests
{
    private static ConsultasService Consultas(EscenarioKardex e) => new(e.Db.Uow, e.Reloj);

    // ---- Inicio -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_inicio_resume_el_inventario_y_los_movimientos_del_mes()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = new InicioViewModel(Consultas(e), new ContextoGlobal(), e.Reloj, new DialogoFalso());

        await vm.CargarAsync();

        vm.TotalAnimales.Should().Be(5);
        vm.SaldoTotalTexto.Should().Be("$13.750.000");
        vm.NacimientosMes.Should().Be(1);
        vm.SalidasMes.Should().Be(2);
        vm.HayDatos.Should().BeTrue();
        vm.HayMovimientos.Should().BeTrue();
        vm.TituloAnimales.Should().Be("Animales por Granja");
        vm.TituloMovimientos.Should().Be("Movimientos del Mes — Octubre de 2026");
        vm.SeriesAnimales.Should().ContainSingle();
        vm.SeriesMovimientos.Should().HaveCount(3, "entradas, salidas y traslados");
    }

    [Fact]
    public async Task Al_elegir_una_granja_el_inicio_muestra_los_animales_por_categoria()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = new InicioViewModel(Consultas(e), new ContextoGlobal { GranjaId = e.Ids.Tesorito }, e.Reloj, new DialogoFalso());

        await vm.CargarAsync();

        vm.TotalAnimales.Should().Be(4);
        vm.TituloAnimales.Should().Be("Animales por Categoría");
    }

    [Fact]
    public async Task Sin_animales_el_inicio_lo_indica_y_no_falla()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var vm = new InicioViewModel(Consultas(e), new ContextoGlobal(), e.Reloj, new DialogoFalso());

        await vm.CargarAsync();

        vm.TotalAnimales.Should().Be(0);
        vm.HayDatos.Should().BeFalse();
        vm.HayMovimientos.Should().BeFalse();
        vm.SaldoTotalTexto.Should().Be("$0");
    }

    // ---- Bovinos ------------------------------------------------------------------------------------------------------

    private static BovinosViewModel CrearBovinos(EscenarioKardex e, ContextoGlobal? contexto = null, FormulariosFalso? formularios = null)
        => new(Consultas(e), new CatalogoService(e.Db.Uow), formularios ?? new FormulariosFalso(), contexto ?? new ContextoGlobal(), new DialogoFalso());

    [Fact]
    public async Task Bovinos_lista_los_disponibles_con_conteo_y_total()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = CrearBovinos(e);

        await vm.CargarAsync();

        vm.Bovinos.Select(b => b.ChapetaAmarilla).Should().Equal("A-3", "N-1", "P-1", "P-2", "T-1");
        vm.ResumenTexto.Should().Be("Mostrando: 5 Bovinos Disponibles");
        vm.TotalTexto.Should().Be("$13.750.000");
        vm.Orientaciones.Select(o => o.Nombre).Should().Equal("Todos", "Lácteo", "Carne/Levante");
        vm.Estados.Select(s => s.Nombre).Should().Equal("Disponible", "Vendido", "Muerto", "Hurtado", "Todos");
    }

    [Fact]
    public async Task Bovinos_aplica_el_filtro_global_de_granja_y_concuerda_singular_y_plural()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = CrearBovinos(e, new ContextoGlobal { GranjaId = e.Ids.LaCruz });

        await vm.CargarAsync();

        vm.Bovinos.Should().ContainSingle().Which.ChapetaAmarilla.Should().Be("A-3");
        vm.ResumenTexto.Should().Be("Mostrando: 1 Bovino Disponible");
    }

    [Fact]
    public async Task Cambiar_el_estado_recarga_la_lista_sin_intervencion_del_usuario()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = CrearBovinos(e);
        await vm.CargarAsync();

        vm.EstadoSeleccionado = vm.Estados.Single(s => s.Valor is null);
        await Esperar.HastaAsync(() => vm.Bovinos.Count == 7);

        vm.ResumenTexto.Should().Be("Mostrando: 7 Bovinos (todos los estados)");

        vm.EstadoSeleccionado = vm.Estados.Single(s => s.Valor == EstadoAnimal.Muerto);
        await Esperar.HastaAsync(() => vm.Bovinos.Count == 1);
        vm.Bovinos.Single().ChapetaAmarilla.Should().Be("A-2");
        vm.ResumenTexto.Should().Be("Mostrando: 1 Bovino Muerto");
    }

    [Fact]
    public async Task La_busqueda_por_texto_espera_a_que_el_usuario_deje_de_escribir()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = CrearBovinos(e);
        await vm.CargarAsync();

        vm.Texto = "P-";
        await Esperar.HastaAsync(() => vm.Bovinos.Count == 2);

        vm.Bovinos.Select(b => b.ChapetaAmarilla).Should().Equal("P-1", "P-2");
    }

    [Fact]
    public async Task Abrir_la_ficha_usa_el_animal_seleccionado_y_recarga_si_se_guardaron_cambios()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var formularios = new FormulariosFalso { Resultado = true };
        var vm = CrearBovinos(e, formularios: formularios);
        await vm.CargarAsync();
        vm.AbrirFichaCommand.CanExecute(null).Should().BeFalse("no hay fila seleccionada");

        vm.Seleccionado = vm.Bovinos.Single(b => b.ChapetaAmarilla == "P-1");
        await vm.AbrirFichaCommand.ExecuteAsync(null);

        formularios.Llamadas.Should().Equal($"ficha:{vm.Seleccionado.Id}");
    }

    // ---- Compras ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Compras_muestra_el_resumen_del_rango_y_permite_buscar_por_factura()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var navegador = new NavegadorFalso();
        var vm = new ComprasViewModel(Consultas(e), new FormulariosFalso(), navegador, new ContextoGlobal(), e.Reloj, new DialogoFalso());

        await vm.CargarAsync();

        vm.Compras.Should().ContainSingle().Which.NumeroFactura.Should().Be("FAC-1");
        vm.TotalCompras.Should().Be(1);
        vm.AnimalesComprados.Should().Be(2);
        vm.ValorTotalTexto.Should().Be("$3.350.000");
        vm.Rango.Desde.Should().Be(new DateTime(2026, 1, 1));
        vm.Rango.Hasta.Should().Be(new DateTime(2026, 10, 31));

        vm.TextoFactura = "otra";
        await vm.BuscarCommand.ExecuteAsync(null);
        vm.Compras.Should().BeEmpty();
        vm.TotalCompras.Should().Be(0);

        vm.GenerarInformeCommand.Execute(null);
        navegador.Destinos.Should().Equal(ShellViewModel.ClaveInformes);
    }

    [Fact]
    public async Task Un_rango_de_meses_invertido_se_advierte_y_no_consulta()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var dialogos = new DialogoFalso();
        var vm = new ComprasViewModel(Consultas(e), new FormulariosFalso(), new NavegadorFalso(), new ContextoGlobal(), e.Reloj, dialogos);
        await vm.CargarAsync();
        vm.Compras.Should().HaveCount(1);

        vm.Rango.MesDesde = FiltroMensual.Meses[11]; // diciembre 2026 > octubre 2026 (cambio dispara la recarga)
        await Esperar.HastaAsync(() => dialogos.Advertencias.Count > 0);

        dialogos.UltimaAdvertencia.Should().Contain("mes inicial no puede ser posterior");
        vm.Compras.Should().HaveCount(1, "la lista anterior no se reemplaza con un rango inválido");
    }

    [Fact]
    public async Task Ver_el_detalle_de_una_compra_abre_su_ventana_y_nueva_compra_recarga_si_se_guardo()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var formularios = new FormulariosFalso { Resultado = true };
        var vm = new ComprasViewModel(Consultas(e), formularios, new NavegadorFalso(), new ContextoGlobal(), e.Reloj, new DialogoFalso());
        await vm.CargarAsync();
        vm.VerDetalleCommand.CanExecute(null).Should().BeFalse();

        vm.Seleccionada = vm.Compras[0];
        await vm.VerDetalleCommand.ExecuteAsync(vm.Seleccionada);
        await vm.NuevaCompraCommand.ExecuteAsync(null);

        formularios.Llamadas.Should().Equal($"compra:{vm.Compras[0].RemisionId}", "compra:nueva");
    }

    // ---- Salidas ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Salidas_resume_ventas_bajas_y_traslados_y_remite_los_traslados_a_su_seccion()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var formularios = new FormulariosFalso();
        var navegador = new NavegadorFalso();
        var dialogos = new DialogoFalso();
        var vm = new SalidasViewModel(Consultas(e), formularios, navegador, new ContextoGlobal(), e.Reloj, dialogos);

        await vm.CargarAsync();

        vm.Salidas.Select(s => s.NumeroRemision).Should().Equal("REM-T-1", "REM-1", "REM-B-1");
        vm.Salidas.Select(s => s.ClaseTexto).Should().Equal("TRASLADO", "VENTA", "MUERTE");
        vm.RecaudoTexto.Should().Be("$6.500.000");
        vm.AnimalesRetirados.Should().Be(2);
        vm.BajasPorMuerteHurto.Should().Be(1);

        await vm.VerDetalleCommand.ExecuteAsync(vm.Salidas.Single(s => s.Clase == ClaseSalida.Traslado));
        navegador.Destinos.Should().Equal(ShellViewModel.ClaveTraslados);
        dialogos.Informaciones.Should().ContainSingle().Which.Should().Contain("sección Traslados");
        formularios.Llamadas.Should().BeEmpty("los traslados no abren el editor de salidas");

        var venta = vm.Salidas.Single(s => s.Clase == ClaseSalida.Venta);
        await vm.VerDetalleCommand.ExecuteAsync(venta);
        formularios.Llamadas.Should().Equal($"salida:{venta.RemisionId}:VENTA");
    }

    [Fact]
    public async Task Salidas_busca_por_chapeta()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = new SalidasViewModel(Consultas(e), new FormulariosFalso(), new NavegadorFalso(), new ContextoGlobal(), e.Reloj, new DialogoFalso());
        await vm.CargarAsync();

        vm.TextoChapeta = "A-2";
        await vm.BuscarCommand.ExecuteAsync(null);

        vm.Salidas.Should().ContainSingle().Which.NumeroRemision.Should().Be("REM-B-1");
    }

    // ---- Traslados ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Traslados_resume_el_periodo_y_anular_uno_lo_saca_del_resumen()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-10-31");
        await e.AltaVacaAsync("A-3", valor: 4_000_000m);
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "A-3"));
        var dialogos = new DialogoFalso { RespuestaConfirmar = true, RespuestaPedir = "Remisión mal numerada" };
        var vm = new TrasladosViewModel(
            Consultas(e), e.Kardex, new FormulariosFalso(), new NavegadorFalso(), new ContextoGlobal(), e.Reloj, dialogos);
        await vm.CargarAsync();

        vm.Traslados.Should().ContainSingle().Which.NumeroRemision.Should().Be("REM-T-1");
        vm.TrasladosRealizados.Should().Be(1);
        vm.AnimalesMovidos.Should().Be(1);
        vm.AnularCommand.CanExecute(null).Should().BeFalse("no hay traslado seleccionado");

        vm.Seleccionado = vm.Traslados[0];
        vm.AnularCommand.CanExecute(null).Should().BeTrue();
        await vm.AnularCommand.ExecuteAsync(null);

        vm.Traslados.Should().BeEmpty();
        vm.TrasladosRealizados.Should().Be(0);
        (await e.RemisionAsync("REM-T-1")).Anulada.Should().BeTrue();
        (await e.AnimalAsync("A-3")).GranjaId.Should().Be(e.Ids.Tesorito, "el animal regresó a la granja de origen");
        dialogos.Confirmaciones.Should().ContainSingle().Which.Should().Contain("(1 animal) regresan a Tesorito");

        vm.IncluirAnulados = true;
        await Esperar.HastaAsync(() => vm.Traslados.Count == 1);
        vm.Traslados[0].Anulado.Should().BeTrue();
        vm.TrasladosRealizados.Should().Be(0, "los anulados no cuentan en el resumen");
    }

    [Fact]
    public async Task Registrar_un_traslado_recarga_la_lista_solo_si_se_guardo()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var formularios = new FormulariosFalso { Resultado = false };
        var vm = new TrasladosViewModel(
            Consultas(e), e.Kardex, formularios, new NavegadorFalso(), new ContextoGlobal(), e.Reloj, new DialogoFalso());
        await vm.CargarAsync();

        await vm.RegistrarTrasladoCommand.ExecuteAsync(null);

        formularios.Llamadas.Should().Equal("traslado:nuevo");
        vm.Traslados.Should().HaveCount(1);
    }

    // ---- Informes -----------------------------------------------------------------------------------------------------

    private static InformesViewModel CrearInformes(EscenarioKardex e, DialogoFalso dialogos, ContextoGlobal? contexto = null)
    {
        var motor = new ExcelReportingEngine(e.Db.Uow, new KardexReportBuilder(e.Db.Uow), new FichaService(e.Db.Uow, e.Reloj), e.Reloj);
        return new InformesViewModel(motor, new CatalogoService(e.Db.Uow), contexto ?? new ContextoGlobal(), e.Reloj, dialogos);
    }

    [Fact]
    public async Task Informes_advierte_si_la_firma_conserva_los_datos_de_ejemplo()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = CrearInformes(e, new DialogoFalso());

        await vm.CargarAsync();

        vm.AdvertenciaFirma.Should().Contain("datos de ejemplo");
        vm.AlcanceTexto.Should().Contain("Todas las granjas");
        vm.Categorias.Select(c => c.Nombre).Should().Equal("Todas las categorías", "Terneros", "Levante", "Novilla Vientre", "Vacas General", "Reproductor");
        vm.MesSeleccionado.Nombre.Should().Be("Septiembre", "el mes sugerido es el último cerrado");

        await EscenariosDeReporte.ConfigurarFirmaAsync(e, null);
        await vm.CargarAsync();
        vm.AdvertenciaFirma.Should().Contain("Paula Valencia Arango");
    }

    [Fact]
    public async Task Si_el_usuario_cancela_el_cuadro_de_guardar_no_se_genera_nada()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var dialogos = new DialogoFalso { RutaAGuardar = null };
        var vm = CrearInformes(e, dialogos);
        await vm.CargarAsync();

        await vm.GenerarCommand.ExecuteAsync(null);

        vm.UltimoArchivo.Should().BeNull();
        dialogos.NombresSugeridos.Should().Equal("Kardex_Financiero_2026-09.xlsx");
        dialogos.Confirmaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Generar_el_kardex_crea_el_libro_de_excel_y_ofrece_abrirlo()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        await EscenariosDeReporte.ConfigurarFirmaAsync(e, null);
        var carpeta = Path.Combine(Path.GetTempPath(), "SistemaGranjasPruebas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            var ruta = Path.Combine(carpeta, "kardex.xlsx");
            var dialogos = new DialogoFalso { RutaAGuardar = ruta, RespuestaConfirmar = true };
            var vm = CrearInformes(e, dialogos);
            await vm.CargarAsync();
            vm.MesSeleccionado = FiltroMensual.Meses[9]; // octubre
            vm.Anio = 2026;

            await vm.GenerarCommand.ExecuteAsync(null);

            File.Exists(ruta).Should().BeTrue();
            new FileInfo(ruta).Length.Should().BeGreaterThan(5_000);
            vm.UltimoArchivo.Should().Be(ruta);
            vm.AbrirUltimoCommand.CanExecute(null).Should().BeTrue();
            dialogos.Abiertos.Should().Equal(ruta);
            dialogos.NombresSugeridos.Should().Equal("Kardex_Financiero_2026-10.xlsx");
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public async Task Generar_el_inventario_fisico_usa_los_filtros_de_granja_y_categoria()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var carpeta = Path.Combine(Path.GetTempPath(), "SistemaGranjasPruebas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            var ruta = Path.Combine(carpeta, "inventario.xlsx");
            var dialogos = new DialogoFalso { RutaAGuardar = ruta, RespuestaConfirmar = false };
            var vm = CrearInformes(e, dialogos, new ContextoGlobal { GranjaId = e.Ids.Tesorito });
            await vm.CargarAsync();
            vm.EsInventario = true;
            vm.EsKardex.Should().BeFalse();
            vm.CategoriaSeleccionada = vm.Categorias.Single(c => c.Nombre == NombresBase.CategoriaVacasGeneral);

            await vm.GenerarCommand.ExecuteAsync(null);

            File.Exists(ruta).Should().BeTrue();
            dialogos.NombresSugeridos.Single().Should().StartWith("Inventario_Fisico_");
            dialogos.Abiertos.Should().BeEmpty("el usuario eligió no abrirlo");
            vm.AlcanceTexto.Should().Be("Granja Tesorito");
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }
}
