using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.App.ViewModels.Configuracion;

public sealed record VigenciaItem(int Id, int Anio, bool Activa)
{
    public string Texto => Activa ? $"Año {Anio} (Activa)" : $"Año {Anio}";
}

/// <summary>Pestaña de un programa en la pantalla de tarifas: la ganadería usa la matriz por edades; los demás, valores de referencia.</summary>
public sealed record ProgramaTarifaItem(Programa Programa)
{
    public string Titulo => EspecieDePrograma.De(Programa).Titulo;

    public string Icono => EspecieDePrograma.De(Programa).Icono;

    public bool UsaValorizacionBiologica => Programa.UsaValorizacionBiologica;
}

/// <summary>
/// Pestaña "Tarifas Valorización": una pestaña por programa. La ganadería (valorización biológica) muestra las vigencias anuales
/// y la matriz editable; los demás programas, la tabla simple de categorías con su valor de referencia. Guardar y Activar pasan por la validación de
/// solapamientos del servicio, así que una matriz inconsistente no se puede guardar ni activar.
/// </summary>
public sealed partial class TarifasViewModel : ViewModelBase, ISeccion
{
    private static readonly string[] FormatosDeFecha = ["d/M/yyyy", "dd/MM/yyyy", "d/MM/yyyy", "dd/M/yyyy"];

    private readonly IVigenciaService _vigencias;
    private readonly ICatalogoService _catalogo;
    private readonly IAgingEngine _valorizacion;
    private readonly IReloj _reloj;
    private bool _cargando;
    private bool _revirtiendo;
    private bool _cambiandoPrograma;

    public TarifasViewModel(
        IVigenciaService vigencias,
        ICatalogoService catalogo,
        IAgingEngine valorizacion,
        IReloj reloj,
        ITarifaProgramaService tarifasPrograma,
        IDialogService dialogos)
        : base(dialogos)
    {
        Simples = new TarifasSimplesViewModel(tarifasPrograma, dialogos);
        _vigencias = vigencias;
        _catalogo = catalogo;
        _valorizacion = valorizacion;
        _reloj = reloj;
        Filas.CollectionChanged += AlCambiarColeccion;
    }

    /// <summary>Una pestaña por programa (tabla PROGRAMA).</summary>
    public ObservableCollection<ProgramaTarifaItem> ProgramasTarifa { get; } = [];

    /// <summary>Tabla simple de categorías del programa seleccionado cuando no usa valorización biológica.</summary>
    public TarifasSimplesViewModel Simples { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsBovino), nameof(NoEsBovino))]
    private ProgramaTarifaItem? _programaTarifa;

    /// <summary>El programa elegido usa la matriz por edades (si no hay programas se muestra la matriz de siempre).</summary>
    public bool EsBovino => ProgramaTarifa?.UsaValorizacionBiologica ?? true;

    public bool NoEsBovino => !EsBovino;

    public ObservableCollection<VigenciaItem> Vigencias { get; } = [];

    public ObservableCollection<TarifaFilaViewModel> Filas { get; } = [];

    public ObservableCollection<CategoriaEtapa> Categorias { get; } = [];

    public ObservableCollection<OpcionFiltro> Orientaciones { get; } = [];

    public IReadOnlyList<string> Sexos { get; } = [Sexo.Hembra, Sexo.Macho, Sexo.Ambos];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClonarCommand))]
    [NotifyCanExecuteChangedFor(nameof(AgregarFilaCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReajustarCommand))]
    [NotifyCanExecuteChangedFor(nameof(ActivarCommand))]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private VigenciaItem? _vigenciaSeleccionada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TituloTabla))]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _hayCambios;

    [ObservableProperty]
    private TarifaFilaViewModel? _filaSeleccionada;

    public string TituloTabla => VigenciaSeleccionada is null
        ? "TABLA DE TARIFAS"
        : $"TABLA DE TARIFAS {(VigenciaSeleccionada.Activa ? "VIGENTES" : "(BORRADOR)")} (AÑO {VigenciaSeleccionada.Anio})"
          + (HayCambios ? " — con cambios sin guardar" : string.Empty);

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            // La matriz por edades es solo de los programas con valorización biológica (bovinos).
            var biologicos = (await _catalogo.GetProgramasAsync()).Where(p => p.UsaValorizacionBiologica).Select(p => p.Id).ToHashSet();
            var categorias = await _catalogo.GetCategoriasAsync();
            Categorias.Clear();
            foreach (var c in categorias.Where(c => biologicos.Contains(c.ProgramaId) && c.Nombre != NombresBase.CategoriaSinClasificar))
            {
                Categorias.Add(c);
            }

            var orientaciones = await _catalogo.GetOrientacionesAsync();
            Orientaciones.Clear();
            Orientaciones.Add(new OpcionFiltro(TarifaFilaViewModel.TodasLasOrientaciones, "Todas"));
            foreach (var o in orientaciones)
            {
                Orientaciones.Add(new OpcionFiltro(o.Id, o.Nombre));
            }

            await RecargarVigenciasAsync(VigenciaSeleccionada?.Id);
            await CargarProgramasAsync();
        }, "cargar las tarifas");
    }

    private async Task CargarProgramasAsync()
    {
        var previo = ProgramaTarifa?.Programa.Id;
        var programas = await _catalogo.GetProgramasAsync();

        _cambiandoPrograma = true;
        try
        {
            ProgramasTarifa.Clear();
            foreach (var p in programas)
            {
                ProgramasTarifa.Add(new ProgramaTarifaItem(p));
            }

            ProgramaTarifa = ProgramasTarifa.FirstOrDefault(p => p.Programa.Id == previo)
                ?? ProgramasTarifa.FirstOrDefault(p => p.UsaValorizacionBiologica)
                ?? ProgramasTarifa.FirstOrDefault();
        }
        finally
        {
            _cambiandoPrograma = false;
        }

        if (ProgramaTarifa is { UsaValorizacionBiologica: false } actual)
        {
            await Simples.CargarAsync(actual.Programa);
        }
    }

    partial void OnProgramaTarifaChanged(ProgramaTarifaItem? oldValue, ProgramaTarifaItem? newValue)
    {
        if (_cambiandoPrograma || newValue is null)
        {
            return;
        }

        if (!PuedeSalir())
        {
            ContextoUi.Diferir(() =>
            {
                _cambiandoPrograma = true;
                try
                {
                    ProgramaTarifa = oldValue;
                }
                finally
                {
                    _cambiandoPrograma = false;
                }
            });
            return;
        }

        // Los cambios que el usuario aceptó descartar se recargan para que la pestaña no conserve valores sin guardar.
        if (!newValue.UsaValorizacionBiologica)
        {
            _ = EjecutarAsync(() => Simples.CargarAsync(newValue.Programa), "cargar las categorías del programa");
        }
        else if (Simples.HayCambios && Simples.Programa is { } anterior)
        {
            _ = EjecutarAsync(() => Simples.CargarAsync(anterior), "descartar los cambios");
        }

        if (HayCambios && VigenciaSeleccionada is { } vigencia)
        {
            _ = EjecutarAsync(() => CargarMatrizAsync(vigencia.Id), "descartar los cambios");
        }
    }

    /// <summary>Con cambios sin guardar pregunta antes de abandonar la pestaña.</summary>
    public bool PuedeSalir()
    {
        if (!HayCambios && !Simples.HayCambios)
        {
            return true;
        }

        if (!Dialogos.Confirmar(
                "Hay cambios sin guardar en las tarifas.\n\n¿Desea descartarlos y salir?", "Cambios sin guardar", destructivo: true))
        {
            return false;
        }

        return true;
    }

    partial void OnVigenciaSeleccionadaChanged(VigenciaItem? oldValue, VigenciaItem? newValue)
    {
        OnPropertyChanged(nameof(TituloTabla));
        if (_cargando || _revirtiendo || newValue is null)
        {
            return;
        }

        if (HayCambios && !Dialogos.Confirmar(
                "Hay cambios sin guardar en la matriz actual.\n\n¿Desea descartarlos y cambiar de vigencia?", "Cambios sin guardar", destructivo: true))
        {
            ContextoUi.Diferir(() =>
            {
                _revirtiendo = true;
                try
                {
                    VigenciaSeleccionada = oldValue;
                }
                finally
                {
                    _revirtiendo = false;
                }
            });
            return;
        }

        _ = EjecutarAsync(() => CargarMatrizAsync(newValue.Id), "cargar la matriz de tarifas");
    }

    [RelayCommand]
    private async Task CrearNuevoAnioAsync()
    {
        if (!PuedeSalir())
        {
            return;
        }

        var sugerido = (Vigencias.Count == 0 ? DateTime.Today.Year : Vigencias.Max(v => v.Anio) + 1).ToString(CultureInfo.InvariantCulture);
        var texto = Dialogos.Pedir("Crear lista de un nuevo año", "Año de la nueva vigencia (se crea vacía y sin activar):", sugerido);
        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var anio))
        {
            if (texto is not null)
            {
                Dialogos.Advertir("Escriba el año con cuatro cifras, por ejemplo 2027.");
            }

            return;
        }

        await EjecutarAsync(async () =>
        {
            var vigencia = await _vigencias.CrearAsync(anio);
            await RecargarVigenciasAsync(vigencia.Id);
        }, "crear la vigencia");
    }

    [RelayCommand(CanExecute = nameof(HayVigencia))]
    private async Task ClonarAsync()
    {
        if (VigenciaSeleccionada is not { } origen || !PuedeSalir())
        {
            return;
        }

        var texto = Dialogos.Pedir(
            "Clonar del año anterior",
            $"Año de la nueva vigencia (se copian las tarifas del año {origen.Anio}):",
            (origen.Anio + 1).ToString(CultureInfo.InvariantCulture));
        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var anio))
        {
            if (texto is not null)
            {
                Dialogos.Advertir("Escriba el año con cuatro cifras, por ejemplo 2027.");
            }

            return;
        }

        await EjecutarAsync(async () =>
        {
            var vigencia = await _vigencias.ClonarAsync(origen.Id, anio);
            await RecargarVigenciasAsync(vigencia.Id);
        }, "clonar la vigencia");
    }

    [RelayCommand(CanExecute = nameof(HayVigencia))]
    private void AgregarFila()
    {
        var fila = new TarifaFilaViewModel(new TablaValorizacionBase
        {
            CategoriaId = Categorias.FirstOrDefault()?.Id ?? 0,
            Sexo = Sexo.Ambos,
            EdadMinimaMeses = 0,
            EdadMaximaMeses = 0,
            ValorEstandar = 0,
        });
        Filas.Add(fila);
        FilaSeleccionada = fila;
    }

    [RelayCommand]
    private void QuitarFila(TarifaFilaViewModel? fila)
    {
        if (fila is not null)
        {
            Filas.Remove(fila);
        }
    }

    [RelayCommand(CanExecute = nameof(HayVigencia))]
    private void Reajustar()
    {
        if (Filas.Count == 0)
        {
            Dialogos.Informar("La vigencia no tiene tarifas para reajustar.");
            return;
        }

        var texto = Dialogos.Pedir(
            "Reajuste porcentual",
            "Porcentaje a aplicar a todos los valores base (use un número negativo para reducirlos). Ejemplo: 5,5",
            "5");
        if (texto is null)
        {
            return;
        }

        if (!decimal.TryParse(texto.Replace("%", string.Empty).Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Formato.Cultura, out var porcentaje))
        {
            Dialogos.Advertir("El porcentaje no es válido. Use la coma para los decimales, por ejemplo 5,5.");
            return;
        }

        if (!Dialogos.Confirmar(
                $"Se aplicará un reajuste de {porcentaje:+0.##;-0.##;0} % a los valores de las {Filas.Count} tarifas. " +
                "Podrá revisar el resultado antes de guardar.\n\n¿Continuar?",
                "Reajuste porcentual"))
        {
            return;
        }

        try
        {
            var reajustadas = _vigencias.AplicarReajustePorcentual(
                Filas.Select(f => f.ToEntidad(VigenciaSeleccionada!.Id)).ToList(), porcentaje);
            for (var i = 0; i < Filas.Count; i++)
            {
                Filas[i].ValorBase = reajustadas[i].ValorEstandar;
            }
        }
        catch (System.ComponentModel.DataAnnotations.ValidationException ex)
        {
            Dialogos.Advertir(ex.Message);
        }
    }

    /// <summary>
    /// Valoriza el inventario a una fecha distinta del cierre mensual automático (p. ej. después de activar una vigencia
    /// a mitad de mes). Usa la matriz de la vigencia activa ya guardada.
    /// </summary>
    [RelayCommand]
    private async Task ValorizarAFechaAsync()
    {
        var texto = Dialogos.Pedir(
            "Valorizar el inventario a una fecha",
            "Fecha de corte (día/mes/año). Se aplica la matriz de la vigencia activa; no puede ser futura ni anterior al último cierre.",
            Formato.Fecha(_reloj.Hoy));
        if (texto is null)
        {
            return;
        }

        if (!DateTime.TryParseExact(texto, FormatosDeFecha, Formato.Cultura, DateTimeStyles.None, out var fecha))
        {
            Dialogos.Advertir("La fecha no es válida. Escríbala como día/mes/año, por ejemplo 06/11/2026.");
            return;
        }

        if (!Dialogos.Confirmar(
                $"Se valorizará el inventario disponible al {Formato.Fecha(fecha)} con la matriz de la vigencia activa y los cambios de valor " +
                "quedarán registrados en el Kardex.\n\n¿Desea continuar?",
                "Valorizar inventario"))
        {
            return;
        }

        ResumenValorizacion? resumen = null;
        if (!await EjecutarAsync(async () => resumen = await _valorizacion.ValorizarAlCorteAsync(fecha), "valorizar el inventario") || resumen is null)
        {
            return;
        }

        var mensaje =
            $"Valorización al {Formato.Fecha(resumen.FechaCorte)} terminada.\n\n" +
            $"Animales evaluados: {resumen.AnimalesEvaluados:N0}\n" +
            $"Con cambio de valor: {resumen.AnimalesConCambioDeValor:N0}\n" +
            $"Cambios de categoría: {resumen.CambiosDeCategoria:N0}\n" +
            $"Variación total: {Formato.DineroConSigno(resumen.DeltaTotal)}";
        if (resumen.SinTarifa.Count > 0)
        {
            mensaje += $"\n\nSin tarifa vigente (conservan su valor): {string.Join(", ", resumen.SinTarifa)}.";
        }

        Dialogos.Informar(mensaje, "Valorización");
    }

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        if (VigenciaSeleccionada is not { } vigencia)
        {
            return;
        }

        var guardado = await EjecutarAsync(async () =>
        {
            await _vigencias.GuardarMatrizAsync(vigencia.Id, Filas.Select(f => f.ToEntidad(vigencia.Id)).ToList());
            await CargarMatrizAsync(vigencia.Id);
        }, "guardar las tarifas");

        if (guardado)
        {
            Dialogos.Informar($"Las tarifas del año {vigencia.Anio} se guardaron correctamente.");
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeActivar))]
    private async Task ActivarAsync()
    {
        if (VigenciaSeleccionada is not { } vigencia)
        {
            return;
        }

        if (!Dialogos.Confirmar(
                $"Se activará la vigencia {vigencia.Anio}. Desde el próximo cierre de mes el inventario se valorizará con esta matriz " +
                "y la vigencia actual dejará de estar activa.\n\n¿Desea continuar?",
                "Activar vigencia"))
        {
            return;
        }

        await EjecutarAsync(async () =>
        {
            if (HayCambios)
            {
                await _vigencias.GuardarMatrizAsync(vigencia.Id, Filas.Select(f => f.ToEntidad(vigencia.Id)).ToList());
            }

            await _vigencias.ActivarAsync(vigencia.Id);
            await RecargarVigenciasAsync(vigencia.Id);
        }, "activar la vigencia");
    }

    private bool HayVigencia() => VigenciaSeleccionada is not null;

    private bool PuedeGuardar() => HayCambios && VigenciaSeleccionada is not null;

    private bool PuedeActivar() => VigenciaSeleccionada is { Activa: false };

    private async Task RecargarVigenciasAsync(int? idASeleccionar)
    {
        _cargando = true;
        try
        {
            var vigencias = await _vigencias.ListarAsync();
            Vigencias.Clear();
            foreach (var v in vigencias)
            {
                Vigencias.Add(new VigenciaItem(v.Id, v.Anio, v.Activa));
            }

            VigenciaSeleccionada = Vigencias.FirstOrDefault(v => v.Id == idASeleccionar)
                ?? Vigencias.FirstOrDefault(v => v.Activa)
                ?? Vigencias.FirstOrDefault();
        }
        finally
        {
            _cargando = false;
        }

        if (VigenciaSeleccionada is { } seleccionada)
        {
            await CargarMatrizAsync(seleccionada.Id);
        }
        else
        {
            Filas.Clear();
            HayCambios = false;
        }
    }

    private async Task CargarMatrizAsync(int vigenciaId)
    {
        var filas = await _vigencias.GetMatrizAsync(vigenciaId);

        _cargando = true;
        try
        {
            foreach (var anterior in Filas)
            {
                anterior.PropertyChanged -= AlCambiarFila;
            }

            Filas.Clear();
            foreach (var fila in filas)
            {
                var vm = new TarifaFilaViewModel(fila);
                vm.PropertyChanged += AlCambiarFila;
                Filas.Add(vm);
            }
        }
        finally
        {
            _cargando = false;
        }

        HayCambios = false;
        OnPropertyChanged(nameof(TituloTabla));
    }

    private void AlCambiarFila(object? sender, PropertyChangedEventArgs e) => MarcarCambios();

    private void AlCambiarColeccion(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_cargando)
        {
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (TarifaFilaViewModel fila in e.NewItems)
            {
                fila.PropertyChanged -= AlCambiarFila;
                fila.PropertyChanged += AlCambiarFila;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (TarifaFilaViewModel fila in e.OldItems)
            {
                fila.PropertyChanged -= AlCambiarFila;
            }
        }

        MarcarCambios();
    }

    private void MarcarCambios()
    {
        if (!_cargando)
        {
            HayCambios = true;
        }
    }
}
