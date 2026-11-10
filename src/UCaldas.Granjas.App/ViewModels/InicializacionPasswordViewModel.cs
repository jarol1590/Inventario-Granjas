using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Seguridad;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Primer inicio del sistema: crea la contraseña, la pregunta secreta con su respuesta y la clave de recuperación.
/// Todo queda en la base de datos local como hashes; no necesita internet ni correo.
/// </summary>
public sealed partial class InicializacionPasswordViewModel : ViewModelBase
{
    private readonly IAuthService _autenticacion;
    private readonly IReloj _reloj;

    public InicializacionPasswordViewModel(IAuthService autenticacion, IReloj reloj, IDialogService dialogos)
        : base(dialogos)
    {
        _autenticacion = autenticacion;
        _reloj = reloj;
        _preguntaSecreta = Preguntas[0];
    }

    /// <summary>Se dispara cuando el usuario terminó: ya tiene su acceso y confirmó que guardó la clave.</summary>
    public event Action? Terminado;

    public IReadOnlyList<string> Preguntas => AuthService.PreguntasSecretas;

    public int LongitudMinima => AuthService.LongitudMinima;

    [ObservableProperty]
    private string _contrasena = string.Empty;

    [ObservableProperty]
    private string _confirmacion = string.Empty;

    [ObservableProperty]
    private string _preguntaSecreta;

    [ObservableProperty]
    private string _respuesta = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    /// <summary>La clave generada; mientras sea nula se muestra el formulario y después, la clave con sus botones.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuestraFormulario), nameof(MuestraClave))]
    [NotifyCanExecuteChangedFor(nameof(CrearCommand), nameof(ContinuarCommand))]
    private ClaveDeRecuperacionViewModel? _clave;

    public bool MuestraFormulario => Clave is null;

    public bool MuestraClave => Clave is not null;

    partial void OnClaveChanged(ClaveDeRecuperacionViewModel? oldValue, ClaveDeRecuperacionViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= AlCambiarLaClave;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += AlCambiarLaClave;
        }
    }

    private void AlCambiarLaClave(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClaveDeRecuperacionViewModel.Confirmada))
        {
            ContinuarCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeCrear))]
    private async Task CrearAsync()
    {
        MensajeError = null;
        EstaOcupado = true;
        try
        {
            var clave = await _autenticacion.CrearAccesoAsync(Contrasena, Confirmacion, PreguntaSecreta, Respuesta);

            // Las credenciales ya están guardadas como hash: no se conservan en memoria más de lo necesario.
            Contrasena = Confirmacion = Respuesta = string.Empty;
            Clave = new ClaveDeRecuperacionViewModel(Dialogos, _reloj, clave);
        }
        catch (ValidationException ex)
        {
            MensajeError = ex.Message;
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    private bool PuedeCrear() => Clave is null;

    /// <summary>Cierra la ventana de inicio. Solo se habilita cuando el usuario marcó que ya guardó la clave.</summary>
    [RelayCommand(CanExecute = nameof(PuedeContinuar))]
    private void Continuar() => Terminado?.Invoke();

    private bool PuedeContinuar() => Clave?.Confirmada == true;
}
