using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.Services.Comercial;

/// <summary>
/// Recibo de Caja SP por sí solo: generarlo para una remisión que todavía no lo tiene (documentos anteriores al módulo, con
/// el recibo como texto) o corregirlo después de guardado. El número del recibo nunca cambia.
/// </summary>
public interface IReciboCajaService
{
    /// <summary>
    /// Crea o corrige el recibo de la remisión: fecha, forma de pago, banco, efectivo, cheques, descuento y observaciones. El total
    /// se recalcula con las líneas de la remisión. <paramref name="comercial"/> solo hace falta en una venta que aún no tiene
    /// comprador registrado (venta anterior al módulo).
    /// </summary>
    Task<ReciboCaja> GuardarAsync(int remisionId, ReciboCajaDto dto, DatosComercialesDto? comercial = null, CancellationToken ct = default);
}

public sealed class ReciboCajaService(IUnitOfWorkFactory fabrica, IReloj reloj) : IReciboCajaService
{
    public async Task<ReciboCaja> GuardarAsync(int remisionId, ReciboCajaDto dto, DatosComercialesDto? comercial = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        await using var uow = await fabrica.BeginAsync(ct);
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct) ?? throw Regla.Violada("La remisión no existe.");
        Regla.Exigir(
            remision.TipoMovimiento is TipoRemision.Venta or TipoRemision.Traslado,
            "Solo las remisiones de venta y de traslado llevan recibo de caja.");
        Regla.Exigir(!remision.Anulada, $"La remisión {remision.NumeroRemision} está anulada: su recibo no se puede modificar.");

        // Al corregir solo el recibo, el comprador es el que ya tiene la remisión.
        comercial ??= remision.TipoMovimiento == TipoRemision.Venta && remision.TipoComprador is not null
            ? new DatosComercialesDto { TipoComprador = remision.TipoComprador, ConvenioId = remision.ConvenioId, ClienteId = remision.ClienteId }
            : null;

        var recibo = await ReciboCajaRegla.AplicarAsync(uow, reloj, remision, comercial, dto, ct);
        await uow.CommitAsync(ct);
        return recibo;
    }
}
