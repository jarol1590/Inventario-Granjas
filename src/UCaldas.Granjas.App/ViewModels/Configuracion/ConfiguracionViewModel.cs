using UCaldas.Granjas.App.Infraestructura;

namespace UCaldas.Granjas.App.ViewModels.Configuracion;

/// <summary>
/// Sección Configuración. Sus cuatro pestañas ocupan el lugar de la barra de módulos de la ventana principal
/// (como en el mockup) y cada una se recarga al abrirse.
/// </summary>
public sealed partial class ConfiguracionViewModel : ViewModelBase, IConBarraSuperior
{
    private readonly TarifasViewModel _tarifas;
    private readonly FirmasViewModel _firmas;

    public ConfiguracionViewModel(
        TarifasViewModel tarifas,
        FirmasViewModel firmas,
        GranjasProgramasViewModel granjasProgramas,
        RespaldosViewModel respaldos,
        IDialogService dialogos)
        : base(dialogos)
    {
        _tarifas = tarifas;
        _firmas = firmas;

        var pestanas = new List<Pestana>
        {
            new("📋", "Tarifas Valorización", tarifas),
            new("✒", "Firmas e Identidad", firmas),
            new("🏛", "Granjas y Programas", granjasProgramas),
            new("💾", "Respaldos / Backup", respaldos),
        };

        BarraSuperior = new BarraDePestanas(pestanas, pestanas[0])
        {
            // Solo la pestaña visible puede tener cambios sin guardar.
            PuedeCambiar = _ => (PestanaActiva?.Contenido as ISeccion)?.PuedeSalir() ?? true,
        };
        BarraSuperior.CambioConfirmado += pestana => _ = AbrirPestanaAsync(pestana);
        _pestanaActiva = pestanas[0];
    }

    public BarraDePestanas BarraSuperior { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Contenido))]
    private Pestana? _pestanaActiva;

    /// <summary>ViewModel de la pestaña activa (la plantilla de datos elige la vista).</summary>
    public object? Contenido => PestanaActiva?.Contenido;

    public async Task CargarAsync()
    {
        var primera = BarraSuperior.Pestanas[0];
        BarraSuperior.SeleccionarSinNotificar(primera);
        PestanaActiva = primera;
        await EjecutarAsync(() => ((ISeccion)primera.Contenido!).CargarAsync(), "abrir la configuración");
    }

    public bool PuedeSalir() => _tarifas.PuedeSalir() && _firmas.PuedeSalir();

    private async Task AbrirPestanaAsync(Pestana pestana)
    {
        PestanaActiva = pestana;
        await EjecutarAsync(() => ((ISeccion)pestana.Contenido!).CargarAsync(), "abrir la pestaña");
    }
}
