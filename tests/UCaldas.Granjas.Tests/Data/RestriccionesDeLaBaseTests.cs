using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Data;

/// <summary>
/// Garantías que da la propia base de datos, independientes de la lógica de la aplicación:
/// integridad referencial, unicidad y la jerarquía documental Oficio → Remisión → Recibo de Caja.
/// </summary>
public class RestriccionesDeLaBaseTests
{
    private const string InsertarRemision =
        """
        INSERT INTO REMISION (numero_remision, tipo_movimiento, oficio_id, recibo_caja, fecha_remision)
        VALUES (@numero, @tipo, @oficio, @recibo, '2026-10-20 00:00:00')
        """;

    private static async Task<int> CrearOficioAsync(BaseDeDatosDePrueba db)
    {
        await db.EjecutarAsync(
            "INSERT INTO OFICIO_VENTA (numero_oficio, programa_id, fecha_oficio) " +
            "SELECT 'OF-120', id, '2026-10-01 00:00:00' FROM PROGRAMA WHERE codigo = 'PRG-GAN'");
        return await db.EscalarAsync<int>("SELECT id FROM OFICIO_VENTA WHERE numero_oficio = 'OF-120'");
    }

    [Fact]
    public async Task Las_claves_foraneas_estan_activas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => db.EjecutarAsync(
            """
            INSERT INTO ANIMAL (chapeta_amarilla, sexo, fecha_nacimiento, clase_entrada, raza, peso_actual_kg,
                                orientacion_id, valor_contable_actual, granja_id, categoria_id)
            VALUES ('X-1', 'Hembra', '2026-01-01 00:00:00', 'NACIMIENTO', 'Holstein', 30, 1, 150000, 999, 1)
            """);

        await act.Should().ThrowAsync<SqliteException>().WithMessage("*FOREIGN KEY*");
    }

    [Fact]
    public async Task Solo_puede_haber_una_vigencia_activa()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => db.EjecutarAsync("INSERT INTO VIGENCIA_VALORIZACION (anio, activa) VALUES (2027, 1)");

        await act.Should().ThrowAsync<SqliteException>().WithMessage("*UNIQUE*");
    }

    [Fact]
    public async Task La_chapeta_amarilla_es_unica_sin_distinguir_mayusculas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        await uow.Animales.InsertAsync(Datos.NuevoAnimal("004C", ids));

        var act = () => uow.Animales.InsertAsync(Datos.NuevoAnimal("004c", ids));

        await act.Should().ThrowAsync<SqliteException>().WithMessage("*UNIQUE*");
    }

    [Fact]
    public async Task El_sexo_del_animal_solo_admite_Macho_o_Hembra()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => db.EjecutarAsync(
            """
            INSERT INTO ANIMAL (chapeta_amarilla, sexo, fecha_nacimiento, clase_entrada, raza, peso_actual_kg,
                                orientacion_id, valor_contable_actual, granja_id, categoria_id)
            VALUES ('X-2', 'Ambos', '2026-01-01 00:00:00', 'NACIMIENTO', 'Holstein', 30, 1, 150000, 1, 1)
            """);

        await act.Should().ThrowAsync<SqliteException>().WithMessage("*CHECK*");
    }

    [Fact]
    public async Task Una_venta_exige_oficio_y_recibo_de_caja()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var oficio = await CrearOficioAsync(db);

        var sinOficio = () => db.EjecutarAsync(InsertarRemision, new { numero = "REM-1", tipo = "VENTA", oficio = (int?)null, recibo = "RC-8841" });
        var sinRecibo = () => db.EjecutarAsync(InsertarRemision, new { numero = "REM-2", tipo = "VENTA", oficio = (int?)oficio, recibo = (string?)null });
        var reciboEnBlanco = () => db.EjecutarAsync(InsertarRemision, new { numero = "REM-3", tipo = "VENTA", oficio = (int?)oficio, recibo = "   " });

        await sinOficio.Should().ThrowAsync<SqliteException>().WithMessage("*CHECK*");
        await sinRecibo.Should().ThrowAsync<SqliteException>().WithMessage("*CHECK*");
        await reciboEnBlanco.Should().ThrowAsync<SqliteException>().WithMessage("*CHECK*");

        var completa = await db.EjecutarAsync(InsertarRemision, new { numero = "REM-4", tipo = "VENTA", oficio = (int?)oficio, recibo = "RC-8841" });
        completa.Should().Be(1);
    }

    [Theory]
    [InlineData("TRASLADO")]
    [InlineData("COMPRA")]
    [InlineData("BAJA")]
    public async Task Traslados_compras_y_bajas_rechazan_oficio_y_recibo_de_caja(string tipo)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var oficio = await CrearOficioAsync(db);

        var conRecibo = () => db.EjecutarAsync(InsertarRemision, new { numero = "REM-A", tipo, oficio = (int?)null, recibo = "RC-1" });
        var conOficio = () => db.EjecutarAsync(InsertarRemision, new { numero = "REM-B", tipo, oficio = (int?)oficio, recibo = (string?)null });

        await conRecibo.Should().ThrowAsync<SqliteException>().WithMessage("*CHECK*");
        await conOficio.Should().ThrowAsync<SqliteException>().WithMessage("*CHECK*");

        var valida = await db.EjecutarAsync(InsertarRemision, new { numero = "REM-C", tipo, oficio = (int?)null, recibo = (string?)null });
        valida.Should().Be(1);
    }

    [Fact]
    public async Task El_numero_de_remision_es_unico_sin_distinguir_mayusculas()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await db.EjecutarAsync(InsertarRemision, new { numero = "REM-001", tipo = "TRASLADO", oficio = (int?)null, recibo = (string?)null });

        var act = () => db.EjecutarAsync(InsertarRemision, new { numero = "rem-001", tipo = "TRASLADO", oficio = (int?)null, recibo = (string?)null });

        await act.Should().ThrowAsync<SqliteException>().WithMessage("*UNIQUE*");
    }

    [Theory]
    [InlineData("NULL", "NULL", false)]
    [InlineData("1", "2", false)]
    [InlineData("1", "NULL", true)]
    [InlineData("NULL", "2", true)]
    public async Task Cada_movimiento_informa_exactamente_una_granja(string origen, string destino, bool valido)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        var animal = Datos.NuevoAnimal("MOV-1", ids);
        await uow.Animales.InsertAsync(animal);
        await uow.CommitAsync();

        var sql = $"""
            INSERT INTO MOVIMIENTO_INVENTARIO (animal_id, tipo_movimiento, fecha_movimiento, granja_origen_id, granja_destino_id, costo_asociado)
            VALUES (@animal, 'Compra', '2026-10-20 00:00:00', {origen}, {destino}, 150000)
            """;
        var act = () => db.EjecutarAsync(sql, new { animal = animal.Id });

        if (valido)
        {
            (await act()).Should().Be(1);
        }
        else
        {
            await act.Should().ThrowAsync<SqliteException>().WithMessage("*CHECK*");
        }
    }

    [Fact]
    public async Task Una_linea_de_remision_sin_animal_ni_lote_es_rechazada()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        await db.EjecutarAsync(InsertarRemision, new { numero = "REM-D", tipo = "TRASLADO", oficio = (int?)null, recibo = (string?)null });
        var remision = await db.EscalarAsync<int>("SELECT id FROM REMISION WHERE numero_remision = 'REM-D'");

        var ninguno = () => db.EjecutarAsync(
            "INSERT INTO REMISION_DETALLE (remision_id, costo_sistema_libros) VALUES (@remision, 100)", new { remision });

        await ninguno.Should().ThrowAsync<SqliteException>().WithMessage("*CHECK*");
    }

    [Fact]
    public async Task El_rango_de_edad_de_una_tarifa_debe_ser_coherente()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();

        var act = () => db.EjecutarAsync(
            """
            INSERT INTO TABLA_VALORIZACION_BASE (vigencia_id, categoria_id, edad_minima_meses, edad_maxima_meses, sexo, valor_estandar)
            VALUES (1, 1, 10, 5, 'Ambos', 1000)
            """);

        await act.Should().ThrowAsync<SqliteException>().WithMessage("*CHECK*");
    }
}
