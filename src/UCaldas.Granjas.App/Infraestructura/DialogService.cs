using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace UCaldas.Granjas.App.Infraestructura;

public sealed class DialogService : IDialogService
{
    private const string TituloPredeterminado = "Sistema de Granjas";

    // Ventanas modales abiertas (la última es la que está encima). Sirven de dueñas de los mensajes y de las
    // ventanas que se abran desde ellas, incluso en el instante en que ninguna ventana está activa.
    private readonly Stack<Window> _modales = new();

    public void Informar(string mensaje, string? titulo = null)
        => MessageBox.Show(Propietario(), mensaje, titulo ?? TituloPredeterminado, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Advertir(string mensaje, string? titulo = null)
        => MessageBox.Show(Propietario(), mensaje, titulo ?? "Revise los datos", MessageBoxButton.OK, MessageBoxImage.Warning);

    public void MostrarError(string mensaje, string? titulo = null)
        => MessageBox.Show(Propietario(), mensaje, titulo ?? "Ocurrió un problema", MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirmar(string mensaje, string? titulo = null, bool destructivo = false)
        => MessageBox.Show(
            Propietario(), mensaje, titulo ?? "Confirmar",
            MessageBoxButton.YesNo,
            destructivo ? MessageBoxImage.Warning : MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public bool? ConfirmarConCancelar(string mensaje, string? titulo = null)
        => MessageBox.Show(
            Propietario(), mensaje, titulo ?? "Confirmar",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public string? AbrirArchivo(string titulo, string filtro)
    {
        var dialogo = new OpenFileDialog { Title = titulo, Filter = filtro, CheckFileExists = true, Multiselect = false };
        return dialogo.ShowDialog(Propietario()) == true ? dialogo.FileName : null;
    }

    public string? GuardarArchivo(string titulo, string filtro, string nombreSugerido)
    {
        var dialogo = new SaveFileDialog { Title = titulo, Filter = filtro, FileName = nombreSugerido, OverwritePrompt = true, AddExtension = true };
        return dialogo.ShowDialog(Propietario()) == true ? dialogo.FileName : null;
    }

    public string? ElegirCarpeta(string titulo, string? carpetaInicial = null)
    {
        var dialogo = new OpenFolderDialog { Title = titulo, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(carpetaInicial) && Directory.Exists(carpetaInicial))
        {
            dialogo.InitialDirectory = carpetaInicial;
        }

        return dialogo.ShowDialog(Propietario()) == true ? dialogo.FolderName : null;
    }

    public string? Pedir(string titulo, string pregunta, string valorInicial = "")
    {
        var viewModel = new DialogoEntradaViewModel(this, titulo, pregunta, valorInicial);
        return Mostrar(viewModel) ? viewModel.Valor.Trim() : null;
    }

    public bool Mostrar(IDialogViewModel dialogo)
    {
        var ventana = new DialogHostWindow
        {
            DataContext = dialogo,
            Width = dialogo.Ancho,
            Owner = Propietario(),
            MaxHeight = SystemParameters.WorkArea.Height - 24,
        };

        if (dialogo.Alto > 0)
        {
            ventana.SizeToContent = SizeToContent.Manual;
            ventana.Height = Math.Min(dialogo.Alto, SystemParameters.WorkArea.Height - 24);
        }

        var cerrada = false;
        ventana.Closed += (_, _) => cerrada = true;

        void AlSolicitarCierre(bool aceptado)
        {
            // El usuario pudo cerrar la ventana con la X antes de que termine una operación asincrónica.
            if (!cerrada)
            {
                ventana.DialogResult = aceptado;
            }
        }

        dialogo.CierreSolicitado += AlSolicitarCierre;
        _modales.Push(ventana);
        try
        {
            return ventana.ShowDialog() == true;
        }
        finally
        {
            _modales.Pop();
            dialogo.CierreSolicitado -= AlSolicitarCierre;
        }
    }

    public void Abrir(string ruta)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Registro.Error(ex, $"No se pudo abrir {ruta}.");
            Advertir($"El archivo se guardó, pero no se pudo abrir automáticamente.\n\n{ruta}");
        }
    }

    public void MostrarEnExplorador(string ruta)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ruta}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Registro.Error(ex, $"No se pudo mostrar {ruta} en el Explorador.");
        }
    }

    private Window? Propietario()
    {
        if (_modales.TryPeek(out var modal) && modal.IsVisible)
        {
            return modal;
        }

        var aplicacion = Application.Current;
        if (aplicacion is null)
        {
            return null;
        }

        return aplicacion.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
            ?? (aplicacion.MainWindow is { IsVisible: true } principal ? principal : null);
    }
}
