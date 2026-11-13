using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Tablero de inicio: KPIs del inventario y dos gráficas (por granja o categoría, y movimientos del mes).</summary>
public sealed partial class InicioViewModel(
    IConsultasService consultas,
    ContextoGlobal contexto,
    IReloj reloj,
    IDialogService dialogos) : ViewModelBase(dialogos), IConAccionesRapidas
{
    private int _version;

    [ObservableProperty]
    private int _totalAnimales;

    [ObservableProperty]
    private string _saldoTotalTexto = Formato.Dinero(0);

    [ObservableProperty]
    private int _nacimientosMes;

    [ObservableProperty]
    private int _salidasMes;

    [ObservableProperty]
    private string _etiquetaTotal = "Total Animales";

    [ObservableProperty]
    private string _etiquetaSaldo = "Saldo total";

    [ObservableProperty]
    private string _tituloAnimales = "Animales por Granja";

    [ObservableProperty]
    private string _tituloMovimientos = "Movimientos del Mes (Entradas/Salidas)";

    [ObservableProperty]
    private ISeries[] _seriesAnimales = [];

    [ObservableProperty]
    private Axis[] _ejesAnimalesX = [];

    [ObservableProperty]
    private Axis[] _ejesAnimalesY = [];

    [ObservableProperty]
    private ISeries[] _seriesMovimientos = [];

    [ObservableProperty]
    private Axis[] _ejesMovimientosX = [];

    [ObservableProperty]
    private Axis[] _ejesMovimientosY = [];

    public async Task CargarAsync()
    {
        var version = ++_version;
        await EjecutarAsync(async () =>
        {
            var granjaId = contexto.GranjaId;
            var datos = await consultas.ObtenerDashboardAsync(granjaId, contexto.ProgramaId, reloj.Hoy);
            if (version != _version)
            {
                return; // llegó una consulta más reciente (el usuario cambió el filtro)
            }

            TotalAnimales = datos.TotalAnimales;
            SaldoTotalTexto = Formato.Dinero(datos.SaldoTotal);
            NacimientosMes = datos.NacimientosMes;
            SalidasMes = datos.SalidasMes;

            // Las gráficas se dibujan siempre: sin animales o sin movimientos salen con todas las barras en cero.
            // En un programa por lotes se cuentan cabezas y las categorías son las etapas del programa.
            var unidad = datos.EsLotes ? "Cabezas" : "Animales";
            EtiquetaTotal = datos.EsLotes ? "Total Cabezas" : "Total Animales";
            EtiquetaSaldo = datos.EsLotes ? "Saldo total lotes" : "Saldo total";
            var barras = granjaId is null ? datos.PorGranja : datos.PorCategoria;
            TituloAnimales = granjaId is null ? $"{unidad} por Granja" : datos.EsLotes ? "Cabezas por Etapa" : "Animales por Categoría";
            (SeriesAnimales, EjesAnimalesX, EjesAnimalesY) = Graficas.BarrasHorizontales(barras);

            var mes = reloj.Hoy.ToString("MMMM 'de' yyyy", Formato.Cultura);
            TituloMovimientos = $"Movimientos del Mes — {char.ToUpper(mes[0], Formato.Cultura)}{mes[1..]}";
            (SeriesMovimientos, EjesMovimientosX, EjesMovimientosY) = Graficas.ColumnasDeMovimientos(datos.MovimientosMes);
        }, "cargar el tablero de inicio");
    }
}
