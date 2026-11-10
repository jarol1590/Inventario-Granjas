using UCaldas.Granjas.App.Infraestructura;

namespace UCaldas.Granjas.App.ViewModels;

/// <summary>Diálogo de confirmación de la anulación de un traslado, compartido por la lista y por el detalle.</summary>
internal static class AnulacionDeTraslados
{
    /// <summary>Pide confirmar y el motivo. Devuelve el motivo, o nulo si el usuario cancela en cualquiera de los dos pasos.</summary>
    public static string? PedirMotivo(IDialogService dialogos, string numeroRemision, int cantidad, string granjaOrigen)
    {
        var confirmado = dialogos.Confirmar(
            $"Se anulará el traslado {numeroRemision}: los animales trasladados ({Formato.Animales(cantidad)}) " +
            $"regresan a {granjaOrigen} y se reversan sus asientos del Kardex. El número de la remisión queda consumido.\n\n¿Desea continuar?",
            "Anular traslado", destructivo: true);

        return confirmado ? dialogos.Pedir("Anular traslado", "Motivo de la anulación:") : null;
    }
}
