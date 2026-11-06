using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
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

    public ObservableCollection<string> Razas { get; } = [];

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

            foreach (var r in await _catalogo.GetRazasAsync())
            {
                Razas.Add(r);
            }

            GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == _contexto.GranjaId) ?? Granjas.FirstOrDefault();
            var sugerida = granjas.FirstOrDefault(g => g.Id == GranjaSeleccionada?.Id)?.OrientacionId;
            OrientacionSeleccionada = Orientaciones.FirstOrDefault(o => o.Id == sugerida) ?? Orientaciones.FirstOrDefault();
            _inicializado = true;
            await ActualizarVistaPreviaAsync();
        }, "preparar el formulario de nacimiento");
        return cargado;
    }

    partial void OnFechaNacimientoChanged(DateTime? value) => _ = ActualizarVistaPreviaAsync();

    partial void OnEsHembraChanged(bool value) => _ = ActualizarVistaPreviaAsync();

    partial void OnOrientacionSeleccionadaChanged(OpcionFiltro? value) => _ = ActualizarVistaPreviaAsync();

    [RelayCommand]
    private async Task GuardarAsync()
    {
        if (GranjaSeleccionada?.Id is not { } granjaId || OrientacionSeleccionada?.Id is not { } orientacionId)
        {
            Dialogos.Advertir("Seleccione la granja de nacimiento y la orientación del animal.");
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
        if (!_inicializado || FechaNacimiento is not { } fecha || OrientacionSeleccionada?.Id is not { } orientacionId)
        {
            return;
        }

        var version = ++_versionVistaPrevia;
        try
        {
            var resultado = await _animales.PrevisualizarValoracionAsync(
                EsHembra ? Domain.Constants.Sexo.Hembra : Domain.Constants.Sexo.Macho, orientacionId, 0, fecha);
            if (version != _versionVistaPrevia)
            {
                return;
            }

            SinTarifa = resultado.SinTarifa;
            CategoriaInicial = resultado.SinTarifa ? "Sin clasificar (no hay tarifa para estos datos)" : resultado.CategoriaNombre;
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
