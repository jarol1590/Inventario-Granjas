using Microsoft.Extensions.DependencyInjection;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels;
using UCaldas.Granjas.App.ViewModels.Configuracion;
using UCaldas.Granjas.Data;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Animales;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Consultas;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Reportes;
using UCaldas.Granjas.Services.Respaldos;
using UCaldas.Granjas.Services.Seguridad;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.App.Composicion;

/// <summary>Registro de dependencias. Es el único lugar que conoce las clases concretas de Data y Services.</summary>
public static class RaizDeComposicion
{
    public static ServiceProvider Construir()
    {
        var servicios = new ServiceCollection();

        // Datos
        servicios.AddSingleton<IReloj, RelojSistema>();
        servicios.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory(RutasSistema.RutaBaseDatos));
        servicios.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        servicios.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        servicios.AddSingleton<IDatabaseMaintenance, SqliteDatabaseMaintenance>();

        // Negocio (sin estado: todo se apoya en la fábrica de unidades de trabajo)
        servicios.AddSingleton<ITarifaValidationService, TarifaValidationService>();
        servicios.AddSingleton<IVigenciaService, VigenciaService>();
        servicios.AddSingleton<IAgingEngine, AgingEngine>();
        servicios.AddSingleton<IDocumentValidationService, DocumentValidationService>();
        servicios.AddSingleton<IKardexService, KardexService>();
        servicios.AddSingleton<IKardexReportBuilder, KardexReportBuilder>();
        servicios.AddSingleton<IFichaService, FichaService>();
        servicios.AddSingleton<IAnimalService, AnimalService>();
        servicios.AddSingleton<IExcelReportingEngine, ExcelReportingEngine>();
        servicios.AddSingleton<IAuthService, AuthService>();
        servicios.AddSingleton<IBackupService, BackupService>();
        servicios.AddSingleton<ICatalogoService, CatalogoService>();
        servicios.AddSingleton<IConsultasService, ConsultasService>();

        // Presentación
        servicios.AddSingleton<IDialogService, DialogService>();
        servicios.AddSingleton<ContextoGlobal>();
        servicios.AddSingleton<ShellViewModel>();
        servicios.AddSingleton<INavegador>(proveedor => proveedor.GetRequiredService<ShellViewModel>());
        servicios.AddSingleton<IFormularios, Formularios>();
        servicios.AddTransient<LoginViewModel>();
        servicios.AddTransient<InicioViewModel>();
        servicios.AddTransient<BovinosViewModel>();
        servicios.AddTransient<ComprasViewModel>();
        servicios.AddTransient<SalidasViewModel>();
        servicios.AddTransient<TrasladosViewModel>();
        servicios.AddTransient<InformesViewModel>();
        servicios.AddTransient<NacimientoViewModel>();
        servicios.AddTransient<FichaViewModel>();
        servicios.AddTransient<CompraEditorViewModel>();
        servicios.AddTransient<SalidaEditorViewModel>();
        servicios.AddTransient<TrasladoEditorViewModel>();
        servicios.AddTransient<SelectorAnimalesViewModel>();
        servicios.AddTransient<ConfiguracionViewModel>();
        servicios.AddTransient<TarifasViewModel>();
        servicios.AddTransient<FirmasViewModel>();
        servicios.AddTransient<GranjasProgramasViewModel>();
        servicios.AddTransient<RespaldosViewModel>();

        return servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
