using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Kardex;
using UCaldas.Granjas.Tests.Infraestructura;

namespace UCaldas.Granjas.Tests.Kardex;

public class DocumentValidationServiceTests
{
    private static readonly DateTime Hoy = Datos.F("2026-11-06");
    private readonly DocumentValidationService _servicio = new();

    private static VentaDto VentaValida() => new()
    {
        NumeroOficio = "OF-120",
        FechaOficio = Datos.F("2026-10-01"),
        ProgramaId = 1,
        NumeroRemision = "REM-001",
        FechaRemision = Datos.F("2026-10-20"),
        ReciboCaja = "RC-8841",
        GranjaOrigenId = 1,
        Comprador = "Frigorífico Manizales",
        Animales = [new VentaAnimalDto { AnimalId = 5, PrecioVenta = 1_000_000m }],
    };

    // ---- Jerarquía documental de 3 niveles ------------------------------------------------------------------------

    [Fact]
    public void Una_venta_completa_es_valida()
    {
        var act = () => _servicio.ValidarVenta(VentaValida(), Hoy);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("", "REM-001", "RC-1", "*número de oficio*")]
    [InlineData("OF-1", "", "RC-1", "*número de remisión*")]
    [InlineData("OF-1", "REM-1", "", "*recibo de caja*obligatorio en las ventas*")]
    [InlineData("OF-1", "REM-1", "   ", "*recibo de caja*obligatorio en las ventas*")]
    public void Una_venta_exige_oficio_remision_y_recibo_de_caja(string oficio, string remision, string recibo, string mensaje)
    {
        var dto = VentaValida();
        dto.NumeroOficio = oficio;
        dto.NumeroRemision = remision;
        dto.ReciboCaja = recibo;

        var act = () => _servicio.ValidarVenta(dto, Hoy);

        act.Should().Throw<ValidationException>().WithMessage(mensaje);
    }

    [Fact]
    public void Una_venta_exige_programa_comprador_granja_y_animales()
    {
        var sinPrograma = VentaValida();
        sinPrograma.ProgramaId = 0;
        var sinComprador = VentaValida();
        sinComprador.Comprador = " ";
        var sinGranja = VentaValida();
        sinGranja.GranjaOrigenId = 0;
        var sinAnimales = VentaValida();
        sinAnimales.Animales.Clear();

        ((Action)(() => _servicio.ValidarVenta(sinPrograma, Hoy))).Should().Throw<ValidationException>().WithMessage("*programa*");
        ((Action)(() => _servicio.ValidarVenta(sinComprador, Hoy))).Should().Throw<ValidationException>().WithMessage("*comprador*");
        ((Action)(() => _servicio.ValidarVenta(sinGranja, Hoy))).Should().Throw<ValidationException>().WithMessage("*granja de origen*");
        ((Action)(() => _servicio.ValidarVenta(sinAnimales, Hoy))).Should().Throw<ValidationException>().WithMessage("*al menos un animal*");
    }

    [Fact]
    public void Una_venta_rechaza_animales_repetidos_precio_cero_y_fecha_futura()
    {
        var repetidos = VentaValida();
        repetidos.Animales.Add(new VentaAnimalDto { AnimalId = 5, PrecioVenta = 1m });
        var sinPrecio = VentaValida();
        sinPrecio.Animales[0].PrecioVenta = 0m;
        var futura = VentaValida();
        futura.FechaRemision = Datos.F("2026-11-07");

        ((Action)(() => _servicio.ValidarVenta(repetidos, Hoy))).Should().Throw<ValidationException>().WithMessage("*repetidos*");
        ((Action)(() => _servicio.ValidarVenta(sinPrecio, Hoy))).Should().Throw<ValidationException>().WithMessage("*precio de venta*");
        ((Action)(() => _servicio.ValidarVenta(futura, Hoy))).Should().Throw<ValidationException>().WithMessage("*no puede ser futura*");
    }

    [Fact]
    public void La_jerarquia_exige_oficio_y_recibo_en_la_venta()
    {
        var sinOficio = new Remision { TipoMovimiento = TipoRemision.Venta, ReciboCaja = "RC-1" };
        var sinRecibo = new Remision { TipoMovimiento = TipoRemision.Venta, OficioId = 3 };
        var completa = new Remision { TipoMovimiento = TipoRemision.Venta, OficioId = 3, ReciboCaja = "RC-1" };

        ((Action)(() => _servicio.ValidarJerarquiaDocumental(sinOficio))).Should().Throw<ValidationException>().WithMessage("*Oficio de Venta*");
        ((Action)(() => _servicio.ValidarJerarquiaDocumental(sinRecibo))).Should().Throw<ValidationException>().WithMessage("*recibo de caja*");
        ((Action)(() => _servicio.ValidarJerarquiaDocumental(completa))).Should().NotThrow();
    }

    [Theory]
    [InlineData(TipoRemision.Traslado)]
    [InlineData(TipoRemision.Baja)]
    [InlineData(TipoRemision.Compra)]
    public void Traslados_bajas_y_compras_solo_llevan_remision(string tipo)
    {
        var conOficio = new Remision { TipoMovimiento = tipo, OficioId = 3 };
        var conRecibo = new Remision { TipoMovimiento = tipo, ReciboCaja = "RC-1" };
        var limpia = new Remision { TipoMovimiento = tipo };

        ((Action)(() => _servicio.ValidarJerarquiaDocumental(conOficio))).Should().Throw<ValidationException>().WithMessage("*solo lleva Remisión*");
        ((Action)(() => _servicio.ValidarJerarquiaDocumental(conRecibo))).Should().Throw<ValidationException>().WithMessage("*no admite Oficio*");
        ((Action)(() => _servicio.ValidarJerarquiaDocumental(limpia))).Should().NotThrow();
    }

    [Fact]
    public void Un_tipo_de_documento_desconocido_se_rechaza()
    {
        var act = () => _servicio.ValidarJerarquiaDocumental(new Remision { TipoMovimiento = "OTRO" });

        act.Should().Throw<ValidationException>().WithMessage("*tipo de documento no es válido*");
    }

    // ---- Traslado y baja -------------------------------------------------------------------------------------------

    [Fact]
    public void Un_traslado_exige_origen_y_destino_distintos()
    {
        var dto = new TrasladoDto
        {
            NumeroRemision = "REM-T-1", FechaTraslado = Datos.F("2026-10-24"), GranjaOrigenId = 1, GranjaDestinoId = 1,
            Animales = [new TrasladoAnimalDto { AnimalId = 4 }],
        };

        var act = () => _servicio.ValidarTraslado(dto, Hoy);

        act.Should().Throw<ValidationException>().WithMessage("*origen y la de destino deben ser distintas*");
    }

    [Fact]
    public void Un_traslado_exige_remision_fecha_y_animales()
    {
        var sinNumero = new TrasladoDto { FechaTraslado = Hoy, GranjaOrigenId = 1, GranjaDestinoId = 2, Animales = [new TrasladoAnimalDto { AnimalId = 1 }] };
        var sinAnimales = new TrasladoDto { NumeroRemision = "R", FechaTraslado = Hoy, GranjaOrigenId = 1, GranjaDestinoId = 2 };
        var sinFecha = new TrasladoDto { NumeroRemision = "R", GranjaOrigenId = 1, GranjaDestinoId = 2, Animales = [new TrasladoAnimalDto { AnimalId = 1 }] };

        ((Action)(() => _servicio.ValidarTraslado(sinNumero, Hoy))).Should().Throw<ValidationException>().WithMessage("*número de remisión*");
        ((Action)(() => _servicio.ValidarTraslado(sinAnimales, Hoy))).Should().Throw<ValidationException>().WithMessage("*al menos un animal*");
        ((Action)(() => _servicio.ValidarTraslado(sinFecha, Hoy))).Should().Throw<ValidationException>().WithMessage("*fecha del traslado es obligatoria*");
    }

    [Theory]
    [InlineData("VENTA")]
    [InlineData("TRASLADO")]
    [InlineData("OTRA")]
    [InlineData("")]
    public void Una_baja_solo_admite_muerte_hurto_o_descarte(string clase)
    {
        var dto = new BajaDto
        {
            NumeroRemision = "REM-B-1", FechaBaja = Datos.F("2026-10-15"), GranjaOrigenId = 1, ClaseSalida = clase,
            DocumentoSoporte = "Acta", Animales = [new BajaAnimalDto { AnimalId = 2 }],
        };

        var act = () => _servicio.ValidarBaja(dto, Hoy);

        act.Should().Throw<ValidationException>().WithMessage("*MUERTE, HURTO o DESCARTE_SANITARIO*");
    }

    [Fact]
    public void Una_baja_exige_documento_de_soporte()
    {
        var dto = new BajaDto
        {
            NumeroRemision = "REM-B-1", FechaBaja = Datos.F("2026-10-15"), GranjaOrigenId = 1, ClaseSalida = ClaseSalida.Muerte,
            DocumentoSoporte = " ", Animales = [new BajaAnimalDto { AnimalId = 2 }],
        };

        var act = () => _servicio.ValidarBaja(dto, Hoy);

        act.Should().Throw<ValidationException>().WithMessage("*documento de soporte*");
    }

    // ---- Nacimiento --------------------------------------------------------------------------------------------------

    private static NacimientoDto NacimientoValido() => new()
    {
        GranjaId = 1, FechaNacimiento = Datos.F("2026-11-05"), ChapetaAmarilla = "0940-26", Sexo = Sexo.Hembra,
        Raza = "Holstein", OrientacionId = 1, PesoAlNacerKg = 38m, MadreIdentificacion = "044",
    };

    [Fact]
    public void Un_nacimiento_valido_pasa()
    {
        var act = () => _servicio.ValidarNacimiento(NacimientoValido(), Hoy);

        act.Should().NotThrow();
    }

    [Fact]
    public void La_inseminacion_exige_la_raza_del_padre_y_la_monta_directa_no()
    {
        var inseminacion = NacimientoValido();
        inseminacion.EsInseminacion = true;
        var montaDirecta = NacimientoValido();

        ((Action)(() => _servicio.ValidarNacimiento(inseminacion, Hoy))).Should().Throw<ValidationException>().WithMessage("*raza del padre*");
        ((Action)(() => _servicio.ValidarNacimiento(montaDirecta, Hoy))).Should().NotThrow();

        inseminacion.RazaPadre = "Jersey";
        ((Action)(() => _servicio.ValidarNacimiento(inseminacion, Hoy))).Should().NotThrow();
    }

    [Theory]
    [InlineData("chapeta", "*chapeta amarilla es obligatorio*")]
    [InlineData("sexo", "*sexo debe ser Macho o Hembra*")]
    [InlineData("raza", "*raza es obligatorio*")]
    [InlineData("madre", "*identificación de la madre es obligatorio*")]
    [InlineData("futura", "*no puede ser futura*")]
    [InlineData("peso", "*peso al nacer no puede ser negativo*")]
    public void Un_nacimiento_rechaza_datos_obligatorios_faltantes(string campo, string mensaje)
    {
        var dto = NacimientoValido();
        switch (campo)
        {
            case "chapeta": dto.ChapetaAmarilla = " "; break;
            case "sexo": dto.Sexo = Sexo.Ambos; break;
            case "raza": dto.Raza = ""; break;
            case "madre": dto.MadreIdentificacion = ""; break;
            case "futura": dto.FechaNacimiento = Datos.F("2026-11-07"); break;
            case "peso": dto.PesoAlNacerKg = -1; break;
        }

        var act = () => _servicio.ValidarNacimiento(dto, Hoy);

        act.Should().Throw<ValidationException>().WithMessage(mensaje);
    }

    // ---- Compra ------------------------------------------------------------------------------------------------------

    private static CompraDto CompraValida() => new()
    {
        NumeroFactura = "FAC-99420", FechaCompra = Datos.F("2026-10-22"), GranjaDestinoId = 1, Proveedor = "Ganadería La Soledad",
        Animales =
        [
            new CompraAnimalDto
            {
                ChapetaAmarilla = "0940-26", Sexo = Sexo.Hembra, FechaNacimiento = Datos.F("2026-06-22"), Raza = "Holstein",
                OrientacionId = 1, PesoKg = 300m, PrecioFactura = 3_225_970m,
            },
        ],
    };

    [Fact]
    public void Una_compra_valida_pasa()
    {
        var act = () => _servicio.ValidarCompra(CompraValida(), Hoy);

        act.Should().NotThrow();
    }

    [Fact]
    public void Una_compra_no_admite_animales_sin_fecha_de_nacimiento_o_posterior_a_la_compra()
    {
        var sinFecha = CompraValida();
        sinFecha.Animales[0].FechaNacimiento = default;
        var posterior = CompraValida();
        posterior.Animales[0].FechaNacimiento = Datos.F("2026-10-23");

        ((Action)(() => _servicio.ValidarCompra(sinFecha, Hoy))).Should().Throw<ValidationException>().WithMessage("*fecha de nacimiento es obligatoria*");
        ((Action)(() => _servicio.ValidarCompra(posterior, Hoy))).Should().Throw<ValidationException>().WithMessage("*posterior a la compra*");
    }

    [Fact]
    public void Una_compra_rechaza_chapetas_repetidas_precio_cero_y_machos_con_partos()
    {
        var repetidas = CompraValida();
        repetidas.Animales.Add(new CompraAnimalDto
        {
            ChapetaAmarilla = "0940-26", Sexo = Sexo.Hembra, FechaNacimiento = Datos.F("2026-06-22"), Raza = "H",
            OrientacionId = 1, PrecioFactura = 1m,
        });
        var sinPrecio = CompraValida();
        sinPrecio.Animales[0].PrecioFactura = 0m;
        var machoConPartos = CompraValida();
        machoConPartos.Animales[0].Sexo = Sexo.Macho;
        machoConPartos.Animales[0].NumeroPartos = 1;

        ((Action)(() => _servicio.ValidarCompra(repetidas, Hoy))).Should().Throw<ValidationException>().WithMessage("*repetida*");
        ((Action)(() => _servicio.ValidarCompra(sinPrecio, Hoy))).Should().Throw<ValidationException>().WithMessage("*precio de factura*");
        ((Action)(() => _servicio.ValidarCompra(machoConPartos, Hoy))).Should().Throw<ValidationException>().WithMessage("*macho no puede tener partos*");
    }

    [Fact]
    public void Una_compra_exige_factura_proveedor_granja_y_animales()
    {
        var sinFactura = CompraValida();
        sinFactura.NumeroFactura = "";
        var sinProveedor = CompraValida();
        sinProveedor.Proveedor = "";
        var sinGranja = CompraValida();
        sinGranja.GranjaDestinoId = 0;
        var sinAnimales = CompraValida();
        sinAnimales.Animales.Clear();

        ((Action)(() => _servicio.ValidarCompra(sinFactura, Hoy))).Should().Throw<ValidationException>().WithMessage("*número de factura*");
        ((Action)(() => _servicio.ValidarCompra(sinProveedor, Hoy))).Should().Throw<ValidationException>().WithMessage("*proveedor*");
        ((Action)(() => _servicio.ValidarCompra(sinGranja, Hoy))).Should().Throw<ValidationException>().WithMessage("*granja destino*");
        ((Action)(() => _servicio.ValidarCompra(sinAnimales, Hoy))).Should().Throw<ValidationException>().WithMessage("*al menos un animal*");
    }
}
