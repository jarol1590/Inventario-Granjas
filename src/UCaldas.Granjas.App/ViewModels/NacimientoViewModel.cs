using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Registro de un nacimiento. Muestra en vivo la categoría y el valor inicial que daría la matriz de tarifas vigente
/// (el valor de compra de una cría nacida en la granja es siempre $0).
/// </summary>
public sealed partial class NacimientoViewModel : DialogViewModelBase
{
    private readonly IKardexService _kardex;
    private readonly IAnimalService _animales;
    private readonly ICatalogoService _catalogo;
    private readonly ContextoGlobal _contexto;
    private readonly IReloj _reloj;
    private int _versionVistaPrevia;
    private bool _inicializado;

    public NacimientoViewModel(
        IKardexService kardex,
        IAnimalService animales,
        ICatalogoService catalogo,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _kardex = kardex;
        _animales = animales;
        _catalogo = catalogo;
        _contexto = contexto;
        _reloj = reloj;
        _fechaNacimiento = reloj.Hoy;
    }

    public override string Titulo => "Registro Nacimiento";

    public override double Ancho => 780;

    public ObservableCollection<OpcionFiltro> Granjas { get; } = [];

    public ObservableCollection<OpcionFiltro> Orientaciones { get; } = [];

    /// <summary>Programas de inventario individual (bovinos, equinos...).</summary>
    public ObservableCollection<Programa> Programas { get; } = [];

    /// <summary>Categorías del programa elegido; solo se usan en programas sin valorización biológica.</summary>
    public ObservableCollection<OpcionFiltro> Categorias { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsBovino), nameof(NoEsBovino))]
    private Programa? _programaSeleccionado;

    [ObservableProperty]
    private OpcionFiltro? _categoriaSeleccionada;

    /// <summary>La orientación, la madre y la matriz de tarifas son de la ganadería (valorización biológica).</summary>
    public bool EsBovino => ProgramaSeleccionado?.UsaValorizacionBiologica ?? true;

    public bool NoEsBovino => !EsBovino;

    /// <summary>Razas activas del programa elegido (catálogo por programa).</summary>
    public ObservableCollection<string> RazasDisponibles { get; } = [];

    [ObservableProperty]
    private OpcionFiltro? _granjaSeleccionada;

    [ObservableProperty]
    private OpcionFiltro? _orientacionSeleccionada;

    [ObservableProperty]
    private DateTime? _fechaNacimiento;

    [ObservableProperty]
    private string _chapetaAmarilla = string.Empty;

    [ObservableProperty]
    private string _chapetaRoja = string.Empty;

    [ObservableProperty]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string _raza = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsMacho))]
    private bool _esHembra = true;

    [ObservableProperty]
    private decimal _pesoAlNacerKg;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsMontaDirecta))]
    private bool _esInseminacion;

    [ObservableProperty]
    private string _madreIdentificacion = string.Empty;

    [ObservableProperty]
    private string _padreChapeta = string.Empty;

    [ObservableProperty]
    private string _nombrePadre = string.Empty;

    [ObservableProperty]
    private string _razaPadre = string.Empty;

    [ObservableProperty]
    private string _categoriaInicial = "—";

    [ObservableProperty]
    private string _valorInicial = "—";

    [ObservableProperty]
    private bool _sinTarifa;

    /// <summary>El valor inicial sale del valor de referencia de la categoría (no de la matriz por edades).</summary>
    [ObservableProperty]
    private bool _esReferencia;

    public bool EsMacho
    {
        get => !EsHembra;
        set => EsHembra = !value;
    }

    public bool EsMontaDirecta
    {
        get => !EsInseminacion;
        set => EsInseminacion = !value;
    }

    /// <summary>Carga las listas y toma la granja de los filtros globales como valor inicial.</summary>
    public async Task<bool> InicializarAsync()
    {
        var cargado = await EjecutarAsync(async () =>
        {
            var granjas = await _catalogo.GetGranjasAsync(soloActivas: true);
            Granjas.Clear();
            foreach (var g in granjas)
            {
                Granjas.Add(new OpcionFiltro(g.Id, g.Nombre));
            }

            foreach (var o in await _catalogo.GetOrientacionesAsync())
            {
                Orientaciones.Add(new OpcionFiltro(o.Id, o.Nombre));
            }

            foreach (var p in await _catalogo.GetProgramasPorTipoAsync(TipoInventario.Individual))
            {
                Programas.Add(p);
            }

            ProgramaSeleccionado = Programas.FirstOrDefault(p => p.Id == _contexto.ProgramaId)
                ?? Programas.FirstOrDefault(p => p.UsaValorizacionBiologica) ?? Programas.FirstOrDefault();
            await CargarCategoriasAsync();
            await CargarRazasAsync();
            GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == _contexto.GranjaId) ?? Granjas.FirstOrDefault();
            var sugerida = granjas.FirstOrDefault(g => g.Id == GranjaSeleccionada?.Id)?.OrientacionId;
            OrientacionSeleccionada = Orientaciones.FirstOrDefault(o => o.Id == sugerida) ?? Orientaciones.FirstOrDefault();
            _inicializado = true;
            await ActualizarVistaPreviaAsync();
        }, "preparar el formulario de nacimiento");
        return cargado;
    }

    partial void OnProgramaSeleccionadoChanged(Programa? value)
    {
        if (_inicializado)
        {
            _ = EjecutarAsync(async () =>
            {
                await CargarCategoriasAsync();
                await CargarRazasAsync();
                await ActualizarVistaPreviaAsync();
            }, "cargar las categorías del programa");
        }
    }

    partial void OnCategoriaSeleccionadaChanged(OpcionFiltro? value) => _ = ActualizarVistaPreviaAsync();

    /// <summary>Carga las razas del programa elegido; si la raza escrita no es de ese programa, se vuelve a la opción por defecto.</summary>
    private async Task CargarRazasAsync()
    {
        RazasDisponibles.Clear();
        if (ProgramaSeleccionado is not { } programa)
        {
            return;
        }

        foreach (var r in await _catalogo.GetRazasDeProgramaAsync(programa.Id))
        {
            RazasDisponibles.Add(r.Nombre);
        }

        if (!RazasDisponibles.Contains(Raza, StringComparer.OrdinalIgnoreCase))
        {
            Raza = RazasDisponibles.FirstOrDefault() ?? string.Empty;
        }
    }

    private async Task CargarCategoriasAsync()
    {
        Categorias.Clear();
        if (ProgramaSeleccionado is not { UsaValorizacionBiologica: false } programa)
        {
            CategoriaSeleccionada = null;
            return;
        }

        foreach (var c in await _catalogo.GetCategoriasDeProgramaAsync(programa.Id))
        {
            Categorias.Add(new OpcionFiltro(c.Id, c.Nombre));
        }

        CategoriaSeleccionada = Categorias.FirstOrDefault();
    }

    partial void OnFechaNacimientoChanged(DateTime? value) => _ = ActualizarVistaPreviaAsync();

    partial void OnEsHembraChanged(bool value) => _ = ActualizarVistaPreviaAsync();

    partial void OnOrientacionSeleccionadaChanged(OpcionFiltro? value) => _ = ActualizarVistaPreviaAsync();

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var orientacionElegida = EsBovino ? OrientacionSeleccionada?.Id : 0;
        if (GranjaSeleccionada?.Id is not { } granjaId || orientacionElegida is not { } orientacionId)
        {
            Dialogos.Advertir("Seleccione la granja de nacimiento y la orientación del animal.");
            return;
        }

        if (NoEsBovino && CategoriaSeleccionada?.Id is null)
        {
            Dialogos.Advertir("Seleccione la categoría del animal.");
            return;
        }

        NacimientoResultado? resultado = null;
        var guardado = await EjecutarAsync(async () =>
        {
            resultado = await _kardex.RegistrarNacimientoAsync(new NacimientoDto
            {
                GranjaId = granjaId,
                FechaNacimiento = FechaNacimiento ?? default,
                ChapetaAmarilla = ChapetaAmarilla,
                ChapetaRoja = Vacio(ChapetaRoja),
                Nombre = Vacio(Nombre),
                Sexo = EsHembra ? Domain.Constants.Sexo.Hembra : Domain.Constants.Sexo.Macho,
                Raza = Raza,
                OrientacionId = orientacionId,
                CategoriaId = NoEsBovino ? CategoriaSeleccionada?.Id : null,
                PesoAlNacerKg = PesoAlNacerKg,
                MadreIdentificacion = MadreIdentificacion,
                EsInseminacion = EsInseminacion,
                RazaPadre = EsInseminacion ? Vacio(RazaPadre) : null,
                NombrePadre = EsInseminacion ? Vacio(NombrePadre) : null,
                PadreChapeta = EsInseminacion ? null : Vacio(PadreChapeta),
            });
        }, "registrar el nacimiento");

        if (!guardado || resultado is null)
        {
            return;
        }

        var mensaje = $"Nacimiento registrado.\n\nCategoría: {resultado.Categoria}\nValor inicial: {Formato.Dinero(resultado.ValorInicial)}";
        if (resultado.MadreActualizada)
        {
            mensaje += "\nSe sumó un parto a la madre.";
        }

        if (resultado.SinTarifa)
        {
            mensaje += "\n\nAtención: la matriz de tarifas vigente no tiene una tarifa para este animal; quedó como «Sin clasificar».";
        }

        Dialogos.Informar(mensaje, "Nacimiento registrado");
        Cerrar(true);
    }

    private async Task ActualizarVistaPreviaAsync()
    {
        if (!_inicializado || FechaNacimiento is not { } fecha || (EsBovino && OrientacionSeleccionada?.Id is null)
            || (NoEsBovino && CategoriaSeleccionada?.Id is null))
        {
            return;
        }

        var orientacionId = EsBovino ? OrientacionSeleccionada!.Id!.Value : 0;

        var version = ++_versionVistaPrevia;
        try
        {
            var resultado = await _animales.PrevisualizarValoracionAsync(
                EsHembra ? Domain.Constants.Sexo.Hembra : Domain.Constants.Sexo.Macho, orientacionId, 0, fecha,
                NoEsBovino ? CategoriaSeleccionada!.Id : null);
            if (version != _versionVistaPrevia)
            {
                return;
            }

            SinTarifa = resultado.SinTarifa;
            CategoriaInicial = resultado.SinTarifa ? "Sin clasificar (no hay tarifa para estos datos)" : resultado.CategoriaNombre;
            EsReferencia = resultado.NoBiologico;
            ValorInicial = Formato.Dinero(resultado.Valor ?? 0m);
        }
        catch (System.ComponentModel.DataAnnotations.ValidationException ex)
        {
            // Fecha futura u otro dato inválido: se muestra en la vista previa y el mensaje completo sale al guardar.
            CategoriaInicial = "—";
            ValorInicial = "—";
            SinTarifa = false;
            Registro.Informacion($"Vista previa de nacimiento no disponible: {ex.Message}");
        }
    }

    private static string? Vacio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
