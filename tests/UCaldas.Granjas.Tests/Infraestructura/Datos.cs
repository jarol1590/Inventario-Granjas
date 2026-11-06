using System.Globalization;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Tests.Infraestructura;

/// <summary>Reloj con fecha fija para pruebas deterministas.</summary>
public sealed class RelojFijo(DateTime ahora) : IReloj
{
    public DateTime Ahora { get; set; } = ahora;
}

/// <summary>Identificadores de los datos semilla, resueltos por nombre/código para no depender del orden.</summary>
public sealed class IdsBase
{
    public int Tesorito { get; init; }

    public int LaCruz { get; init; }

    public int Montelindo { get; init; }

    public int Lacteo { get; init; }

    public int Carne { get; init; }

    public int ProgramaGanaderia { get; init; }

    public IReadOnlyDictionary<string, int> Categorias { get; init; } = new Dictionary<string, int>();

    public int Categoria(string nombre) => Categorias[nombre];
}

public static class Datos
{
    /// <summary>Convierte "2026-10-31" en fecha sin depender de la cultura del equipo.</summary>
    public static DateTime F(string iso) => DateTime.Parse(iso, CultureInfo.InvariantCulture);

    public static async Task<IdsBase> IdsBaseAsync(IUnitOfWork uow)
    {
        var programa = await uow.Programas.GetByCodigoAsync(NombresBase.ProgramaGanaderia)
            ?? throw new InvalidOperationException("Falta el programa semilla PRG-GAN.");
        var categorias = await uow.Categorias.GetByProgramaAsync(programa.Id);

        return new IdsBase
        {
            Tesorito = (await uow.Granjas.GetByCodigoAsync("G-01"))!.Id,
            LaCruz = (await uow.Granjas.GetByCodigoAsync("G-02"))!.Id,
            Montelindo = (await uow.Granjas.GetByCodigoAsync("G-03"))!.Id,
            Lacteo = (await uow.Orientaciones.GetByNombreAsync(NombresBase.OrientacionLactea))!.Id,
            Carne = (await uow.Orientaciones.GetByNombreAsync(NombresBase.OrientacionCarne))!.Id,
            ProgramaGanaderia = programa.Id,
            Categorias = categorias.ToDictionary(c => c.Nombre, c => c.Id),
        };
    }

    /// <summary>Hembra lechera de Tesorito nacida el 20/06/2026, categoría Terneros, con valores de ejemplo.</summary>
    public static Animal NuevoAnimal(string chapeta, IdsBase ids) => new()
    {
        ChapetaAmarilla = chapeta,
        Sexo = Sexo.Hembra,
        FechaNacimiento = new DateTime(2026, 6, 20),
        ClaseEntrada = ClaseEntrada.Nacimiento,
        Raza = "Holstein",
        PesoActualKg = 35m,
        OrientacionId = ids.Lacteo,
        ValorCompraOrigen = 0m,
        ValorContableActual = 150000m,
        GranjaId = ids.Tesorito,
        CategoriaId = ids.Categoria(NombresBase.CategoriaTerneros),
        Estado = EstadoAnimal.Disponible,
    };

    /// <summary>Animal con los datos que importan para valorizar; el resto toma valores razonables.</summary>
    public static Animal Animal(
        IdsBase ids,
        string chapeta,
        string sexo,
        string nacimiento,
        string categoria,
        decimal valor,
        string? orientacion = null,
        int partos = 0,
        bool exento = false,
        string? registro = null,
        int? granjaId = null,
        string clase = ClaseEntrada.Nacimiento)
    {
        var fechaNacimiento = F(nacimiento);
        return new Animal
        {
            ChapetaAmarilla = chapeta,
            Sexo = sexo,
            FechaNacimiento = fechaNacimiento,
            ClaseEntrada = clase,
            Raza = "Holstein",
            PesoActualKg = 100m,
            OrientacionId = orientacion == NombresBase.OrientacionCarne ? ids.Carne : ids.Lacteo,
            ValorCompraOrigen = clase == ClaseEntrada.Compra ? valor : 0m,
            ValorContableActual = valor,
            GranjaId = granjaId ?? ids.Tesorito,
            CategoriaId = ids.Categoria(categoria),
            Estado = EstadoAnimal.Disponible,
            NumeroPartos = partos,
            ExentoValorizacion = exento,
            FechaRegistro = registro is null ? fechaNacimiento : F(registro),
        };
    }

    /// <summary>
    /// Inserta el animal y su asiento de entrada en el libro por su valor contable (como lo hace el alta real),
    /// de modo que se cumple la invariante: saldo del libro = valor contable.
    /// </summary>
    public static async Task<Animal> AltaAsync(IUnitOfWork uow, Animal animal, DateTime? fechaMovimiento = null)
    {
        await uow.Animales.InsertAsync(animal);
        await uow.Movimientos.InsertAsync(new MovimientoInventario
        {
            AnimalId = animal.Id,
            TipoMovimiento = animal.ClaseEntrada == ClaseEntrada.Compra ? TipoMovimiento.Compra : TipoMovimiento.Nacimiento,
            FechaMovimiento = fechaMovimiento ?? animal.FechaRegistro,
            GranjaDestinoId = animal.GranjaId,
            CostoAsociado = animal.ValorContableActual,
        });
        return animal;
    }

    /// <summary>Crea la fila de configuración (lo que hace el primer arranque al elegir la contraseña).</summary>
    public static Task GuardarConfiguracionAsync(IUnitOfWork uow, DateTime? ultimoCierre = null)
        => uow.Configuracion.GuardarSistemaAsync(new ConfiguracionSistema
        {
            PasswordHash = "hash-de-prueba",
            BackupAutomaticoSalir = false,
            UltimoCierreValorizacion = ultimoCierre,
        });
}
