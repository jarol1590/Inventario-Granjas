namespace UCaldas.Granjas.Domain.Constants;

/// <summary>Valores de <c>sexo</c>. Los animales solo son Macho/Hembra; las tarifas y categorías admiten además "Ambos".</summary>
public static class Sexo
{
    public const string Macho = "Macho";
    public const string Hembra = "Hembra";
    public const string Ambos = "Ambos";

    public static bool EsDeAnimal(string? valor) => valor is Macho or Hembra;

    public static bool EsDeTarifa(string? valor) => valor is Macho or Hembra or Ambos;
}

/// <summary>Valores de <c>ANIMAL.estado</c>.</summary>
public static class EstadoAnimal
{
    public const string Disponible = "Disponible";
    public const string Vendido = "Vendido";
    public const string Hurtado = "Hurtado";
    public const string Muerto = "Muerto";

    /// <summary>Reservado por el esquema; un traslado entre granjas deja al animal Disponible en la sede destino.</summary>
    public const string Trasladado = "Trasladado";

    public static readonly IReadOnlyList<string> Todos = [Disponible, Vendido, Hurtado, Muerto, Trasladado];
}

/// <summary>Valores de <c>ANIMAL.clase_entrada</c>.</summary>
public static class ClaseEntrada
{
    public const string Nacimiento = "NACIMIENTO";
    public const string Compra = "COMPRA";
    public const string Traslado = "TRASLADO";
}

/// <summary>Valores de <c>REMISION.tipo_movimiento</c> (el documento).</summary>
public static class TipoRemision
{
    public const string Venta = "VENTA";
    public const string Traslado = "TRASLADO";
    public const string Compra = "COMPRA";
    public const string Baja = "BAJA";
}

/// <summary>Valores de <c>REMISION.clase_salida</c>.</summary>
public static class ClaseSalida
{
    public const string Venta = "VENTA";
    public const string Muerte = "MUERTE";
    public const string Hurto = "HURTO";
    public const string DescarteSanitario = "DESCARTE_SANITARIO";
    public const string Traslado = "TRASLADO";

    /// <summary>Clases que se registran como baja (sin Oficio ni Recibo de Caja).</summary>
    public static readonly IReadOnlyList<string> Bajas = [Muerte, Hurto, DescarteSanitario];
}

/// <summary>Valores de <c>MOVIMIENTO_INVENTARIO.tipo_movimiento</c> (el libro del Kardex).</summary>
public static class TipoMovimiento
{
    public const string Nacimiento = "Nacimiento";
    public const string Compra = "Compra";
    public const string Venta = "Venta";
    public const string Muerte = "Muerte";
    public const string Hurto = "Hurto";
    public const string Traslado = "Traslado";
    public const string Valorizacion = "Valorizacion";
}

/// <summary>Valores de <c>PROGRAMA.tipo_inventario</c>.</summary>
public static class TipoInventario
{
    public const string Individual = "INDIVIDUAL";
    public const string Lote = "LOTE";
}

/// <summary>Nombres de datos base sembrados que el código necesita reconocer.</summary>
public static class NombresBase
{
    public const string OrientacionLactea = "Lácteo";
    public const string OrientacionCarne = "Carne/Levante";

    public const string CategoriaTerneros = "Terneros";
    public const string CategoriaLevante = "Levante";
    public const string CategoriaNovillaVientre = "Novilla Vientre";
    public const string CategoriaVacasGeneral = "Vacas General";
    public const string CategoriaReproductor = "Reproductor";

    /// <summary>Categoría que recibe el animal cuando ninguna tarifa de la vigencia activa le aplica.</summary>
    public const string CategoriaSinClasificar = "Sin clasificar";

    public const string ProgramaGanaderia = "PRG-GAN";
}
