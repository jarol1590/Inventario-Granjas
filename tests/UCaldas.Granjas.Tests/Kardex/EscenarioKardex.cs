using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Services.Valorizacion;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

/// <summary>
/// Entorno completo para probar el Kardex: base real, reloj fijo (hoy = 06/11/2026), motor de valorización y servicio,
/// más constructores de comandos y el verificador de la invariante contable del libro.
/// </summary>
public sealed class EscenarioKardex : IAsyncDisposable
{
    private EscenarioKardex(BaseDeDatosDePrueba db, IdsBase ids, RelojFijo reloj)
    {
        Db = db;
        Ids = ids;
        Reloj = reloj;
        Aging = new AgingEngine(db.Uow, reloj);
        Kardex = new KardexService(db.Uow, Aging, new DocumentValidationService(), reloj);
    }

    public BaseDeDatosDePrueba Db { get; }

    public IdsBase Ids { get; }

    public RelojFijo Reloj { get; }

    public AgingEngine Aging { get; }

    public KardexService Kardex { get; }

    public static async Task<EscenarioKardex> CrearAsync(string hoy = "2026-11-06")
    {
        var db = await BaseDeDatosDePrueba.CrearAsync();
        IdsBase ids;
        await using (var uow = await db.Uow.BeginAsync())
        {
            ids = await Datos.IdsBaseAsync(uow);
            await Datos.GuardarConfiguracionAsync(uow, Datos.F("2026-10-31"));
            await uow.CommitAsync();
        }

        return new EscenarioKardex(db, ids, new RelojFijo(Datos.F(hoy)));
    }

    public async ValueTask DisposeAsync() => await Db.DisposeAsync();

    // ---- Lectura ----------------------------------------------------------------------------------------------

    public async Task<Animal> AnimalAsync(string chapeta)
    {
        await using var uow = await Db.Uow.OpenReadOnlyAsync();
        return await uow.Animales.GetByChapetaAsync(chapeta) ?? throw new InvalidOperationException($"No existe el animal {chapeta}.");
    }

    public async Task<int> IdAsync(string chapeta) => (await AnimalAsync(chapeta)).Id;

    public async Task<bool> ExisteAnimalAsync(string chapeta)
    {
        await using var uow = await Db.Uow.OpenReadOnlyAsync();
        return await uow.Animales.GetByChapetaAsync(chapeta) is not null;
    }

    public async Task<Remision> RemisionAsync(string numero)
    {
        await using var uow = await Db.Uow.OpenReadOnlyAsync();
        return await uow.Remisiones.GetByNumeroAsync(numero) ?? throw new InvalidOperationException($"No existe la remisión {numero}.");
    }

    public async Task<IReadOnlyList<RemisionDetalle>> DetallesAsync(string numeroRemision)
    {
        await using var uow = await Db.Uow.OpenReadOnlyAsync();
        var remision = await uow.Remisiones.GetByNumeroAsync(numeroRemision) ?? throw new InvalidOperationException();
        return await uow.RemisionDetalles.GetByRemisionAsync(remision.Id);
    }

    public async Task<IReadOnlyList<MovimientoInventario>> MovimientosAsync(string chapeta)
    {
        await using var uow = await Db.Uow.OpenReadOnlyAsync();
        var animal = await uow.Animales.GetByChapetaAsync(chapeta) ?? throw new InvalidOperationException();
        return await uow.Movimientos.GetByAnimalAsync(animal.Id);
    }

    public async Task<IReadOnlyList<MovimientoInventario>> MovimientosDeRemisionAsync(string numeroRemision)
    {
        await using var uow = await Db.Uow.OpenReadOnlyAsync();
        var remision = await uow.Remisiones.GetByNumeroAsync(numeroRemision) ?? throw new InvalidOperationException();
        return await uow.Movimientos.GetByRemisionAsync(remision.Id);
    }

    public Task<long> ContarAsync(string tabla) => Db.EscalarAsync<long>($"SELECT COUNT(*) FROM {tabla}");

    // ---- Alta directa (sin pasar por el Kardex) -----------------------------------------------------------------

    /// <summary>Inserta un animal con su asiento de entrada para partir de un estado conocido.</summary>
    public async Task<Animal> AltaDirectaAsync(Animal animal)
    {
        await using var uow = await Db.Uow.BeginAsync();
        await Datos.AltaAsync(uow, animal);
        await uow.CommitAsync();
        return animal;
    }

    public Task<Animal> AltaVacaAsync(string chapeta, string nacimiento = "2022-01-10", decimal valor = 6_000_000m, int? granja = null, string? orientacion = null, int partos = 2)
        => AltaDirectaAsync(Datos.Animal(Ids, chapeta, Sexo.Hembra, nacimiento, NombresBase.CategoriaVacasGeneral, valor,
            orientacion: orientacion, partos: partos, granjaId: granja, registro: "2026-06-01"));

    public Task<Animal> AltaTerneraAsync(string chapeta, string nacimiento = "2026-09-01", decimal valor = 800_000m, int? granja = null)
        => AltaDirectaAsync(Datos.Animal(Ids, chapeta, Sexo.Hembra, nacimiento, NombresBase.CategoriaTerneros, valor, granjaId: granja, registro: "2026-09-01"));

    // ---- Constructores de comandos -------------------------------------------------------------------------------

    public NacimientoDto Nacimiento(
        string chapeta, string sexo = Sexo.Hembra, string nacimiento = "2026-11-06", string madre = "044", int? granja = null) => new()
    {
        GranjaId = granja ?? Ids.Tesorito,
        FechaNacimiento = Datos.F(nacimiento),
        ChapetaAmarilla = chapeta,
        Sexo = sexo,
        Raza = "Holstein",
        OrientacionId = Ids.Lacteo,
        PesoAlNacerKg = 38m,
        MadreIdentificacion = madre,
        EsInseminacion = false,
    };

    public CompraAnimalDto AnimalComprado(
        string chapeta, string sexo, string nacimiento, decimal precio, int partos = 0, bool carne = false) => new()
    {
        ChapetaAmarilla = chapeta,
        Sexo = sexo,
        FechaNacimiento = Datos.F(nacimiento),
        Raza = "Holstein",
        PesoKg = 400m,
        OrientacionId = carne ? Ids.Carne : Ids.Lacteo,
        NumeroPartos = partos,
        PrecioFactura = precio,
    };

    public CompraDto Compra(string factura, params CompraAnimalDto[] animales) => new()
    {
        NumeroFactura = factura,
        OrdenCompra = "OC-2026-018",
        FechaCompra = Datos.F("2026-10-22"),
        GranjaDestinoId = Ids.Tesorito,
        Proveedor = "Ganadería La Soledad",
        ProveedorNit = "890.123.456-1",
        Observaciones = "Compra de prueba",
        Animales = [.. animales],
    };

    public async Task<VentaDto> VentaAsync(
        string remision, string oficio, string recibo, int? granjaOrigen = null, params (string Chapeta, decimal Precio)[] animales)
    {
        var lista = new List<VentaAnimalDto>();
        foreach (var (chapeta, precio) in animales)
        {
            lista.Add(new VentaAnimalDto { AnimalId = await IdAsync(chapeta), PrecioVenta = precio });
        }

        return new VentaDto
        {
            NumeroOficio = oficio,
            FechaOficio = Datos.F("2026-10-15"),
            ProgramaId = Ids.ProgramaGanaderia,
            NumeroRemision = remision,
            FechaRemision = Datos.F("2026-10-20"),
            ReciboCaja = recibo,
            GranjaOrigenId = granjaOrigen ?? Ids.Tesorito,
            Comprador = "Frigorífico Manizales",
            Observaciones = "Venta de prueba",
            Animales = lista,
        };
    }

    public async Task<BajaDto> BajaAsync(string remision, string clase, int? granjaOrigen = null, params string[] chapetas)
    {
        var lista = new List<BajaAnimalDto>();
        foreach (var chapeta in chapetas)
        {
            lista.Add(new BajaAnimalDto { AnimalId = await IdAsync(chapeta) });
        }

        return new BajaDto
        {
            NumeroRemision = remision,
            FechaBaja = Datos.F("2026-10-15"),
            GranjaOrigenId = granjaOrigen ?? Ids.Tesorito,
            ClaseSalida = clase,
            DocumentoSoporte = "Acta Sanitaria N°4",
            Observaciones = "Baja de prueba",
            Animales = lista,
        };
    }

    public async Task<TrasladoDto> TrasladoAsync(string remision, int origen, int destino, params string[] chapetas)
    {
        var lista = new List<TrasladoAnimalDto>();
        foreach (var chapeta in chapetas)
        {
            lista.Add(new TrasladoAnimalDto { AnimalId = await IdAsync(chapeta) });
        }

        return new TrasladoDto
        {
            NumeroRemision = remision,
            FechaTraslado = Datos.F("2026-10-24"),
            GranjaOrigenId = origen,
            GranjaDestinoId = destino,
            Observaciones = "Traslado de prueba",
            Animales = lista,
        };
    }

    // ---- Invariante contable del libro -------------------------------------------------------------------------------

    /// <summary>
    /// El libro del Kardex nunca se descuadra: el saldo de cada animal es su valor contable si está Disponible y cero
    /// si salió del inventario, y el saldo del libro de cada granja es la suma del valor de sus animales Disponibles.
    /// </summary>
    public async Task AsegurarInvarianteAsync()
    {
        await using var uow = await Db.Uow.OpenReadOnlyAsync();
        var animales = await uow.Animales.GetAllAsync();
        var saldos = await uow.Movimientos.GetSaldosPorAnimalAsync();

        foreach (var animal in animales)
        {
            var esperado = animal.Estado == EstadoAnimal.Disponible ? animal.ValorContableActual : 0m;
            saldos.GetValueOrDefault(animal.Id).Should().Be(esperado, $"el libro de {animal.ChapetaAmarilla} debe cuadrar con su estado ({animal.Estado})");
        }

        var movimientos = await uow.Movimientos.GetAllAsync();
        // All() y no OnlyContain(): un libro vacío (p. ej. tras anular la única compra) también cuadra.
        movimientos.All(m => (m.GranjaOrigenId == null) != (m.GranjaDestinoId == null)).Should().BeTrue("cada asiento afecta a una sola granja");
        foreach (var granja in await uow.Granjas.GetAllAsync())
        {
            var libro = movimientos.Where(m => (m.GranjaOrigenId ?? m.GranjaDestinoId) == granja.Id).Sum(m => m.CostoAsociado);
            var valor = animales.Where(a => a.Estado == EstadoAnimal.Disponible && a.GranjaId == granja.Id).Sum(a => a.ValorContableActual);
            libro.Should().Be(valor, $"el saldo del libro de {granja.Nombre} debe ser el valor de sus animales disponibles");
        }
    }
}
