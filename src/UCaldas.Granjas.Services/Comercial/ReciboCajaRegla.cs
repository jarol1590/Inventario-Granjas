using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.Services.Comercial;

/// <summary>
/// Reglas del Recibo de Caja SP compartidas por los documentos que lo graban en su misma transacción (ventas y traslados de
/// animales, lotes y productos) y por la edición del recibo por sí solo. Toda remisión tiene exactamente un recibo.
/// </summary>
internal static class ReciboCajaRegla
{
    /// <summary>
    /// Aplica el comprador y el recibo a la remisión ya con sus líneas guardadas: valida, calcula subtotal, descuento y total, y
    /// crea el recibo con el siguiente consecutivo (o corrige el existente conservando su número). No hace commit.
    /// </summary>
    public static async Task<ReciboCaja> AplicarAsync(
        IUnitOfWork uow, IReloj reloj, Remision remision, DatosComercialesDto? comercial, ReciboCajaDto dto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var esVenta = remision.TipoMovimiento == TipoRemision.Venta;

        if (esVenta)
        {
            await AplicarCompradorAsync(uow, remision, comercial, ct);
        }

        ValidarRecibo(dto, esVenta, reloj);

        var lineas = await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct);
        var totales = esVenta
            ? TotalesRemision.Calcular(lineas, dto.DescuentoPorcentaje)
            : new TotalesRemision(dto.TotalPagado, 0m, dto.TotalPagado);
        Regla.Exigir(!esVenta || totales.Subtotal > 0, "La venta debe tener un valor mayor que cero.");

        remision.Subtotal = totales.Subtotal;
        remision.DescuentoPorcentaje = esVenta ? dto.DescuentoPorcentaje : 0m;
        remision.ValorDescuento = totales.ValorDescuento;
        remision.Total = totales.Total;

        var recibo = await uow.Recibos.GetByRemisionAsync(remision.Id, ct);
        if (recibo is null)
        {
            var config = await uow.ConfiguracionRemisiones.GetAsync(ct);
            var numero = Math.Max(config.UltimoConsecutivoRecibo, await uow.Recibos.GetMaxNumeroAsync(ct)) + 1;
            config.UltimoConsecutivoRecibo = numero;
            await uow.ConfiguracionRemisiones.GuardarAsync(config, ct);

            recibo = new ReciboCaja { NumeroRecibo = numero, Prefijo = "SP", RemisionId = remision.Id };
            CopiarDatos(recibo, dto, totales.Total);
            await uow.Recibos.InsertAsync(recibo, ct);
        }
        else
        {
            CopiarDatos(recibo, dto, totales.Total);
            recibo.Anulado = false;
            await uow.Recibos.UpdateAsync(recibo, ct);
        }

        // En una venta el recibo también queda referenciado en la remisión (texto «829 SP»); un traslado deja la columna vacía.
        remision.ReciboCaja = esVenta ? $"{recibo.NumeroRecibo} {recibo.Prefijo}" : null;
        await uow.Remisiones.UpdateAsync(remision, ct);
        return recibo;
    }

    /// <summary>Marca el recibo de la remisión como anulado (conserva su número). Sin recibo no hace nada.</summary>
    public static async Task AnularDeRemisionAsync(IUnitOfWork uow, int remisionId, CancellationToken ct)
    {
        var recibo = await uow.Recibos.GetByRemisionAsync(remisionId, ct);
        if (recibo is { Anulado: false })
        {
            recibo.Anulado = true;
            await uow.Recibos.UpdateAsync(recibo, ct);
        }
    }

    private static void CopiarDatos(ReciboCaja recibo, ReciboCajaDto dto, decimal total)
    {
        recibo.Fecha = dto.Fecha.Date;
        recibo.FormaPago = dto.FormaPago;
        recibo.BancoConsignacion = dto.FormaPago == FormaPago.Consignacion ? Limpio(dto.BancoConsignacion) : null;
        recibo.EsEfectivo = dto.FormaPago == FormaPago.EfectivoCheque && dto.EsEfectivo;
        recibo.Cheque1Num = dto.FormaPago == FormaPago.EfectivoCheque ? Limpio(dto.Cheque1Num) : null;
        recibo.Cheque2Num = dto.FormaPago == FormaPago.EfectivoCheque ? Limpio(dto.Cheque2Num) : null;
        recibo.TotalPagado = total;
        recibo.TotalLetras = NumeroALetras.Pesos(total);
        recibo.Observaciones = Limpio(dto.Observaciones);
    }

    internal static void ValidarRecibo(ReciboCajaDto dto, bool esVenta, IReloj reloj)
    {
        Regla.Exigir(dto.Fecha != default && dto.Fecha.Date <= reloj.Hoy, "La fecha del recibo de caja es obligatoria y no puede ser futura.");
        if (esVenta)
        {
            Regla.Exigir(
                dto.FormaPago is FormaPago.Consignacion or FormaPago.EfectivoCheque,
                "La forma de pago de una venta es consignación bancaria o cheque / efectivo.");
            Regla.Exigir(dto.DescuentoPorcentaje is >= 0m and <= 100m, "El descuento debe estar entre 0 % y 100 %.");
            if (dto.FormaPago == FormaPago.Consignacion)
            {
                Regla.ExigirTexto(dto.BancoConsignacion, "La entidad bancaria de la consignación");
            }
            else
            {
                Regla.Exigir(
                    dto.EsEfectivo || !string.IsNullOrWhiteSpace(dto.Cheque1Num),
                    "Indique si el pago fue en efectivo o el número del cheque.");
            }
        }
        else
        {
            Regla.Exigir(dto.FormaPago == FormaPago.TrasladoInterno, "El recibo de un traslado es de forma de pago «Traslado interno».");
            Regla.Exigir(dto.TotalPagado >= 0m, "El valor del recibo de un traslado no puede ser negativo.");
        }
    }

    private static async Task AplicarCompradorAsync(IUnitOfWork uow, Remision remision, DatosComercialesDto? comercial, CancellationToken ct)
    {
        Regla.Exigir(comercial is not null, "Seleccione el comprador: un convenio u otro comprador.");
        switch (comercial!.TipoComprador)
        {
            case TipoComprador.Convenio:
                var convenio = comercial.ConvenioId is { } cid ? await uow.Convenios.GetByIdAsync(cid, ct) : null;
                Regla.Exigir(convenio is not null, "Seleccione el convenio comprador.");
                Regla.Exigir(convenio!.Activo || remision.ConvenioId == convenio.Id, $"El convenio {convenio.Nombre} está inactivo.");
                remision.TipoComprador = TipoComprador.Convenio;
                remision.ConvenioId = convenio.Id;
                remision.ClienteId = null;
                remision.TerceroEntidad = convenio.Nombre;
                remision.TerceroNit = convenio.Nit;
                break;

            case TipoComprador.Otro:
                var cliente = comercial.ClienteId is { } kid ? await uow.Clientes.GetByIdAsync(kid, ct) : null;
                Regla.Exigir(cliente is not null, "Seleccione o registre el cliente comprador.");
                Regla.Exigir(cliente!.Activo || remision.ClienteId == cliente.Id, $"El cliente {cliente.NombreRazonSocial} está inactivo.");
                remision.TipoComprador = TipoComprador.Otro;
                remision.ClienteId = cliente.Id;
                remision.ConvenioId = null;
                remision.TerceroEntidad = cliente.NombreRazonSocial;
                remision.TerceroNit = cliente.NitCc;
                break;

            default:
                throw Regla.Violada("El tipo de comprador debe ser convenio u otro comprador.");
        }
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
