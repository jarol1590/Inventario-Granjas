using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.Services.Consultas;

// ---- Bovinos --------------------------------------------------------------------------------------------------------

/// <param name="Estado">Por defecto solo los Disponibles; nulo = todos los estados.</param>
public sealed record BovinoFiltro(
    int? GranjaId = null,
    int? ProgramaId = null,
    string? Texto = null,
    int? OrientacionId = null,
    string? Estado = EstadoAnimal.Disponible,
    string? Sexo = null,
    int? CategoriaId = null,
    string? Raza = null);

public sealed record BovinoListItem(
    int Id,
    string ChapetaAmarilla,
    string? ChapetaRoja,
    string? Nombre,
    int GranjaId,
    string GranjaNombre,
    string Sexo,
    string Raza,
    int OrientacionId,
    string Orientacion,
    int CategoriaId,
    string Categoria,
    bool SinTarifa,
    int EdadMeses,
    decimal ValorContable,
    decimal ValorCompraOrigen,
    decimal Valorizacion,
    string Estado,
    bool Exento);

public sealed record ResumenBovinos(int Cantidad, int Machos, int Hembras, decimal ValorContableTotal, decimal ValorCompraTotal);

public sealed record ResultadoBovinos(IReadOnlyList<BovinoListItem> Items, ResumenBovinos Resumen);

/// <summary>Animal elegible para un documento (venta, baja, traslado).</summary>
public sealed record AnimalSeleccionable(
    int Id,
    string ChapetaAmarilla,
    string? ChapetaRoja,
    string Sexo,
    DateTime FechaNacimiento,
    string Raza,
    string Categoria,
    int OrientacionId,
    string Orientacion,
    decimal ValorContable);

// ---- Compras --------------------------------------------------------------------------------------------------------

/// <param name="Desde">Inicio del rango (inclusive).</param>
/// <param name="Hasta">Fin del rango (inclusive, hasta el final de ese día).</param>
public sealed record CompraFiltro(int? GranjaId, DateTime Desde, DateTime Hasta, string? Factura = null, bool IncluirAnuladas = false);

public sealed record CompraListItem(
    int RemisionId,
    string NumeroFactura,
    string? OrdenCompra,
    DateTime Fecha,
    string Proveedor,
    string? Nit,
    string GranjaDestino,
    int Cantidad,
    decimal TotalFactura,
    bool Anulada);

public sealed record ResumenCompras(int TotalCompras, int AnimalesComprados, decimal ValorTotal);

public sealed record ResultadoCompras(IReadOnlyList<CompraListItem> Items, ResumenCompras Resumen);

public sealed record CompraEditable(CompraDto Dto, bool Anulada);

// ---- Salidas y bajas -------------------------------------------------------------------------------------------------

public sealed record SalidaFiltro(int? GranjaId, DateTime Desde, DateTime Hasta, string? Chapeta = null, bool IncluirAnuladas = false);

/// <param name="Clase">VENTA, MUERTE, HURTO, DESCARTE_SANITARIO o TRASLADO.</param>
/// <param name="Total">Valor de venta para las ventas; costo en libros para bajas y traslados.</param>
public sealed record SalidaListItem(
    int RemisionId,
    string NumeroRemision,
    DateTime Fecha,
    string Clase,
    string TipoDocumento,
    string? NumeroOficio,
    string? ReciboCaja,
    string Destino,
    int Cantidad,
    decimal Total,
    bool Anulada)
{
    /// <summary>La clase tal como se muestra al usuario ("DESCARTE SANITARIO").</summary>
    public string ClaseTexto => Clase.Replace('_', ' ');
}

public sealed record ResumenSalidas(decimal RecaudoVentas, int AnimalesRetirados, int BajasPorMuerteHurto);

public sealed record ResultadoSalidas(IReadOnlyList<SalidaListItem> Items, ResumenSalidas Resumen);

public sealed record VentaEditable(VentaDto Dto, bool Anulada, IReadOnlyList<AnimalSeleccionable> Animales);

public sealed record BajaEditable(BajaDto Dto, bool Anulada, IReadOnlyList<AnimalSeleccionable> Animales);

// ---- Traslados -------------------------------------------------------------------------------------------------------

/// <param name="GranjaId">Granja de origen o de destino.</param>
/// <param name="Texto">Número de remisión o chapeta amarilla (contiene).</param>
public sealed record TrasladoFiltro(int? GranjaId, DateTime Desde, DateTime Hasta, string? Texto = null, bool IncluirAnulados = false);

public sealed record TrasladoListItem(
    int RemisionId,
    string NumeroRemision,
    DateTime Fecha,
    string Origen,
    string Destino,
    int Cantidad,
    bool Anulado);

public sealed record ResumenTraslados(int TrasladosRealizados, int AnimalesMovidos);

public sealed record ResultadoTraslados(IReadOnlyList<TrasladoListItem> Items, ResumenTraslados Resumen);

// ---- Dashboard ---------------------------------------------------------------------------------------------------------

public sealed record PuntoGrafica(string Etiqueta, decimal Valor);

public sealed record DashboardDatos(
    int TotalAnimales,
    decimal SaldoTotal,
    int NacimientosMes,
    int SalidasMes,
    IReadOnlyList<PuntoGrafica> PorGranja,
    IReadOnlyList<PuntoGrafica> PorCategoria,
    IReadOnlyList<PuntoGrafica> MovimientosMes);
