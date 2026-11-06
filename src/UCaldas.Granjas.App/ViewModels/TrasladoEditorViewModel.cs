using System.Collections.ObjectModel;
using System.Collections.Specialized;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Traslado de semovientes entre granjas. Genera una Remisión (sin Oficio ni Recibo de Caja) y mueve los animales con su
/// valor contable; la orientación productiva de cada animal en la sede destino se elige por animal.
/// </summary>
public sealed partial class TrasladoEditorViewModel : DialogViewModelBase
{
    private readonly IKardexService _kardex;
    private readonly ICatalogoService _catalogo;
    private readonly IFormularios _formularios;
    private readonly ContextoGlobal _contexto;
    private readonly Dictionary<int, int?> _orientacionSugerida = [];
    private bool _cargando;
    private bool _revirtiendo;

    public TrasladoEditorViewModel(
        IKardexService kardex,
        ICatalogoService catalogo,
        IFormularios formularios,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _kardex = kardex;
        _catalogo = catalogo;
        _formularios = formularios;
        _contexto = contexto;
        _fechaTraslado = reloj.Hoy;
        Animales.CollectionChanged += (_, _) => NotificarTotales();
    }

    public override string Titulo => "Traslado de Semovientes";

    public override double Ancho => 1060;

    public override double Alto => 740;

    public ObservableCollection<OpcionFiltro> Granjas { get; } = [];

    public ObservableCollection<OpcionFiltro> Orientaciones { get; } = [];

    public ObservableCollection<TrasladoFila> Animales { get; } = [];

    [ObservableProperty]
    private OpcionFiltro? _granjaOrigen;

    [ObservableProperty]
    private OpcionFiltro? _granjaDestino;

    [ObservableProperty]
    private DateTime? _fechaTraslado;

    [ObservableProperty]
    private string _numeroRemision = string.Empty;

    [ObservableProperty]
    private string _observaciones = string.Empty;

    [ObservableProperty]
    private string _textoBusqueda = string.Empty;

    [ObservableProperty]
    private TrasladoFila? _filaSeleccionada;

    public string CantidadTexto => $"Total Animales a Mover: {Animales.Count}";

    public string ValorTexto => Formato.Dinero(Animales.Sum(a => a.Animal.ValorContable));

    public async Task<bool> InicializarAsync()
    {
        return await EjecutarAsync(async () =>
        {
            var granjas = await _catalogo.GetGranjasAsync(soloActivas: true);
            _cargando = true;
            try
            {
                Granjas.Clear();
                foreach (var g in granjas)
                {
                    Granjas.Add(new OpcionFiltro(g.Id, g.Nombre));
                    _orientacionSugerida[g.Id] = g.OrientacionId;
                }

                Orientaciones.Clear();
                foreach (var o in await _catalogo.GetOrientacionesAsync())
                {
                    Orientaciones.Add(new OpcionFiltro(o.Id, o.Nombre));
                }

                GranjaOrigen = Granjas.FirstOrDefault(g => g.Id == _contexto.GranjaId) ?? Granjas.FirstOrDefault();
                GranjaDestino = Granjas.FirstOrDefault(g => g.Id != GranjaOrigen?.Id);
            }
            finally
            {
                _cargando = false;
            }
        }, "preparar el formulario de traslado");
    }

    partial void OnGranjaOrigenChanged(OpcionFiltro? oldValue, OpcionFiltro? newValue)
    {
        if (_cargando || _revirtiendo || newValue is null || Animales.Count == 0)
        {
            return;
        }

        if (!Dialogos.Confirmar(
                "Al cambiar la granja de origen se quitarán de la lista los animales ya agregados, porque pertenecen a la granja anterior.\n\n¿Desea continuar?",
                "Cambiar granja de origen"))
        {
            ContextoUi.Diferir(() =>
            {
                _revirtiendo = true;
                try
                {
                    GranjaOrigen = oldValue;
                }
                finally
                {
                    _revirtiendo = false;
                }
            });
            return;
        }

        Animales.Clear();
    }

    partial void OnGranjaDestinoChanged(OpcionFiltro? value)
    {
        if (_cargando || value?.Id is not { } destinoId)
        {
            return;
        }

        // La orientación sugerida de la sede destino reemplaza la de cada fila (se puede volver a cambiar por animal).
        if (_orientacionSugerida.GetValueOrDefault(destinoId) is { } sugerida)
        {
            foreach (var fila in Animales)
            {
                fila.OrientacionDestinoId = sugerida;
            }
        }
    }

    [RelayCommand]
    private async Task AgregarALaListaAsync()
    {
        if (GranjaOrigen?.Id is not { } origenId)
        {
            Dialogos.Advertir("Seleccione primero la granja de origen.");
            return;
        }

        var elegidos = await _formularios.SeleccionarAnimalesAsync(
            origenId, [.. Animales.Select(a => a.AnimalId)], string.IsNullOrWhiteSpace(TextoBusqueda) ? null : TextoBusqueda.Trim());
        if (elegidos is null)
        {
            return;
        }

        var sugerida = GranjaDestino?.Id is { } destinoId ? _orientacionSugerida.GetValueOrDefault(destinoId) : null;
        foreach (var animal in elegidos)
        {
            Animales.Add(new TrasladoFila(animal, sugerida ?? animal.OrientacionId));
        }

        TextoBusqueda = string.Empty;
    }

    [RelayCommand]
    private void QuitarAnimal(TrasladoFila? fila)
    {
        if (fila is not null)
        {
            Animales.Remove(fila);
        }
    }

    [RelayCommand]
    private async Task ConfirmarAsync()
    {
        var origen = GranjaOrigen;
        var destino = GranjaDestino;
        if (origen?.Id is not { } origenId || destino?.Id is not { } destinoId)
        {
            Dialogos.Advertir("Seleccione la granja de origen y la de destino.");
            return;
        }

        TrasladoResultado? resultado = null;
        var guardado = await EjecutarAsync(async () =>
        {
            resultado = await _kardex.RegistrarTrasladoAsync(new TrasladoDto
            {
                NumeroRemision = NumeroRemision,
                FechaTraslado = FechaTraslado ?? default,
                GranjaOrigenId = origenId,
                GranjaDestinoId = destinoId,
                Observaciones = string.IsNullOrWhiteSpace(Observaciones) ? null : Observaciones.Trim(),
                Animales = [.. Animales.Select(a => a.ToDto())],
            });
        }, "registrar el traslado");

        if (!guardado || resultado is null)
        {
            return;
        }

        Dialogos.Informar(
            $"Traslado registrado: {Formato.Animales(resultado.Animales)} de {origen.Nombre} a {destino.Nombre}.\n" +
            $"Valor trasladado: {Formato.Dinero(resultado.TotalValor)}.",
            "Traslado registrado");
        Cerrar(true);
    }

    private void NotificarTotales()
    {
        OnPropertyChanged(nameof(CantidadTexto));
        OnPropertyChanged(nameof(ValorTexto));
    }
}
