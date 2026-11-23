using System.Collections.ObjectModel;
using UCaldas.Granjas.App.Infraestructura;
using UCaldas.Granjas.Domain;
using UCaldas.Granjas.Domain.Abstractions;
using UCaldas.Granjas.Domain.Constants;
using UCaldas.Granjas.Domain.Entities;
using UCaldas.Granjas.Services.Comercial;
using UCaldas.Granjas.Services.Kardex;

namespace UCaldas.Granjas.App.ViewModels.Remisiones;

/// <summary>Lo que el recibo de caja necesita saber de la remisión a la que pertenece.</summary>
/// <param name="NumeroRecibo">«829 SP»: el consecutivo ya asignado, o el que se asignará al guardar.</param>
/// <param name="Existente">Datos del recibo ya emitido (al editar); nulo en un recibo nuevo.</param>
/// <param name="RemisionIdParaGuardar">Si se informa, al aceptar el recibo se guarda de inmediato (editar un recibo, generar el de una remisión anterior); si es nulo solo se captura.</param>
/// <param name="PideComprador">Remisión de venta anterior al módulo: el recibo también registra el comprador.</param>
public sealed record ReciboContexto(
    bool EsVenta,
    string NumeroRecibo,
    string NumeroRemision,
    string Programa,
    string Comprador,
    string? Nit,
    IReadOnlyList<LineaRemision> Lineas,
    decimal Subtotal,
    decimal ValorSugeridoDelTraslado,
    ReciboCajaDto? Existente = null,
    int? RemisionIdParaGuardar = null,
    bool PideComprador = false);

/// <summary>
/// Ventana del Recibo de Caja SP: consecutivo automático, comprador y suma en letras, forma de pago (consignación o cheque /
/// efectivo; traslado interno en los traslados), descuento y total. Sirve para capturar el recibo antes de guardar la remisión
/// y para editarlo o generarlo después.
/// </summary>
public sealed partial class ReciboCajaViewModel : DialogViewModelBase
{
    private readonly IReciboCajaService _recibos;
    private readonly ICatalogoComercialService _comercial;
    private readonly IReloj _reloj;

    public ReciboCajaViewModel(IReciboCajaService recibos, ICatalogoComercialService comercial, IReloj reloj, IDialogService dialogos, ReciboContexto contexto)
        : base(dialogos)
    {
        _recibos = recibos;
        _comercial = comercial;
        _reloj = reloj;
        Contexto = contexto;
        var existente = contexto.Existente;
        _fecha = existente?.Fecha ?? reloj.Hoy;
        _esConsignacion = existente is null || existente.FormaPago == FormaPago.Consignacion;
        _esEfectivo = existente?.EsEfectivo ?? false;
        _banco = existente?.BancoConsignacion ?? string.Empty;
        _cheque1 = existente?.Cheque1Num ?? string.Empty;
        _cheque2 = existente?.Cheque2Num ?? string.Empty;
        _descuento = existente?.DescuentoPorcentaje ?? 0m;
        _observaciones = existente?.Observaciones ?? string.Empty;
        _totalTraslado = existente is { FormaPago: FormaPago.TrasladoInterno } ? existente.TotalPagado : contexto.ValorSugeridoDelTraslado;
    }

    public ReciboContexto Contexto { get; }

    public override string Titulo => $"Recibo de Caja — {Contexto.NumeroRecibo}";

    public override double Ancho => 860;

    /// <summary>Lo que se capturó; nulo si se canceló.</summary>
    public ReciboCajaDto? Resultado { get; private set; }

    /// <summary>Comprador elegido (solo cuando <see cref="ReciboContexto.PideComprador"/>).</summary>
    public DatosComercialesDto? ResultadoComprador { get; private set; }

    public bool EsVenta => Contexto.EsVenta;

    public bool EsTraslado => !Contexto.EsVenta;

    public IReadOnlyList<LineaRemision> Lineas => Contexto.Lineas;

    public ObservableCollection<string> Bancos { get; } = [];

    public ObservableCollection<Convenio> Convenios { get; } = [];

    public ObservableCollection<Cliente> Clientes { get; } = [];

    [ObservableProperty]
    private Convenio? _convenio;

    [ObservableProperty]
    private Cliente? _cliente;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsOtro))]
    private bool _esConvenio = true;

    public bool EsOtro
    {
        get => !EsConvenio;
        set => EsConvenio = !value;
    }

    public bool PideComprador => Contexto.PideComprador;

    [ObservableProperty]
    private DateTime? _fecha;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEfectivoCheque), nameof(MostrarBanco))]
    private bool _esConsignacion;

    public bool EsEfectivoCheque
    {
        get => !EsConsignacion;
        set => EsConsignacion = !value;
    }

    public bool MostrarBanco => EsVenta && EsConsignacion;

    public bool MostrarCheques => EsVenta && !EsConsignacion;

    [ObservableProperty]
    private string _banco;

    [ObservableProperty]
    private bool _esEfectivo;

    [ObservableProperty]
    private string _cheque1;

    [ObservableProperty]
    private string _cheque2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValorDescuento), nameof(Total), nameof(TotalTexto), nameof(DescuentoTexto), nameof(TotalLetras))]
    private decimal _descuento;

    public string DescuentoTexto => Formato.Dinero(-ValorDescuento);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Total), nameof(TotalTexto), nameof(TotalLetras))]
    private decimal _totalTraslado;

    [ObservableProperty]
    private string _observaciones;

    public string SubtotalTexto => Formato.Dinero(Contexto.Subtotal);

    public decimal ValorDescuento => EsVenta ? Math.Round(Contexto.Subtotal * Math.Clamp(Descuento, 0m, 100m) / 100m, 0, MidpointRounding.AwayFromZero) : 0m;

    public decimal Total => EsVenta ? Contexto.Subtotal - ValorDescuento : TotalTraslado;

    public string TotalTexto => Formato.Dinero(Total);

    public string TotalLetras => NumeroALetras.Pesos(Total);

    public async Task<bool> InicializarAsync()
    {
        return await EjecutarAsync(async () =>
        {
            var config = await _comercial.GetConfiguracionAsync();
            Bancos.Clear();
            var principal = $"{config.EntidadBancaria} — CTA {config.TipoCuenta} {config.NumeroCuenta}";
            Bancos.Add(principal);
            foreach (var otro in new[] { "BANCOLOMBIA", "BANCO DE BOGOTÁ", "BANCO AGRARIO" })
            {
                Bancos.Add(otro);
            }

            if (!string.IsNullOrWhiteSpace(Banco) && !Bancos.Contains(Banco))
            {
                Bancos.Add(Banco);
            }

            Banco = string.IsNullOrWhiteSpace(Banco) ? principal : Banco;
            if (PideComprador)
            {
                foreach (var c in await _comercial.ListarConveniosAsync(soloActivos: true))
                {
                    Convenios.Add(c);
                }

                foreach (var c in await _comercial.ListarClientesAsync(soloActivos: true))
                {
                    Clientes.Add(c);
                }

                Convenio = Convenios.FirstOrDefault();
                Cliente = Clientes.FirstOrDefault();
            }
        }, "preparar el recibo de caja");
    }

    [RelayCommand]
    private async Task AceptarAsync()
    {
        var dto = new ReciboCajaDto
        {
            Fecha = Fecha ?? default,
            FormaPago = EsTraslado ? FormaPago.TrasladoInterno : EsConsignacion ? FormaPago.Consignacion : FormaPago.EfectivoCheque,
            BancoConsignacion = Banco,
            EsEfectivo = EsEfectivo,
            Cheque1Num = Cheque1,
            Cheque2Num = Cheque2,
            Observaciones = Observaciones,
            DescuentoPorcentaje = EsVenta ? Descuento : 0m,
            TotalPagado = EsTraslado ? TotalTraslado : 0m,
        };
        DatosComercialesDto? comprador = null;
        if (PideComprador)
        {
            comprador = EsConvenio
                ? new DatosComercialesDto { TipoComprador = TipoComprador.Convenio, ConvenioId = Convenio?.Id }
                : new DatosComercialesDto { TipoComprador = TipoComprador.Otro, ClienteId = Cliente?.Id };
        }

        if (Contexto.RemisionIdParaGuardar is { } remisionId)
        {
            if (!await EjecutarAsync(() => _recibos.GuardarAsync(remisionId, dto, comprador), "guardar el recibo de caja"))
            {
                return;
            }
        }
        else if (Fecha is null || Fecha.Value.Date > _reloj.Hoy)
        {
            Dialogos.Advertir("La fecha del recibo es obligatoria y no puede ser futura.");
            return;
        }

        Resultado = dto;
        ResultadoComprador = comprador;
        Cerrar(true);
    }
}
