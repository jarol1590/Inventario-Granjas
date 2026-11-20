using System.Text.RegularExpressions;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Comercial;

/// <summary>Estados que muestra la bandeja de remisiones.</summary>
public static class EstadoRemision
{
    public const string Pagada = "Pagada";
    public const string PendienteRecibo = "Pen.Recibo SP";
    public const string TrasladoInterno = "Traslado Interno";
    public const string Anulada = "Anulada";

    public static readonly IReadOnlyList<string> Todos = [Pagada, PendienteRecibo, TrasladoInterno, Anulada];
}

/// <param name="Tipo"><see cref="TipoRemision.Venta"/> o <see cref="TipoRemision.Traslado"/>; nulo = ambos.</param>
public sealed record RemisionFiltro(
    int? GranjaId = null,
    int? ProgramaId = null,
    string? Tipo = null,
    string? Estado = null,
    string? Texto = null,
    DateTime? Desde = null,
    DateTime? Hasta = null,
    bool IncluirAnuladas = false);

public sealed record RemisionFila(
    int Id,
    string Numero,
    DateTime Fecha,
    string Tipo,
    string? Oficio,
    int? OficioId,
    int? ProgramaId,
    string Programa,
    string ClienteODestino,
    decimal Total,
    string? Recibo,
    string Estado,
    bool Anulada,
    string Clase,
    bool EsDelModulo);

/// <summary>Una línea de la remisión sea cual sea su origen (animal, lote o producto).</summary>
public sealed record LineaRemision(
    string Clase,
    int? AnimalId,
    int? LoteId,
    int? ProductoId,
    string Codigo,
    string Descripcion,
    string Unidad,
    decimal Cantidad,
    decimal ValorUnitario,
    decimal ValorTotal,
    decimal? PesoKg,
    decimal CostoLibros,
    string? Observaciones,
    int? OrientacionDestinoId,
    string? CodigoLoteDestino);

public static class ClaseDeLinea
{
    public const string Animal = "ANIMAL";
    public const string Lote = "LOTE";
    public const string Producto = "PRODUCTO";
}

public sealed record RemisionVista(
    Remision Remision,
    string GranjaOrigen,
    string? GranjaDestino,
    int? ProgramaId,
    string Programa,
    OficioVenta? Oficio,
    string Comprador,
    string? CompradorNit,
    ReciboCaja? Recibo,
    IReadOnlyList<LineaRemision> Lineas,
    TotalesRemision Totales,
    string Estado,
    string Clase,
    bool EsDelModulo);

public sealed record ReciboFila(
    ReciboCaja Recibo,
    string Etiqueta,
    string RemisionNumero,
    string? Oficio,
    string Cliente,
    string Tipo,
    string FormaPagoTexto);

public sealed record ResumenRemisiones(int RemisionesDelMes, decimal VentasDelMes);

public sealed record ResumenRecibos(decimal Consignacion, decimal EfectivoCheque, string UltimoConsecutivo);

/// <summary>Consultas del módulo Remisiones: bandeja, detalle, recibos y consecutivos sugeridos.</summary>
public interface IRemisionesQueryService
{
    Task<IReadOnlyList<RemisionFila>> ListarAsync(RemisionFiltro filtro, CancellationToken ct = default);

    Task<RemisionVista> ObtenerAsync(int remisionId, CancellationToken ct = default);

    /// <summary>KPI del mes de <paramref name="mes"/>: remisiones emitidas y total vendido.</summary>
    Task<ResumenRemisiones> ResumirAsync(DateTime mes, int? granjaId, int? programaId, CancellationToken ct = default);

    Task<IReadOnlyList<ReciboFila>> ListarRecibosAsync(string? formaPago = null, string? texto = null, bool incluirAnulados = false, CancellationToken ct = default);

    Task<ResumenRecibos> ResumirRecibosAsync(CancellationToken ct = default);

    /// <summary>Siguiente número de remisión sugerido («REM-413») y siguiente consecutivo de recibo SP.</summary>
    Task<(string Remision, int Recibo)> SiguientesConsecutivosAsync(CancellationToken ct = default);

    /// <summary>Remisiones de venta de un oficio (con sus recibos).</summary>
    Task<IReadOnlyList<RemisionFila>> ListarDeOficioAsync(int oficioId, CancellationToken ct = default);
}

public sealed class RemisionesQueryService(IUnitOfWorkFactory fabrica) : IRemisionesQueryService
{
    private sealed class Datos
    {
        public required IReadOnlyList<Remision> Remisiones { get; init; }
        public required ILookup<int, RemisionDetalle> Detalles { get; init; }
        public required Dictionary<int, Granja> Granjas { get; init; }
        public required Dictionary<int, Programa> Programas { get; init; }
        public required Dictionary<int, OficioVenta> Oficios { get; init; }
        public required Dictionary<int, ReciboCaja> Recibos { get; init; }
        public required Dictionary<int, Animal> Animales { get; init; }
        public required Dictionary<int, CategoriaEtapa> Categorias { get; init; }
        public required Dictionary<int, LotePoblacion> Lotes { get; init; }
        public required Dictionary<int, CatalogoProducto> Productos { get; init; }
    }

    private static async Task<Datos> CargarAsync(IUnitOfWork uow, CancellationToken ct)
    {
        var remisiones = (await uow.Remisiones.GetAllAsync(ct)).Where(r => r.TipoMovimiento is TipoRemision.Venta or TipoRemision.Traslado).ToList();
        var ids = remisiones.Select(r => r.Id).ToHashSet();
        return new Datos
        {
            Remisiones = remisiones,
            Detalles = (await uow.RemisionDetalles.GetAllAsync(ct)).Where(d => ids.Contains(d.RemisionId)).ToLookup(d => d.RemisionId),
            Granjas = (await uow.Granjas.GetAllAsync(ct)).ToDictionary(g => g.Id),
            Programas = (await uow.Programas.GetAllAsync(ct)).ToDictionary(p => p.Id),
            Oficios = (await uow.Oficios.GetAllAsync(ct)).ToDictionary(o => o.Id),
            Recibos = (await uow.Recibos.GetAllAsync(ct)).ToDictionary(r => r.RemisionId),
            Animales = (await uow.Animales.GetAllAsync(ct)).ToDictionary(a => a.Id),
            Categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id),
            Lotes = (await uow.Lotes.GetAllAsync(ct)).ToDictionary(l => l.Id),
            Productos = (await uow.Productos.GetAllAsync(ct)).ToDictionary(p => p.Id),
        };
    }

    // ---- Bandeja --------------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<RemisionFila>> ListarAsync(RemisionFiltro filtro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var d = await CargarAsync(uow, ct);
        var texto = filtro.Texto?.Trim();

        return d.Remisiones
            .Select(r => Fila(r, d))
            .Where(f => (filtro.IncluirAnuladas || !f.Anulada)
                        && (filtro.Tipo is null || f.Tipo == filtro.Tipo)
                        && (filtro.Estado is null || f.Estado == filtro.Estado)
                        && (filtro.ProgramaId is null || f.ProgramaId == filtro.ProgramaId)
                        && (filtro.Desde is null || f.Fecha.Date >= filtro.Desde.Value.Date)
                        && (filtro.Hasta is null || f.Fecha.Date <= filtro.Hasta.Value.Date)
                        && (string.IsNullOrEmpty(texto)
                            || f.Numero.Contains(texto, StringComparison.OrdinalIgnoreCase)
                            || (f.Oficio?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false)
                            || f.ClienteODestino.Contains(texto, StringComparison.OrdinalIgnoreCase)
                            || (f.Recibo?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false)))
            .Where(f => filtro.GranjaId is null || GranjaDe(d.Remisiones.First(r => r.Id == f.Id), filtro.GranjaId.Value))
            .OrderByDescending(f => f.Fecha).ThenByDescending(f => f.Id)
            .ToList();
    }

    public async Task<IReadOnlyList<RemisionFila>> ListarDeOficioAsync(int oficioId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var d = await CargarAsync(uow, ct);
        return d.Remisiones.Where(r => r.OficioId == oficioId).Select(r => Fila(r, d)).OrderBy(f => f.Fecha).ThenBy(f => f.Id).ToList();
    }

    /// <summary>La granja afecta una remisión por su origen o por su destino (un traslado llega o sale).</summary>
    private static bool GranjaDe(Remision r, int granjaId) => r.GranjaOrigenId == granjaId || r.GranjaDestinoId == granjaId;

    private static RemisionFila Fila(Remision r, Datos d)
    {
        var lineas = d.Detalles[r.Id].ToList();
        var programaId = r.ProgramaId ?? ProgramaDeLineas(lineas, d);
        var oficio = r.OficioId is { } oid ? d.Oficios.GetValueOrDefault(oid) : null;
        var total = r.Total > 0 || r.Subtotal > 0 ? r.Total : TotalesRemision.Calcular(lineas, r.DescuentoPorcentaje).Total;
        d.Recibos.TryGetValue(r.Id, out var recibo);
        var esVenta = r.TipoMovimiento == TipoRemision.Venta;

        return new RemisionFila(
            r.Id,
            r.NumeroRemision,
            r.FechaRemision,
            r.TipoMovimiento,
            oficio?.NumeroOficio,
            r.OficioId,
            programaId,
            programaId is { } pid ? d.Programas.GetValueOrDefault(pid)?.Nombre ?? "—" : "—",
            esVenta ? r.TerceroEntidad ?? "—" : $"Destino: {(r.GranjaDestinoId is { } gid ? d.Granjas.GetValueOrDefault(gid)?.Nombre : null) ?? "—"}",
            total,
            recibo is not null ? $"{recibo.NumeroRecibo} {recibo.Prefijo}" : esVenta && !string.IsNullOrWhiteSpace(r.ReciboCaja) ? r.ReciboCaja : null,
            EstadoDe(r, recibo),
            r.Anulada,
            ClaseDe(lineas),
            recibo is not null);
    }

    private static string EstadoDe(Remision r, ReciboCaja? recibo)
    {
        if (r.Anulada)
        {
            return EstadoRemision.Anulada;
        }

        if (r.TipoMovimiento == TipoRemision.Traslado)
        {
            return EstadoRemision.TrasladoInterno;
        }

        return recibo is { Anulado: false } || !string.IsNullOrWhiteSpace(r.ReciboCaja) ? EstadoRemision.Pagada : EstadoRemision.PendienteRecibo;
    }

    private static string ClaseDe(IReadOnlyList<RemisionDetalle> lineas)
        => lineas.Any(l => l.ProductoId is not null) ? "PRODUCTOS" : lineas.Any(l => l.LoteId is not null) ? "LOTES" : "ANIMALES";

    private static int? ProgramaDeLineas(IReadOnlyList<RemisionDetalle> lineas, Datos d)
    {
        var linea = lineas.FirstOrDefault();
        if (linea is null)
        {
            return null;
        }

        if (linea.AnimalId is { } aid)
        {
            return d.Animales.TryGetValue(aid, out var animal) ? d.Categorias.GetValueOrDefault(animal.CategoriaId)?.ProgramaId : null;
        }

        if (linea.LoteId is { } lid)
        {
            return d.Lotes.GetValueOrDefault(lid)?.ProgramaId;
        }

        return linea.ProductoId is { } pid ? d.Productos.GetValueOrDefault(pid)?.ProgramaId : null;
    }

    // ---- Detalle --------------------------------------------------------------------------------------------------------

    public async Task<RemisionVista> ObtenerAsync(int remisionId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var d = await CargarAsync(uow, ct);
        var r = d.Remisiones.FirstOrDefault(x => x.Id == remisionId) ?? throw Regla.Violada("La remisión no existe.");
        var fila = Fila(r, d);
        var detalles = d.Detalles[r.Id].ToList();
        d.Recibos.TryGetValue(r.Id, out var recibo);

        var lineas = detalles.Select(l => Linea(l, d)).ToList();
        var oficio = r.OficioId is { } oid ? d.Oficios.GetValueOrDefault(oid) : null;
        return new RemisionVista(
            r,
            d.Granjas.GetValueOrDefault(r.GranjaOrigenId ?? 0)?.Nombre ?? "—",
            r.GranjaDestinoId is { } gid ? d.Granjas.GetValueOrDefault(gid)?.Nombre : null,
            fila.ProgramaId,
            fila.Programa,
            oficio,
            r.TerceroEntidad ?? "—",
            r.TerceroNit,
            recibo,
            lineas,
            TotalesRemision.Calcular(detalles, r.DescuentoPorcentaje),
            fila.Estado,
            fila.Clase,
            fila.EsDelModulo);
    }

    private static LineaRemision Linea(RemisionDetalle l, Datos d)
    {
        if (l.ProductoId is { } pid)
        {
            var producto = d.Productos.GetValueOrDefault(pid);
            return new LineaRemision(
                ClaseDeLinea.Producto, null, null, pid, producto?.Codigo ?? "—", l.DescripcionItem ?? producto?.Nombre ?? "—", l.UnidadMedida ?? producto?.UnidadMedida ?? "—",
                l.CantidadProducto ?? 0m, l.ValorUnitarioTransaccion ?? 0m, l.ValorTotalTransaccion ?? 0m, null, 0m, l.ObservacionesEspecificas, null, null);
        }

        if (l.LoteId is { } lid)
        {
            var lote = d.Lotes.GetValueOrDefault(lid);
            var destino = l.LoteDestinoId is { } did && did != lid ? d.Lotes.GetValueOrDefault(did)?.CodigoLote : null;
            return new LineaRemision(
                ClaseDeLinea.Lote, null, lid, null, lote?.CodigoLote ?? "—", $"Lote {lote?.CodigoLote} ({lote?.LineaRaza})", "Cabeza", l.Cantidad,
                l.Cantidad > 0 ? (l.ValorTotalTransaccion ?? 0m) / l.Cantidad : l.ValorTotalTransaccion ?? 0m, l.ValorTotalTransaccion ?? 0m, l.PesoVendidoKg,
                l.CostoSistemaLibros, l.ObservacionesEspecificas, null, destino);
        }

        var animal = d.Animales.GetValueOrDefault(l.AnimalId ?? 0);
        var categoria = animal is null ? null : d.Categorias.GetValueOrDefault(animal.CategoriaId)?.Nombre;
        return new LineaRemision(
            ClaseDeLinea.Animal, l.AnimalId, null, null, animal?.ChapetaAmarilla ?? "—", $"{categoria} — {animal?.Raza}", "Cabeza", 1m,
            l.ValorUnitarioTransaccion ?? l.ValorTotalTransaccion ?? 0m, l.ValorTotalTransaccion ?? 0m, l.PesoMomentoKg, l.CostoSistemaLibros,
            l.ObservacionesEspecificas, l.OrientacionDestinoId, null);
    }

    // ---- KPI y recibos ----------------------------------------------------------------------------------------------------

    public async Task<ResumenRemisiones> ResumirAsync(DateTime mes, int? granjaId, int? programaId, CancellationToken ct = default)
    {
        var desde = new DateTime(mes.Year, mes.Month, 1);
        var hasta = desde.AddMonths(1).AddDays(-1);
        var filas = await ListarAsync(new RemisionFiltro(granjaId, programaId, Desde: desde, Hasta: hasta), ct);
        return new ResumenRemisiones(filas.Count, filas.Where(f => f.Tipo == TipoRemision.Venta).Sum(f => f.Total));
    }

    public async Task<IReadOnlyList<ReciboFila>> ListarRecibosAsync(string? formaPago = null, string? texto = null, bool incluirAnulados = false, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var d = await CargarAsync(uow, ct);
        var buscado = texto?.Trim();
        var porId = d.Remisiones.ToDictionary(r => r.Id);

        return d.Recibos.Values
            .Where(rc => porId.ContainsKey(rc.RemisionId) && (incluirAnulados || !rc.Anulado) && (formaPago is null || rc.FormaPago == formaPago))
            .Select(rc =>
            {
                var r = porId[rc.RemisionId];
                var oficio = r.OficioId is { } oid ? d.Oficios.GetValueOrDefault(oid)?.NumeroOficio : null;
                var cliente = r.TipoMovimiento == TipoRemision.Venta ? r.TerceroEntidad ?? "—" : $"Traslado a {(r.GranjaDestinoId is { } gid ? d.Granjas.GetValueOrDefault(gid)?.Nombre : null) ?? "—"}";
                return new ReciboFila(rc, $"{rc.NumeroRecibo} {rc.Prefijo}", r.NumeroRemision, oficio, cliente, r.TipoMovimiento, TextoFormaPago(rc.FormaPago, rc.BancoConsignacion));
            })
            .Where(f => string.IsNullOrEmpty(buscado)
                        || f.Etiqueta.Contains(buscado, StringComparison.OrdinalIgnoreCase)
                        || f.RemisionNumero.Contains(buscado, StringComparison.OrdinalIgnoreCase)
                        || (f.Oficio?.Contains(buscado, StringComparison.OrdinalIgnoreCase) ?? false)
                        || f.Cliente.Contains(buscado, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Recibo.NumeroRecibo)
            .ToList();
    }

    internal static string TextoFormaPago(string forma, string? banco) => forma switch
    {
        FormaPago.Consignacion => string.IsNullOrWhiteSpace(banco) ? "Consignación bancaria" : $"Consignación {banco}",
        FormaPago.EfectivoCheque => "Efectivo / Cheque",
        _ => "Traslado interno",
    };

    public async Task<ResumenRecibos> ResumirRecibosAsync(CancellationToken ct = default)
    {
        var recibos = await ListarRecibosAsync(ct: ct);
        return new ResumenRecibos(
            recibos.Where(r => r.Recibo.FormaPago == FormaPago.Consignacion).Sum(r => r.Recibo.TotalPagado),
            recibos.Where(r => r.Recibo.FormaPago == FormaPago.EfectivoCheque).Sum(r => r.Recibo.TotalPagado),
            recibos.Count == 0 ? "—" : recibos[0].Etiqueta);
    }

    // ---- Consecutivos -----------------------------------------------------------------------------------------------------

    public async Task<(string Remision, int Recibo)> SiguientesConsecutivosAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var mayor = (await uow.Remisiones.GetAllAsync(ct))
            .Select(r => Regex.Match(r.NumeroRemision, @"^REM-(\d+)$", RegexOptions.IgnoreCase))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();
        var config = await uow.ConfiguracionRemisiones.GetAsync(ct);
        var recibo = Math.Max(config.UltimoConsecutivoRecibo, await uow.Recibos.GetMaxNumeroAsync(ct)) + 1;
        return ($"REM-{mayor + 1}", recibo);
    }
}
