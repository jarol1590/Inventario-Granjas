using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.Services.Animales;

/// <summary>Un hecho de la vida del animal: ingreso, traslado, valorización, salida...</summary>
/// <param name="Valor">Valor del asiento (con signo para valorizaciones; absoluto para los traslados).</param>
public sealed record EventoFicha(
    DateTime Fecha,
    string Tipo,
    string Descripcion,
    string? GranjaOrigen,
    string? GranjaDestino,
    string? NumeroRemision,
    decimal? Valor);

/// <summary>Ficha técnica de un bovino: datos básicos, doble valoración, genealogía e historial.</summary>
public sealed class FichaAnimal
{
    public required Animal Animal { get; init; }

    public required string GranjaNombre { get; init; }

    public required string OrientacionNombre { get; init; }

    public required string CategoriaNombre { get; init; }

    public required int EdadMeses { get; init; }

    public DatosGenealogia? Genealogia { get; init; }

    public string? PadreChapeta { get; init; }

    /// <summary>Suma de las valorizaciones registradas desde el ingreso del animal.</summary>
    public decimal VariacionAcumulada { get; init; }

    public IReadOnlyList<EventoFicha> Historial { get; init; } = [];
}

public interface IFichaService
{
    Task<FichaAnimal> ObtenerAsync(int animalId, CancellationToken ct = default);
}

public sealed class FichaService(IUnitOfWorkFactory fabrica, IReloj reloj) : IFichaService
{
    public async Task<FichaAnimal> ObtenerAsync(int animalId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);

        var animal = await uow.Animales.GetByIdAsync(animalId, ct) ?? throw Regla.Violada("El animal no existe.");
        var granjas = (await uow.Granjas.GetAllAsync(ct)).ToDictionary(g => g.Id, g => g.Nombre);
        var orientaciones = (await uow.Orientaciones.GetAllAsync(ct)).ToDictionary(o => o.Id, o => o.Nombre);
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id, c => c.Nombre);

        var genealogia = await uow.Genealogias.GetByAnimalIdAsync(animalId, ct);
        string? padreChapeta = null;
        if (genealogia?.PadreId is { } padreId)
        {
            padreChapeta = (await uow.Animales.GetByIdAsync(padreId, ct))?.ChapetaAmarilla;
        }

        var movimientos = await uow.Movimientos.GetByAnimalAsync(animalId, ct);
        var remisiones = new Dictionary<int, Remision>();
        foreach (var id in movimientos.Where(m => m.RemisionId is not null).Select(m => m.RemisionId!.Value).Distinct())
        {
            if (await uow.Remisiones.GetByIdAsync(id, ct) is { } remision)
            {
                remisiones[id] = remision;
            }
        }

        string? NombreGranja(int? id) => id is { } g && granjas.TryGetValue(g, out var n) ? n : null;

        var historial = new List<EventoFicha>();
        var trasladosVistos = new HashSet<int>();
        foreach (var m in movimientos)
        {
            remisiones.TryGetValue(m.RemisionId ?? 0, out var remision);

            if (m.TipoMovimiento == TipoMovimiento.Traslado)
            {
                // Un traslado deja dos asientos (salida y entrada); en la ficha es un solo evento Origen → Destino.
                if (remision is null || !trasladosVistos.Add(remision.Id))
                {
                    continue;
                }

                historial.Add(new EventoFicha(
                    m.FechaMovimiento, "Traslado", remision.Observaciones ?? "Traslado entre granjas",
                    NombreGranja(remision.GranjaOrigenId), NombreGranja(remision.GranjaDestinoId),
                    remision.NumeroRemision, Math.Abs(m.CostoAsociado)));
                continue;
            }

            historial.Add(new EventoFicha(
                m.FechaMovimiento,
                m.TipoMovimiento,
                Describir(m, remision),
                NombreGranja(m.GranjaOrigenId),
                NombreGranja(m.GranjaDestinoId),
                remision?.NumeroRemision,
                m.CostoAsociado));
        }

        var referencia = reloj.Hoy < animal.FechaNacimiento.Date ? animal.FechaNacimiento.Date : reloj.Hoy;

        return new FichaAnimal
        {
            Animal = animal,
            GranjaNombre = granjas.GetValueOrDefault(animal.GranjaId, "—"),
            OrientacionNombre = orientaciones.GetValueOrDefault(animal.OrientacionId, "—"),
            CategoriaNombre = categorias.GetValueOrDefault(animal.CategoriaId, "—"),
            EdadMeses = EdadCalculator.Meses(animal.FechaNacimiento, referencia),
            Genealogia = genealogia,
            PadreChapeta = padreChapeta,
            VariacionAcumulada = movimientos.Where(m => m.TipoMovimiento == TipoMovimiento.Valorizacion).Sum(m => m.CostoAsociado),
            Historial = historial,
        };
    }

    private static string Describir(MovimientoInventario m, Remision? remision) => m.TipoMovimiento switch
    {
        TipoMovimiento.Nacimiento => "Nacimiento en la granja",
        TipoMovimiento.Compra => $"Compra a {remision?.TerceroEntidad ?? "proveedor"}",
        TipoMovimiento.Venta => $"Venta a {remision?.TerceroEntidad ?? "comprador"}",
        TipoMovimiento.Muerte => remision?.ClaseSalida == ClaseSalida.DescarteSanitario ? "Descarte sanitario" : "Muerte",
        TipoMovimiento.Hurto => "Hurto",
        TipoMovimiento.Valorizacion => m.CostoAsociado >= 0 ? "Valorización por cambio de etapa" : "Desvalorización por cambio de etapa",
        _ => m.TipoMovimiento,
    };
}
