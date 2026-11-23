using System.Windows;
using System.Windows.Controls;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>
/// Carga cada vista del módulo Remisiones con los diccionarios de la aplicación: un recurso estático inexistente
/// (p. ej. un color que solo existía en el prototipo) falla aquí y no frente al usuario.
/// </summary>
public class VistasRemisionesTests
{
    private static readonly Type[] Vistas =
    [
        typeof(App.Vistas.Remisiones.RemisionesView),
        typeof(App.Vistas.Remisiones.OficiosView),
        typeof(App.Vistas.Remisiones.RecibosCajaView),
        typeof(App.Vistas.Remisiones.InformesRemisionesView),
        typeof(App.Vistas.Remisiones.ConfiguracionRemisionesView),
        typeof(App.Vistas.Remisiones.ConveniosTabView),
        typeof(App.Vistas.Remisiones.ProductosTabView),
        typeof(App.Vistas.Remisiones.ProgramasTabView),
        typeof(App.Vistas.Remisiones.DatosInstitucionalesView),
        typeof(App.Vistas.Remisiones.RemisionEditorView),
        typeof(App.Vistas.Remisiones.ReciboCajaView),
        typeof(App.Vistas.Remisiones.NuevoOficioView),
        typeof(App.Vistas.Remisiones.ConvenioEditorView),
        typeof(App.Vistas.Remisiones.ClienteEditorView),
        typeof(App.Vistas.Remisiones.ProductoEditorView),
        typeof(App.Vistas.SalidaEditorView),
    ];

    [Fact]
    public void Todas_las_vistas_del_modulo_cargan_sus_recursos()
    {
        var errores = new List<string>();
        var hilo = new Thread(() =>
        {
            var app = Application.Current ?? new Application();
            if (app.Resources.MergedDictionaries.Count == 0)
            {
                foreach (var ruta in new[] { "Estilos/Colores.xaml", "Estilos/Controles.xaml", "Vistas/Plantillas.xaml" })
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/SistemaGranjas;component/{ruta}") });
                }
            }

            foreach (var tipo in Vistas)
            {
                try
                {
                    var vista = (UserControl)Activator.CreateInstance(tipo)!;
                    vista.Measure(new Size(1200, 900));
                }
                catch (Exception ex)
                {
                    errores.Add($"{tipo.Name}: {(ex.InnerException ?? ex).Message}");
                }
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();

        errores.Should().BeEmpty();
    }
}
