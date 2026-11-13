using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Reportes;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Centro de informes: Kardex financiero (por granja y mes) e inventario físico por granja y categoría. Los informes
/// salen únicamente en Excel (.xlsx) con el bloque de firma configurado.
/// </summary>
public sealed partial class InformesViewModel : ViewModelBase, IUsaFiltroGlobal
{
    private const string FirmaDeEjemplo = "NOMBRE DE LA AUTORIDAD";

    private readonly IExcelReportingEngine _informes;
    private readonly ICatalogoService _catalogo;
    private readonly ContextoGlobal _contexto;
    private readonly IReloj _reloj;

    public InformesViewModel(
        IExcelReportingEngine informes,
        ICatalogoService catalogo,
        ContextoGlobal contexto,
        IReloj reloj,
        IDialogService dialogos)
        : base(dialogos)
    {
        _informes = informes;
        _catalogo = catalogo;
        _contexto = contexto;
        _reloj = reloj;

        var anterior = reloj.Hoy.AddMonths(-1);
        Anios = Enumerable.Range(reloj.Hoy.Year - 6, 8).ToList();
        _mesSeleccionado = FiltroMensual.Meses[anterior.Month - 1];
        _anio = anterior.Year;
        Estados = [new(EstadoAnimal.Disponible, "Disponibles"), new(null, "Todos los estados")];
        _estadoSeleccionado = Estados[0];
    }

    public IReadOnlyList<OpcionMes> Meses => FiltroMensual.Meses;

    public IReadOnlyList<int> Anios { get; }

    public IReadOnlyList<OpcionTexto> Estados { get; }

    public ObservableCollection<OpcionFiltro> Categorias { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsInventario))]
    private bool _esKardex = true;

    [ObservableProperty]
    private OpcionMes _mesSeleccionado;

    [ObservableProperty]
    private int _anio;

    [ObservableProperty]
    private OpcionFiltro? _categoriaSeleccionada;

    [ObservableProperty]
    private OpcionTexto _estadoSeleccionado;

    [ObservableProperty]
    private string _alcanceTexto = string.Empty;

    [ObservableProperty]
    private string? _advertenciaFirma;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirUltimoCommand), nameof(MostrarUltimoCommand))]
    private string? _ultimoArchivo;

    public bool EsInventario
    {
        get => !EsKardex;
        set => EsKardex = !value;
    }

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            var firma = await _catalogo.GetFirmaAsync();
            AdvertenciaFirma = firma.NombreAutoridad.Trim().Equals(FirmaDeEjemplo, StringComparison.OrdinalIgnoreCase)
                ? "La firma de los informes todavía tiene los datos de ejemplo. Configúrela en Configuración › Firmas e Identidad."
                : $"Firma de los informes: {firma.NombreAutoridad} — {firma.CargoAutoridad}.";

            if (Categorias.Count == 0)
            {
                Categorias.Add(new OpcionFiltro(null, "Todas las categorías"));
                // Las categorías del inventario por categoría son las de la ganadería (valorización biológica).
                var biologicos = (await _catalogo.GetProgramasAsync()).Where(p => p.UsaValorizacionBiologica).Select(p => p.Id).ToHashSet();
                foreach (var c in (await _catalogo.GetCategoriasAsync()).Where(c => biologicos.Contains(c.ProgramaId) && c.Nombre != NombresBase.CategoriaSinClasificar))
                {
                    Categorias.Add(new OpcionFiltro(c.Id, c.Nombre));
                }

                CategoriaSeleccionada = Categorias[0];
            }

            var granjas = await _catalogo.GetGranjasAsync();
            var granja = _contexto.GranjaId is { } id ? granjas.FirstOrDefault(g => g.Id == id)?.Nombre : null;
            AlcanceTexto = granja is null
                ? "Todas las granjas (una pestaña por granja y un consolidado general)"
                : $"Granja {granja}";
        }, "preparar los informes");
    }

    [RelayCommand]
    private async Task GenerarAsync()
    {
        var hoy = _reloj.Hoy;
        var sugerido = EsKardex
            ? $"Kardex_Financiero_{Anio}-{MesSeleccionado.Numero:00}.xlsx"
            : $"Inventario_Fisico_{hoy:yyyyMMdd}.xlsx";
        var ruta = Dialogos.GuardarArchivo("Guardar el informe", "Libro de Excel (*.xlsx)|*.xlsx", sugerido);
        if (ruta is null)
        {
            return;
        }

        string? generado = null;
        var terminado = await EjecutarAsync(async () =>
        {
            generado = EsKardex
                ? await _informes.GenerarKardexFinancieroAsync(new KardexReporteRequest(Anio, MesSeleccionado.Numero, _contexto.GranjaId, ruta))
                : await _informes.GenerarInventarioFisicoAsync(new InventarioFisicoRequest(
                    _contexto.GranjaId, _contexto.ProgramaId, CategoriaSeleccionada?.Id, EstadoSeleccionado.Valor, ruta));
        }, "generar el informe");

        if (!terminado || generado is null)
        {
            return;
        }

        UltimoArchivo = generado;
        if (Dialogos.Confirmar($"El informe se guardó en:\n\n{generado}\n\n¿Desea abrirlo ahora?", "Informe generado"))
        {
            Dialogos.Abrir(generado);
        }
    }

    [RelayCommand(CanExecute = nameof(HayUltimo))]
    private void AbrirUltimo()
    {
        if (UltimoArchivo is { } ruta)
        {
            Dialogos.Abrir(ruta);
        }
    }

    [RelayCommand(CanExecute = nameof(HayUltimo))]
    private void MostrarUltimo()
    {
        if (UltimoArchivo is { } ruta)
        {
            Dialogos.MostrarEnExplorador(ruta);
        }
    }

    private bool HayUltimo() => !string.IsNullOrEmpty(UltimoArchivo) && File.Exists(UltimoArchivo);
}
