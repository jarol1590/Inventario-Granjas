namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Permite a un ViewModel pedir a la ventana principal que cambie de sección o se recargue.</summary>
public interface INavegador
{
    /// <summary>Abre la sección con esa clave (ver <c>ShellViewModel.Clave*</c>).</summary>
    void IrA(string clave);

    /// <summary>Vuelve a cargar filtros y pantalla de inicio (p. ej. después de restaurar un respaldo).</summary>
    Task RecargarTodoAsync();

    /// <summary>Refresca los combos Granja / Programa de la ventana principal (p. ej. tras crear o inactivar una granja).</summary>
    Task RecargarFiltrosAsync();
}
