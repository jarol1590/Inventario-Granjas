using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels.Configuracion;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Respaldos;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Opción de un combo de filtro; <see cref="Id"/> nulo = "todas".</summary>
public sealed record OpcionFiltro(int? Id, string Nombre);

/// <summary>
/// Ventana principal: barra de módulos, menú lateral, filtros globales (Granja / Programa), sección activa y acciones
/// rápidas. Es también quien cierra la aplicación (con el respaldo automático configurado).
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase, INavegador
{
    public const string ClaveInicio = "inicio";
    public const string ClaveBovinos = "bovinos";
    public const string ClaveLotes = "lotes";
    public const string ClaveCompras = "compras";
    public const string ClaveSalidas = "salidas";
    public const string ClaveTraslados = "traslados";
    public const string ClaveInformes = "informes";
    public const string ClaveConfiguracion = "configuracion";

    private readonly IServiceProvider _servicios;
    private readonly ICatalogoService _catalogo;
    private readonly IBackupService _respaldos;
    private readonly IFormularios _formularios;
    private readonly ContextoGlobal _contexto;
    private readonly BarraDePestanas _barraModulos;
    private readonly Dictionary<string, Pestana> _menuPorClave;
    private bool _cargandoFiltros;

    public ShellViewModel(
        IServiceProvider servicios,
        ICatalogoService catalogo,
        IBackupService respaldos,
        IFormularios formularios,
        ContextoGlobal contexto,
        IDialogService dialogos)
        : base(dialogos)
    {
        _servicios = servicios;
        _catalogo = catalogo;
        _respaldos = respaldos;
        _formularios = formularios;
        _contexto = contexto;

        var opciones = new List<Pestana>
        {
            new("🏠", "Inicio", ClaveInicio),
            new("🐄", "Bovinos", ClaveBovinos),
            new("📦", "Lotes", ClaveLotes, Habilitada: false, Ayuda: "Inventario por lotes (aves, peces): no disponible en esta versión."),
            new("🛒", "Compras", ClaveCompras),
            new("📤", "Salidas/Bajas", ClaveSalidas),
            new("🔄", "Traslados", ClaveTraslados),
            new("📊", "Informes", ClaveInformes),
            new("⚙", "Configuración", ClaveConfiguracion),
        };
        _menuPorClave = opciones.ToDictionary(o => (string)o.Contenido!);
        Menu = new BarraDePestanas(opciones, opciones[0])
        {
            PuedeCambiar = _ => Contenido?.PuedeSalir() ?? true,
        };
        Menu.CambioConfirmado += pestana => _ = NavegarAsync((string)pestana.Contenido!);

        var modulos = new List<Pestana>
        {
            new("🐄", "SISTEMA GRANJAS"),
            new("📦", "SUMINISTROS E INSUMOS", Habilitada: false, Ayuda: "Módulo fuera del alcance de esta versión."),
            new("🛒", "REMISIONES", Habilitada: false, Ayuda: "Módulo fuera del alcance de esta versión."),
        };
        _barraModulos = new BarraDePestanas(modulos, modulos[0]);
        _barraSuperior = _barraModulos;
    }

    /// <summary>La asigna el arranque: cierra la aplicación una vez hecho el respaldo.</summary>
    public Action? CerrarAplicacion { get; set; }

    /// <summary>true cuando ya se confirmó la salida; evita repetir el flujo en el evento Closing de la ventana.</summary>
    public bool SalidaAutorizada { get; private set; }

    public BarraDePestanas Menu { get; }

    [ObservableProperty]
    private BarraDePestanas _barraSuperior;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarFiltros))]
    [NotifyPropertyChangedFor(nameof(MostrarAccionesRapidas))]
    private ISeccion? _contenido;

    public bool MostrarFiltros => Contenido is IUsaFiltroGlobal;

    public bool MostrarAccionesRapidas => Contenido is IConAccionesRapidas;

    public ObservableCollection<OpcionFiltro> Granjas { get; } = [];

    public ObservableCollection<Programa> Programas { get; } = [];

    [ObservableProperty]
    private OpcionFiltro? _granjaSeleccionada;

    [ObservableProperty]
    private Programa? _programaSeleccionado;

    /// <summary>Carga los filtros y abre la pantalla de Inicio.</summary>
    public async Task IniciarAsync()
    {
        await EjecutarAsync(async () =>
        {
            await CargarFiltrosAsync();
            Menu.SeleccionarSinNotificar(_menuPorClave[ClaveInicio]);
            await NavegarCoreAsync(ClaveInicio);
        }, "iniciar el sistema");
    }

    /// <summary>Abre otra sección desde código (acciones rápidas) manteniendo el menú sincronizado.</summary>
    public void IrA(string clave)
    {
        if (_menuPorClave.TryGetValue(clave, out var pestana))
        {
            Menu.Seleccionada = pestana;
        }
    }

    public Task RecargarTodoAsync() => IniciarAsync();

    public async Task RecargarFiltrosAsync()
        => await EjecutarAsync(CargarFiltrosAsync, "actualizar los filtros");

    [RelayCommand]
    private async Task RegistrarNacimientoAsync()
    {
        if (await _formularios.NuevoNacimientoAsync())
        {
            await RecargarContenidoAsync();
        }
    }

    [RelayCommand]
    private async Task RegistrarCompraAsync()
    {
        if (await _formularios.NuevaCompraAsync())
        {
            await RecargarContenidoAsync();
        }
    }

    [RelayCommand]
    private void IrASalidas() => IrA(ClaveSalidas);

    [RelayCommand]
    private void IrAInformes() => IrA(ClaveInformes);

    /// <summary>Recarga la sección activa (p. ej. tras registrar un movimiento desde las acciones rápidas).</summary>
    public async Task RecargarContenidoAsync()
    {
        if (Contenido is { } seccion)
        {
            await EjecutarAsync(seccion.CargarAsync, "actualizar la pantalla");
        }
    }

    [RelayCommand]
    private async Task SalirAsync()
    {
        if (Contenido?.PuedeSalir() == false)
        {
            return;
        }

        await EjecutarAsync(async () =>
        {
            var (exito, mensaje) = await _respaldos.RespaldoAlSalirAsync();
            if (!exito && !Dialogos.Confirmar($"{mensaje}\n\n¿Desea salir de todos modos?", "Respaldo automático", destructivo: true))
            {
                return;
            }

            SalidaAutorizada = true;
            CerrarAplicacion?.Invoke();
        }, "cerrar el sistema");
    }

    partial void OnGranjaSeleccionadaChanged(OpcionFiltro? value)
    {
        if (_cargandoFiltros || value is null)
        {
            return;
        }

        _ = EjecutarAsync(async () =>
        {
            _cargandoFiltros = true;
            try
            {
                await CargarProgramasAsync(value.Id);
            }
            finally
            {
                _cargandoFiltros = false;
            }

            AplicarFiltros();
            await RecargarContenidoSiUsaFiltrosAsync();
        }, "aplicar el filtro de granja");
    }

    partial void OnProgramaSeleccionadoChanged(Programa? value)
    {
        if (_cargandoFiltros || value is null)
        {
            return;
        }

        _ = EjecutarAsync(async () =>
        {
            AplicarFiltros();
            await RecargarContenidoSiUsaFiltrosAsync();
        }, "aplicar el filtro de programa");
    }

    partial void OnContenidoChanged(ISeccion? value)
        => BarraSuperior = (value as IConBarraSuperior)?.BarraSuperior ?? _barraModulos;

    private async Task NavegarAsync(string clave)
        => await EjecutarAsync(() => NavegarCoreAsync(clave), "abrir la sección");

    private async Task NavegarCoreAsync(string clave)
    {
        var seccion = CrearSeccion(clave);
        Contenido = seccion;
        await seccion.CargarAsync();
    }

    private ISeccion CrearSeccion(string clave) => clave switch
    {
        ClaveInicio => _servicios.GetRequiredService<InicioViewModel>(),
        ClaveBovinos => _servicios.GetRequiredService<BovinosViewModel>(),
        ClaveCompras => _servicios.GetRequiredService<ComprasViewModel>(),
        ClaveSalidas => _servicios.GetRequiredService<SalidasViewModel>(),
        ClaveTraslados => _servicios.GetRequiredService<TrasladosViewModel>(),
        ClaveInformes => _servicios.GetRequiredService<InformesViewModel>(),
        ClaveConfiguracion => _servicios.GetRequiredService<ConfiguracionViewModel>(),
        _ => throw new InvalidOperationException($"La sección «{clave}» no está disponible."),
    };

    private async Task RecargarContenidoSiUsaFiltrosAsync()
    {
        if (Contenido is IUsaFiltroGlobal seccion)
        {
            await seccion.CargarAsync();
        }
    }

    /// <summary>Recarga los combos conservando la granja y el programa elegidos mientras sigan existiendo.</summary>
    private async Task CargarFiltrosAsync()
    {
        var granjaPrevia = GranjaSeleccionada?.Id;
        _cargandoFiltros = true;
        try
        {
            var granjas = await _catalogo.GetGranjasAsync(soloActivas: true);
            Granjas.Clear();
            Granjas.Add(new OpcionFiltro(null, "Todas las Granjas"));
            foreach (var granja in granjas)
            {
                Granjas.Add(new OpcionFiltro(granja.Id, granja.Nombre));
            }

            GranjaSeleccionada = Granjas.FirstOrDefault(g => g.Id == granjaPrevia) ?? Granjas[0];
            await CargarProgramasAsync(GranjaSeleccionada.Id);
        }
        finally
        {
            _cargandoFiltros = false;
        }

        AplicarFiltros();
    }

    /// <summary>Programas de la granja elegida (o todos). Conserva el programa actual si sigue disponible; si no, Ganadería.</summary>
    private async Task CargarProgramasAsync(int? granjaId)
    {
        var anterior = ProgramaSeleccionado?.Id;
        var programas = granjaId is { } id
            ? await _catalogo.GetProgramasDeGranjaAsync(id)
            : await _catalogo.GetProgramasAsync();

        Programas.Clear();
        foreach (var programa in programas)
        {
            Programas.Add(programa);
        }

        ProgramaSeleccionado = Programas.FirstOrDefault(p => p.Id == anterior)
            ?? Programas.FirstOrDefault(p => p.Codigo == NombresBase.ProgramaGanaderia)
            ?? Programas.FirstOrDefault();
    }

    private void AplicarFiltros()
    {
        _contexto.GranjaId = GranjaSeleccionada?.Id;
        _contexto.ProgramaId = ProgramaSeleccionado?.Id;
    }
}
