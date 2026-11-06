using UCaldas.Granjas.App.Infraestructura;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>
/// Doble de <see cref="IDialogService"/> para probar ViewModels sin abrir ventanas: registra los mensajes que el usuario
/// habría visto y responde a las confirmaciones con valores configurables.
/// </summary>
public sealed class DialogoFalso : IDialogService
{
    public List<string> Informaciones { get; } = [];

    public List<string> Advertencias { get; } = [];

    public List<string> Errores { get; } = [];

    public List<string> Confirmaciones { get; } = [];

    /// <summary>Respuesta a <see cref="Confirmar"/>.</summary>
    public bool RespuestaConfirmar { get; set; } = true;

    /// <summary>Respuesta a <see cref="Pedir"/>; nulo = el usuario cancela.</summary>
    public string? RespuestaPedir { get; set; }

    public bool RespuestaMostrar { get; set; }

    public List<IDialogViewModel> Mostrados { get; } = [];

    public string? UltimaAdvertencia => Advertencias.LastOrDefault();

    public void Informar(string mensaje, string? titulo = null) => Informaciones.Add(mensaje);

    public void Advertir(string mensaje, string? titulo = null) => Advertencias.Add(mensaje);

    public void MostrarError(string mensaje, string? titulo = null) => Errores.Add(mensaje);

    public bool Confirmar(string mensaje, string? titulo = null, bool destructivo = false)
    {
        Confirmaciones.Add(mensaje);
        return RespuestaConfirmar;
    }

    public bool? ConfirmarConCancelar(string mensaje, string? titulo = null)
    {
        Confirmaciones.Add(mensaje);
        return RespuestaConfirmar;
    }

    /// <summary>Ruta que devuelve <see cref="GuardarArchivo"/>; nulo = el usuario cancela el cuadro.</summary>
    public string? RutaAGuardar { get; set; }

    public List<string> NombresSugeridos { get; } = [];

    public List<string> Abiertos { get; } = [];

    /// <summary>Ruta que devuelve <see cref="AbrirArchivo"/>; nulo = el usuario cancela el cuadro.</summary>
    public string? RutaAAbrir { get; set; }

    /// <summary>Carpeta que devuelve <see cref="ElegirCarpeta"/>; nulo = el usuario cancela el cuadro.</summary>
    public string? CarpetaAElegir { get; set; }

    /// <summary>Si se define, <see cref="Mostrar"/> la ejecuta y devuelve su resultado (permite "usar" la ventana en la prueba).</summary>
    public Func<IDialogViewModel, bool>? AlMostrar { get; set; }

    public string? AbrirArchivo(string titulo, string filtro) => RutaAAbrir;

    public string? GuardarArchivo(string titulo, string filtro, string nombreSugerido)
    {
        NombresSugeridos.Add(nombreSugerido);
        return RutaAGuardar;
    }

    public string? ElegirCarpeta(string titulo, string? carpetaInicial = null) => CarpetaAElegir;

    public string? Pedir(string titulo, string pregunta, string valorInicial = "") => RespuestaPedir;

    public bool Mostrar(IDialogViewModel dialogo)
    {
        Mostrados.Add(dialogo);
        return AlMostrar?.Invoke(dialogo) ?? RespuestaMostrar;
    }

    public void Abrir(string ruta) => Abiertos.Add(ruta);

    public void MostrarEnExplorador(string ruta)
    {
    }
}
