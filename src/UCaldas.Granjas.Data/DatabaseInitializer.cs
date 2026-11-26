using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Domain;

namespace UCaldas.Granjas.Data;

public interface IDatabaseInitializer
{
    /// <summary>
    /// Crea la base de datos con su esquema y datos semilla si no existe y, si viene de una versión anterior, la migra.
    /// Idempotente: si ya está en la versión vigente no hace nada y nunca borra ni sobrescribe datos.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);
}

public sealed class DatabaseInitializer(IDbConnectionFactory conexiones) : IDatabaseInitializer
{
    /// <summary>
    /// Versión del esquema (<c>PRAGMA user_version</c>). 1 = esquema v2.1; 2 = esquema v2.2 (recuperación de contraseña en
    /// <c>CONFIGURACION_SISTEMA</c> y huecos de la matriz 2026 completados); 3 = esquema v2.3 (programas generalizados, tarifas de referencia
    /// por categoría y módulo de lotes); 4 = esquema v2.4 (documentos de lotes y edición de su compra); 5 = esquema v2.5 (catálogo de
    /// razas y líneas por programa); 6 = esquema v2.6 (Vacas General con parto desde los 14 meses); 7 = v2.7 (observaciones de animales y movimientos);
    /// 8 = v2.8 (módulo Remisiones: convenios, clientes, productos, oficios cerrables y recibos de caja SP);
    /// 9 = v2.9 (líneas de subproductos escritas a mano y toda venta con su Recibo de Caja SP).
    /// </summary>
    public const int VersionEsquemaActual = 12;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var carpeta = Path.GetDirectoryName(conexiones.DatabasePath);
        if (!string.IsNullOrEmpty(carpeta))
        {
            Directory.CreateDirectory(carpeta);
        }

        await using var connection = await conexiones.OpenAsync(ct);
        var version = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition("PRAGMA user_version;", cancellationToken: ct));

        if (version == VersionEsquemaActual)
        {
            return;
        }

        if (version is not (0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11))
        {
            throw new InvalidOperationException(
                $"La base de datos tiene una versión de esquema desconocida ({version}); se esperaba {VersionEsquemaActual}. " +
                "No se modificó ningún dato.");
        }

        // Todo se aplica en una sola transacción: o queda completo o no queda nada.
        await using var transaction = connection.BeginTransaction();
        if (version == 0)
        {
            // Base nueva: el esquema ya incluye la v2.5 y las semillas llevan la matriz completa.
            await connection.ExecuteAsync(new CommandDefinition(LeerScript("schema.sql"), transaction: transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(LeerScript("seed.sql"), transaction: transaction, cancellationToken: ct));
        }
        else
        {
            // Bases anteriores: se aplican en orden las migraciones que falten, sin tocar los datos existentes.
            if (version == 1)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v2.sql"), transaction: transaction, cancellationToken: ct));
            }

            if (version <= 2)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v3.sql"), transaction: transaction, cancellationToken: ct));
            }

            if (version <= 3)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v4.sql"), transaction: transaction, cancellationToken: ct));
            }

            if (version <= 4)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v5.sql"), transaction: transaction, cancellationToken: ct));
            }

            if (version <= 5)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v6.sql"), transaction: transaction, cancellationToken: ct));
            }

            if (version <= 6)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v7.sql"), transaction: transaction, cancellationToken: ct));
            }

            if (version <= 7)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v8.sql"), transaction: transaction, cancellationToken: ct));
            }

            // REMISION_DETALLE se reconstruye una sola vez (para admitir líneas de producto).
            var tieneProducto = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM pragma_table_info('REMISION_DETALLE') WHERE name = 'producto_id'", transaction: transaction, cancellationToken: ct));
            if (tieneProducto == 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v8_detalle.sql"), transaction: transaction, cancellationToken: ct));
            }

            // v2.9: líneas de subproductos escritas a mano (otro CHECK en REMISION_DETALLE).
            var definicion = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'REMISION_DETALLE'", transaction: transaction, cancellationToken: ct));
            if (definicion?.Contains("descripcion_item IS NOT NULL", StringComparison.OrdinalIgnoreCase) != true)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v9_detalle.sql"), transaction: transaction, cancellationToken: ct));
            }

            await ConvertirRecibosAnterioresAsync(connection, transaction, ct);

            // v2.10: el NIT o cédula del cliente pasa a ser opcional (CLIENTE se reconstruye).
            if (version <= 9)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v10.sql"), transaction: transaction, cancellationToken: ct));
            }

            // v2.11: nuevas formas de pago del recibo (RECIBO_CAJA se reconstruye con el CHECK ampliado).
            if (version <= 10)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v11.sql"), transaction: transaction, cancellationToken: ct));
            }

            // v2.12: número y tipo de cuenta en el recibo de caja.
            if (version <= 11)
            {
                await connection.ExecuteAsync(new CommandDefinition(LeerScript("migracion_v12.sql"), transaction: transaction, cancellationToken: ct));
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            $"PRAGMA user_version = {VersionEsquemaActual};", transaction: transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
    }

    private sealed class VentaSinRecibo
    {
        public int Id { get; set; }
        public DateTime FechaRemision { get; set; }
        public string? ReciboCaja { get; set; }
        public bool Anulada { get; set; }
        public decimal DescuentoPorcentaje { get; set; }
    }

    private sealed class ConfiguracionBanco
    {
        public string EntidadBancaria { get; set; } = string.Empty;
        public int UltimoConsecutivoRecibo { get; set; }
    }

    /// <summary>
    /// v2.9: toda venta tiene su Recibo de Caja SP. Las ventas registradas con el recibo escrito a mano (p. ej. «RC-1») reciben el
    /// siguiente consecutivo, por fecha; el número anterior queda en las observaciones del recibo. Fecha = la de la venta y forma
    /// de pago = consignación al banco configurado (se corrigen después en Recibos Caja). Los valores se suman en C# (decimal).
    /// </summary>
    private static async Task ConvertirRecibosAnterioresAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        var ventas = (await connection.QueryAsync<VentaSinRecibo>(new CommandDefinition(
            """
            SELECT r.id AS Id, r.fecha_remision AS FechaRemision, r.recibo_caja AS ReciboCaja, r.anulada AS Anulada,
                   r.descuento_porcentaje AS DescuentoPorcentaje
            FROM REMISION r
            WHERE r.tipo_movimiento = 'VENTA' AND NOT EXISTS (SELECT 1 FROM RECIBO_CAJA c WHERE c.remision_id = r.id)
            ORDER BY r.fecha_remision, r.id
            """, transaction: transaction, cancellationToken: ct))).ToList();
        if (ventas.Count == 0)
        {
            return;
        }

        var config = await connection.QuerySingleAsync<ConfiguracionBanco>(new CommandDefinition(
            "SELECT entidad_bancaria AS EntidadBancaria, ultimo_consecutivo_recibo AS UltimoConsecutivoRecibo FROM CONFIGURACION_REMISIONES WHERE id = 1",
            transaction: transaction, cancellationToken: ct));
        var emitido = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(MAX(numero_recibo), 0) FROM RECIBO_CAJA", transaction: transaction, cancellationToken: ct));
        var numero = (int)Math.Max(config.UltimoConsecutivoRecibo, emitido);

        foreach (var venta in ventas)
        {
            numero++;
            var lineas = await connection.QueryAsync<decimal?>(new CommandDefinition(
                "SELECT valor_total_transaccion FROM REMISION_DETALLE WHERE remision_id = @Id", new { venta.Id }, transaction, cancellationToken: ct));
            var subtotal = lineas.Sum(v => v ?? 0m);
            var descuento = Math.Round(subtotal * venta.DescuentoPorcentaje / 100m, 0, MidpointRounding.AwayFromZero);
            var total = subtotal - descuento;
            var anterior = string.IsNullOrWhiteSpace(venta.ReciboCaja) ? null : $"Recibo anterior: {venta.ReciboCaja.Trim()}";

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO RECIBO_CAJA (numero_recibo, prefijo, remision_id, fecha, forma_pago, banco_consignacion, es_efectivo,
                                         total_pagado, total_letras, observaciones, anulado)
                VALUES (@numero, 'SP', @Id, @FechaRemision, 'CONSIGNACION', @banco, 0, @total, @letras, @anterior, @Anulada)
                """,
                new { numero, venta.Id, venta.FechaRemision, banco = config.EntidadBancaria, total, letras = NumeroALetras.Pesos(total), anterior, venta.Anulada },
                transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE REMISION SET recibo_caja = @etiqueta, subtotal = @subtotal, valor_descuento = @descuento, total = @total WHERE id = @Id",
                new { etiqueta = $"{numero} SP", subtotal, descuento, total, venta.Id }, transaction, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE CONFIGURACION_REMISIONES SET ultimo_consecutivo_recibo = @numero WHERE id = 1", new { numero }, transaction, cancellationToken: ct));
    }

    private static string LeerScript(string nombre)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        var recurso = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("." + nombre, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException($"No se encontró el recurso incrustado {recurso}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
