using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;

namespace UCaldas.Granjas.App.ViewModels.Configuracion;

/// <summary>Ventana para agregar o editar una granja (sede).</summary>
public sealed partial class GranjaEditorViewModel : DialogViewModelBase
{
    private readonly ICatalogoService _catalogo;
    private readonly Granja _original;

    public GranjaEditorViewModel(
        ICatalogoService catalogo,
        IDialogService dialogos,
        IReadOnlyList<OrientacionProductiva> orientaciones,
        Granja? existente)
        : base(dialogos)
    {
        _catalogo = catalogo;
        _original = existente ?? new Granja { Activa = true };
        EsNueva = existente is null;

        Orientaciones = [new OpcionFiltro(null, "(Sin orientación sugerida)"), .. orientaciones.Select(o => new OpcionFiltro(o.Id, o.Nombre))];
        _orientacionSeleccionada = Orientaciones.FirstOrDefault(o => o.Id == _original.OrientacionId) ?? Orientaciones[0];
        _codigo = _original.Codigo;
        _nombre = _original.Nombre;
        _activa = _original.Activa;
        _observaciones = _original.Observaciones ?? string.Empty;
    }

    public bool EsNueva { get; }

    public override string Titulo => EsNueva ? "Agregar nueva granja" : "Editar granja";

    public override double Ancho => 520;

    public IReadOnlyList<OpcionFiltro> Orientaciones { get; }

    [ObservableProperty]
    private string _codigo;

    [ObservableProperty]
    private string _nombre;

    [ObservableProperty]
    private bool _activa;

    [ObservableProperty]
    private string _observaciones;

    [ObservableProperty]
    private OpcionFiltro _orientacionSeleccionada;

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var guardado = await EjecutarAsync(async () =>
        {
            await _catalogo.GuardarGranjaAsync(new Granja
            {
                Id = _original.Id,
                Codigo = Codigo,
                Nombre = Nombre,
                Activa = Activa,
                Observaciones = Observaciones,
                OrientacionId = OrientacionSeleccionada.Id,
            });
        }, "guardar la granja");

        if (guardado)
        {
            Cerrar(true);
        }
    }
}
