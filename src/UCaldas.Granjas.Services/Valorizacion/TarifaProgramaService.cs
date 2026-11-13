using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Valorizacion;

/// <summary>
/// Categorías/etapas y valores de referencia de los programas SIN valorización biológica (todo menos Bovinos). No hay
/// vigencias ni cálculo por edad: el valor en libros es el costo de ingreso o el valor de referencia de la categoría.
/// </summary>
public interface ITarifaProgramaService
{
    Task<IReadOnlyList<TarifaCategoriaPrograma>> ListarAsync(int programaId, CancellationToken ct = default);

    /// <summary>
    /// Deja la tabla del programa exactamente como las filas recibidas: agrega las nuevas (Id = 0), actualiza las existentes
    /// y elimina las que falten. La categoría «General» no se puede borrar ni renombrar y una categoría en uso no se elimina.
    /// </summary>
    Task GuardarAsync(int programaId, IReadOnlyList<TarifaCategoriaPrograma> filas, CancellationToken ct = default);

    /// <summary>Valor de referencia de una categoría (0 si no tiene tarifa).</summary>
    Task<decimal> ValorDeReferenciaAsync(int categoriaId, CancellationToken ct = default);
}

public sealed class TarifaProgramaService(IUnitOfWorkFactory fabrica) : ITarifaProgramaService
{
    public async Task<IReadOnlyList<TarifaCategoriaPrograma>> ListarAsync(int programaId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return await uow.TarifasPrograma.GetByProgramaAsync(programaId, ct);
    }

    public async Task<decimal> ValorDeReferenciaAsync(int categoriaId, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return (await uow.TarifasPrograma.GetByCategoriaAsync(categoriaId, ct))?.ValorReferencia ?? 0m;
    }

    public async Task GuardarAsync(int programaId, IReadOnlyList<TarifaCategoriaPrograma> filas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filas);

        await using var uow = await fabrica.BeginAsync(ct);
        var programa = await uow.Programas.GetByIdAsync(programaId, ct) ?? throw Regla.Violada("El programa no existe.");
        Regla.Exigir(
            !programa.UsaValorizacionBiologica,
            $"El programa {programa.Nombre} usa la matriz de valorización por edades; sus tarifas se editan en la matriz.");

        // Validación de forma de todas las filas antes de tocar la base.
        var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fila in filas)
        {
            var nombre = (fila.NombreCategoria ?? string.Empty).Trim();
            Regla.Exigir(nombre.Length > 0, "Toda categoría o etapa debe tener nombre.");
            Regla.Exigir(nombres.Add(nombre), $"La categoría «{nombre}» está repetida en el programa.");
            Regla.Exigir(UnidadCobro.Todas.Contains(fila.UnidadCobro), $"La unidad de cobro de «{nombre}» debe ser UNIDAD o KG.");
            Regla.Exigir(fila.ValorReferencia >= 0, $"El valor de referencia de «{nombre}» no puede ser negativo.");
            Regla.Exigir(
                !nombre.Equals(NombresBase.CategoriaSinClasificar, StringComparison.OrdinalIgnoreCase),
                $"«{NombresBase.CategoriaSinClasificar}» es un nombre reservado de la ganadería.");
        }

        var existentes = (await uow.TarifasPrograma.GetByProgramaAsync(programaId, ct)).ToDictionary(t => t.Id);
        var categorias = (await uow.Categorias.GetByProgramaAsync(programaId, ct)).ToDictionary(c => c.Id);

        foreach (var fila in filas.Where(f => f.Id != 0))
        {
            Regla.Exigir(existentes.ContainsKey(fila.Id), "Una de las categorías ya no existe; recargue la pantalla.");
        }

        // 1) Filas que faltan: se eliminan (nunca la General ni una categoría con animales o lotes).
        var animales = (await uow.Animales.GetAllAsync(ct)).Select(a => a.CategoriaId).ToHashSet();
        var lotes = (await uow.Lotes.GetAllAsync(ct)).Where(l => l.ProgramaId == programaId).Select(l => l.EtapaId).ToHashSet();
        var conservadas = filas.Where(f => f.Id != 0).Select(f => f.Id).ToHashSet();
        foreach (var quitada in existentes.Values.Where(e => !conservadas.Contains(e.Id)).ToList())
        {
            Regla.Exigir(!quitada.EsDefault, $"La categoría «{quitada.NombreCategoria}» es la predeterminada del programa y no se puede eliminar.");
            Regla.Exigir(
                !animales.Contains(quitada.CategoriaId) && !lotes.Contains(quitada.CategoriaId),
                $"La categoría «{quitada.NombreCategoria}» tiene animales o lotes registrados y no se puede eliminar.");
            await uow.TarifasPrograma.DeleteAsync(quitada.Id, ct);
            await uow.Categorias.DeleteAsync(quitada.CategoriaId, ct);
        }

        // 2) Existentes: se actualizan (la General conserva su nombre). 3) Nuevas: se crean con su categoría.
        foreach (var fila in filas)
        {
            var nombre = fila.NombreCategoria.Trim();
            if (fila.Id != 0)
            {
                var actual = existentes[fila.Id];
                Regla.Exigir(
                    !actual.EsDefault || nombre.Equals(actual.NombreCategoria, StringComparison.OrdinalIgnoreCase),
                    $"La categoría «{actual.NombreCategoria}» es la predeterminada y no se puede renombrar.");

                actual.NombreCategoria = nombre;
                actual.UnidadCobro = fila.UnidadCobro;
                actual.ValorReferencia = fila.ValorReferencia;
                actual.Activo = fila.Activo;
                await uow.TarifasPrograma.UpdateAsync(actual, ct);

                var categoria = categorias[actual.CategoriaId];
                if (!categoria.Nombre.Equals(nombre, StringComparison.Ordinal))
                {
                    categoria.Nombre = nombre;
                    await uow.Categorias.UpdateAsync(categoria, ct);
                }

                continue;
            }

            var nueva = new CategoriaEtapa { Nombre = nombre, Sexo = Sexo.Ambos, ProgramaId = programaId };
            await uow.Categorias.InsertAsync(nueva, ct);
            await uow.TarifasPrograma.InsertAsync(new TarifaCategoriaPrograma
            {
                ProgramaId = programaId,
                CategoriaId = nueva.Id,
                NombreCategoria = nombre,
                UnidadCobro = fila.UnidadCobro,
                ValorReferencia = fila.ValorReferencia,
                EsDefault = false,
                Activo = fila.Activo,
            }, ct);
        }

        await uow.CommitAsync(ct);
    }
}