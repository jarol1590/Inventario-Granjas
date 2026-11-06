using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.Services.Kardex;

/// <summary>
/// Movimientos de inventario. Cada operación es una sola transacción: o se registra todo (animales, documento,
/// líneas y asientos del libro) o no se registra nada.
/// <para>
/// El libro (<c>MOVIMIENTO_INVENTARIO</c>) lleva el signo en <c>costo_asociado</c>: positivo en las entradas (compra,
/// nacimiento, llegada por traslado) y negativo en las salidas (venta, baja, salida por traslado). La granja que el
/// movimiento afecta es la columna informada: destino en las entradas, origen en las salidas.
/// </para>
/// </summary>
public interface IKardexService
{
    /// <summary>Registra una cría: <c>clase_entrada = NACIMIENTO</c>, <c>valor_compra_origen = 0</c>, categoría y valor iniciales de la matriz.</summary>
    Task<NacimientoResultado> RegistrarNacimientoAsync(NacimientoDto dto, CancellationToken ct = default);

    /// <summary>Registra una compra individual o masiva: el precio de factura queda como <c>valor_compra_origen</c> (inmutable).</summary>
    Task<CompraResultado> RegistrarCompraAsync(CompraDto dto, CancellationToken ct = default);

    /// <summary>Registra una venta exigiendo Oficio de Venta, Remisión y Recibo de Caja; el animal pasa a "Vendido".</summary>
    Task<SalidaResultado> RegistrarVentaAsync(VentaDto dto, CancellationToken ct = default);

    /// <summary>Registra una baja (muerte, hurto, descarte sanitario) solo con Remisión.</summary>
    Task<SalidaResultado> RegistrarBajaAsync(BajaDto dto, CancellationToken ct = default);

    /// <summary>Traslada animales entre granjas: genera la salida en el origen y la entrada en el destino.</summary>
    Task<TrasladoResultado> RegistrarTrasladoAsync(TrasladoDto dto, CancellationToken ct = default);

    /// <summary>Edita una compra ya registrada (encabezado, animales y precios), reajustando el Kardex.</summary>
    Task<CompraResultado> EditarCompraAsync(CompraDto dto, CancellationToken ct = default);

    /// <summary>Edita una venta ya registrada (encabezado, animales y precios), reajustando el Kardex.</summary>
    Task<SalidaResultado> EditarVentaAsync(VentaDto dto, CancellationToken ct = default);

    /// <summary>Edita una baja ya registrada, reajustando el Kardex.</summary>
    Task<SalidaResultado> EditarBajaAsync(BajaDto dto, CancellationToken ct = default);

    /// <summary>
    /// Anula un documento (compra, venta, baja o traslado): revierte todos sus efectos y lo deja marcado como anulado,
    /// conservando su número. Solo si ningún animal tiene movimientos posteriores al documento.
    /// </summary>
    Task AnularAsync(int remisionId, string? motivo = null, CancellationToken ct = default);
}

public sealed partial class KardexService(
    IUnitOfWorkFactory fabrica,
    IAgingEngine aging,
    IDocumentValidationService validador,
    IReloj reloj) : IKardexService
{
    // ---- Nacimiento -------------------------------------------------------------------------------------------

    public async Task<NacimientoResultado> RegistrarNacimientoAsync(NacimientoDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        validador.ValidarNacimiento(dto, reloj.Hoy);

        await using var uow = await fabrica.BeginAsync(ct);

        var granja = await ObtenerGranjaActivaAsync(uow, dto.GranjaId, "La granja de nacimiento", ct);
        await ExigirOrientacionAsync(uow, dto.OrientacionId, ct);
        var chapeta = Limpiar(dto.ChapetaAmarilla);
        await ExigirChapetaLibreAsync(uow, chapeta, null, ct);

        // Genealogía. La madre puede no estar en el sistema (se guarda solo su chapeta); si está, debe ser coherente.
        var madreChapeta = Limpiar(dto.MadreIdentificacion);
        var madre = await uow.Animales.GetByChapetaAsync(madreChapeta, ct);
        if (madre is not null)
        {
            Regla.Exigir(madre.Sexo == Sexo.Hembra, $"La madre {madreChapeta} está registrada como macho.");
            Regla.Exigir(
                madre.FechaNacimiento.Date < dto.FechaNacimiento.Date,
                $"La madre {madreChapeta} no puede haber nacido después de la cría.");
        }

        int? padreId = null;
        if (!dto.EsInseminacion && !string.IsNullOrWhiteSpace(dto.PadreChapeta))
        {
            var padre = await uow.Animales.GetByChapetaAsync(Limpiar(dto.PadreChapeta), ct)
                ?? throw Regla.Violada($"El padre {Limpiar(dto.PadreChapeta)} no está registrado en el sistema.");
            Regla.Exigir(padre.Sexo == Sexo.Macho, $"El padre {padre.ChapetaAmarilla} está registrado como hembra.");
            padreId = padre.Id;
        }

        var animal = new Animal
        {
            ChapetaAmarilla = chapeta,
            ChapetaRoja = LimpiarONulo(dto.ChapetaRoja),
            Nombre = LimpiarONulo(dto.Nombre),
            Sexo = dto.Sexo,
            FechaNacimiento = dto.FechaNacimiento.Date,
            FechaCompra = null,
            ClaseEntrada = ClaseEntrada.Nacimiento,
            Raza = Limpiar(dto.Raza),
            MadreIdentificacion = madreChapeta,
            PesoActualKg = dto.PesoAlNacerKg,
            OrientacionId = dto.OrientacionId,
            ValorCompraOrigen = 0m,
            GranjaId = granja.Id,
            Estado = EstadoAnimal.Disponible,
            FechaRegistro = reloj.Ahora,
        };

        var valoracion = await aging.DeterminarAsync(uow, animal, reloj.Hoy, ct);
        animal.CategoriaId = valoracion.CategoriaId;
        animal.ValorContableActual = valoracion.Valor ?? 0m;

        await uow.Animales.InsertAsync(animal, ct);
        await uow.Genealogias.InsertAsync(new DatosGenealogia
        {
            AnimalId = animal.Id,
            EsInseminacion = dto.EsInseminacion,
            RazaPadreInseminacion = dto.EsInseminacion ? LimpiarONulo(dto.RazaPadre) : null,
            NombrePadreInseminacion = dto.EsInseminacion ? LimpiarONulo(dto.NombrePadre) : null,
            PadreId = padreId,
        }, ct);
        await uow.Movimientos.InsertAsync(
            Entrada(animal.Id, null, TipoMovimiento.Nacimiento, dto.FechaNacimiento, granja.Id, animal.ValorContableActual), ct);

        var madreActualizada = false;
        if (madre is not null)
        {
            madre.NumeroPartos++;
            await uow.Animales.UpdateAsync(madre, ct);
            madreActualizada = true;
        }

        await uow.CommitAsync(ct);
        return new NacimientoResultado(animal.Id, valoracion.CategoriaNombre, animal.ValorContableActual, valoracion.SinTarifa, madreActualizada);
    }

    // ---- Compra -----------------------------------------------------------------------------------------------

    public async Task<CompraResultado> RegistrarCompraAsync(CompraDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.Exigir(dto.RemisionId is null, "Esta compra ya está registrada; use la edición para modificarla.");
        validador.ValidarCompra(dto, reloj.Hoy);

        await using var uow = await fabrica.BeginAsync(ct);

        await ObtenerGranjaActivaAsync(uow, dto.GranjaDestinoId, "La granja destino", ct);
        var numero = Limpiar(dto.NumeroFactura);
        await ExigirNumeroLibreAsync(uow, numero, null, ct);
        foreach (var animal in dto.Animales)
        {
            await ExigirOrientacionAsync(uow, animal.OrientacionId, ct);
            await ExigirChapetaLibreAsync(uow, Limpiar(animal.ChapetaAmarilla), null, ct);
        }

        var remision = new Remision
        {
            NumeroRemision = numero,
            TipoMovimiento = TipoRemision.Compra,
            GranjaDestinoId = dto.GranjaDestinoId,
            FechaRemision = dto.FechaCompra.Date,
            TerceroEntidad = Limpiar(dto.Proveedor),
            TerceroNit = LimpiarONulo(dto.ProveedorNit),
            OrdenCompra = LimpiarONulo(dto.OrdenCompra),
            Observaciones = LimpiarONulo(dto.Observaciones),
        };
        validador.ValidarJerarquiaDocumental(remision);
        await uow.Remisiones.InsertAsync(remision, ct);

        var valorador = await aging.CargarValoradorAsync(uow, ct);
        var sinTarifa = new List<string>();
        var altas = new List<AltaCompra>();
        foreach (var item in dto.Animales)
        {
            altas.Add(await AltaAnimalCompradoAsync(uow, valorador, remision, item, sinTarifa, ct));
        }

        await uow.CommitAsync(ct);
        return new CompraResultado(
            remision.Id, altas.Select(a => a.AnimalId).ToList(), altas.Sum(a => a.Precio), altas.Sum(a => a.Costo), sinTarifa);
    }

    private sealed record AltaCompra(int AnimalId, decimal Precio, decimal Costo);

    /// <summary>Crea un animal comprado con su línea de remisión y su asiento de entrada.</summary>
    private async Task<AltaCompra> AltaAnimalCompradoAsync(
        IUnitOfWork uow,
        IValoradorDeTarifas valorador,
        Remision remision,
        CompraAnimalDto item,
        List<string> sinTarifa,
        CancellationToken ct)
    {
        var animal = new Animal
        {
            ChapetaAmarilla = Limpiar(item.ChapetaAmarilla),
            ChapetaRoja = LimpiarONulo(item.ChapetaRoja),
            Nombre = LimpiarONulo(item.Nombre),
            Sexo = item.Sexo,
            FechaNacimiento = item.FechaNacimiento.Date,
            FechaCompra = remision.FechaRemision.Date,
            ClaseEntrada = ClaseEntrada.Compra,
            Raza = Limpiar(item.Raza),
            PesoActualKg = item.PesoKg,
            OrientacionId = item.OrientacionId,
            ValorCompraOrigen = item.PrecioFactura,
            GranjaId = remision.GranjaDestinoId!.Value,
            Estado = EstadoAnimal.Disponible,
            NumeroPartos = item.NumeroPartos,
            FechaRegistro = reloj.Ahora,
        };

        // El valor contable sale de la matriz (igual que en los nacidos); si no hay tarifa se parte del precio de factura.
        var valoracion = valorador.Determinar(animal, reloj.Hoy);
        animal.CategoriaId = valoracion.CategoriaId;
        animal.ValorContableActual = valoracion.Valor ?? item.PrecioFactura;
        if (valoracion.SinTarifa)
        {
            sinTarifa.Add(animal.ChapetaAmarilla);
        }

        await uow.Animales.InsertAsync(animal, ct);
        await uow.RemisionDetalles.InsertAsync(new RemisionDetalle
        {
            RemisionId = remision.Id,
            AnimalId = animal.Id,
            PesoMomentoKg = item.PesoKg,
            ValorUnitarioTransaccion = item.PrecioFactura,
            ValorTotalTransaccion = item.PrecioFactura,
            CostoSistemaLibros = animal.ValorContableActual,
            ObservacionesEspecificas = LimpiarONulo(item.Observaciones),
        }, ct);
        await uow.Movimientos.InsertAsync(
            Entrada(animal.Id, remision.Id, TipoMovimiento.Compra, remision.FechaRemision, animal.GranjaId, animal.ValorContableActual), ct);

        return new AltaCompra(animal.Id, item.PrecioFactura, animal.ValorContableActual);
    }

    // ---- Ayudantes compartidos ---------------------------------------------------------------------------------

    private static string Limpiar(string? valor) => valor?.Trim() ?? string.Empty;

    private static string? LimpiarONulo(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static async Task<Granja> ObtenerGranjaActivaAsync(IUnitOfWork uow, int granjaId, string rol, CancellationToken ct)
    {
        var granja = await uow.Granjas.GetByIdAsync(granjaId, ct) ?? throw Regla.Violada($"{rol} no existe.");
        Regla.Exigir(granja.Activa, $"{rol} ({granja.Nombre}) está inactiva.");
        return granja;
    }

    private static async Task ExigirOrientacionAsync(IUnitOfWork uow, int orientacionId, CancellationToken ct)
        => Regla.Exigir(await uow.Orientaciones.GetByIdAsync(orientacionId, ct) is not null, "La orientación productiva no existe.");

    private static async Task ExigirChapetaLibreAsync(IUnitOfWork uow, string chapeta, int? excluirAnimalId, CancellationToken ct)
        => Regla.Exigir(
            !await uow.Animales.ExisteChapetaAsync(chapeta, excluirAnimalId, ct),
            $"La chapeta {chapeta} ya está registrada en el sistema.");

    private static async Task ExigirNumeroLibreAsync(IUnitOfWork uow, string numero, int? excluirRemisionId, CancellationToken ct)
        => Regla.Exigir(
            !await uow.Remisiones.ExisteNumeroAsync(numero, excluirRemisionId, ct),
            $"Ya existe un documento con el número {numero}.");

    /// <summary>Asiento de entrada: suma al saldo de la granja destino.</summary>
    private static MovimientoInventario Entrada(int? animalId, int? remisionId, string tipo, DateTime fecha, int granjaDestinoId, decimal costo) => new()
    {
        AnimalId = animalId,
        RemisionId = remisionId,
        TipoMovimiento = tipo,
        FechaMovimiento = fecha.Date,
        GranjaDestinoId = granjaDestinoId,
        CostoAsociado = costo,
    };

    /// <summary>Asiento de salida: resta del saldo de la granja origen (el costo se guarda con signo negativo).</summary>
    private static MovimientoInventario Salida(int? animalId, int? remisionId, string tipo, DateTime fecha, int granjaOrigenId, decimal costo) => new()
    {
        AnimalId = animalId,
        RemisionId = remisionId,
        TipoMovimiento = tipo,
        FechaMovimiento = fecha.Date,
        GranjaOrigenId = granjaOrigenId,
        CostoAsociado = -costo,
    };
}
