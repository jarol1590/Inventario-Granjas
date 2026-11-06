using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Valorizacion;

public class VigenciaServiceTests
{
    private static VigenciaService Crear(BaseDeDatosDePrueba db) => new(db.Uow, new TarifaValidationService());

    private static async Task<int> VigenciaIdAsync(BaseDeDatosDePrueba db, int anio)
        => await db.EscalarAsync<int>("SELECT id FROM VIGENCIA_VALORIZACION WHERE anio = @anio", new { anio });

    [Fact]
    public async Task Listar_devuelve_las_vigencias_con_la_mas_reciente_primero()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var servicio = Crear(db);
        await servicio.CrearAsync(2025);
        await servicio.CrearAsync(2027);

        var vigencias = await servicio.ListarAsync();

        vigencias.Select(v => v.Anio).Should().Equal(2027, 2026, 2025);
        vigencias.Single(v => v.Activa).Anio.Should().Be(2026);
    }

    [Fact]
    public async Task La_matriz_se_devuelve_en_el_orden_de_la_pantalla_de_tarifas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var vigenciaId = await VigenciaIdAsync(db, 2026);

        var filas = await Crear(db).GetMatrizAsync(vigenciaId);

        filas.Should().HaveCount(9);
        filas.Select(f => (f.CategoriaId, f.EdadMinimaMeses, f.Sexo)).Should().BeInAscendingOrder();
        filas.Take(3).Select(f => (f.EdadMinimaMeses, f.Sexo, f.ValorEstandar)).Should().Equal(
            (0, Sexo.Hembra, 150_000m), (0, Sexo.Macho, 160_000m), (3, Sexo.Ambos, 800_000m));
        (await Crear(db).GetMatrizAsync(9999)).Should().BeEmpty();
    }

    [Fact]
    public async Task Crear_agrega_una_vigencia_inactiva_y_vacia()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var vigencia = await Crear(db).CrearAsync(2027, "Borrador");

        vigencia.Id.Should().BeGreaterThan(0);
        vigencia.Activa.Should().BeFalse();
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM TABLA_VALORIZACION_BASE WHERE vigencia_id = @id", new { id = vigencia.Id })).Should().Be(0);
        (await db.EscalarAsync<int>("SELECT anio FROM VIGENCIA_VALORIZACION WHERE activa = 1")).Should().Be(2026);
    }

    [Theory]
    [InlineData(2026, "*Ya existe una vigencia*")]
    [InlineData(1999, "*El año debe estar entre*")]
    [InlineData(2101, "*El año debe estar entre*")]
    public async Task Crear_rechaza_anios_repetidos_o_fuera_de_rango(int anio, string mensaje)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).CrearAsync(anio);

        await act.Should().ThrowAsync<ValidationException>().WithMessage(mensaje);
    }

    [Fact]
    public async Task Clonar_copia_todas_las_tarifas_a_la_nueva_vigencia_sin_activarla()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var origenId = await VigenciaIdAsync(db, 2026);

        var clon = await Crear(db).ClonarAsync(origenId, 2027);

        clon.Anio.Should().Be(2027);
        clon.Activa.Should().BeFalse();
        clon.Observaciones.Should().Contain("2026");
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var originales = await uow.Tarifas.GetByVigenciaAsync(origenId);
        var copiadas = await uow.Tarifas.GetByVigenciaAsync(clon.Id);
        copiadas.Should().HaveCount(9);
        copiadas.Select(t => (t.CategoriaId, t.OrientacionId, t.EdadMinimaMeses, t.EdadMaximaMeses, t.Sexo, t.RequierePartos, t.ValorEstandar))
            .Should().BeEquivalentTo(originales.Select(t => (t.CategoriaId, t.OrientacionId, t.EdadMinimaMeses, t.EdadMaximaMeses, t.Sexo, t.RequierePartos, t.ValorEstandar)));
        copiadas.Select(t => t.Id).Should().NotIntersectWith(originales.Select(t => t.Id));
        originales.Should().HaveCount(9, "la vigencia origen queda intacta");
    }

    [Fact]
    public async Task Clonar_a_un_anio_existente_o_desde_una_vigencia_inexistente_falla()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var origenId = await VigenciaIdAsync(db, 2026);

        var repetido = () => Crear(db).ClonarAsync(origenId, 2026);
        var inexistente = () => Crear(db).ClonarAsync(9999, 2030);

        await repetido.Should().ThrowAsync<ValidationException>().WithMessage("*Ya existe una vigencia*");
        await inexistente.Should().ThrowAsync<ValidationException>().WithMessage("*no existe*");
    }

    [Fact]
    public async Task GuardarMatriz_reemplaza_las_filas_de_la_vigencia()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var servicio = Crear(db);
        var vigencia = await servicio.CrearAsync(2027);
        IdsBase ids;
        await using (var uow = await db.Uow.OpenReadOnlyAsync())
        {
            ids = await Datos.IdsBaseAsync(uow);
        }

        var filas = new[]
        {
            Matriz2026.Fila(0, ids.Categoria(NombresBase.CategoriaTerneros), null, 0, 2, Sexo.Hembra, false, 175_000m),
            Matriz2026.Fila(0, ids.Categoria(NombresBase.CategoriaTerneros), null, 3, 7, Sexo.Ambos, false, 850_000m),
        };

        await servicio.GuardarMatrizAsync(vigencia.Id, filas);

        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        var guardadas = await lectura.Tarifas.GetByVigenciaAsync(vigencia.Id);
        guardadas.Select(t => t.ValorEstandar).Should().Equal(175_000m, 850_000m);
        guardadas.Should().OnlyContain(t => t.VigenciaId == vigencia.Id);
    }

    [Fact]
    public async Task GuardarMatriz_con_solapamiento_se_bloquea_y_no_modifica_lo_guardado()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var vigenciaId = await VigenciaIdAsync(db, 2026);
        List<Domain.Entities.TablaValorizacionBase> filas;
        await using (var uow = await db.Uow.OpenReadOnlyAsync())
        {
            filas = (await uow.Tarifas.GetByVigenciaAsync(vigenciaId)).ToList();
            var vacasLacteo = filas.First(f => f.RequierePartos && f.OrientacionId is not null);
            filas.Add(new Domain.Entities.TablaValorizacionBase
            {
                CategoriaId = vacasLacteo.CategoriaId, OrientacionId = vacasLacteo.OrientacionId, Sexo = vacasLacteo.Sexo,
                RequierePartos = true, EdadMinimaMeses = 30, EdadMaximaMeses = 40, ValorEstandar = 1m,
            });
        }

        var act = () => Crear(db).GuardarMatrizAsync(vigenciaId, filas);

        await act.Should().ThrowAsync<SolapamientoDeTarifasException>().WithMessage("*Vacas General*30–40*");
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM TABLA_VALORIZACION_BASE WHERE vigencia_id = @vigenciaId", new { vigenciaId })).Should().Be(9);
    }

    [Fact]
    public async Task Activar_exige_tarifas_y_deja_una_sola_vigencia_activa()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var servicio = Crear(db);
        var vacia = await servicio.CrearAsync(2027);

        var sinTarifas = () => servicio.ActivarAsync(vacia.Id);
        await sinTarifas.Should().ThrowAsync<ValidationException>().WithMessage("*no tiene tarifas*");

        var clon = await servicio.ClonarAsync(await VigenciaIdAsync(db, 2026), 2028);
        await servicio.ActivarAsync(clon.Id);

        (await db.EscalarAsync<int>("SELECT anio FROM VIGENCIA_VALORIZACION WHERE activa = 1")).Should().Be(2028);
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM VIGENCIA_VALORIZACION WHERE activa = 1")).Should().Be(1);
    }

    [Fact]
    public async Task Activar_una_vigencia_inexistente_falla_sin_desactivar_la_actual()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => Crear(db).ActivarAsync(9999);

        await act.Should().ThrowAsync<ValidationException>();
        (await db.EscalarAsync<int>("SELECT anio FROM VIGENCIA_VALORIZACION WHERE activa = 1")).Should().Be(2026);
    }

    [Fact]
    public void Reajuste_porcentual_redondea_al_peso_y_no_modifica_las_filas_originales()
    {
        var originales = new[]
        {
            Matriz2026.Fila(1, Matriz2026.Terneros, null, 0, 2, Sexo.Hembra, false, 150_000m),
            Matriz2026.Fila(2, Matriz2026.Terneros, null, 0, 2, Sexo.Macho, false, 160_000m),
            Matriz2026.Fila(3, Matriz2026.Levante, null, 8, 11, Sexo.Ambos, false, 1_001m),
        };
        var servicio = new VigenciaService(null!, new TarifaValidationService());

        var subidas = servicio.AplicarReajustePorcentual(originales, 5m);

        subidas.Select(f => f.ValorEstandar).Should().Equal(157_500m, 168_000m, 1_051m);
        originales.Select(f => f.ValorEstandar).Should().Equal(150_000m, 160_000m, 1_001m);
        subidas.Select(f => f.EdadMaximaMeses).Should().Equal(2, 2, 11);
    }

    [Fact]
    public void Reajuste_porcentual_negativo_reduce_los_valores()
    {
        var filas = new[] { Matriz2026.Fila(1, Matriz2026.Terneros, null, 0, 2, Sexo.Hembra, false, 150_000m) };
        var servicio = new VigenciaService(null!, new TarifaValidationService());

        servicio.AplicarReajustePorcentual(filas, -10m)[0].ValorEstandar.Should().Be(135_000m);
        servicio.AplicarReajustePorcentual(filas, 0m)[0].ValorEstandar.Should().Be(150_000m);
    }

    [Fact]
    public void Reajuste_de_menos_cien_por_ciento_o_menos_se_rechaza()
    {
        var servicio = new VigenciaService(null!, new TarifaValidationService());

        var act = () => servicio.AplicarReajustePorcentual([], -100m);

        act.Should().Throw<ValidationException>().WithMessage("*mayor que -100*");
    }
}
