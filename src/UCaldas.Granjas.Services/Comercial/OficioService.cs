using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Comercial;

/// <summary>Totales de una remisión calculados desde sus líneas (nunca con SUM de SQL).</summary>
public readonly record struct TotalesRemision(decimal Subtotal, decimal ValorDescuento, decimal Total)
{
    /// <summary>Subtotal = suma de las líneas; descuento = subtotal × porcentaje (redondeado a pesos); total = subtotal − descuento.</summary>
    public static TotalesRemision Calcular(IEnumerable<RemisionDetalle> lineas, decimal descuentoPorcentaje)
    {
        var subtotal = lineas.Sum(l => l.ValorTotalTransaccion ?? 0m);
        var descuento = Math.Round(subtotal * descuentoPorcentaje / 100m, 0, MidpointRounding.AwayFromZero);
        return new TotalesRemision(subtotal, descuento, subtotal - descuento);
    }
}

/// <summary>Oficio con lo que lleva dentro: remisiones vigentes y lo vendido.</summary>
public sealed record OficioResumen(OficioVenta Oficio, string ProgramaNombre, int Remisiones, decimal TotalVendido);

public sealed class OficioDto
{
    /// <summary>0 al crear; el id del oficio al editarlo.</summary>
    public int OficioId { get; set; }

    public string NumeroOficio { get; set; } = string.Empty;
    public int ProgramaId { get; set; }
    public DateTime Fecha { get; set; }
    public string? Observaciones { get; set; }
}

/// <summary>
/// Oficios de venta: documento exclusivo de las ventas y de un solo programa. Un oficio se puede cerrar (no admite más
/// remisiones) y reabrir; lo ya emitido dentro de un oficio cerrado sigue siendo editable y anulable.
/// </summary>
public interface IOficioService
{
    Task<IReadOnlyList<OficioResumen>> ListarAsync(int? programaId = null, string? texto = null, CancellationToken ct = default);

    Task<OficioResumen> ObtenerAsync(int oficioId, CancellationToken ct = default);

    /// <summary>Crea un oficio (el número no se repite). Devuelve su id.</summary>
    Task<int> CrearAsync(OficioDto dto, CancellationToken ct = default);

    /// <summary>Corrige número, fecha y observaciones; el programa solo cambia si el oficio todavía no tiene remisiones.</summary>
    Task ActualizarAsync(OficioDto dto, CancellationToken ct = default);

    Task CerrarAsync(int oficioId, CancellationToken ct = default);

    Task ReabrirAsync(int oficioId, CancellationToken ct = default);
}

public sealed class OficioService(IUnitOfWorkFactory fabrica, IReloj reloj) : IOficioService
{
    public async Task<IReadOnlyList<OficioResumen>> ListarAsync(int? programaId = null, string? texto = null, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var (oficios, programas, remisiones, detalles) = await CargarAsync(uow, ct);
        var buscado = texto?.Trim();
        return oficios
            .Where(o => (programaId is null || o.ProgramaId == programaId)
                        && (string.IsNullOrEmpty(buscado)
                            || o.NumeroOficio.Contains(buscado, StringComparison.OrdinalIgnoreCase)
                            || (o.Observaciones?.Contains(buscado, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderByDescending(o => o.FechaOficio).ThenByDescending(o => o.Id)
            .Select(o => Resumir(o, programas, remisiones, detalles))
            .ToList();
    }

    public async Task<OficioResumen> ObtenerAsync(int oficioId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var (oficios, programas, remisiones, detalles) = await CargarAsync(uow, ct);
        var oficio = oficios.FirstOrDefault(o => o.Id == oficioId) ?? throw Regla.Violada("El oficio no existe.");
        return Resumir(oficio, programas, remisiones, detalles);
    }

    public async Task<int> CrearAsync(OficioDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var numero = Validar(dto);

        await using var uow = await fabrica.BeginAsync(ct);
        Regla.Exigir(await uow.Programas.GetByIdAsync(dto.ProgramaId, ct) is not null, "El programa del oficio no existe.");
        Regla.Exigir(await uow.Oficios.GetByNumeroAsync(numero, ct) is null, $"Ya existe un oficio con el número {numero}.");

        var oficio = new OficioVenta
        {
            NumeroOficio = numero,
            ProgramaId = dto.ProgramaId,
            FechaOficio = dto.Fecha.Date,
            Observaciones = Limpio(dto.Observaciones),
        };
        await uow.Oficios.InsertAsync(oficio, ct);
        await uow.CommitAsync(ct);
        return oficio.Id;
    }

    public async Task ActualizarAsync(OficioDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var numero = Validar(dto);

        await using var uow = await fabrica.BeginAsync(ct);
        var oficio = await uow.Oficios.GetByIdAsync(dto.OficioId, ct) ?? throw Regla.Violada("El oficio no existe.");
        var otro = await uow.Oficios.GetByNumeroAsync(numero, ct);
        Regla.Exigir(otro is null || otro.Id == oficio.Id, $"Ya existe un oficio con el número {numero}.");

        if (dto.ProgramaId != oficio.ProgramaId)
        {
            Regla.Exigir(await uow.Programas.GetByIdAsync(dto.ProgramaId, ct) is not null, "El programa del oficio no existe.");
            Regla.Exigir(
                !(await uow.Remisiones.GetAllAsync(ct)).Any(r => r.OficioId == oficio.Id),
                "El oficio ya tiene remisiones: no se le puede cambiar el programa.");
            oficio.ProgramaId = dto.ProgramaId;
        }

        oficio.NumeroOficio = numero;
        oficio.FechaOficio = dto.Fecha.Date;
        oficio.Observaciones = Limpio(dto.Observaciones);
        await uow.Oficios.UpdateAsync(oficio, ct);
        await uow.CommitAsync(ct);
    }

    public async Task CerrarAsync(int oficioId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var oficio = await uow.Oficios.GetByIdAsync(oficioId, ct) ?? throw Regla.Violada("El oficio no existe.");
        Regla.Exigir(!oficio.Cerrado, $"El oficio {oficio.NumeroOficio} ya está cerrado.");
        oficio.Cerrado = true;
        oficio.FechaCierre = reloj.Hoy;
        await uow.Oficios.UpdateAsync(oficio, ct);
        await uow.CommitAsync(ct);
    }

    public async Task ReabrirAsync(int oficioId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var oficio = await uow.Oficios.GetByIdAsync(oficioId, ct) ?? throw Regla.Violada("El oficio no existe.");
        Regla.Exigir(oficio.Cerrado, $"El oficio {oficio.NumeroOficio} ya está abierto.");
        oficio.Cerrado = false;
        oficio.FechaCierre = null;
        await uow.Oficios.UpdateAsync(oficio, ct);
        await uow.CommitAsync(ct);
    }

    private string Validar(OficioDto dto)
    {
        var numero = (dto.NumeroOficio ?? string.Empty).Trim();
        Regla.ExigirTexto(numero, "El número del oficio");
        Regla.Exigir(dto.ProgramaId > 0, "Seleccione el programa del oficio.");
        Regla.Exigir(dto.Fecha != default && dto.Fecha.Date <= reloj.Hoy, "La fecha del oficio es obligatoria y no puede ser futura.");
        return numero;
    }

    private static async Task<(IReadOnlyList<OficioVenta>, Dictionary<int, string>, IReadOnlyList<Remision>, ILookup<int, RemisionDetalle>)> CargarAsync(
        IUnitOfWork uow, CancellationToken ct)
    {
        var oficios = await uow.Oficios.GetAllAsync(ct);
        var programas = (await uow.Programas.GetAllAsync(ct)).ToDictionary(p => p.Id, p => p.Nombre);
        var remisiones = (await uow.Remisiones.GetAllAsync(ct)).Where(r => r.OficioId is not null && !r.Anulada).ToList();
        var ids = remisiones.Select(r => r.Id).ToHashSet();
        var detalles = (await uow.RemisionDetalles.GetAllAsync(ct)).Where(d => ids.Contains(d.RemisionId)).ToLookup(d => d.RemisionId);
        return (oficios, programas, remisiones, detalles);
    }

    private static OficioResumen Resumir(
        OficioVenta oficio, Dictionary<int, string> programas, IReadOnlyList<Remision> remisiones, ILookup<int, RemisionDetalle> detalles)
    {
        var delOficio = remisiones.Where(r => r.OficioId == oficio.Id).ToList();
        var total = delOficio.Sum(r => TotalesRemision.Calcular(detalles[r.Id], r.DescuentoPorcentaje).Total);
        return new OficioResumen(oficio, programas.GetValueOrDefault(oficio.ProgramaId, "—"), delOficio.Count, total);
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
