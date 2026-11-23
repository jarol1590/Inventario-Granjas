using Microsoft.Extensions.DependencyInjection;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.App.ViewModels.Remisiones;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Comercial;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.Composicion;

/// <summary>Abre las ventanas modales del módulo Remisiones. Cada método devuelve true si se guardó algo.</summary>
public interface IFormulariosRemisiones
{
    /// <summary>Nueva remisión de venta de subproductos; con <paramref name="oficioId"/> arranca en ese oficio.</summary>
    Task<bool> NuevaRemisionAsync(int? oficioId = null);

    /// <summary>Abre una remisión de subproductos en modo consulta (desde ahí se edita o anula). Las ventas de animales o lotes se abren en el «Detalle de la salida» de Sistema Granjas.</summary>
    Task<bool> AbrirRemisionAsync(int remisionId);

    /// <summary>Muestra el recibo para capturarlo (o guardarlo si el contexto trae la remisión). Nulo si se cancela.</summary>
    Task<(ReciboCajaDto Recibo, DatosComercialesDto? Comprador)?> CapturarReciboAsync(ReciboContexto contexto);

    /// <summary>Edita el recibo ya emitido de la remisión, o lo genera si aún no lo tiene.</summary>
    Task<bool> EditarReciboAsync(int remisionId);

    Task<bool> NuevoOficioAsync(int? programaId = null);

    Task<bool> EditarOficioAsync(OficioResumen oficio);

    Task<bool> EditarConvenioAsync(Convenio? existente);

    Task<bool> EditarProductoAsync(CatalogoProducto? existente, int? programaId);

    /// <summary>Registra un cliente nuevo («otro comprador») y lo devuelve; nulo si se cancela.</summary>
    Task<Cliente?> NuevoClienteAsync();
}

public sealed class FormulariosRemisiones(IServiceProvider servicios, IDialogService dialogos, IFormularios formularios) : IFormulariosRemisiones
{
    public async Task<bool> NuevaRemisionAsync(int? oficioId = null)
    {
        var vm = servicios.GetRequiredService<RemisionEditorViewModel>();
        return await vm.InicializarNuevaAsync(oficioId) && dialogos.Mostrar(vm);
    }

    public async Task<bool> AbrirRemisionAsync(int remisionId)
    {
        var consultas = servicios.GetRequiredService<IRemisionesQueryService>();
        var vista = await consultas.ObtenerAsync(remisionId);
        if (vista.Clase != "PRODUCTOS")
        {
            // Venta (o traslado) de animales o lotes: se consulta y edita en su pantalla de Sistema Granjas.
            return vista.Remision.TipoMovimiento == Domain.Constants.TipoRemision.Traslado
                ? await formularios.AbrirTrasladoAsync(remisionId)
                : await formularios.AbrirSalidaAsync(remisionId, Domain.Constants.ClaseSalida.Venta);
        }

        var vm = servicios.GetRequiredService<RemisionEditorViewModel>();
        return await vm.InicializarExistenteAsync(remisionId) && dialogos.Mostrar(vm);
    }

    public async Task<(ReciboCajaDto Recibo, DatosComercialesDto? Comprador)?> CapturarReciboAsync(ReciboContexto contexto)
    {
        var vm = ActivatorUtilities.CreateInstance<ReciboCajaViewModel>(servicios, contexto);
        if (!await vm.InicializarAsync() || !dialogos.Mostrar(vm) || vm.Resultado is null)
        {
            return null;
        }

        return (vm.Resultado, vm.ResultadoComprador);
    }

    public async Task<bool> EditarReciboAsync(int remisionId)
    {
        var consultas = servicios.GetRequiredService<IRemisionesQueryService>();
        var vista = await consultas.ObtenerAsync(remisionId);
        var r = vista.Remision;
        var esVenta = r.TipoMovimiento == Domain.Constants.TipoRemision.Venta;
        var recibo = vista.Recibo;
        var consecutivos = recibo is null ? await consultas.SiguientesConsecutivosAsync() : default;
        var contexto = new ReciboContexto(
            esVenta,
            recibo is null ? $"{consecutivos.Recibo} SP" : $"{recibo.NumeroRecibo} {recibo.Prefijo}",
            r.NumeroRemision,
            vista.Programa,
            esVenta ? vista.Comprador : $"Traslado a {vista.GranjaDestino}",
            vista.CompradorNit,
            vista.Lineas,
            vista.Totales.Subtotal,
            vista.Lineas.Sum(l => l.CostoLibros),
            recibo is null ? null : new ReciboCajaDto
            {
                Fecha = recibo.Fecha, FormaPago = recibo.FormaPago, BancoConsignacion = recibo.BancoConsignacion, EsEfectivo = recibo.EsEfectivo,
                Cheque1Num = recibo.Cheque1Num, Cheque2Num = recibo.Cheque2Num, Observaciones = recibo.Observaciones,
                DescuentoPorcentaje = r.DescuentoPorcentaje, TotalPagado = recibo.TotalPagado,
            },
            remisionId);   // una venta de animales o lotes conserva el comprador escrito en Sistema Granjas
        return await CapturarReciboAsync(contexto) is not null;
    }

    public async Task<bool> NuevoOficioAsync(int? programaId = null)
    {
        var vm = servicios.GetRequiredService<NuevoOficioViewModel>();
        return await vm.InicializarAsync(programaId) && dialogos.Mostrar(vm);
    }

    public async Task<bool> EditarOficioAsync(OficioResumen oficio)
    {
        var vm = ActivatorUtilities.CreateInstance<NuevoOficioViewModel>(servicios, oficio);
        return await vm.InicializarAsync() && dialogos.Mostrar(vm);
    }

    public Task<bool> EditarConvenioAsync(Convenio? existente)
        => Task.FromResult(dialogos.Mostrar(ActivatorUtilities.CreateInstance<ConvenioEditorViewModel>(servicios, existente ?? new Convenio())));

    public async Task<bool> EditarProductoAsync(CatalogoProducto? existente, int? programaId)
    {
        var vm = ActivatorUtilities.CreateInstance<ProductoEditorViewModel>(servicios, existente ?? new CatalogoProducto { ProgramaId = programaId ?? 0 });
        return await vm.InicializarAsync() && dialogos.Mostrar(vm);
    }

    public Task<Cliente?> NuevoClienteAsync()
    {
        var vm = ActivatorUtilities.CreateInstance<ClienteEditorViewModel>(servicios);
        return Task.FromResult(dialogos.Mostrar(vm) ? vm.Guardado : null);
    }
}
