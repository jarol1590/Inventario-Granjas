using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Valorizacion;

/// <summary>Categoría y valor que la matriz activa le asigna a un animal en una fecha.</summary>
/// <param name="Valor">Nulo cuando ninguna tarifa aplica (<see cref="SinTarifa"/>).</param>
public sealed record ResultadoValorizacion(int EdadMeses, int CategoriaId, string CategoriaNombre, decimal? Valor, int? TarifaId)
{
    /// <summary>Ninguna tarifa de la vigencia activa aplica: el animal queda como "Sin clasificar".</summary>
    public bool SinTarifa => TarifaId is null;
}

/// <summary>Resultado de valorizar el inventario a una fecha de corte.</summary>
public sealed record ResumenValorizacion(
    DateTime FechaCorte,
    int AnimalesEvaluados,
    int AnimalesConCambioDeValor,
    int CambiosDeCategoria,
    decimal DeltaTotal,
    IReadOnlyList<string> SinTarifa);

/// <summary>Matriz de la vigencia activa ya cargada: permite valorar muchos animales sin volver a consultar la base.</summary>
public interface IValoradorDeTarifas
{
    ResultadoValorizacion Determinar(Animal animal, DateTime fechaReferencia);
}

public interface IAgingEngine
{
    /// <summary>Edad en meses cumplidos a una fecha.</summary>
    int CalcularEdadMeses(DateTime fechaNacimiento, DateTime fechaReferencia);

    /// <summary>Carga la matriz activa una vez para valorar varios animales (compras masivas).</summary>
    Task<IValoradorDeTarifas> CargarValoradorAsync(IUnitOfWork uow, CancellationToken ct = default);

    /// <summary>
    /// Calcula, sin guardar nada, la categoría y el valor que la matriz activa asigna al animal a esa fecha.
    /// Pensado para usarse dentro de la transacción de otro servicio (alta de nacimientos y compras).
    /// </summary>
    Task<ResultadoValorizacion> DeterminarAsync(IUnitOfWork uow, Animal animal, DateTime fechaReferencia, CancellationToken ct = default);

    /// <summary>Valoriza todos los animales Disponibles a la fecha de corte dentro de la transacción recibida (no hace commit).</summary>
    Task<ResumenValorizacion> ValorizarAlCorteAsync(IUnitOfWork uow, DateTime fechaCorte, CancellationToken ct = default);

    /// <summary>Valoriza a la fecha de corte en su propia transacción.</summary>
    Task<ResumenValorizacion> ValorizarAlCorteAsync(DateTime fechaCorte, CancellationToken ct = default);

    /// <summary>
    /// Procesa en orden cada fin de mes ya cumplido y aún no valorizado (se llama al abrir la aplicación).
    /// Todos los cierres se aplican en una sola transacción.
    /// </summary>
    Task<IReadOnlyList<ResumenValorizacion>> ProcesarCierresPendientesAsync(CancellationToken ct = default);
}

/// <summary>
/// Motor de envejecimiento y valorización. Recalcula categoría y valor contable de los animales Disponibles con la
/// matriz de la vigencia activa y registra la diferencia como movimiento "Valorizacion" en el Kardex.
/// </summary>
public sealed class AgingEngine(IUnitOfWorkFactory fabrica, IReloj reloj) : IAgingEngine
{
    private sealed record Contexto(
        VigenciaValorizacion Vigencia,
        IReadOnlyList<TablaValorizacionBase> Tarifas,
        IReadOnlyDictionary<int, CategoriaEtapa> Categorias,
        CategoriaEtapa SinClasificar);

    private sealed class Valorador(Contexto contexto) : IValoradorDeTarifas
    {
        public ResultadoValorizacion Determinar(Animal animal, DateTime fechaReferencia)
            => AgingEngine.Determinar(contexto, animal, fechaReferencia);
    }

    public int CalcularEdadMeses(DateTime fechaNacimiento, DateTime fechaReferencia)
        => EdadCalculator.Meses(fechaNacimiento, fechaReferencia);

    public async Task<IValoradorDeTarifas> CargarValoradorAsync(IUnitOfWork uow, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uow);
        return new Valorador(await CargarContextoAsync(uow, ct));
    }

    public async Task<ResultadoValorizacion> DeterminarAsync(
        IUnitOfWork uow, Animal animal, DateTime fechaReferencia, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(animal);

        var contexto = await CargarContextoAsync(uow, ct);
        return Determinar(contexto, animal, fechaReferencia);
    }

    public async Task<ResumenValorizacion> ValorizarAlCorteAsync(DateTime fechaCorte, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var resumen = await ValorizarAlCorteAsync(uow, fechaCorte, ct);
        await uow.CommitAsync(ct);
        return resumen;
    }

    public async Task<ResumenValorizacion> ValorizarAlCorteAsync(IUnitOfWork uow, DateTime fechaCorte, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uow);

        var corte = fechaCorte.Date;
        Regla.Exigir(corte <= reloj.Hoy, $"No se puede valorizar a una fecha futura ({corte:dd/MM/yyyy}).");

        var configuracion = await uow.Configuracion.GetSistemaAsync(ct);
        var ultimoCierre = configuracion?.UltimoCierreValorizacion?.Date;
        if (ultimoCierre is { } ultimo)
        {
            Regla.Exigir(
                corte >= ultimo,
                $"La fecha de corte ({corte:dd/MM/yyyy}) es anterior al último cierre de valorización ({ultimo:dd/MM/yyyy}). " +
                "Los cierres deben hacerse en orden cronológico.");
        }

        var contexto = await CargarContextoAsync(uow, ct);
        var animales = await uow.Animales.GetByEstadoAsync(EstadoAnimal.Disponible, ct);

        var evaluados = 0;
        var conCambioDeValor = 0;
        var cambiosDeCategoria = 0;
        var deltaTotal = 0m;
        var sinTarifa = new List<string>();

        foreach (var animal in animales)
        {
            // Un animal registrado (o nacido) después del corte todavía no formaba parte del inventario a esa fecha.
            if (animal.FechaRegistro.Date > corte || animal.FechaNacimiento.Date > corte)
            {
                continue;
            }

            evaluados++;
            var resultado = Determinar(contexto, animal, corte);

            // Sin tarifa o exento: se conserva el valor contable; el exento sí actualiza su categoría.
            var nuevoValor = animal.ValorContableActual;
            if (resultado.SinTarifa)
            {
                sinTarifa.Add(animal.ChapetaAmarilla);
            }
            else if (!animal.ExentoValorizacion)
            {
                nuevoValor = resultado.Valor!.Value;
            }

            var delta = nuevoValor - animal.ValorContableActual;
            var cambiaCategoria = resultado.CategoriaId != animal.CategoriaId;
            if (cambiaCategoria)
            {
                cambiosDeCategoria++;
            }

            if (delta == 0m && !cambiaCategoria)
            {
                continue;
            }

            await uow.Animales.ActualizarValoracionAsync(animal.Id, resultado.CategoriaId, nuevoValor, ct);

            if (delta != 0m)
            {
                conCambioDeValor++;
                deltaTotal += delta;
                await uow.Movimientos.InsertAsync(new MovimientoInventario
                {
                    AnimalId = animal.Id,
                    TipoMovimiento = TipoMovimiento.Valorizacion,
                    FechaMovimiento = corte,
                    GranjaDestinoId = animal.GranjaId,
                    CostoAsociado = delta,
                }, ct);
            }
        }

        if (configuracion is not null)
        {
            configuracion.UltimoCierreValorizacion = ultimoCierre is { } u && u > corte ? u : corte;
            await uow.Configuracion.GuardarSistemaAsync(configuracion, ct);
        }

        return new ResumenValorizacion(corte, evaluados, conCambioDeValor, cambiosDeCategoria, deltaTotal, sinTarifa);
    }

    public async Task<IReadOnlyList<ResumenValorizacion>> ProcesarCierresPendientesAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);

        var configuracion = await uow.Configuracion.GetSistemaAsync(ct);
        if (configuracion is null)
        {
            return []; // Primer arranque: todavía no hay contraseña ni historia que valorizar.
        }

        var hoy = reloj.Hoy;
        if (configuracion.UltimoCierreValorizacion is null)
        {
            // Sin historial: se parte del último fin de mes ya terminado para no inventar valorizaciones pasadas.
            configuracion.UltimoCierreValorizacion = EdadCalculator.FinDeMes(hoy.AddMonths(-1));
            await uow.Configuracion.GuardarSistemaAsync(configuracion, ct);
        }

        var resumenes = new List<ResumenValorizacion>();
        var corte = EdadCalculator.FinDeMes(configuracion.UltimoCierreValorizacion.Value.Date.AddDays(1));
        while (corte <= hoy)
        {
            resumenes.Add(await ValorizarAlCorteAsync(uow, corte, ct));
            corte = EdadCalculator.FinDeMes(corte.AddDays(1));
        }

        await uow.CommitAsync(ct);
        return resumenes;
    }

    private static ResultadoValorizacion Determinar(Contexto contexto, Animal animal, DateTime fechaReferencia)
    {
        var edad = EdadCalculator.Meses(animal.FechaNacimiento, fechaReferencia);
        var tarifa = TarifaSelector.Seleccionar(contexto.Tarifas, animal.Sexo, animal.OrientacionId, animal.NumeroPartos, edad);

        return tarifa is null
            ? new ResultadoValorizacion(edad, contexto.SinClasificar.Id, contexto.SinClasificar.Nombre, null, null)
            : new ResultadoValorizacion(edad, tarifa.CategoriaId, contexto.Categorias[tarifa.CategoriaId].Nombre, tarifa.ValorEstandar, tarifa.Id);
    }

    private static async Task<Contexto> CargarContextoAsync(IUnitOfWork uow, CancellationToken ct)
    {
        var vigencia = await uow.Vigencias.GetActivaAsync(ct)
            ?? throw Regla.Violada("No hay una vigencia de valorización activa. Active una en Configuración > Tarifas de valorización.");

        var tarifas = await uow.Tarifas.GetByVigenciaAsync(vigencia.Id, ct);
        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id);
        var sinClasificar = categorias.Values.FirstOrDefault(
                c => c.Nombre.Equals(NombresBase.CategoriaSinClasificar, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Falta la categoría «{NombresBase.CategoriaSinClasificar}» en la base de datos.");

        return new Contexto(vigencia, tarifas, categorias, sinClasificar);
    }
}
