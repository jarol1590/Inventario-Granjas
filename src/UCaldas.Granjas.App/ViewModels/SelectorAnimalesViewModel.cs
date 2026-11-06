using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Un animal del selector con su casilla de marcado.</summary>
public sealed partial class FilaSeleccionable(AnimalSeleccionable animal) : ObservableObject
{
    public AnimalSeleccionable Animal { get; } = animal;

    [ObservableProperty]
    private bool _marcado;
}

/// <summary>Ventana para elegir varios animales Disponibles de una granja (documentos de salida y de traslado).</summary>
public sealed partial class SelectorAnimalesViewModel : DialogViewModelBase
{
    private static readonly TimeSpan RetardoDeBusqueda = TimeSpan.FromMilliseconds(250);

    private readonly IConsultasService _consultas;
    private int _granjaId;
    private HashSet<int> _excluidos = [];
    private CancellationTokenSource? _retardo;
    private int _version;
    private bool _inicializando;

    public SelectorAnimalesViewModel(IConsultasService consultas, IDialogService dialogos)
        : base(dialogos)
    {
        _consultas = consultas;
    }

    public override string Titulo => "Seleccionar animales";

    public override double Ancho => 820;

    public override double Alto => 560;

    public ObservableCollection<FilaSeleccionable> Animales { get; } = [];

    /// <summary>Los animales marcados cuando el usuario aceptó.</summary>
    public IReadOnlyList<AnimalSeleccionable> Seleccionados { get; private set; } = [];

    [ObservableProperty]
    private string _texto = string.Empty;

    [ObservableProperty]
    private string _resumen = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarCommand))]
    private int _cantidadMarcada;

    public async Task<bool> InicializarAsync(int granjaId, IReadOnlyCollection<int> excluidos, string? textoInicial)
    {
        _granjaId = granjaId;
        _excluidos = [.. excluidos];
        _inicializando = true;
        Texto = textoInicial ?? string.Empty;
        _inicializando = false;
        return await EjecutarAsync(BuscarAsync, "buscar los animales");
    }

    partial void OnTextoChanged(string value)
    {
        if (!_inicializando)
        {
            _ = BuscarConRetardoAsync();
        }
    }

    [RelayCommand]
    private void MarcarTodos()
    {
        foreach (var fila in Animales)
        {
            fila.Marcado = true;
        }

        ActualizarConteo();
    }

    [RelayCommand]
    private void QuitarMarcas()
    {
        foreach (var fila in Animales)
        {
            fila.Marcado = false;
        }

        ActualizarConteo();
    }

    [RelayCommand(CanExecute = nameof(HayMarcados))]
    private void Agregar()
    {
        Seleccionados = Animales.Where(f => f.Marcado).Select(f => f.Animal).ToList();
        Cerrar(true);
    }

    private void ActualizarConteo() => CantidadMarcada = Animales.Count(f => f.Marcado);

    private void AlCambiarFila(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilaSeleccionable.Marcado))
        {
            ActualizarConteo();
        }
    }

    private bool HayMarcados() => CantidadMarcada > 0;

    private async Task BuscarConRetardoAsync()
    {
        _retardo?.Cancel();
        var origen = _retardo = new CancellationTokenSource();
        try
        {
            await Task.Delay(RetardoDeBusqueda, origen.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await EjecutarAsync(BuscarAsync, "buscar los animales");
    }

    private async Task BuscarAsync()
    {
        var version = ++_version;
        var encontrados = await _consultas.BuscarDisponiblesAsync(_granjaId, string.IsNullOrWhiteSpace(Texto) ? null : Texto.Trim());
        if (version != _version)
        {
            return;
        }

        var marcados = Animales.Where(f => f.Marcado).Select(f => f.Animal.Id).ToHashSet();
        foreach (var anterior in Animales)
        {
            anterior.PropertyChanged -= AlCambiarFila;
        }

        Animales.Clear();
        foreach (var animal in encontrados.Where(a => !_excluidos.Contains(a.Id)))
        {
            var fila = new FilaSeleccionable(animal) { Marcado = marcados.Contains(animal.Id) };
            fila.PropertyChanged += AlCambiarFila;
            Animales.Add(fila);
        }

        Resumen = Animales.Count == 0
            ? "No hay animales disponibles que coincidan en esta granja."
            : $"{Formato.Animales(Animales.Count)} {(Animales.Count == 1 ? "disponible" : "disponibles")} en la granja de origen.";
        ActualizarConteo();
    }
}
