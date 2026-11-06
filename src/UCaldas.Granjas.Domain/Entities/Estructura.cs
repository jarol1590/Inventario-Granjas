namespace UCaldas.Granjas.Domain.Entities;

/// <summary>Configuración global del sistema (fila única). Si no existe, es el primer arranque.</summary>
public class ConfiguracionSistema
{
    public int Id { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool BackupAutomaticoSalir { get; set; } = true;
    public string? RutaBackupPredeterminada { get; set; }

    /// <summary>Último fin de mes ya valorizado (extensión v2.1).</summary>
    public DateTime? UltimoCierreValorizacion { get; set; }
}

/// <summary>Datos de la autoridad que firma los informes institucionales (fila única).</summary>
public class ConfiguracionFirma
{
    public int Id { get; set; }
    public string NombreAutoridad { get; set; } = string.Empty;
    public string CargoAutoridad { get; set; } = string.Empty;
    public string EntidadFacultad { get; set; } = string.Empty;
    public string? Telefono { get; set; }

    /// <summary>Imagen de la firma: ruta local o Base64 (con o sin prefijo <c>data:image/...;base64,</c>).</summary>
    public string? RutaImagenFirma { get; set; }
}

/// <summary>Sede de la Universidad (Tesorito, La Cruz, Montelindo).</summary>
public class Granja
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;
    public string? Observaciones { get; set; }

    /// <summary>Orientación predominante de la sede; solo se usa para sugerirla en los traslados (extensión v2.1).</summary>
    public int? OrientacionId { get; set; }
}

/// <summary>Programa productivo (Ganadería, Avicultura...).</summary>
public class Programa
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;

    /// <summary>INDIVIDUAL (por chapeta) o LOTE (por población).</summary>
    public string TipoInventario { get; set; } = string.Empty;

    public string? Observaciones { get; set; }
}

/// <summary>Relación N:M entre granjas y programas.</summary>
public class GranjaPrograma
{
    public int Id { get; set; }
    public int GranjaId { get; set; }
    public int ProgramaId { get; set; }
}

/// <summary>Orientación productiva del semoviente (Lácteo, Carne/Levante).</summary>
public class OrientacionProductiva
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

/// <summary>Categoría biológica (Terneros, Levante, Novilla Vientre, Vacas General, Reproductor...).</summary>
public class CategoriaEtapa
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Macho, Hembra o Ambos.</summary>
    public string Sexo { get; set; } = string.Empty;

    public int ProgramaId { get; set; }
}
