using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;

namespace UCaldas.Granjas.App.ViewModels.Configuracion;

public sealed record GranjaFila(Granja Granja)
{
    public string Codigo => Granja.Codigo;

    public string Nombre => Granja.Nombre;

    public string Estado => Granja.Activa ? "Activa" : "Inactiva";

    public string Observaciones => Granja.Observaciones ?? string.Empty;
}

public sealed record ProgramaFila(Programa Programa)
{
    public string Codigo => Programa.Codigo;

    public string Nombre => Programa.Nombre;

    public string Tipo => Programa.TipoInventario == TipoInventario.Lote ? "Lote (Población)" : "Individual (Chapeta)";

    public string Observaciones => Programa.Observaciones ?? string.Empty;
}

/// <summary>Pestaña "Granjas y Programas": sedes y programas productivos con su asociación.</summary>
public sealed partial class GranjasProgramasViewModel : ViewModelBase, ISeccion
{
    private readonly ICatalogoService _catalogo;
    private readonly INavegador _navegador;

    public GranjasProgramasViewModel(ICatalogoService catalogo, INavegador navegador, IDialogService dialogos)
        : base(dialogos)
    {
        _catalogo = catalogo;
        _navegador = navegador;
    }

    public ObservableCollection<GranjaFila> Granjas { get; } = [];

    public ObservableCollection<ProgramaFila> Programas { get; } = [];

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            var granjas = await _catalogo.GetGranjasAsync();
            Granjas.Clear();
            foreach (var g in granjas)
            {
                Granjas.Add(new GranjaFila(g));
            }

            var programas = await _catalogo.GetProgramasAsync();
            Programas.Clear();
            foreach (var p in programas)
            {
                Programas.Add(new ProgramaFila(p));
            }
        }, "cargar granjas y programas");
    }

    [RelayCommand]
    private async Task AgregarGranjaAsync() => await AbrirEditorDeGranjaAsync(null);

    [RelayCommand]
    private async Task EditarGranjaAsync(GranjaFila? fila)
    {
        if (fila is not null)
        {
            await AbrirEditorDeGranjaAsync(fila.Granja);
        }
    }

    [RelayCommand]
    private async Task QuitarGranjaAsync(GranjaFila? fila)
    {
        if (fila is null || !Dialogos.Confirmar(
                $"¿Quitar la granja «{fila.Nombre}»?\n\nSolo se puede quitar una granja sin animales, documentos ni movimientos; " +
                "si ya tiene historia, el sistema le pedirá inactivarla.",
                "Quitar granja", destructivo: true))
        {
            return;
        }

        if (await EjecutarAsync(() => _catalogo.EliminarGranjaAsync(fila.Granja.Id), "quitar la granja"))
        {
            await CargarAsync();
            await _navegador.RecargarFiltrosAsync();
        }
    }

    [RelayCommand]
    private async Task AgregarProgramaAsync() => await AbrirEditorDeProgramaAsync(null);

    [RelayCommand]
    private async Task EditarProgramaAsync(ProgramaFila? fila)
    {
        if (fila is not null)
        {
            await AbrirEditorDeProgramaAsync(fila.Programa);
        }
    }

    [RelayCommand]
    private async Task QuitarProgramaAsync(ProgramaFila? fila)
    {
        if (fila is null || !Dialogos.Confirmar(
                $"¿Quitar el programa «{fila.Nombre}»?\n\nSolo se puede quitar un programa sin categorías, oficios ni lotes.",
                "Quitar programa", destructivo: true))
        {
            return;
        }

        if (await EjecutarAsync(() => _catalogo.EliminarProgramaAsync(fila.Programa.Id), "quitar el programa"))
        {
            await CargarAsync();
            await _navegador.RecargarFiltrosAsync();
        }
    }

    private async Task AbrirEditorDeGranjaAsync(Granja? existente)
    {
        IReadOnlyList<OrientacionProductiva> orientaciones = [];
        if (!await EjecutarAsync(async () => orientaciones = await _catalogo.GetOrientacionesAsync(), "preparar el formulario"))
        {
            return;
        }

        var editor = new GranjaEditorViewModel(_catalogo, Dialogos, orientaciones, existente);
        if (Dialogos.Mostrar(editor))
        {
            await CargarAsync();
            await _navegador.RecargarFiltrosAsync();
        }
    }

    private async Task AbrirEditorDeProgramaAsync(Programa? existente)
    {
        IReadOnlyList<Granja> granjas = [];
        IReadOnlyList<int> asociadas = [];
        if (!await EjecutarAsync(async () =>
            {
                granjas = await _catalogo.GetGranjasAsync();
                asociadas = existente is null ? [] : await _catalogo.GetGranjaIdsDeProgramaAsync(existente.Id);
            }, "preparar el formulario"))
        {
            return;
        }

        var editor = new ProgramaEditorViewModel(_catalogo, Dialogos, granjas, asociadas, existente);
        if (Dialogos.Mostrar(editor))
        {
            await CargarAsync();
            await _navegador.RecargarFiltrosAsync();
        }
    }
}
