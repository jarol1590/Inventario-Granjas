using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Comercial;

namespace UCaldas.Granjas.App.ViewModels.Remisiones;

/// <summary>Ventana para crear o corregir un oficio de venta: número, fecha, programa exclusivo y observaciones.</summary>
public sealed partial class NuevoOficioViewModel : DialogViewModelBase
{
    private readonly IOficioService _oficios;
    private readonly ICatalogoService _catalogo;
    private readonly IReloj _reloj;
    private readonly OficioResumen? _existente;

    public NuevoOficioViewModel(IOficioService oficios, ICatalogoService catalogo, IReloj reloj, IDialogService dialogos, OficioResumen? existente = null)
        : base(dialogos)
    {
        _oficios = oficios;
        _catalogo = catalogo;
        _reloj = reloj;
        _existente = existente;
        _numeroOficio = existente?.Oficio.NumeroOficio ?? string.Empty;
        _fecha = existente?.Oficio.FechaOficio ?? reloj.Hoy;
        _observaciones = existente?.Oficio.Observaciones ?? string.Empty;
    }

    public bool EsNuevo => _existente is null;

    public override string Titulo => EsNuevo ? "Crear nuevo oficio de venta" : $"Editar oficio {_existente!.Oficio.NumeroOficio}";

    public override double Ancho => 580;

    public ObservableCollection<Programa> Programas { get; } = [];

    /// <summary>Un oficio con remisiones ya no puede cambiar de programa.</summary>
    public bool PuedeCambiarPrograma => _existente is null || _existente.Remisiones == 0;

    [ObservableProperty]
    private string _numeroOficio;

    [ObservableProperty]
    private DateTime? _fecha;

    [ObservableProperty]
    private Programa? _programaSeleccionado;

    [ObservableProperty]
    private string _observaciones;

    public async Task<bool> InicializarAsync(int? programaSugeridoId = null)
    {
        return await EjecutarAsync(async () =>
        {
            foreach (var p in await _catalogo.GetProgramasAsync())
            {
                Programas.Add(p);
            }

            ProgramaSeleccionado = Programas.FirstOrDefault(p => p.Id == (_existente?.Oficio.ProgramaId ?? programaSugeridoId)) ?? Programas.FirstOrDefault();
            if (EsNuevo)
            {
                await ProponerNumeroAsync();
            }

            _listo = true;
        }, "preparar el oficio");
    }

    private bool _listo;

    /// <summary>Con otro programa cambia el prefijo y el consecutivo: se vuelve a proponer el número.</summary>
    partial void OnProgramaSeleccionadoChanged(Programa? value)
    {
        if (_listo && EsNuevo)
        {
            _ = EjecutarAsync(ProponerNumeroAsync, "proponer el número del oficio");
        }
    }

    private async Task ProponerNumeroAsync()
    {
        if (ProgramaSeleccionado is { } programa)
        {
            NumeroOficio = await _oficios.SiguienteNumeroAsync(programa.Id);
        }
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var dto = new OficioDto
        {
            OficioId = _existente?.Oficio.Id ?? 0,
            NumeroOficio = NumeroOficio,
            ProgramaId = ProgramaSeleccionado?.Id ?? 0,
            Fecha = Fecha ?? default,
            Observaciones = Observaciones,
        };
        var guardado = await EjecutarAsync(async () =>
        {
            if (EsNuevo)
            {
                await _oficios.CrearAsync(dto);
            }
            else
            {
                await _oficios.ActualizarAsync(dto);
            }
        }, "guardar el oficio");

        if (guardado)
        {
            Cerrar(true);
        }
    }
}

/// <summary>
/// Oficios de venta: lista maestro-detalle con las remisiones de cada oficio. Un oficio se puede cerrar (no admite remisiones
/// nuevas) y reabrir; lo ya emitido dentro de un oficio cerrado sigue siendo editable.
/// </summary>
public sealed partial class OficiosViewModel : ViewModelBase, IUsaFiltroGlobal
{
    private readonly IOficioService _oficios;
    private readonly IRemisionesQueryService _consultas;
    private readonly IFormulariosRemisiones _formularios;
    private readonly ContextoGlobal _contexto;
    private int _version;

    public OficiosViewModel(
        IOficioService oficios,
        IRemisionesQueryService consultas,
        IFormulariosRemisiones formularios,
        ContextoGlobal contexto,
        IDialogService dialogos)
        : base(dialogos)
    {
        _oficios = oficios;
        _consultas = consultas;
        _formularios = formularios;
        _contexto = contexto;
    }

    public ObservableCollection<OficioResumen> Oficios { get; } = [];

    public ObservableCollection<RemisionFila> Remisiones { get; } = [];

    [ObservableProperty]
    private string _texto = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TituloDetalle), nameof(TextoCerrarAbrir), nameof(EstaCerrado), nameof(HaySeleccion))]
    [NotifyCanExecuteChangedFor(nameof(EditarOficioCommand), nameof(CerrarOAbrirCommand), nameof(AgregarRemisionCommand))]
    private OficioResumen? _seleccionado;

    [ObservableProperty]
    private RemisionFila? _remisionSeleccionada;

    public bool HaySeleccion => Seleccionado is not null;

    public bool EstaCerrado => Seleccionado?.Oficio.Cerrado == true;

    public string TextoCerrarAbrir => EstaCerrado ? "🔓  ABRIR OFICIO" : "🔒  CERRAR OFICIO";

    public string TituloDetalle => Seleccionado is null
        ? "Seleccione un oficio para ver sus remisiones"
        : $"Remisiones de venta vinculadas al oficio {Seleccionado.Oficio.NumeroOficio} ({Seleccionado.ProgramaNombre})"
          + (EstaCerrado ? " — OFICIO CERRADO" : string.Empty);

    partial void OnTextoChanged(string value) => _ = CargarAsync();

    partial void OnSeleccionadoChanged(OficioResumen? value) => _ = CargarRemisionesAsync();

    public async Task CargarAsync()
    {
        var version = ++_version;
        await EjecutarAsync(async () =>
        {
            var previo = Seleccionado?.Oficio.Id;
            var lista = await _oficios.ListarAsync(_contexto.ProgramaId, string.IsNullOrWhiteSpace(Texto) ? null : Texto);
            if (version != _version)
            {
                return;
            }

            Oficios.Clear();
            foreach (var o in lista)
            {
                Oficios.Add(o);
            }

            Seleccionado = Oficios.FirstOrDefault(o => o.Oficio.Id == previo) ?? Oficios.FirstOrDefault();
        }, "consultar los oficios");
    }

    private async Task CargarRemisionesAsync()
    {
        Remisiones.Clear();
        if (Seleccionado is not { } actual)
        {
            return;
        }

        await EjecutarAsync(async () =>
        {
            foreach (var r in await _consultas.ListarDeOficioAsync(actual.Oficio.Id))
            {
                Remisiones.Add(r);
            }
        }, "consultar las remisiones del oficio");
    }

    [RelayCommand]
    private async Task NuevoOficioAsync()
    {
        if (await _formularios.NuevoOficioAsync(_contexto.ProgramaId))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task EditarOficioAsync()
    {
        if (Seleccionado is { } o && await _formularios.EditarOficioAsync(o))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task CerrarOAbrirAsync()
    {
        if (Seleccionado is not { } actual)
        {
            return;
        }

        var cerrar = !actual.Oficio.Cerrado;
        var mensaje = cerrar
            ? $"El oficio {actual.Oficio.NumeroOficio} dejará de admitir remisiones nuevas. Lo ya emitido seguirá siendo editable y el oficio se podrá abrir de nuevo.\n\n¿Cerrar el oficio?"
            : $"El oficio {actual.Oficio.NumeroOficio} volverá a admitir remisiones nuevas.\n\n¿Abrir el oficio?";
        if (!Dialogos.Confirmar(mensaje, cerrar ? "Cerrar oficio" : "Abrir oficio"))
        {
            return;
        }

        var id = actual.Oficio.Id;
        if (await EjecutarAsync(() => cerrar ? _oficios.CerrarAsync(id) : _oficios.ReabrirAsync(id), cerrar ? "cerrar el oficio" : "abrir el oficio"))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeAgregarRemision))]
    private async Task AgregarRemisionAsync()
    {
        if (Seleccionado is { } o && await _formularios.NuevaRemisionAsync(o.Oficio.Id))
        {
            await CargarAsync();
        }
    }

    private bool PuedeAgregarRemision() => Seleccionado is { Oficio.Cerrado: false };

    [RelayCommand]
    private async Task VerRemisionAsync(RemisionFila? fila)
    {
        fila ??= RemisionSeleccionada;
        if (fila is not null && await _formularios.AbrirRemisionAsync(fila.Id))
        {
            await CargarAsync();
        }
    }
}
