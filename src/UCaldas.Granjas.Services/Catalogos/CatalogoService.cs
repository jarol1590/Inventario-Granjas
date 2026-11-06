using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Reportes;

namespace UCaldas.Granjas.Services.Catalogos;

/// <summary>Listas para los selectores y administración de granjas, programas, firma y preferencias del sistema.</summary>
public interface ICatalogoService
{
    Task<IReadOnlyList<Granja>> GetGranjasAsync(bool soloActivas = false, CancellationToken ct = default);

    Task<IReadOnlyList<Programa>> GetProgramasAsync(CancellationToken ct = default);

    /// <summary>Programas asociados a una granja (el filtro de programa de la cabecera depende de la granja elegida).</summary>
    Task<IReadOnlyList<Programa>> GetProgramasDeGranjaAsync(int granjaId, CancellationToken ct = default);

    Task<IReadOnlyList<OrientacionProductiva>> GetOrientacionesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CategoriaEtapa>> GetCategoriasAsync(CancellationToken ct = default);

    /// <summary>Razas usadas en el inventario más algunas comunes, para autocompletar.</summary>
    Task<IReadOnlyList<string>> GetRazasAsync(CancellationToken ct = default);

    Task<IReadOnlyList<int>> GetGranjaIdsDeProgramaAsync(int programaId, CancellationToken ct = default);

    /// <summary>Crea o actualiza una granja (código y nombre únicos). No se inactiva una granja con inventario disponible.</summary>
    Task<Granja> GuardarGranjaAsync(Granja granja, CancellationToken ct = default);

    /// <summary>Elimina una granja solo si nada la referencia; si tiene historia, hay que inactivarla.</summary>
    Task EliminarGranjaAsync(int granjaId, CancellationToken ct = default);

    /// <summary>Crea o actualiza un programa y lo asocia exactamente a las granjas indicadas.</summary>
    Task<Programa> GuardarProgramaAsync(Programa programa, IReadOnlyCollection<int> granjaIds, CancellationToken ct = default);

    Task EliminarProgramaAsync(int programaId, CancellationToken ct = default);

    Task<ConfiguracionFirma> GetFirmaAsync(CancellationToken ct = default);

    /// <summary>Guarda la autoridad firmante. La imagen, si se informa, debe ser un PNG o JPG válido.</summary>
    Task GuardarFirmaAsync(ConfiguracionFirma firma, CancellationToken ct = default);

    Task<ConfiguracionSistema> GetSistemaAsync(CancellationToken ct = default);

    Task GuardarPreferenciasDeRespaldoAsync(bool respaldoAlSalir, string? carpeta, CancellationToken ct = default);
}

public sealed class CatalogoService(IUnitOfWorkFactory fabrica) : ICatalogoService
{
    private static readonly string[] RazasComunes =
    [
        "Holstein", "Jersey", "Normando", "Pardo Suizo", "Ayrshire", "Gyr", "Brahman", "Cebú", "Simmental", "Angus",
        "Romosinuano", "Blanco Orejinegro (BON)", "Criollo", "Cruce",
    ];

    public async Task<IReadOnlyList<Granja>> GetGranjasAsync(bool soloActivas = false, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var granjas = await uow.Granjas.GetAllAsync(ct);
        return granjas.Where(g => !soloActivas || g.Activa).OrderBy(g => g.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<Programa>> GetProgramasAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return (await uow.Programas.GetAllAsync(ct)).OrderBy(p => p.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<Programa>> GetProgramasDeGranjaAsync(int granjaId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var ids = (await uow.GranjaProgramas.GetProgramaIdsAsync(granjaId, ct)).ToHashSet();
        return (await uow.Programas.GetAllAsync(ct)).Where(p => ids.Contains(p.Id)).OrderBy(p => p.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<OrientacionProductiva>> GetOrientacionesAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return await uow.Orientaciones.GetAllAsync(ct);
    }

    public async Task<IReadOnlyList<CategoriaEtapa>> GetCategoriasAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return await uow.Categorias.GetAllAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetRazasAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        var usadas = (await uow.Animales.GetAllAsync(ct)).Select(a => a.Raza);
        return usadas.Concat(RazasComunes)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<int>> GetGranjaIdsDeProgramaAsync(int programaId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return await uow.GranjaProgramas.GetGranjaIdsAsync(programaId, ct);
    }

    public async Task<Granja> GuardarGranjaAsync(Granja granja, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(granja);
        var codigo = (granja.Codigo ?? string.Empty).Trim();
        var nombre = (granja.Nombre ?? string.Empty).Trim();
        Regla.ExigirTexto(codigo, "El código de la granja");
        Regla.ExigirTexto(nombre, "El nombre de la granja");

        await using var uow = await fabrica.BeginAsync(ct);
        var id = granja.Id == 0 ? (int?)null : granja.Id;
        Regla.Exigir(!await uow.Granjas.ExisteCodigoAsync(codigo, id, ct), $"Ya existe una granja con el código {codigo}.");
        Regla.Exigir(!await uow.Granjas.ExisteNombreAsync(nombre, id, ct), $"Ya existe una granja con el nombre {nombre}.");
        if (granja.OrientacionId is { } orientacionId)
        {
            Regla.Exigir(await uow.Orientaciones.GetByIdAsync(orientacionId, ct) is not null, "La orientación sugerida no existe.");
        }

        var guardada = new Granja
        {
            Id = granja.Id,
            Codigo = codigo,
            Nombre = nombre,
            Activa = granja.Activa,
            Observaciones = string.IsNullOrWhiteSpace(granja.Observaciones) ? null : granja.Observaciones.Trim(),
            OrientacionId = granja.OrientacionId,
        };

        if (guardada.Id == 0)
        {
            await uow.Granjas.InsertAsync(guardada, ct);
        }
        else
        {
            var actual = await uow.Granjas.GetByIdAsync(guardada.Id, ct) ?? throw Regla.Violada("La granja no existe.");
            if (actual.Activa && !guardada.Activa)
            {
                var inventario = await uow.Granjas.ContarInventarioActivoAsync(guardada.Id, ct);
                Regla.Exigir(
                    inventario == 0,
                    $"No se puede inactivar la granja {actual.Nombre}: tiene {inventario} semovientes o lotes disponibles. Trasládelos o deles salida primero.");
            }

            await uow.Granjas.UpdateAsync(guardada, ct);
        }

        await uow.CommitAsync(ct);
        return guardada;
    }

    public async Task EliminarGranjaAsync(int granjaId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var granja = await uow.Granjas.GetByIdAsync(granjaId, ct) ?? throw Regla.Violada("La granja no existe.");
        Regla.Exigir(
            !await uow.Granjas.TieneReferenciasAsync(granjaId, ct),
            $"La granja {granja.Nombre} tiene animales, documentos o movimientos registrados y no se puede eliminar. Inactívela para dejar de usarla.");

        await uow.GranjaProgramas.EliminarPorGranjaAsync(granjaId, ct);
        await uow.Granjas.DeleteAsync(granjaId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<Programa> GuardarProgramaAsync(Programa programa, IReadOnlyCollection<int> granjaIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(programa);
        ArgumentNullException.ThrowIfNull(granjaIds);
        var codigo = (programa.Codigo ?? string.Empty).Trim();
        var nombre = (programa.Nombre ?? string.Empty).Trim();
        Regla.ExigirTexto(codigo, "El código del programa");
        Regla.ExigirTexto(nombre, "El nombre del programa");
        Regla.Exigir(
            programa.TipoInventario is TipoInventario.Individual or TipoInventario.Lote,
            "El tipo de inventario debe ser INDIVIDUAL (por chapeta) o LOTE (por población).");

        await using var uow = await fabrica.BeginAsync(ct);
        Regla.Exigir(
            !await uow.Programas.ExisteCodigoAsync(codigo, programa.Id == 0 ? null : programa.Id, ct),
            $"Ya existe un programa con el código {codigo}.");
        foreach (var id in granjaIds)
        {
            Regla.Exigir(await uow.Granjas.GetByIdAsync(id, ct) is not null, "Una de las granjas seleccionadas no existe.");
        }

        var guardado = new Programa
        {
            Id = programa.Id,
            Codigo = codigo,
            Nombre = nombre,
            TipoInventario = programa.TipoInventario,
            Observaciones = string.IsNullOrWhiteSpace(programa.Observaciones) ? null : programa.Observaciones.Trim(),
        };

        if (guardado.Id == 0)
        {
            await uow.Programas.InsertAsync(guardado, ct);
        }
        else
        {
            Regla.Exigir(await uow.Programas.GetByIdAsync(guardado.Id, ct) is not null, "El programa no existe.");
            await uow.Programas.UpdateAsync(guardado, ct);
        }

        await uow.GranjaProgramas.ReemplazarGranjasDeProgramaAsync(guardado.Id, granjaIds, ct);
        await uow.CommitAsync(ct);
        return guardado;
    }

    public async Task EliminarProgramaAsync(int programaId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var programa = await uow.Programas.GetByIdAsync(programaId, ct) ?? throw Regla.Violada("El programa no existe.");
        Regla.Exigir(
            !await uow.Programas.TieneReferenciasAsync(programaId, ct),
            $"El programa {programa.Nombre} tiene categorías, oficios o lotes asociados y no se puede eliminar.");

        await uow.GranjaProgramas.EliminarPorProgramaAsync(programaId, ct);
        await uow.Programas.DeleteAsync(programaId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<ConfiguracionFirma> GetFirmaAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return await uow.Configuracion.GetFirmaAsync(ct) ?? new ConfiguracionFirma
        {
            NombreAutoridad = string.Empty,
            CargoAutoridad = string.Empty,
            EntidadFacultad = "FACULTAD DE CIENCIAS AGROPECUARIAS",
        };
    }

    public async Task GuardarFirmaAsync(ConfiguracionFirma firma, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(firma);
        Regla.ExigirTexto(firma.NombreAutoridad, "El nombre de la autoridad");
        Regla.ExigirTexto(firma.CargoAutoridad, "El cargo");
        Regla.ExigirTexto(firma.EntidadFacultad, "El nombre del departamento o facultad");
        if (!string.IsNullOrWhiteSpace(firma.RutaImagenFirma))
        {
            Regla.Exigir(
                FirmaBlockWriter.CargarImagen(firma.RutaImagenFirma) is not null,
                "La imagen de la firma no es un archivo PNG o JPG válido.");
        }

        await using var uow = await fabrica.BeginAsync(ct);
        await uow.Configuracion.GuardarFirmaAsync(new ConfiguracionFirma
        {
            NombreAutoridad = firma.NombreAutoridad.Trim(),
            CargoAutoridad = firma.CargoAutoridad.Trim(),
            EntidadFacultad = firma.EntidadFacultad.Trim(),
            Telefono = string.IsNullOrWhiteSpace(firma.Telefono) ? null : firma.Telefono.Trim(),
            RutaImagenFirma = string.IsNullOrWhiteSpace(firma.RutaImagenFirma) ? null : firma.RutaImagenFirma.Trim(),
        }, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<ConfiguracionSistema> GetSistemaAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return await uow.Configuracion.GetSistemaAsync(ct) ?? throw Regla.Violada("El sistema aún no tiene contraseña configurada.");
    }

    public async Task GuardarPreferenciasDeRespaldoAsync(bool respaldoAlSalir, string? carpeta, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var configuracion = await uow.Configuracion.GetSistemaAsync(ct) ?? throw Regla.Violada("El sistema aún no tiene contraseña configurada.");
        configuracion.BackupAutomaticoSalir = respaldoAlSalir;
        configuracion.RutaBackupPredeterminada = string.IsNullOrWhiteSpace(carpeta) ? Rutas.CarpetaRespaldosPredeterminada : carpeta.Trim();
        await uow.Configuracion.GuardarSistemaAsync(configuracion, ct);
        await uow.CommitAsync(ct);
    }
}
