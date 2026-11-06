using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Valorizacion;

public interface IVigenciaService
{
    /// <summary>Todas las vigencias, la más reciente primero.</summary>
    Task<IReadOnlyList<VigenciaValorizacion>> ListarAsync(CancellationToken ct = default);

    /// <summary>Filas de la matriz de una vigencia, ordenadas como se muestran en pantalla (categoría, edad mínima, sexo).</summary>
    Task<IReadOnlyList<TablaValorizacionBase>> GetMatrizAsync(int vigenciaId, CancellationToken ct = default);

    /// <summary>Crea una vigencia vacía e inactiva para el año indicado.</summary>
    Task<VigenciaValorizacion> CrearAsync(int anio, string? observaciones = null, CancellationToken ct = default);

    /// <summary>Crea la vigencia del año destino con una copia de las tarifas de la vigencia origen.</summary>
    Task<VigenciaValorizacion> ClonarAsync(int vigenciaOrigenId, int anioDestino, CancellationToken ct = default);

    /// <summary>Valida (sin solapamientos) y reemplaza todas las filas de la vigencia.</summary>
    Task GuardarMatrizAsync(int vigenciaId, IReadOnlyList<TablaValorizacionBase> filas, CancellationToken ct = default);

    /// <summary>Valida la matriz guardada y la convierte en la vigencia activa (las demás se desactivan).</summary>
    Task ActivarAsync(int vigenciaId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve copias de las filas con su valor base incrementado (o reducido) en el porcentaje indicado,
    /// redondeado al peso. No guarda nada: la pantalla lo muestra y el usuario decide si guarda.
    /// </summary>
    IReadOnlyList<TablaValorizacionBase> AplicarReajustePorcentual(IReadOnlyList<TablaValorizacionBase> filas, decimal porcentaje);
}

public sealed class VigenciaService(IUnitOfWorkFactory fabrica, ITarifaValidationService validador) : IVigenciaService
{
    private const int AnioMinimo = 2000;
    private const int AnioMaximo = 2100;

    public async Task<IReadOnlyList<VigenciaValorizacion>> ListarAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return (await uow.Vigencias.GetAllAsync(ct)).OrderByDescending(v => v.Anio).ToList();
    }

    public async Task<IReadOnlyList<TablaValorizacionBase>> GetMatrizAsync(int vigenciaId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var filas = await uow.Tarifas.GetByVigenciaAsync(vigenciaId, ct);
        return filas
            .OrderBy(f => f.CategoriaId)
            .ThenBy(f => f.EdadMinimaMeses)
            .ThenBy(f => f.Sexo, StringComparer.Ordinal)
            .ThenBy(f => f.OrientacionId ?? 0)
            .ThenBy(f => f.Id)
            .ToList();
    }

    public async Task<VigenciaValorizacion> CrearAsync(int anio, string? observaciones = null, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var vigencia = await CrearInternoAsync(uow, anio, observaciones, ct);
        await uow.CommitAsync(ct);
        return vigencia;
    }

    public async Task<VigenciaValorizacion> ClonarAsync(int vigenciaOrigenId, int anioDestino, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);

        var origen = await uow.Vigencias.GetByIdAsync(vigenciaOrigenId, ct)
            ?? throw Regla.Violada("La vigencia que se quiere clonar no existe.");
        var filas = await uow.Tarifas.GetByVigenciaAsync(vigenciaOrigenId, ct);

        var nueva = await CrearInternoAsync(uow, anioDestino, $"Copia de la vigencia {origen.Anio}", ct);
        await uow.Tarifas.ReemplazarVigenciaAsync(nueva.Id, filas.Select(Copiar), ct);

        await uow.CommitAsync(ct);
        return nueva;
    }

    public async Task GuardarMatrizAsync(int vigenciaId, IReadOnlyList<TablaValorizacionBase> filas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filas);

        await using var uow = await fabrica.BeginAsync(ct);
        Regla.Exigir(await uow.Vigencias.GetByIdAsync(vigenciaId, ct) is not null, "La vigencia no existe.");

        var copias = filas.Select(Copiar).ToList();
        foreach (var fila in copias)
        {
            fila.VigenciaId = vigenciaId;
        }

        await ValidarAsync(uow, copias, ct);
        await uow.Tarifas.ReemplazarVigenciaAsync(vigenciaId, copias, ct);
        await uow.CommitAsync(ct);
    }

    public async Task ActivarAsync(int vigenciaId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);

        var vigencia = await uow.Vigencias.GetByIdAsync(vigenciaId, ct) ?? throw Regla.Violada("La vigencia no existe.");
        var filas = await uow.Tarifas.GetByVigenciaAsync(vigenciaId, ct);
        Regla.Exigir(filas.Count > 0, $"La vigencia {vigencia.Anio} no tiene tarifas. Agregue al menos una antes de activarla.");

        await ValidarAsync(uow, filas, ct);
        await uow.Vigencias.ActivarAsync(vigenciaId, ct);
        await uow.CommitAsync(ct);
    }

    public IReadOnlyList<TablaValorizacionBase> AplicarReajustePorcentual(IReadOnlyList<TablaValorizacionBase> filas, decimal porcentaje)
    {
        ArgumentNullException.ThrowIfNull(filas);
        Regla.Exigir(porcentaje > -100m, "El porcentaje de reajuste debe ser mayor que -100 %.");

        var factor = 1m + (porcentaje / 100m);
        return filas
            .Select(f =>
            {
                var copia = Copiar(f);
                copia.ValorEstandar = Math.Round(f.ValorEstandar * factor, 0, MidpointRounding.AwayFromZero);
                return copia;
            })
            .ToList();
    }

    private async Task<VigenciaValorizacion> CrearInternoAsync(IUnitOfWork uow, int anio, string? observaciones, CancellationToken ct)
    {
        Regla.Exigir(anio is >= AnioMinimo and <= AnioMaximo, $"El año debe estar entre {AnioMinimo} y {AnioMaximo}.");
        Regla.Exigir(await uow.Vigencias.GetByAnioAsync(anio, ct) is null, $"Ya existe una vigencia para el año {anio}.");

        var vigencia = new VigenciaValorizacion { Anio = anio, Activa = false, Observaciones = observaciones };
        await uow.Vigencias.InsertAsync(vigencia, ct);
        return vigencia;
    }

    private async Task ValidarAsync(IUnitOfWork uow, IReadOnlyCollection<TablaValorizacionBase> filas, CancellationToken ct)
    {
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id);
        var orientaciones = (await uow.Orientaciones.GetAllAsync(ct)).ToDictionary(o => o.Id);
        validador.ValidarMatriz(filas, categorias, orientaciones);
    }

    private static TablaValorizacionBase Copiar(TablaValorizacionBase f) => new()
    {
        Id = 0,
        VigenciaId = f.VigenciaId,
        CategoriaId = f.CategoriaId,
        OrientacionId = f.OrientacionId,
        EdadMinimaMeses = f.EdadMinimaMeses,
        EdadMaximaMeses = f.EdadMaximaMeses,
        Sexo = f.Sexo,
        RequierePartos = f.RequierePartos,
        ValorEstandar = f.ValorEstandar,
    };
}
