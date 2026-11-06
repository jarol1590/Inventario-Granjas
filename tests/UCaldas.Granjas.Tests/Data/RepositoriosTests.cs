using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Data;

public class RepositoriosTests
{
    [Fact]
    public async Task Animal_ida_y_vuelta_conserva_decimales_fechas_y_booleanos()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var animal = Datos.NuevoAnimal("0940-26", ids);
        animal.Nombre = "Lucero";
        animal.ChapetaRoja = "0940-26R";
        animal.ClaseEntrada = ClaseEntrada.Compra;
        animal.ValorCompraOrigen = 1234567.89m;
        animal.ValorContableActual = 3_200_000m;
        animal.PesoActualKg = 480.5m;
        animal.FechaNacimiento = new DateTime(2026, 6, 20, 13, 45, 10);
        animal.FechaCompra = new DateTime(2026, 10, 20);
        animal.NumeroPartos = 2;
        animal.ExentoValorizacion = true;
        animal.FechaRegistro = new DateTime(2026, 11, 6, 9, 30, 0);
        animal.MadreIdentificacion = "044";

        var id = await uow.Animales.InsertAsync(animal);
        await uow.CommitAsync();

        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        var leido = await lectura.Animales.GetByIdAsync(id);

        id.Should().BeGreaterThan(0);
        animal.Id.Should().Be(id);
        leido.Should().BeEquivalentTo(animal);
    }

    [Fact]
    public async Task Animal_sin_fecha_de_compra_ni_nombre_lee_nulos()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var animal = Datos.NuevoAnimal("0941-26", ids);
        animal.Sexo = Sexo.Macho;
        await uow.Animales.InsertAsync(animal);

        var leido = await uow.Animales.GetByChapetaAsync("0941-26");

        leido!.FechaCompra.Should().BeNull();
        leido.Nombre.Should().BeNull();
        leido.ChapetaRoja.Should().BeNull();
        leido.ExentoValorizacion.Should().BeFalse();
        leido.FechaRegistro.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1), "se asigna al insertar");
    }

    [Fact]
    public async Task Animal_UpdateAsync_nunca_modifica_los_campos_inmutables()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var animal = Datos.NuevoAnimal("0950-26", ids);
        animal.ClaseEntrada = ClaseEntrada.Compra;
        animal.ValorCompraOrigen = 500_000m;
        animal.FechaRegistro = new DateTime(2026, 10, 1, 8, 0, 0);
        await uow.Animales.InsertAsync(animal);

        animal.ValorCompraOrigen = 999m;
        animal.ClaseEntrada = ClaseEntrada.Nacimiento;
        animal.FechaRegistro = new DateTime(2000, 1, 1);
        animal.Nombre = "Lucero";
        animal.ValorContableActual = 6_000_000m;
        animal.NumeroPartos = 1;
        await uow.Animales.UpdateAsync(animal);

        var leido = await uow.Animales.GetByIdAsync(animal.Id);
        leido!.ValorCompraOrigen.Should().Be(500_000m, "el costo de origen es inmutable");
        leido.ClaseEntrada.Should().Be(ClaseEntrada.Compra);
        leido.FechaRegistro.Should().Be(new DateTime(2026, 10, 1, 8, 0, 0));
        leido.Nombre.Should().Be("Lucero");
        leido.ValorContableActual.Should().Be(6_000_000m);
        leido.NumeroPartos.Should().Be(1);
    }

    [Fact]
    public async Task Animal_ActualizarValorCompraOrigen_es_la_unica_via_para_corregir_la_factura()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var animal = Datos.NuevoAnimal("0960-26", ids);
        animal.ValorCompraOrigen = 500_000m;
        await uow.Animales.InsertAsync(animal);

        await uow.Animales.ActualizarValorCompraOrigenAsync(animal.Id, 750_000.50m);

        (await uow.Animales.GetByIdAsync(animal.Id))!.ValorCompraOrigen.Should().Be(750_000.50m);
    }

    [Fact]
    public async Task Animal_UpdateAsync_de_un_registro_inexistente_falla()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var fantasma = Datos.NuevoAnimal("NO-EXISTE", ids);
        fantasma.Id = 12345;

        var act = () => uow.Animales.UpdateAsync(fantasma);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Animal_ExisteChapeta_ignora_mayusculas_y_permite_excluir_al_propio_animal()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var animal = Datos.NuevoAnimal("004C", ids);
        await uow.Animales.InsertAsync(animal);

        (await uow.Animales.ExisteChapetaAsync("004c")).Should().BeTrue();
        (await uow.Animales.ExisteChapetaAsync("004c", excluirId: animal.Id)).Should().BeFalse();
        (await uow.Animales.ExisteChapetaAsync("999")).Should().BeFalse();
        (await uow.Animales.GetByChapetaAsync("004c"))!.Id.Should().Be(animal.Id);
    }

    [Fact]
    public async Task Animal_GetByIds_y_GetByEstado_filtran_correctamente()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var a = Datos.NuevoAnimal("A-1", ids);
        var b = Datos.NuevoAnimal("A-2", ids);
        var c = Datos.NuevoAnimal("A-3", ids);
        c.Estado = EstadoAnimal.Vendido;
        foreach (var animal in new[] { a, b, c })
        {
            await uow.Animales.InsertAsync(animal);
        }

        (await uow.Animales.GetByIdsAsync([])).Should().BeEmpty();
        (await uow.Animales.GetByIdsAsync([a.Id, c.Id, a.Id])).Select(x => x.ChapetaAmarilla).Should().Equal("A-1", "A-3");
        (await uow.Animales.GetByEstadoAsync(EstadoAnimal.Disponible)).Select(x => x.ChapetaAmarilla).Should().Equal("A-1", "A-2");
    }

    [Fact]
    public async Task Animal_ActualizarValoracion_cambia_solo_categoria_y_valor_contable()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var animal = Datos.NuevoAnimal("V-1", ids);
        await uow.Animales.InsertAsync(animal);

        await uow.Animales.ActualizarValoracionAsync(animal.Id, ids.Categoria(NombresBase.CategoriaLevante), 1_200_000m);

        var leido = await uow.Animales.GetByIdAsync(animal.Id);
        leido!.CategoriaId.Should().Be(ids.Categoria(NombresBase.CategoriaLevante));
        leido.ValorContableActual.Should().Be(1_200_000m);
        leido.ChapetaAmarilla.Should().Be("V-1");
    }

    [Fact]
    public async Task Vigencia_ActivarAsync_deja_una_sola_vigencia_activa()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var v2027 = new VigenciaValorizacion { Anio = 2027, Activa = false, Observaciones = "Borrador" };
        await uow.Vigencias.InsertAsync(v2027);

        await uow.Vigencias.ActivarAsync(v2027.Id);

        (await uow.Vigencias.GetActivaAsync())!.Anio.Should().Be(2027);
        (await uow.Vigencias.GetAllAsync()).Count(v => v.Activa).Should().Be(1);
        (await uow.Vigencias.GetByAnioAsync(2026))!.Activa.Should().BeFalse();
    }

    [Fact]
    public async Task Vigencia_ActivarAsync_de_una_inexistente_falla_sin_desactivar_la_actual()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();

        var act = () => uow.Vigencias.ActivarAsync(9999);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await uow.Vigencias.GetActivaAsync())!.Anio.Should().Be(2026);
    }

    [Fact]
    public async Task Tarifa_ReemplazarVigencia_sustituye_todas_las_filas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var vigencia = (await uow.Vigencias.GetActivaAsync())!;
        var nuevas = new[]
        {
            new TablaValorizacionBase
            {
                CategoriaId = ids.Categoria(NombresBase.CategoriaTerneros), EdadMinimaMeses = 0, EdadMaximaMeses = 5,
                Sexo = Sexo.Ambos, ValorEstandar = 100m,
            },
            new TablaValorizacionBase
            {
                CategoriaId = ids.Categoria(NombresBase.CategoriaLevante), EdadMinimaMeses = 6, EdadMaximaMeses = 11,
                Sexo = Sexo.Ambos, OrientacionId = ids.Lacteo, RequierePartos = true, ValorEstandar = 200.55m,
            },
        };

        await uow.Tarifas.ReemplazarVigenciaAsync(vigencia.Id, nuevas);

        var filas = await uow.Tarifas.GetByVigenciaAsync(vigencia.Id);
        filas.Should().HaveCount(2);
        filas.Should().OnlyContain(f => f.VigenciaId == vigencia.Id);
        filas.Single(f => f.ValorEstandar == 200.55m).OrientacionId.Should().Be(ids.Lacteo);
        filas.Single(f => f.ValorEstandar == 200.55m).RequierePartos.Should().BeTrue();
    }

    [Fact]
    public async Task Granja_valida_unicidad_de_codigo_y_nombre_ignorando_mayusculas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var tesorito = (await uow.Granjas.GetByCodigoAsync("g-01"))!;

        (await uow.Granjas.ExisteCodigoAsync("G-01")).Should().BeTrue();
        (await uow.Granjas.ExisteCodigoAsync("G-01", excluirId: tesorito.Id)).Should().BeFalse();
        (await uow.Granjas.ExisteNombreAsync("TESORITO")).Should().BeTrue();
        (await uow.Granjas.ExisteNombreAsync("TESORITO", excluirId: tesorito.Id)).Should().BeFalse();
        (await uow.Granjas.ExisteNombreAsync("Otra")).Should().BeFalse();
    }

    [Fact]
    public async Task Granja_cuenta_inventario_activo_y_detecta_referencias()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var vendido = Datos.NuevoAnimal("G-3", ids);
        vendido.Estado = EstadoAnimal.Vendido;
        foreach (var animal in new[] { Datos.NuevoAnimal("G-1", ids), Datos.NuevoAnimal("G-2", ids), vendido })
        {
            await uow.Animales.InsertAsync(animal);
        }

        (await uow.Granjas.ContarInventarioActivoAsync(ids.Tesorito)).Should().Be(2, "el vendido no cuenta");
        (await uow.Granjas.TieneReferenciasAsync(ids.Tesorito)).Should().BeTrue("tiene animales, aunque uno esté vendido");
        (await uow.Granjas.ContarInventarioActivoAsync(ids.LaCruz)).Should().Be(0);
        (await uow.Granjas.TieneReferenciasAsync(ids.LaCruz)).Should().BeFalse();
    }

    [Fact]
    public async Task Programa_detecta_referencias_de_categorias()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var gan = (await uow.Programas.GetByCodigoAsync("prg-gan"))!;
        var ave = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!;

        (await uow.Programas.TieneReferenciasAsync(gan.Id)).Should().BeTrue();
        (await uow.Programas.TieneReferenciasAsync(ave.Id)).Should().BeFalse();
        (await uow.Programas.ExisteCodigoAsync("prg-ave")).Should().BeTrue();
    }

    [Fact]
    public async Task GranjaPrograma_reemplaza_las_granjas_de_un_programa()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var ave = (await uow.Programas.GetByCodigoAsync("PRG-AVE"))!;

        await uow.GranjaProgramas.ReemplazarGranjasDeProgramaAsync(ave.Id, [ids.Tesorito, ids.LaCruz, ids.Tesorito]);

        (await uow.GranjaProgramas.GetGranjaIdsAsync(ave.Id)).Should().BeEquivalentTo(new[] { ids.Tesorito, ids.LaCruz });
        (await uow.GranjaProgramas.GetProgramaIdsAsync(ids.Montelindo)).Should().Equal(ids.ProgramaGanaderia);

        await uow.GranjaProgramas.EliminarPorGranjaAsync(ids.Tesorito);
        (await uow.GranjaProgramas.GetProgramaIdsAsync(ids.Tesorito)).Should().BeEmpty();
    }

    [Fact]
    public async Task Configuracion_guarda_con_upsert_y_mantiene_una_sola_fila()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();

        await uow.Configuracion.GuardarSistemaAsync(new ConfiguracionSistema
        {
            PasswordHash = "hash-1", BackupAutomaticoSalir = true, RutaBackupPredeterminada = @"C:\Respaldos",
        });
        await uow.Configuracion.GuardarSistemaAsync(new ConfiguracionSistema
        {
            PasswordHash = "hash-2", BackupAutomaticoSalir = false, UltimoCierreValorizacion = new DateTime(2026, 10, 31),
        });
        await uow.Configuracion.GuardarFirmaAsync(new ConfiguracionFirma
        {
            NombreAutoridad = "PAULA", CargoAutoridad = "DECANO", EntidadFacultad = "FACULTAD", Telefono = "8781586",
        });

        var sistema = await uow.Configuracion.GetSistemaAsync();
        sistema!.PasswordHash.Should().Be("hash-2");
        sistema.BackupAutomaticoSalir.Should().BeFalse();
        sistema.RutaBackupPredeterminada.Should().BeNull();
        sistema.UltimoCierreValorizacion.Should().Be(new DateTime(2026, 10, 31));
        (await uow.Configuracion.GetFirmaAsync())!.NombreAutoridad.Should().Be("PAULA");
        await uow.CommitAsync();
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM CONFIGURACION_SISTEMA")).Should().Be(1);
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM CONFIGURACION_FIRMA")).Should().Be(1);
    }

    [Fact]
    public async Task Movimientos_suman_el_saldo_por_animal_con_decimal_exacto()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var animal = Datos.NuevoAnimal("S-1", ids);
        var otro = Datos.NuevoAnimal("S-2", ids);
        await uow.Animales.InsertAsync(animal);
        await uow.Animales.InsertAsync(otro);

        foreach (var costo in new[] { 0.10m, 0.20m, 1_000_000.05m, -400_000.15m })
        {
            await uow.Movimientos.InsertAsync(new MovimientoInventario
            {
                AnimalId = animal.Id, TipoMovimiento = TipoMovimiento.Valorizacion,
                FechaMovimiento = new DateTime(2026, 10, 31), GranjaDestinoId = ids.Tesorito, CostoAsociado = costo,
            });
        }

        await uow.Movimientos.InsertAsync(new MovimientoInventario
        {
            AnimalId = otro.Id, TipoMovimiento = TipoMovimiento.Compra, FechaMovimiento = new DateTime(2026, 10, 1),
            GranjaDestinoId = ids.Tesorito, CostoAsociado = 150_000m,
        });

        // 0,10 + 0,20 + 1.000.000,05 − 400.000,15 = 600.000,20 exacto (en coma flotante saldría 600000.2000000001 o similar).
        (await uow.Movimientos.GetSaldoAnimalAsync(animal.Id)).Should().Be(600_000.20m);
        var saldos = await uow.Movimientos.GetSaldosPorAnimalAsync();
        saldos[animal.Id].Should().Be(600_000.20m);
        saldos[otro.Id].Should().Be(150_000m);
        (await uow.Movimientos.GetSaldoAnimalAsync(99999)).Should().Be(0m);
    }

    [Fact]
    public async Task Movimientos_del_animal_salen_en_orden_cronologico()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var animal = Datos.NuevoAnimal("S-3", ids);
        await uow.Animales.InsertAsync(animal);
        foreach (var fecha in new[] { new DateTime(2026, 10, 31), new DateTime(2026, 6, 20), new DateTime(2026, 8, 31) })
        {
            await uow.Movimientos.InsertAsync(new MovimientoInventario
            {
                AnimalId = animal.Id, TipoMovimiento = TipoMovimiento.Valorizacion, FechaMovimiento = fecha,
                GranjaDestinoId = ids.Tesorito, CostoAsociado = 1m,
            });
        }

        var movimientos = await uow.Movimientos.GetByAnimalAsync(animal.Id);

        movimientos.Select(m => m.FechaMovimiento).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Un_movimiento_con_las_dos_granjas_es_rechazado_por_la_base()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var animal = Datos.NuevoAnimal("S-4", ids);
        await uow.Animales.InsertAsync(animal);

        var act = () => uow.Movimientos.InsertAsync(new MovimientoInventario
        {
            AnimalId = animal.Id, TipoMovimiento = TipoMovimiento.Traslado, FechaMovimiento = DateTime.Today,
            GranjaOrigenId = ids.Tesorito, GranjaDestinoId = ids.LaCruz, CostoAsociado = 1m,
        });

        await act.Should().ThrowAsync<SqliteException>().WithMessage("*CHECK*");
    }
}
