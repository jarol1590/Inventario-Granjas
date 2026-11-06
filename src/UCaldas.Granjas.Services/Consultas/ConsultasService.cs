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
    Task<ResultadoBovinos> BuscarBovinosAsync(BovinoFiltro filtro, CancellationToken ct = default);

    /// <summary>Animales Disponibles de una granja, opcionalmente filtrados por chapeta o nombre (para armar documentos).</summary>
    Task<IReadOnlyList<AnimalSeleccionable>> BuscarDisponiblesAsync(int granjaId, string? texto = null, CancellationToken ct = default);

    Task<ResultadoCompras> ListarComprasAsync(CompraFiltro filtro, CancellationToken ct = default);

    Task<CompraEditable> ObtenerCompraAsync(int remisionId, CancellationToken ct = default);

    Task<ResultadoSalidas> ListarSalidasAsync(SalidaFiltro filtro, CancellationToken ct = default);

    Task<VentaEditable> ObtenerVentaAsync(int remisionId, CancellationToken ct = default);

    Task<BajaEditable> ObtenerBajaAsync(int remisionId, CancellationToken ct = default);

    Task<ResultadoTraslados> ListarTrasladosAsync(TrasladoFiltro filtro, CancellationToken ct = default);

    Task<DashboardDatos> ObtenerDashboardAsync(int? granjaId, int? programaId, DateTime mes, CancellationToken ct = default);
}

public sealed class ConsultasService(IUnitOfWorkFactory fabrica, IReloj reloj) : IConsultasService
{
    // ---- Bovinos ---------------------------------------------------------------------------------------------------

    public async Task<ResultadoBovinos> BuscarBovinosAsync(BovinoFiltro filtro, CancellationToken ct = default)
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
            .Select(a => new BovinoListItem(
                a.Id, a.ChapetaAmarilla, a.ChapetaRoja, a.Nombre, a.GranjaId, granjas.GetValueOrDefault(a.GranjaId, "—"),
                a.Sexo, a.Raza, a.OrientacionId, orientaciones.GetValueOrDefault(a.OrientacionId, "—"),
                a.CategoriaId, categorias.TryGetValue(a.CategoriaId, out var c) ? c.Nombre : "—",
                categorias.TryGetValue(a.CategoriaId, out var cat) && cat.Nombre == NombresBase.CategoriaSinClasificar,
                Edad(a.FechaNacimiento), a.ValorContableActual, a.ValorCompraOrigen,
                variacion.GetValueOrDefault(a.Id), a.Estado, a.ExentoValorizacion))
            .ToList();

        var resumen = new ResumenBovinos(
            items.Count,
            items.Count(i => i.Sexo == Sexo.Macho),
            items.Count(i => i.Sexo == Sexo.Hembra),
            items.Sum(i => i.ValorContable),
            items.Sum(i => i.ValorCompraOrigen));
        return new ResultadoBovinos(items, resumen);
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
        var detalles = (await uow.RemisionDetalles.GetAllAsync(ct)).ToLookup(d => d.RemisionId);
        var factura = filtro.Factura?.Trim();

        var items = (await uow.Remisiones.GetAllAsync(ct))
            .Where(r => r.TipoMovimiento == TipoRemision.Compra
                        && (filtro.IncluirAnuladas || !r.Anulada)
                        && EnRango(r.FechaRemision, filtro.Desde, filtro.Hasta)
                        && (filtro.GranjaId is null || r.GranjaDestinoId == filtro.GranjaId)
                        && (string.IsNullOrEmpty(factura) || r.NumeroRemision.Contains(factura, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.FechaRemision).ThenByDescending(r => r.Id)
            .Select(r => new CompraListItem(
                r.Id, r.NumeroRemision, r.OrdenCompra, r.FechaRemision, r.TerceroEntidad ?? "—", r.TerceroNit,
                granjas.GetValueOrDefault(r.GranjaDestinoId ?? 0, "—"),
                detalles[r.Id].Count(), detalles[r.Id].Sum(d => d.ValorTotalTransaccion ?? 0m), r.Anulada))
            .ToList();

        var vigentes = items.Where(i => !i.Anulada).ToList();
        return new ResultadoCompras(items, new ResumenCompras(vigentes.Count, vigentes.Sum(i => i.Cantidad), vigentes.Sum(i => i.TotalFactura)));
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

        bool TieneChapeta(Remision r)
            => string.IsNullOrEmpty(chapeta)
               || detalles[r.Id].Any(d => d.AnimalId is { } id && animales.TryGetValue(id, out var c) && c.Contains(chapeta, StringComparison.OrdinalIgnoreCase));

        var items = (await uow.Remisiones.GetAllAsync(ct))
            .Where(r => r.TipoMovimiento is TipoRemision.Venta or TipoRemision.Baja or TipoRemision.Traslado
                        && (filtro.IncluirAnuladas || !r.Anulada)
                        && EnRango(r.FechaRemision, filtro.Desde, filtro.Hasta)
                        && (filtro.GranjaId is null || r.GranjaOrigenId == filtro.GranjaId)
                        && TieneChapeta(r))
            .OrderByDescending(r => r.FechaRemision).ThenByDescending(r => r.Id)
            .Select(r =>
            {
                var lineas = detalles[r.Id].ToList();
                var esVenta = r.TipoMovimiento == TipoRemision.Venta;
                var destino = r.TipoMovimiento == TipoRemision.Traslado
                    ? granjas.GetValueOrDefault(r.GranjaDestinoId ?? 0, "—")
                    : r.TerceroEntidad ?? "—";
                return new SalidaListItem(
                    r.Id, r.NumeroRemision, r.FechaRemision, r.ClaseSalida ?? r.TipoMovimiento, r.TipoMovimiento,
                    r.OficioId is { } o && oficios.TryGetValue(o, out var numero) ? numero : null,
                    r.ReciboCaja, destino, lineas.Count,
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

    // ---- Dashboard -------------------------------------------------------------------------------------------------

    public async Task<DashboardDatos> ObtenerDashboardAsync(int? granjaId, int? programaId, DateTime mes, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);

        var granjas = (await uow.Granjas.GetAllAsync(ct)).OrderBy(g => g.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id);
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
            enFiltro.GroupBy(a => a.CategoriaId)
                .OrderBy(g => g.Key)
                .Select(g => new PuntoGrafica(categorias.TryGetValue(g.Key, out var c) ? c.Nombre : "—", g.Count()))
                .ToList(),
            [
                new PuntoGrafica("Nacimientos", nacimientos),
                new PuntoGrafica("Compras", compras),
                new PuntoGrafica("Ventas", ventas),
                new PuntoGrafica("Bajas", bajas),
                new PuntoGrafica("Traslados", traslados),
            ]);
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
