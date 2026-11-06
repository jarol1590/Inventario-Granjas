using System.Globalization;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Formatos de presentación en cultura es-CO: miles con punto, fechas día/mes/año.</summary>
public static class Formato
{
    public static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    public static string Dinero(decimal valor) => valor.ToString("$#,##0;-$#,##0", Cultura);

    /// <summary>Con signo explícito: +$650.000 / -$120.000 / $0.</summary>
    public static string DineroConSigno(decimal valor) => valor.ToString("+$#,##0;-$#,##0;$0", Cultura);

    public static string Fecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy", Cultura);

    /// <summary>03/11/2026 08:30 AM (como en el mockup de respaldos).</summary>
    public static string FechaHora(DateTime fecha) => fecha.ToString("dd/MM/yyyy hh:mm tt", CultureInfo.InvariantCulture);

    /// <summary>"1 animal" / "5 animales" / "1.250 animales".</summary>
    public static string Animales(int cantidad) => cantidad == 1 ? "1 animal" : $"{cantidad.ToString("N0", Cultura)} animales";

    public static string Tamano(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{(bytes / 1024d).ToString("0.#", Cultura)} KB",
        _ => $"{(bytes / 1024d / 1024d).ToString("0.#", Cultura)} MB",
    };
}
