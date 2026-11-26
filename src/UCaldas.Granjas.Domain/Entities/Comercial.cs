namespace UCaldas.Granjas.Domain.Entities;

/// <summary>Convenio institucional que puede ser comprador en una venta (extensión v2.8).</summary>
public class Convenio
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Nit { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string? CorreoElectronico { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Cliente particular o externo («Otro comprador») de una venta (extensión v2.8).</summary>
public class Cliente
{
    public int Id { get; set; }
    public string NombreRazonSocial { get; set; } = string.Empty;
    public string? NitCc { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? CorreoElectronico { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Producto o subproducto vendible de un programa (huevos, leche, hortalizas, frutales...) (extensión v2.8).</summary>
public class CatalogoProducto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int ProgramaId { get; set; }
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Cubeta, Unidad, Kg, Racimo, Bulto...</summary>
    public string UnidadMedida { get; set; } = string.Empty;

    /// <summary>Precio sugerido; la venta puede cambiarlo.</summary>
    public decimal PrecioUnitario { get; set; }

    public bool Activo { get; set; } = true;
}

/// <summary>Datos institucionales del módulo Remisiones y último consecutivo de recibos SP. Fila única (id = 1).</summary>
public class ConfiguracionRemisiones
{
    public int Id { get; set; }
    public string NombreDirector { get; set; } = string.Empty;
    public string CargoDirector { get; set; } = string.Empty;
    public string? TituloDirector { get; set; }
    public string JefeTesoreria { get; set; } = string.Empty;
    public string FacultadDepto { get; set; } = string.Empty;
    public string? Telefonos { get; set; }
    public string? Extensiones { get; set; }
    public string EntidadBancaria { get; set; } = string.Empty;
    public string TipoCuenta { get; set; } = string.Empty;
    public string NumeroCuenta { get; set; } = string.Empty;
    public int UltimoConsecutivoRecibo { get; set; }
}

/// <summary>Recibo de Caja SP: exactamente uno por remisión, sea de venta o de traslado (extensión v2.8).</summary>
public class ReciboCaja
{
    public int Id { get; set; }
    public int NumeroRecibo { get; set; }
    public string Prefijo { get; set; } = "SP";
    public int RemisionId { get; set; }
    public DateTime Fecha { get; set; }

    /// <summary>CONSIGNACION, EFECTIVO_CHEQUE o TRASLADO_INTERNO.</summary>
    public string FormaPago { get; set; } = string.Empty;

    public string? BancoConsignacion { get; set; }
    public bool EsEfectivo { get; set; }
    public string? Cheque1Num { get; set; }
    public string? Cheque2Num { get; set; }
    public decimal TotalPagado { get; set; }
    public string TotalLetras { get; set; } = string.Empty;
    public string? Observaciones { get; set; }

    /// <summary>Recibo de una remisión anulada: conserva su número.</summary>
    public bool Anulado { get; set; }
}
