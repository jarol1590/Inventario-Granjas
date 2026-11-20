namespace UCaldas.Granjas.Services.Comercial;

/// <summary>Convierte valores en pesos a letras en español para el Recibo de Caja («CUATROCIENTOS VEINTE MIL PESOS M/CTE»).</summary>
public static class NumeroALetras
{
    private static readonly string[] Unidades =
    [
        "CERO", "UN", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE",
        "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE", "VEINTE", "VEINTIÚN", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO",
        "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE",
    ];

    private static readonly string[] Decenas = ["", "", "", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"];

    private static readonly string[] Centenas =
        ["", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"];

    /// <summary>«1.000.000» → «UN MILLÓN DE PESOS M/CTE»; los centavos, si hay, se escriben como «CON 50/100».</summary>
    public static string Pesos(decimal valor)
    {
        var negativo = valor < 0;
        var absoluto = Math.Abs(valor);
        var enteros = (long)decimal.Truncate(absoluto);
        var centavos = (int)Math.Round((absoluto - enteros) * 100m, MidpointRounding.AwayFromZero);
        if (centavos == 100)
        {
            enteros++;
            centavos = 0;
        }

        var letras = Entero(enteros);
        var moneda = enteros == 1 ? "PESO" : enteros % 1_000_000 == 0 && enteros != 0 ? "DE PESOS" : "PESOS";
        var texto = $"{letras} {moneda}" + (centavos > 0 ? $" CON {centavos:00}/100" : string.Empty) + " M/CTE";
        return negativo ? "MENOS " + texto : texto;
    }

    /// <summary>Un entero en letras, sin moneda («420000» → «CUATROCIENTOS VEINTE MIL»).</summary>
    public static string Entero(long n)
    {
        if (n == 0)
        {
            return "CERO";
        }

        if (n < 0)
        {
            return "MENOS " + Entero(-n);
        }

        var partes = new List<string>();
        var billones = n / 1_000_000_000_000;
        var resto = n % 1_000_000_000_000;
        if (billones > 0)
        {
            partes.Add(billones == 1 ? "UN BILLÓN" : $"{Menor1000Apocopado(billones)} BILLONES");
        }

        var millones = resto / 1_000_000;
        resto %= 1_000_000;
        if (millones > 0)
        {
            partes.Add(millones == 1 ? "UN MILLÓN" : $"{Menor1000Apocopado(millones)} MILLONES");
        }

        var miles = resto / 1000;
        resto %= 1000;
        if (miles > 0)
        {
            partes.Add(miles == 1 ? "MIL" : $"{Menor1000Apocopado(miles)} MIL");
        }

        if (resto > 0)
        {
            partes.Add(Menor1000(resto));
        }

        return string.Join(" ", partes);
    }

    /// <summary>Antes de «mil», «millones»... el uno se apocopa («VEINTIÚN MIL», «TREINTA Y UN MIL»).</summary>
    private static string Menor1000Apocopado(long n)
    {
        return Entero(n);
    }

    private static string Menor1000(long n)
    {
        if (n == 100)
        {
            return "CIEN";
        }

        var centena = (int)(n / 100);
        var resto = (int)(n % 100);
        var partes = new List<string>();
        if (centena > 0)
        {
            partes.Add(Centenas[centena]);
        }

        if (resto > 0)
        {
            partes.Add(Menor100(resto));
        }

        return string.Join(" ", partes);
    }

    private static string Menor100(int n)
    {
        if (n < 30)
        {
            return Unidades[n];
        }

        var decena = n / 10;
        var unidad = n % 10;
        return unidad == 0 ? Decenas[decena] : $"{Decenas[decena]} Y {Unidades[unidad]}";
    }
}
