using System.Windows.Media.Imaging;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Convierte la imagen de la firma (ruta local o data URI Base64) en algo que WPF pueda mostrar.</summary>
public static class ImagenHelper
{
    public const string PrefijoPng = "data:image/png;base64,";
    public const string PrefijoJpg = "data:image/jpeg;base64,";

    /// <summary>Nulo si no hay imagen o no se puede leer (la vista simplemente no la muestra).</summary>
    public static BitmapImage? Cargar(string? origen)
    {
        var bytes = LeerBytes(origen);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            using var flujo = new MemoryStream(bytes);
            var imagen = new BitmapImage();
            imagen.BeginInit();
            imagen.CacheOption = BitmapCacheOption.OnLoad;
            imagen.StreamSource = flujo;
            imagen.EndInit();
            imagen.Freeze();
            return imagen;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException)
        {
            Registro.Error(ex, "No se pudo leer la imagen de la firma.");
            return null;
        }
    }

    /// <summary>Lee un archivo PNG/JPG y lo devuelve como data URI, para guardarlo dentro de la base de datos.</summary>
    public static string ADataUri(string rutaArchivo)
    {
        var bytes = File.ReadAllBytes(rutaArchivo);
        var prefijo = Path.GetExtension(rutaArchivo).ToLowerInvariant() is ".jpg" or ".jpeg" ? PrefijoJpg : PrefijoPng;
        return prefijo + Convert.ToBase64String(bytes);
    }

    private static byte[]? LeerBytes(string? origen)
    {
        if (string.IsNullOrWhiteSpace(origen))
        {
            return null;
        }

        try
        {
            var coma = origen.IndexOf(',');
            if (origen.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma > 0)
            {
                return Convert.FromBase64String(origen[(coma + 1)..]);
            }

            if (File.Exists(origen))
            {
                return File.ReadAllBytes(origen);
            }

            return Convert.FromBase64String(origen);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
