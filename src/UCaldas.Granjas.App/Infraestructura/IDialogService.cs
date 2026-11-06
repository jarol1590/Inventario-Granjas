namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Mensajes, selectores de archivo y ventanas modales. Los ViewModels solo conocen esta interfaz.</summary>
public interface IDialogService
{
    void Informar(string mensaje, string? titulo = null);

    void Advertir(string mensaje, string? titulo = null);

    void MostrarError(string mensaje, string? titulo = null);

    /// <param name="destructivo">Resalta que la acción no se puede deshacer; la respuesta predeterminada es "No".</param>
    bool Confirmar(string mensaje, string? titulo = null, bool destructivo = false);

    /// <summary>Sí / No / Cancelar. Devuelve true = Sí, false = No, null = Cancelar.</summary>
    bool? ConfirmarConCancelar(string mensaje, string? titulo = null);

    /// <param name="filtro">Formato de Windows, p. ej. <c>Respaldos (*.zip)|*.zip</c>.</param>
    string? AbrirArchivo(string titulo, string filtro);

    string? GuardarArchivo(string titulo, string filtro, string nombreSugerido);

    string? ElegirCarpeta(string titulo, string? carpetaInicial = null);

    /// <summary>Pide un texto corto (año, porcentaje...). Nulo si el usuario cancela.</summary>
    string? Pedir(string titulo, string pregunta, string valorInicial = "");

    /// <summary>Muestra una ventana modal con la vista asociada al ViewModel. true = aceptado, false = cancelado.</summary>
    bool Mostrar(IDialogViewModel dialogo);

    /// <summary>Abre un archivo con el programa predeterminado de Windows (p. ej. Excel). No lanza excepciones.</summary>
    void Abrir(string ruta);

    /// <summary>Abre el Explorador de archivos con el archivo seleccionado. No lanza excepciones.</summary>
    void MostrarEnExplorador(string ruta);
}
