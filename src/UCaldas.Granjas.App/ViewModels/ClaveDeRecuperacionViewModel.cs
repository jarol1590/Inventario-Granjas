using System.Text;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Seguridad;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>
/// Muestra la clave de recuperación recién generada (una sola vez: en la base solo queda su hash) y deja copiarla o
/// guardarla en un archivo de texto. El usuario debe confirmar que ya la guardó antes de continuar.
/// </summary>
public sealed partial class ClaveDeRecuperacionViewModel : ViewModelBase
{
    public const string NombreSugeridoDelArchivo = "ClaveDeRecuperacion_SistemaGranjas.txt";

    private readonly IReloj _reloj;

    public ClaveDeRecuperacionViewModel(IDialogService dialogos, IReloj reloj, string clave)
        : base(dialogos)
    {
        _reloj = reloj;
        Clave = AuthService.FormatearClave(clave);
    }

    /// <summary>La clave tal como se lee y se copia: <c>ABCD-EFGH-JKMN-PQRS</c>.</summary>
    public string Clave { get; }

    /// <summary>true cuando el usuario marcó que ya la guardó en un lugar seguro.</summary>
    [ObservableProperty]
    private bool _confirmada;

    [ObservableProperty]
    private string? _mensaje;

    [RelayCommand]
    private void Copiar()
    {
        Mensaje = Dialogos.CopiarAlPortapapeles(Clave)
            ? "✔ La clave se copió al portapapeles. Péguela en un lugar seguro."
            : "No se pudo copiar: otro programa tiene ocupado el portapapeles. Inténtelo de nuevo o guárdela en un archivo.";
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var ruta = Dialogos.GuardarArchivo(
            "Guardar la clave de recuperación", "Archivo de texto (*.txt)|*.txt", NombreSugeridoDelArchivo);
        if (ruta is null)
        {
            return;
        }

        // UTF-8 con marca de orden de bytes: el Bloc de notas muestra bien las tildes.
        if (await EjecutarAsync(
                () => File.WriteAllTextAsync(ruta, ContenidoDelArchivo(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)),
                "guardar la clave de recuperación"))
        {
            Mensaje = $"✔ La clave se guardó en:\n{ruta}\nCopie ese archivo a un lugar seguro, fuera de este equipo.";
        }
    }

    /// <summary>Texto del archivo que se entrega al usuario.</summary>
    public string ContenidoDelArchivo() =>
        $"""
        SISTEMA UNIFICADO DE GESTIÓN DE GRANJAS — UNIVERSIDAD DE CALDAS
        CLAVE DE RECUPERACIÓN DE CONTRASEÑA

        Clave: {Clave}
        Generada el: {_reloj.Ahora:dd/MM/yyyy HH:mm}

        Con esta clave (o con su pregunta secreta) puede restablecer la contraseña del sistema sin perder ningún dato.
        - Guárdela en un lugar seguro y fuera de este equipo (memoria USB, correo personal, caja fuerte).
        - Quien la tenga puede entrar al sistema: no la comparta.
        - El sistema no la guarda: solo conserva una huella cifrada, así que no se puede volver a consultar.
        - Se usa una sola vez: al restablecer la contraseña el sistema genera una clave nueva.
        """;
}
