using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.Services.Animales;

/// <summary>Datos de la ficha que el usuario puede corregir. El valor de compra y el valor contable NO se editan aquí.</summary>
public sealed class AnimalEdicionDto
{
    public int AnimalId { get; set; }

    public string ChapetaAmarilla { get; set; } = string.Empty;

    public string? ChapetaRoja { get; set; }

    public string? Nombre { get; set; }

    public string Sexo { get; set; } = string.Empty;

    public DateTime FechaNacimiento { get; set; }

    public string Raza { get; set; } = string.Empty;

    public decimal PesoActualKg { get; set; }

    public int OrientacionId { get; set; }

    public int NumeroPartos { get; set; }

    public bool ExentoValorizacion { get; set; }

    public string? MadreIdentificacion { get; set; }
}

public interface IAnimalService
{
    /// <summary>
    /// Corrige los datos de la ficha. La ubicación solo cambia con un traslado, y el valor contable lo recalcula el
    /// próximo cierre de valorización: ninguno de los dos se modifica aquí.
    /// </summary>
    Task ActualizarAsync(AnimalEdicionDto dto, CancellationToken ct = default);

    /// <summary>Categoría y valor que la matriz activa asignaría hoy (para mostrar al registrar un nacimiento).</summary>
    Task<ResultadoValorizacion> PrevisualizarValoracionAsync(
        string sexo, int orientacionId, int numeroPartos, DateTime fechaNacimiento, CancellationToken ct = default);
}

public sealed class AnimalService(IUnitOfWorkFactory fabrica, IAgingEngine aging, IReloj reloj) : IAnimalService
{
    public async Task ActualizarAsync(AnimalEdicionDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var chapeta = (dto.ChapetaAmarilla ?? string.Empty).Trim();
        Regla.ExigirTexto(chapeta, "La chapeta amarilla");
        Regla.Exigir(Sexo.EsDeAnimal(dto.Sexo), "El sexo debe ser Macho o Hembra.");
        Regla.ExigirTexto(dto.Raza, "La raza");
        Regla.Exigir(dto.FechaNacimiento != default && dto.FechaNacimiento.Date <= reloj.Hoy, "La fecha de nacimiento es obligatoria y no puede ser futura.");
        Regla.Exigir(dto.PesoActualKg >= 0, "El peso no puede ser negativo.");
        Regla.Exigir(dto.NumeroPartos >= 0, "El número de partos no puede ser negativo.");
        Regla.Exigir(dto.NumeroPartos == 0 || dto.Sexo == Sexo.Hembra, "Un macho no puede tener partos.");

        await using var uow = await fabrica.BeginAsync(ct);
        var animal = await uow.Animales.GetByIdAsync(dto.AnimalId, ct) ?? throw Regla.Violada("El animal no existe.");
        Regla.Exigir(
            !await uow.Animales.ExisteChapetaAsync(chapeta, animal.Id, ct),
            $"La chapeta {chapeta} ya está registrada en otro animal.");
        Regla.Exigir(await uow.Orientaciones.GetByIdAsync(dto.OrientacionId, ct) is not null, "La orientación productiva no existe.");
        if (animal.FechaCompra is { } compra)
        {
            Regla.Exigir(dto.FechaNacimiento.Date <= compra.Date, "La fecha de nacimiento no puede ser posterior a la fecha de compra.");
        }

        animal.ChapetaAmarilla = chapeta;
        animal.ChapetaRoja = Normalizar(dto.ChapetaRoja);
        animal.Nombre = Normalizar(dto.Nombre);
        animal.Sexo = dto.Sexo;
        animal.FechaNacimiento = dto.FechaNacimiento.Date;
        animal.Raza = dto.Raza.Trim();
        animal.PesoActualKg = dto.PesoActualKg;
        animal.OrientacionId = dto.OrientacionId;
        animal.NumeroPartos = dto.NumeroPartos;
        animal.ExentoValorizacion = dto.ExentoValorizacion;
        animal.MadreIdentificacion = Normalizar(dto.MadreIdentificacion);

        // UpdateAsync nunca toca valor_compra_origen, fecha_registro ni clase_entrada (inmutables).
        await uow.Animales.UpdateAsync(animal, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<ResultadoValorizacion> PrevisualizarValoracionAsync(
        string sexo, int orientacionId, int numeroPartos, DateTime fechaNacimiento, CancellationToken ct = default)
    {
        Regla.Exigir(Sexo.EsDeAnimal(sexo), "Seleccione el sexo.");
        Regla.Exigir(fechaNacimiento != default && fechaNacimiento.Date <= reloj.Hoy, "La fecha de nacimiento no puede ser futura.");

        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var temporal = new Animal
        {
            Sexo = sexo,
            OrientacionId = orientacionId,
            NumeroPartos = numeroPartos,
            FechaNacimiento = fechaNacimiento.Date,
        };
        return await aging.DeterminarAsync(uow, temporal, reloj.Hoy, ct);
    }

    private static string? Normalizar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
