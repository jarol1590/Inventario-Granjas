using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Domain.Abstractions;

namespace UCaldas.Granjas.Data.Repositories;

/// <summary>
/// SQL de CRUD generado por reflexión a partir de la entidad: la tabla es el nombre de la clase en
/// MAYÚSCULAS_CON_GUION_BAJO y las columnas son las propiedades en minúscula_con_guion_bajo.
/// </summary>
internal static class EntityMap<T> where T : class
{
    public static readonly string Tabla;
    public static readonly string SelectPorIdSql;
    public static readonly string SelectTodosSql;
    public static readonly string InsertSql;
    public static readonly string UpdateSql;
    public static readonly string DeleteSql;
    public static readonly Action<T, int> AsignarId;

    static EntityMap()
    {
        var propiedades = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true })
            .ToArray();

        var id = propiedades.FirstOrDefault(p => p.Name == "Id" && p.PropertyType == typeof(int))
            ?? throw new InvalidOperationException($"{typeof(T).Name} debe tener una propiedad int Id.");

        AsignarId = (Action<T, int>)Delegate.CreateDelegate(typeof(Action<T, int>), id.GetSetMethod()!);

        Tabla = SqliteMapping.ASnakeMinuscula(typeof(T).Name).ToUpperInvariant();
        var datos = propiedades.Where(p => p.Name != "Id").ToArray();
        var columnas = string.Join(", ", datos.Select(p => SqliteMapping.ASnakeMinuscula(p.Name)));
        var parametros = string.Join(", ", datos.Select(p => "@" + p.Name));
        var asignaciones = string.Join(", ", datos.Select(p => $"{SqliteMapping.ASnakeMinuscula(p.Name)} = @{p.Name}"));

        SelectPorIdSql = $"SELECT * FROM {Tabla} WHERE id = @Id";
        SelectTodosSql = $"SELECT * FROM {Tabla} ORDER BY id";
        InsertSql = $"INSERT INTO {Tabla} ({columnas}) VALUES ({parametros}); SELECT last_insert_rowid();";
        UpdateSql = $"UPDATE {Tabla} SET {asignaciones} WHERE id = @Id";
        DeleteSql = $"DELETE FROM {Tabla} WHERE id = @Id";
    }
}

/// <summary>Base de los repositorios Dapper: comparten conexión y transacción de la unidad de trabajo.</summary>
internal abstract class RepositoryBase<T> : IRepository<T> where T : class
{
    protected RepositoryBase(SqliteConnection connection, SqliteTransaction? transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    protected SqliteConnection Connection { get; }

    protected SqliteTransaction? Transaction { get; }

    protected CommandDefinition Cmd(string sql, object? parametros = null, CancellationToken ct = default)
        => new(sql, parametros, Transaction, cancellationToken: ct);

    protected async Task<TResult?> UnoAsync<TResult>(string sql, object? parametros, CancellationToken ct)
        => await Connection.QueryFirstOrDefaultAsync<TResult>(Cmd(sql, parametros, ct));

    protected async Task<IReadOnlyList<TResult>> ListaAsync<TResult>(string sql, object? parametros, CancellationToken ct)
        => (await Connection.QueryAsync<TResult>(Cmd(sql, parametros, ct))).AsList();

    protected async Task<bool> ExisteAsync(string sql, object? parametros, CancellationToken ct)
        => await Connection.ExecuteScalarAsync<long>(Cmd(sql, parametros, ct)) > 0;

    protected async Task<long> EscalarAsync(string sql, object? parametros, CancellationToken ct)
        => await Connection.ExecuteScalarAsync<long>(Cmd(sql, parametros, ct));

    protected async Task EjecutarAsync(string sql, object? parametros, CancellationToken ct)
        => await Connection.ExecuteAsync(Cmd(sql, parametros, ct));

    public virtual Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
        => UnoAsync<T>(EntityMap<T>.SelectPorIdSql, new { Id = id }, ct);

    public virtual Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => ListaAsync<T>(EntityMap<T>.SelectTodosSql, null, ct);

    public virtual async Task<int> InsertAsync(T entity, CancellationToken ct = default)
    {
        var id = (int)await Connection.ExecuteScalarAsync<long>(Cmd(EntityMap<T>.InsertSql, entity, ct));
        EntityMap<T>.AsignarId(entity, id);
        return id;
    }

    public virtual async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        var filas = await Connection.ExecuteAsync(Cmd(EntityMap<T>.UpdateSql, entity, ct));
        if (filas == 0)
        {
            throw new InvalidOperationException($"No se encontró el registro que se intentó actualizar en {EntityMap<T>.Tabla}.");
        }
    }

    public virtual Task DeleteAsync(int id, CancellationToken ct = default)
        => EjecutarAsync(EntityMap<T>.DeleteSql, new { Id = id }, ct);
}

/// <summary>Repositorio sin consultas especiales: solo CRUD.</summary>
internal sealed class SimpleRepository<T>(SqliteConnection connection, SqliteTransaction? transaction)
    : RepositoryBase<T>(connection, transaction) where T : class;
