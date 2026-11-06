using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Un animal de un traslado, con la orientación que tendrá en la sede destino.</summary>
public sealed partial class TrasladoFila(AnimalSeleccionable animal, int orientacionDestinoId) : ObservableObject
{
    public AnimalSeleccionable Animal { get; } = animal;

    public int AnimalId => Animal.Id;

    [ObservableProperty]
    private int _orientacionDestinoId = orientacionDestinoId;

    [ObservableProperty]
    private string _observaciones = string.Empty;

    public TrasladoAnimalDto ToDto() => new()
    {
        AnimalId = Animal.Id,
        OrientacionDestinoId = OrientacionDestinoId,
        ObservacionesEspecificas = string.IsNullOrWhiteSpace(Observaciones) ? null : Observaciones.Trim(),
    };
}
