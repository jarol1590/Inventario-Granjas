using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Kardex;

/// <summary>
/// Validaciones de forma de los documentos de inventario y de la jerarquía documental de 3 niveles
/// (Oficio de Venta → Remisión → Recibo de Caja). No consulta la base de datos: las comprobaciones que
/// dependen de ella (existencia, duplicados, estado de los animales) las hace el <see cref="IKardexService"/>.
/// Todas lanzan <see cref="System.ComponentModel.DataAnnotations.ValidationException"/> con un mensaje para el usuario.
/// </summary>
public interface IDocumentValidationService
{
    void ValidarNacimiento(NacimientoDto dto, DateTime hoy);

    void ValidarCompra(CompraDto dto, DateTime hoy);

    void ValidarVenta(VentaDto dto, DateTime hoy);

    void ValidarBaja(BajaDto dto, DateTime hoy);

    void ValidarTraslado(TrasladoDto dto, DateTime hoy);

    /// <summary>
    /// Una VENTA exige Oficio y Recibo de Caja; el traslado, la baja y la compra solo llevan Remisión y no admiten
    /// ninguno de los dos.
    /// </summary>
    void ValidarJerarquiaDocumental(Remision remision);
}

public sealed class DocumentValidationService : IDocumentValidationService
{
    public void ValidarNacimiento(NacimientoDto dto, DateTime hoy)
    {
        ArgumentNullException.ThrowIfNull(dto);

        Regla.Exigir(dto.GranjaId > 0, "Seleccione la granja de nacimiento.");
        ExigirFechaPasada(dto.FechaNacimiento, hoy, "La fecha de nacimiento");
        Regla.ExigirTexto(dto.ChapetaAmarilla, "La chapeta amarilla");
        Regla.Exigir(Sexo.EsDeAnimal(dto.Sexo), "El sexo debe ser Macho o Hembra.");
        Regla.ExigirTexto(dto.Raza, "La raza");
        Regla.Exigir(dto.OrientacionId > 0 || dto.CategoriaId > 0, "Seleccione la orientación productiva.");
        Regla.Exigir(dto.PesoAlNacerKg >= 0, "El peso al nacer no puede ser negativo.");
        // La genealogía por chapeta de la madre es de la ganadería; en otros programas es opcional.
        if (dto.CategoriaId is not > 0)
        {
            Regla.ExigirTexto(dto.MadreIdentificacion, "La identificación de la madre");
        }

        if (dto.EsInseminacion)
        {
            Regla.ExigirTexto(dto.RazaPadre, "La raza del padre (obligatoria en la inseminación artificial)");
        }
    }

    public void ValidarCompra(CompraDto dto, DateTime hoy)
    {
        ArgumentNullException.ThrowIfNull(dto);

        Regla.ExigirTexto(dto.NumeroFactura, "El número de factura / remisión de compra");
        ExigirFechaPasada(dto.FechaCompra, hoy, "La fecha de compra");
        Regla.Exigir(dto.GranjaDestinoId > 0, "Seleccione la granja destino de la compra.");
        Regla.ExigirTexto(dto.Proveedor, "El proveedor");
        Regla.Exigir(dto.Animales.Count > 0, "Agregue al menos un animal a la compra.");

        var repetidas = dto.Animales
            .GroupBy(a => a.ChapetaAmarilla?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        Regla.Exigir(repetidas is null, $"La chapeta {repetidas?.Key} está repetida en la compra.");

        foreach (var animal in dto.Animales)
        {
            var etiqueta = string.IsNullOrWhiteSpace(animal.ChapetaAmarilla) ? "Un animal de la compra" : $"El animal {animal.ChapetaAmarilla}";

            Regla.ExigirTexto(animal.ChapetaAmarilla, "La chapeta amarilla de cada animal");
            Regla.Exigir(Sexo.EsDeAnimal(animal.Sexo), $"{etiqueta}: el sexo debe ser Macho o Hembra.");
            Regla.ExigirTexto(animal.Raza, $"{etiqueta}: la raza");
            Regla.Exigir(animal.OrientacionId > 0 || animal.CategoriaId > 0, $"{etiqueta}: seleccione la orientación productiva.");
            Regla.Exigir(
                animal.FechaNacimiento != default && animal.FechaNacimiento.Date <= dto.FechaCompra.Date,
                $"{etiqueta}: la fecha de nacimiento es obligatoria y no puede ser posterior a la compra.");
            Regla.Exigir(animal.PrecioFactura > 0, $"{etiqueta}: el precio de factura debe ser mayor que cero.");
            Regla.Exigir(animal.PesoKg >= 0, $"{etiqueta}: el peso no puede ser negativo.");
            Regla.Exigir(animal.NumeroPartos >= 0, $"{etiqueta}: el número de partos no puede ser negativo.");
            Regla.Exigir(
                animal.NumeroPartos == 0 || animal.Sexo == Sexo.Hembra,
                $"{etiqueta}: un macho no puede tener partos.");
        }
    }

    public void ValidarVenta(VentaDto dto, DateTime hoy)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Nivel 1: Oficio de Venta, de un solo programa.
        Regla.ExigirTexto(dto.NumeroOficio, "El número de oficio de venta");
        Regla.Exigir(dto.ProgramaId > 0, "Seleccione el programa del oficio de venta.");

        // Nivel 2: Remisión.
        Regla.ExigirTexto(dto.NumeroRemision, "El número de remisión");
        ExigirFechaPasada(dto.FechaRemision, hoy, "La fecha de la salida");
        Regla.Exigir(dto.GranjaOrigenId > 0, "Seleccione la granja de origen.");
        Regla.ExigirTexto(dto.Comprador, "El comprador");

        // Nivel 3: Recibo de Caja de la remisión.
        Regla.ExigirTexto(dto.ReciboCaja, "El número de recibo de caja (obligatorio en las ventas)");

        Regla.Exigir(dto.Animales.Count > 0, "Agregue al menos un animal a la venta.");
        ExigirAnimalesSinRepetir(dto.Animales.Select(a => a.AnimalId));

        foreach (var animal in dto.Animales)
        {
            Regla.Exigir(animal.PrecioVenta > 0, "Cada animal vendido debe tener un precio de venta mayor que cero.");
            Regla.Exigir(animal.PesoKg is null or >= 0, "El peso no puede ser negativo.");
        }
    }

    public void ValidarBaja(BajaDto dto, DateTime hoy)
    {
        ArgumentNullException.ThrowIfNull(dto);

        Regla.ExigirTexto(dto.NumeroRemision, "El número de remisión");
        ExigirFechaPasada(dto.FechaBaja, hoy, "La fecha de la baja");
        Regla.Exigir(dto.GranjaOrigenId > 0, "Seleccione la granja de origen.");
        Regla.Exigir(
            ClaseSalida.Bajas.Contains(dto.ClaseSalida),
            "La clase de baja debe ser MUERTE, HURTO o DESCARTE_SANITARIO.");
        Regla.ExigirTexto(dto.DocumentoSoporte, "El documento de soporte (acta sanitaria, denuncia...)");
        Regla.Exigir(dto.Animales.Count > 0, "Agregue al menos un animal a la baja.");
        ExigirAnimalesSinRepetir(dto.Animales.Select(a => a.AnimalId));
    }

    public void ValidarTraslado(TrasladoDto dto, DateTime hoy)
    {
        ArgumentNullException.ThrowIfNull(dto);

        Regla.ExigirTexto(dto.NumeroRemision, "El número de remisión");
        ExigirFechaPasada(dto.FechaTraslado, hoy, "La fecha del traslado");
        Regla.Exigir(dto.GranjaOrigenId > 0, "Seleccione la granja de origen.");
        Regla.Exigir(dto.GranjaDestinoId > 0, "Seleccione la granja de destino.");
        Regla.Exigir(dto.GranjaOrigenId != dto.GranjaDestinoId, "La granja de origen y la de destino deben ser distintas.");
        Regla.Exigir(dto.Animales.Count > 0, "Agregue al menos un animal al traslado.");
        ExigirAnimalesSinRepetir(dto.Animales.Select(a => a.AnimalId));
    }

    public void ValidarJerarquiaDocumental(Remision remision)
    {
        ArgumentNullException.ThrowIfNull(remision);

        Regla.Exigir(
            remision.TipoMovimiento is TipoRemision.Venta or TipoRemision.Traslado or TipoRemision.Compra or TipoRemision.Baja,
            "El tipo de documento no es válido.");

        if (remision.TipoMovimiento == TipoRemision.Venta)
        {
            Regla.Exigir(remision.OficioId is not null, "Toda venta exige un Oficio de Venta.");
            Regla.ExigirTexto(remision.ReciboCaja, "El número de recibo de caja (obligatorio en las ventas)");
            return;
        }

        Regla.Exigir(
            remision.OficioId is null && string.IsNullOrWhiteSpace(remision.ReciboCaja),
            $"Un documento de tipo {remision.TipoMovimiento} solo lleva Remisión: no admite Oficio de Venta ni Recibo de Caja.");
    }

    private static void ExigirFechaPasada(DateTime fecha, DateTime hoy, string nombre)
    {
        Regla.Exigir(fecha != default, $"{nombre} es obligatoria.");
        Regla.Exigir(fecha.Date <= hoy.Date, $"{nombre} no puede ser futura.");
    }

    private static void ExigirAnimalesSinRepetir(IEnumerable<int> ids)
    {
        var lista = ids.ToList();
        Regla.Exigir(lista.All(id => id > 0), "Hay un animal sin identificar en la lista.");
        Regla.Exigir(lista.Distinct().Count() == lista.Count, "Hay animales repetidos en la lista.");
    }
}
