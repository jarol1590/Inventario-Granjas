using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.App.ViewModels.Configuracion;

/// <summary>Una fila editable de la matriz de tarifas.</summary>
public sealed partial class TarifaFilaViewModel : ObservableObject
{
    /// <summary>Clave que representa "Todas" las orientaciones en el combo (el dominio usa nulo).</summary>
    public const int TodasLasOrientaciones = 0;

    public TarifaFilaViewModel(TablaValorizacionBase fila)
    {
        _categoriaId = fila.CategoriaId;
        _orientacionClave = fila.OrientacionId ?? TodasLasOrientaciones;
        _edadMinimaMeses = fila.EdadMinimaMeses;
        _edadMaximaMeses = fila.EdadMaximaMeses;
        _sexo = fila.Sexo;
        _requierePartos = fila.RequierePartos;
        _valorBase = fila.ValorEstandar;
    }

    [ObservableProperty]
    private int _categoriaId;

    [ObservableProperty]
    private int _orientacionClave;

    [ObservableProperty]
    private int _edadMinimaMeses;

    [ObservableProperty]
    private int _edadMaximaMeses;

    [ObservableProperty]
    private string _sexo;

    [ObservableProperty]
    private bool _requierePartos;

    [ObservableProperty]
    private decimal _valorBase;

    public TablaValorizacionBase ToEntidad(int vigenciaId) => new()
    {
        VigenciaId = vigenciaId,
        CategoriaId = CategoriaId,
        OrientacionId = OrientacionClave == TodasLasOrientaciones ? null : OrientacionClave,
        EdadMinimaMeses = EdadMinimaMeses,
        EdadMaximaMeses = EdadMaximaMeses,
        Sexo = Sexo,
        RequierePartos = RequierePartos,
        ValorEstandar = ValorBase,
    };
}
