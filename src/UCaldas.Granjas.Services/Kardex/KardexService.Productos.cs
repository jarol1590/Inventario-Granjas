using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Kardex;

/// <summary>
/// Remisiones de productos (v2.8): hortalizas, frutales, huevos, leche... Son documentos con su oficio (ventas) y su Recibo de
/// Caja SP, pero no mueven inventario: no hay animales ni lotes y por eso no escriben en el libro del Kardex.
/// </summary>
public sealed partial class KardexService
{
    public async Task<ProductosResultado> RegistrarRemisionProductosAsync(RemisionProductosDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is null, "Esta remisión ya está registrada; use la edición para modificarla.");
        ValidarRemisionProductos(dto);

        await using var uow = await fabrica.BeginAsync(ct);
        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, null, ct);

        var remision = new Remision();
        await PrepararRemisionProductosAsync(uow, remision, dto, numero, ct);
        await uow.Remisiones.InsertAsync(remision, ct);
        await AplicarLineasDeProductosAsync(uow, remision, dto, ct);

        await GuardarReciboAsync(uow, remision, dto.ProgramaId, dto.Comercial, dto.Recibo, ct);
        var lineas = (await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct)).Count;
        await uow.CommitAsync(ct);
        return new ProductosResultado(remision.Id, lineas, remision.Total);
    }

    public async Task<ProductosResultado> EditarRemisionProductosAsync(RemisionProductosDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is not null, "Falta indicar la remisión que se va a editar.");
        ValidarRemisionProductos(dto);

        await using var uow = await fabrica.BeginAsync(ct);
        var remision = await uow.Remisiones.GetByIdAsync(dto.RemisionId!.Value, ct) ?? throw Regla.Violada("La remisión no existe.");
        Regla.Exigir(!remision.Anulada, $"La remisión {remision.NumeroRemision} está anulada y ya no se puede editar.");
        Regla.Exigir(
            await EsDocumentoDeProductosAsync(uow, remision, ct),
            $"La remisión {remision.NumeroRemision} no es de productos: edítela desde su pantalla de animales o lotes.");
        Regla.Exigir(remision.TipoMovimiento == dto.Tipo, "No se puede cambiar el tipo de una remisión: anúlela y registre otra.");
        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, remision.Id, ct);

        await uow.RemisionDetalles.EliminarPorRemisionAsync(remision.Id, ct);
        await PrepararRemisionProductosAsync(uow, remision, dto, numero, ct);
        await uow.Remisiones.UpdateAsync(remision, ct);
        await AplicarLineasDeProductosAsync(uow, remision, dto, ct);

        await GuardarReciboAsync(uow, remision, dto.ProgramaId, dto.Comercial, dto.Recibo, ct);
        var lineas = (await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct)).Count;
        await uow.CommitAsync(ct);
        return new ProductosResultado(remision.Id, lineas, remision.Total);
    }

    /// <summary>Anula una remisión de productos: no hay inventario que restaurar. Devuelve false si el documento no es de productos.</summary>
    private async Task<bool> AnularDocumentoDeProductosAsync(IUnitOfWork uow, int remisionId, string? motivo, CancellationToken ct)
    {
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct) ?? throw Regla.Violada("El documento no existe.");
        if (!await EsDocumentoDeProductosAsync(uow, remision, ct))
        {
            return false;
        }

        Regla.Exigir(!remision.Anulada, $"La remisión {remision.NumeroRemision} ya está anulada.");
        var nota = $"ANULADA el {reloj.Hoy:dd/MM/yyyy}" + (string.IsNullOrWhiteSpace(motivo) ? string.Empty : $": {motivo.Trim()}");
        remision.Anulada = true;
        remision.Observaciones = string.IsNullOrWhiteSpace(remision.Observaciones) ? nota : $"{nota} | {remision.Observaciones}";
        await uow.Remisiones.UpdateAsync(remision, ct);
        return true;
    }

    private static async Task<bool> EsDocumentoDeProductosAsync(IUnitOfWork uow, Remision remision, CancellationToken ct)
    {
        var lineas = await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct);
        return EsDeSubproductos(lineas);
    }

    /// <summary>Remisión de subproductos: todas sus líneas son ítems (de catálogo o escritos a mano), ninguna es un animal o un lote.</summary>
    internal static bool EsDeSubproductos(IEnumerable<RemisionDetalle> lineas)
    {
        var lista = lineas.ToList();
        return lista.Count > 0 && lista.All(l => l.AnimalId is null && l.LoteId is null);
    }

    private void ValidarRemisionProductos(RemisionProductosDto dto)
    {
        Regla.Exigir(
            dto.Tipo == TipoRemision.Venta,
            "El módulo Remisiones solo registra ventas de subproductos; los traslados se hacen en Sistema Granjas.");
        Regla.Exigir(dto.ProgramaId > 0, "Seleccione el programa de la remisión.");
        Regla.ExigirTexto(dto.NumeroRemision, "El número de remisión");
        Regla.Exigir(dto.Fecha != default && dto.Fecha.Date <= reloj.Hoy, "La fecha de la remisión es obligatoria y no puede ser futura.");
        Regla.Exigir(dto.GranjaOrigenId > 0, "Seleccione la granja de origen.");
        if (dto.Tipo == TipoRemision.Venta)
        {
            Regla.ExigirTexto(dto.NumeroOficio, "El oficio de venta");
            Regla.ExigirTexto(dto.Comprador, "El comprador");
        }
        else
        {
            Regla.Exigir(dto.GranjaDestinoId is > 0, "Seleccione la granja de destino.");
            Regla.Exigir(dto.GranjaDestinoId != dto.GranjaOrigenId, "La granja de destino debe ser distinta de la de origen.");
        }

        Regla.Exigir(dto.Lineas.Count > 0, "Agregue al menos un producto a la remisión.");
        foreach (var linea in dto.Lineas)
        {
            Regla.Exigir(linea.ProductoId is not null || !string.IsNullOrWhiteSpace(linea.Descripcion), "Escriba la descripción de cada ítem vendido.");
            Regla.ExigirTexto(linea.UnidadMedida, "La unidad de medida de cada ítem");
            Regla.Exigir(linea.Cantidad > 0, "La cantidad de cada ítem debe ser mayor que cero.");
            Regla.Exigir(linea.ValorUnitario >= 0, "El valor unitario no puede ser negativo.");
            Regla.Exigir(dto.Tipo == TipoRemision.Traslado || linea.ValorUnitario > 0, "Cada ítem vendido debe tener un valor unitario mayor que cero.");
        }
    }

    /// <summary>Encabezado de la remisión de productos: oficio (venta) o granja destino (traslado).</summary>
    private async Task PrepararRemisionProductosAsync(IUnitOfWork uow, Remision remision, RemisionProductosDto dto, string numero, CancellationToken ct)
    {
        var programa = await uow.Programas.GetByIdAsync(dto.ProgramaId, ct) ?? throw Regla.Violada("El programa de la remisión no existe.");
        await ObtenerGranjaActivaAsync(uow, dto.GranjaOrigenId, "La granja de origen", ct);

        var oficioActual = remision.OficioId;
        remision.NumeroRemision = numero;
        remision.TipoMovimiento = dto.Tipo;
        remision.GranjaOrigenId = dto.GranjaOrigenId;
        remision.FechaRemision = dto.Fecha.Date;
        remision.Observaciones = LimpiarONulo(dto.Observaciones);
        remision.ProgramaId = programa.Id;

        if (dto.Tipo == TipoRemision.Venta)
        {
            var numeroOficio = Limpiar(dto.NumeroOficio);
            var oficio = await uow.Oficios.GetByNumeroAsync(numeroOficio, ct)
                ?? throw Regla.Violada($"El oficio {numeroOficio} no existe: créelo primero en la sección Oficios.");
            Regla.Exigir(
                oficio.ProgramaId == programa.Id,
                $"El oficio {numeroOficio} pertenece a otro programa: un oficio solo puede tener remisiones de un solo programa.");
            ExigirOficioAbierto(oficio, oficioActual);

            remision.OficioId = oficio.Id;
            remision.ReciboCaja = ReciboParaValidar(remision.ReciboCaja, dto.Recibo);
            remision.GranjaDestinoId = null;
            remision.TerceroEntidad = LimpiarONulo(dto.Comprador);
            remision.ClaseSalida = ClaseSalida.Venta;
        }
        else
        {
            await ObtenerGranjaActivaAsync(uow, dto.GranjaDestinoId!.Value, "La granja de destino", ct);
            remision.OficioId = null;
            remision.ReciboCaja = null;
            remision.GranjaDestinoId = dto.GranjaDestinoId;
            remision.TerceroEntidad = null;
            remision.ClaseSalida = ClaseSalida.Traslado;
        }

        validador.ValidarJerarquiaDocumental(remision);
    }

    private static async Task AplicarLineasDeProductosAsync(IUnitOfWork uow, Remision remision, RemisionProductosDto dto, CancellationToken ct)
    {
        foreach (var linea in dto.Lineas)
        {
            CatalogoProducto? producto = null;
            if (linea.ProductoId is { } productoId)
            {
                producto = await uow.Productos.GetByIdAsync(productoId, ct) ?? throw Regla.Violada("Uno de los productos ya no existe en el catálogo.");
                Regla.Exigir(
                    producto.ProgramaId == dto.ProgramaId,
                    $"El producto {producto.Nombre} no pertenece al programa de la remisión.");
            }

            await uow.RemisionDetalles.InsertAsync(new RemisionDetalle
            {
                RemisionId = remision.Id,
                ProductoId = producto?.Id,
                DescripcionItem = string.IsNullOrWhiteSpace(linea.Descripcion) ? producto!.Nombre : linea.Descripcion.Trim(),
                UnidadMedida = linea.UnidadMedida.Trim(),
                CantidadProducto = linea.Cantidad,
                ValorUnitarioTransaccion = linea.ValorUnitario,
                ValorTotalTransaccion = Math.Round(linea.Cantidad * linea.ValorUnitario, 2, MidpointRounding.AwayFromZero),
                CostoSistemaLibros = 0m,
            }, ct);
        }
    }
}
