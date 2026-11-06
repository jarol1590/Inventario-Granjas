using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Valorizacion;

/// <summary>
/// Elige la fila de la matriz de valorización que le corresponde a un animal. Función pura: no accede a datos.
/// </summary>
public static class TarifaSelector
{
    /// <summary>
    /// Candidatas: sexo propio o "Ambos", orientación propia o "Todas", edad dentro del rango (inclusivo en ambos
    /// extremos) y, si la fila exige partos, que el animal tenga al menos uno.
    /// <para>Desempate por especificidad, en este orden:</para>
    /// <list type="number">
    /// <item>orientación específica sobre "Todas" (regla del documento);</item>
    /// <item>fila que exige partos cuando el animal los tiene (extensión: una vaca con parto no es "Novilla Vientre");</item>
    /// <item>sexo específico sobre "Ambos" (extensión);</item>
    /// <item>menor edad mínima (regla del documento);</item>
    /// <item>menor id, para que el resultado sea siempre el mismo.</item>
    /// </list>
    /// </summary>
    /// <returns>La fila elegida o <c>null</c> si ninguna tarifa aplica.</returns>
    public static TablaValorizacionBase? Seleccionar(
        IEnumerable<TablaValorizacionBase> tarifas, string sexo, int orientacionId, int numeroPartos, int edadMeses)
    {
        var tieneParto = numeroPartos >= 1;

        return tarifas
            .Where(t => (t.Sexo == sexo || t.Sexo == Sexo.Ambos)
                        && (t.OrientacionId is null || t.OrientacionId == orientacionId)
                        && edadMeses >= t.EdadMinimaMeses
                        && edadMeses <= t.EdadMaximaMeses
                        && (!t.RequierePartos || tieneParto))
            .OrderByDescending(t => t.OrientacionId is not null)
            .ThenByDescending(t => t.RequierePartos && tieneParto)
            .ThenByDescending(t => t.Sexo != Sexo.Ambos)
            .ThenBy(t => t.EdadMinimaMeses)
            .ThenBy(t => t.Id)
            .FirstOrDefault();
    }
}
