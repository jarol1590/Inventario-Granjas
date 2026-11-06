using System.Text;
using UCaldas.Granjas.Data;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Bitácora en <c>%LOCALAPPDATA%\SistemaGranjas\logs</c>. Nunca lanza excepciones.</summary>
public static class Registro
{
    private static readonly object Candado = new();

    public static string RutaActual => Path.Combine(RutasSistema.CarpetaLogs, $"{DateTime.Now:yyyy-MM-dd}.log");

    public static void Informacion(string mensaje) => Escribir("INFO", mensaje);

    public static void Error(Exception excepcion, string contexto) => Escribir("ERROR", $"{contexto}{Environment.NewLine}{excepcion}");

    private static void Escribir(string nivel, string mensaje)
    {
        try
        {
            lock (Candado)
            {
                Directory.CreateDirectory(RutasSistema.CarpetaLogs);
                File.AppendAllText(RutaActual, $"{DateTime.Now:HH:mm:ss.fff} [{nivel}] {mensaje}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // La bitácora nunca debe tumbar la aplicación.
        }
    }
}
