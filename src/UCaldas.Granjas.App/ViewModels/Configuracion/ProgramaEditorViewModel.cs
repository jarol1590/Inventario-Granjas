using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;

namespace UCaldas.Granjas.App.ViewModels.Configuracion;

/// <summary>Una granja con su casilla "el programa se ejecuta aquí".</summary>
public sealed partial class GranjaSeleccionable(Granja granja, bool seleccionada) : ObservableObject
{
    public Granja Granja { get; } = granja;

    [ObservableProperty]
    private bool _seleccionada = seleccionada;
}

/// <summary>Ventana para agregar o editar un programa productivo y las granjas donde se ejecuta.</summary>
public sealed partial class ProgramaEditorViewModel : DialogViewModelBase
{
    private readonly ICatalogoService _catalogo;
    private readonly Programa _original;

    public ProgramaEditorViewModel(
        ICatalogoService catalogo,
        IDialogService dialogos,
        IReadOnlyList<Granja> granjas,
        IReadOnlyCollection<int> granjaIdsAsociadas,
        Programa? existente)
        : base(dialogos)
    {
        _catalogo = catalogo;
        _original = existente ?? new Programa { TipoInventario = TipoInventario.Individual };
        EsNuevo = existente is null;

        Tipos = [new(TipoInventario.Individual, "Individual (Chapeta)"), new(TipoInventario.Lote, "Lote (Población)")];
        _tipoSeleccionado = Tipos.First(t => t.Valor == _original.TipoInventario);
        _codigo = _original.Codigo;
        _nombre = _original.Nombre;
        _observaciones = _original.Observaciones ?? string.Empty;

        foreach (var granja in granjas)
        {
            Granjas.Add(new GranjaSeleccionable(granja, granjaIdsAsociadas.Contains(granja.Id)));
        }
    }

    public bool EsNuevo { get; }

    public override string Titulo => EsNuevo ? "Agregar nuevo programa" : "Editar programa";

    public override double Ancho => 540;

    public IReadOnlyList<OpcionTexto> Tipos { get; }

    public ObservableCollection<GranjaSeleccionable> Granjas { get; } = [];

    [ObservableProperty]
    private string _codigo;

    [ObservableProperty]
    private string _nombre;

    [ObservableProperty]
    private string _observaciones;

    [ObservableProperty]
    private OpcionTexto _tipoSeleccionado;

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var guardado = await EjecutarAsync(async () =>
        {
            var granjaIds = Granjas.Where(g => g.Seleccionada).Select(g => g.Granja.Id).ToList();
            await _catalogo.GuardarProgramaAsync(
                new Programa
                {
                    Id = _original.Id,
                    Codigo = Codigo,
                    Nombre = Nombre,
                    TipoInventario = TipoSeleccionado.Valor!,
                    Observaciones = Observaciones,
                },
                granjaIds);
        }, "guardar el programa");

        if (guardado)
        {
            Cerrar(true);
        }
    }
}
