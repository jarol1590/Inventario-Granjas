using UCaldas.Granjas.App.Composicion;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Services.Consultas;

namespace UCaldas.Granjas.Tests.Presentacion;

/// <summary>Espera a que se cumpla una condición provocada por una tarea "dispara y olvida" del ViewModel.</summary>
public static class Esperar
{
    public static async Task HastaAsync(Func<bool> condicion, int milisegundos = 4000)
    {
        var limite = Environment.TickCount64 + milisegundos;
        while (!condicion())
        {
            if (Environment.TickCount64 > limite)
            {
                throw new TimeoutException("La condición esperada no se cumplió a tiempo.");
            }

            await Task.Delay(20);
        }
    }

    public static async Task HastaAsync(Func<Task<bool>> condicion, int milisegundos = 4000)
    {
        var limite = Environment.TickCount64 + milisegundos;
        while (!await condicion())
        {
            if (Environment.TickCount64 > limite)
            {
                throw new TimeoutException("La condición esperada no se cumplió a tiempo.");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Ejecuta una operación asincrónica desde un callback sincrónico sin riesgo de bloqueo por el contexto de xUnit.</summary>
    public static void Ahora(Func<Task> operacion) => Task.Run(operacion).GetAwaiter().GetResult();
}

/// <summary>
/// Doble de <see cref="IFormularios"/>: responde al selector de animales con lo que la prueba indique y registra qué
/// ventanas se habrían abierto.
/// </summary>
public sealed class FormulariosFalso : IFormularios
{
    public List<AnimalSeleccionable> Seleccion { get; } = [];

    public bool Cancelar { get; set; }

    public List<(int GranjaId, string? Texto)> Consultas { get; } = [];

    /// <summary>Ventanas abiertas, p. ej. <c>compra:5</c>, <c>salida:7:VENTA</c>, <c>nacimiento</c>.</summary>
    public List<string> Llamadas { get; } = [];

    /// <summary>Lo que devuelven las ventanas (true = "se guardó algo").</summary>
    public bool Resultado { get; set; }

    public Task<IReadOnlyList<AnimalSeleccionable>?> SeleccionarAnimalesAsync(
        int granjaId, IReadOnlyCollection<int> excluidos, string? textoInicial = null)
    {
        Consultas.Add((granjaId, textoInicial));
        IReadOnlyList<AnimalSeleccionable>? resultado = Cancelar ? null : Seleccion.Where(a => !excluidos.Contains(a.Id)).ToList();
        return Task.FromResult(resultado);
    }

    public Task<bool> NuevoNacimientoAsync() => Registrar("nacimiento");

    public Task<bool> VerFichaAsync(int animalId) => Registrar($"ficha:{animalId}");

    public Task<bool> NuevaCompraAsync() => Registrar("compra:nueva");

    public Task<bool> NuevoLoteAsync() => Registrar("lote:nuevo");

    public Task<bool> AbrirCompraAsync(int remisionId) => Registrar($"compra:{remisionId}");

    public Task<bool> AbrirCompraLoteAsync(int loteId) => Registrar($"compralote:{loteId}");

    public Task<bool> NuevaSalidaAsync() => Registrar("salida:nueva");

    public Task<bool> AbrirSalidaAsync(int remisionId, string clase) => Registrar($"salida:{remisionId}:{clase}");

    public Task<bool> NuevoTrasladoAsync() => Registrar("traslado:nuevo");

    public Task<bool> AbrirTrasladoAsync(int remisionId) => Registrar($"traslado:{remisionId}");

    private Task<bool> Registrar(string llamada)
    {
        Llamadas.Add(llamada);
        return Task.FromResult(Resultado);
    }
}

public sealed class NavegadorFalso : INavegador
{
    public List<string> Destinos { get; } = [];

    public int Recargas { get; private set; }

    public int RecargasDeFiltros { get; private set; }

    public void IrA(string clave) => Destinos.Add(clave);

    public Task RecargarTodoAsync()
    {
        Recargas++;
        return Task.CompletedTask;
    }

    public Task RecargarFiltrosAsync()
    {
        RecargasDeFiltros++;
        return Task.CompletedTask;
    }
}
