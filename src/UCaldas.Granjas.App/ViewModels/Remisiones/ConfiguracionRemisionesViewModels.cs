using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Catalogos;
using UCaldas.Granjas.Services.Comercial;

namespace UCaldas.Granjas.App.ViewModels.Remisiones;

// ---- Ventanas de alta y edición ---------------------------------------------------------------------------------------------

public sealed partial class ConvenioEditorViewModel : DialogViewModelBase
{
    private readonly ICatalogoComercialService _comercial;
    private readonly Convenio _original;

    public ConvenioEditorViewModel(ICatalogoComercialService comercial, IDialogService dialogos, Convenio existente)
        : base(dialogos)
    {
        _comercial = comercial;
        _original = existente;
        _nombre = existente.Nombre;
        _nit = existente.Nit;
        _direccion = existente.Direccion;
        _telefono = existente.Telefono;
        _correo = existente.CorreoElectronico ?? string.Empty;
        _activo = existente.Id == 0 || existente.Activo;
    }

    public bool EsNuevo => _original.Id == 0;

    public override string Titulo => EsNuevo ? "Agregar convenio" : "Editar convenio";

    public override double Ancho => 560;

    [ObservableProperty]
    private string _nombre;

    [ObservableProperty]
    private string _nit;

    [ObservableProperty]
    private string _direccion;

    [ObservableProperty]
    private string _telefono;

    [ObservableProperty]
    private string _correo;

    [ObservableProperty]
    private bool _activo;

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var guardado = await EjecutarAsync(async () => await _comercial.GuardarConvenioAsync(new Convenio
        {
            Id = _original.Id, Nombre = Nombre, Nit = Nit, Direccion = Direccion, Telefono = Telefono, CorreoElectronico = Correo, Activo = Activo,
        }), "guardar el convenio");
        if (guardado)
        {
            Cerrar(true);
        }
    }
}

public sealed partial class ClienteEditorViewModel : DialogViewModelBase
{
    private readonly ICatalogoComercialService _comercial;

    public ClienteEditorViewModel(ICatalogoComercialService comercial, IDialogService dialogos)
        : base(dialogos)
    {
        _comercial = comercial;
    }

    public override string Titulo => "Registrar cliente (otro comprador)";

    public override double Ancho => 540;

    /// <summary>El cliente guardado, para que la remisión lo seleccione.</summary>
    public Cliente? Guardado { get; private set; }

    [ObservableProperty]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string _nit = string.Empty;

    [ObservableProperty]
    private string _direccion = string.Empty;

    [ObservableProperty]
    private string _telefono = string.Empty;

    [ObservableProperty]
    private string _correo = string.Empty;

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var guardado = await EjecutarAsync(async () => Guardado = await _comercial.GuardarClienteAsync(new Cliente
        {
            NombreRazonSocial = Nombre, NitCc = Nit, Direccion = Direccion, Telefono = Telefono, CorreoElectronico = Correo, Activo = true,
        }), "guardar el cliente");
        if (guardado)
        {
            Cerrar(true);
        }
    }
}

public sealed partial class ProductoEditorViewModel : DialogViewModelBase
{
    private readonly ICatalogoComercialService _comercial;
    private readonly ICatalogoService _catalogo;
    private readonly CatalogoProducto _original;

    public ProductoEditorViewModel(ICatalogoComercialService comercial, ICatalogoService catalogo, IDialogService dialogos, CatalogoProducto existente)
        : base(dialogos)
    {
        _comercial = comercial;
        _catalogo = catalogo;
        _original = existente;
        _codigo = existente.Codigo;
        _nombre = existente.Nombre;
        _unidad = existente.UnidadMedida;
        _precio = existente.PrecioUnitario;
        _activo = existente.Id == 0 || existente.Activo;
    }

    public bool EsNuevo => _original.Id == 0;

    public override string Titulo => EsNuevo ? "Agregar producto" : "Editar producto";

    public override double Ancho => 560;

    public ObservableCollection<Programa> Programas { get; } = [];

    public IReadOnlyList<string> Unidades { get; } = ["Unidad", "Kg", "Cubeta", "Litro", "Racimo", "Bulto", "Libra", "Caja"];

    [ObservableProperty]
    private Programa? _programa;

    [ObservableProperty]
    private string _codigo;

    [ObservableProperty]
    private string _nombre;

    [ObservableProperty]
    private string _unidad;

    [ObservableProperty]
    private decimal _precio;

    [ObservableProperty]
    private bool _activo;

    public async Task<bool> InicializarAsync()
    {
        return await EjecutarAsync(async () =>
        {
            foreach (var p in await _catalogo.GetProgramasAsync())
            {
                Programas.Add(p);
            }

            Programa = Programas.FirstOrDefault(p => p.Id == _original.ProgramaId) ?? Programas.FirstOrDefault();
        }, "preparar el producto");
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var guardado = await EjecutarAsync(async () => await _comercial.GuardarProductoAsync(new CatalogoProducto
        {
            Id = _original.Id, Codigo = Codigo, ProgramaId = Programa?.Id ?? 0, Nombre = Nombre, UnidadMedida = Unidad, PrecioUnitario = Precio, Activo = Activo,
        }), "guardar el producto");
        if (guardado)
        {
            Cerrar(true);
        }
    }
}

// ---- Pestañas de Configuración de Remisiones ------------------------------------------------------------------------------------

public sealed partial class ConveniosTabViewModel : ViewModelBase, ISeccion
{
    private readonly ICatalogoComercialService _comercial;
    private readonly IFormulariosRemisiones _formularios;

    public ConveniosTabViewModel(ICatalogoComercialService comercial, IFormulariosRemisiones formularios, IDialogService dialogos)
        : base(dialogos)
    {
        _comercial = comercial;
        _formularios = formularios;
    }

    public ObservableCollection<Convenio> Convenios { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand), nameof(CambiarEstadoCommand))]
    private Convenio? _seleccionado;

    public string TextoEstado => Seleccionado is { Activo: false } ? "Activar" : "Inactivar";

    partial void OnSeleccionadoChanged(Convenio? value) => OnPropertyChanged(nameof(TextoEstado));

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            var previo = Seleccionado?.Id;
            Convenios.Clear();
            foreach (var c in await _comercial.ListarConveniosAsync())
            {
                Convenios.Add(c);
            }

            Seleccionado = Convenios.FirstOrDefault(c => c.Id == previo);
        }, "consultar los convenios");
    }

    [RelayCommand]
    private async Task AgregarAsync()
    {
        if (await _formularios.EditarConvenioAsync(null))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task EditarAsync()
    {
        if (Seleccionado is { } c && await _formularios.EditarConvenioAsync(c))
        {
            await CargarAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task CambiarEstadoAsync()
    {
        if (Seleccionado is { } c && await EjecutarAsync(() => _comercial.CambiarEstadoConvenioAsync(c.Id, !c.Activo), "cambiar el estado del convenio"))
        {
            await CargarAsync();
        }
    }

    private bool HaySeleccion() => Seleccionado is not null;
}

public sealed partial class ProductosTabViewModel : ViewModelBase, ISeccion
{
    private readonly ICatalogoComercialService _comercial;
    private readonly ICatalogoService _catalogo;
    private readonly IFormulariosRemisiones _formularios;
    private Dictionary<int, string> _programas = [];

    public ProductosTabViewModel(ICatalogoComercialService comercial, ICatalogoService catalogo, IFormulariosRemisiones formularios, IDialogService dialogos)
        : base(dialogos)
    {
        _comercial = comercial;
        _catalogo = catalogo;
        _formularios = formularios;
    }

    public ObservableCollection<FilaProducto> Productos { get; } = [];

    public ObservableCollection<OpcionFiltro> ProgramasFiltro { get; } = [];

    [ObservableProperty]
    private OpcionFiltro? _programaFiltro;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand), nameof(CambiarEstadoCommand))]
    private FilaProducto? _seleccionado;

    public string TextoEstado => Seleccionado is { Producto.Activo: false } ? "Activar" : "Inactivar";

    partial void OnSeleccionadoChanged(FilaProducto? value) => OnPropertyChanged(nameof(TextoEstado));

    partial void OnProgramaFiltroChanged(OpcionFiltro? value) => _ = CargarProductosAsync();

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            var programas = await _catalogo.GetProgramasAsync();
            _programas = programas.ToDictionary(p => p.Id, p => p.Nombre);
            var previo = ProgramaFiltro?.Id;
            ProgramasFiltro.Clear();
            ProgramasFiltro.Add(new OpcionFiltro(null, "Todos los programas"));
            foreach (var p in programas)
            {
                ProgramasFiltro.Add(new OpcionFiltro(p.Id, p.Nombre));
            }

            ProgramaFiltro = ProgramasFiltro.FirstOrDefault(o => o.Id == previo) ?? ProgramasFiltro[0];
        }, "consultar el catálogo de productos");
    }

    private async Task CargarProductosAsync()
    {
        await EjecutarAsync(async () =>
        {
            var previo = Seleccionado?.Producto.Id;
            Productos.Clear();
            foreach (var p in await _comercial.ListarProductosAsync(ProgramaFiltro?.Id))
            {
                Productos.Add(new FilaProducto(p, _programas.GetValueOrDefault(p.ProgramaId, "—")));
            }

            Seleccionado = Productos.FirstOrDefault(p => p.Producto.Id == previo);
        }, "consultar el catálogo de productos");
    }

    [RelayCommand]
    private async Task AgregarAsync()
    {
        if (await _formularios.EditarProductoAsync(null, ProgramaFiltro?.Id))
        {
            await CargarProductosAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task EditarAsync()
    {
        if (Seleccionado is { } f && await _formularios.EditarProductoAsync(f.Producto, null))
        {
            await CargarProductosAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HaySeleccion))]
    private async Task CambiarEstadoAsync()
    {
        if (Seleccionado is { } f && await EjecutarAsync(() => _comercial.CambiarEstadoProductoAsync(f.Producto.Id, !f.Producto.Activo), "cambiar el estado del producto"))
        {
            await CargarProductosAsync();
        }
    }

    private bool HaySeleccion() => Seleccionado is not null;
}

public sealed record FilaProducto(CatalogoProducto Producto, string Programa)
{
    public string Estado => Producto.Activo ? "Activo" : "Inactivo";
}

public sealed record FilaPrograma(Programa Programa)
{
    public string Tipo => !Programa.ManejaInventarioAnimal ? "Productos (sin animales)" : Programa.TipoInventario == TipoInventario.Lote ? "Lote (población)" : "Individual (chapeta)";

    public string ManejaAnimales => Programa.ManejaInventarioAnimal ? "Sí" : "No";
}

public sealed partial class ProgramasTabViewModel : ViewModelBase, ISeccion
{
    private readonly ICatalogoService _catalogo;
    private readonly INavegador _navegador;

    public ProgramasTabViewModel(ICatalogoService catalogo, INavegador navegador, IDialogService dialogos)
        : base(dialogos)
    {
        _catalogo = catalogo;
        _navegador = navegador;
    }

    public ObservableCollection<FilaPrograma> Programas { get; } = [];

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            Programas.Clear();
            foreach (var p in await _catalogo.GetProgramasAsync())
            {
                Programas.Add(new FilaPrograma(p));
            }
        }, "consultar los programas");
    }

    /// <summary>Los programas (y su alta como programa de productos) se administran en Configuración › Granjas y Programas.</summary>
    [RelayCommand]
    private void AdministrarProgramas() => _navegador.IrA(ShellViewModel.ClaveConfiguracion);
}

public sealed partial class DatosInstitucionalesViewModel : ViewModelBase, ISeccion
{
    private readonly ICatalogoComercialService _comercial;
    private ConfiguracionRemisiones _actual = new();

    public DatosInstitucionalesViewModel(ICatalogoComercialService comercial, IDialogService dialogos)
        : base(dialogos)
    {
        _comercial = comercial;
    }

    [ObservableProperty]
    private string _nombreDirector = string.Empty;

    [ObservableProperty]
    private string _cargoDirector = string.Empty;

    [ObservableProperty]
    private string _tituloDirector = string.Empty;

    [ObservableProperty]
    private string _jefeTesoreria = string.Empty;

    [ObservableProperty]
    private string _facultadDepto = string.Empty;

    [ObservableProperty]
    private string _telefonos = string.Empty;

    [ObservableProperty]
    private string _extensiones = string.Empty;

    [ObservableProperty]
    private string _entidadBancaria = string.Empty;

    [ObservableProperty]
    private string _tipoCuenta = string.Empty;

    [ObservableProperty]
    private string _numeroCuenta = string.Empty;

    [ObservableProperty]
    private int _ultimoConsecutivo;

    public async Task CargarAsync()
    {
        await EjecutarAsync(async () =>
        {
            _actual = await _comercial.GetConfiguracionAsync();
            NombreDirector = _actual.NombreDirector;
            CargoDirector = _actual.CargoDirector;
            TituloDirector = _actual.TituloDirector ?? string.Empty;
            JefeTesoreria = _actual.JefeTesoreria;
            FacultadDepto = _actual.FacultadDepto;
            Telefonos = _actual.Telefonos ?? string.Empty;
            Extensiones = _actual.Extensiones ?? string.Empty;
            EntidadBancaria = _actual.EntidadBancaria;
            TipoCuenta = _actual.TipoCuenta;
            NumeroCuenta = _actual.NumeroCuenta;
            UltimoConsecutivo = _actual.UltimoConsecutivoRecibo;
        }, "consultar los datos institucionales");
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var guardado = await EjecutarAsync(() => _comercial.GuardarConfiguracionAsync(new ConfiguracionRemisiones
        {
            NombreDirector = NombreDirector, CargoDirector = CargoDirector, TituloDirector = TituloDirector, JefeTesoreria = JefeTesoreria,
            FacultadDepto = FacultadDepto, Telefonos = Telefonos, Extensiones = Extensiones, EntidadBancaria = EntidadBancaria,
            TipoCuenta = TipoCuenta, NumeroCuenta = NumeroCuenta, UltimoConsecutivoRecibo = UltimoConsecutivo,
        }), "guardar los datos institucionales");
        if (guardado)
        {
            Dialogos.Informar("Los datos institucionales se guardaron.", "Datos institucionales");
            await CargarAsync();
        }
    }
}

/// <summary>Configuración del módulo Remisiones: convenios, productos, programas y datos institucionales.</summary>
public sealed class ConfiguracionRemisionesViewModel : ISeccion
{
    public ConfiguracionRemisionesViewModel(
        ConveniosTabViewModel convenios, ProductosTabViewModel productos, ProgramasTabViewModel programas, DatosInstitucionalesViewModel datos)
    {
        Convenios = convenios;
        Productos = productos;
        Programas = programas;
        Datos = datos;
    }

    public ConveniosTabViewModel Convenios { get; }

    public ProductosTabViewModel Productos { get; }

    public ProgramasTabViewModel Programas { get; }

    public DatosInstitucionalesViewModel Datos { get; }

    public async Task CargarAsync()
    {
        await Convenios.CargarAsync();
        await Productos.CargarAsync();
        await Programas.CargarAsync();
        await Datos.CargarAsync();
    }
}
