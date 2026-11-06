using System.Collections.ObjectModel;
using System.ComponentModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Reportes;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Ficha técnica de un bovino con sus dos precios: el valor de compra (inmutable, con candado) y el valor contable
/// (lo asigna el cierre de valorización). Permite corregir los datos básicos y generar la hoja de vida en Excel.
/// </summary>
public sealed partial class FichaViewModel : DialogViewModelBase
{
    private readonly IFichaService _fichas;
    private readonly IAnimalService _animales;
    private readonly IExcelReportingEngine _informes;
    private readonly ICatalogoService _catalogo;
    private bool _cargando;
    private int _animalId;

    public FichaViewModel(
        IFichaService fichas,
        IAnimalService animales,
        IExcelReportingEngine informes,
        ICatalogoService catalogo,
        IDialogService dialogos)
        : base(dialogos)
    {
        _fichas = fichas;
        _animales = animales;
        _informes = informes;
        _catalogo = catalogo;
        CerrarCommand = new RelayCommand(() => Cerrar(HuboCambios));
    }

    public override string Titulo => "Ficha Técnica del Bovino";

    public override double Ancho => 880;

    /// <summary>true si se guardaron correcciones: la pantalla que abrió la ficha debe refrescarse.</summary>
    public bool HuboCambios { get; private set; }

    public IRelayCommand CerrarCommand { get; }

    public ObservableCollection<OpcionFiltro> Orientaciones { get; } = [];

    public ObservableCollection<string> Razas { get; } = [];

    public ObservableCollection<EventoFicha> Historial { get; } = [];

    // ---- Datos editables --------------------------------------------------------------------------------------------

    [ObservableProperty]
    private string _chapetaAmarilla = string.Empty;

    [ObservableProperty]
    private string _chapetaRoja = string.Empty;

    [ObservableProperty]
    private string _nombre = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsMacho))]
    private bool _esHembra = true;

    [ObservableProperty]
    private DateTime? _fechaNacimiento;

    [ObservableProperty]
    private string _raza = string.Empty;

    [ObservableProperty]
    private string _madreIdentificacion = string.Empty;

    [ObservableProperty]
    private decimal _pesoActualKg;

    [ObservableProperty]
    private OpcionFiltro? _orientacionSeleccionada;

    [ObservableProperty]
    private int _numeroPartos;

    [ObservableProperty]
    private bool _exentoValorizacion;

    // ---- Datos de solo lectura --------------------------------------------------------------------------------------

    [ObservableProperty]
    private string _fechaCompraTexto = "—";

    [ObservableProperty]
    private string _ubicacionActual = string.Empty;

    [ObservableProperty]
    private string _estadoTexto = string.Empty;

    [ObservableProperty]
    private string _origenTexto = string.Empty;

    [ObservableProperty]
    private string _genealogiaTexto = string.Empty;

    [ObservableProperty]
    private string _edadTexto = string.Empty;

    [ObservableProperty]
    private string _valorCompraTexto = Formato.Dinero(0);

    [ObservableProperty]
    private string _valorContableTexto = Formato.Dinero(0);

    [ObservableProperty]
    private string _categoriaTexto = string.Empty;

    [ObservableProperty]
    private bool _sinTarifa;

    [ObservableProperty]
    private string _variacionTexto = string.Empty;

    [ObservableProperty]
    private decimal _variacionAcumulada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCambiosCommand))]
    private bool _hayCambios;

    public bool EsMacho
    {
        get => !EsHembra;
        set => EsHembra = !value;
    }

    public async Task<bool> InicializarAsync(int animalId)
    {
        _animalId = animalId;
        return await EjecutarAsync(async () =>
        {
            Orientaciones.Clear();
            foreach (var o in await _catalogo.GetOrientacionesAsync())
            {
                Orientaciones.Add(new OpcionFiltro(o.Id, o.Nombre));
            }

            Razas.Clear();
            foreach (var r in await _catalogo.GetRazasAsync())
            {
                Razas.Add(r);
            }

            await RecargarFichaAsync();
        }, "abrir la ficha del bovino");
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_cargando && e.PropertyName is nameof(ChapetaAmarilla) or nameof(ChapetaRoja) or nameof(Nombre) or nameof(EsHembra)
            or nameof(FechaNacimiento) or nameof(Raza) or nameof(MadreIdentificacion) or nameof(PesoActualKg)
            or nameof(OrientacionSeleccionada) or nameof(NumeroPartos) or nameof(ExentoValorizacion))
        {
            HayCambios = true;
        }
    }

    [RelayCommand(CanExecute = nameof(HayCambios))]
    private async Task GuardarCambiosAsync()
    {
        if (OrientacionSeleccionada?.Id is not { } orientacionId)
        {
            Dialogos.Advertir("Seleccione la orientación productiva del animal.");
            return;
        }

        var guardado = await EjecutarAsync(async () =>
        {
            await _animales.ActualizarAsync(new AnimalEdicionDto
            {
                AnimalId = _animalId,
                ChapetaAmarilla = ChapetaAmarilla,
                ChapetaRoja = Vacio(ChapetaRoja),
                Nombre = Vacio(Nombre),
                Sexo = EsHembra ? Domain.Constants.Sexo.Hembra : Domain.Constants.Sexo.Macho,
                FechaNacimiento = FechaNacimiento ?? default,
                Raza = Raza,
                PesoActualKg = PesoActualKg,
                OrientacionId = orientacionId,
                NumeroPartos = EsHembra ? NumeroPartos : 0,
                ExentoValorizacion = ExentoValorizacion,
                MadreIdentificacion = Vacio(MadreIdentificacion),
            });
            HuboCambios = true;
            await RecargarFichaAsync();
        }, "guardar los cambios de la ficha");

        if (guardado)
        {
            Dialogos.Informar(
                "Los datos de la ficha se guardaron.\n\nSi cambió la orientación o la fecha de nacimiento, el valor contable se recalcula " +
                "en el próximo cierre de valorización mensual.",
                "Ficha actualizada");
        }
    }

    [RelayCommand]
    private async Task GenerarInformeAsync()
    {
        var ruta = Dialogos.GuardarArchivo(
            "Guardar la hoja de vida", "Libro de Excel (*.xlsx)|*.xlsx", $"HojaDeVida_{ChapetaAmarilla.Trim()}.xlsx");
        if (ruta is null)
        {
            return;
        }

        if (await EjecutarAsync(async () => await _informes.GenerarHojaDeVidaAsync(_animalId, ruta), "generar la hoja de vida")
            && Dialogos.Confirmar($"La hoja de vida se guardó en:\n\n{ruta}\n\n¿Desea abrirla ahora?", "Informe generado"))
        {
            Dialogos.Abrir(ruta);
        }
    }

    private async Task RecargarFichaAsync()
    {
        var ficha = await _fichas.ObtenerAsync(_animalId);
        var animal = ficha.Animal;

        _cargando = true;
        try
        {
            ChapetaAmarilla = animal.ChapetaAmarilla;
            ChapetaRoja = animal.ChapetaRoja ?? string.Empty;
            Nombre = animal.Nombre ?? string.Empty;
            EsHembra = animal.Sexo == Domain.Constants.Sexo.Hembra;
            FechaNacimiento = animal.FechaNacimiento;
            Raza = animal.Raza;
            MadreIdentificacion = animal.MadreIdentificacion ?? string.Empty;
            PesoActualKg = animal.PesoActualKg;
            OrientacionSeleccionada = Orientaciones.FirstOrDefault(o => o.Id == animal.OrientacionId);
            NumeroPartos = animal.NumeroPartos;
            ExentoValorizacion = animal.ExentoValorizacion;

            FechaCompraTexto = animal.FechaCompra is { } compra ? Formato.Fecha(compra) : "— (nació en la granja)";
            UbicacionActual = ficha.GranjaNombre;
            EstadoTexto = animal.Estado;
            OrigenTexto = animal.ClaseEntrada == ClaseEntrada.Compra ? "Compra" : animal.ClaseEntrada == ClaseEntrada.Traslado ? "Traslado" : "Nacimiento";
            EdadTexto = $"{ficha.EdadMeses} meses";
            GenealogiaTexto = DescribirGenealogia(ficha);
            ValorCompraTexto = Formato.Dinero(animal.ValorCompraOrigen);
            ValorContableTexto = Formato.Dinero(animal.ValorContableActual);
            CategoriaTexto = ficha.CategoriaNombre;
            SinTarifa = ficha.CategoriaNombre == NombresBase.CategoriaSinClasificar;
            VariacionAcumulada = ficha.VariacionAcumulada;
            VariacionTexto = Formato.DineroConSigno(ficha.VariacionAcumulada);

            Historial.Clear();
            foreach (var evento in ficha.Historial)
            {
                Historial.Add(evento);
            }
        }
        finally
        {
            _cargando = false;
        }

        HayCambios = false;
    }

    private static string DescribirGenealogia(FichaAnimal ficha)
    {
        var madre = string.IsNullOrWhiteSpace(ficha.Animal.MadreIdentificacion) ? "sin dato" : ficha.Animal.MadreIdentificacion;
        var genealogia = ficha.Genealogia;
        if (genealogia is null)
        {
            return $"Madre: {madre}";
        }

        var padre = genealogia.EsInseminacion
            ? $"Inseminación ({genealogia.NombrePadreInseminacion ?? "sin nombre"}, raza {genealogia.RazaPadreInseminacion ?? "sin dato"})"
            : $"Monta directa ({ficha.PadreChapeta ?? "padre no registrado"})";
        return $"Madre: {madre} · {padre}";
    }

    private static string? Vacio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
