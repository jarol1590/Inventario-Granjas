using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Data.Repositories;
using UCaldas.Granjas.Domain.Abstractions;

namespace UCaldas.Granjas.Data;

/// <summary>Una conexión, una transacción opcional y los repositorios que las comparten.</summary>
internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction? _transaction;
    private bool _finalizada;

    private IConfiguracionRepository? _configuracion;
    private IGranjaRepository? _granjas;
    private IProgramaRepository? _programas;
    private IGranjaProgramaRepository? _granjaProgramas;
    private IOrientacionRepository? _orientaciones;
    private ICategoriaRepository? _categorias;
    private IVigenciaRepository? _vigencias;
    private ITarifaRepository? _tarifas;
    private ITarifaProgramaRepository? _tarifasPrograma;
    private IRazaLineaRepository? _razas;
    private IAnimalRepository? _animales;
    private IGenealogiaRepository? _genealogias;
    private ILoteRepository? _lotes;
    private IOficioRepository? _oficios;
    private IRemisionRepository? _remisiones;
    private IRemisionDetalleRepository? _remisionDetalles;
    private IMovimientoRepository? _movimientos;

    public UnitOfWork(SqliteConnection connection, SqliteTransaction? transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public IConfiguracionRepository Configuracion => _configuracion ??= new ConfiguracionRepository(_connection, _transaction);

    public IGranjaRepository Granjas => _granjas ??= new GranjaRepository(_connection, _transaction);

    public IProgramaRepository Programas => _programas ??= new ProgramaRepository(_connection, _transaction);

    public IGranjaProgramaRepository GranjaProgramas => _granjaProgramas ??= new GranjaProgramaRepository(_connection, _transaction);

    public IOrientacionRepository Orientaciones => _orientaciones ??= new OrientacionRepository(_connection, _transaction);

    public ICategoriaRepository Categorias => _categorias ??= new CategoriaRepository(_connection, _transaction);

    public IVigenciaRepository Vigencias => _vigencias ??= new VigenciaRepository(_connection, _transaction);

    public ITarifaRepository Tarifas => _tarifas ??= new TarifaRepository(_connection, _transaction);

    public ITarifaProgramaRepository TarifasPrograma => _tarifasPrograma ??= new TarifaProgramaRepository(_connection, _transaction);
    public IRazaLineaRepository Razas => _razas ??= new RazaLineaRepository(_connection, _transaction);

    public IAnimalRepository Animales => _animales ??= new AnimalRepository(_connection, _transaction);

    public IGenealogiaRepository Genealogias => _genealogias ??= new GenealogiaRepository(_connection, _transaction);

    public ILoteRepository Lotes => _lotes ??= new LoteRepository(_connection, _transaction);

    public IOficioRepository Oficios => _oficios ??= new OficioRepository(_connection, _transaction);

    public IRemisionRepository Remisiones => _remisiones ??= new RemisionRepository(_connection, _transaction);

    public IRemisionDetalleRepository RemisionDetalles => _remisionDetalles ??= new RemisionDetalleRepository(_connection, _transaction);

    public IMovimientoRepository Movimientos => _movimientos ??= new MovimientoRepository(_connection, _transaction);

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null || _finalizada)
        {
            return;
        }

        await _transaction.CommitAsync(ct);
        _finalizada = true;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null || _finalizada)
        {
            return;
        }

        await _transaction.RollbackAsync(ct);
        _finalizada = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            if (!_finalizada)
            {
                try
                {
                    await _transaction.RollbackAsync();
                }
                catch (Exception)
                {
                    // La conexión ya no es utilizable; SQLite revierte la transacción al cerrarla.
                }
            }

            await _transaction.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }
}

public sealed class UnitOfWorkFactory(IDbConnectionFactory conexiones) : IUnitOfWorkFactory
{
    public async Task<IUnitOfWork> BeginAsync(CancellationToken ct = default)
    {
        var connection = await conexiones.OpenAsync(ct);
        try
        {
            // Serializable (por defecto) = BEGIN IMMEDIATE: toma el bloqueo de escritura desde el inicio.
            var transaction = connection.BeginTransaction();
            return new UnitOfWork(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<IUnitOfWork> OpenReadOnlyAsync(CancellationToken ct = default)
    {
        var connection = await conexiones.OpenAsync(ct);
        return new UnitOfWork(connection, transaction: null);
    }
}
