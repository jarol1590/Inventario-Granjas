using System.ComponentModel.DataAnnotations;

namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>
/// Base de los ViewModels: estado "ocupado" y ejecución de operaciones con manejo uniforme de errores
/// (reglas de negocio → advertencia con el mensaje; fallas inesperadas → bitácora + mensaje genérico).
/// </summary>
public abstract partial class ViewModelBase(IDialogService dialogos) : ObservableObject
{
    private int _operacionesEnCurso;

    protected IDialogService Dialogos { get; } = dialogos;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoEstaOcupado))]
    private bool _estaOcupado;

    public bool NoEstaOcupado => !EstaOcupado;

    /// <summary>
    /// Ejecuta la acción marcando la vista como ocupada mientras haya al menos una operación en curso.
    /// Devuelve true si terminó sin error.
    /// </summary>
    protected async Task<bool> EjecutarAsync(Func<Task> accion, string operacion = "completar la operación")
    {
        _operacionesEnCurso++;
        EstaOcupado = true;
        try
        {
            await accion();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ValidationException ex)
        {
            Dialogos.Advertir(ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Registro.Error(ex, $"No se pudo {operacion} ({GetType().Name}).");
            Dialogos.MostrarError($"No se pudo {operacion}.\n\n{ex.Message}\n\nEl detalle quedó en: {Registro.RutaActual}");
            return false;
        }
        finally
        {
            if (--_operacionesEnCurso == 0)
            {
                EstaOcupado = false;
            }
        }
    }
}
