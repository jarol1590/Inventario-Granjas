using System.ComponentModel.DataAnnotations;
using UCaldas.Granjas.Domain;

namespace UCaldas.Granjas.Tests.Catalogos;

/// <summary>Formato de teléfonos: solo el símbolo + al inicio y dígitos.</summary>
public class ReglaTelefonoTests
{
    [Theory]
    [InlineData("+573001234567")]
    [InlineData("3001234567")]
    [InlineData(" 8781586 ")]
    [InlineData("")]
    [InlineData(null)]
    public void Un_telefono_valido_o_vacio_se_acepta(string? valor)
    {
        var accion = () => Regla.ExigirTelefono(valor, "El teléfono");
        accion.Should().NotThrow();
    }

    [Theory]
    [InlineData("300-123")]
    [InlineData("(6) 123")]
    [InlineData("abc")]
    [InlineData("+57 300")]
    [InlineData("3001+")]
    [InlineData("++57")]
    [InlineData("+")]
    [InlineData("3001,3002")]
    public void Un_telefono_con_otros_simbolos_genera_aviso_de_formato(string valor)
    {
        var accion = () => Regla.ExigirTelefono(valor, "El teléfono");
        accion.Should().Throw<ValidationException>().WithMessage("*formato no válido*");
    }

    [Theory]
    [InlineData("8781586")]
    [InlineData("8781586, +573001234567")]
    [InlineData("")]
    public void Varios_telefonos_separados_por_coma_se_aceptan(string valor)
    {
        var accion = () => Regla.ExigirTelefonos(valor, "Los teléfonos");
        accion.Should().NotThrow();
    }

    [Theory]
    [InlineData("8781586; 3001")]
    [InlineData("8781586,,3001")]
    [InlineData("8781586, 30-01")]
    public void Varios_telefonos_con_un_numero_invalido_generan_aviso(string valor)
    {
        var accion = () => Regla.ExigirTelefonos(valor, "Los teléfonos");
        accion.Should().Throw<ValidationException>().WithMessage("*formato no válido*");
    }
}
