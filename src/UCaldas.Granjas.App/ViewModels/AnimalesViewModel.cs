using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Animales (Control Individual): módulo genérico con una pestaña por cada programa de inventario individual
/// (<c>PROGRAMA.tipo_inventario = INDIVIDUAL</c>: bovinos, equinos, caprinos...). Al elegir una pestaña se muestran la
/// grilla de animales, las categorías biológicas y los totales de esa especie; todas usan la misma vista.
/// </summary>
public sealed partial class AnimalesViewModel : ViewModelBase, IConAccionesRapidas
{
    private readonly IConsultasService _consultas;
    private readonly ICatalogoService _catalogo;
    private readonly IFormularios _formularios;
    private readonly ContextoGlobal _contexto;
    private bool _pestanasCargadas;
    private bool _cargandoPestanas;

    public AnimalesViewModel(
        IConsultasService consultas,
        ICatalogoService catalogo,
        IFormularios formularios,
        ContextoGlobal contexto,
        IDialogService dialogos)
        : base(dialogos)
    {
        _consultas = consultas;
        _catalogo = catalogo;
        _formularios = formularios;
        _contexto = contexto;
    }

    /// <summary>Las pestañas eligen su propio programa: el combo global de Programa no aplica en esta sección.</summary>
    public bool MostrarFiltroDePrograma => false;

    /// <summary>Una pestaña por programa de inventario individual.</summary>
    public ObservableCollection<ProgramaTabViewModel> Programas { get; } = [];

    [ObservableProperty]
    private ProgramaTabViewModel? _programaActivo;

    public bool HayProgramas => Programas.Count > 0;

    /// <summary>Arma las pestañas la primera vez y consulta la pestaña activa (con el filtro global de granja).</summary>
    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            if (!_pestanasCargadas)
            {
                await CargarPestanasAsync();
            }

            if (ProgramaActivo is { } activa)
            {
                await activa.CargarAsync();
            }
        }, "consultar los animales");
    }

    // Elegir otra pestaña consulta su programa; ella misma avisa de sus errores.
    partial void OnProgramaActivoChanged(ProgramaTabViewModel? value)
    {
        if (!_cargandoPestanas && value is not null)
        {
            _ = value.CargarAsync();
        }
    }

    private async Task CargarPestanasAsync()
    {
        var programas = await _catalogo.GetProgramasPorTipoAsync(TipoInventario.Individual);

        _cargandoPestanas = true;
        try
        {
            Programas.Clear();
            foreach (var programa in programas)
            {
                Programas.Add(new ProgramaTabViewModel(programa, _consultas, _catalogo, _formularios, _contexto, Dialogos));
            }

            // Ganadería es la pestaña de siempre; si no existe se abre la primera.
            ProgramaActivo = Programas.FirstOrDefault(p => p.Programa.Codigo == NombresBase.ProgramaGanaderia) ?? Programas.FirstOrDefault();
        }
        finally
        {
            _cargandoPestanas = false;
        }

        OnPropertyChanged(nameof(HayProgramas));
        _pestanasCargadas = true;
    }
}
