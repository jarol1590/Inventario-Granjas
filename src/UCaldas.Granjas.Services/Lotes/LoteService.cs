using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Lotes;

/// <summary>Un lote con los nombres que muestra la grilla.</summary>
public sealed record LoteDetalle(LotePoblacion Lote, string GranjaNombre, string EtapaNombre);

/// <summary>Totales de los lotes que se están viendo (KPIs de la cabecera).</summary>
public sealed record ResumenLotes(int PoblacionViva, int MortandadAcumulada, int PoblacionInicial, decimal ValorContable, int LotesActivos)
{
    /// <summary>Bajas acumuladas sobre la población con que iniciaron los lotes (0 si no hay población).</summary>
    public decimal PorcentajeMortandad => PoblacionInicial == 0 ? 0m : Math.Round(MortandadAcumulada * 100m / PoblacionInicial, 2);

    public static ResumenLotes De(IEnumerable<LotePoblacion> lotes)
    {
        var lista = lotes.ToList();
        return new ResumenLotes(
            lista.Sum(l => l.CantidadActual),
            lista.Sum(l => l.MortandadAcumulada),
            lista.Sum(l => l.CantidadInicial),
            lista.Sum(l => l.ValorTotalContable),   // suma en C#, nunca en SQL
            lista.Count(l => l.Estado == EstadoLote.Activo));
    }
}

public sealed class NuevoLoteDto
{
    public int ProgramaId { get; set; }
    public int GranjaId { get; set; }
    public int EtapaId { get; set; }

    /// <summary>COMPRA o NACIMIENTO.</summary>
    public string ClaseEntrada { get; set; } = string.Empty;

    public string CodigoLote { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;
    public string LineaRaza { get; set; } = string.Empty;
    public DateTime FechaIngreso { get; set; }

    /// <summary>false = estanque sin conteo de individuos (las cantidades no aplican).</summary>
    public bool ControlNumericoPoblacion { get; set; } = true;

    public int Cantidad { get; set; }

    /// <summary>Compra: precio de factura por cabeza (o por lote si no hay conteo). Se ignora en los nacimientos.</summary>
    public decimal PrecioUnitarioFactura { get; set; }

    public string? Factura { get; set; }
    public string? OrdenCompra { get; set; }
    public string? Proveedor { get; set; }

    /// <summary>Nacimiento: acta de incubación/parición o chapeta de la madre (opcional).</summary>
    public string? OrigenReferencia { get; set; }

    public string? Observaciones { get; set; }
}

/// <summary>Corrección de la compra de un lote (factura y datos del lote; cantidad y precio solo si el lote no tuvo operaciones).</summary>
public sealed class EditarCompraLoteDto
{
    public int LoteId { get; set; }
    public string Factura { get; set; } = string.Empty;
    public string OrdenCompra { get; set; } = string.Empty;
    public string Proveedor { get; set; } = string.Empty;
    public DateTime FechaIngreso { get; set; }
    public int GranjaId { get; set; }
    public string Ubicacion { get; set; } = string.Empty;
    public string LineaRaza { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public decimal PrecioUnitarioFactura { get; set; }
    public string? Observaciones { get; set; }
}

/// <summary>Corrección de los datos descriptivos de un lote desde su ficha (nunca cantidades ni valores).</summary>
public sealed class ActualizarLoteDto
{
    public int LoteId { get; set; }
    public string CodigoLote { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;
    public string LineaRaza { get; set; } = string.Empty;
    public DateTime FechaIngreso { get; set; }
    public string? OrigenReferencia { get; set; }
    public string? Proveedor { get; set; }
    public string? OrdenCompra { get; set; }
    public string? Observaciones { get; set; }

    // Población y valorización (el valor de compra nunca se cambia desde la ficha).
    public int GranjaId { get; set; }
    public int EtapaId { get; set; }
    public int CantidadInicial { get; set; }
    public int MortandadAcumulada { get; set; }
    public decimal ValorContableUnitario { get; set; }
}

public sealed record CambioEtapaResultado(decimal ValorAnteriorLote, decimal ValorNuevoLote, bool SinValorDeReferencia)
{
    /// <summary>Ajuste de valorización del lote: (valor unitario nuevo − anterior) × cantidad.</summary>
    public decimal Delta => ValorNuevoLote - ValorAnteriorLote;
}

/// <summary>
/// Inventario de animales vivos manejados en grupo (aves, peces, cerdos). Los valores salen de los valores de referencia
/// por etapa (<see cref="TarifaCategoriaPrograma"/>). IMPORTANTE: todavía no escribe en el Kardex (<c>MOVIMIENTO_INVENTARIO</c>);
/// cada operación deja una línea en las observaciones del lote mientras se define cómo se asentarán los lotes.
/// </summary>
public interface ILoteService
{
    /// <summary>Lotes del programa. Los lotes con la compra anulada solo se incluyen si se piden.</summary>
    Task<IReadOnlyList<LoteDetalle>> ListarAsync(int programaId, bool incluirAnulados = false, CancellationToken ct = default);

    Task<LoteDetalle> ObtenerAsync(int loteId, CancellationToken ct = default);

    Task<int> RegistrarNuevoLoteAsync(NuevoLoteDto dto, CancellationToken ct = default);

    /// <summary>
    /// Corrige la compra de un lote. Los datos de la factura y del lote siempre se pueden corregir; la cantidad inicial y el
    /// precio solo si el lote no tuvo operaciones (etapa, desdoble, bajas, ventas o traslados).
    /// </summary>
    Task EditarCompraAsync(EditarCompraLoteDto dto, CancellationToken ct = default);

    /// <summary>
    /// Corrige los datos descriptivos del lote (código, ubicación, línea/raza, fecha de ingreso, origen, proveedor y anotaciones).
    /// No toca la granja, la etapa, las cantidades ni los valores. Un lote con la compra anulada no se edita.
    /// </summary>
    Task ActualizarDatosAsync(ActualizarLoteDto dto, CancellationToken ct = default);

    /// <summary>Anula la compra de un lote sin operaciones: el lote deja de contar y su código y factura quedan consumidos.</summary>
    Task AnularCompraAsync(int loteId, string? motivo, CancellationToken ct = default);

    /// <summary>Promueve todo el lote a otra etapa (y opcionalmente a otra ubicación) y recalcula su valor.</summary>
    Task<CambioEtapaResultado> CambiarEtapaAsync(int loteId, int nuevaEtapaId, string? nuevaUbicacion, CancellationToken ct = default);

    /// <summary>Divide el lote: descuenta cabezas del origen y crea otro lote con el mismo costo unitario. Devuelve el id del nuevo lote.</summary>
    Task<int> DesdoblarAsync(int loteId, int cantidad, string codigoNuevoLote, string ubicacionNueva, CancellationToken ct = default);

    /// <summary>Mortandad o descarte parcial. Si no queda ninguna cabeza el lote pasa a Finalizado.</summary>
    Task RegistrarBajaAsync(int loteId, int cantidad, string causa, DateTime fecha, string? observaciones, CancellationToken ct = default);
}

public sealed class LoteService(IUnitOfWorkFactory fabrica, IReloj reloj) : ILoteService
{
    public static readonly IReadOnlyList<string> CausasDeBaja = ["Mortandad", "Hurto", "Descarte sanitario"];

    public async Task<IReadOnlyList<LoteDetalle>> ListarAsync(int programaId, bool incluirAnulados = false, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var granjas = (await uow.Granjas.GetAllAsync(ct)).ToDictionary(g => g.Id, g => g.Nombre);
        var etapas = (await uow.Categorias.GetByProgramaAsync(programaId, ct)).ToDictionary(c => c.Id, c => c.Nombre);
        return (await uow.Lotes.GetAllAsync(ct))
            .Where(l => l.ProgramaId == programaId && (incluirAnulados || !l.Anulado))
            .OrderBy(l => l.CodigoLote, StringComparer.OrdinalIgnoreCase)
            .Select(l => new LoteDetalle(l, granjas.GetValueOrDefault(l.GranjaId, "—"), etapas.GetValueOrDefault(l.EtapaId, "—")))
            .ToList();
    }

    public async Task<LoteDetalle> ObtenerAsync(int loteId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var lote = await uow.Lotes.GetByIdAsync(loteId, ct) ?? throw Regla.Violada("El lote no existe.");
        var granja = await uow.Granjas.GetByIdAsync(lote.GranjaId, ct);
        var etapa = await uow.Categorias.GetByIdAsync(lote.EtapaId, ct);
        return new LoteDetalle(lote, granja?.Nombre ?? "—", etapa?.Nombre ?? "—");
    }

    public async Task<int> RegistrarNuevoLoteAsync(NuevoLoteDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var codigo = (dto.CodigoLote ?? string.Empty).Trim();
        Regla.ExigirTexto(codigo, "El código del lote");
        Regla.ExigirTexto(dto.Ubicacion, "La ubicación (galpón, estanque o corral)");
        Regla.ExigirTexto(dto.LineaRaza, "La línea o raza");
        Regla.Exigir(dto.ClaseEntrada is ClaseEntrada.Compra or ClaseEntrada.Nacimiento, "El ingreso debe ser por compra o por nacimiento / cría propia.");
        Regla.Exigir(dto.FechaIngreso != default && dto.FechaIngreso.Date <= reloj.Hoy, "La fecha de ingreso es obligatoria y no puede ser futura.");
        if (dto.ControlNumericoPoblacion)
        {
            Regla.Exigir(dto.Cantidad > 0, "La cantidad inicial debe ser mayor que cero.");
        }

        var esCompra = dto.ClaseEntrada == ClaseEntrada.Compra;
        if (esCompra)
        {
            Regla.ExigirTexto(dto.Factura, "El número de factura o remisión de compra");
            Regla.ExigirTexto(dto.OrdenCompra, "La orden de compra");
            Regla.ExigirTexto(dto.Proveedor, "El proveedor");
            Regla.Exigir(dto.PrecioUnitarioFactura > 0, "El precio de factura debe ser mayor que cero.");
        }

        await using var uow = await fabrica.BeginAsync(ct);
        var programa = await uow.Programas.GetByIdAsync(dto.ProgramaId, ct) ?? throw Regla.Violada("El programa no existe.");
        Regla.Exigir(programa.TipoInventario == TipoInventario.Lote, $"El programa {programa.Nombre} no se maneja por lotes.");
        var granja = await uow.Granjas.GetByIdAsync(dto.GranjaId, ct) ?? throw Regla.Violada("La granja no existe.");
        Regla.Exigir(granja.Activa, $"La granja {granja.Nombre} está inactiva.");
        var etapa = await uow.Categorias.GetByIdAsync(dto.EtapaId, ct) ?? throw Regla.Violada("La etapa no existe.");
        Regla.Exigir(etapa.ProgramaId == programa.Id, "La etapa elegida no pertenece al programa.");
        Regla.Exigir(!await uow.Lotes.ExisteCodigoAsync(codigo, ct), $"Ya existe un lote con el código {codigo}.");

        // Compra: el valor contable parte de la factura. Nacimiento: compra = $0 y el valor sale de la etapa.
        var referencia = (await uow.TarifasPrograma.GetByCategoriaAsync(etapa.Id, ct))?.ValorReferencia ?? 0m;
        var valorCompra = esCompra ? dto.PrecioUnitarioFactura : 0m;
        var valorContable = esCompra ? dto.PrecioUnitarioFactura : referencia;
        var cantidad = dto.ControlNumericoPoblacion ? dto.Cantidad : 0;

        var lote = new LotePoblacion
        {
            CodigoLote = codigo,
            ProgramaId = programa.Id,
            GranjaId = granja.Id,
            EtapaId = etapa.Id,
            ClaseEntrada = dto.ClaseEntrada,
            Ubicacion = dto.Ubicacion.Trim(),
            LineaRaza = dto.LineaRaza.Trim(),
            OrigenReferencia = Limpio(esCompra ? dto.Factura : dto.OrigenReferencia),
            Proveedor = esCompra ? Limpio(dto.Proveedor) : null,
            OrdenCompra = esCompra ? Limpio(dto.OrdenCompra) : null,
            FechaNacimientoIngreso = dto.FechaIngreso.Date,
            ControlNumericoPoblacion = dto.ControlNumericoPoblacion,
            CantidadInicial = cantidad,
            CantidadActual = cantidad,
            ValorCompraUnitario = valorCompra,
            ValorContableUnitario = valorContable,
            Observaciones = Limpio(dto.Observaciones),
            Estado = EstadoLote.Activo,
        };
        lote.ValorTotalContable = Total(lote);

        await uow.Lotes.InsertAsync(lote, ct);
        await uow.CommitAsync(ct);
        return lote.Id;
    }

    public async Task<CambioEtapaResultado> CambiarEtapaAsync(int loteId, int nuevaEtapaId, string? nuevaUbicacion, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var lote = await ObtenerActivoAsync(uow, loteId, ct);
        var etapa = await uow.Categorias.GetByIdAsync(nuevaEtapaId, ct) ?? throw Regla.Violada("La etapa no existe.");
        Regla.Exigir(etapa.ProgramaId == lote.ProgramaId, "La etapa elegida no pertenece al programa del lote.");
        Regla.Exigir(etapa.Id != lote.EtapaId || !string.IsNullOrWhiteSpace(nuevaUbicacion), "Elija otra etapa o indique la nueva ubicación.");

        var anterior = lote.ValorTotalContable;
        var referencia = (await uow.TarifasPrograma.GetByCategoriaAsync(etapa.Id, ct))?.ValorReferencia ?? 0m;

        // Una etapa sin valor de referencia (0) no borra el valor del lote: se conserva, igual que un animal sin tarifa.
        var sinReferencia = etapa.Id != lote.EtapaId && referencia == 0m;
        if (etapa.Id != lote.EtapaId && !sinReferencia)
        {
            lote.ValorContableUnitario = referencia;
        }

        var etapaAnterior = lote.EtapaId;
        lote.EtapaId = etapa.Id;
        if (!string.IsNullOrWhiteSpace(nuevaUbicacion))
        {
            lote.Ubicacion = nuevaUbicacion.Trim();
        }

        lote.ValorTotalContable = Total(lote);
        lote.Operaciones++;
        if (etapa.Id != etapaAnterior)
        {
            lote.Observaciones = Anotar(lote.Observaciones, $"Cambio de etapa a «{etapa.Nombre}»");
        }

        await uow.Lotes.UpdateAsync(lote, ct);
        await uow.CommitAsync(ct);
        return new CambioEtapaResultado(anterior, lote.ValorTotalContable, sinReferencia);
    }

    public async Task<int> DesdoblarAsync(int loteId, int cantidad, string codigoNuevoLote, string ubicacionNueva, CancellationToken ct = default)
    {
        var codigo = (codigoNuevoLote ?? string.Empty).Trim();
        Regla.ExigirTexto(codigo, "El código del nuevo lote");
        Regla.ExigirTexto(ubicacionNueva, "La nueva ubicación");

        await using var uow = await fabrica.BeginAsync(ct);
        var origen = await ObtenerActivoAsync(uow, loteId, ct);
        Regla.Exigir(origen.ControlNumericoPoblacion, "Un lote sin control numérico de población no se puede desdoblar por cantidades.");
        Regla.Exigir(cantidad > 0, "La cantidad a transferir debe ser mayor que cero.");
        Regla.Exigir(cantidad < origen.CantidadActual, $"La cantidad a transferir debe ser menor que la población actual ({origen.CantidadActual:N0}).");
        Regla.Exigir(!await uow.Lotes.ExisteCodigoAsync(codigo, ct), $"Ya existe un lote con el código {codigo}.");

        var nuevo = new LotePoblacion
        {
            CodigoLote = codigo,
            ProgramaId = origen.ProgramaId,
            GranjaId = origen.GranjaId,
            EtapaId = origen.EtapaId,
            ClaseEntrada = ClaseEntrada.Desdoble,
            Ubicacion = ubicacionNueva.Trim(),
            LineaRaza = origen.LineaRaza,
            OrigenReferencia = origen.CodigoLote,
            FechaNacimientoIngreso = origen.FechaNacimientoIngreso,
            ControlNumericoPoblacion = true,
            CantidadInicial = cantidad,
            CantidadActual = cantidad,
            ValorCompraUnitario = origen.ValorCompraUnitario,
            ValorContableUnitario = origen.ValorContableUnitario,
            Observaciones = Anotar(null, $"Desdoblado del lote {origen.CodigoLote} ({cantidad:N0} cabezas)"),
            Estado = EstadoLote.Activo,
        };
        nuevo.ValorTotalContable = Total(nuevo);

        // El saldo financiero total no cambia: las cabezas y su valor pasan de un lote al otro.
        origen.CantidadActual -= cantidad;
        origen.CantidadInicial = Math.Max(0, origen.CantidadInicial - cantidad);
        origen.ValorTotalContable = Total(origen);
        origen.Operaciones++;
        origen.Observaciones = Anotar(origen.Observaciones, $"Desdoble de {cantidad:N0} cabezas al lote {codigo}");

        await uow.Lotes.InsertAsync(nuevo, ct);
        await uow.Lotes.UpdateAsync(origen, ct);
        await uow.CommitAsync(ct);
        return nuevo.Id;
    }

    public async Task RegistrarBajaAsync(int loteId, int cantidad, string causa, DateTime fecha, string? observaciones, CancellationToken ct = default)
    {
        Regla.Exigir(CausasDeBaja.Contains(causa), "Elija la causa de la baja: mortandad, hurto o descarte sanitario.");
        Regla.Exigir(fecha != default && fecha.Date <= reloj.Hoy, "La fecha de la baja es obligatoria y no puede ser futura.");

        await using var uow = await fabrica.BeginAsync(ct);
        var lote = await ObtenerActivoAsync(uow, loteId, ct);
        Regla.Exigir(lote.ControlNumericoPoblacion, "Este lote no lleva control numérico de población: no se registran bajas por cantidad.");
        Regla.Exigir(fecha.Date >= lote.FechaNacimientoIngreso.Date, "La baja no puede ser anterior al ingreso del lote.");
        Regla.Exigir(cantidad > 0, "La cantidad de bajas debe ser mayor que cero.");
        Regla.Exigir(cantidad <= lote.CantidadActual, $"No se pueden dar de baja {cantidad:N0} cabezas: el lote solo tiene {lote.CantidadActual:N0}.");

        lote.CantidadActual -= cantidad;
        lote.MortandadAcumulada += cantidad;
        lote.Operaciones++;
        lote.ValorTotalContable = Total(lote);
        if (lote.CantidadActual == 0)
        {
            lote.Estado = EstadoLote.Finalizado;
        }

        var nota = $"{causa}: {cantidad:N0} cabezas" + (string.IsNullOrWhiteSpace(observaciones) ? string.Empty : $" ({observaciones.Trim()})");
        lote.Observaciones = Anotar(lote.Observaciones, nota, fecha);
        await uow.Lotes.UpdateAsync(lote, ct);
        await uow.CommitAsync(ct);
    }

    public async Task EditarCompraAsync(EditarCompraLoteDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Regla.ExigirTexto(dto.Factura, "El número de factura o remisión de compra");
        Regla.ExigirTexto(dto.OrdenCompra, "La orden de compra");
        Regla.ExigirTexto(dto.Proveedor, "El proveedor");
        Regla.ExigirTexto(dto.Ubicacion, "La ubicación (galpón, estanque o corral)");
        Regla.ExigirTexto(dto.LineaRaza, "La línea o raza");
        Regla.Exigir(dto.PrecioUnitarioFactura > 0, "El precio de factura debe ser mayor que cero.");
        Regla.Exigir(dto.FechaIngreso != default && dto.FechaIngreso.Date <= reloj.Hoy, "La fecha de ingreso es obligatoria y no puede ser futura.");

        await using var uow = await fabrica.BeginAsync(ct);
        var lote = await ObtenerCompraEditableAsync(uow, dto.LoteId, "corregir", ct);
        var granja = await uow.Granjas.GetByIdAsync(dto.GranjaId, ct) ?? throw Regla.Violada("La granja no existe.");
        Regla.Exigir(granja.Activa || granja.Id == lote.GranjaId, $"La granja {granja.Nombre} está inactiva.");
        var programa = await uow.Programas.GetByIdAsync(lote.ProgramaId, ct);
        Regla.Exigir(
            granja.Id == lote.GranjaId || (await uow.GranjaProgramas.GetGranjaIdsAsync(lote.ProgramaId, ct)).Contains(granja.Id),
            $"El programa {programa?.Nombre} no se ejecuta en la granja {granja.Nombre}.");

        var cambiaCantidad = lote.ControlNumericoPoblacion && dto.Cantidad != lote.CantidadInicial;
        var cambiaPrecio = dto.PrecioUnitarioFactura != lote.ValorCompraUnitario;
        Regla.Exigir(!lote.ControlNumericoPoblacion || dto.Cantidad > 0, "La cantidad inicial debe ser mayor que cero.");

        lote.OrigenReferencia = dto.Factura.Trim();
        lote.OrdenCompra = dto.OrdenCompra.Trim();
        lote.Proveedor = dto.Proveedor.Trim();
        lote.FechaNacimientoIngreso = dto.FechaIngreso.Date;
        lote.GranjaId = granja.Id;
        lote.Ubicacion = dto.Ubicacion.Trim();
        lote.LineaRaza = dto.LineaRaza.Trim();
        lote.Observaciones = Limpio(dto.Observaciones);
        if (cambiaCantidad)
        {
            AplicarCantidadInicial(lote, dto.Cantidad);
        }

        if (cambiaPrecio)
        {
            // Si el lote nunca se revaloró (sigue valiendo lo que costó) el valor contable acompaña al precio de la factura.
            if (lote.ValorContableUnitario == lote.ValorCompraUnitario)
            {
                lote.ValorContableUnitario = dto.PrecioUnitarioFactura;
            }

            lote.ValorCompraUnitario = dto.PrecioUnitarioFactura;
        }

        lote.ValorTotalContable = Total(lote);
        await uow.Lotes.UpdateAsync(lote, ct);
        await uow.CommitAsync(ct);
    }

    public async Task ActualizarDatosAsync(ActualizarLoteDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var codigo = (dto.CodigoLote ?? string.Empty).Trim();
        Regla.ExigirTexto(codigo, "El código del lote");
        Regla.ExigirTexto(dto.Ubicacion, "La ubicación (galpón, estanque o corral)");
        Regla.ExigirTexto(dto.LineaRaza, "La línea o raza");
        Regla.Exigir(dto.FechaIngreso != default && dto.FechaIngreso.Date <= reloj.Hoy, "La fecha de ingreso es obligatoria y no puede ser futura.");

        await using var uow = await fabrica.BeginAsync(ct);
        var lote = await uow.Lotes.GetByIdAsync(dto.LoteId, ct) ?? throw Regla.Violada("El lote no existe.");
        Regla.Exigir(!lote.Anulado, $"La compra del lote {lote.CodigoLote} fue anulada: ya no se puede editar.");
        if (lote.ClaseEntrada == ClaseEntrada.Compra)
        {
            Regla.ExigirTexto(dto.Proveedor, "El proveedor");
            Regla.ExigirTexto(dto.OrdenCompra, "La orden de compra");
        }

        var otro = await uow.Lotes.GetByCodigoAsync(codigo, ct);
        Regla.Exigir(otro is null || otro.Id == lote.Id, $"Ya existe un lote con el código {codigo}.");

        lote.CodigoLote = codigo;
        lote.Ubicacion = dto.Ubicacion.Trim();
        lote.LineaRaza = dto.LineaRaza.Trim();
        lote.FechaNacimientoIngreso = dto.FechaIngreso.Date;
        lote.OrigenReferencia = Limpio(dto.OrigenReferencia);
        if (lote.ClaseEntrada == ClaseEntrada.Compra)
        {
            lote.Proveedor = dto.Proveedor!.Trim();
            lote.OrdenCompra = dto.OrdenCompra!.Trim();
        }

        // Población y valorización.
        var cambios = new List<string>();
        var granja = await uow.Granjas.GetByIdAsync(dto.GranjaId, ct) ?? throw Regla.Violada("La granja no existe.");
        var etapa = await uow.Categorias.GetByIdAsync(dto.EtapaId, ct) ?? throw Regla.Violada("La etapa no existe.");
        Regla.Exigir(etapa.ProgramaId == lote.ProgramaId, "La etapa elegida no pertenece al programa del lote.");
        Regla.Exigir(dto.ValorContableUnitario >= 0, "El valor contable no puede ser negativo.");
        if (granja.Id != lote.GranjaId)
        {
            var programa = await uow.Programas.GetByIdAsync(lote.ProgramaId, ct);
            Regla.Exigir(granja.Activa, $"La granja {granja.Nombre} está inactiva.");
            Regla.Exigir(
                (await uow.GranjaProgramas.GetGranjaIdsAsync(lote.ProgramaId, ct)).Contains(granja.Id),
                $"El programa {programa?.Nombre} no se ejecuta en la granja {granja.Nombre}.");
            var anterior = await uow.Granjas.GetByIdAsync(lote.GranjaId, ct);
            cambios.Add($"granja de {anterior?.Nombre ?? "—"} a {granja.Nombre}");
            lote.GranjaId = granja.Id;
        }

        if (etapa.Id != lote.EtapaId)
        {
            cambios.Add($"etapa a «{etapa.Nombre}»");
            lote.EtapaId = etapa.Id;
        }

        if (lote.ControlNumericoPoblacion
            && (dto.CantidadInicial != lote.CantidadInicial || dto.MortandadAcumulada != lote.MortandadAcumulada))
        {
            Regla.Exigir(dto.CantidadInicial > 0, "La cantidad inicial debe ser mayor que cero.");
            AplicarCantidadInicial(lote, dto.CantidadInicial, dto.MortandadAcumulada);
            cambios.Add($"población (inicial {lote.CantidadInicial:N0}, bajas {lote.MortandadAcumulada:N0}, actual {lote.CantidadActual:N0})");
        }

        if (dto.ValorContableUnitario != lote.ValorContableUnitario)
        {
            cambios.Add($"valor contable de {lote.ValorContableUnitario:C0} a {dto.ValorContableUnitario:C0}");
            lote.ValorContableUnitario = dto.ValorContableUnitario;
        }

        lote.ValorTotalContable = Total(lote);
        lote.Observaciones = Limpio(dto.Observaciones);
        if (cambios.Count > 0)
        {
            lote.Observaciones = Anotar(lote.Observaciones, "Corrección desde la ficha: " + string.Join("; ", cambios));
        }

        await uow.Lotes.UpdateAsync(lote, ct);
        await uow.CommitAsync(ct);
    }

    public async Task AnularCompraAsync(int loteId, string? motivo, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var lote = await ObtenerCompraEditableAsync(uow, loteId, "anular", ct);

        // Las bajas y los cambios de etapa solo afectan al lote; el desdoble, la venta y el traslado ya involucran a otros lotes o documentos.
        var desdoblado = (await uow.Lotes.GetAllAsync(ct))
            .Any(x => x.ClaseEntrada == ClaseEntrada.Desdoble && string.Equals(x.OrigenReferencia, lote.CodigoLote, StringComparison.OrdinalIgnoreCase));
        var enDocumentos = (await uow.RemisionDetalles.GetAllAsync(ct)).Any(d => d.LoteId == lote.Id || d.LoteDestinoId == lote.Id);
        Regla.Exigir(
            !desdoblado && !enDocumentos,
            $"El lote {lote.CodigoLote} ya fue desdoblado, vendido o trasladado: no se puede quitar su compra.");

        lote.Anulado = true;
        lote.Estado = EstadoLote.Finalizado;
        lote.Observaciones = Anotar(lote.Observaciones, "Compra anulada" + (string.IsNullOrWhiteSpace(motivo) ? string.Empty : $": {motivo.Trim()}"));
        await uow.Lotes.UpdateAsync(lote, ct);
        await uow.CommitAsync(ct);
    }

    private static async Task<LotePoblacion> ObtenerCompraEditableAsync(IUnitOfWork uow, int loteId, string accion, CancellationToken ct)
    {
        var lote = await uow.Lotes.GetByIdAsync(loteId, ct) ?? throw Regla.Violada("El lote no existe.");
        Regla.Exigir(lote.ClaseEntrada == ClaseEntrada.Compra, $"El lote {lote.CodigoLote} no ingresó por compra: no hay una compra que {accion}.");
        Regla.Exigir(!lote.Anulado, $"La compra del lote {lote.CodigoLote} ya fue anulada.");
        return lote;
    }

    private static async Task<LotePoblacion> ObtenerActivoAsync(IUnitOfWork uow, int loteId, CancellationToken ct)
    {
        var lote = await uow.Lotes.GetByIdAsync(loteId, ct) ?? throw Regla.Violada("El lote no existe.");
        Regla.Exigir(!lote.Anulado, $"La compra del lote {lote.CodigoLote} fue anulada.");
        Regla.Exigir(lote.Estado == EstadoLote.Activo, $"El lote {lote.CodigoLote} está finalizado: ya no admite operaciones.");
        return lote;
    }

    /// <summary>
    /// Fija la cantidad inicial conservando las salidas ya aplicadas (bajas, desdobles, ventas, traslados): la población actual
    /// se mueve lo mismo que la inicial y no puede quedar negativa. Un lote vaciado queda Finalizado; uno con cabezas, Activo.
    /// </summary>
    private static void AplicarCantidadInicial(LotePoblacion lote, int nueva, int? bajas = null)
    {
        var nuevasBajas = bajas ?? lote.MortandadAcumulada;
        Regla.Exigir(nuevasBajas >= 0 && nuevasBajas <= nueva, "Las bajas no pueden ser negativas ni superar la cantidad inicial.");
        var actual = lote.CantidadActual + (nueva - lote.CantidadInicial) - (nuevasBajas - lote.MortandadAcumulada);
        Regla.Exigir(
            actual >= 0,
            $"Con una cantidad inicial de {nueva:N0} y {nuevasBajas:N0} bajas la población actual quedaría negativa: ya hay cabezas desdobladas, vendidas o trasladadas.");
        lote.CantidadInicial = nueva;
        lote.MortandadAcumulada = nuevasBajas;
        lote.CantidadActual = actual;
        if (!lote.Anulado)
        {
            lote.Estado = actual == 0 ? EstadoLote.Finalizado : EstadoLote.Activo;
        }
    }

    /// <summary>Valor total: cabezas × valor unitario; en un lote sin conteo, el valor del lote entero.</summary>
    private static decimal Total(LotePoblacion lote)
        => lote.ControlNumericoPoblacion ? lote.CantidadActual * lote.ValorContableUnitario : lote.ValorContableUnitario;

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private string Anotar(string? actual, string nota, DateTime? fecha = null)
    {
        var linea = $"[{(fecha ?? reloj.Hoy):dd/MM/yyyy}] {nota}";
        return string.IsNullOrWhiteSpace(actual) ? linea : actual + Environment.NewLine + linea;
    }
}