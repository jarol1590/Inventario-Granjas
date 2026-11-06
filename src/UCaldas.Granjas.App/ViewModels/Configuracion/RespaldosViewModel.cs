using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Respaldos;
using UCaldas.Granjas.Services.Seguridad;

namespace UCaldas.Granjas.App.ViewModels.Configuracion;

/// <summary>Pestaña "Respaldos / Backup": copias manuales y automáticas, restauración, mantenimiento y contraseña.</summary>
public sealed partial class RespaldosViewModel : ViewModelBase, ISeccion
{
    private readonly IBackupService _respaldos;
    private readonly ICatalogoService _catalogo;
    private readonly IAuthService _autenticacion;
    private readonly INavegador _navegador;
    private bool _cargando;

    public RespaldosViewModel(
        IBackupService respaldos,
        ICatalogoService catalogo,
        IAuthService autenticacion,
        INavegador navegador,
        IDialogService dialogos)
        : base(dialogos)
    {
        _respaldos = respaldos;
        _catalogo = catalogo;
        _autenticacion = autenticacion;
        _navegador = navegador;
    }

    [ObservableProperty]
    private string _ultimoRespaldoTexto = "Aún no hay respaldos";

    [ObservableProperty]
    private string _ubicacionTexto = string.Empty;

    [ObservableProperty]
    private bool _respaldoAutomatico;

    [ObservableProperty]
    private string _carpetaRespaldos = string.Empty;

    [ObservableProperty]
    private string _contrasenaActual = string.Empty;

    [ObservableProperty]
    private string _contrasenaNueva = string.Empty;

    [ObservableProperty]
    private string _contrasenaConfirmacion = string.Empty;

    public int LongitudMinimaContrasena => AuthService.LongitudMinima;

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            var sistema = await _catalogo.GetSistemaAsync();
            _cargando = true;
            try
            {
                RespaldoAutomatico = sistema.BackupAutomaticoSalir;
                CarpetaRespaldos = sistema.RutaBackupPredeterminada ?? string.Empty;
            }
            finally
            {
                _cargando = false;
            }

            await ActualizarUltimoRespaldoAsync();
        }, "cargar la configuración de respaldos");
    }

    partial void OnRespaldoAutomaticoChanged(bool value)
    {
        if (!_cargando)
        {
            _ = GuardarPreferenciasAsync();
        }
    }

    [RelayCommand]
    private async Task CrearRespaldoAsync()
    {
        ResultadoRespaldo? resultado = null;
        if (await EjecutarAsync(async () => resultado = await _respaldos.CrearRespaldoAsync(CarpetaOpcional()), "crear el respaldo"))
        {
            await ActualizarUltimoRespaldoAsync();
            Dialogos.Informar($"Respaldo creado correctamente ({Formato.Tamano(resultado!.Bytes)}):\n\n{resultado.Ruta}", "Respaldo");
        }
    }

    [RelayCommand]
    private async Task RestaurarAsync()
    {
        var ruta = Dialogos.AbrirArchivo("Seleccionar el respaldo que desea restaurar", "Respaldos del sistema (*.zip)|*.zip");
        if (ruta is null)
        {
            return;
        }

        if (!Dialogos.Confirmar(
                "Se reemplazará TODA la información actual (animales, movimientos, tarifas y configuración) por la del respaldo:\n\n" +
                $"{ruta}\n\nAntes de reemplazarla, el sistema guarda una copia de la base de datos actual. ¿Desea continuar?",
                "Restaurar base de datos", destructivo: true))
        {
            return;
        }

        if (await EjecutarAsync(() => _respaldos.RestaurarAsync(ruta), "restaurar el respaldo"))
        {
            Dialogos.Informar("La base de datos se restauró correctamente. Se volverá a cargar el sistema.", "Restauración completada");
            await _navegador.RecargarTodoAsync();
        }
    }

    [RelayCommand]
    private async Task CompactarAsync()
    {
        (long Antes, long Despues) tamanos = default;
        if (await EjecutarAsync(async () => tamanos = await _respaldos.CompactarAsync(), "compactar la base de datos"))
        {
            Dialogos.Informar(
                $"Base de datos compactada.\n\nAntes: {Formato.Tamano(tamanos.Antes)}\nDespués: {Formato.Tamano(tamanos.Despues)}",
                "Mantenimiento");
        }
    }

    [RelayCommand]
    private async Task CambiarCarpetaAsync()
    {
        var carpeta = Dialogos.ElegirCarpeta("Seleccione la carpeta de respaldos", CarpetaRespaldos);
        if (carpeta is null)
        {
            return;
        }

        CarpetaRespaldos = carpeta;
        await GuardarPreferenciasAsync();
        await EjecutarAsync(ActualizarUltimoRespaldoAsync, "buscar el último respaldo");
    }

    [RelayCommand]
    private async Task CambiarContrasenaAsync()
    {
        if (await EjecutarAsync(
                () => _autenticacion.CambiarContrasenaAsync(ContrasenaActual, ContrasenaNueva, ContrasenaConfirmacion),
                "cambiar la contraseña"))
        {
            ContrasenaActual = ContrasenaNueva = ContrasenaConfirmacion = string.Empty;
            Dialogos.Informar("La contraseña se cambió correctamente. Úsela la próxima vez que abra el sistema.", "Contraseña");
        }
    }

    private async Task GuardarPreferenciasAsync()
    {
        await EjecutarAsync(
            () => _catalogo.GuardarPreferenciasDeRespaldoAsync(RespaldoAutomatico, CarpetaRespaldos),
            "guardar las preferencias de respaldo");
    }

    private string? CarpetaOpcional() => string.IsNullOrWhiteSpace(CarpetaRespaldos) ? null : CarpetaRespaldos;

    private async Task ActualizarUltimoRespaldoAsync()
    {
        var ultimo = await _respaldos.UltimoRespaldoAsync(CarpetaOpcional());
        UltimoRespaldoTexto = ultimo is null ? "Aún no hay respaldos" : Formato.FechaHora(ultimo.Fecha);
        UbicacionTexto = ultimo is null ? "(No hay respaldos en la carpeta configurada)" : $"(Ubicación: {ultimo.Ruta})";
    }
}
