using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Comercial;

namespace UCaldas.Granjas.Services.Kardex;

/// <summary>
/// Módulo Remisiones (v2.8): el Recibo de Caja SP se graba o corrige en la misma transacción de la remisión. Los documentos
/// registrados desde Salidas o Traslados (sin <c>Recibo</c> en el DTO) no pasan por aquí y quedan como antes.
/// </summary>
public sealed partial class KardexService
{
    /// <summary>Texto provisional del recibo en una venta: se reemplaza por «829 SP» al asignar el consecutivo.</summary>
    private const string ReciboProvisional = "SP-PENDIENTE";

    /// <summary>El validador de la venta exige el recibo; en el módulo Remisiones se genera, así que se le da un valor provisional.</summary>
    private static string ReciboParaValidar(string? actual, ReciboCajaDto? recibo)
        => recibo is not null && string.IsNullOrWhiteSpace(actual) ? ReciboProvisional : actual ?? string.Empty;

    /// <summary>Fija el programa de la remisión (el del oficio o el deducido de sus líneas) y graba comprador y recibo.</summary>
    private async Task GuardarReciboAsync(
        IUnitOfWork uow, Remision remision, int? programaId, DatosComercialesDto? comercial, ReciboCajaDto? recibo, CancellationToken ct)
    {
        if (recibo is null)
        {
            // Toda venta tiene su Recibo SP (v2.9): si quien registra no lo trae, se conserva el que ya tenía o se crea uno con
            // la fecha de la venta y consignación al banco configurado (se corrige después en Remisiones › Recibos Caja).
            if (remision.TipoMovimiento != TipoRemision.Venta)
            {
                return;
            }

            recibo = await ReciboPorDefectoAsync(uow, remision, ct);
        }

        remision.ProgramaId = programaId ?? await ProgramaDeLasLineasAsync(uow, remision, ct);
        await ReciboCajaRegla.AplicarAsync(uow, reloj, remision, comercial, recibo, ct);
    }

    private static async Task<ReciboCajaDto> ReciboPorDefectoAsync(IUnitOfWork uow, Remision remision, CancellationToken ct)
    {
        if (await uow.Recibos.GetByRemisionAsync(remision.Id, ct) is { } actual)
        {
            return new ReciboCajaDto
            {
                Fecha = actual.Fecha, FormaPago = actual.FormaPago, BancoConsignacion = actual.BancoConsignacion, EsEfectivo = actual.EsEfectivo,
                Cheque1Num = actual.Cheque1Num, Cheque2Num = actual.Cheque2Num, Observaciones = actual.Observaciones,
                DescuentoPorcentaje = remision.DescuentoPorcentaje,
            };
        }

        var config = await uow.ConfiguracionRemisiones.GetAsync(ct);
        var escrito = remision.ReciboCaja;
        return new ReciboCajaDto
        {
            Fecha = remision.FechaRemision,
            FormaPago = FormaPago.Consignacion,
            BancoConsignacion = config.EntidadBancaria,
            Observaciones = string.IsNullOrWhiteSpace(escrito) || escrito == ReciboProvisional ? null : $"Recibo anterior: {escrito.Trim()}",
        };
    }

    /// <summary>El programa de una remisión de traslado sale de lo que se traslada: categoría del animal, programa del lote o del producto.</summary>
    private static async Task<int?> ProgramaDeLasLineasAsync(IUnitOfWork uow, Remision remision, CancellationToken ct)
    {
        var detalle = (await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct)).FirstOrDefault();
        if (detalle is null)
        {
            return remision.ProgramaId;
        }

        if (detalle.AnimalId is { } animalId)
        {
            var animal = await uow.Animales.GetByIdAsync(animalId, ct);
            return animal is null ? remision.ProgramaId : (await uow.Categorias.GetByIdAsync(animal.CategoriaId, ct))?.ProgramaId;
        }

        if (detalle.LoteId is { } loteId)
        {
            return (await uow.Lotes.GetByIdAsync(loteId, ct))?.ProgramaId;
        }

        return detalle.ProductoId is { } productoId ? (await uow.Productos.GetByIdAsync(productoId, ct))?.ProgramaId : remision.ProgramaId;
    }
}
