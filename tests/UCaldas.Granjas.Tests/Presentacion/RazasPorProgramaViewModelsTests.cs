using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.App.ViewModels.Configuracion;
using UCaldas.Granjas.App.ViewModels.Lotes;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Lotes;
using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;
using UCaldas.Granjas.Tests.Kardex;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>Los selectores de Raza / Línea muestran solo las razas del programa del registro (catálogo por programa).</summary>
public class RazasPorProgramaViewModelsTests
{
    private const string PorDefecto = "No Especificada / Cruce";

    private static async Task<(Programa Equinos, CatalogoService Catalogo)> EquinosConRazasAsync(EscenarioKardex e)
    {
        var catalogo = new CatalogoService(e.Db.Uow);
        var equinos = await catalogo.GuardarProgramaAsync(
            new Programa { Codigo = "PRG-EQU", Nombre = "Equinos", TipoInventario = TipoInventario.Individual }, [e.Ids.Tesorito]);
        await catalogo.AgregarRazaAsync(equinos.Id, "Paso Fino");
        await catalogo.AgregarRazaAsync(equinos.Id, "Árabe");
        return (equinos, catalogo);
    }

    [Fact]
    public async Task El_nacimiento_ofrece_solo_las_razas_del_programa_y_preselecciona_la_de_por_defecto()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var (equinos, catalogo) = await EquinosConRazasAsync(e);

        var vmEquinos = new NacimientoViewModel(e.Kardex, new AnimalService(e.Db.Uow, e.Aging, e.Reloj), catalogo,
            new ContextoGlobal { ProgramaId = equinos.Id }, e.Reloj, new DialogoFalso());
        await vmEquinos.InicializarAsync();
        vmEquinos.RazasDisponibles.Should().Equal([PorDefecto, "Árabe", "Paso Fino"]);
        vmEquinos.Raza.Should().Be(PorDefecto);

        var vmBovinos = new NacimientoViewModel(e.Kardex, new AnimalService(e.Db.Uow, e.Aging, e.Reloj), catalogo,
            new ContextoGlobal(), e.Reloj, new DialogoFalso());
        await vmBovinos.InicializarAsync();
        vmBovinos.RazasDisponibles.Should().Contain(["Holstein", PorDefecto]).And.NotContain("Paso Fino");

        // Al cambiar de programa la lista se recarga y una raza que no es del nuevo programa vuelve a la de por defecto.
        vmBovinos.Raza = "Holstein";
        vmBovinos.ProgramaSeleccionado = vmBovinos.Programas.Single(p => p.Id == equinos.Id);
        await Esperar.HastaAsync(() => Task.FromResult(vmBovinos.RazasDisponibles.Contains("Paso Fino")));
        vmBovinos.RazasDisponibles.Should().NotContain("Holstein");
        vmBovinos.Raza.Should().Be(PorDefecto);
    }

    [Fact]
    public async Task La_compra_de_animales_ofrece_las_razas_del_programa_y_las_filas_nacen_con_la_de_por_defecto()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        var (equinos, catalogo) = await EquinosConRazasAsync(e);
        var vm = new CompraEditorViewModel(e.Kardex, new ConsultasService(e.Db.Uow, e.Reloj), catalogo, new LoteService(e.Db.Uow, e.Reloj),
            new ContextoGlobal { ProgramaId = equinos.Id }, e.Reloj, new DialogoFalso());

        await vm.InicializarNuevaAsync();

        vm.RazasDisponibles.Should().Equal([PorDefecto, "Árabe", "Paso Fino"]);
        vm.Animales[0].Raza.Should().Be(PorDefecto);
    }

    [Fact]
    public async Task La_ficha_del_animal_muestra_las_razas_de_su_programa_y_conserva_una_raza_antigua()
    {
        await using var e = await EscenarioKardex.CrearAsync();
        await e.AltaVacaAsync("A-1", valor: 6_000_000m);
        var animal = await e.AnimalAsync("A-1");
        var catalogo = new CatalogoService(e.Db.Uow);
        var vm = new FichaViewModel(new FichaService(e.Db.Uow, e.Reloj), new AnimalService(e.Db.Uow, e.Aging, e.Reloj), null!, catalogo, new DialogoFalso());

        await vm.InicializarAsync(animal.Id);

        vm.RazasDisponibles.Should().Contain(["Holstein", PorDefecto]).And.NotContain("Hy-Line Brown");
        vm.RazasDisponibles.Should().Contain(vm.Raza, "la raza actual siempre se puede mostrar, aunque sea antigua");
    }

    private static async Task<(BaseDeDatosDePrueba Db, Programa Aves, CatalogoService Catalogo, LoteService Lotes)> LotesAsync()
    {
        var db = await BaseDeDatosDePrueba.CrearAsync();
        var catalogo = new CatalogoService(db.Uow);
        var aves = (await catalogo.GetProgramasAsync()).Single(p => p.Codigo == "PRG-AVE");
        return (db, aves, catalogo, new LoteService(db.Uow, new RelojFijo(Datos.F("2026-11-06"))));
    }

    [Fact]
    public async Task Nuevo_lote_ofrece_las_lineas_del_programa_y_preselecciona_la_de_por_defecto()
    {
        var (db, aves, catalogo, lotes) = await LotesAsync();
        await using var _ = db;
        var vm = new NuevoLoteViewModel(lotes, catalogo, new TarifaProgramaService(db.Uow), new RelojFijo(Datos.F("2026-11-06")), new DialogoFalso(), aves);

        await vm.InicializarAsync();

        vm.RazasDisponibles.Should().Equal([PorDefecto, "Cobb", "Hy-Line Brown", "Lohmann", "Ross"]);
        vm.LineaRaza.Should().Be(PorDefecto);
        vm.RazasDisponibles.Should().NotContain("Holstein");
    }

    [Fact]
    public async Task La_ficha_del_lote_ofrece_las_lineas_del_programa_y_conserva_la_antigua()
    {
        var (db, aves, catalogo, lotes) = await LotesAsync();
        await using var _ = db;
        int etapa;
        await using (var uow = await db.Uow.OpenReadOnlyAsync())
        {
            etapa = (await uow.Categorias.GetByNombreAsync(aves.Id, NombresBase.CategoriaGeneral))!.Id;
        }

        var granja = (await catalogo.GetGranjasAsync()).Single(g => g.Codigo == "G-03").Id;
        var id = await lotes.RegistrarNuevoLoteAsync(new NuevoLoteDto
        {
            ProgramaId = aves.Id, GranjaId = granja, EtapaId = etapa, ClaseEntrada = ClaseEntrada.Compra, CodigoLote = "L-1", Ubicacion = "G1",
            LineaRaza = "Isa Brown", FechaIngreso = Datos.F("2026-11-02"), Cantidad = 100, PrecioUnitarioFactura = 10_000m,
            Factura = "F", OrdenCompra = "O", Proveedor = "P",
        });
        var vm = new FichaLoteViewModel(lotes, catalogo, new DialogoFalso(), await lotes.ObtenerAsync(id));

        await vm.InicializarAsync();

        vm.RazasDisponibles.Should().Contain(["Ross", "Cobb", PorDefecto, "Isa Brown"]);
        vm.LineaRaza.Should().Be("Isa Brown");
        vm.HayCambios.Should().BeFalse();
    }

    [Fact]
    public async Task Configuracion_lista_agrega_e_inactiva_las_razas_del_programa_elegido()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var dialogos = new DialogoFalso();
        var vm = new GranjasProgramasViewModel(new CatalogoService(db.Uow), new NavegadorFalso(), dialogos);

        await vm.CargarAsync();

        vm.ProgramasDeRazas.Select(p => p.Codigo).Should().Equal("PRG-AVE", "PRG-GAN");
        vm.ProgramaDeRazas!.Codigo.Should().Be("PRG-AVE");
        await Esperar.HastaAsync(() => Task.FromResult(vm.Razas.Count == 5));
        vm.Razas[0].Nombre.Should().Contain("por defecto");
        vm.Razas[0].PuedeCambiarEstado.Should().BeFalse();

        vm.NuevaRaza = "Isa Brown";
        await vm.AgregarRazaCommand.ExecuteAsync(null);
        vm.NuevaRaza.Should().BeEmpty();
        vm.Razas.Select(r => r.Raza.Nombre).Should().Contain("Isa Brown");

        var fila = vm.Razas.Single(r => r.Raza.Nombre == "Isa Brown");
        await vm.CambiarEstadoRazaCommand.ExecuteAsync(fila);
        vm.Razas.Single(r => r.Raza.Nombre == "Isa Brown").Estado.Should().Be("Inactiva");

        vm.NuevaRaza = "Ross";   // ya existe
        await vm.AgregarRazaCommand.ExecuteAsync(null);
        dialogos.UltimaAdvertencia.Should().Contain("Ya existe");

        // Cambiar de programa muestra las razas de ese programa.
        vm.ProgramaDeRazas = vm.ProgramasDeRazas.Single(p => p.Codigo == "PRG-GAN");
        await Esperar.HastaAsync(() => Task.FromResult(vm.Razas.Any(r => r.Raza.Nombre == "Holstein")));
        vm.Razas.Select(r => r.Raza.Nombre).Should().NotContain("Isa Brown");
    }
}
