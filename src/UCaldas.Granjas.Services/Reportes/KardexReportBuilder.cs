using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Reportes;

public interface IKardexReportBuilder
{
    /// <summary>
    /// Arma el Kardex financiero del mes para la granja indicada o, si <paramref name="granjaId"/> es nulo, para todas
    /// las granjas activas o con movimientos.
    /// </summary>
    Task<IReadOnlyList<KardexGranjaMensual>> ConstruirAsync(int anio, int mes, int? granjaId, CancellationToken ct = default);
}

/// <summary>Calcula el Kardex mensual a partir del libro de movimientos. No escribe nada.</summary>
public sealed class KardexReportBuilder(IUnitOfWorkFactory fabrica) : IKardexReportBuilder
{
    public async Task<IReadOnlyList<KardexGranjaMensual>> ConstruirAsync(int anio, int mes, int? granjaId, CancellationToken ct = default)
    {
        Regla.Exigir(anio is >= 2000 and <= 2100, "El año del informe debe estar entre 2000 y 2100.");
        Regla.Exigir(mes is >= 1 and <= 12, "El mes del informe debe estar entre 1 y 12.");

        await using var uow = await fabrica.OpenReadOnlyAsync(ct);

        var granjas = (await uow.Granjas.GetAllAsync(ct)).OrderBy(g => g.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
        var movimientos = await uow.Movimientos.GetAllAsync(ct);

        if (granjaId is { } id)
        {
            granjas = granjas.Where(g => g.Id == id).ToList();
            Regla.Exigir(granjas.Count == 1, "La granja seleccionada no existe.");
        }
        else
        {
            var conMovimientos = movimientos.Select(m => m.GranjaOrigenId ?? m.GranjaDestinoId).ToHashSet();
            granjas = granjas.Where(g => g.Activa || conMovimientos.Contains(g.Id)).ToList();
        }

        var animales = (await uow.Animales.GetAllAsync(ct)).ToDictionary(a => a.Id);
        var detalles = await uow.RemisionDetalles.GetAllAsync(ct);

        return granjas.Select(g => Construir(g, anio, mes, movimientos, animales, detalles)).ToList();
    }

    /// <summary>Función pura: arma el Kardex de una granja con los datos ya cargados.</summary>
    public static KardexGranjaMensual Construir(
        Granja granja,
        int anio,
        int mes,
        IReadOnlyList<MovimientoInventario> movimientos,
        IReadOnlyDictionary<int, Animal> animales,
        IReadOnlyList<RemisionDetalle> detalles)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);

        // La granja afectada por cada asiento es la columna informada (exactamente una de las dos).
        var propios = movimientos
            .Where(m => (m.GranjaOrigenId ?? m.GranjaDestinoId) == granja.Id)
            .OrderBy(m => m.FechaMovimiento).ThenBy(m => m.Id)
            .ToList();
        var previos = propios.Where(m => m.FechaMovimiento < desde).ToList();
        var delMes = propios.Where(m => m.FechaMovimiento >= desde && m.FechaMovimiento < hasta).ToList();

        // Cada asiento es un animal: las entradas suman uno y las salidas restan uno; la valorización no cambia la cantidad.
        int Cantidad(MovimientoInventario m) => m.TipoMovimiento == TipoMovimiento.Valorizacion ? 0 : m.GranjaDestinoId == granja.Id ? 1 : -1;

        var detallePorDocumento = detalles
            .Where(d => d.AnimalId is not null)
            .GroupBy(d => (d.RemisionId, d.AnimalId!.Value))
            .ToDictionary(g => g.Key, g => g.First());

        string Chapeta(MovimientoInventario m) => m.AnimalId is { } id && animales.TryGetValue(id, out var a) ? a.ChapetaAmarilla : "(sin animal)";

        decimal ValorTransaccion(MovimientoInventario m, decimal respaldo)
            => m is { RemisionId: { } r, AnimalId: { } a } && detallePorDocumento.TryGetValue((r, a), out var d) && d.ValorTotalTransaccion is { } v ? v : respaldo;

        var ventas = delMes.Where(m => m.TipoMovimiento == TipoMovimiento.Venta)
            .Select(m => new LineaVenta(Chapeta(m), -m.CostoAsociado, ValorTransaccion(m, 0m))).ToList();

        var compras = delMes.Where(m => m.TipoMovimiento == TipoMovimiento.Compra)
            .Select(m => new LineaCompra(
                Chapeta(m),
                m.CostoAsociado,
                ValorTransaccion(m, m.AnimalId is { } id && animales.TryGetValue(id, out var a) ? a.ValorCompraOrigen : 0m)))
            .ToList();

        var nacimientos = delMes.Where(m => m.TipoMovimiento == TipoMovimiento.Nacimiento)
            .Select(m => new LineaValor(Chapeta(m), m.CostoAsociado)).ToList();

        var muertes = delMes.Where(m => m.TipoMovimiento is TipoMovimiento.Muerte or TipoMovimiento.Hurto)
            .Select(m => new LineaValor(Chapeta(m), -m.CostoAsociado)).ToList();

        var llegados = delMes.Where(m => m.TipoMovimiento == TipoMovimiento.Traslado && m.GranjaDestinoId == granja.Id)
            .Select(m => new LineaValor(Chapeta(m), m.CostoAsociado)).ToList();

        var traslados = delMes.Where(m => m.TipoMovimiento == TipoMovimiento.Traslado && m.GranjaOrigenId == granja.Id)
            .Select(m => new LineaValor(Chapeta(m), -m.CostoAsociado)).ToList();

        var saldoAnteriorCantidad = previos.Sum(Cantidad);
        var saldoAnteriorValor = previos.Sum(m => m.CostoAsociado);

        return new KardexGranjaMensual
        {
            GranjaId = granja.Id,
            GranjaNombre = granja.Nombre,
            Anio = anio,
            Mes = mes,
            SaldoAnteriorCantidad = saldoAnteriorCantidad,
            SaldoAnteriorValor = saldoAnteriorValor,
            Ventas = ventas,
            Compras = compras,
            Nacimientos = nacimientos,
            Muertes = muertes,
            Llegados = llegados,
            Traslados = traslados,
            Valorizacion = delMes.Where(m => m.TipoMovimiento == TipoMovimiento.Valorizacion).Sum(m => m.CostoAsociado),
            SaldoFinalCantidad = saldoAnteriorCantidad + delMes.Sum(Cantidad),
            SaldoFinalValor = saldoAnteriorValor + delMes.Sum(m => m.CostoAsociado),
        };
    }
}
