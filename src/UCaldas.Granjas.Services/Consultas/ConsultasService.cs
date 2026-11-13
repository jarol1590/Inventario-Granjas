using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.Services.Consultas;

/// <summary>Consultas de solo lectura para las pantallas: listados con filtros, KPIs, selectores y dashboard.</summary>
public interface IConsultasService
{
    Task<ResultadoAnimales> BuscarAnimalesAsync(AnimalFiltro filtro, CancellationToken ct = default);

    /// <summary>Animales Disponibles de una granja, opcionalmente filtrados por chapeta o nombre (para armar documentos).</summary>
    Task<IReadOnlyList<AnimalSeleccionable>> BuscarDisponiblesAsync(int granjaId, string? texto = null, CancellationToken ct = default);

    Task<ResultadoCompras> ListarComprasAsync(CompraFiltro filtro, CancellationToken ct = default);

    Task<CompraEditable> ObtenerCompraAsync(int remisionId, CancellationToken ct = default);

    Task<ResultadoSalidas> ListarSalidasAsync(SalidaFiltro filtro, CancellationToken ct = default);

    Task<VentaEditable> ObtenerVentaAsync(int remisionId, CancellationToken ct = default);

    Task<BajaEditable> ObtenerBajaAsync(int remisionId, CancellationToken ct = default);

    /// <summary>true si la remisión tiene líneas de lotes (se abre con el editor en modo lote).</summary>
    Task<bool> EsDocumentoDeLotesAsync(int remisionId, CancellationToken ct = default);

    Task<VentaLoteEditable> ObtenerVentaLoteAsync(int remisionId, CancellationToken ct = default);

    Task<TrasladoLoteEditable> ObtenerTrasladoLoteAsync(int remisionId, CancellationToken ct = default);

    Task<ResultadoTraslados> ListarTrasladosAsync(TrasladoFiltro filtro, CancellationToken ct = default);

    Task<TrasladoEditable> ObtenerTrasladoAsync(int remisionId, CancellationToken ct = default);

    Task<DashboardDatos> ObtenerDashboardAsync(int? granjaId, int? programaId, DateTime mes, CancellationToken ct = default);
}

public sealed class ConsultasService(IUnitOfWorkFactory fabrica, IReloj reloj) : IConsultasService
{
    // ---- Animales ---------------------------------------------------------------------------------------------------

    public async Task<ResultadoAnimales> BuscarAnimalesAsync(AnimalFiltro filtro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);

        var granjas = (await uow.Granjas.GetAllAsync(ct)).ToDictionary(g => g.Id, g => g.Nombre);
        var orientaciones = (await uow.Orientaciones.GetAllAsync(ct)).ToDictionary(o => o.Id, o => o.Nombre);
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id);
        var variacion = await VariacionAcumuladaAsync(uow, ct);
        var texto = filtro.Texto?.Trim();

        bool Coincide(Animal a)
            => (filtro.Estado is null || a.Estado == filtro.Estado)
               && (filtro.GranjaId is null || a.GranjaId == filtro.GranjaId)
               && (filtro.OrientacionId is null || a.OrientacionId == filtro.OrientacionId)
               && (filtro.CategoriaId is null || a.CategoriaId == filtro.CategoriaId)
               && (string.IsNullOrEmpty(filtro.Sexo) || a.Sexo == filtro.Sexo)
               && (string.IsNullOrWhiteSpace(filtro.Raza) || a.Raza.Equals(filtro.Raza, StringComparison.OrdinalIgnoreCase))
               && (filtro.ProgramaId is null || categorias.GetValueOrDefault(a.CategoriaId)?.ProgramaId == filtro.ProgramaId)
               && (string.IsNullOrEmpty(texto)
                   || a.ChapetaAmarilla.Contains(texto, StringComparison.OrdinalIgnoreCase)
                   || (a.ChapetaRoja?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false)
                   || (a.Nombre?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false));

        var items = (await uow.Animales.GetAllAsync(ct))
            .Where(Coincide)
            .OrderBy(a => a.ChapetaAmarilla, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AnimalListItem(
                a.Id, a.ChapetaAmarilla, a.ChapetaRoja, a.Nombre, a.GranjaId, granjas.GetValueOrDefault(a.GranjaId, "—"),
                a.Sexo, a.Raza, a.OrientacionId, orientaciones.GetValueOrDefault(a.OrientacionId, "—"),
                a.CategoriaId, categorias.TryGetValue(a.CategoriaId, out var c) ? c.Nombre : "—",
                categorias.TryGetValue(a.CategoriaId, out var cat) && cat.Nombre == NombresBase.CategoriaSinClasificar,
                Edad(a.FechaNacimiento), a.ValorContableActual, a.ValorCompraOrigen,
                variacion.GetValueOrDefault(a.Id), a.Estado, a.ExentoValorizacion))
            .ToList();

        var resumen = new ResumenAnimales(
            items.Count,
            items.Count(i => i.Sexo == Sexo.Macho),
            items.Count(i => i.Sexo == Sexo.Hembra),
            items.Sum(i => i.ValorContable),
            items.Sum(i => i.ValorCompraOrigen));
        return new ResultadoAnimales(items, resumen);
    }

    public async Task<IReadOnlyList<AnimalSeleccionable>> BuscarDisponiblesAsync(int granjaId, string? texto = null, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var buscado = texto?.Trim();
        var animales = (await uow.Animales.GetByEstadoAsync(EstadoAnimal.Disponible, ct))
            .Where(a => a.GranjaId == granjaId
                        && (string.IsNullOrEmpty(buscado)
                            || a.ChapetaAmarilla.Contains(buscado, StringComparison.OrdinalIgnoreCase)
                            || (a.ChapetaRoja?.Contains(buscado, StringComparison.OrdinalIgnoreCase) ?? false)
                            || (a.Nombre?.Contains(buscado, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderBy(a => a.ChapetaAmarilla, StringComparer.OrdinalIgnoreCase);
        return await ASeleccionablesAsync(uow, animales, ct);
    }

    // ---- Compras ---------------------------------------------------------------------------------------------------

    public async Task<ResultadoCompras> ListarComprasAsync(CompraFiltro filtro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);

        var granjas = (await uow.Granjas.GetAllAsync(ct)).ToDictionary(g => g.Id, g => g.Nombre);
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id);
        var factura = filtro.Factura?.Trim();
        var programa = filtro.ProgramaId is { } programaId ? await uow.Programas.GetByIdAsync(programaId, ct) : null;
        var items = new List<CompraListItem>();

        // Programas de animales individuales: una fila por factura (remisión de compra).
        if (programa is null || programa.TipoInventario != TipoInventario.Lote)
        {
            var detalles = (await uow.RemisionDetalles.GetAllAsync(ct)).ToLookup(d => d.RemisionId);
            var delPrograma = await FiltroDeProgramaAsync(uow, programa?.Id, detalles, ct);
            var animales = (await uow.Animales.GetAllAsync(ct)).ToDictionary(a => a.Id);

            items.AddRange((await uow.Remisiones.GetAllAsync(ct))
                .Where(r => r.TipoMovimiento == TipoRemision.Compra
                            && (filtro.IncluirAnuladas || !r.Anulada)
                            && EnRango(r.FechaRemision, filtro.Desde, filtro.Hasta)
                            && (filtro.GranjaId is null || r.GranjaDestinoId == filtro.GranjaId)
                            && delPrograma(r)
                            && (string.IsNullOrEmpty(factura) || r.NumeroRemision.Contains(factura, StringComparison.OrdinalIgnoreCase)))
                .Select(r =>
                {
                    var lineas = detalles[r.Id].ToList();
                    var compradas = lineas.Where(d => d.AnimalId is not null && animales.ContainsKey(d.AnimalId.Value))
                        .Select(d => animales[d.AnimalId!.Value]).ToList();
                    var total = lineas.Sum(d => d.ValorTotalTransaccion ?? 0m);
                    var identificador = compradas.Count == 0
                        ? "—"
                        : string.Join(", ", compradas.Take(3).Select(a => a.ChapetaAmarilla)) + (compradas.Count > 3 ? $" (+{compradas.Count - 3})" : string.Empty);
                    var categoria = compradas.Count == 0
                        ? "—"
                        : CategoriaUnica(compradas.Select(a => categorias.TryGetValue(a.CategoriaId, out var c) ? c.Nombre : "—"));
                    return new CompraListItem(
                        r.Id, r.NumeroRemision, r.OrdenCompra, r.FechaRemision, r.TerceroEntidad ?? "—", r.TerceroNit,
                        granjas.GetValueOrDefault(r.GranjaDestinoId ?? 0, "—"), lineas.Count, total, r.Anulada,
                        null, identificador, categoria, lineas.Count == 0 ? 0m : total / lineas.Count);
                }));
        }

        // Programas por lotes: los lotes comprados (todavía no tienen remisión ni Kardex: la compra vive en el propio lote).
        if (programa is null || programa.TipoInventario == TipoInventario.Lote)
        {
            var programasLote = (await uow.Programas.GetAllAsync(ct)).Where(p => p.TipoInventario == TipoInventario.Lote).Select(p => p.Id).ToHashSet();
            items.AddRange((await uow.Lotes.GetAllAsync(ct))
                .Where(l => l.ClaseEntrada == ClaseEntrada.Compra
                            && (filtro.IncluirAnuladas || !l.Anulado)
                            && programasLote.Contains(l.ProgramaId)
                            && (programa is null || l.ProgramaId == programa.Id)
                            && EnRango(l.FechaNacimientoIngreso, filtro.Desde, filtro.Hasta)
                            && (filtro.GranjaId is null || l.GranjaId == filtro.GranjaId)
                            && (string.IsNullOrEmpty(factura) || (l.OrigenReferencia?.Contains(factura, StringComparison.OrdinalIgnoreCase) ?? false)))
                .Select(l => new CompraListItem(
                    0, l.OrigenReferencia ?? "—", l.OrdenCompra, l.FechaNacimientoIngreso, l.Proveedor ?? "—", null,
                    granjas.GetValueOrDefault(l.GranjaId, "—"), l.CantidadInicial,
                    l.ControlNumericoPoblacion ? l.CantidadInicial * l.ValorCompraUnitario : l.ValorCompraUnitario, l.Anulado,
                    l.Id, l.CodigoLote, categorias.TryGetValue(l.EtapaId, out var etapa) ? etapa.Nombre : "—", l.ValorCompraUnitario)));
        }

        items = [.. items.OrderByDescending(i => i.Fecha).ThenByDescending(i => i.RemisionId).ThenByDescending(i => i.LoteId)];
        var vigentes = items.Where(i => !i.Anulada).ToList();
        return new ResultadoCompras(items, new ResumenCompras(vigentes.Count, vigentes.Sum(i => i.Cantidad), vigentes.Sum(i => i.TotalFactura)));
    }
    /// <summary>
    /// Predicado "el documento es del programa". El programa de un documento sale de sus líneas: la categoría del animal
    /// (ANIMAL no guarda el programa) o el programa del lote. Un documento sin líneas (p. ej. una compra anulada, que retira
    /// sus animales) se asigna al programa de valorización biológica para que no desaparezca. Sin programa, todos pasan.
    /// </summary>
    private static async Task<Func<Remision, bool>> FiltroDeProgramaAsync(
        IUnitOfWork uow, int? programaId, ILookup<int, RemisionDetalle> detalles, CancellationToken ct)
    {
        if (programaId is not { } id)
        {
            return _ => true;
        }

        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id);
        var animales = (await uow.Animales.GetAllAsync(ct)).ToDictionary(a => a.Id);
        var lotes = (await uow.Lotes.GetAllAsync(ct)).ToDictionary(l => l.Id);
        var biologico = (await uow.Programas.GetAllAsync(ct)).FirstOrDefault(p => p.UsaValorizacionBiologica)?.Id;

        bool LineaDelPrograma(RemisionDetalle d)
            => d.LoteId is { } loteId
                ? lotes.TryGetValue(loteId, out var lote) && lote.ProgramaId == id
                : d.AnimalId is { } animalId && animales.TryGetValue(animalId, out var a)
                  && categorias.TryGetValue(a.CategoriaId, out var c) && c.ProgramaId == id;

        return remision =>
        {
            var lineas = detalles[remision.Id].ToList();
            return lineas.Count == 0 ? id == biologico : lineas.Any(LineaDelPrograma);
        };
    }
    /// <summary>La categoría común de una factura, o "Varias" si sus animales son de distintas categorías.</summary>
    private static string CategoriaUnica(IEnumerable<string> nombres)
    {
        var distintos = nombres.Distinct().ToList();
        return distintos.Count == 1 ? distintos[0] : "Varias";
    }

    public async Task<CompraEditable> ObtenerCompraAsync(int remisionId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct) ?? throw Regla.Violada("La compra no existe.");
        Regla.Exigir(remision.TipoMovimiento == TipoRemision.Compra, $"El documento {remision.NumeroRemision} no es una compra.");

        var detalles = await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct);
        var animales = (await uow.Animales.GetByIdsAsync(detalles.Where(d => d.AnimalId is not null).Select(d => d.AnimalId!.Value), ct)).ToDictionary(a => a.Id);

        var dto = new CompraDto
        {
            RemisionId = remision.Id,
            NumeroFactura = remision.NumeroRemision,
            OrdenCompra = remision.OrdenCompra,
            FechaCompra = remision.FechaRemision,
            GranjaDestinoId = remision.GranjaDestinoId ?? 0,
            Proveedor = remision.TerceroEntidad ?? string.Empty,
            ProveedorNit = remision.TerceroNit,
            Observaciones = remision.Observaciones,
            Animales = detalles
                .Where(d => d.AnimalId is not null && animales.ContainsKey(d.AnimalId.Value))
                .Select(d =>
                {
                    var a = animales[d.AnimalId!.Value];
                    return new CompraAnimalDto
                    {
                        AnimalId = a.Id,
                        ChapetaAmarilla = a.ChapetaAmarilla,
                        ChapetaRoja = a.ChapetaRoja,
                        Nombre = a.Nombre,
                        Sexo = a.Sexo,
                        FechaNacimiento = a.FechaNacimiento,
                        Raza = a.Raza,
                        PesoKg = a.PesoActualKg,
                        OrientacionId = a.OrientacionId,
                        CategoriaId = a.CategoriaId,
                        NumeroPartos = a.NumeroPartos,
                        PrecioFactura = d.ValorTotalTransaccion ?? a.ValorCompraOrigen,
                        Observaciones = d.ObservacionesEspecificas,
                    };
                })
                .OrderBy(a => a.ChapetaAmarilla, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
        return new CompraEditable(dto, remision.Anulada);
    }

    // ---- Salidas y bajas -------------------------------------------------------------------------------------------

    public async Task<ResultadoSalidas> ListarSalidasAsync(SalidaFiltro filtro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);

        var granjas = (await uow.Granjas.GetAllAsync(ct)).ToDictionary(g => g.Id, g => g.Nombre);
        var oficios = (await uow.Oficios.GetAllAsync(ct)).ToDictionary(o => o.Id, o => o.NumeroOficio);
        var animales = (await uow.Animales.GetAllAsync(ct)).ToDictionary(a => a.Id, a => a.ChapetaAmarilla);
        var detalles = (await uow.RemisionDetalles.GetAllAsync(ct)).ToLookup(d => d.RemisionId);
        var chapeta = filtro.Chapeta?.Trim();
        var delPrograma = await FiltroDeProgramaAsync(uow, filtro.ProgramaId, detalles, ct);

        bool TieneChapeta(Remision r)
            => string.IsNullOrEmpty(chapeta)
               || detalles[r.Id].Any(d => d.AnimalId is { } id && animales.TryGetValue(id, out var c) && c.Contains(chapeta, StringComparison.OrdinalIgnoreCase));

        var items = (await uow.Remisiones.GetAllAsync(ct))
            .Where(r => r.TipoMovimiento is TipoRemision.Venta or TipoRemision.Baja or TipoRemision.Traslado
                        && (filtro.IncluirAnuladas || !r.Anulada)
                        && EnRango(r.FechaRemision, filtro.Desde, filtro.Hasta)
                        && (filtro.GranjaId is null || r.GranjaOrigenId == filtro.GranjaId)
                        && delPrograma(r)
                        && TieneChapeta(r))
            .OrderByDescending(r => r.FechaRemision).ThenByDescending(r => r.Id)
            .Select(r =>
            {
                var lineas = detalles[r.Id].ToList();
                var cabezas = lineas.Any(d => d.LoteId is not null) ? lineas.Sum(d => d.Cantidad) : lineas.Count;
                var esVenta = r.TipoMovimiento == TipoRemision.Venta;
                var destino = r.TipoMovimiento == TipoRemision.Traslado
                    ? granjas.GetValueOrDefault(r.GranjaDestinoId ?? 0, "—")
                    : r.TerceroEntidad ?? "—";
                return new SalidaListItem(
                    r.Id, r.NumeroRemision, r.FechaRemision, r.ClaseSalida ?? r.TipoMovimiento, r.TipoMovimiento,
                    r.OficioId is { } o && oficios.TryGetValue(o, out var numero) ? numero : null,
                    r.ReciboCaja, destino, cabezas,
                    esVenta ? lineas.Sum(d => d.ValorTotalTransaccion ?? 0m) : lineas.Sum(d => d.CostoSistemaLibros),
                    r.Anulada);
            })
            .ToList();

        var vigentes = items.Where(i => !i.Anulada).ToList();
        var resumen = new ResumenSalidas(
            vigentes.Where(i => i.TipoDocumento == TipoRemision.Venta).Sum(i => i.Total),
            vigentes.Where(i => i.TipoDocumento is TipoRemision.Venta or TipoRemision.Baja).Sum(i => i.Cantidad),
            vigentes.Where(i => i.TipoDocumento == TipoRemision.Baja).Sum(i => i.Cantidad));
        return new ResultadoSalidas(items, resumen);
    }

    public async Task<VentaEditable> ObtenerVentaAsync(int remisionId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct) ?? throw Regla.Violada("La venta no existe.");
        Regla.Exigir(remision.TipoMovimiento == TipoRemision.Venta, $"El documento {remision.NumeroRemision} no es una venta.");

        var oficio = remision.OficioId is { } id ? await uow.Oficios.GetByIdAsync(id, ct) : null;
        var detalles = await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct);
        var animales = await uow.Animales.GetByIdsAsync(detalles.Where(d => d.AnimalId is not null).Select(d => d.AnimalId!.Value), ct);

        var dto = new VentaDto
        {
            RemisionId = remision.Id,
            NumeroOficio = oficio?.NumeroOficio ?? string.Empty,
            FechaOficio = oficio?.FechaOficio ?? default,
            ProgramaId = oficio?.ProgramaId ?? 0,
            ObservacionesOficio = oficio?.Observaciones,
            NumeroRemision = remision.NumeroRemision,
            FechaRemision = remision.FechaRemision,
            ReciboCaja = remision.ReciboCaja ?? string.Empty,
            GranjaOrigenId = remision.GranjaOrigenId ?? 0,
            Comprador = remision.TerceroEntidad ?? string.Empty,
            Observaciones = remision.Observaciones,
            Animales = detalles.Where(d => d.AnimalId is not null).Select(d => new VentaAnimalDto
            {
                AnimalId = d.AnimalId!.Value,
                PrecioVenta = d.ValorTotalTransaccion ?? 0m,
                PesoKg = d.PesoMomentoKg,
                ObservacionesEspecificas = d.ObservacionesEspecificas,
            }).ToList(),
        };
        return new VentaEditable(dto, remision.Anulada, await ASeleccionablesAsync(uow, animales.OrderBy(a => a.ChapetaAmarilla, StringComparer.OrdinalIgnoreCase), ct));
    }

    public async Task<bool> EsDocumentoDeLotesAsync(int remisionId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return (await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct)).Any(d => d.LoteId is not null);
    }

    public async Task<VentaLoteEditable> ObtenerVentaLoteAsync(int remisionId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct) ?? throw Regla.Violada("La venta no existe.");
        Regla.Exigir(remision.TipoMovimiento == TipoRemision.Venta, $"El documento {remision.NumeroRemision} no es una venta.");

        var oficio = remision.OficioId is { } id ? await uow.Oficios.GetByIdAsync(id, ct) : null;
        var detalles = (await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct)).Where(d => d.LoteId is not null).ToList();
        var dto = new VentaLoteDto
        {
            RemisionId = remision.Id,
            NumeroOficio = oficio?.NumeroOficio ?? string.Empty,
            FechaOficio = oficio?.FechaOficio ?? default,
            ProgramaId = oficio?.ProgramaId ?? 0,
            ObservacionesOficio = oficio?.Observaciones,
            NumeroRemision = remision.NumeroRemision,
            FechaRemision = remision.FechaRemision,
            ReciboCaja = remision.ReciboCaja ?? string.Empty,
            GranjaOrigenId = remision.GranjaOrigenId ?? 0,
            Comprador = remision.TerceroEntidad ?? string.Empty,
            Observaciones = remision.Observaciones,
            Lotes = detalles.Select(d => new VentaLoteLineaDto
            {
                LoteId = d.LoteId!.Value, Cantidad = d.Cantidad, PesoKg = d.PesoVendidoKg, ValorVenta = d.ValorTotalTransaccion ?? 0m,
                ObservacionesEspecificas = d.ObservacionesEspecificas,
            }).ToList(),
        };
        return new VentaLoteEditable(dto, remision.Anulada, await LotesDeLineasAsync(uow, detalles, ct));
    }

    public async Task<TrasladoLoteEditable> ObtenerTrasladoLoteAsync(int remisionId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct) ?? throw Regla.Violada("El traslado no existe.");
        Regla.Exigir(remision.TipoMovimiento == TipoRemision.Traslado, $"El documento {remision.NumeroRemision} no es un traslado.");

        var detalles = (await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct)).Where(d => d.LoteId is not null).ToList();
        var lotes = (await uow.Lotes.GetAllAsync(ct)).ToDictionary(l => l.Id);
        var dto = new TrasladoLoteDto
        {
            RemisionId = remision.Id,
            NumeroRemision = remision.NumeroRemision,
            FechaTraslado = remision.FechaRemision,
            GranjaOrigenId = remision.GranjaOrigenId ?? 0,
            GranjaDestinoId = remision.GranjaDestinoId ?? 0,
            Observaciones = remision.Observaciones,
            Lotes = detalles.Select(d =>
            {
                var parcial = d.LoteDestinoId is { } destinoId && destinoId != d.LoteId && lotes.ContainsKey(destinoId);
                return new TrasladoLoteLineaDto
                {
                    LoteId = d.LoteId!.Value,
                    Cantidad = d.Cantidad,
                    CodigoNuevoLote = parcial ? lotes[d.LoteDestinoId!.Value].CodigoLote : null,
                    UbicacionDestino = d.LoteDestinoId is { } dest && lotes.TryGetValue(dest, out var l) ? l.Ubicacion : null,
                };
            }).ToList(),
        };
        return new TrasladoLoteEditable(dto, remision.Anulada, await LotesDeLineasAsync(uow, detalles, ct));
    }

    private static async Task<IReadOnlyList<LoteSeleccionable>> LotesDeLineasAsync(IUnitOfWork uow, IEnumerable<RemisionDetalle> detalles, CancellationToken ct)
    {
        var etapas = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id, c => c.Nombre);
        var lotes = (await uow.Lotes.GetAllAsync(ct)).ToDictionary(l => l.Id);
        return detalles
            .Where(d => d.LoteId is not null && lotes.ContainsKey(d.LoteId.Value))
            .Select(d => lotes[d.LoteId!.Value])
            .Select(l => new LoteSeleccionable(
                l.Id, l.CodigoLote, etapas.GetValueOrDefault(l.EtapaId, "—"), l.Ubicacion, l.CantidadActual, l.ControlNumericoPoblacion, l.ValorContableUnitario))
            .ToList();
    }

    public async Task<BajaEditable> ObtenerBajaAsync(int remisionId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct) ?? throw Regla.Violada("La baja no existe.");
        Regla.Exigir(remision.TipoMovimiento == TipoRemision.Baja, $"El documento {remision.NumeroRemision} no es una baja.");

        var detalles = await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct);
        var animales = await uow.Animales.GetByIdsAsync(detalles.Where(d => d.AnimalId is not null).Select(d => d.AnimalId!.Value), ct);

        var dto = new BajaDto
        {
            RemisionId = remision.Id,
            NumeroRemision = remision.NumeroRemision,
            FechaBaja = remision.FechaRemision,
            GranjaOrigenId = remision.GranjaOrigenId ?? 0,
            ClaseSalida = remision.ClaseSalida ?? string.Empty,
            DocumentoSoporte = remision.TerceroEntidad ?? string.Empty,
            Observaciones = remision.Observaciones,
            Animales = detalles.Where(d => d.AnimalId is not null).Select(d => new BajaAnimalDto
            {
                AnimalId = d.AnimalId!.Value,
                ObservacionesEspecificas = d.ObservacionesEspecificas,
            }).ToList(),
        };
        return new BajaEditable(dto, remision.Anulada, await ASeleccionablesAsync(uow, animales.OrderBy(a => a.ChapetaAmarilla, StringComparer.OrdinalIgnoreCase), ct));
    }

    // ---- Traslados -------------------------------------------------------------------------------------------------

    public async Task<ResultadoTraslados> ListarTrasladosAsync(TrasladoFiltro filtro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);

        var granjas = (await uow.Granjas.GetAllAsync(ct)).ToDictionary(g => g.Id, g => g.Nombre);
        var animales = (await uow.Animales.GetAllAsync(ct)).ToDictionary(a => a.Id, a => a.ChapetaAmarilla);
        var detalles = (await uow.RemisionDetalles.GetAllAsync(ct)).ToLookup(d => d.RemisionId);
        var texto = filtro.Texto?.Trim();

        bool CoincideTexto(Remision r)
            => string.IsNullOrEmpty(texto)
               || r.NumeroRemision.Contains(texto, StringComparison.OrdinalIgnoreCase)
               || detalles[r.Id].Any(d => d.AnimalId is { } id && animales.TryGetValue(id, out var c) && c.Contains(texto, StringComparison.OrdinalIgnoreCase));

        var items = (await uow.Remisiones.GetAllAsync(ct))
            .Where(r => r.TipoMovimiento == TipoRemision.Traslado
                        && (filtro.IncluirAnulados || !r.Anulada)
                        && EnRango(r.FechaRemision, filtro.Desde, filtro.Hasta)
                        && (filtro.GranjaId is null || r.GranjaOrigenId == filtro.GranjaId || r.GranjaDestinoId == filtro.GranjaId)
                        && CoincideTexto(r))
            .OrderByDescending(r => r.FechaRemision).ThenByDescending(r => r.Id)
            .Select(r => new TrasladoListItem(
                r.Id, r.NumeroRemision, r.FechaRemision,
                granjas.GetValueOrDefault(r.GranjaOrigenId ?? 0, "—"), granjas.GetValueOrDefault(r.GranjaDestinoId ?? 0, "—"),
                detalles[r.Id].Count(), r.Anulada))
            .ToList();

        var vigentes = items.Where(i => !i.Anulado).ToList();
        return new ResultadoTraslados(items, new ResumenTraslados(vigentes.Count, vigentes.Sum(i => i.Cantidad)));
    }

    public async Task<TrasladoEditable> ObtenerTrasladoAsync(int remisionId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var remision = await uow.Remisiones.GetByIdAsync(remisionId, ct) ?? throw Regla.Violada("El traslado no existe.");
        Regla.Exigir(remision.TipoMovimiento == TipoRemision.Traslado, $"El documento {remision.NumeroRemision} no es un traslado.");

        var detalles = (await uow.RemisionDetalles.GetByRemisionAsync(remisionId, ct)).Where(d => d.AnimalId is not null).ToList();
        var animales = (await uow.Animales.GetByIdsAsync(detalles.Select(d => d.AnimalId!.Value), ct)).ToDictionary(a => a.Id);
        var orientaciones = (await uow.Orientaciones.GetAllAsync(ct)).ToDictionary(o => o.Id, o => o.Nombre);
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id, c => c.Nombre);

        var dto = new TrasladoDto
        {
            RemisionId = remision.Id,
            NumeroRemision = remision.NumeroRemision,
            FechaTraslado = remision.FechaRemision,
            GranjaOrigenId = remision.GranjaOrigenId ?? 0,
            GranjaDestinoId = remision.GranjaDestinoId ?? 0,
            Observaciones = remision.Observaciones,
            Animales = detalles.Select(d => new TrasladoAnimalDto
            {
                AnimalId = d.AnimalId!.Value,
                OrientacionDestinoId = d.OrientacionDestinoId,
                ObservacionesEspecificas = d.ObservacionesEspecificas,
            }).ToList(),
        };

        var seleccionables = detalles
            .Where(d => animales.ContainsKey(d.AnimalId!.Value))
            .Select(d =>
            {
                var a = animales[d.AnimalId!.Value];
                var orientacionOrigen = d.OrientacionOrigenId ?? a.OrientacionId;
                return new AnimalSeleccionable(
                    a.Id, a.ChapetaAmarilla, a.ChapetaRoja, a.Sexo, a.FechaNacimiento, a.Raza,
                    categorias.GetValueOrDefault(a.CategoriaId, "—"), orientacionOrigen,
                    orientaciones.GetValueOrDefault(orientacionOrigen, "—"), d.CostoSistemaLibros);
            })
            .OrderBy(a => a.ChapetaAmarilla, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TrasladoEditable(dto, remision.Anulada, seleccionables);
    }

    // ---- Dashboard -------------------------------------------------------------------------------------------------

    public async Task<DashboardDatos> ObtenerDashboardAsync(int? granjaId, int? programaId, DateTime mes, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);

        var granjas = (await uow.Granjas.GetAllAsync(ct)).OrderBy(g => g.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id);

        // Programa por lotes: el tablero cuenta cabezas de lotes y lista las etapas del programa.
        if (programaId is { } idPrograma && await uow.Programas.GetByIdAsync(idPrograma, ct) is { TipoInventario: TipoInventario.Lote })
        {
            return await DashboardDeLotesAsync(uow, granjas, categorias.Values.Where(c => c.ProgramaId == idPrograma).OrderBy(c => c.Id).ToList(), idPrograma, granjaId, mes, ct);
        }

        var individuales = (await uow.Programas.GetAllAsync(ct)).Where(p => p.TipoInventario == TipoInventario.Individual).Select(p => p.Id).ToHashSet();
        var animales = await uow.Animales.GetAllAsync(ct);
        var movimientos = await uow.Movimientos.GetAllAsync(ct);

        var desde = new DateTime(mes.Year, mes.Month, 1);
        var hasta = desde.AddMonths(1);
        bool DelPrograma(int categoriaId) => programaId is null || categorias.GetValueOrDefault(categoriaId)?.ProgramaId == programaId;

        var disponibles = animales.Where(a => a.Estado == EstadoAnimal.Disponible && DelPrograma(a.CategoriaId)).ToList();
        var enFiltro = disponibles.Where(a => granjaId is null || a.GranjaId == granjaId).ToList();

        var animalesPorId = animales.ToDictionary(a => a.Id);
        var delMes = movimientos
            .Where(m => m.FechaMovimiento >= desde && m.FechaMovimiento < hasta
                        && (granjaId is null || (m.GranjaOrigenId ?? m.GranjaDestinoId) == granjaId)
                        && m.AnimalId is { } id && animalesPorId.TryGetValue(id, out var a) && DelPrograma(a.CategoriaId))
            .ToList();

        int Contar(params string[] tipos) => delMes.Count(m => tipos.Contains(m.TipoMovimiento));
        var nacimientos = Contar(TipoMovimiento.Nacimiento);
        var compras = Contar(TipoMovimiento.Compra);
        var ventas = Contar(TipoMovimiento.Venta);
        var bajas = Contar(TipoMovimiento.Muerte, TipoMovimiento.Hurto);
        var traslados = delMes.Count(m => m.TipoMovimiento == TipoMovimiento.Traslado && m.GranjaOrigenId is not null);

        return new DashboardDatos(
            enFiltro.Count,
            enFiltro.Sum(a => a.ValorContableActual),
            nacimientos,
            ventas + bajas,
            granjas.Select(g => new PuntoGrafica(g.Nombre, disponibles.Count(a => a.GranjaId == g.Id))).ToList(),
            PuntosPorCategoria(categorias.Values.Where(c => individuales.Contains(c.ProgramaId)), enFiltro, programaId),   // los lotes no son animales
            [
                new PuntoGrafica("Nacimientos", nacimientos),
                new PuntoGrafica("Compras", compras),
                new PuntoGrafica("Ventas", ventas),
                new PuntoGrafica("Bajas", bajas),
                new PuntoGrafica("Traslados", traslados),
            ]);
    }

    /// <summary>
    /// Tablero de un programa por lotes. Las barras son cabezas vivas (un lote sin conteo suma 1) por etapa y por granja. Los
    /// movimientos del mes salen de los documentos (compra y nacimiento = ingreso del lote; venta y traslado = remisiones);
    /// la mortandad no tiene registro fechado y por eso no aparece.
    /// </summary>
    private static async Task<DashboardDatos> DashboardDeLotesAsync(
        IUnitOfWork uow, List<Granja> granjas, List<CategoriaEtapa> etapas, int programaId, int? granjaId, DateTime mes, CancellationToken ct)
    {
        var desde = new DateTime(mes.Year, mes.Month, 1);
        var hasta = desde.AddMonths(1);
        static int Cabezas(LotePoblacion l) => l.ControlNumericoPoblacion ? l.CantidadActual : 1;

        var delPrograma = (await uow.Lotes.GetAllAsync(ct)).Where(l => l.ProgramaId == programaId && !l.Anulado).ToList();
        var activos = delPrograma.Where(l => l.Estado == EstadoLote.Activo).ToList();
        var enFiltro = activos.Where(l => granjaId is null || l.GranjaId == granjaId).ToList();

        var lotes = delPrograma.ToDictionary(l => l.Id);
        var todasLasRemisiones = (await uow.Remisiones.GetAllAsync(ct)).Where(r => !r.Anulada).ToDictionary(r => r.Id);
        var todosLosDetalles = (await uow.RemisionDetalles.GetAllAsync(ct)).Where(d => d.LoteId is { } id && lotes.ContainsKey(id)).ToList();

        // Un desdoble o un traslado parcial le restan cabezas al lote de origen (también a su cantidad inicial): para contar
        // lo que ingresó originalmente se suman de nuevo.
        int Ingresadas(LotePoblacion l)
        {
            if (!l.ControlNumericoPoblacion)
            {
                return 1;
            }

            var trasladadas = todosLosDetalles
                .Where(d => d.LoteId == l.Id && d.LoteDestinoId is { } destino && destino != l.Id
                            && todasLasRemisiones.TryGetValue(d.RemisionId, out var r) && r.TipoMovimiento == TipoRemision.Traslado)
                .Sum(d => d.Cantidad);
            var desdobladas = delPrograma.Where(x => x.ClaseEntrada == ClaseEntrada.Desdoble && x.OrigenReferencia == l.CodigoLote).Sum(x => x.CantidadInicial);
            return l.CantidadInicial + trasladadas + desdobladas;
        }

        int Ingresados(string clase)
            => delPrograma.Where(l => l.ClaseEntrada == clase && l.FechaNacimientoIngreso >= desde && l.FechaNacimientoIngreso < hasta
                                      && (granjaId is null || l.GranjaId == granjaId))
                .Sum(Ingresadas);

        var detalles = todosLosDetalles;
        var remisiones = todasLasRemisiones.Values
            .Where(r => r.FechaRemision >= desde && r.FechaRemision < hasta && (granjaId is null || r.GranjaOrigenId == granjaId))
            .ToDictionary(r => r.Id);
        int Movidas(string tipo)
            => detalles.Where(d => remisiones.TryGetValue(d.RemisionId, out var r) && r.TipoMovimiento == tipo).Sum(d => d.Cantidad > 0 ? d.Cantidad : 1);

        var nacimientos = Ingresados(ClaseEntrada.Nacimiento);
        var compras = Ingresados(ClaseEntrada.Compra);
        var ventas = Movidas(TipoRemision.Venta);
        var traslados = Movidas(TipoRemision.Traslado);

        var porEtapa = etapas.Select(e => new PuntoGrafica(e.Nombre, enFiltro.Where(l => l.EtapaId == e.Id).Sum(Cabezas))).ToList();
        if (porEtapa.Count == 0)
        {
            porEtapa.Add(new PuntoGrafica("Sin categorías", 0));
        }

        return new DashboardDatos(
            enFiltro.Sum(Cabezas),
            enFiltro.Sum(l => l.ValorTotalContable),
            nacimientos,
            ventas,
            granjas.Select(g => new PuntoGrafica(g.Nombre, activos.Where(l => l.GranjaId == g.Id).Sum(Cabezas))).ToList(),
            porEtapa,
            [
                new PuntoGrafica("Nacimientos", nacimientos),
                new PuntoGrafica("Compras", compras),
                new PuntoGrafica("Ventas", ventas),
                new PuntoGrafica("Traslados", traslados),
            ],
            EsLotes: true);
    }

    /// <summary>
    /// Animales por categoría para la gráfica del tablero. Lista todas las categorías del programa (aunque no tengan
    /// animales: la gráfica se dibuja en cero en vez de quedar vacía); "Sin clasificar" solo aparece si tiene animales.
    /// Un programa sin categorías (recién creado) produce un único punto en cero.
    /// </summary>
    private static List<PuntoGrafica> PuntosPorCategoria(IEnumerable<CategoriaEtapa> categorias, IReadOnlyList<Animal> enFiltro, int? programaId)
    {
        var puntos = categorias
            .Where(c => programaId is null || c.ProgramaId == programaId)
            .OrderBy(c => c.Id)
            .Select(c => new PuntoGrafica(c.Nombre, enFiltro.Count(a => a.CategoriaId == c.Id)))
            .Where(p => p.Valor > 0 || p.Etiqueta != NombresBase.CategoriaSinClasificar)
            .ToList();

        if (puntos.Count == 0)
        {
            puntos.Add(new PuntoGrafica("Sin categorías", 0));
        }

        return puntos;
    }

    // ---- Auxiliares ------------------------------------------------------------------------------------------------

    private static bool EnRango(DateTime fecha, DateTime desde, DateTime hasta)
        => fecha.Date >= desde.Date && fecha.Date <= hasta.Date;

    private int Edad(DateTime nacimiento)
        => reloj.Hoy < nacimiento.Date ? 0 : EdadCalculator.Meses(nacimiento, reloj.Hoy);

    private static async Task<Dictionary<int, decimal>> VariacionAcumuladaAsync(IUnitOfWork uow, CancellationToken ct)
        => (await uow.Movimientos.GetAllAsync(ct))
            .Where(m => m.TipoMovimiento == TipoMovimiento.Valorizacion && m.AnimalId is not null)
            .GroupBy(m => m.AnimalId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.CostoAsociado));

    private static async Task<IReadOnlyList<AnimalSeleccionable>> ASeleccionablesAsync(IUnitOfWork uow, IEnumerable<Animal> animales, CancellationToken ct)
    {
        var orientaciones = (await uow.Orientaciones.GetAllAsync(ct)).ToDictionary(o => o.Id, o => o.Nombre);
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id, c => c.Nombre);
        return animales.Select(a => new AnimalSeleccionable(
            a.Id, a.ChapetaAmarilla, a.ChapetaRoja, a.Sexo, a.FechaNacimiento, a.Raza,
            categorias.GetValueOrDefault(a.CategoriaId, "—"), a.OrientacionId, orientaciones.GetValueOrDefault(a.OrientacionId, "—"),
            a.ValorContableActual)).ToList();
    }
}
