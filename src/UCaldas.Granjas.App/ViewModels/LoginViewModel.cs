using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Services.Seguridad;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Acceso al sistema. Con la base recién creada (primer arranque) pide crear la contraseña; después solo la solicita.
/// </summary>
public sealed partial class LoginViewModel(IAuthService autenticacion, IDialogService dialogos) : ViewModelBase(dialogos)
{
    /// <summary>Se dispara cuando la contraseña es correcta (o se acaba de crear).</summary>
    public event Action? IngresoExitoso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Instruccion), nameof(TextoBoton))]
    private bool _esPrimerArranque;

    [ObservableProperty]
    private string _contrasena = string.Empty;

    [ObservableProperty]
    private string _confirmacion = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    public string Instruccion => EsPrimerArranque
        ? "Primer uso: cree la contraseña de acceso. Es única para este equipo; guárdela en un lugar seguro, porque no se puede recuperar."
        : "Ingrese su contraseña para continuar.";

    public string TextoBoton => EsPrimerArranque ? "CREAR CONTRASEÑA Y CONTINUAR" : "INGRESAR";

    public int LongitudMinima => AuthService.LongitudMinima;

    public async Task InicializarAsync() => EsPrimerArranque = !await autenticacion.EstaConfiguradoAsync();

    [RelayCommand]
    private async Task AceptarAsync()
    {
        MensajeError = null;
        EstaOcupado = true;
        try
        {
            if (EsPrimerArranque)
            {
                await autenticacion.CrearContrasenaAsync(Contrasena, Confirmacion);
                IngresoExitoso?.Invoke();
                return;
            }

            if (await autenticacion.VerificarAsync(Contrasena))
            {
                IngresoExitoso?.Invoke();
                return;
            }

            MensajeError = "La contraseña no es correcta.";
            Contrasena = string.Empty;
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
}
