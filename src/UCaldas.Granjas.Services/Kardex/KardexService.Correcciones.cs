using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Kardex;

/// <summary>
/// Corrección de documentos ya registrados (editar y anular). Principio común: el libro nunca se descuadra. Un cambio
/// que alteraría el Kardex solo se permite si ningún animal afectado tiene movimientos posteriores a los del propio
/// documento; si los tiene, se rechaza con un mensaje que explica qué hacer.
/// </summary>
public sealed partial class KardexService
{
    private sealed record Documento(Remision Remision, List<RemisionDetalle> Detalles, Dictionary<int, Animal> Animales);

    // ---- Anular -----------------------------------------------------------------------------------------------

    public async Task AnularAsync(int remisionId, string? motivo = null, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);

        var doc = await CargarDocumentoAsync(uow, remisionId, null, ct);
        var remision = doc.Remision;

        switch (remision.TipoMovimiento)
        {
            case TipoRemision.Venta:
            case TipoRemision.Baja:
                foreach (var animal in doc.Animales.Values)
                {
                    await RestaurarAnimalDeSalidaAsync(uow, animal, remision, "anular el documento", ct);
                }

                break;

            case TipoRemision.Traslado:
                foreach (var detalle in doc.Detalles)
                {
                    await RestaurarAnimalDeTrasladoAsync(
                        uow, doc.Animales[detalle.AnimalId!.Value], detalle, remision, "anular el traslado", ct);
                }

                await uow.Movimientos.EliminarPorRemisionAsync(remision.Id, ct);
                await uow.RemisionDetalles.EliminarPorRemisionAsync(remision.Id, ct);
                break;

            case TipoRemision.Compra:
                foreach (var animal in doc.Animales.Values)
                {
                    await ExigirSinMovimientosPosterioresAsync(uow, animal, remision.Id, "anular la compra", ct);
                }

                foreach (var animal in doc.Animales.Values)
                {
                    await EliminarAnimalDeCompraAsync(uow, animal, remision.Id, ct);
                }

                break;

            default:
                throw Regla.Violada($"No se puede anular un documento de tipo {remision.TipoMovimiento}.");
        }

        var nota = $"ANULADA el {reloj.Hoy:dd/MM/yyyy}" + (string.IsNullOrWhiteSpace(motivo) ? string.Empty : $": {motivo.Trim()}");
        remision.Anulada = true;
        remision.Observaciones = string.IsNullOrWhiteSpace(remision.Observaciones) ? nota : $"{nota} | {remision.Observaciones}";
        await uow.Remisiones.UpdateAsync(remision, ct);

        await uow.CommitAsync(ct);
    }

    // ---- Editar compra ----------------------------------------------------------------------------------------

    public async Task<CompraResultado> EditarCompraAsync(CompraDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is not null, "Falta indicar la compra que se va a editar.");
        validador.ValidarCompra(dto, reloj.Hoy);
        var conservadosDto = IndexarPorId(dto.Animales.Where(a => a.AnimalId is not null), a => a.AnimalId!.Value);

        await using var uow = await fabrica.BeginAsync(ct);

        var doc = await CargarDocumentoAsync(uow, dto.RemisionId!.Value, TipoRemision.Compra, ct);
        var remision = doc.Remision;

        var numero = Limpiar(dto.NumeroFactura);
        await ExigirNumeroLibreAsync(uow, numero, remision.Id, ct);
        await ObtenerGranjaActivaAsync(uow, dto.GranjaDestinoId, "La granja destino", ct);

        foreach (var item in dto.Animales.Where(a => a.AnimalId is not null))
        {
            Regla.Exigir(doc.Animales.ContainsKey(item.AnimalId!.Value), $"El animal {item.ChapetaAmarilla} no pertenece a esta compra.");
        }

        var fecha = dto.FechaCompra.Date;
        var cambioGranja = dto.GranjaDestinoId != remision.GranjaDestinoId;
        var valorador = await aging.CargarValoradorAsync(uow, ct);
        var sinTarifa = new List<string>();

        // 1) Animales quitados de la compra: se eliminan por completo (solo si nunca tuvieron otro movimiento).
        foreach (var id in doc.Animales.Keys.Except(conservadosDto.Keys).ToList())
        {
            var animal = doc.Animales[id];
            await ExigirSinMovimientosPosterioresAsync(uow, animal, remision.Id, "quitarlo de la compra", ct);
            await EliminarAnimalDeCompraAsync(uow, animal, remision.Id, ct);
        }

        // 2) Animales que se conservan.
        var detalles = (await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct)).ToDictionary(d => d.AnimalId!.Value);
        var asientos = (await uow.Movimientos.GetByRemisionAsync(remision.Id, ct)).ToDictionary(m => m.AnimalId!.Value);
        foreach (var (id, item) in conservadosDto)
        {
            var animal = doc.Animales[id];
            var chapeta = Limpiar(item.ChapetaAmarilla);
            if (!chapeta.Equals(animal.ChapetaAmarilla, StringComparison.OrdinalIgnoreCase))
            {
                await ExigirChapetaLibreAsync(uow, chapeta, animal.Id, ct);
            }

            await ExigirOrientacionAsync(uow, item.OrientacionId, ct);

            // Cambios que alteran la valoración inicial del animal (y por tanto el Kardex de la compra).
            var estructural = animal.Sexo != item.Sexo
                              || animal.FechaNacimiento.Date != item.FechaNacimiento.Date
                              || animal.OrientacionId != item.OrientacionId
                              || animal.NumeroPartos != item.NumeroPartos
                              || cambioGranja;
            if (estructural)
            {
                var posteriores = await MovimientosPosterioresAsync(uow, animal.Id, remision.Id, ct);
                Regla.Exigir(
                    posteriores.Count == 0,
                    $"El animal {animal.ChapetaAmarilla} ya tiene movimientos posteriores a esta compra ({Resumir(posteriores)}): " +
                    "no se pueden cambiar aquí su sexo, fecha de nacimiento, orientación, partos ni la granja destino. " +
                    "Corríjalos desde la ficha del bovino; el valor se ajustará en el próximo cierre de valorización.");
            }

            animal.ChapetaAmarilla = chapeta;
            animal.ChapetaRoja = LimpiarONulo(item.ChapetaRoja);
            animal.Nombre = LimpiarONulo(item.Nombre);
            animal.Sexo = item.Sexo;
            animal.FechaNacimiento = item.FechaNacimiento.Date;
            animal.FechaCompra = fecha;
            animal.Raza = Limpiar(item.Raza);
            animal.PesoActualKg = item.PesoKg;
            animal.OrientacionId = item.OrientacionId;
            animal.NumeroPartos = item.NumeroPartos;
            animal.GranjaId = dto.GranjaDestinoId;

            var asiento = asientos[id];
            var detalle = detalles[id];
            if (estructural)
            {
                var valoracion = valorador.Determinar(animal, reloj.Hoy);
                animal.CategoriaId = valoracion.CategoriaId;
                animal.ValorContableActual = valoracion.Valor ?? item.PrecioFactura;
                if (valoracion.SinTarifa)
                {
                    sinTarifa.Add(chapeta);
                }

                asiento.CostoAsociado = animal.ValorContableActual;
                detalle.CostoSistemaLibros = animal.ValorContableActual;
            }

            await uow.Animales.UpdateAsync(animal, ct);
            if (item.PrecioFactura != animal.ValorCompraOrigen)
            {
                // El costo de origen solo se corrige por esta vía: es la factura de la compra que se está editando.
                await uow.Animales.ActualizarValorCompraOrigenAsync(animal.Id, item.PrecioFactura, ct);
                animal.ValorCompraOrigen = item.PrecioFactura;
            }

            detalle.PesoMomentoKg = item.PesoKg;
            detalle.ValorUnitarioTransaccion = item.PrecioFactura;
            detalle.ValorTotalTransaccion = item.PrecioFactura;
            detalle.ObservacionesEspecificas = LimpiarONulo(item.Observaciones);
            await uow.RemisionDetalles.UpdateAsync(detalle, ct);

            asiento.FechaMovimiento = fecha;
            asiento.GranjaDestinoId = dto.GranjaDestinoId;
            await uow.Movimientos.UpdateAsync(asiento, ct);
        }

        // 3) Animales agregados al editar.
        foreach (var item in dto.Animales.Where(a => a.AnimalId is null))
        {
            await ExigirOrientacionAsync(uow, item.OrientacionId, ct);
            await ExigirChapetaLibreAsync(uow, Limpiar(item.ChapetaAmarilla), null, ct);
        }

        // 4) Encabezado.
        remision.NumeroRemision = numero;
        remision.GranjaDestinoId = dto.GranjaDestinoId;
        remision.FechaRemision = fecha;
        remision.TerceroEntidad = Limpiar(dto.Proveedor);
        remision.TerceroNit = LimpiarONulo(dto.ProveedorNit);
        remision.OrdenCompra = LimpiarONulo(dto.OrdenCompra);
        remision.Observaciones = LimpiarONulo(dto.Observaciones);
        validador.ValidarJerarquiaDocumental(remision);
        await uow.Remisiones.UpdateAsync(remision, ct);

        foreach (var item in dto.Animales.Where(a => a.AnimalId is null))
        {
            await AltaAnimalCompradoAsync(uow, valorador, remision, item, sinTarifa, ct);
        }

        var finales = await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct);
        var resultado = new CompraResultado(
            remision.Id,
            finales.Select(d => d.AnimalId!.Value).ToList(),
            finales.Sum(d => d.ValorTotalTransaccion ?? 0m),
            finales.Sum(d => d.CostoSistemaLibros),
            sinTarifa);

        await uow.CommitAsync(ct);
        return resultado;
    }

    // ---- Editar venta -----------------------------------------------------------------------------------------

    public async Task<SalidaResultado> EditarVentaAsync(VentaDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is not null, "Falta indicar la venta que se va a editar.");
        validador.ValidarVenta(dto, reloj.Hoy);
        var finales = IndexarPorId(dto.Animales, a => a.AnimalId);

        await using var uow = await fabrica.BeginAsync(ct);

        var doc = await CargarDocumentoAsync(uow, dto.RemisionId!.Value, TipoRemision.Venta, ct);
        var remision = doc.Remision;
        ExigirMismaGranjaDeOrigen(dto.GranjaOrigenId, remision);

        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, remision.Id, ct);

        var nuevosIds = finales.Keys.Except(doc.Animales.Keys).ToList();
        var animalesNuevos = nuevosIds.Count == 0
            ? []
            : await CargarAnimalesDisponiblesAsync(uow, nuevosIds, remision.GranjaOrigenId!.Value, "vender", ct);
        var todos = doc.Animales.Values.Where(a => finales.ContainsKey(a.Id)).Concat(animalesNuevos).ToList();
        var oficio = await ObtenerOCrearOficioAsync(uow, dto, todos, ct);

        foreach (var id in doc.Animales.Keys.Except(finales.Keys).ToList())
        {
            await RestaurarAnimalDeSalidaAsync(uow, doc.Animales[id], remision, "quitarlo de la venta", ct);
        }

        remision.NumeroRemision = numero;
        remision.OficioId = oficio.Id;
        remision.ReciboCaja = Limpiar(dto.ReciboCaja);
        remision.FechaRemision = dto.FechaRemision.Date;
        remision.TerceroEntidad = Limpiar(dto.Comprador);
        remision.Observaciones = LimpiarONulo(dto.Observaciones);
        validador.ValidarJerarquiaDocumental(remision);
        await uow.Remisiones.UpdateAsync(remision, ct);

        var detalles = (await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct)).ToDictionary(d => d.AnimalId!.Value);
        var asientos = (await uow.Movimientos.GetByRemisionAsync(remision.Id, ct)).ToDictionary(m => m.AnimalId!.Value);
        foreach (var (id, item) in finales.Where(f => doc.Animales.ContainsKey(f.Key)))
        {
            var detalle = detalles[id];
            detalle.ValorUnitarioTransaccion = item.PrecioVenta;
            detalle.ValorTotalTransaccion = item.PrecioVenta;
            detalle.PesoMomentoKg = item.PesoKg ?? detalle.PesoMomentoKg;
            detalle.ObservacionesEspecificas = LimpiarONulo(item.ObservacionesEspecificas);
            await uow.RemisionDetalles.UpdateAsync(detalle, ct);

            await SincronizarFechaDelAsientoAsync(uow, asientos[id], remision.FechaRemision, ct);
        }

        foreach (var animal in animalesNuevos)
        {
            var item = finales[animal.Id];
            await AplicarSalidaAsync(
                uow, remision, animal, EstadoAnimal.Vendido, TipoMovimiento.Venta, item.PrecioVenta, item.PesoKg, item.ObservacionesEspecificas, ct);
        }

        var resultado = await ResumirSalidaAsync(uow, remision.Id, oficio.Id, ct);
        await uow.CommitAsync(ct);
        return resultado;
    }

    // ---- Editar baja ------------------------------------------------------------------------------------------

    public async Task<SalidaResultado> EditarBajaAsync(BajaDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is not null, "Falta indicar la baja que se va a editar.");
        validador.ValidarBaja(dto, reloj.Hoy);
        var finales = IndexarPorId(dto.Animales, a => a.AnimalId);

        await using var uow = await fabrica.BeginAsync(ct);

        var doc = await CargarDocumentoAsync(uow, dto.RemisionId!.Value, TipoRemision.Baja, ct);
        var remision = doc.Remision;
        ExigirMismaGranjaDeOrigen(dto.GranjaOrigenId, remision);

        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, remision.Id, ct);

        var nuevosIds = finales.Keys.Except(doc.Animales.Keys).ToList();
        var animalesNuevos = nuevosIds.Count == 0
            ? []
            : await CargarAnimalesDisponiblesAsync(uow, nuevosIds, remision.GranjaOrigenId!.Value, "dar de baja", ct);

        foreach (var id in doc.Animales.Keys.Except(finales.Keys).ToList())
        {
            await RestaurarAnimalDeSalidaAsync(uow, doc.Animales[id], remision, "quitarlo de la baja", ct);
        }

        remision.NumeroRemision = numero;
        remision.FechaRemision = dto.FechaBaja.Date;
        remision.TerceroEntidad = Limpiar(dto.DocumentoSoporte);
        remision.Observaciones = LimpiarONulo(dto.Observaciones);
        remision.ClaseSalida = dto.ClaseSalida;
        validador.ValidarJerarquiaDocumental(remision);
        await uow.Remisiones.UpdateAsync(remision, ct);

        // Si cambia la clase (p. ej. de muerte a hurto) los animales conservados cambian de estado y de tipo de asiento.
        var estado = EstadoDeBaja(dto.ClaseSalida);
        var tipo = MovimientoDeBaja(dto.ClaseSalida);
        var detalles = (await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct)).ToDictionary(d => d.AnimalId!.Value);
        var asientos = (await uow.Movimientos.GetByRemisionAsync(remision.Id, ct)).ToDictionary(m => m.AnimalId!.Value);
        foreach (var (id, item) in finales.Where(f => doc.Animales.ContainsKey(f.Key)))
        {
            var animal = doc.Animales[id];
            if (animal.Estado != estado)
            {
                animal.Estado = estado;
                await uow.Animales.UpdateAsync(animal, ct);
            }

            var detalle = detalles[id];
            detalle.ObservacionesEspecificas = LimpiarONulo(item.ObservacionesEspecificas);
            await uow.RemisionDetalles.UpdateAsync(detalle, ct);

            var asiento = asientos[id];
            asiento.TipoMovimiento = tipo;
            asiento.FechaMovimiento = remision.FechaRemision.Date;
            await uow.Movimientos.UpdateAsync(asiento, ct);
        }

        foreach (var animal in animalesNuevos)
        {
            await AplicarSalidaAsync(uow, remision, animal, estado, tipo, null, null, finales[animal.Id].ObservacionesEspecificas, ct);
        }

        var resultado = await ResumirSalidaAsync(uow, remision.Id, null, ct);
        await uow.CommitAsync(ct);
        return resultado;
    }

    // ---- Editar traslado --------------------------------------------------------------------------------------

    public async Task<TrasladoResultado> EditarTrasladoAsync(TrasladoDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is not null, "Falta indicar el traslado que se va a editar.");
        validador.ValidarTraslado(dto, reloj.Hoy);
        var finales = IndexarPorId(dto.Animales, a => a.AnimalId);

        await using var uow = await fabrica.BeginAsync(ct);

        var doc = await CargarDocumentoAsync(uow, dto.RemisionId!.Value, TipoRemision.Traslado, ct);
        var remision = doc.Remision;
        ExigirMismaGranjaDeOrigen(dto.GranjaOrigenId, remision);

        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, remision.Id, ct);

        var cambioDestino = dto.GranjaDestinoId != remision.GranjaDestinoId;
        var origen = await uow.Granjas.GetByIdAsync(remision.GranjaOrigenId!.Value, ct)
            ?? throw Regla.Violada("La granja de origen no existe.");
        var destino = cambioDestino
            ? await ObtenerGranjaActivaAsync(uow, dto.GranjaDestinoId, "La granja de destino", ct)
            : await uow.Granjas.GetByIdAsync(dto.GranjaDestinoId, ct) ?? throw Regla.Violada("La granja de destino no existe.");

        var nuevosIds = finales.Keys.Except(doc.Animales.Keys).ToList();
        if (nuevosIds.Count > 0)
        {
            Regla.Exigir(origen.Activa, $"La granja de origen ({origen.Nombre}) está inactiva.");
        }

        var animalesNuevos = nuevosIds.Count == 0
            ? []
            : await CargarAnimalesDisponiblesAsync(uow, nuevosIds, origen.Id, "trasladar", ct);
        foreach (var item in dto.Animales.Where(a => a.OrientacionDestinoId is not null))
        {
            await ExigirOrientacionAsync(uow, item.OrientacionDestinoId!.Value, ct);
        }

        var fecha = dto.FechaTraslado.Date;
        var detalles = doc.Detalles.Where(d => d.AnimalId is not null).ToDictionary(d => d.AnimalId!.Value);
        var asientos = (await uow.Movimientos.GetByRemisionAsync(remision.Id, ct))
            .Where(m => m.AnimalId is not null)
            .ToLookup(m => m.AnimalId!.Value);

        // 1) Animales quitados del traslado: regresan a la sede y a la orientación de origen, y desaparecen sus asientos.
        foreach (var id in doc.Animales.Keys.Except(finales.Keys).ToList())
        {
            await RestaurarAnimalDeTrasladoAsync(uow, doc.Animales[id], detalles[id], remision, "quitarlo del traslado", ct);
            foreach (var asiento in asientos[id])
            {
                await uow.Movimientos.DeleteAsync(asiento.Id, ct);
            }

            await uow.RemisionDetalles.DeleteAsync(detalles[id].Id, ct);
        }

        // 2) Animales que se conservan.
        foreach (var (id, item) in finales.Where(f => doc.Animales.ContainsKey(f.Key)))
        {
            var animal = doc.Animales[id];
            var detalle = detalles[id];
            var orientacionDestino = item.OrientacionDestinoId ?? detalle.OrientacionDestinoId ?? animal.OrientacionId;

            // Cambiar la sede destino o la orientación toca lo que quedó asentado: solo si nada ocurrió después.
            if (cambioDestino || orientacionDestino != detalle.OrientacionDestinoId)
            {
                await ExigirSinMovimientosPosterioresAsync(
                    uow, animal, remision.Id, "cambiar el destino o la orientación de este animal en el traslado", ct);
                animal.GranjaId = destino.Id;
                animal.OrientacionId = orientacionDestino;
                await uow.Animales.UpdateAsync(animal, ct);
                detalle.OrientacionDestinoId = orientacionDestino;
            }

            detalle.ObservacionesEspecificas = LimpiarONulo(item.ObservacionesEspecificas);
            await uow.RemisionDetalles.UpdateAsync(detalle, ct);

            foreach (var asiento in asientos[id])
            {
                var destinoDelAsiento = asiento.GranjaDestinoId is not null ? destino.Id : asiento.GranjaDestinoId;
                if (asiento.FechaMovimiento.Date != fecha || asiento.GranjaDestinoId != destinoDelAsiento)
                {
                    asiento.FechaMovimiento = fecha;
                    asiento.GranjaDestinoId = destinoDelAsiento;
                    await uow.Movimientos.UpdateAsync(asiento, ct);
                }
            }
        }

        // 3) Encabezado.
        remision.NumeroRemision = numero;
        remision.GranjaDestinoId = destino.Id;
        remision.FechaRemision = fecha;
        remision.Observaciones = LimpiarONulo(dto.Observaciones);
        validador.ValidarJerarquiaDocumental(remision);
        await uow.Remisiones.UpdateAsync(remision, ct);

        // 4) Animales agregados al editar.
        foreach (var animal in animalesNuevos)
        {
            await AplicarTrasladoAsync(uow, remision, animal, origen, destino, finales[animal.Id], ct);
        }

        var lineas = await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct);
        var resultado = new TrasladoResultado(remision.Id, lineas.Count, lineas.Sum(d => d.CostoSistemaLibros));
        await uow.CommitAsync(ct);
        return resultado;
    }

    // ---- Comunes de las correcciones -----------------------------------------------------------------------------

    /// <summary>
    /// Deshace el traslado de un animal: regresa a la sede y a la orientación de origen. Exige que no tenga movimientos
    /// posteriores; sus asientos y su línea de la remisión los borra quien llama.
    /// </summary>
    private static async Task RestaurarAnimalDeTrasladoAsync(
        IUnitOfWork uow, Animal animal, RemisionDetalle detalle, Remision remision, string accion, CancellationToken ct)
    {
        await ExigirSinMovimientosPosterioresAsync(uow, animal, remision.Id, accion, ct);

        animal.GranjaId = remision.GranjaOrigenId!.Value;
        animal.OrientacionId = detalle.OrientacionOrigenId ?? animal.OrientacionId;
        await uow.Animales.UpdateAsync(animal, ct);
    }

    private static async Task<Documento> CargarDocumentoAsync(IUnitOfWork uow, int remisionId, string? tipoEsperado, CancellationToken ct)
    {
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct) ?? throw Regla.Violada("El documento no existe.");
        Regla.Exigir(!remision.Anulada, $"El documento {remision.NumeroRemision} ya está anulado.");
        if (tipoEsperado is not null)
        {
            Regla.Exigir(
                remision.TipoMovimiento == tipoEsperado,
                $"El documento {remision.NumeroRemision} no es de tipo {tipoEsperado}.");
        }

        var detalles = (await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct)).ToList();
        var animales = (await uow.Animales.GetByIdsAsync(
            detalles.Where(d => d.AnimalId is not null).Select(d => d.AnimalId!.Value), ct)).ToDictionary(a => a.Id);

        return new Documento(remision, detalles, animales);
    }

    /// <summary>Movimientos del animal registrados después de los de este documento (se comparan por orden de creación).</summary>
    private static async Task<IReadOnlyList<MovimientoInventario>> MovimientosPosterioresAsync(
        IUnitOfWork uow, int animalId, int remisionId, CancellationToken ct)
    {
        var movimientos = await uow.Movimientos.GetByAnimalAsync(animalId, ct);
        var ultimoPropio = movimientos.Where(m => m.RemisionId == remisionId).Select(m => m.Id).DefaultIfEmpty(0).Max();
        return movimientos.Where(m => m.Id > ultimoPropio && m.RemisionId != remisionId).ToList();
    }

    private static string Resumir(IEnumerable<MovimientoInventario> movimientos)
        => string.Join(", ", movimientos.Select(m => m.TipoMovimiento).Distinct());

    private static async Task ExigirSinMovimientosPosterioresAsync(
        IUnitOfWork uow, Animal animal, int remisionId, string accion, CancellationToken ct)
    {
        var posteriores = await MovimientosPosterioresAsync(uow, animal.Id, remisionId, ct);
        Regla.Exigir(
            posteriores.Count == 0,
            $"El animal {animal.ChapetaAmarilla} ya tiene movimientos posteriores a este documento ({Resumir(posteriores)}); no se puede {accion}.");
    }

    /// <summary>Deshace la salida de un animal: vuelve a Disponible y se borran su línea y su asiento de esta remisión.</summary>
    private static async Task RestaurarAnimalDeSalidaAsync(
        IUnitOfWork uow, Animal animal, Remision remision, string accion, CancellationToken ct)
    {
        await ExigirSinMovimientosPosterioresAsync(uow, animal, remision.Id, accion, ct);

        animal.Estado = EstadoAnimal.Disponible;
        await uow.Animales.UpdateAsync(animal, ct);

        foreach (var asiento in (await uow.Movimientos.GetByRemisionAsync(remision.Id, ct)).Where(m => m.AnimalId == animal.Id))
        {
            await uow.Movimientos.DeleteAsync(asiento.Id, ct);
        }

        foreach (var detalle in (await uow.RemisionDetalles.GetByRemisionAsync(remision.Id, ct)).Where(d => d.AnimalId == animal.Id))
        {
            await uow.RemisionDetalles.DeleteAsync(detalle.Id, ct);
        }
    }

    /// <summary>Elimina por completo un animal creado por una compra, con todo lo que cuelga de él.</summary>
    private static async Task EliminarAnimalDeCompraAsync(IUnitOfWork uow, Animal animal, int remisionId, CancellationToken ct)
    {
        Regla.Exigir(
            !await uow.Genealogias.EsPadreAsync(animal.Id, ct),
            $"El animal {animal.ChapetaAmarilla} figura como padre de otro animal; no se puede eliminar.");

        await uow.Movimientos.EliminarPorAnimalAsync(animal.Id, ct);
        foreach (var detalle in await uow.RemisionDetalles.GetByAnimalAsync(animal.Id, ct))
        {
            await uow.RemisionDetalles.DeleteAsync(detalle.Id, ct);
        }

        await uow.Genealogias.EliminarPorAnimalAsync(animal.Id, ct);
        await uow.Animales.DeleteAsync(animal.Id, ct);
    }

    private static void ExigirMismaGranjaDeOrigen(int granjaOrigenId, Remision remision)
        => Regla.Exigir(
            granjaOrigenId == remision.GranjaOrigenId,
            "No se puede cambiar la granja de origen de un documento registrado; anule el documento y regístrelo de nuevo.");

    private static async Task SincronizarFechaDelAsientoAsync(IUnitOfWork uow, MovimientoInventario asiento, DateTime fecha, CancellationToken ct)
    {
        if (asiento.FechaMovimiento.Date != fecha.Date)
        {
            asiento.FechaMovimiento = fecha.Date;
            await uow.Movimientos.UpdateAsync(asiento, ct);
        }
    }

    private static async Task<SalidaResultado> ResumirSalidaAsync(IUnitOfWork uow, int remisionId, int? oficioId, CancellationToken ct)
    {
        var detalles = await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct);
        return new SalidaResultado(
            remisionId,
            oficioId,
            detalles.Count,
            detalles.Sum(d => d.CostoSistemaLibros),
            detalles.Sum(d => d.ValorTotalTransaccion ?? 0m));
    }

    private static Dictionary<int, T> IndexarPorId<T>(IEnumerable<T> items, Func<T, int> id)
    {
        var lista = items.ToList();
        var indice = new Dictionary<int, T>();
        foreach (var item in lista)
        {
            Regla.Exigir(indice.TryAdd(id(item), item), "Hay animales repetidos en la lista.");
        }

        return indice;
    }
}
