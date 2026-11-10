using Microsoft.Data.Sqlite;
using UCaldas.Granjas.Data;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Seguridad;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Reportes;

/// <summary>Se omite salvo que exista la variable de entorno GRANJAS_DEMO (carpeta de salida).</summary>
public sealed class DemoFactAttribute : FactAttribute
{
    public const string Variable = "GRANJAS_DEMO";

    public DemoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Herramienta de revisión visual: defina {Variable} con la carpeta donde crear la base de demostración.";
        }
    }
}

/// <summary>
/// No es una prueba de comportamiento: crea una base de datos de demostración (octubre de 2026 más animales de las
/// tres granjas, contraseña «1234» y una firma de ejemplo) para abrir la aplicación con datos y revisar la interfaz.
/// La recuperación de contraseña queda configurada: pregunta 1 con respuesta «Firulais» y clave ABCD-EFGH-JKMN-PQRS.
/// Uso: <c>$env:GRANJAS_DEMO="C:\demo"; dotnet test --filter DatosDeDemostracion</c> y luego
/// <c>$env:SISTEMA_GRANJAS_DATOS="C:\demo"; dotnet run --project src/UCaldas.Granjas.App</c>.
/// </summary>
public class DatosDeDemostracion
{
    public const string RespuestaDeDemostracion = "Firulais";
    public const string ClaveDeDemostracion = "ABCD-EFGH-JKMN-PQRS";

    [DemoFact]
    public async Task Genera_una_base_de_demostracion_para_probar_la_interfaz()
    {
        var carpeta = Environment.GetEnvironmentVariable(DemoFactAttribute.Variable)!;
        Directory.CreateDirectory(carpeta);

        await using var e = await EscenariosDeReporte.CrearOctubreDeTesoritoAsync();

        // Tesorito (lácteo)
        await e.AltaDirectaAsync(Datos.Animal(e.Ids, "0940-26", Sexo.Hembra, "2020-05-10", NombresBase.CategoriaVacasGeneral, 6_000_000m, partos: 3, registro: "2026-06-01"));
        await e.AltaDirectaAsync(Datos.Animal(e.Ids, "0941-26", Sexo.Macho, "2022-02-01", NombresBase.CategoriaReproductor, 7_000_000m, registro: "2026-06-01"));
        await e.AltaDirectaAsync(Datos.Animal(e.Ids, "0950-26", Sexo.Hembra, "2025-05-20", NombresBase.CategoriaNovillaVientre, 3_200_000m, registro: "2026-06-01"));
        await e.AltaDirectaAsync(Datos.Animal(e.Ids, "0951-26", Sexo.Hembra, "2025-06-10", NombresBase.CategoriaNovillaVientre, 3_200_000m, registro: "2026-06-01"));
        await e.AltaDirectaAsync(Datos.Animal(e.Ids, "0960-26", Sexo.Macho, "2026-01-15", NombresBase.CategoriaLevante, 1_200_000m, registro: "2026-06-01"));

        // Montelindo (carne / levante)
        foreach (var (chapeta, sexo, nacimiento, categoria, valor, partos) in new[]
        {
            ("M-101", Sexo.Hembra, "2019-03-01", NombresBase.CategoriaVacasGeneral, 4_000_000m, 4),
            ("M-102", Sexo.Macho, "2021-08-01", NombresBase.CategoriaReproductor, 7_000_000m, 0),
            ("M-103", Sexo.Hembra, "2025-02-01", NombresBase.CategoriaNovillaVientre, 3_200_000m, 0),
            ("M-104", Sexo.Hembra, "2026-02-10", NombresBase.CategoriaLevante, 1_200_000m, 0),
            ("M-105", Sexo.Macho, "2026-08-15", NombresBase.CategoriaTerneros, 160_000m, 0),
        })
        {
            var animal = Datos.Animal(e.Ids, chapeta, sexo, nacimiento, categoria, valor, NombresBase.OrientacionCarne, partos,
                registro: "2026-06-01", granjaId: e.Ids.Montelindo);
            animal.Raza = "Brahman";
            await e.AltaDirectaAsync(animal);
        }

        // Contraseña «1234», recuperación de contraseña (para no mostrar el aviso de "configure su recuperación" en cada
        // arranque de la demostración) y firma de ejemplo
        await using (var uow = await e.Db.Uow.BeginAsync())
        {
            var sistema = (await uow.Configuracion.GetSistemaAsync())!;
            sistema.PasswordHash = AuthService.GenerarHash("1234");
            sistema.PreguntaSecreta = AuthService.PreguntasSecretas[0];
            sistema.RespuestaHash = AuthService.GenerarHash(AuthService.NormalizarRespuesta(RespuestaDeDemostracion));
            sistema.RecoveryKeyHash = AuthService.GenerarHash(AuthService.NormalizarClave(ClaveDeDemostracion));
            sistema.BackupAutomaticoSalir = false;
            sistema.RutaBackupPredeterminada = Path.Combine(carpeta, "respaldos");
            await uow.Configuracion.GuardarSistemaAsync(sistema);
            await uow.CommitAsync();
        }

        await EscenariosDeReporte.ConfigurarFirmaAsync(e, "data:image/png;base64," + Convert.ToBase64String(FirmaDePrueba.GenerarPng()));
        await e.AsegurarInvarianteAsync();

        SqliteConnection.ClearAllPools();
        File.Copy(e.Db.Ruta, Path.Combine(carpeta, RutasSistema.NombreBaseDatos), overwrite: true);
    }
}
