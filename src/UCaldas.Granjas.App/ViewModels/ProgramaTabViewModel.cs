using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Opción de un combo de filtro por texto (estado, sexo); <see cref="Valor"/> nulo = "todos".</summary>
public sealed record OpcionTexto(string? Valor, string Nombre);

/// <summary>
/// Pestaña de Animales: un programa de inventario individual (bovinos, equinos, caprinos...) con su grilla de animales,
/// sus filtros, las categorías biológicas de su especie y el resumen con el valor contable (saldo del Kardex).
/// Todas las pestañas comparten la misma vista; aquí solo cambia el programa por el que se consulta.
/// </summary>
public sealed partial class ProgramaTabViewModel : ViewModelBase
{
    private static readonly TimeSpan RetardoDeBusqueda = TimeSpan.FromMilliseconds(300);

    private readonly IConsultasService _consultas;
    private readonly ICatalogoService _catalogo;
    private readonly IFormularios _formularios;
    private readonly ContextoGlobal _contexto;
    private CancellationTokenSource? _retardo;
    private bool _catalogosCargados;
    private bool _cargandoOpciones;
    private int _version;

    public ProgramaTabViewModel(
        Programa programa,
        IConsultasService consultas,
        ICatalogoService catalogo,
        IFormularios formularios,
        ContextoGlobal contexto,
        IDialogService dialogos)
        : base(dialogos)
    {
        Programa = programa;
        Especie = EspecieDePrograma.De(programa);
        _consultas = consultas;
        _catalogo = catalogo;
        _formularios = formularios;
        _contexto = contexto;

        Estados =
        [
            new(EstadoAnimal.Disponible, "Disponible"),
            new(EstadoAnimal.Vendido, "Vendido"),
            new(EstadoAnimal.Muerto, "Muerto"),
            new(EstadoAnimal.Hurtado, "Hurtado"),
            new(null, "Todos"),
        ];
        Sexos = [new(null, "Todos"), new(Sexo.Hembra, "Hembra"), new(Sexo.Macho, "Macho")];
        _estadoSeleccionado = Estados[0];
        _sexoSeleccionado = Sexos[0];
        _resumenTexto = $"Mostrando: 0 {Especie.Plural}";
    }

    /// <summary>El programa de inventario individual que representa la pestaña.</summary>
    public Programa Programa { get; }

    public EspecieDePrograma Especie { get; }

    /// <summary>Encabezado de la pestaña («Bovinos»).</summary>
    public string Titulo => Especie.Titulo;

    public string Icono => Especie.Icono;

    public string TituloGestion => $"Gestión de {Especie.Plural}";

    public ObservableCollection<AnimalListItem> Animales { get; } = [];

    public ObservableCollection<OpcionFiltro> Orientaciones { get; } = [];

    /// <summary>Solo las categorías biológicas del programa (las de otra especie no aparecen).</summary>
    public ObservableCollection<OpcionFiltro> Categorias { get; } = [];

    public IReadOnlyList<OpcionTexto> Estados { get; }

    public IReadOnlyList<OpcionTexto> Sexos { get; }

    [ObservableProperty]
    private string _texto = string.Empty;

    [ObservableProperty]
    private OpcionFiltro? _orientacionSeleccionada;

    [ObservableProperty]
    private OpcionFiltro? _categoriaSeleccionada;

    [ObservableProperty]
    private OpcionTexto _estadoSeleccionado;

    [ObservableProperty]
    private OpcionTexto _sexoSeleccionado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirFichaCommand))]
    private AnimalListItem? _seleccionado;


    [ObservableProperty]
    private string _resumenTexto;

    [ObservableProperty]
    private string _totalTexto = Formato.Dinero(0);

    /// <summary>Consulta los animales del programa con los filtros de la pestaña y el filtro global de granja.</summary>
    public async Task CargarAsync()
    {
        var version = ++_version;
        await EjecutarAsync(async () =>
        {
            if (!_catalogosCargados)
            {
                await CargarOpcionesAsync();
            }

            var filtro = new AnimalFiltro(
                GranjaId: _contexto.GranjaId,
                ProgramaId: Programa.Id,
                Texto: string.IsNullOrWhiteSpace(Texto) ? null : Texto.Trim(),
                OrientacionId: OrientacionSeleccionada?.Id,
                Estado: EstadoSeleccionado.Valor,
                Sexo: SexoSeleccionado.Valor,
                CategoriaId: CategoriaSeleccionada?.Id);

            var resultado = await _consultas.BuscarAnimalesAsync(filtro);
            if (version != _version)
            {
                return; // llegó una búsqueda más reciente
            }

            Animales.Clear();
            foreach (var item in resultado.Items)
            {
                Animales.Add(item);
            }

            var resumen = resultado.Resumen;
            var unidad = resumen.Cantidad == 1 ? Especie.Singular : Especie.Plural;
            ResumenTexto = $"Mostrando: {resumen.Cantidad:N0} {unidad} {DescribirEstado(EstadoSeleccionado.Valor, resumen.Cantidad)}";
            TotalTexto = Formato.Dinero(resumen.ValorContableTotal);
        }, $"consultar los animales ({Especie.Titulo})");
    }

    /// <summary>Abre la ficha técnica del animal (botón de la fila, doble clic en la tabla o botón "Ver ficha técnica").</summary>
    [RelayCommand(CanExecute = nameof(PuedeAbrirFicha))]
    private async Task AbrirFichaAsync(AnimalListItem? item)
    {
        item ??= Seleccionado;
        if (item is not null && await _formularios.VerFichaAsync(item.Id))
        {
            await CargarAsync();
        }
    }

    // El botón de cada fila pasa su propio animal; los demás usan el seleccionado.
    private bool PuedeAbrirFicha(AnimalListItem? item) => (item ?? Seleccionado) is not null;

    partial void OnTextoChanged(string value) => _ = BuscarConRetardoAsync();

    partial void OnOrientacionSeleccionadaChanged(OpcionFiltro? value) => RecargarSiCorresponde();

    partial void OnCategoriaSeleccionadaChanged(OpcionFiltro? value) => RecargarSiCorresponde();

    partial void OnEstadoSeleccionadoChanged(OpcionTexto value) => RecargarSiCorresponde();

    partial void OnSexoSeleccionadoChanged(OpcionTexto value) => RecargarSiCorresponde();

    private void RecargarSiCorresponde()
    {
        if (!_cargandoOpciones)
        {
            _ = CargarAsync();
        }
    }

    // La búsqueda por texto espera a que el usuario deje de escribir para no consultar la base en cada tecla.
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

        await CargarAsync();
    }

    private async Task CargarOpcionesAsync()
    {
        _cargandoOpciones = true;
        try
        {
            var orientaciones = await _catalogo.GetOrientacionesAsync();
            Orientaciones.Clear();
            Orientaciones.Add(new OpcionFiltro(null, "Todos"));
            foreach (var o in orientaciones)
            {
                Orientaciones.Add(new OpcionFiltro(o.Id, o.Nombre));
            }

            var categorias = await _catalogo.GetCategoriasDeProgramaAsync(Programa.Id);
            Categorias.Clear();
            Categorias.Add(new OpcionFiltro(null, "Todas"));
            foreach (var c in categorias)
            {
                Categorias.Add(new OpcionFiltro(c.Id, c.Nombre));
            }

            OrientacionSeleccionada = Orientaciones[0];
            CategoriaSeleccionada = Categorias[0];
            _catalogosCargados = true;
        }
        finally
        {
            _cargandoOpciones = false;
        }
    }

    private static string DescribirEstado(string? estado, int cantidad) => estado switch
    {
        EstadoAnimal.Disponible => cantidad == 1 ? "Disponible" : "Disponibles",
        EstadoAnimal.Vendido => cantidad == 1 ? "Vendido" : "Vendidos",
        EstadoAnimal.Muerto => cantidad == 1 ? "Muerto" : "Muertos",
        EstadoAnimal.Hurtado => cantidad == 1 ? "Hurtado" : "Hurtados",
        _ => "(todos los estados)",
    };
}
