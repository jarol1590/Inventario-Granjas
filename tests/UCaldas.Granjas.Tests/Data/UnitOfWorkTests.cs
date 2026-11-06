using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Data;

public class UnitOfWorkTests
{
    private static Granja NuevaGranja(string codigo) => new() { Codigo = codigo, Nombre = $"Granja {codigo}" };

    [Fact]
    public async Task Commit_persiste_los_cambios()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        await using (var uow = await db.Uow.BeginAsync())
        {
            await uow.Granjas.InsertAsync(NuevaGranja("G-10"));
            await uow.CommitAsync();
        }

        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        (await lectura.Granjas.GetByCodigoAsync("G-10")).Should().NotBeNull();
    }

    [Fact]
    public async Task Liberar_sin_commit_revierte_todo()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        await using (var uow = await db.Uow.BeginAsync())
        {
            await uow.Granjas.InsertAsync(NuevaGranja("G-11"));
            await uow.Granjas.InsertAsync(NuevaGranja("G-12"));
        }

        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(3);
    }

    [Fact]
    public async Task Rollback_explicito_descarta_los_cambios_y_commit_posterior_no_hace_nada()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        await using (var uow = await db.Uow.BeginAsync())
        {
            await uow.Granjas.InsertAsync(NuevaGranja("G-13"));
            await uow.RollbackAsync();
            await uow.CommitAsync();
        }

        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(3);
    }

    [Fact]
    public async Task Una_excepcion_a_mitad_de_la_operacion_no_deja_cambios_parciales()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = async () =>
        {
            await using var uow = await db.Uow.BeginAsync();
            await uow.Granjas.InsertAsync(NuevaGranja("G-14"));
            await uow.Granjas.InsertAsync(NuevaGranja("G-14")); // código duplicado: viola UNIQUE
            await uow.CommitAsync();
        };

        await act.Should().ThrowAsync<Microsoft.Data.Sqlite.SqliteException>();
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA WHERE codigo = 'G-14'")).Should().Be(0);
    }

    [Fact]
    public async Task La_sesion_de_solo_lectura_consulta_sin_transaccion()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var lectura = await db.Uow.OpenReadOnlyAsync();

        var granjas = await lectura.Granjas.GetAllAsync();
        await lectura.CommitAsync();

        granjas.Should().HaveCount(3);
    }

    [Fact]
    public async Task Los_repositorios_de_una_misma_unidad_comparten_la_transaccion()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();

        var programa = new Programa { Codigo = "PRG-PIS", Nombre = "Piscicultura", TipoInventario = "LOTE" };
        await uow.Programas.InsertAsync(programa);
        await uow.GranjaProgramas.ReemplazarGranjasDeProgramaAsync(programa.Id, [(await uow.Granjas.GetByCodigoAsync("G-03"))!.Id]);

        // Aún sin commit, otra conexión no ve los cambios; la misma unidad sí.
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM PROGRAMA")).Should().Be(2);
        (await uow.Programas.GetByCodigoAsync("PRG-PIS")).Should().NotBeNull();
    }
}
