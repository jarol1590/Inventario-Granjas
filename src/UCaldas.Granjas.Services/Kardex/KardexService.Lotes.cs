using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Kardex;

/// <summary>
/// Documentos de lotes: venta y traslado. Son remisiones reales (con sus niveles documentales) cuyas líneas apuntan a un
/// lote (<c>lote_id</c> + <c>cantidad</c>), editables y anulables como las de animales. Todavía NO escriben en el libro
/// (<c>MOVIMIENTO_INVENTARIO</c>): el Kardex de lotes está pendiente. Editar = revertir las líneas y volver a aplicarlas.
/// </summary>
public sealed partial class KardexService
{
    private const string PrefijoUbicacionOrigen = "Ubicación de origen: ";

    // ---- Venta de lotes ---------------------------------------------------------------------------------------------

    public async Task<SalidaResultado> RegistrarVentaLoteAsync(VentaLoteDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is null, "Esta venta ya está registrada; use la edición para modificarla.");
        ValidarVentaLote(dto);

        await using var uow = await fabrica.BeginAsync(ct);
        await ObtenerGranjaActivaAsync(uow, dto.GranjaOrigenId, "La granja de origen", ct);
        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, null, ct);

        var remision = new Remision { TipoMovimiento = TipoRemision.Venta, ClaseSalida = ClaseSalida.Venta };
        var oficio = await PrepararVentaLoteAsync(uow, remision, dto, numero, ct);
        await uow.Remisiones.InsertAsync(remision, ct);
        await AplicarLineasDeVentaAsync(uow, remision, dto, ct);

        var resultado = await ResumirSalidaDeLotesAsync(uow, remision.Id, oficio.Id, ct);
        await uow.CommitAsync(ct);
        return resultado;
    }

    public async Task<SalidaResultado> EditarVentaLoteAsync(VentaLoteDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is not null, "Falta indicar la venta que se va a editar.");
        ValidarVentaLote(dto);

        await using var uow = await fabrica.BeginAsync(ct);
        var remision = await CargarDocumentoDeLotesAsync(uow, dto.RemisionId!.Value, TipoRemision.Venta, ct);
        ExigirMismaGranjaDeOrigen(dto.GranjaOrigenId, remision);
        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, remision.Id, ct);

        await RevertirLineasDeVentaAsync(uow, remision, eliminarLineas: true, ct);
        var oficio = await PrepararVentaLoteAsync(uow, remision, dto, numero, ct);
        await uow.Remisiones.UpdateAsync(remision, ct);
        await AplicarLineasDeVentaAsync(uow, remision, dto, ct);

        var resultado = await ResumirSalidaDeLotesAsync(uow, remision.Id, oficio.Id, ct);
        await uow.CommitAsync(ct);
        return resultado;
    }

    private void ValidarVentaLote(VentaLoteDto dto)
    {
        Regla.ExigirTexto(dto.NumeroOficio, "El número de oficio de venta");
        Regla.Exigir(dto.ProgramaId > 0, "Seleccione el programa del oficio de venta.");
        Regla.ExigirTexto(dto.NumeroRemision, "El número de remisión");
        Regla.Exigir(dto.FechaRemision != default && dto.FechaRemision.Date <= reloj.Hoy, "La fecha de la salida es obligatoria y no puede ser futura.");
        Regla.Exigir(dto.GranjaOrigenId > 0, "Seleccione la granja de origen.");
        Regla.ExigirTexto(dto.Comprador, "El comprador");
        Regla.ExigirTexto(dto.ReciboCaja, "El número de recibo de caja (obligatorio en las ventas)");
        Regla.Exigir(dto.Lotes.Count > 0, "Agregue al menos un lote a la venta.");
        Regla.Exigir(dto.Lotes.Select(l => l.LoteId).Distinct().Count() == dto.Lotes.Count, "Hay lotes repetidos en la lista.");
        foreach (var linea in dto.Lotes)
        {
            Regla.Exigir(linea.LoteId > 0, "Hay una línea sin lote.");
            Regla.Exigir(linea.ValorVenta > 0, "Cada lote vendido debe tener un valor de venta mayor que cero.");
            Regla.Exigir(linea.PesoKg is null or >= 0, "El peso no puede ser negativo.");
        }
    }

    /// <summary>Nivel 1 (oficio) y nivel 2 (remisión) de la venta; deja los datos del encabezado en <paramref name="remision"/>.</summary>
    private async Task<OficioVenta> PrepararVentaLoteAsync(IUnitOfWork uow, Remision remision, VentaLoteDto dto, string numero, CancellationToken ct)
    {
        var programa = await uow.Programas.GetByIdAsync(dto.ProgramaId, ct) ?? throw Regla.Violada("El programa del oficio de venta no existe.");
        Regla.Exigir(programa.TipoInventario == TipoInventario.Lote, $"El programa {programa.Nombre} no se maneja por lotes.");

        var numeroOficio = Limpiar(dto.NumeroOficio);
        var oficio = await uow.Oficios.GetByNumeroAsync(numeroOficio, ct);
        if (oficio is null)
        {
            Regla.Exigir(dto.FechaOficio != default, $"El oficio {numeroOficio} es nuevo: indique su fecha.");
            oficio = new OficioVenta
            {
                NumeroOficio = numeroOficio,
                ProgramaId = programa.Id,
                FechaOficio = dto.FechaOficio.Date,
                Observaciones = LimpiarONulo(dto.ObservacionesOficio),
            };
            await uow.Oficios.InsertAsync(oficio, ct);
        }
        else
        {
            Regla.Exigir(
                oficio.ProgramaId == programa.Id,
                $"El oficio {numeroOficio} pertenece a otro programa: un oficio solo puede tener remisiones de un solo programa.");
        }

        remision.NumeroRemision = numero;
        remision.OficioId = oficio.Id;
        remision.ReciboCaja = Limpiar(dto.ReciboCaja);
        remision.GranjaOrigenId = dto.GranjaOrigenId;
        remision.FechaRemision = dto.FechaRemision.Date;
        remision.TerceroEntidad = Limpiar(dto.Comprador);
        remision.Observaciones = LimpiarONulo(dto.Observaciones);
        validador.ValidarJerarquiaDocumental(remision);
        return oficio;
    }

    private async Task AplicarLineasDeVentaAsync(IUnitOfWork uow, Remision remision, VentaLoteDto dto, CancellationToken ct)
    {
        var programa = (await uow.Programas.GetByIdAsync(dto.ProgramaId, ct))!;
        foreach (var linea in dto.Lotes)
        {
            var lote = await ObtenerLoteParaSalidaAsync(uow, linea.LoteId, dto.GranjaOrigenId, programa.Id, "vender", ct);
            var cabezas = lote.ControlNumericoPoblacion ? linea.Cantidad : 0;
            if (lote.ControlNumericoPoblacion)
            {
                Regla.Exigir(cabezas > 0, $"Indique las cabezas vendidas del lote {lote.CodigoLote}.");
                Regla.Exigir(cabezas <= lote.CantidadActual, $"El lote {lote.CodigoLote} solo tiene {lote.CantidadActual:N0} cabezas; no se pueden vender {cabezas:N0}.");
            }

            if (programa.UnidadVentaDefault == UnidadCobro.Kg)
            {
                Regla.Exigir(linea.PesoKg is > 0, $"El programa {programa.Nombre} se vende por kilogramo: indique el peso en báscula del lote {lote.CodigoLote}.");
            }

            var costo = lote.ControlNumericoPoblacion ? cabezas * lote.ValorContableUnitario : lote.ValorTotalContable;
            await uow.RemisionDetalles.InsertAsync(new RemisionDetalle
            {
                RemisionId = remision.Id,
                LoteId = lote.Id,
                Cantidad = cabezas,
                PesoVendidoKg = linea.PesoKg,
                ValorUnitarioTransaccion = cabezas > 0 ? linea.ValorVenta / cabezas : linea.ValorVenta,
                ValorTotalTransaccion = linea.ValorVenta,
                CostoSistemaLibros = costo,
                ObservacionesEspecificas = LimpiarONulo(linea.ObservacionesEspecificas),
            }, ct);

            lote.CantidadActual -= cabezas;
            lote.ValorTotalContable = TotalDelLote(lote);
            if (lote.CantidadActual == 0)
            {
                lote.Estado = EstadoLote.Finalizado;
            }

            lote.Operaciones++;
            lote.Observaciones = AnotarLote(lote.Observaciones, $"Venta {remision.NumeroRemision}: " + (lote.ControlNumericoPoblacion ? $"{cabezas:N0} cabezas" : "lote completo"));
            await uow.Lotes.UpdateAsync(lote, ct);
        }
    }

    /// <summary>Devuelve las cabezas vendidas a sus lotes (y los reactiva). Con <paramref name="eliminarLineas"/> borra las líneas.</summary>
    private static async Task RevertirLineasDeVentaAsync(IUnitOfWork uow, Remision remision, bool eliminarLineas, CancellationToken ct)
    {
        foreach (var detalle in (await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct)).Where(d => d.LoteId is not null))
        {
            var lote = await uow.Lotes.GetByIdAsync(detalle.LoteId!.Value, ct);
            if (lote is not null)
            {
                lote.CantidadActual += detalle.Cantidad;
                lote.ValorTotalContable = TotalDelLote(lote);
                lote.Estado = lote.Anulado ? EstadoLote.Finalizado : EstadoLote.Activo;
                lote.Operaciones = Math.Max(0, lote.Operaciones - 1);
                await uow.Lotes.UpdateAsync(lote, ct);
            }

            if (eliminarLineas)
            {
                await uow.RemisionDetalles.DeleteAsync(detalle.Id, ct);
            }
        }
    }

    // ---- Traslado de lotes -------------------------------------------------------------------------------------------

    public async Task<TrasladoResultado> RegistrarTrasladoLoteAsync(TrasladoLoteDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is null, "Este traslado ya está registrado; use la edición para modificarlo.");
        ValidarTrasladoLote(dto);

        await using var uow = await fabrica.BeginAsync(ct);
        await ObtenerGranjaActivaAsync(uow, dto.GranjaOrigenId, "La granja de origen", ct);
        await ObtenerGranjaActivaAsync(uow, dto.GranjaDestinoId, "La granja de destino", ct);
        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, null, ct);

        var remision = new Remision { TipoMovimiento = TipoRemision.Traslado, ClaseSalida = ClaseSalida.Traslado };
        EncabezadoDeTrasladoLote(remision, dto, numero);
        await uow.Remisiones.InsertAsync(remision, ct);
        var valor = await AplicarLineasDeTrasladoAsync(uow, remision, dto, ct);

        await uow.CommitAsync(ct);
        return new TrasladoResultado(remision.Id, dto.Lotes.Count, valor);
    }

    public async Task<TrasladoResultado> EditarTrasladoLoteAsync(TrasladoLoteDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is not null, "Falta indicar el traslado que se va a editar.");
        ValidarTrasladoLote(dto);

        await using var uow = await fabrica.BeginAsync(ct);
        var remision = await CargarDocumentoDeLotesAsync(uow, dto.RemisionId!.Value, TipoRemision.Traslado, ct);
        ExigirMismaGranjaDeOrigen(dto.GranjaOrigenId, remision);
        await ObtenerGranjaActivaAsync(uow, dto.GranjaDestinoId, "La granja de destino", ct);
        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, remision.Id, ct);

        await RevertirLineasDeTrasladoAsync(uow, remision, eliminarLineas: true, ct);
        EncabezadoDeTrasladoLote(remision, dto, numero);
        await uow.Remisiones.UpdateAsync(remision, ct);
        var valor = await AplicarLineasDeTrasladoAsync(uow, remision, dto, ct);

        await uow.CommitAsync(ct);
        return new TrasladoResultado(remision.Id, dto.Lotes.Count, valor);
    }

    private void ValidarTrasladoLote(TrasladoLoteDto dto)
    {
        Regla.ExigirTexto(dto.NumeroRemision, "El número de remisión");
        Regla.Exigir(dto.FechaTraslado != default && dto.FechaTraslado.Date <= reloj.Hoy, "La fecha del traslado es obligatoria y no puede ser futura.");
        Regla.Exigir(dto.GranjaOrigenId > 0, "Seleccione la granja de origen.");
        Regla.Exigir(dto.GranjaDestinoId > 0, "Seleccione la granja de destino.");
        Regla.Exigir(dto.GranjaOrigenId != dto.GranjaDestinoId, "La granja de origen y la de destino deben ser distintas.");
        Regla.Exigir(dto.Lotes.Count > 0, "Agregue al menos un lote al traslado.");
        Regla.Exigir(dto.Lotes.Select(l => l.LoteId).Distinct().Count() == dto.Lotes.Count, "Hay lotes repetidos en la lista.");
    }

    private void EncabezadoDeTrasladoLote(Remision remision, TrasladoLoteDto dto, string numero)
    {
        remision.NumeroRemision = numero;
        remision.GranjaOrigenId = dto.GranjaOrigenId;
        remision.GranjaDestinoId = dto.GranjaDestinoId;
        remision.FechaRemision = dto.FechaTraslado.Date;
        remision.Observaciones = LimpiarONulo(dto.Observaciones);
        validador.ValidarJerarquiaDocumental(remision);
    }

    private async Task<decimal> AplicarLineasDeTrasladoAsync(IUnitOfWork uow, Remision remision, TrasladoLoteDto dto, CancellationToken ct)
    {
        var total = 0m;
        foreach (var linea in dto.Lotes)
        {
            var lote = await ObtenerLoteParaSalidaAsync(uow, linea.LoteId, dto.GranjaOrigenId, null, "trasladar", ct);
            var completo = !lote.ControlNumericoPoblacion || linea.Cantidad == lote.CantidadActual;
            var cabezas = lote.ControlNumericoPoblacion ? linea.Cantidad : 0;
            if (lote.ControlNumericoPoblacion)
            {
                Regla.Exigir(cabezas > 0, $"Indique las cabezas a trasladar del lote {lote.CodigoLote}.");
                Regla.Exigir(cabezas <= lote.CantidadActual, $"El lote {lote.CodigoLote} solo tiene {lote.CantidadActual:N0} cabezas; no se pueden trasladar {cabezas:N0}.");
            }

            var costo = lote.ControlNumericoPoblacion ? cabezas * lote.ValorContableUnitario : lote.ValorTotalContable;
            total += costo;
            var detalle = new RemisionDetalle
            {
                RemisionId = remision.Id,
                LoteId = lote.Id,
                Cantidad = cabezas,
                CostoSistemaLibros = costo,
                ObservacionesEspecificas = LimpiarONulo(linea.ObservacionesEspecificas),
            };

            if (completo)
            {
                // Todo el lote cambia de granja (conserva su código); se recuerda su ubicación para poder revertirlo.
                detalle.ObservacionesEspecificas = PrefijoUbicacionOrigen + lote.Ubicacion;
                detalle.LoteDestinoId = lote.Id;
                lote.GranjaId = dto.GranjaDestinoId;
                if (!string.IsNullOrWhiteSpace(linea.UbicacionDestino))
                {
                    lote.Ubicacion = linea.UbicacionDestino.Trim();
                }
            }
            else
            {
                var codigo = Limpiar(linea.CodigoNuevoLote);
                Regla.ExigirTexto(codigo, $"El código del lote que se crea en el destino con parte del lote {lote.CodigoLote}");
                Regla.Exigir(!await uow.Lotes.ExisteCodigoAsync(codigo, ct), $"Ya existe un lote con el código {codigo}.");

                var nuevo = new LotePoblacion
                {
                    CodigoLote = codigo,
                    ProgramaId = lote.ProgramaId,
                    GranjaId = dto.GranjaDestinoId,
                    EtapaId = lote.EtapaId,
                    ClaseEntrada = ClaseEntrada.Traslado,
                    Ubicacion = string.IsNullOrWhiteSpace(linea.UbicacionDestino) ? lote.Ubicacion : linea.UbicacionDestino.Trim(),
                    LineaRaza = lote.LineaRaza,
                    OrigenReferencia = lote.CodigoLote,
                    FechaNacimientoIngreso = lote.FechaNacimientoIngreso,
                    ControlNumericoPoblacion = true,
                    CantidadInicial = cabezas,
                    CantidadActual = cabezas,
                    ValorCompraUnitario = lote.ValorCompraUnitario,
                    ValorContableUnitario = lote.ValorContableUnitario,
                    Observaciones = AnotarLote(null, $"Traslado {remision.NumeroRemision} desde el lote {lote.CodigoLote} ({cabezas:N0} cabezas)"),
                };
                nuevo.ValorTotalContable = TotalDelLote(nuevo);
                await uow.Lotes.InsertAsync(nuevo, ct);
                detalle.LoteDestinoId = nuevo.Id;

                lote.CantidadActual -= cabezas;
                lote.CantidadInicial = Math.Max(0, lote.CantidadInicial - cabezas);
                lote.ValorTotalContable = TotalDelLote(lote);
            }

            lote.Operaciones++;
            lote.Observaciones = AnotarLote(lote.Observaciones, $"Traslado {remision.NumeroRemision}: " + (lote.ControlNumericoPoblacion ? $"{cabezas:N0} cabezas" : "lote completo"));
            await uow.Lotes.UpdateAsync(lote, ct);
            await uow.RemisionDetalles.InsertAsync(detalle, ct);
        }

        return total;
    }

    /// <summary>Deshace un traslado de lotes. El lote creado en el destino solo se retira si sigue sin operaciones.</summary>
    private static async Task RevertirLineasDeTrasladoAsync(IUnitOfWork uow, Remision remision, bool eliminarLineas, CancellationToken ct)
    {
        foreach (var detalle in (await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct)).Where(d => d.LoteId is not null).ToList())
        {
            var lote = await uow.Lotes.GetByIdAsync(detalle.LoteId!.Value, ct);
            var destinoId = detalle.LoteDestinoId;
            if (lote is not null && destinoId == lote.Id)
            {
                // Traslado total: vuelve a la granja y a la ubicación de origen.
                lote.GranjaId = remision.GranjaOrigenId!.Value;
                if (detalle.ObservacionesEspecificas?.StartsWith(PrefijoUbicacionOrigen, StringComparison.Ordinal) == true)
                {
                    lote.Ubicacion = detalle.ObservacionesEspecificas[PrefijoUbicacionOrigen.Length..];
                }

                lote.Operaciones = Math.Max(0, lote.Operaciones - 1);
                await uow.Lotes.UpdateAsync(lote, ct);
            }
            else if (lote is not null && destinoId is { } id && await uow.Lotes.GetByIdAsync(id, ct) is { } destino)
            {
                Regla.Exigir(
                    destino.Operaciones == 0 && destino.CantidadActual == detalle.Cantidad && destino.MortandadAcumulada == 0,
                    $"El lote {destino.CodigoLote}, creado por este traslado, ya tuvo operaciones: no se puede editar ni anular el traslado.");

                lote.CantidadActual += detalle.Cantidad;
                lote.CantidadInicial += detalle.Cantidad;
                lote.ValorTotalContable = TotalDelLote(lote);
                lote.Operaciones = Math.Max(0, lote.Operaciones - 1);
                await uow.Lotes.UpdateAsync(lote, ct);

                detalle.LoteDestinoId = null;
                await uow.RemisionDetalles.UpdateAsync(detalle, ct);
                await uow.Lotes.DeleteAsync(destino.Id, ct);
            }

            if (eliminarLineas)
            {
                await uow.RemisionDetalles.DeleteAsync(detalle.Id, ct);
            }
            else
            {
                detalle.LoteDestinoId = null;
                await uow.RemisionDetalles.UpdateAsync(detalle, ct);
            }
        }
    }

    // ---- Anular y comunes --------------------------------------------------------------------------------------------

    /// <summary>Anula la venta o el traslado si el documento es de lotes. Devuelve false si no lo es (sigue el flujo de animales).</summary>
    private async Task<bool> AnularDocumentoDeLotesAsync(IUnitOfWork uow, int remisionId, string? motivo, CancellationToken ct)
    {
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct);
        if (remision is null || !(await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct)).Any(d => d.LoteId is not null))
        {
            return false;
        }

        Regla.Exigir(!remision.Anulada, $"El documento {remision.NumeroRemision} ya está anulado.");
        switch (remision.TipoMovimiento)
        {
            case TipoRemision.Venta:
                await RevertirLineasDeVentaAsync(uow, remision, eliminarLineas: false, ct);
                break;
            case TipoRemision.Traslado:
                await RevertirLineasDeTrasladoAsync(uow, remision, eliminarLineas: false, ct);
                break;
            default:
                throw Regla.Violada($"No se puede anular un documento de lotes de tipo {remision.TipoMovimiento}.");
        }

        var nota = $"ANULADA el {reloj.Hoy:dd/MM/yyyy}" + (string.IsNullOrWhiteSpace(motivo) ? string.Empty : $": {motivo.Trim()}");
        remision.Anulada = true;
        remision.Observaciones = string.IsNullOrWhiteSpace(remision.Observaciones) ? nota : $"{nota} | {remision.Observaciones}";
        await uow.Remisiones.UpdateAsync(remision, ct);
        return true;
    }

    private static async Task<Remision> CargarDocumentoDeLotesAsync(IUnitOfWork uow, int remisionId, string tipo, CancellationToken ct)
    {
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct) ?? throw Regla.Violada("El documento no existe.");
        Regla.Exigir(!remision.Anulada, $"El documento {remision.NumeroRemision} ya está anulado.");
        Regla.Exigir(remision.TipoMovimiento == tipo, $"El documento {remision.NumeroRemision} no es de tipo {tipo}.");
        Regla.Exigir(
            (await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct)).Any(d => d.LoteId is not null),
            $"El documento {remision.NumeroRemision} no es un documento de lotes.");
        return remision;
    }

    private static async Task<LotePoblacion> ObtenerLoteParaSalidaAsync(
        IUnitOfWork uow, int loteId, int granjaOrigenId, int? programaId, string accion, CancellationToken ct)
    {
        var lote = await uow.Lotes.GetByIdAsync(loteId, ct) ?? throw Regla.Violada($"El lote con id {loteId} no existe.");
        Regla.Exigir(!lote.Anulado && lote.Estado == EstadoLote.Activo, $"El lote {lote.CodigoLote} no está activo; no se puede {accion}.");
        Regla.Exigir(lote.GranjaId == granjaOrigenId, $"El lote {lote.CodigoLote} no se encuentra en la granja de origen seleccionada.");
        Regla.Exigir(programaId is null || lote.ProgramaId == programaId, $"El lote {lote.CodigoLote} no pertenece al programa del oficio.");
        return lote;
    }

    private static async Task<SalidaResultado> ResumirSalidaDeLotesAsync(IUnitOfWork uow, int remisionId, int? oficioId, CancellationToken ct)
    {
        var detalles = await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct);
        return new SalidaResultado(
            remisionId, oficioId, detalles.Sum(d => d.Cantidad), detalles.Sum(d => d.CostoSistemaLibros), detalles.Sum(d => d.ValorTotalTransaccion ?? 0m));
    }

    private static decimal TotalDelLote(LotePoblacion lote)
        => lote.ControlNumericoPoblacion ? lote.CantidadActual * lote.ValorContableUnitario : lote.ValorContableUnitario;

    private string AnotarLote(string? actual, string nota)
    {
        var linea = $"[{reloj.Hoy:dd/MM/yyyy}] {nota}";
        return string.IsNullOrWhiteSpace(actual) ? linea : actual + Environment.NewLine + linea;
    }
}