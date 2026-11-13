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

/// <summary>Una raza o línea del programa elegido en la sección de razas.</summary>
public sealed record RazaFila(RazaLineaPrograma Raza)
{
    public string Nombre => Raza.EsDefault ? $"{Raza.Nombre} (por defecto)" : Raza.Nombre;

    public string Estado => Raza.Activo ? "Activa" : "Inactiva";

    /// <summary>La opción por defecto no se puede inactivar.</summary>
    public bool PuedeCambiarEstado => !Raza.EsDefault;

    public string TextoAccion => Raza.Activo ? "Inactivar" : "Activar";
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

    /// <summary>Programas entre los que se elige el de la sección de razas / líneas.</summary>
    public ObservableCollection<Programa> ProgramasDeRazas { get; } = [];

    public ObservableCollection<RazaFila> Razas { get; } = [];

    [ObservableProperty]
    private Programa? _programaDeRazas;

    [ObservableProperty]
    private string _nuevaRaza = string.Empty;

    partial void OnProgramaDeRazasChanged(Programa? value) => _ = CargarRazasAsync();

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

            var elegido = ProgramaDeRazas?.Id;
            ProgramasDeRazas.Clear();
            foreach (var p in programas)
            {
                ProgramasDeRazas.Add(p);
            }

            // Asignar dispara la carga de las razas; si el programa elegido sigue siendo el mismo se recargan a mano.
            var nuevo = ProgramasDeRazas.FirstOrDefault(p => p.Id == elegido) ?? ProgramasDeRazas.FirstOrDefault();
            if (ReferenceEquals(nuevo, ProgramaDeRazas))
            {
                await CargarRazasAsync();
            }
            else
            {
                ProgramaDeRazas = nuevo;
            }
        }, "cargar granjas y programas");
    }

    private async Task CargarRazasAsync()
    {
        await EjecutarAsync(async () =>
        {
            Razas.Clear();
            if (ProgramaDeRazas is not { } programa)
            {
                return;
            }

            foreach (var r in await _catalogo.GetRazasDeProgramaAsync(programa.Id, soloActivas: false))
            {
                Razas.Add(new RazaFila(r));
            }
        }, "cargar las razas y líneas");
    }

    [RelayCommand]
    private async Task AgregarRazaAsync()
    {
        if (ProgramaDeRazas is not { } programa)
        {
            Dialogos.Advertir("Seleccione el programa al que se agregará la raza o línea.");
            return;
        }

        if (await EjecutarAsync(() => _catalogo.AgregarRazaAsync(programa.Id, NuevaRaza), "agregar la raza o línea"))
        {
            NuevaRaza = string.Empty;
            await CargarRazasAsync();
        }
    }

    [RelayCommand]
    private async Task CambiarEstadoRazaAsync(RazaFila? fila)
    {
        if (fila is not null && await EjecutarAsync(() => _catalogo.CambiarEstadoRazaAsync(fila.Raza.Id, !fila.Raza.Activo), "cambiar el estado de la raza o línea"))
        {
            await CargarRazasAsync();
        }
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
