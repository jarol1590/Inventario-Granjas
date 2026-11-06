using System.ComponentModel;
using System.Windows.Media.Imaging;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;

namespace UCaldas.Granjas.App.ViewModels.Configuracion;

/// <summary>Pestaña "Firmas e Identidad": autoridad que firma los informes de Excel y su firma digitalizada.</summary>
public sealed partial class FirmasViewModel : ViewModelBase, ISeccion
{
    private const long TamanoMaximoImagen = 2 * 1024 * 1024;

    private readonly ICatalogoService _catalogo;
    private bool _cargando;
    private int _id;

    public FirmasViewModel(ICatalogoService catalogo, IDialogService dialogos)
        : base(dialogos)
    {
        _catalogo = catalogo;
    }

    [ObservableProperty]
    private string _nombreAutoridad = string.Empty;

    [ObservableProperty]
    private string _cargo = string.Empty;

    [ObservableProperty]
    private string _departamento = string.Empty;

    [ObservableProperty]
    private string _telefono = string.Empty;

    /// <summary>Imagen guardada en la base de datos como data URI (así viaja dentro de los respaldos).</summary>
    [ObservableProperty]
    private string? _imagenOrigen;

    [ObservableProperty]
    private BitmapImage? _imagenVista;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _hayCambios;

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            var firma = await _catalogo.GetFirmaAsync();
            _cargando = true;
            try
            {
                _id = firma.Id;
                NombreAutoridad = firma.NombreAutoridad;
                Cargo = firma.CargoAutoridad;
                Departamento = firma.EntidadFacultad;
                Telefono = firma.Telefono ?? string.Empty;
                ImagenOrigen = firma.RutaImagenFirma;
                ImagenVista = ImagenHelper.Cargar(firma.RutaImagenFirma);
            }
            finally
            {
                _cargando = false;
            }

            HayCambios = false;
        }, "cargar la configuración de firmas");
    }

    public bool PuedeSalir()
        => !HayCambios || Dialogos.Confirmar(
            "Hay cambios sin guardar en Firmas e Identidad.\n\n¿Desea descartarlos y salir?", "Cambios sin guardar", destructivo: true);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_cargando && e.PropertyName is nameof(NombreAutoridad) or nameof(Cargo) or nameof(Departamento) or nameof(Telefono) or nameof(ImagenOrigen))
        {
            HayCambios = true;
        }
    }

    [RelayCommand]
    private void CargarImagen()
    {
        var ruta = Dialogos.AbrirArchivo("Seleccionar la imagen de la firma", "Imágenes PNG o JPG (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
        if (ruta is null)
        {
            return;
        }

        try
        {
            if (new FileInfo(ruta).Length > TamanoMaximoImagen)
            {
                Dialogos.Advertir("La imagen pesa más de 2 MB. Use una firma de unos 300 x 100 píxeles (PNG con fondo transparente o JPG).");
                return;
            }

            var origen = ImagenHelper.ADataUri(ruta);
            var vista = ImagenHelper.Cargar(origen);
            if (vista is null)
            {
                Dialogos.Advertir("El archivo no es una imagen PNG o JPG válida.");
                return;
            }

            ImagenOrigen = origen;
            ImagenVista = vista;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Registro.Error(ex, $"No se pudo leer la imagen {ruta}.");
            Dialogos.Advertir($"No se pudo leer el archivo seleccionado.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void QuitarFirma()
    {
        ImagenOrigen = null;
        ImagenVista = null;
    }

    [RelayCommand(CanExecute = nameof(HayCambios))]
    private async Task GuardarAsync()
    {
        var guardado = await EjecutarAsync(async () =>
        {
            await _catalogo.GuardarFirmaAsync(new ConfiguracionFirma
            {
                Id = _id,
                NombreAutoridad = NombreAutoridad,
                CargoAutoridad = Cargo,
                EntidadFacultad = Departamento,
                Telefono = string.IsNullOrWhiteSpace(Telefono) ? null : Telefono,
                RutaImagenFirma = ImagenOrigen,
            });
        }, "guardar la configuración de firmas");

        if (guardado)
        {
            HayCambios = false;
            Dialogos.Informar("La configuración de la firma se guardó. Los próximos informes saldrán con estos datos.");
        }
    }
}
