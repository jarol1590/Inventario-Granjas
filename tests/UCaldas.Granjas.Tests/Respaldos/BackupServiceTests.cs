using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using UCaldas.Granjas.Data;
using UCaldas.Granjas.Services.Respaldos;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Respaldos;

public class BackupServiceTests
{
    private sealed class Entorno(BaseDeDatosDePrueba db, RelojFijo reloj, SqliteDatabaseMaintenance mantenimiento, BackupService servicio, string carpeta) : IAsyncDisposable
    {
        public BaseDeDatosDePrueba Db { get; } = db;

        public RelojFijo Reloj { get; } = reloj;

        public SqliteDatabaseMaintenance Mantenimiento { get; } = mantenimiento;

        public BackupService Servicio { get; } = servicio;

        public string Carpeta { get; } = carpeta;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<Entorno> CrearAsync(bool respaldoAlSalir = true)
    {
        var db = await BaseDeDatosDePrueba.CrearAsync();
        var carpeta = Path.Combine(db.Carpeta, "respaldos");
        await using (var uow = await db.Uow.BeginAsync())
        {
            await uow.Configuracion.GuardarSistemaAsync(new Domain.Entities.ConfiguracionSistema
            {
                PasswordHash = "x", BackupAutomaticoSalir = respaldoAlSalir, RutaBackupPredeterminada = carpeta,
            });
            await uow.CommitAsync();
        }

        var reloj = new RelojFijo(new DateTime(2026, 11, 6, 8, 30, 0));
        var mantenimiento = new SqliteDatabaseMaintenance(db.Conexiones);
        return new Entorno(db, reloj, mantenimiento, new BackupService(db.Uow, mantenimiento, reloj), carpeta);
    }

    private static async Task<long> ContarGranjasEnZipAsync(string zip, string carpetaTemporal)
    {
        var extraida = Path.Combine(carpetaTemporal, $"{Guid.NewGuid():N}.db");
        using (var archivo = ZipFile.OpenRead(zip))
        {
            archivo.GetEntry(BackupService.NombreBaseEnZip)!.ExtractToFile(extraida);
        }

        var cadena = $"Data Source={extraida};Mode=ReadOnly;Pooling=False";
        await using var conexion = new Microsoft.Data.Sqlite.SqliteConnection(cadena);
        await conexion.OpenAsync();
        await using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM GRANJA";
        return (long)(await comando.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Crea_un_zip_con_la_base_y_el_manifiesto_con_el_nombre_estandar()
    {
        await using var e = await CrearAsync();

        var resultado = await e.Servicio.CrearRespaldoAsync();

        Path.GetFileName(resultado.Ruta).Should().Be("backup_20261106_0830.zip");
        resultado.Bytes.Should().BeGreaterThan(0).And.Be(new FileInfo(resultado.Ruta).Length);
        resultado.Fecha.Should().Be(new DateTime(2026, 11, 6, 8, 30, 0));
        using var zip = ZipFile.OpenRead(resultado.Ruta);
        zip.Entries.Select(x => x.FullName).Should().BeEquivalentTo(BackupService.NombreBaseEnZip, "manifiesto.txt");
    }

    [Fact]
    public async Task El_respaldo_contiene_los_datos_y_es_una_base_valida()
    {
        await using var e = await CrearAsync();
        await e.Db.EjecutarAsync("INSERT INTO GRANJA (codigo, nombre) VALUES ('G-77', 'Granja nueva')");

        var resultado = await e.Servicio.CrearRespaldoAsync();

        (await ContarGranjasEnZipAsync(resultado.Ruta, e.Db.Carpeta)).Should().Be(4);
        var extraida = Path.Combine(e.Db.Carpeta, "valida.db");
        using (var zip = ZipFile.OpenRead(resultado.Ruta))
        {
            zip.GetEntry(BackupService.NombreBaseEnZip)!.ExtractToFile(extraida);
        }

        (await e.Mantenimiento.EsBaseValidaAsync(extraida)).Should().BeTrue();
    }

    [Fact]
    public async Task Dos_respaldos_en_el_mismo_minuto_no_se_pisan_y_no_quedan_temporales()
    {
        await using var e = await CrearAsync();

        var primero = await e.Servicio.CrearRespaldoAsync();
        var segundo = await e.Servicio.CrearRespaldoAsync();

        primero.Ruta.Should().NotBe(segundo.Ruta);
        Path.GetFileName(segundo.Ruta).Should().Be("backup_20261106_0830_2.zip");
        Directory.GetFiles(e.Carpeta).Should().OnlyContain(f => f.EndsWith(".zip"));
    }

    [Fact]
    public async Task Crea_la_carpeta_de_destino_si_no_existe_y_acepta_una_carpeta_explicita()
    {
        await using var e = await CrearAsync();
        var otra = Path.Combine(e.Db.Carpeta, "otra", "carpeta", "mas");

        var resultado = await e.Servicio.CrearRespaldoAsync(otra);

        resultado.Ruta.Should().StartWith(otra);
        File.Exists(resultado.Ruta).Should().BeTrue();
    }

    [Fact]
    public async Task Una_carpeta_imposible_se_rechaza_con_un_mensaje_claro()
    {
        await using var e = await CrearAsync();

        var act = () => e.Servicio.CrearRespaldoAsync("Z:\\carpeta?:invalida|*");

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*No se pudo usar la carpeta de respaldos*");
    }

    [Fact]
    public async Task El_ultimo_respaldo_es_el_mas_reciente_de_la_carpeta()
    {
        await using var e = await CrearAsync();
        (await e.Servicio.UltimoRespaldoAsync()).Should().BeNull("todavía no hay respaldos");

        var viejo = await e.Servicio.CrearRespaldoAsync();
        var nuevo = await e.Servicio.CrearRespaldoAsync();
        File.SetLastWriteTime(viejo.Ruta, new DateTime(2026, 10, 1));
        File.SetLastWriteTime(nuevo.Ruta, new DateTime(2026, 11, 5, 9, 15, 0));

        var ultimo = await e.Servicio.UltimoRespaldoAsync();

        ultimo!.Ruta.Should().Be(nuevo.Ruta);
        ultimo.Fecha.Should().Be(new DateTime(2026, 11, 5, 9, 15, 0));
        ultimo.Bytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Restaurar_devuelve_la_base_al_estado_del_respaldo_y_guarda_una_copia_previa()
    {
        await using var e = await CrearAsync();
        var respaldo = await e.Servicio.CrearRespaldoAsync();
        await e.Db.EjecutarAsync("INSERT INTO GRANJA (codigo, nombre) VALUES ('G-77', 'Creada despues del respaldo')");
        (await e.Db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(4);
        e.Reloj.Ahora = new DateTime(2026, 11, 6, 9, 45, 0);

        await e.Servicio.RestaurarAsync(respaldo.Ruta);

        (await e.Db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(3, "se volvió al estado del respaldo");
        (await e.Db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA WHERE codigo = 'G-77'")).Should().Be(0);

        var previa = Directory.GetFiles(e.Carpeta, "antes_de_restaurar_*.zip").Should().ContainSingle().Subject;
        (await ContarGranjasEnZipAsync(previa, e.Db.Carpeta)).Should().Be(4, "la copia previa conserva lo que se iba a perder");
    }

    [Fact]
    public async Task Tras_restaurar_la_base_sigue_funcionando_con_el_sistema()
    {
        await using var e = await CrearAsync();
        var respaldo = await e.Servicio.CrearRespaldoAsync();
        await e.Db.EjecutarAsync("INSERT INTO GRANJA (codigo, nombre) VALUES ('G-77', 'Temporal')");

        await e.Servicio.RestaurarAsync(respaldo.Ruta);

        await using var uow = await e.Db.Uow.BeginAsync();
        await uow.Granjas.InsertAsync(new Domain.Entities.Granja { Codigo = "G-88", Nombre = "Posterior" });
        await uow.CommitAsync();
        (await e.Db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(4);
    }

    [Fact]
    public async Task Un_archivo_que_no_es_zip_no_toca_la_base()
    {
        await using var e = await CrearAsync();
        var falso = Path.Combine(e.Db.Carpeta, "no-es-zip.zip");
        await File.WriteAllTextAsync(falso, "esto no es un zip");

        var act = () => e.Servicio.RestaurarAsync(falso);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*no es un .zip válido*");
        (await e.Db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(3);
    }

    [Fact]
    public async Task Un_zip_sin_la_base_o_con_una_base_danada_no_toca_la_base_actual()
    {
        await using var e = await CrearAsync();
        var sinBase = Path.Combine(e.Db.Carpeta, "sin-base.zip");
        using (var zip = ZipFile.Open(sinBase, ZipArchiveMode.Create))
        {
            zip.CreateEntry("otra-cosa.txt");
        }

        var danada = Path.Combine(e.Db.Carpeta, "danada.zip");
        var archivoDanado = Path.Combine(e.Db.Carpeta, "base-danada.db");
        await File.WriteAllBytesAsync(archivoDanado, new byte[4096]);
        using (var zip = ZipFile.Open(danada, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(archivoDanado, BackupService.NombreBaseEnZip);
        }

        await ((Func<Task>)(() => e.Servicio.RestaurarAsync(sinBase))).Should().ThrowAsync<ValidationException>().WithMessage("*no contiene*");
        await ((Func<Task>)(() => e.Servicio.RestaurarAsync(danada))).Should().ThrowAsync<ValidationException>().WithMessage("*dañado o es de una versión incompatible*");
        await ((Func<Task>)(() => e.Servicio.RestaurarAsync(Path.Combine(e.Db.Carpeta, "no-existe.zip")))).Should().ThrowAsync<ValidationException>().WithMessage("*no existe*");

        (await e.Db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(3);
        Directory.Exists(e.Carpeta).Should().BeFalse("no se creó una copia previa porque la restauración se rechazó antes de empezar");
    }

    [Fact]
    public async Task Una_base_de_una_version_de_esquema_desconocida_se_rechaza()
    {
        await using var e = await CrearAsync();
        var futura = Path.Combine(e.Db.Carpeta, "futura.db");
        await e.Mantenimiento.CopiarConsistenteAsync(futura);
        await using (var conexion = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={futura};Pooling=False"))
        {
            await conexion.OpenAsync();
            await using var comando = conexion.CreateCommand();
            comando.CommandText = "PRAGMA user_version = 99";
            await comando.ExecuteNonQueryAsync();
        }

        (await e.Mantenimiento.EsBaseValidaAsync(futura)).Should().BeFalse();
        (await e.Mantenimiento.EsBaseValidaAsync(Path.Combine(e.Db.Carpeta, "no-existe.db"))).Should().BeFalse();
    }

    [Fact]
    public async Task Compactar_no_pierde_datos_y_libera_espacio_tras_borrar()
    {
        await using var e = await CrearAsync();
        await e.Db.EjecutarAsync("CREATE TABLE RELLENO (id INTEGER PRIMARY KEY, texto TEXT)");
        await e.Db.EjecutarAsync(
            "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 20000) " +
            "INSERT INTO RELLENO (texto) SELECT printf('%0500d', i) FROM n");
        await e.Db.EjecutarAsync("DELETE FROM RELLENO");

        var (antes, despues) = await e.Servicio.CompactarAsync();

        despues.Should().BeLessThan(antes);
        (await e.Db.EscalarAsync<long>("SELECT COUNT(*) FROM GRANJA")).Should().Be(3);
    }

    [Fact]
    public async Task El_respaldo_al_salir_crea_el_zip_solo_si_esta_activado()
    {
        await using var activado = await CrearAsync(respaldoAlSalir: true);
        await using var desactivado = await CrearAsync(respaldoAlSalir: false);

        var conRespaldo = await activado.Servicio.RespaldoAlSalirAsync();
        var sinRespaldo = await desactivado.Servicio.RespaldoAlSalirAsync();

        conRespaldo.Exito.Should().BeTrue();
        conRespaldo.Mensaje.Should().Contain("Respaldo guardado en");
        Directory.GetFiles(activado.Carpeta, "backup_*.zip").Should().ContainSingle();
        sinRespaldo.Exito.Should().BeTrue();
        sinRespaldo.Mensaje.Should().Contain("desactivado");
        Directory.Exists(desactivado.Carpeta).Should().BeFalse();
    }

    [Fact]
    public async Task Si_el_respaldo_al_salir_falla_informa_sin_lanzar_excepcion()
    {
        await using var e = await CrearAsync();
        await e.Db.EjecutarAsync("UPDATE CONFIGURACION_SISTEMA SET ruta_backup_predeterminada = 'Z:\\carpeta?:invalida|*'");

        var resultado = await e.Servicio.RespaldoAlSalirAsync();

        resultado.Exito.Should().BeFalse();
        resultado.Mensaje.Should().Contain("No se pudo crear el respaldo automático");
    }
}
