using Dapper;
using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Data.Repositories;

internal sealed class AnimalRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<Animal>(connection, transaction), IAnimalRepository
{
    // Columnas editables: valor_compra_origen, fecha_registro, clase_entrada e id NO se actualizan nunca por esta vía.
    private const string UpdateEditableSql =
        """
        UPDATE ANIMAL SET
            chapeta_amarilla = @ChapetaAmarilla, chapeta_roja = @ChapetaRoja, nombre = @Nombre, sexo = @Sexo,
            fecha_nacimiento = @FechaNacimiento, fecha_compra = @FechaCompra, raza = @Raza,
            madre_identificacion = @MadreIdentificacion, peso_actual_kg = @PesoActualKg,
            orientacion_id = @OrientacionId, valor_contable_actual = @ValorContableActual,
            granja_id = @GranjaId, categoria_id = @CategoriaId, estado = @Estado,
            numero_partos = @NumeroPartos, exento_valorizacion = @ExentoValorizacion
        WHERE id = @Id
        """;

    public override Task<int> InsertAsync(Animal entity, CancellationToken ct = default)
    {
        if (entity.FechaRegistro == default)
        {
            entity.FechaRegistro = DateTime.Now;
        }

        return base.InsertAsync(entity, ct);
    }

    public override async Task UpdateAsync(Animal entity, CancellationToken ct = default)
    {
        var filas = await Connection.ExecuteAsync(Cmd(UpdateEditableSql, entity, ct));
        if (filas == 0)
        {
            throw new InvalidOperationException($"No se encontró el animal con id {entity.Id} que se intentó actualizar.");
        }
    }

    public Task<Animal?> GetByChapetaAsync(string chapetaAmarilla, CancellationToken ct = default)
        => UnoAsync<Animal>("SELECT * FROM ANIMAL WHERE chapeta_amarilla = @chapetaAmarilla", new { chapetaAmarilla }, ct);

    public Task<bool> ExisteChapetaAsync(string chapetaAmarilla, int? excluirId = null, CancellationToken ct = default)
        => ExisteAsync(
            "SELECT COUNT(1) FROM ANIMAL WHERE chapeta_amarilla = @chapetaAmarilla AND (@excluirId IS NULL OR id <> @excluirId)",
            new { chapetaAmarilla, excluirId }, ct);

    public Task<IReadOnlyList<Animal>> GetByEstadoAsync(string estado, CancellationToken ct = default)
        => ListaAsync<Animal>("SELECT * FROM ANIMAL WHERE estado = @estado ORDER BY id", new { estado }, ct);

    public Task<IReadOnlyList<Animal>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var lista = ids.Distinct().ToArray();
        return lista.Length == 0
            ? Task.FromResult<IReadOnlyList<Animal>>([])
            : ListaAsync<Animal>("SELECT * FROM ANIMAL WHERE id IN @lista ORDER BY id", new { lista }, ct);
    }

    public Task ActualizarValoracionAsync(int animalId, int categoriaId, decimal valorContable, CancellationToken ct = default)
        => EjecutarAsync(
            "UPDATE ANIMAL SET categoria_id = @categoriaId, valor_contable_actual = @valorContable WHERE id = @animalId",
            new { animalId, categoriaId, valorContable }, ct);

    public Task ActualizarValorCompraOrigenAsync(int animalId, decimal valor, CancellationToken ct = default)
        => EjecutarAsync("UPDATE ANIMAL SET valor_compra_origen = @valor WHERE id = @animalId", new { animalId, valor }, ct);
}

internal sealed class GenealogiaRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<DatosGenealogia>(connection, transaction), IGenealogiaRepository
{
    public Task<DatosGenealogia?> GetByAnimalIdAsync(int animalId, CancellationToken ct = default)
        => UnoAsync<DatosGenealogia>("SELECT * FROM DATOS_GENEALOGIA WHERE animal_id = @animalId", new { animalId }, ct);

    public Task EliminarPorAnimalAsync(int animalId, CancellationToken ct = default)
        => EjecutarAsync("DELETE FROM DATOS_GENEALOGIA WHERE animal_id = @animalId", new { animalId }, ct);

    public Task<bool> EsPadreAsync(int animalId, CancellationToken ct = default)
        => ExisteAsync("SELECT COUNT(1) FROM DATOS_GENEALOGIA WHERE padre_id = @animalId", new { animalId }, ct);
}

internal sealed class LoteRepository(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<LotePoblacion>(connection, transaction), ILoteRepository;
