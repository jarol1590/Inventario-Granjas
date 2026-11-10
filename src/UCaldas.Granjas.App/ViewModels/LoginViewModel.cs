using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Seguridad;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>En qué parte del acceso está el usuario.</summary>
public enum PasoDeAcceso
{
    /// <summary>Escribe su contraseña.</summary>
    Ingreso,

    /// <summary>Olvidó la contraseña: se identifica con su pregunta secreta o con la clave de recuperación.</summary>
    Recuperacion,

    /// <summary>La contraseña ya se restableció: se le entrega la clave de recuperación nueva.</summary>
    ClaveNueva,
}

public enum MetodoDeRecuperacion
{
    PreguntaSecreta,
    ClaveDeRecuperacion,
}

/// <summary>
/// Acceso al sistema con contraseña. Si el usuario la olvida puede restablecerla (sin borrar la base de datos) respondiendo
/// su pregunta secreta o usando la clave de recuperación. La creación de la contraseña en el primer uso vive en
/// <see cref="InicializacionPasswordViewModel"/>.
/// </summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _autenticacion;
    private readonly IReloj _reloj;

    public LoginViewModel(IAuthService autenticacion, IReloj reloj, IDialogService dialogos)
        : base(dialogos)
    {
        _autenticacion = autenticacion;
        _reloj = reloj;
    }

    /// <summary>Se dispara cuando la contraseña es correcta.</summary>
    public event Action? IngresoExitoso;

    public int LongitudMinima => AuthService.LongitudMinima;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsIngreso), nameof(EsRecuperacion), nameof(EsClaveNueva))]
    private PasoDeAcceso _paso = PasoDeAcceso.Ingreso;

    public bool EsIngreso => Paso == PasoDeAcceso.Ingreso;

    public bool EsRecuperacion => Paso == PasoDeAcceso.Recuperacion;

    public bool EsClaveNueva => Paso == PasoDeAcceso.ClaveNueva;

    [ObservableProperty]
    private string _contrasena = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    private string? _mensajeInformativo;

    // ---- Recuperación -------------------------------------------------------------------------------------------------

    /// <summary>false si el acceso se creó antes de que existiera la recuperación (no hay pregunta secreta ni clave).</summary>
    [ObservableProperty]
    private bool _recuperacionDisponible;

    [ObservableProperty]
    private string? _preguntaSecreta;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsaPregunta), nameof(UsaClave))]
    private MetodoDeRecuperacion _metodo = MetodoDeRecuperacion.PreguntaSecreta;

    /// <summary>Opción "Responder mi pregunta secreta" (se enlaza a un botón de opción).</summary>
    public bool UsaPregunta
    {
        get => Metodo == MetodoDeRecuperacion.PreguntaSecreta;
        set
        {
            if (value)
            {
                Metodo = MetodoDeRecuperacion.PreguntaSecreta;
            }
        }
    }

    /// <summary>Opción "Usar mi clave de recuperación" (se enlaza a un botón de opción).</summary>
    public bool UsaClave
    {
        get => Metodo == MetodoDeRecuperacion.ClaveDeRecuperacion;
        set
        {
            if (value)
            {
                Metodo = MetodoDeRecuperacion.ClaveDeRecuperacion;
            }
        }
    }

    [ObservableProperty]
    private string _respuesta = string.Empty;

    [ObservableProperty]
    private string _claveIngresada = string.Empty;

    [ObservableProperty]
    private string _nuevaContrasena = string.Empty;

    [ObservableProperty]
    private string _confirmacionNueva = string.Empty;

    /// <summary>La clave de recuperación nueva, mostrada tras restablecer la contraseña.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinuarTrasRestablecerCommand))]
    private ClaveDeRecuperacionViewModel? _claveNueva;

    partial void OnClaveNuevaChanged(ClaveDeRecuperacionViewModel? oldValue, ClaveDeRecuperacionViewModel? newValue)
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
            ContinuarTrasRestablecerCommand.NotifyCanExecuteChanged();
        }
    }

    // ---- Ingreso ------------------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task AceptarAsync()
    {
        MensajeError = null;
        MensajeInformativo = null;
        EstaOcupado = true;
        try
        {
            if (await _autenticacion.VerificarAsync(Contrasena))
            {
                IngresoExitoso?.Invoke();
                return;
            }

            MensajeError = "La contraseña no es correcta.";
            Contrasena = string.Empty;
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    // ---- Restablecimiento ---------------------------------------------------------------------------------------------

    /// <summary>"¿Olvidó su contraseña?": abre el restablecimiento con lo que esta instalación tenga configurado.</summary>
    [RelayCommand]
    private async Task OlvideLaContrasenaAsync()
    {
        MensajeError = null;
        MensajeInformativo = null;
        EstaOcupado = true;
        try
        {
            var estado = await _autenticacion.ObtenerEstadoDeRecuperacionAsync();
            RecuperacionDisponible = estado.Configurada;
            PreguntaSecreta = estado.PreguntaSecreta;
            Metodo = MetodoDeRecuperacion.PreguntaSecreta;
            LimpiarDatosDeRecuperacion();
            Contrasena = string.Empty;
            Paso = PasoDeAcceso.Recuperacion;
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    [RelayCommand]
    private void VolverAlIngreso()
    {
        MensajeError = null;
        LimpiarDatosDeRecuperacion();
        Paso = PasoDeAcceso.Ingreso;
    }

    /// <summary>
    /// Valida la respuesta secreta o la clave de recuperación y, si es correcta, redefine la contraseña. La base de datos
    /// no se toca: solo cambian la contraseña y la clave de recuperación (que se renueva).
    /// </summary>
    [RelayCommand]
    private async Task RestablecerAsync()
    {
        MensajeError = null;
        EstaOcupado = true;
        try
        {
            var clave = UsaPregunta
                ? await _autenticacion.RestablecerConRespuestaAsync(Respuesta, NuevaContrasena, ConfirmacionNueva)
                : await _autenticacion.RestablecerConClaveAsync(ClaveIngresada, NuevaContrasena, ConfirmacionNueva);

            LimpiarDatosDeRecuperacion();
            ClaveNueva = new ClaveDeRecuperacionViewModel(Dialogos, _reloj, clave);
            Paso = PasoDeAcceso.ClaveNueva;
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

    /// <summary>Vuelve al ingreso con la contraseña ya cambiada. Se habilita cuando el usuario confirmó que guardó la clave nueva.</summary>
    [RelayCommand(CanExecute = nameof(PuedeContinuarTrasRestablecer))]
    private void ContinuarTrasRestablecer()
    {
        ClaveNueva = null;
        Contrasena = string.Empty;
        MensajeError = null;
        MensajeInformativo = "Contraseña restablecida. Ingrese con la nueva contraseña.";
        Paso = PasoDeAcceso.Ingreso;
    }

    private bool PuedeContinuarTrasRestablecer() => ClaveNueva?.Confirmada == true;

    private void LimpiarDatosDeRecuperacion()
    {
        Respuesta = ClaveIngresada = NuevaContrasena = ConfirmacionNueva = string.Empty;
    }
}
