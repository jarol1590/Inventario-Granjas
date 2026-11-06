namespace UCaldas.Granjas.App.Infraestructura;

public static class ContextoUi
{
    /// <summary>
    /// Ejecuta la acción en el siguiente turno del hilo de la interfaz. Sirve para revertir una selección rechazada:
    /// WPF ignora el cambio de la propiedad mientras el mismo enlace todavía está actualizando el origen.
    /// </summary>
    public static void Diferir(Action accion)
    {
        var contexto = SynchronizationContext.Current;
        if (contexto is null)
        {
            accion();
        }
        else
        {
            contexto.Post(_ => accion(), null);
        }
    }
}
