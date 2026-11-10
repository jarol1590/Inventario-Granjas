using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Valorizacion;

public class AgingEngineTests
{
    private static async Task<(AgingEngine Motor, IdsBase Ids)> PrepararAsync(
        BaseDeDatosDePrueba db, string hoy, string? ultimoCierre)
    {
        await using var uow = await db.Uow.BeginAsync();
        var ids = await Datos.IdsBaseAsync(uow);
        await Datos.GuardarConfiguracionAsync(uow, ultimoCierre is null ? null : Datos.F(ultimoCierre));
        await uow.CommitAsync();
        return (new AgingEngine(db.Uow, new RelojFijo(Datos.F(hoy))), ids);
    }

    // ---- Determinar (sin guardar) -------------------------------------------------------------------------------

    [Fact]
    public async Task Determina_al_recien_nacido_con_la_tarifa_de_terneros()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", null);
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var cria = Datos.Animal(ids, "N-1", Sexo.Hembra, "2026-11-06", NombresBase.CategoriaTerneros, 0m);

        var resultado = await motor.DeterminarAsync(uow, cria, Datos.F("2026-11-06"));

        resultado.EdadMeses.Should().Be(0);
        resultado.CategoriaNombre.Should().Be(NombresBase.CategoriaTerneros);
        resultado.CategoriaId.Should().Be(ids.Categoria(NombresBase.CategoriaTerneros));
        resultado.Valor.Should().Be(150_000m);
        resultado.TarifaId.Should().NotBeNull();
        resultado.SinTarifa.Should().BeFalse();
    }

    [Fact]
    public async Task Determina_al_macho_recien_nacido_con_la_tarifa_de_160_mil()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", null);
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var cria = Datos.Animal(ids, "N-2", Sexo.Macho, "2026-11-06", NombresBase.CategoriaTerneros, 0m);

        var resultado = await motor.DeterminarAsync(uow, cria, Datos.F("2026-11-06"));

        resultado.Valor.Should().Be(160_000m);
    }

    [Fact]
    public async Task Cuando_ninguna_tarifa_aplica_devuelve_Sin_clasificar_y_sin_valor()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", null);
        await db.QuitarFilaDeLevanteParaMachosAsync(); // una matriz editada con un hueco: macho de 14 meses sin fila
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var macho14Meses = Datos.Animal(ids, "N-3", Sexo.Macho, "2025-09-01", NombresBase.CategoriaLevante, 1_200_000m);

        var resultado = await motor.DeterminarAsync(uow, macho14Meses, Datos.F("2026-11-06"));

        resultado.SinTarifa.Should().BeTrue();
        resultado.CategoriaNombre.Should().Be(NombresBase.CategoriaSinClasificar);
        resultado.Valor.Should().BeNull();
        resultado.EdadMeses.Should().Be(14);
    }

    [Theory]
    [InlineData("2025-11-06", 12)]
    [InlineData("2025-09-01", 14)]
    [InlineData("2025-06-05", 17)]
    public async Task Un_macho_de_12_a_17_meses_mantiene_la_clasificacion_Levante_con_su_valor(string nacimiento, int edad)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", null);
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var macho = Datos.Animal(ids, "M-1", Sexo.Macho, nacimiento, NombresBase.CategoriaLevante, 1_200_000m);

        var resultado = await motor.DeterminarAsync(uow, macho, Datos.F("2026-11-06"));

        resultado.EdadMeses.Should().Be(edad);
        resultado.SinTarifa.Should().BeFalse("la matriz ya cubre a los machos de 12 a 17 meses");
        resultado.CategoriaNombre.Should().Be(NombresBase.CategoriaLevante);
        resultado.Valor.Should().Be(1_200_000m);
    }

    [Fact]
    public async Task A_los_18_meses_el_macho_deja_de_ser_Levante_y_pasa_a_Reproductor()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", null);
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var macho = Datos.Animal(ids, "M-2", Sexo.Macho, "2025-05-06", NombresBase.CategoriaLevante, 1_200_000m);

        var resultado = await motor.DeterminarAsync(uow, macho, Datos.F("2026-11-06"));

        resultado.EdadMeses.Should().Be(18);
        resultado.CategoriaNombre.Should().Be(NombresBase.CategoriaReproductor);
        resultado.Valor.Should().Be(7_000_000m);
    }

    [Theory]
    [InlineData(Sexo.Hembra, 0, "Lácteo", "Novilla Vientre", 3_200_000)]
    [InlineData(Sexo.Hembra, 0, "Carne/Levante", "Novilla Vientre", 3_200_000)]
    [InlineData(Sexo.Hembra, 1, "Lácteo", "Vacas General", 6_000_000)]
    [InlineData(Sexo.Hembra, 2, "Carne/Levante", "Vacas General", 4_000_000)]
    public async Task Una_hembra_de_mas_de_24_meses_sin_partos_sigue_siendo_novilla_y_con_partos_es_vaca(
        string sexo, int partos, string orientacion, string categoria, int valor)
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", null);
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var hembra = Datos.Animal(ids, "H-1", sexo, "2023-05-06", NombresBase.CategoriaNovillaVientre, 3_200_000m, orientacion: orientacion, partos: partos); // 42 meses

        var resultado = await motor.DeterminarAsync(uow, hembra, Datos.F("2026-11-06"));

        resultado.EdadMeses.Should().Be(42);
        resultado.SinTarifa.Should().BeFalse();
        resultado.CategoriaNombre.Should().Be(categoria);
        resultado.Valor.Should().Be(valor);
    }

    [Fact]
    public async Task Una_vaca_con_parto_se_clasifica_como_vaca_y_no_como_novilla()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", null);
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        // Ambas cumplen exactamente 24 meses: ahí se cruzan "Novilla Vientre" (12–24) y "Vacas General" (24–999).
        var conParto = Datos.Animal(ids, "V-1", Sexo.Hembra, "2024-11-06", NombresBase.CategoriaNovillaVientre, 3_200_000m, partos: 1);
        var sinParto = Datos.Animal(ids, "V-2", Sexo.Hembra, "2024-11-06", NombresBase.CategoriaNovillaVientre, 3_200_000m, partos: 0);

        var a = await motor.DeterminarAsync(uow, conParto, Datos.F("2026-11-06"));
        var b = await motor.DeterminarAsync(uow, sinParto, Datos.F("2026-11-06"));

        a.CategoriaNombre.Should().Be(NombresBase.CategoriaVacasGeneral);
        a.Valor.Should().Be(6_000_000m);
        b.CategoriaNombre.Should().Be(NombresBase.CategoriaNovillaVientre);
        b.Valor.Should().Be(3_200_000m);
    }

    [Fact]
    public async Task La_orientacion_Carne_recibe_la_tarifa_de_carne()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", null);
        await using var uow = await db.Uow.OpenReadOnlyAsync();
        var vacaDeCarne = Datos.Animal(ids, "V-3", Sexo.Hembra, "2022-01-10", NombresBase.CategoriaVacasGeneral, 0m,
            orientacion: NombresBase.OrientacionCarne, partos: 2);

        var resultado = await motor.DeterminarAsync(uow, vacaDeCarne, Datos.F("2026-11-06"));

        resultado.Valor.Should().Be(4_000_000m);
    }

    // ---- Valorizar al corte -------------------------------------------------------------------------------------

    [Fact]
    public async Task Valoriza_al_corte_actualiza_categoria_y_valor_y_registra_el_delta_en_el_libro()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", "2026-09-30");
        await db.QuitarFilaDeLevanteParaMachosAsync(); // para E: una matriz editada con un hueco
        await using (var uow = await db.Uow.BeginAsync())
        {
            // A: ternera que cumple 3 meses: misma categoría (Terneros), el valor sube de 150 mil a 800 mil.
            await Datos.AltaAsync(uow, Datos.Animal(ids, "A-1", Sexo.Hembra, "2026-07-15", NombresBase.CategoriaTerneros, 150_000m));
            // B: macho que cumple 8 meses: pasa de Terneros a Levante.
            await Datos.AltaAsync(uow, Datos.Animal(ids, "B-1", Sexo.Macho, "2026-02-10", NombresBase.CategoriaTerneros, 800_000m));
            // C: hembra con un parto que cumple 24 meses: de Novilla Vientre a Vacas General.
            await Datos.AltaAsync(uow, Datos.Animal(ids, "C-1", Sexo.Hembra, "2024-10-20", NombresBase.CategoriaNovillaVientre, 3_200_000m, partos: 1));
            // D: reproductor exento: no cambia nada.
            await Datos.AltaAsync(uow, Datos.Animal(ids, "D-1", Sexo.Macho, "2024-01-01", NombresBase.CategoriaReproductor, 9_000_000m, exento: true));
            // E: macho de 14 meses: la tabla no tiene fila (hueco real): conserva su valor y queda "Sin clasificar".
            await Datos.AltaAsync(uow, Datos.Animal(ids, "E-1", Sexo.Macho, "2025-08-01", NombresBase.CategoriaLevante, 1_200_000m));
            // F: registrado después del corte: todavía no formaba parte del inventario.
            await Datos.AltaAsync(uow, Datos.Animal(ids, "F-1", Sexo.Hembra, "2026-10-20", NombresBase.CategoriaTerneros, 150_000m, registro: "2026-11-02"));
            // G: vendido: no se valoriza.
            var vendido = Datos.Animal(ids, "G-1", Sexo.Hembra, "2025-01-01", NombresBase.CategoriaNovillaVientre, 3_200_000m);
            vendido.Estado = EstadoAnimal.Vendido;
            await Datos.AltaAsync(uow, vendido);
            await uow.CommitAsync();
        }

        var resumen = await motor.ValorizarAlCorteAsync(Datos.F("2026-10-31"));

        resumen.FechaCorte.Should().Be(Datos.F("2026-10-31"));
        resumen.AnimalesEvaluados.Should().Be(5);
        resumen.AnimalesConCambioDeValor.Should().Be(3);
        resumen.CambiosDeCategoria.Should().Be(3);
        resumen.DeltaTotal.Should().Be(650_000m + 400_000m + 2_800_000m);
        resumen.SinTarifa.Should().Equal("E-1");

        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        async Task<Domain.Entities.Animal> Leer(string chapeta) => (await lectura.Animales.GetByChapetaAsync(chapeta))!;

        var a = await Leer("A-1");
        a.ValorContableActual.Should().Be(800_000m);
        a.CategoriaId.Should().Be(ids.Categoria(NombresBase.CategoriaTerneros));

        var b = await Leer("B-1");
        b.ValorContableActual.Should().Be(1_200_000m);
        b.CategoriaId.Should().Be(ids.Categoria(NombresBase.CategoriaLevante));

        var c = await Leer("C-1");
        c.ValorContableActual.Should().Be(6_000_000m);
        c.CategoriaId.Should().Be(ids.Categoria(NombresBase.CategoriaVacasGeneral));

        var d = await Leer("D-1");
        d.ValorContableActual.Should().Be(9_000_000m);
        d.CategoriaId.Should().Be(ids.Categoria(NombresBase.CategoriaReproductor));

        var e = await Leer("E-1");
        e.ValorContableActual.Should().Be(1_200_000m, "sin tarifa se conserva el valor");
        e.CategoriaId.Should().Be(ids.Categoria(NombresBase.CategoriaSinClasificar));

        (await Leer("F-1")).ValorContableActual.Should().Be(150_000m, "se registró después del corte");
        (await Leer("G-1")).ValorContableActual.Should().Be(3_200_000m, "no está Disponible");

        // Los deltas quedan en el libro, fechados al corte y sobre la granja donde está el animal.
        var movimientos = await lectura.Movimientos.GetByAnimalAsync(a.Id);
        var delta = movimientos.Single(m => m.TipoMovimiento == TipoMovimiento.Valorizacion);
        delta.FechaMovimiento.Should().Be(Datos.F("2026-10-31"));
        delta.GranjaDestinoId.Should().Be(ids.Tesorito);
        delta.GranjaOrigenId.Should().BeNull();
        delta.CostoAsociado.Should().Be(650_000m);

        (await lectura.Movimientos.GetByAnimalAsync(d.Id)).Should().ContainSingle("el exento no genera delta");
        (await lectura.Movimientos.GetByAnimalAsync(e.Id)).Should().ContainSingle("sin tarifa no genera delta");

        // Invariante del Kardex: el saldo del libro de cada animal es igual a su valor contable.
        foreach (var chapeta in new[] { "A-1", "B-1", "C-1", "D-1", "E-1", "F-1", "G-1" })
        {
            var animal = await Leer(chapeta);
            (await lectura.Movimientos.GetSaldoAnimalAsync(animal.Id)).Should().Be(animal.ValorContableActual, chapeta);
        }

        (await lectura.Configuracion.GetSistemaAsync())!.UltimoCierreValorizacion.Should().Be(Datos.F("2026-10-31"));
    }

    [Fact]
    public async Task Valorizar_dos_veces_a_la_misma_fecha_no_genera_deltas_nuevos()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", "2026-09-30");
        await using (var uow = await db.Uow.BeginAsync())
        {
            await Datos.AltaAsync(uow, Datos.Animal(ids, "A-1", Sexo.Hembra, "2026-07-15", NombresBase.CategoriaTerneros, 150_000m));
            await Datos.AltaAsync(uow, Datos.Animal(ids, "E-1", Sexo.Macho, "2025-08-01", NombresBase.CategoriaLevante, 1_200_000m));
            await uow.CommitAsync();
        }

        await motor.ValorizarAlCorteAsync(Datos.F("2026-10-31"));
        var segunda = await motor.ValorizarAlCorteAsync(Datos.F("2026-10-31"));

        segunda.AnimalesConCambioDeValor.Should().Be(0);
        segunda.CambiosDeCategoria.Should().Be(0);
        segunda.DeltaTotal.Should().Be(0m);
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM MOVIMIENTO_INVENTARIO WHERE tipo_movimiento = 'Valorizacion'")).Should().Be(1);
    }

    [Fact]
    public async Task Un_animal_exento_actualiza_su_categoria_pero_conserva_su_valor_contable()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", "2026-09-30");
        await using (var uow = await db.Uow.BeginAsync())
        {
            // Hembra de alta genética comprada a 12 millones; a los 26 meses con parto le corresponde "Vacas General".
            await Datos.AltaAsync(uow, Datos.Animal(ids, "X-1", Sexo.Hembra, "2024-08-31", NombresBase.CategoriaNovillaVientre,
                12_000_000m, partos: 1, exento: true, clase: ClaseEntrada.Compra));
            await uow.CommitAsync();
        }

        var resumen = await motor.ValorizarAlCorteAsync(Datos.F("2026-10-31"));

        resumen.CambiosDeCategoria.Should().Be(1);
        resumen.AnimalesConCambioDeValor.Should().Be(0);
        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        var animal = (await lectura.Animales.GetByChapetaAsync("X-1"))!;
        animal.CategoriaId.Should().Be(ids.Categoria(NombresBase.CategoriaVacasGeneral));
        animal.ValorContableActual.Should().Be(12_000_000m);
        animal.ValorCompraOrigen.Should().Be(12_000_000m);
        (await lectura.Movimientos.GetByAnimalAsync(animal.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Un_animal_comprado_se_valoriza_con_la_matriz_como_los_nacidos_y_conserva_su_costo_de_origen()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", "2026-09-30");
        await using (var uow = await db.Uow.BeginAsync())
        {
            await Datos.AltaAsync(uow, Datos.Animal(ids, "K-1", Sexo.Hembra, "2024-08-31", NombresBase.CategoriaNovillaVientre,
                3_225_970m, partos: 1, clase: ClaseEntrada.Compra));
            await uow.CommitAsync();
        }

        await motor.ValorizarAlCorteAsync(Datos.F("2026-10-31"));

        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        var animal = (await lectura.Animales.GetByChapetaAsync("K-1"))!;
        animal.ValorContableActual.Should().Be(6_000_000m);
        animal.ValorCompraOrigen.Should().Be(3_225_970m, "el precio de factura es inmutable");
    }

    [Fact]
    public async Task Rechaza_un_corte_anterior_al_ultimo_cierre()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, _) = await PrepararAsync(db, "2026-11-06", "2026-10-31");

        var act = () => motor.ValorizarAlCorteAsync(Datos.F("2026-09-30"));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*anterior al último cierre*");
    }

    [Fact]
    public async Task Rechaza_un_corte_en_el_futuro()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, _) = await PrepararAsync(db, "2026-11-06", "2026-10-31");

        var act = () => motor.ValorizarAlCorteAsync(Datos.F("2026-11-30"));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*fecha futura*");
    }

    [Fact]
    public async Task Sin_vigencia_activa_falla_con_un_mensaje_claro_y_no_cambia_nada()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", "2026-09-30");
        await using (var uow = await db.Uow.BeginAsync())
        {
            await Datos.AltaAsync(uow, Datos.Animal(ids, "A-1", Sexo.Hembra, "2026-07-15", NombresBase.CategoriaTerneros, 150_000m));
            await uow.CommitAsync();
        }

        await db.EjecutarAsync("UPDATE VIGENCIA_VALORIZACION SET activa = 0");

        var act = () => motor.ValorizarAlCorteAsync(Datos.F("2026-10-31"));

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*vigencia de valorización activa*");
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM MOVIMIENTO_INVENTARIO WHERE tipo_movimiento = 'Valorizacion'")).Should().Be(0);
    }

    [Fact]
    public async Task Un_corte_a_mitad_de_mes_tambien_fija_el_ultimo_cierre()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, _) = await PrepararAsync(db, "2026-11-06", "2026-10-31");

        await motor.ValorizarAlCorteAsync(Datos.F("2026-11-06"));

        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        (await lectura.Configuracion.GetSistemaAsync())!.UltimoCierreValorizacion.Should().Be(Datos.F("2026-11-06"));
    }

    [Fact]
    public async Task Repetir_un_corte_anterior_al_ya_registrado_no_retrocede_el_ultimo_cierre()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, _) = await PrepararAsync(db, "2026-11-06", "2026-10-31");

        await motor.ValorizarAlCorteAsync(Datos.F("2026-10-31"));

        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        (await lectura.Configuracion.GetSistemaAsync())!.UltimoCierreValorizacion.Should().Be(Datos.F("2026-10-31"));
    }

    // ---- Cierres mensuales pendientes (al abrir la aplicación) -----------------------------------------------------

    private static async Task CrearTernerasAsync(BaseDeDatosDePrueba db, IdsBase ids)
    {
        await using var uow = await db.Uow.BeginAsync();
        // P cumple 3 meses el 15/10: su cambio de valor pertenece al cierre de octubre.
        await Datos.AltaAsync(uow, Datos.Animal(ids, "P-1", Sexo.Hembra, "2026-07-15", NombresBase.CategoriaTerneros, 150_000m));
        // Q cumplió 3 meses el 10/09: su cambio de valor pertenece al cierre de septiembre.
        await Datos.AltaAsync(uow, Datos.Animal(ids, "Q-1", Sexo.Hembra, "2026-06-10", NombresBase.CategoriaTerneros, 150_000m));
        await uow.CommitAsync();
    }

    [Fact]
    public async Task Procesa_cada_cierre_pendiente_en_orden_y_fecha_los_deltas_al_fin_de_mes()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", "2026-08-31");
        await CrearTernerasAsync(db, ids);

        var resumenes = await motor.ProcesarCierresPendientesAsync();

        resumenes.Select(r => r.FechaCorte).Should().Equal(Datos.F("2026-09-30"), Datos.F("2026-10-31"));
        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        var p = (await lectura.Animales.GetByChapetaAsync("P-1"))!;
        var q = (await lectura.Animales.GetByChapetaAsync("Q-1"))!;
        var deltaP = (await lectura.Movimientos.GetByAnimalAsync(p.Id)).Single(m => m.TipoMovimiento == TipoMovimiento.Valorizacion);
        var deltaQ = (await lectura.Movimientos.GetByAnimalAsync(q.Id)).Single(m => m.TipoMovimiento == TipoMovimiento.Valorizacion);
        deltaP.FechaMovimiento.Should().Be(Datos.F("2026-10-31"));
        deltaQ.FechaMovimiento.Should().Be(Datos.F("2026-09-30"));
        p.ValorContableActual.Should().Be(800_000m);
        q.ValorContableActual.Should().Be(800_000m);
        (await lectura.Configuracion.GetSistemaAsync())!.UltimoCierreValorizacion.Should().Be(Datos.F("2026-10-31"));
    }

    [Fact]
    public async Task Si_hoy_es_fin_de_mes_ese_dia_ya_se_puede_cerrar()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-10-31", "2026-09-30");
        await CrearTernerasAsync(db, ids);

        var resumenes = await motor.ProcesarCierresPendientesAsync();

        resumenes.Select(r => r.FechaCorte).Should().Equal(Datos.F("2026-10-31"));
    }

    [Fact]
    public async Task No_hace_nada_si_el_ultimo_fin_de_mes_ya_fue_cerrado()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", "2026-10-31");
        await CrearTernerasAsync(db, ids);

        var resumenes = await motor.ProcesarCierresPendientesAsync();

        resumenes.Should().BeEmpty();
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM MOVIMIENTO_INVENTARIO WHERE tipo_movimiento = 'Valorizacion'")).Should().Be(0);
    }

    [Fact]
    public async Task Sin_cierre_previo_parte_del_ultimo_fin_de_mes_para_no_inventar_historia()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, ids) = await PrepararAsync(db, "2026-11-06", null);
        await CrearTernerasAsync(db, ids);

        var primera = await motor.ProcesarCierresPendientesAsync();
        var segunda = await motor.ProcesarCierresPendientesAsync();

        primera.Should().BeEmpty();
        segunda.Should().BeEmpty();
        await using var lectura = await db.Uow.OpenReadOnlyAsync();
        (await lectura.Configuracion.GetSistemaAsync())!.UltimoCierreValorizacion.Should().Be(Datos.F("2026-10-31"));
        (await db.EscalarAsync<long>("SELECT COUNT(*) FROM MOVIMIENTO_INVENTARIO WHERE tipo_movimiento = 'Valorizacion'")).Should().Be(0);
    }

    [Fact]
    public async Task En_el_primer_arranque_sin_configuracion_no_hace_nada_ni_falla()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var motor = new AgingEngine(db.Uow, new RelojFijo(Datos.F("2026-11-06")));

        var resumenes = await motor.ProcesarCierresPendientesAsync();

        resumenes.Should().BeEmpty();
    }

    [Fact]
    public async Task Tras_un_corte_manual_a_mitad_de_mes_el_siguiente_cierre_es_el_fin_de_ese_mes()
    {
        await using var db = await BaseDeDatosDePrueba.CrearAsync();
        var (motor, _) = await PrepararAsync(db, "2026-11-06", "2026-10-31");
        await motor.ValorizarAlCorteAsync(Datos.F("2026-11-06"));
        var reloj = new RelojFijo(Datos.F("2026-12-01"));
        var motorEnDiciembre = new AgingEngine(db.Uow, reloj);

        var resumenes = await motorEnDiciembre.ProcesarCierresPendientesAsync();

        resumenes.Select(r => r.FechaCorte).Should().Equal(Datos.F("2026-11-30"));
    }
}
