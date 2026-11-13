using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Valorizacion;

namespace UCaldas.Granjas.App.ViewModels.Configuracion;

/// <summary>Una fila de la tabla de categorías y valores de referencia de un programa sin valorización biológica.</summary>
public sealed partial class TarifaSimpleFila : ObservableObject
{
    public TarifaSimpleFila(TarifaCategoriaPrograma tarifa)
    {
        Id = tarifa.Id;
        CategoriaId = tarifa.CategoriaId;
        EsDefault = tarifa.EsDefault;
        Activo = tarifa.Activo;
        _nombreCategoria = tarifa.NombreCategoria;
        _unidadCobro = tarifa.UnidadCobro;
        _valorReferencia = tarifa.ValorReferencia;
    }

    public int Id { get; }

    public int CategoriaId { get; }

    /// <summary>La categoría «General»: no se renombra ni se elimina.</summary>
    public bool EsDefault { get; }

    public bool Activo { get; }

    public bool NoEsDefault => !EsDefault;

    [ObservableProperty]
    private string _nombreCategoria;

    [ObservableProperty]
    private string _unidadCobro;

    [ObservableProperty]
    private decimal _valorReferencia;

    public TarifaCategoriaPrograma ToEntidad(int programaId) => new()
    {
        Id = Id,
        ProgramaId = programaId,
        CategoriaId = CategoriaId,
        NombreCategoria = NombreCategoria?.Trim() ?? string.Empty,
        UnidadCobro = UnidadCobro,
        ValorReferencia = ValorReferencia,
        EsDefault = EsDefault,
        Activo = Activo,
    };
}

/// <summary>
/// Tabla simplificada de 3 columnas (Categoría / Etapa, Unidad de cobro, Valor de referencia) de los programas que no usan
/// la valorización biológica por edades (Equinos, Avícola, Piscícola...). La categoría «General» no se puede eliminar.
/// </summary>
public sealed partial class TarifasSimplesViewModel : ViewModelBase
{
    private readonly ITarifaProgramaService _servicio;
    private bool _cargando;

    public TarifasSimplesViewModel(ITarifaProgramaService servicio, IDialogService dialogos)
        : base(dialogos)
    {
        _servicio = servicio;
        Filas.CollectionChanged += AlCambiarColeccion;
    }

    public IReadOnlyList<OpcionTexto> Unidades { get; } =
        [new(UnidadCobro.Unidad, UnidadCobro.Texto(UnidadCobro.Unidad)), new(UnidadCobro.Kg, UnidadCobro.Texto(UnidadCobro.Kg))];

    public ObservableCollection<TarifaSimpleFila> Filas { get; } = [];

    public Programa? Programa { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _hayCambios;

    [ObservableProperty]
    private TarifaSimpleFila? _filaSeleccionada;

    public string Titulo => Programa is null
        ? "CATEGORÍAS Y VALORES DE REFERENCIA"
        : $"CATEGORÍAS Y VALORES DE REFERENCIA — {Programa.Nombre.ToUpperInvariant()}" + (HayCambios ? " (con cambios sin guardar)" : string.Empty);

    public async Task CargarAsync(Programa programa)
    {
        Programa = programa;
        await EjecutarAsync(async () =>
        {
            var tarifas = await _servicio.ListarAsync(programa.Id);

            _cargando = true;
            try
            {
                foreach (var anterior in Filas)
                {
                    anterior.PropertyChanged -= AlCambiarFila;
                }

                Filas.Clear();
                foreach (var tarifa in tarifas)
                {
                    var fila = new TarifaSimpleFila(tarifa);
                    fila.PropertyChanged += AlCambiarFila;
                    Filas.Add(fila);
                }
            }
            finally
            {
                _cargando = false;
            }

            HayCambios = false;
        }, "cargar las categorías del programa");
        OnPropertyChanged(nameof(Titulo));
    }

    [RelayCommand]
    private void AgregarFila()
    {
        var fila = new TarifaSimpleFila(new TarifaCategoriaPrograma
        {
            ProgramaId = Programa?.Id ?? 0,
            NombreCategoria = string.Empty,
            UnidadCobro = Programa?.UnidadVentaDefault ?? UnidadCobro.Unidad,
            ValorReferencia = 0m,
        });
        Filas.Add(fila);
        FilaSeleccionada = fila;
    }

    [RelayCommand]
    private void QuitarFila(TarifaSimpleFila? fila)
    {
        if (fila is null)
        {
            return;
        }

        if (fila.EsDefault)
        {
            Dialogos.Advertir("La categoría «General» es la predeterminada del programa y no se puede eliminar.");
            return;
        }

        Filas.Remove(fila);
    }

    [RelayCommand]
    private void Reajustar()
    {
        if (Filas.Count == 0)
        {
            Dialogos.Informar("El programa no tiene categorías para reajustar.");
            return;
        }

        var texto = Dialogos.Pedir(
            "Reajuste porcentual",
            "Porcentaje a aplicar a todos los valores de referencia (use un número negativo para reducirlos). Ejemplo: 5,5",
            "5");
        if (texto is null)
        {
            return;
        }

        if (!decimal.TryParse(texto.Replace("%", string.Empty).Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Formato.Cultura, out var porcentaje)
            || porcentaje <= -100)
        {
            Dialogos.Advertir("El porcentaje no es válido. Use la coma para los decimales, por ejemplo 5,5 (y mayor que -100).");
            return;
        }

        if (!Dialogos.Confirmar(
                $"Se aplicará un reajuste de {porcentaje:+0.##;-0.##;0} % a los valores de las {Filas.Count} categorías. Podrá revisar el resultado antes de guardar.\n\n¿Continuar?",
                "Reajuste porcentual"))
        {
            return;
        }

        foreach (var fila in Filas)
        {
            fila.ValorReferencia = Math.Round(fila.ValorReferencia * (1 + porcentaje / 100m), 0, MidpointRounding.AwayFromZero);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        if (Programa is not { } programa)
        {
            return;
        }

        var guardado = await EjecutarAsync(async () =>
        {
            await _servicio.GuardarAsync(programa.Id, Filas.Select(f => f.ToEntidad(programa.Id)).ToList());
            await CargarAsync(programa);
        }, "guardar las categorías");

        if (guardado)
        {
            Dialogos.Informar($"Las categorías de {programa.Nombre} se guardaron correctamente.");
        }
    }

    private bool PuedeGuardar() => HayCambios && Programa is not null;

    private void AlCambiarFila(object? sender, PropertyChangedEventArgs e) => Marcar();

    private void AlCambiarColeccion(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_cargando)
        {
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (TarifaSimpleFila fila in e.NewItems)
            {
                fila.PropertyChanged -= AlCambiarFila;
                fila.PropertyChanged += AlCambiarFila;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (TarifaSimpleFila fila in e.OldItems)
            {
                fila.PropertyChanged -= AlCambiarFila;
            }
        }

        Marcar();
    }

    private void Marcar()
    {
        if (!_cargando)
        {
            HayCambios = true;
        }
    }
}