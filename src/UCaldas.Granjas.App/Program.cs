using System.Windows;
using UCaldas.Granjas.App.Composicion;

namespace UCaldas.Granjas.App;

/// <summary>Punto de entrada: garantiza una sola instancia (la base SQLite es de un único usuario) y arranca la aplicación.</summary>
public static class Program
{
    private const string NombreMutex = @"Local\UCaldas.SistemaGranjas";

    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, NombreMutex, out var esPrimeraInstancia);
        if (!esPrimeraInstancia)
        {
            MessageBox.Show(
                "El Sistema de Granjas ya está abierto en este equipo. Ciérrelo antes de abrir otra ventana.",
                "Sistema de Granjas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Arranque.ConfigurarCultura();
        var aplicacion = new App();
        aplicacion.InitializeComponent();
        aplicacion.Startup += async (_, _) => await Arranque.IniciarAsync(aplicacion);
        aplicacion.Run();
    }
}
