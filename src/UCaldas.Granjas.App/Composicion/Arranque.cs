using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.DependencyInjection;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.App.ViewModels.Configuracion;
using UCaldas.Granjas.App.Vistas;
using UCaldas.Granjas.Data;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Seguridad;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.App.Composicion;

/// <summary>
/// Secuencia de arranque: base de datos → acceso (contraseña) → cierres de valorización pendientes → ventana principal.
/// Vive fuera del code-behind para que App.xaml.cs quede sin lógica.
/// </summary>
public static class Arranque
{
    /// <summary>Cultura es-CO para hilos y para los enlaces de WPF (miles con punto, fechas día/mes/año).</summary>
    public static void ConfigurarCultura()
    {
        var cultura = Formato.Cultura;
        CultureInfo.DefaultThreadCurrentCulture = cultura;
        CultureInfo.DefaultThreadCurrentUICulture = cultura;
        Thread.CurrentThread.CurrentCulture = cultura;
        Thread.CurrentThread.CurrentUICulture = cultura;

        // Sin esto, los StringFormat de los enlaces usan en-US y el dinero se vería como $6,000,000.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(cultura.IetfLanguageTag)));
    }

    public static async Task IniciarAsync(Application aplicacion)
    {
        ConfigurarManejoDeErrores(aplicacion);

        ServiceProvider proveedor;
        try
        {
            proveedor = RaizDeComposicion.Construir();
            await proveedor.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        }
        catch (Exception ex)
        {
            Registro.Error(ex, "No se pudo preparar la base de datos.");
            MessageBox.Show(
                $"No se pudo preparar la base de datos en:\n{RutasSistema.RutaBaseDatos}\n\n{ex.Message}\n\nDetalle en: {Registro.RutaActual}",
                "Sistema de Granjas", MessageBoxButton.OK, MessageBoxImage.Error);
            aplicacion.Shutdown(1);
            return;
        }

        try
        {
            if (!await IniciarSesionAsync(aplicacion, proveedor))
            {
                aplicacion.Shutdown();
                return;
            }

            var advertenciaDeCierres = await ProcesarCierresPendientesAsync(proveedor);
            await MostrarVentanaPrincipalAsync(aplicacion, proveedor, advertenciaDeCierres);
        }
        catch (Exception ex)
        {
            Registro.Error(ex, "Falla durante el arranque.");
            MessageBox.Show(
                $"El sistema no pudo iniciar.\n\n{ex.Message}\n\nDetalle en: {Registro.RutaActual}",
                "Sistema de Granjas", MessageBoxButton.OK, MessageBoxImage.Error);
            aplicacion.Shutdown(1);
        }
    }

    private static async Task<bool> IniciarSesionAsync(Application aplicacion, ServiceProvider proveedor)
    {
        if (!await proveedor.GetRequiredService<IAuthService>().EstaConfiguradoAsync())
        {
            // Primer inicio: contraseña, pregunta secreta y clave de recuperación.
            var inicio = proveedor.GetRequiredService<InicializacionPasswordViewModel>();
            var ventanaInicial = new InicializacionPasswordWindow { DataContext = inicio };
            inicio.Terminado += () => ventanaInicial.DialogResult = true;
            aplicacion.MainWindow = ventanaInicial;
            return ventanaInicial.ShowDialog() == true;
        }

        var viewModel = proveedor.GetRequiredService<LoginViewModel>();
        var ventana = new LoginWindow { DataContext = viewModel };
        viewModel.IngresoExitoso += () => ventana.DialogResult = true;
        aplicacion.MainWindow = ventana;
        return ventana.ShowDialog() == true;
    }

    /// <summary>
    /// Las instalaciones creadas antes de que existiera la recuperación de contraseña no tienen pregunta secreta ni clave:
    /// se ofrece configurarlas (sin obligar) una vez abierta la ventana principal. Un fallo aquí nunca impide usar el sistema.
    /// </summary>
    private static async Task OfrecerConfigurarRecuperacionAsync(ServiceProvider proveedor)
    {
        try
        {
            var autenticacion = proveedor.GetRequiredService<IAuthService>();
            if ((await autenticacion.ObtenerEstadoDeRecuperacionAsync()).Configurada)
            {
                return;
            }

            var dialogos = proveedor.GetRequiredService<IDialogService>();
            if (dialogos.Confirmar(
                    "Esta instalación todavía no tiene recuperación de contraseña: si la olvida no podrá restablecerla.\n\n" +
                    "¿Desea configurar ahora su pregunta secreta y obtener su clave de recuperación?\n" +
                    "(También puede hacerlo después en Configuración › Respaldos / Backup).",
                    "Recuperación de contraseña"))
            {
                dialogos.Mostrar(new ConfigurarRecuperacionViewModel(
                    autenticacion, proveedor.GetRequiredService<IReloj>(), dialogos));
            }
        }
        catch (Exception ex)
        {
            Registro.Error(ex, "No se pudo ofrecer la configuración de la recuperación de contraseña.");
        }
    }

    /// <summary>
    /// Valoriza los cierres de mes que se hayan acumulado desde la última vez que se abrió el sistema. Si falla (por
    /// ejemplo, no hay una vigencia activa) el sistema abre igual y avisa al usuario.
    /// </summary>
    private static async Task<string?> ProcesarCierresPendientesAsync(ServiceProvider proveedor)
    {
        try
        {
            var resumenes = await proveedor.GetRequiredService<IAgingEngine>().ProcesarCierresPendientesAsync();
            Registro.Informacion($"Cierres de valorización procesados al abrir: {resumenes.Count}.");
            return null;
        }
        catch (Exception ex)
        {
            Registro.Error(ex, "No se pudieron procesar los cierres de valorización pendientes.");
            return $"No se pudo valorizar el inventario al cierre de mes: {ex.Message}\nPuede intentarlo de nuevo cuando corrija la causa.";
        }
    }

    private static async Task MostrarVentanaPrincipalAsync(Application aplicacion, ServiceProvider proveedor, string? advertencia)
    {
        var shell = proveedor.GetRequiredService<ShellViewModel>();
        var ventana = new MainWindow { DataContext = shell };
        shell.CerrarAplicacion = aplicacion.Shutdown;

        // La X de la ventana pasa por el mismo flujo que el botón Salir (respaldo automático incluido).
        ventana.Closing += (_, e) =>
        {
            if (shell.SalidaAutorizada)
            {
                return;
            }

            e.Cancel = true;
            shell.SalirCommand.Execute(null);
        };

        // La ventana de acceso ya se cerró al terminar su ShowDialog; WPF reasigna MainWindow al primer Window nuevo.
        aplicacion.MainWindow = ventana;
        ventana.Show();

        if (advertencia is not null)
        {
            proveedor.GetRequiredService<IDialogService>().Advertir(advertencia, "Valorización pendiente");
        }

        await shell.IniciarAsync();
        await OfrecerConfigurarRecuperacionAsync(proveedor);
    }

    private static void ConfigurarManejoDeErrores(Application aplicacion)
    {
        aplicacion.DispatcherUnhandledException += (_, e) =>
        {
            Registro.Error(e.Exception, "Excepción no controlada en la interfaz.");
            MessageBox.Show(
                $"Ocurrió un error inesperado y la operación no se completó.\n\n{e.Exception.Message}\n\nDetalle en: {Registro.RutaActual}",
                "Sistema de Granjas", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Registro.Error(e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString()), "Excepción fatal.");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Registro.Error(e.Exception, "Tarea sin observar.");
            e.SetObserved();
        };
    }
}
