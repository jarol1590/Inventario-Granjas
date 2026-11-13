using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
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
    public async Task Sin_animales_el_inicio_dibuja_las_graficas_en_cero_en_vez_de_un_aviso()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var vm = new InicioViewModel(Consultas(e), new ContextoGlobal(), e.Reloj, new DialogoFalso());

        await vm.CargarAsync();

        vm.TotalAnimales.Should().Be(0);
        vm.SaldoTotalTexto.Should().Be("$0");

        // Animales por granja: las tres sedes, cada una en 0.
        var porGranja = vm.SeriesAnimales.Should().ContainSingle().Subject.Should().BeOfType<RowSeries<double>>().Subject;
        porGranja.Values.Should().Equal(0d, 0d, 0d);
        vm.EjesAnimalesY.Single().Labels.Should().Equal("Tesorito (0)", "La Cruz (0)", "Montelindo (0)");
        vm.EjesAnimalesX.Single().MaxLimit.Should().Be(Graficas.TopeMinimoDelEje, "el eje se ve de 0 a 4 aunque todo sea cero");

        // Movimientos del mes: las cinco categorías en 0 (sin movimientos no hay aviso: hay gráfica).
        vm.SeriesMovimientos.Should().HaveCount(3);
        vm.SeriesMovimientos.Cast<ColumnSeries<double?>>().SelectMany(s => s.Values!).Where(v => v is not null).Should().OnlyContain(v => v == 0d);
        vm.EjesMovimientosX.Single().Labels.Should().Equal("Nacimientos\n(0)", "Compras\n(0)", "Ventas\n(0)", "Bajas\n(0)", "Traslados\n(0)");
    }

    [Fact]
    public async Task El_inicio_de_una_granja_sin_animales_muestra_todas_las_categorias_del_programa_en_cero()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var vm = new InicioViewModel(Consultas(e), new ContextoGlobal { GranjaId = e.Ids.Montelindo, ProgramaId = e.Ids.ProgramaGanaderia }, e.Reloj, new DialogoFalso());

        await vm.CargarAsync();

        vm.TituloAnimales.Should().Be("Animales por Categoría");
        vm.EjesAnimalesY.Single().Labels.Should().Equal(
            $"{NombresBase.CategoriaTerneros} (0)", $"{NombresBase.CategoriaLevante} (0)", $"{NombresBase.CategoriaNovillaVientre} (0)",
            $"{NombresBase.CategoriaVacasGeneral} (0)", $"{NombresBase.CategoriaReproductor} (0)");
        ((RowSeries<double>)vm.SeriesAnimales.Single()).Values.Should().OnlyContain(v => v == 0d);
    }

    [Fact]
    public async Task Al_volver_a_cargar_el_inicio_las_graficas_se_vuelven_a_armar_con_tipografias_propias()
    {
        // Regresión: una SKTypeface compartida se liberaba junto con la primera gráfica y las siguientes salían en blanco.
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = new InicioViewModel(Consultas(e), new ContextoGlobal(), e.Reloj, new DialogoFalso());
        await vm.CargarAsync();
        var primera = (RowSeries<double>)vm.SeriesAnimales.Single();

        await vm.CargarAsync();
        var segunda = (RowSeries<double>)vm.SeriesAnimales.Single();

        segunda.Should().NotBeSameAs(primera);
        if (primera.DataLabelsPaint is SolidColorPaint pincel1 && segunda.DataLabelsPaint is SolidColorPaint pincel2 && pincel1.SKTypeface is not null)
        {
            pincel2.SKTypeface.Should().NotBeNull().And.NotBeSameAs(pincel1.SKTypeface, "cada pincel es dueño de su tipografía");
        }
    }

    // ---- Animales (Control Individual) ----------------------------------------------------------------------------------

    private static AnimalesViewModel CrearAnimales(EscenarioKardex e, ContextoGlobal? contexto = null, FormulariosFalso? formularios = null)
        => new(Consultas(e), new CatalogoService(e.Db.Uow), formularios ?? new FormulariosFalso(), contexto ?? new ContextoGlobal(), new DialogoFalso());

    /// <summary>Carga el módulo y devuelve la pestaña que queda activa (Ganadería).</summary>
    private static async Task<ProgramaTabViewModel> AbrirAsync(AnimalesViewModel vm)
    {
        await vm.CargarAsync();
        return vm.ProgramaActivo ?? throw new InvalidOperationException("No quedó ninguna pestaña activa.");
    }

    [Fact]
    public async Task Animales_arma_una_pestana_por_cada_programa_de_inventario_individual()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = CrearAnimales(e);

        var pestana = await AbrirAsync(vm);

        // Avicultura es de inventario por lote: no es una pestaña de control individual.
        vm.Programas.Select(p => p.Programa.Codigo).Should().Equal("PRG-GAN");
        vm.HayProgramas.Should().BeTrue();
        pestana.Programa.Codigo.Should().Be("PRG-GAN");
        pestana.Titulo.Should().Be("Bovinos");
        pestana.Icono.Should().Be("🐄");
        pestana.TituloGestion.Should().Be("Gestión de Bovinos");
        vm.MostrarFiltroDePrograma.Should().BeFalse("el programa lo eligen las pestañas, no el combo global");
        ((IUsaFiltroGlobal)new InicioViewModel(Consultas(e), new ContextoGlobal(), e.Reloj, new DialogoFalso())).MostrarFiltroDePrograma
            .Should().BeTrue("las demás secciones sí usan el combo de programa");
    }

    [Fact]
    public async Task Animales_lista_los_disponibles_con_conteo_total_y_solo_las_categorias_de_la_especie()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = CrearAnimales(e);

        var pestana = await AbrirAsync(vm);

        pestana.Animales.Select(b => b.ChapetaAmarilla).Should().Equal("A-3", "N-1", "P-1", "P-2", "T-1");
        pestana.ResumenTexto.Should().Be("Mostrando: 5 Bovinos Disponibles");
        pestana.TotalTexto.Should().Be("$13.750.000");
        pestana.Orientaciones.Select(o => o.Nombre).Should().Equal("Todos", "Lácteo", "Carne/Levante");
        pestana.Estados.Select(s => s.Nombre).Should().Equal("Disponible", "Vendido", "Muerto", "Hurtado", "Todos");
        pestana.Categorias.Select(c => c.Nombre).Should().Equal(
            "Todas", NombresBase.CategoriaTerneros, NombresBase.CategoriaLevante, NombresBase.CategoriaNovillaVientre,
            NombresBase.CategoriaVacasGeneral, NombresBase.CategoriaReproductor, NombresBase.CategoriaSinClasificar);
    }

    [Fact]
    public async Task Animales_aplica_el_filtro_global_de_granja_y_concuerda_singular_y_plural()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var vm = CrearAnimales(e, new ContextoGlobal { GranjaId = e.Ids.LaCruz });

        var pestana = await AbrirAsync(vm);

        pestana.Animales.Should().ContainSingle().Which.ChapetaAmarilla.Should().Be("A-3");
        pestana.ResumenTexto.Should().Be("Mostrando: 1 Bovino Disponible");
    }

    [Fact]
    public async Task Cambiar_el_estado_recarga_la_lista_sin_intervencion_del_usuario()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var pestana = await AbrirAsync(CrearAnimales(e));

        pestana.EstadoSeleccionado = pestana.Estados.Single(s => s.Valor is null);
        await Esperar.HastaAsync(() => pestana.Animales.Count == 7);

        pestana.ResumenTexto.Should().Be("Mostrando: 7 Bovinos (todos los estados)");

        pestana.EstadoSeleccionado = pestana.Estados.Single(s => s.Valor == EstadoAnimal.Muerto);
        await Esperar.HastaAsync(() => pestana.Animales.Count == 1);
        pestana.Animales.Single().ChapetaAmarilla.Should().Be("A-2");
        pestana.ResumenTexto.Should().Be("Mostrando: 1 Bovino Muerto");
    }

    [Fact]
    public async Task La_busqueda_por_texto_espera_a_que_el_usuario_deje_de_escribir()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var pestana = await AbrirAsync(CrearAnimales(e));

        pestana.Texto = "P-";
        await Esperar.HastaAsync(() => pestana.Animales.Count == 2);

        pestana.Animales.Select(b => b.ChapetaAmarilla).Should().Equal("P-1", "P-2");
    }

    [Fact]
    public async Task Abrir_la_ficha_usa_el_animal_seleccionado_y_recarga_si_se_guardaron_cambios()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        var formularios = new FormulariosFalso { Resultado = true };
        var pestana = await AbrirAsync(CrearAnimales(e, formularios: formularios));
        pestana.AbrirFichaCommand.CanExecute(null).Should().BeFalse("no hay fila seleccionada");

        pestana.Seleccionado = pestana.Animales.Single(b => b.ChapetaAmarilla == "P-1");
        await pestana.AbrirFichaCommand.ExecuteAsync(null);

        formularios.Llamadas.Should().Equal($"ficha:{pestana.Seleccionado.Id}");
    }

    /// <summary>Agrega el programa de equinos (inventario individual) con una categoría propia y un potro.</summary>
    private static async Task AgregarEquinosAsync(EscenarioKardex e)
    {
        await e.Db.EjecutarAsync("INSERT INTO PROGRAMA (codigo, nombre, tipo_inventario) VALUES ('PRG-EQU', 'Caballar (Equinos)', 'INDIVIDUAL')");
        await e.Db.EjecutarAsync("INSERT INTO CATEGORIA_ETAPA (nombre, sexo, programa_id) SELECT 'Potro', 'Macho', id FROM PROGRAMA WHERE codigo = 'PRG-EQU'");
        var potro = Datos.Animal(e.Ids, "E-1", Sexo.Macho, "2025-03-01", NombresBase.CategoriaReproductor, 7_000_000m);
        potro.CategoriaId = (int)await e.Db.EscalarAsync<long>("SELECT id FROM CATEGORIA_ETAPA WHERE nombre = 'Potro'");
        await e.AltaDirectaAsync(potro);
    }

    [Fact]
    public async Task Un_programa_individual_nuevo_aparece_como_pestana_con_sus_animales_y_categorias()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        await AgregarEquinosAsync(e);
        var vm = CrearAnimales(e);

        var bovinos = await AbrirAsync(vm);

        vm.Programas.Select(p => p.Programa.Codigo).Should().Equal("PRG-EQU", "PRG-GAN");
        vm.ProgramaActivo.Should().BeSameAs(bovinos);
        bovinos.Titulo.Should().Be("Bovinos", "Ganadería es la pestaña que se abre primero");

        // Elegir la pestaña filtra la grilla, las categorías y el total por ese programa, con la misma vista.
        var equinos = vm.Programas.Single(p => p.Programa.Codigo == "PRG-EQU");
        vm.ProgramaActivo = equinos;
        await Esperar.HastaAsync(() => equinos.Animales.Count == 1);

        equinos.Titulo.Should().Be("Equinos");
        equinos.Icono.Should().Be("🐴");
        equinos.Animales.Single().ChapetaAmarilla.Should().Be("E-1");
        equinos.ResumenTexto.Should().Be("Mostrando: 1 Equino Disponible");
        equinos.TotalTexto.Should().Be("$7.000.000");
        equinos.Categorias.Select(c => c.Nombre).Should().Equal("Todas", "Potro");
        bovinos.Animales.Select(b => b.ChapetaAmarilla).Should().NotContain("E-1");
        bovinos.TotalTexto.Should().Be("$13.750.000", "el total de bovinos no incluye a los equinos");
    }

    [Fact]
    public async Task Cada_pestana_conserva_sus_propios_filtros()
    {
        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();
        await AgregarEquinosAsync(e);
        var vm = CrearAnimales(e);
        var bovinos = await AbrirAsync(vm);
        var equinos = vm.Programas.Single(p => p.Programa.Codigo == "PRG-EQU");

        bovinos.EstadoSeleccionado = bovinos.Estados.Single(s => s.Valor == EstadoAnimal.Muerto);
        await Esperar.HastaAsync(() => bovinos.Animales.Count == 1);
        vm.ProgramaActivo = equinos;
        await Esperar.HastaAsync(() => equinos.Animales.Count == 1);

        equinos.EstadoSeleccionado.Valor.Should().Be(EstadoAnimal.Disponible, "el estado de bovinos no afecta a equinos");
        vm.ProgramaActivo = bovinos;
        await Esperar.HastaAsync(() => bovinos.Animales.Count == 1);
        bovinos.EstadoSeleccionado.Valor.Should().Be(EstadoAnimal.Muerto, "al volver, la pestaña recuerda su filtro");
    }

    [Fact]
    public async Task Sin_programas_de_inventario_individual_no_hay_pestanas_ni_errores()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.Db.EjecutarAsync("UPDATE PROGRAMA SET tipo_inventario = 'LOTE'");
        var dialogos = new DialogoFalso();
        var vm = new AnimalesViewModel(Consultas(e), new CatalogoService(e.Db.Uow), new FormulariosFalso(), new ContextoGlobal(), dialogos);

        await vm.CargarAsync();

        vm.Programas.Should().BeEmpty();
        vm.HayProgramas.Should().BeFalse();
        vm.ProgramaActivo.Should().BeNull();
        dialogos.Errores.Should().BeEmpty();
        dialogos.Advertencias.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Ganadería (Bovinos)", "Bovinos", "Bovino", "Bovinos", "🐄")]
    [InlineData("Caballar (Equinos)", "Equinos", "Equino", "Equinos", "🐴")]
    [InlineData("Caprinos", "Caprinos", "Caprino", "Caprinos", "🐐")]
    [InlineData("Ovinos (Ovejas)", "Ovejas", "Oveja", "Ovejas", "🐑")]
    [InlineData("Piscicultura (Peces)", "Peces", "Pez", "Peces", "🐟")]
    [InlineData("Porcicultura (Porcinos)", "Porcinos", "Porcino", "Porcinos", "🐖")]
    [InlineData("Ganadería", "Ganadería", "Animal", "Animales", "🐄")]
    [InlineData("Ganado de doble propósito", "Ganado de doble propósito", "Animal", "Animales", "🐄")]
    [InlineData("Llamas y alpacas", "Llamas y alpacas", "Animal", "Animales", "🐾")]
    public void Cada_programa_se_presenta_con_el_nombre_de_su_especie_el_conteo_y_un_icono(
        string nombre, string titulo, string singular, string plural, string icono)
    {
        var especie = EspecieDePrograma.De(new UCaldas.Granjas.Domain.Entities.Programa { Codigo = "PRG-X", Nombre = nombre, TipoInventario = TipoInventario.Individual });

        especie.Should().Be(new EspecieDePrograma(titulo, singular, plural, icono));
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
    public async Task Salidas_resume_ventas_bajas_y_traslados_y_abre_el_detalle_de_cada_clase()
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

        // Un traslado abre su propio detalle (editable), no el editor de salidas ni otra sección.
        var traslado = vm.Salidas.Single(s => s.Clase == ClaseSalida.Traslado);
        await vm.VerDetalleCommand.ExecuteAsync(traslado);
        formularios.Llamadas.Should().Equal($"traslado:{traslado.RemisionId}");
        navegador.Destinos.Should().BeEmpty();
        dialogos.Informaciones.Should().BeEmpty();

        var venta = vm.Salidas.Single(s => s.Clase == ClaseSalida.Venta);
        await vm.VerDetalleCommand.ExecuteAsync(venta);
        formularios.Llamadas.Should().Equal($"traslado:{traslado.RemisionId}", $"salida:{venta.RemisionId}:VENTA");
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
    public async Task Traslados_abre_el_detalle_del_seleccionado_y_recarga_solo_si_se_guardo_algo()
    {
        await using var e = await EscenarioKardex.CrearAsync("2026-10-31");
        await e.AltaVacaAsync("A-3", valor: 4_000_000m);
        await e.Kardex.RegistrarTrasladoAsync(await e.TrasladoAsync("REM-T-1", e.Ids.Tesorito, e.Ids.LaCruz, "A-3"));
        var formularios = new FormulariosFalso { Resultado = false };
        var vm = new TrasladosViewModel(
            Consultas(e), e.Kardex, formularios, new NavegadorFalso(), new ContextoGlobal(), e.Reloj, new DialogoFalso());
        await vm.CargarAsync();
        vm.VerDetalleCommand.CanExecute(null).Should().BeFalse("no hay traslado seleccionado");

        vm.Seleccionado = vm.Traslados[0];
        var id = vm.Seleccionado.RemisionId;
        vm.VerDetalleCommand.CanExecute(null).Should().BeTrue();
        await vm.VerDetalleCommand.ExecuteAsync(vm.Seleccionado);

        formularios.Llamadas.Should().Equal($"traslado:{id}");
        vm.Traslados.Should().HaveCount(1, "la ventana se cerró sin guardar: no se recarga");

        // Si desde el detalle se anuló o se editó algo la ventana devuelve true y la lista se actualiza.
        await e.Kardex.AnularAsync(id);
        formularios.Resultado = true;
        await vm.VerDetalleCommand.ExecuteAsync(vm.Seleccionado);

        vm.Traslados.Should().BeEmpty("el traslado anulado ya no figura en la lista");
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
