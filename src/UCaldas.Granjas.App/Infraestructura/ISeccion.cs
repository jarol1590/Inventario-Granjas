namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Pantalla del menú lateral. La ventana principal la crea, la carga y la recarga cuando cambian los filtros.</summary>
public interface ISeccion
{
    Task CargarAsync();

    /// <summary>false si hay cambios sin guardar que el usuario decidió conservar (se cancela la navegación).</summary>
    bool PuedeSalir() => true;
}

/// <summary>Marca las secciones que dependen de los filtros globales Granja / Programa.</summary>
public interface IUsaFiltroGlobal : ISeccion
{
    /// <summary>false si la sección elige su propio programa (p. ej. las pestañas de Animales) y no usa el combo global.</summary>
    bool MostrarFiltroDePrograma => true;
}

/// <summary>Secciones que muestran la barra inferior de acciones rápidas (nacimiento, compra, salidas, informe).</summary>
public interface IConAccionesRapidas : IUsaFiltroGlobal
{
}

/// <summary>Sección que reemplaza la barra superior de módulos por sus propias pestañas (p. ej. Configuración).</summary>
public interface IConBarraSuperior : ISeccion
{
    BarraDePestanas BarraSuperior { get; }
}
