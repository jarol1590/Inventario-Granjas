using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Kardex;

public sealed partial class KardexService
{
    // ---- Venta ------------------------------------------------------------------------------------------------

    public async Task<SalidaResultado> RegistrarVentaAsync(VentaDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is null, "Esta venta ya está registrada; use la edición para modificarla.");
        validador.ValidarVenta(dto, reloj.Hoy);

        await using var uow = await fabrica.BeginAsync(ct);

        await ObtenerGranjaActivaAsync(uow, dto.GranjaOrigenId, "La granja de origen", ct);
        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, null, ct);

        var animales = await CargarAnimalesDisponiblesAsync(
            uow, dto.Animales.Select(a => a.AnimalId).ToList(), dto.GranjaOrigenId, "vender", ct);
        var oficio = await ObtenerOCrearOficioAsync(uow, dto, animales, ct);

        var remision = new Remision
        {
            NumeroRemision = numero,
            TipoMovimiento = TipoRemision.Venta,
            OficioId = oficio.Id,
            ReciboCaja = Limpiar(dto.ReciboCaja),
            GranjaOrigenId = dto.GranjaOrigenId,
            FechaRemision = dto.FechaRemision.Date,
            TerceroEntidad = Limpiar(dto.Comprador),
            Observaciones = LimpiarONulo(dto.Observaciones),
            ClaseSalida = ClaseSalida.Venta,
        };
        validador.ValidarJerarquiaDocumental(remision);
        await uow.Remisiones.InsertAsync(remision, ct);

        var totalCosto = 0m;
        var totalVenta = 0m;
        foreach (var item in dto.Animales)
        {
            var animal = animales.Single(a => a.Id == item.AnimalId);
            totalCosto += animal.ValorContableActual;
            totalVenta += item.PrecioVenta;
            await AplicarSalidaAsync(
                uow, remision, animal, EstadoAnimal.Vendido, TipoMovimiento.Venta, item.PrecioVenta, item.PesoKg, item.ObservacionesEspecificas, ct);
        }

        await uow.CommitAsync(ct);
        return new SalidaResultado(remision.Id, oficio.Id, dto.Animales.Count, totalCosto, totalVenta);
    }

    /// <summary>
    /// Nivel 1 de la jerarquía: el oficio se reutiliza si ya existe (un oficio agrupa varias remisiones) o se crea.
    /// Un oficio pertenece a un solo programa y todos los animales de sus remisiones deben ser de ese programa.
    /// </summary>
    private static async Task<OficioVenta> ObtenerOCrearOficioAsync(
        IUnitOfWork uow, VentaDto dto, IReadOnlyCollection<Animal> animales, CancellationToken ct)
    {
        var programa = await uow.Programas.GetByIdAsync(dto.ProgramaId, ct)
            ?? throw Regla.Violada("El programa del oficio de venta no existe.");
        var numero = Limpiar(dto.NumeroOficio);

        var oficio = await uow.Oficios.GetByNumeroAsync(numero, ct);
        if (oficio is null)
        {
            Regla.Exigir(dto.FechaOficio != default, $"El oficio {numero} es nuevo: indique su fecha.");
            oficio = new OficioVenta
            {
                NumeroOficio = numero,
                ProgramaId = programa.Id,
                FechaOficio = dto.FechaOficio.Date,
                Observaciones = LimpiarONulo(dto.ObservacionesOficio),
            };
            await uow.Oficios.InsertAsync(oficio, ct);
        }
        else if (oficio.ProgramaId != programa.Id)
        {
            var delOficio = await uow.Programas.GetByIdAsync(oficio.ProgramaId, ct);
            throw Regla.Violada(
                $"El oficio {numero} pertenece al programa «{delOficio?.Nombre}»: un oficio solo puede tener remisiones de un solo programa.");
        }

        var categorias = (await uow.Categorias.GetAllAsync(ct)).ToDictionary(c => c.Id);
        foreach (var animal in animales)
        {
            Regla.Exigir(
                categorias[animal.CategoriaId].ProgramaId == oficio.ProgramaId,
                $"El animal {animal.ChapetaAmarilla} no pertenece al programa del oficio {numero} ({programa.Nombre}).");
        }

        return oficio;
    }

    // ---- Baja -------------------------------------------------------------------------------------------------

    public async Task<SalidaResultado> RegistrarBajaAsync(BajaDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is null, "Esta baja ya está registrada; use la edición para modificarla.");
        validador.ValidarBaja(dto, reloj.Hoy);

        await using var uow = await fabrica.BeginAsync(ct);

        await ObtenerGranjaActivaAsync(uow, dto.GranjaOrigenId, "La granja de origen", ct);
        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, null, ct);

        var animales = await CargarAnimalesDisponiblesAsync(
            uow, dto.Animales.Select(a => a.AnimalId).ToList(), dto.GranjaOrigenId, "dar de baja", ct);

        var remision = new Remision
        {
            NumeroRemision = numero,
            TipoMovimiento = TipoRemision.Baja,
            GranjaOrigenId = dto.GranjaOrigenId,
            FechaRemision = dto.FechaBaja.Date,
            TerceroEntidad = Limpiar(dto.DocumentoSoporte),
            Observaciones = LimpiarONulo(dto.Observaciones),
            ClaseSalida = dto.ClaseSalida,
        };
        validador.ValidarJerarquiaDocumental(remision);
        await uow.Remisiones.InsertAsync(remision, ct);

        var totalCosto = 0m;
        foreach (var item in dto.Animales)
        {
            var animal = animales.Single(a => a.Id == item.AnimalId);
            totalCosto += animal.ValorContableActual;
            await AplicarSalidaAsync(
                uow, remision, animal, EstadoDeBaja(dto.ClaseSalida), MovimientoDeBaja(dto.ClaseSalida), null, null, item.ObservacionesEspecificas, ct);
        }

        await uow.CommitAsync(ct);
        return new SalidaResultado(remision.Id, null, dto.Animales.Count, totalCosto, 0m);
    }

    private static string EstadoDeBaja(string clase) => clase == ClaseSalida.Hurto ? EstadoAnimal.Hurtado : EstadoAnimal.Muerto;

    private static string MovimientoDeBaja(string clase) => clase == ClaseSalida.Hurto ? TipoMovimiento.Hurto : TipoMovimiento.Muerte;

    // ---- Traslado ---------------------------------------------------------------------------------------------

    public async Task<TrasladoResultado> RegistrarTrasladoAsync(TrasladoDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        validador.ValidarTraslado(dto, reloj.Hoy);

        await using var uow = await fabrica.BeginAsync(ct);

        var origen = await ObtenerGranjaActivaAsync(uow, dto.GranjaOrigenId, "La granja de origen", ct);
        var destino = await ObtenerGranjaActivaAsync(uow, dto.GranjaDestinoId, "La granja de destino", ct);
        var numero = Limpiar(dto.NumeroRemision);
        await ExigirNumeroLibreAsync(uow, numero, null, ct);

        var animales = await CargarAnimalesDisponiblesAsync(
            uow, dto.Animales.Select(a => a.AnimalId).ToList(), origen.Id, "trasladar", ct);
        foreach (var item in dto.Animales.Where(a => a.OrientacionDestinoId is not null))
        {
            await ExigirOrientacionAsync(uow, item.OrientacionDestinoId!.Value, ct);
        }

        // Remisión de traslado: sin Oficio ni Recibo de Caja.
        var remision = new Remision
        {
            NumeroRemision = numero,
            TipoMovimiento = TipoRemision.Traslado,
            GranjaOrigenId = origen.Id,
            GranjaDestinoId = destino.Id,
            FechaRemision = dto.FechaTraslado.Date,
            Observaciones = LimpiarONulo(dto.Observaciones),
            ClaseSalida = ClaseSalida.Traslado,
        };
        validador.ValidarJerarquiaDocumental(remision);
        await uow.Remisiones.InsertAsync(remision, ct);

        var totalValor = 0m;
        foreach (var item in dto.Animales)
        {
            var animal = animales.Single(a => a.Id == item.AnimalId);
            var costo = animal.ValorContableActual;
            var orientacionDestino = item.OrientacionDestinoId ?? animal.OrientacionId;

            await uow.RemisionDetalles.InsertAsync(new RemisionDetalle
            {
                RemisionId = remision.Id,
                AnimalId = animal.Id,
                PesoMomentoKg = PesoONulo(animal.PesoActualKg),
                CostoSistemaLibros = costo,
                ObservacionesEspecificas = LimpiarONulo(item.ObservacionesEspecificas),
                OrientacionOrigenId = animal.OrientacionId,
                OrientacionDestinoId = orientacionDestino,
            }, ct);

            // El animal conserva su chapeta y su valor de compra; solo cambian la sede y, si se confirma, la orientación.
            animal.GranjaId = destino.Id;
            animal.OrientacionId = orientacionDestino;
            await uow.Animales.UpdateAsync(animal, ct);

            // Registro dual: salida en el origen (−valor) y entrada en el destino (+valor).
            await uow.Movimientos.InsertAsync(Salida(animal.Id, remision.Id, TipoMovimiento.Traslado, remision.FechaRemision, origen.Id, costo), ct);
            await uow.Movimientos.InsertAsync(Entrada(animal.Id, remision.Id, TipoMovimiento.Traslado, remision.FechaRemision, destino.Id, costo), ct);
            totalValor += costo;
        }

        await uow.CommitAsync(ct);
        return new TrasladoResultado(remision.Id, dto.Animales.Count, totalValor);
    }

    // ---- Comunes de las salidas --------------------------------------------------------------------------------

    /// <summary>Carga los animales indicados exigiendo que existan, estén Disponibles y se encuentren en la granja de origen.</summary>
    private static async Task<IReadOnlyList<Animal>> CargarAnimalesDisponiblesAsync(
        IUnitOfWork uow, IReadOnlyCollection<int> ids, int granjaOrigenId, string accion, CancellationToken ct)
    {
        var animales = await uow.Animales.GetByIdsAsync(ids, ct);

        foreach (var id in ids)
        {
            Regla.Exigir(animales.Any(a => a.Id == id), $"El animal con id {id} no existe.");
        }

        foreach (var animal in animales)
        {
            Regla.Exigir(
                animal.Estado == EstadoAnimal.Disponible,
                $"El animal {animal.ChapetaAmarilla} no está disponible (estado: {animal.Estado}); no se puede {accion}.");
            Regla.Exigir(
                animal.GranjaId == granjaOrigenId,
                $"El animal {animal.ChapetaAmarilla} no se encuentra en la granja de origen seleccionada.");
        }

        return animales;
    }

    /// <summary>
    /// Saca un animal del inventario: línea de la remisión con su costo en libros, nuevo estado y asiento negativo
    /// en el libro por exactamente su valor contable (así el saldo del animal queda en cero).
    /// </summary>
    private async Task AplicarSalidaAsync(
        IUnitOfWork uow,
        Remision remision,
        Animal animal,
        string nuevoEstado,
        string tipoMovimiento,
        decimal? precioVenta,
        decimal? pesoKg,
        string? observacionesEspecificas,
        CancellationToken ct)
    {
        var costo = animal.ValorContableActual;

        await uow.RemisionDetalles.InsertAsync(new RemisionDetalle
        {
            RemisionId = remision.Id,
            AnimalId = animal.Id,
            PesoMomentoKg = pesoKg ?? PesoONulo(animal.PesoActualKg),
            ValorUnitarioTransaccion = precioVenta,
            ValorTotalTransaccion = precioVenta,
            CostoSistemaLibros = costo,
            ObservacionesEspecificas = LimpiarONulo(observacionesEspecificas),
        }, ct);

        animal.Estado = nuevoEstado;
        await uow.Animales.UpdateAsync(animal, ct);

        await uow.Movimientos.InsertAsync(
            Salida(animal.Id, remision.Id, tipoMovimiento, remision.FechaRemision, remision.GranjaOrigenId!.Value, costo), ct);
    }

    private static decimal? PesoONulo(decimal peso) => peso > 0 ? peso : null;
}
