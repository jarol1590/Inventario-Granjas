using System.Data;
using System.Globalization;
using Dapper;

namespace UCaldas.Granjas.Data;

/// <summary>
/// Configura Dapper para SQLite: nombres snake_case ↔ PascalCase y conversión explícita de fechas,
/// decimales y booleanos (SQLite no tiene esos tipos nativos).
/// </summary>
public static class SqliteMapping
{
    /// <summary>Formato de fecha almacenado: texto ISO ordenable lexicográficamente.</summary>
    public const string FormatoFecha = "yyyy-MM-dd HH:mm:ss";

    private static int _inicializado;

    /// <summary>Idempotente y segura entre hilos.</summary>
    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _inicializado, 1) == 1)
        {
            return;
        }

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new FechaHandler());
        SqlMapper.AddTypeHandler(new DecimalHandler());
        SqlMapper.AddTypeHandler(new BooleanoHandler());
    }

    /// <summary>"ChapetaAmarilla" → "chapeta_amarilla".</summary>
    internal static string ASnakeMinuscula(string nombre)
    {
        var sb = new System.Text.StringBuilder(nombre.Length + 4);
        for (var i = 0; i < nombre.Length; i++)
        {
            var c = nombre[i];
            if (char.IsUpper(c) && i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private sealed class FechaHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString(FormatoFecha, CultureInfo.InvariantCulture);
        }

        public override DateTime Parse(object value) => value switch
        {
            DateTime d => d,
            string s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None),
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture),
        };
    }

    private sealed class DecimalHandler : SqlMapper.TypeHandler<decimal>
    {
        public override void SetValue(IDbDataParameter parameter, decimal value)
        {
            parameter.DbType = DbType.Decimal;
            parameter.Value = value;
        }

        public override decimal Parse(object value) => value switch
        {
            decimal m => m,
            long l => l,
            int i => i,
            double d => (decimal)d,
            string s => decimal.Parse(s, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture),
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        };
    }

    private sealed class BooleanoHandler : SqlMapper.TypeHandler<bool>
    {
        public override void SetValue(IDbDataParameter parameter, bool value)
        {
            parameter.DbType = DbType.Int64;
            parameter.Value = value ? 1L : 0L;
        }

        public override bool Parse(object value) => value switch
        {
            bool b => b,
            long l => l != 0,
            int i => i != 0,
            string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
        };
    }
}
