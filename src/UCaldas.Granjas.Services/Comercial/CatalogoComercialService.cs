using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Entities;

namespace UCaldas.Granjas.Services.Comercial;

/// <summary>
/// Catálogos del módulo Remisiones: convenios institucionales, clientes («Otro comprador»), productos por programa y datos
/// institucionales. Nada se borra: se inactiva, porque las remisiones ya emitidas los referencian.
/// </summary>
public interface ICatalogoComercialService
{
    Task<IReadOnlyList<Convenio>> ListarConveniosAsync(bool soloActivos = false, CancellationToken ct = default);

    /// <summary>Crea (Id = 0) o actualiza un convenio. El NIT no se puede repetir.</summary>
    Task<Convenio> GuardarConvenioAsync(Convenio convenio, CancellationToken ct = default);

    Task CambiarEstadoConvenioAsync(int convenioId, bool activo, CancellationToken ct = default);

    Task<IReadOnlyList<Cliente>> ListarClientesAsync(bool soloActivos = false, CancellationToken ct = default);

    /// <summary>Crea (Id = 0) o actualiza un cliente. El NIT o cédula no se puede repetir.</summary>
    Task<Cliente> GuardarClienteAsync(Cliente cliente, CancellationToken ct = default);

    Task CambiarEstadoClienteAsync(int clienteId, bool activo, CancellationToken ct = default);

    /// <summary>Productos; con <paramref name="programaId"/> solo los de ese programa.</summary>
    Task<IReadOnlyList<CatalogoProducto>> ListarProductosAsync(int? programaId = null, bool soloActivos = false, CancellationToken ct = default);

    /// <summary>Crea (Id = 0) o actualiza un producto. El código no se puede repetir.</summary>
    Task<CatalogoProducto> GuardarProductoAsync(CatalogoProducto producto, CancellationToken ct = default);

    Task CambiarEstadoProductoAsync(int productoId, bool activo, CancellationToken ct = default);

    Task<ConfiguracionRemisiones> GetConfiguracionAsync(CancellationToken ct = default);

    /// <summary>Guarda los datos institucionales. El consecutivo de recibos solo se puede mover hacia adelante de lo ya emitido.</summary>
    Task GuardarConfiguracionAsync(ConfiguracionRemisiones configuracion, CancellationToken ct = default);
}

public sealed class CatalogoComercialService(IUnitOfWorkFactory fabrica) : ICatalogoComercialService
{
    // ---- Convenios ----------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<Convenio>> ListarConveniosAsync(bool soloActivos = false, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return (await uow.Convenios.GetAllAsync(ct)).Where(c => !soloActivos || c.Activo).OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<Convenio> GuardarConvenioAsync(Convenio convenio, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(convenio);
        var nombre = (convenio.Nombre ?? string.Empty).Trim();
        var nit = (convenio.Nit ?? string.Empty).Trim();
        Regla.ExigirTexto(nombre, "El nombre del convenio");
        Regla.ExigirTexto(nit, "El NIT del convenio");
        Regla.ExigirTexto(convenio.Direccion, "La dirección del convenio");
        Regla.ExigirTexto(convenio.Telefono, "El teléfono del convenio");
        Regla.ExigirTelefono(convenio.Telefono, "El teléfono del convenio");
        var correo = Limpio(convenio.CorreoElectronico);
        Regla.Exigir(correo is null || EsCorreo(correo), "El correo electrónico no es válido.");

        await using var uow = await fabrica.BeginAsync(ct);
        var otro = await uow.Convenios.GetByNitAsync(nit, ct);
        Regla.Exigir(otro is null || otro.Id == convenio.Id, $"Ya existe un convenio con el NIT {nit}.");

        var guardado = new Convenio
        {
            Id = convenio.Id, Nombre = nombre, Nit = nit, Direccion = convenio.Direccion.Trim(), Telefono = convenio.Telefono.Trim(),
            CorreoElectronico = correo, Activo = convenio.Activo,
        };
        if (guardado.Id == 0)
        {
            await uow.Convenios.InsertAsync(guardado, ct);
        }
        else
        {
            Regla.Exigir(await uow.Convenios.GetByIdAsync(guardado.Id, ct) is not null, "El convenio no existe.");
            await uow.Convenios.UpdateAsync(guardado, ct);
        }

        await uow.CommitAsync(ct);
        return guardado;
    }

    public async Task CambiarEstadoConvenioAsync(int convenioId, bool activo, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var convenio = await uow.Convenios.GetByIdAsync(convenioId, ct) ?? throw Regla.Violada("El convenio no existe.");
        convenio.Activo = activo;
        await uow.Convenios.UpdateAsync(convenio, ct);
        await uow.CommitAsync(ct);
    }

    // ---- Clientes -----------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<Cliente>> ListarClientesAsync(bool soloActivos = false, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return (await uow.Clientes.GetAllAsync(ct)).Where(c => !soloActivos || c.Activo).OrderBy(c => c.NombreRazonSocial, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<Cliente> GuardarClienteAsync(Cliente cliente, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        var nombre = (cliente.NombreRazonSocial ?? string.Empty).Trim();
        var nit = (cliente.NitCc ?? string.Empty).Trim();
        Regla.ExigirTexto(nombre, "El nombre o razón social del cliente");
        Regla.ExigirTexto(nit, "El NIT o cédula del cliente");
        Regla.ExigirTelefono(cliente.Telefono, "El teléfono del cliente");
        var correo = Limpio(cliente.CorreoElectronico);
        Regla.Exigir(correo is null || EsCorreo(correo), "El correo electrónico no es válido.");

        await using var uow = await fabrica.BeginAsync(ct);
        var otro = await uow.Clientes.GetByNitAsync(nit, ct);
        Regla.Exigir(otro is null || otro.Id == cliente.Id, $"Ya existe un cliente con el NIT o cédula {nit}.");

        var guardado = new Cliente
        {
            Id = cliente.Id, NombreRazonSocial = nombre, NitCc = nit, Direccion = Limpio(cliente.Direccion), Telefono = Limpio(cliente.Telefono),
            CorreoElectronico = correo, Activo = cliente.Activo,
        };
        if (guardado.Id == 0)
        {
            await uow.Clientes.InsertAsync(guardado, ct);
        }
        else
        {
            Regla.Exigir(await uow.Clientes.GetByIdAsync(guardado.Id, ct) is not null, "El cliente no existe.");
            await uow.Clientes.UpdateAsync(guardado, ct);
        }

        await uow.CommitAsync(ct);
        return guardado;
    }

    public async Task CambiarEstadoClienteAsync(int clienteId, bool activo, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var cliente = await uow.Clientes.GetByIdAsync(clienteId, ct) ?? throw Regla.Violada("El cliente no existe.");
        cliente.Activo = activo;
        await uow.Clientes.UpdateAsync(cliente, ct);
        await uow.CommitAsync(ct);
    }

    // ---- Productos ----------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<CatalogoProducto>> ListarProductosAsync(int? programaId = null, bool soloActivos = false, CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return (await uow.Productos.GetAllAsync(ct))
            .Where(p => (programaId is null || p.ProgramaId == programaId) && (!soloActivos || p.Activo))
            .OrderBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<CatalogoProducto> GuardarProductoAsync(CatalogoProducto producto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(producto);
        var codigo = (producto.Codigo ?? string.Empty).Trim();
        var nombre = (producto.Nombre ?? string.Empty).Trim();
        var unidad = (producto.UnidadMedida ?? string.Empty).Trim();
        Regla.ExigirTexto(codigo, "El código del producto");
        Regla.ExigirTexto(nombre, "El nombre del producto");
        Regla.ExigirTexto(unidad, "La unidad de medida");
        Regla.Exigir(producto.PrecioUnitario >= 0, "El precio sugerido no puede ser negativo.");

        await using var uow = await fabrica.BeginAsync(ct);
        Regla.Exigir(await uow.Programas.GetByIdAsync(producto.ProgramaId, ct) is not null, "El programa del producto no existe.");
        var otro = await uow.Productos.GetByCodigoAsync(codigo, ct);
        Regla.Exigir(otro is null || otro.Id == producto.Id, $"Ya existe un producto con el código {codigo}.");

        var guardado = new CatalogoProducto
        {
            Id = producto.Id, Codigo = codigo, ProgramaId = producto.ProgramaId, Nombre = nombre, UnidadMedida = unidad,
            PrecioUnitario = producto.PrecioUnitario, Activo = producto.Activo,
        };
        if (guardado.Id == 0)
        {
            await uow.Productos.InsertAsync(guardado, ct);
        }
        else
        {
            Regla.Exigir(await uow.Productos.GetByIdAsync(guardado.Id, ct) is not null, "El producto no existe.");
            await uow.Productos.UpdateAsync(guardado, ct);
        }

        await uow.CommitAsync(ct);
        return guardado;
    }

    public async Task CambiarEstadoProductoAsync(int productoId, bool activo, CancellationToken ct = default)
    {
        await using var uow = await fabrica.BeginAsync(ct);
        var producto = await uow.Productos.GetByIdAsync(productoId, ct) ?? throw Regla.Violada("El producto no existe.");
        producto.Activo = activo;
        await uow.Productos.UpdateAsync(producto, ct);
        await uow.CommitAsync(ct);
    }

    // ---- Datos institucionales ----------------------------------------------------------------------------------------

    public async Task<ConfiguracionRemisiones> GetConfiguracionAsync(CancellationToken ct = default)
    {
        await using var uow = await fabrica.OpenReadOnlyAsync(ct);
        return await uow.ConfiguracionRemisiones.GetAsync(ct);
    }

    public async Task GuardarConfiguracionAsync(ConfiguracionRemisiones configuracion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        Regla.ExigirTexto(configuracion.EntidadBancaria, "La entidad bancaria");
        Regla.ExigirTexto(configuracion.TipoCuenta, "El tipo de cuenta");
        Regla.ExigirTexto(configuracion.NumeroCuenta, "El número de cuenta");
        Regla.ExigirTelefonos(configuracion.Telefonos, "Los teléfonos");
        Regla.Exigir(configuracion.UltimoConsecutivoRecibo >= 0, "El consecutivo de recibos no puede ser negativo.");

        await using var uow = await fabrica.BeginAsync(ct);
        var emitido = await uow.Recibos.GetMaxNumeroAsync(ct);
        Regla.Exigir(
            configuracion.UltimoConsecutivoRecibo >= emitido,
            $"El último consecutivo no puede ser menor que el recibo {emitido} SP, que ya se emitió.");

        await uow.ConfiguracionRemisiones.GuardarAsync(new ConfiguracionRemisiones
        {
            NombreDirector = (configuracion.NombreDirector ?? string.Empty).Trim(),
            CargoDirector = (configuracion.CargoDirector ?? string.Empty).Trim(),
            TituloDirector = Limpio(configuracion.TituloDirector),
            JefeTesoreria = (configuracion.JefeTesoreria ?? string.Empty).Trim(),
            FacultadDepto = (configuracion.FacultadDepto ?? string.Empty).Trim(),
            Telefonos = Limpio(configuracion.Telefonos),
            Extensiones = Limpio(configuracion.Extensiones),
            EntidadBancaria = configuracion.EntidadBancaria.Trim(),
            TipoCuenta = configuracion.TipoCuenta.Trim(),
            NumeroCuenta = configuracion.NumeroCuenta.Trim(),
            UltimoConsecutivoRecibo = configuracion.UltimoConsecutivoRecibo,
        }, ct);
        await uow.CommitAsync(ct);
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static bool EsCorreo(string valor)
    {
        var arroba = valor.IndexOf('@');
        return arroba > 0 && arroba == valor.LastIndexOf('@') && valor.IndexOf('.', arroba) > arroba + 1 && !valor.Contains(' ');
    }
}
