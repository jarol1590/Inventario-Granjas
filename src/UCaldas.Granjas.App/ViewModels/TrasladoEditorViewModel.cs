using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Traslado de semovientes entre granjas. Genera una Remisión (sin Oficio ni Recibo de Caja) y mueve los animales con su
/// valor contable; la orientación productiva de cada animal en la sede destino se elige por animal. Permite crear,
/// consultar, editar y anular, igual que las salidas.
/// </summary>
public sealed partial class TrasladoEditorViewModel : DialogViewModelBase
{
    private readonly IKardexService _kardex;
    private readonly IConsultasService _consultas;
    private readonly ICatalogoService _catalogo;
    private readonly IFormularios _formularios;
    private readonly ContextoGlobal _contexto;
    private readonly Dictionary<int, int?> _orientacionSugerida = [];
    private int? _remisionId;
    private bool _cargando;
    private bool _revirtiendo;

    public TrasladoEditorViewModel(
        IKardexService kardex,
        IConsultasService consultas,
        ICatalogoService catalogo,
        IFormularios formularios,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _kardex = kardex;
        _consultas = consultas;
        _catalogo = catalogo;
        _formularios = formularios;
        _contexto = contexto;
        _fechaTraslado = reloj.Hoy;
        Animales.CollectionChanged += (_, _) => NotificarTotales();
    }

    public override string Titulo => Modo switch
    {
        ModoDocumento.Nuevo => "Traslado de Semovientes",
        ModoDocumento.Detalle => $"Detalle del traslado {NumeroRemision}",
        _ => $"Editar traslado {NumeroRemision}",
    };

    public override double Ancho => 1060;

    public override double Alto => 740;

    public ObservableCollection<OpcionFiltro> Granjas { get; } = [];

    public ObservableCollection<OpcionFiltro> Orientaciones { get; } = [];

    public ObservableCollection<TrasladoFila> Animales { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo), nameof(EsLectura), nameof(EsEditable), nameof(PuedeEditarODesechar), nameof(PuedeCambiarOrigen), nameof(TextoConfirmar))]
    private ModoDocumento _modo = ModoDocumento.Nuevo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditable), nameof(PuedeEditarODesechar))]
    private bool _anulado;

    [ObservableProperty]
    private OpcionFiltro? _granjaOrigen;

    [ObservableProperty]
    private OpcionFiltro? _granjaDestino;

    [ObservableProperty]
    private DateTime? _fechaTraslado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private string _numeroRemision = string.Empty;

    [ObservableProperty]
    private string _observaciones = string.Empty;

    [ObservableProperty]
    private string _textoBusqueda = string.Empty;

    [ObservableProperty]
    private TrasladoFila? _filaSeleccionada;

    public bool EsLectura => Modo == ModoDocumento.Detalle;

    public bool EsEditable => Modo != ModoDocumento.Detalle && !Anulado;

    public bool PuedeEditarODesechar => EsLectura && !Anulado;

    /// <summary>La granja de origen solo se elige al crear el traslado (no se puede mover un traslado a otro origen).</summary>
    public bool PuedeCambiarOrigen => Modo == ModoDocumento.Nuevo;

    public string TextoConfirmar => Modo == ModoDocumento.Edicion ? "💾 GUARDAR CAMBIOS" : "💾 CONFIRMAR TRASLADO";

    public string CantidadTexto => $"Total Animales a Mover: {Animales.Count}";

    public string ValorTexto => Formato.Dinero(Animales.Sum(a => a.Animal.ValorContable));

    public async Task<bool> InicializarAsync()
    {
        _remisionId = null;
        return await EjecutarAsync(async () =>
        {
            var granjas = await _catalogo.GetGranjasAsync(soloActivas: true);
            _cargando = true;
            try
            {
                await CargarCatalogosAsync(granjas);

                GranjaOrigen = Granjas.FirstOrDefault(g => g.Id == _contexto.GranjaId) ?? Granjas.FirstOrDefault();
                GranjaDestino = Granjas.FirstOrDefault(g => g.Id != GranjaOrigen?.Id);
            }
            finally
            {
                _cargando = false;
            }

            Modo = ModoDocumento.Nuevo;
        }, "preparar el formulario de traslado");
    }

    /// <summary>Abre un traslado ya registrado en modo de consulta (desde ahí se puede editar o anular).</summary>
    public async Task<bool> InicializarExistenteAsync(int remisionId)
    {
        _remisionId = remisionId;
        return await EjecutarAsync(async () =>
        {
            var traslado = await _consultas.ObtenerTrasladoAsync(remisionId);
            var dto = traslado.Dto;

            // Las sedes inactivas siguen apareciendo si son las del documento, para que se vea el traslado completo.
            var granjas = (await _catalogo.GetGranjasAsync())
                .Where(g => g.Activa || g.Id == dto.GranjaOrigenId || g.Id == dto.GranjaDestinoId)
                .ToList();

            _cargando = true;
            try
            {
                await CargarCatalogosAsync(granjas);

                GranjaOrigen = Granjas.FirstOrDefault(g => g.Id == dto.GranjaOrigenId);
                GranjaDestino = Granjas.FirstOrDefault(g => g.Id == dto.GranjaDestinoId);
                FechaTraslado = dto.FechaTraslado;
                NumeroRemision = dto.NumeroRemision;
                Observaciones = dto.Observaciones ?? string.Empty;
                Anulado = traslado.Anulada;

                var lineas = dto.Animales.ToDictionary(a => a.AnimalId);
                Animales.Clear();
                foreach (var animal in traslado.Animales)
                {
                    var linea = lineas[animal.Id];
                    Animales.Add(new TrasladoFila(animal, linea.OrientacionDestinoId ?? animal.OrientacionId, linea.ObservacionesEspecificas));
                }
            }
            finally
            {
                _cargando = false;
            }

            Modo = ModoDocumento.Detalle;
            NotificarTotales();
        }, "abrir el traslado");
    }

    partial void OnModoChanged(ModoDocumento value) => NotificarComandos();

    partial void OnAnuladoChanged(bool value) => NotificarComandos();

    partial void OnGranjaOrigenChanged(OpcionFiltro? oldValue, OpcionFiltro? newValue)
    {
        if (_cargando || _revirtiendo || Modo != ModoDocumento.Nuevo || newValue is null || Animales.Count == 0)
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

    [RelayCommand(CanExecute = nameof(EsEditable))]
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

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private void QuitarAnimal(TrasladoFila? fila)
    {
        if (fila is not null)
        {
            Animales.Remove(fila);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeEditarODesechar))]
    private void EditarTraslado() => Modo = ModoDocumento.Edicion;

    [RelayCommand(CanExecute = nameof(EsEditable))]
    private async Task ConfirmarAsync()
    {
        var origen = GranjaOrigen;
        var destino = GranjaDestino;
        if (origen?.Id is not { } origenId || destino?.Id is not { } destinoId)
        {
            Dialogos.Advertir("Seleccione la granja de origen y la de destino.");
            return;
        }

        var dto = new TrasladoDto
        {
            RemisionId = _remisionId,
            NumeroRemision = NumeroRemision,
            FechaTraslado = FechaTraslado ?? default,
            GranjaOrigenId = origenId,
            GranjaDestinoId = destinoId,
            Observaciones = string.IsNullOrWhiteSpace(Observaciones) ? null : Observaciones.Trim(),
            Animales = [.. Animales.Select(a => a.ToDto())],
        };

        TrasladoResultado? resultado = null;
        var guardado = await EjecutarAsync(
            async () => resultado = _remisionId is null ? await _kardex.RegistrarTrasladoAsync(dto) : await _kardex.EditarTrasladoAsync(dto),
            "guardar el traslado");

        if (!guardado || resultado is null)
        {
            return;
        }

        var accion = _remisionId is null ? "registrado" : "actualizado";
        Dialogos.Informar(
            $"Traslado {accion}: {Formato.Animales(resultado.Animales)} de {origen.Nombre} a {destino.Nombre}.\n" +
            $"Valor trasladado: {Formato.Dinero(resultado.TotalValor)}.",
            $"Traslado {accion}");
        Cerrar(true);
    }

    [RelayCommand(CanExecute = nameof(PuedeEditarODesechar))]
    private async Task AnularAsync()
    {
        if (_remisionId is not { } id)
        {
            return;
        }

        var motivo = AnulacionDeTraslados.PedirMotivo(Dialogos, NumeroRemision, Animales.Count, GranjaOrigen?.Nombre ?? "la granja de origen");
        if (motivo is null)
        {
            return;
        }

        if (await EjecutarAsync(() => _kardex.AnularAsync(id, motivo), "anular el traslado"))
        {
            Dialogos.Informar($"El traslado {NumeroRemision} fue anulado.", "Traslado anulado");
            Cerrar(true);
        }
    }

    private void NotificarComandos()
    {
        AgregarALaListaCommand.NotifyCanExecuteChanged();
        QuitarAnimalCommand.NotifyCanExecuteChanged();
        EditarTrasladoCommand.NotifyCanExecuteChanged();
        ConfirmarCommand.NotifyCanExecuteChanged();
        AnularCommand.NotifyCanExecuteChanged();
    }

    private async Task CargarCatalogosAsync(IReadOnlyList<Granja> granjas)
    {
        Granjas.Clear();
        _orientacionSugerida.Clear();
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
    }

    private void NotificarTotales()
    {
        OnPropertyChanged(nameof(CantidadTexto));
        OnPropertyChanged(nameof(ValorTexto));
    }
}
