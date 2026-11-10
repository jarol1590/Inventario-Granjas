using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Seguridad;

namespace UCaldas.Granjas.App.ViewModels.Configuracion;

/// <summary>
/// Configura (o cambia) la recuperación de contraseña de una instalación que ya tiene acceso: pregunta secreta, su
/// respuesta y una clave de recuperación nueva. Pide la contraseña actual para poder hacerlo.
/// </summary>
public sealed partial class ConfigurarRecuperacionViewModel : DialogViewModelBase
{
    private readonly IAuthService _autenticacion;
    private readonly IReloj _reloj;

    public ConfigurarRecuperacionViewModel(IAuthService autenticacion, IReloj reloj, IDialogService dialogos)
        : base(dialogos)
    {
        _autenticacion = autenticacion;
        _reloj = reloj;
        _preguntaSecreta = Preguntas[0];
    }

    public override string Titulo => "Recuperación de contraseña";

    public override double Ancho => 560;

    public IReadOnlyList<string> Preguntas => AuthService.PreguntasSecretas;

    [ObservableProperty]
    private string _contrasenaActual = string.Empty;

    [ObservableProperty]
    private string _preguntaSecreta;

    [ObservableProperty]
    private string _respuesta = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuestraFormulario), nameof(MuestraClave))]
    [NotifyCanExecuteChangedFor(nameof(GenerarCommand), nameof(ListoCommand))]
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
            ListoCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(MuestraFormulario))]
    private async Task GenerarAsync()
    {
        MensajeError = null;
        EstaOcupado = true;
        try
        {
            var clave = await _autenticacion.ConfigurarRecuperacionAsync(ContrasenaActual, PreguntaSecreta, Respuesta);
            ContrasenaActual = Respuesta = string.Empty;
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

    /// <summary>Cierra la ventana; solo después de confirmar que la clave se guardó (ya no se podrá consultar).</summary>
    [RelayCommand(CanExecute = nameof(PuedeTerminar))]
    private void Listo() => Cerrar(true);

    private bool PuedeTerminar() => Clave?.Confirmada == true;
}
