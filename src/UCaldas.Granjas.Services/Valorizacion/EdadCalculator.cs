namespace UCaldas.Granjas.Services.Valorizacion;

/// <summary>Cálculo de la edad de un semoviente en meses cumplidos.</summary>
public static class EdadCalculator
{
    /// <summary>
    /// Meses completos transcurridos entre el nacimiento y la fecha de referencia. Un mes se cumple cuando se
    /// alcanza el mismo día del mes de nacimiento; si ese día no existe en el mes de referencia (nació el 31 y el mes
    /// termina el 28/29/30) se cumple el último día del mes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Si la referencia es anterior al nacimiento.</exception>
    public static int Meses(DateTime fechaNacimiento, DateTime fechaReferencia)
    {
        var nacimiento = fechaNacimiento.Date;
        var referencia = fechaReferencia.Date;

        if (referencia < nacimiento)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fechaReferencia),
                $"La fecha de referencia ({referencia:dd/MM/yyyy}) es anterior al nacimiento ({nacimiento:dd/MM/yyyy}).");
        }

        var meses = ((referencia.Year - nacimiento.Year) * 12) + (referencia.Month - nacimiento.Month);

        var esUltimoDiaDelMes = referencia.Day == DateTime.DaysInMonth(referencia.Year, referencia.Month);
        if (referencia.Day < nacimiento.Day && !esUltimoDiaDelMes)
        {
            meses--;
        }

        return meses;
    }

    /// <summary>Último día del mes de la fecha dada.</summary>
    public static DateTime FinDeMes(DateTime fecha)
        => new(fecha.Year, fecha.Month, DateTime.DaysInMonth(fecha.Year, fecha.Month));
}
