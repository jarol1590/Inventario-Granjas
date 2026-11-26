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
/// Ventana del Recibo de Caja SP: consecutivo automático, comprador y suma en letras, forma de pago (consignación, transferencia o
/// recursos propios; traslado interno en los traslados), descuento y total. Sirve para capturar el recibo antes de guardar la remisión
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
        // Un recibo anterior de efectivo / cheque ya no es una opción: se propone consignación hasta que se elija otra.
        _formaPagoElegida = existente?.FormaPago is FormaPago.Transferencia or FormaPago.RecursosPropios ? existente.FormaPago : FormaPago.Consignacion;
        _banco = existente?.BancoConsignacion ?? string.Empty;
        _numeroCuenta = existente?.NumeroCuenta ?? string.Empty;
        _tipoCuenta = existente?.TipoCuenta ?? string.Empty;
        _descuento = existente?.DescuentoPorcentaje ?? 0m;
        _observaciones = existente?.Observaciones ?? string.Empty;
        _totalTraslado = existente is { FormaPago: FormaPago.TrasladoInterno } ? existente.TotalPagado : contexto.ValorSugeridoDelTraslado;
    }

    public ReciboContexto Contexto { get; }

    public override string Titulo => $"Recibo de Caja — {Contexto.NumeroRecibo}";

    public override double Ancho => 1000;

    /// <summary>Lo que se capturó; nulo si se canceló.</summary>
    public ReciboCajaDto? Resultado { get; private set; }

    /// <summary>Comprador elegido (solo cuando <see cref="ReciboContexto.PideComprador"/>).</summary>
    public DatosComercialesDto? ResultadoComprador { get; private set; }

    public bool EsVenta => Contexto.EsVenta;

    public bool EsTraslado => !Contexto.EsVenta;

    public IReadOnlyList<LineaRemision> Lineas => Contexto.Lineas;

    public IReadOnlyList<string> TiposCuenta { get; } = ["Ahorros", "Corriente"];

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

    /// <summary>Forma de pago elegida de una venta (<see cref="FormaPago"/>): consignación, transferencia o recursos propios.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsConsignacion), nameof(EsTransferencia), nameof(EsRecursosPropios), nameof(MostrarBanco))]
    private string _formaPagoElegida = FormaPago.Consignacion;

    public bool EsConsignacion
    {
        get => FormaPagoElegida == FormaPago.Consignacion;
        set => Elegir(value, FormaPago.Consignacion);
    }

    public bool EsTransferencia
    {
        get => FormaPagoElegida == FormaPago.Transferencia;
        set => Elegir(value, FormaPago.Transferencia);
    }

    public bool EsRecursosPropios
    {
        get => FormaPagoElegida == FormaPago.RecursosPropios;
        set => Elegir(value, FormaPago.RecursosPropios);
    }

    private void Elegir(bool marcada, string forma)
    {
        if (marcada)
        {
            FormaPagoElegida = forma;
        }
    }

    public bool MostrarBanco => EsVenta && (EsConsignacion || EsTransferencia);

    [ObservableProperty]
    private string _banco;

    [ObservableProperty]
    private string _numeroCuenta;

    [ObservableProperty]
    private string _tipoCuenta;

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
            // La cuenta registrada en Datos institucionales se propone; los tres datos se pueden cambiar si el pago fue a otra cuenta.
            if (string.IsNullOrWhiteSpace(Banco))
            {
                Banco = config.EntidadBancaria;
            }

            if (string.IsNullOrWhiteSpace(NumeroCuenta))
            {
                NumeroCuenta = config.NumeroCuenta;
            }

            if (string.IsNullOrWhiteSpace(TipoCuenta))
            {
                TipoCuenta = config.TipoCuenta.Contains("corr", StringComparison.OrdinalIgnoreCase) ? "Corriente" : "Ahorros";
            }

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
            FormaPago = EsTraslado ? FormaPago.TrasladoInterno : FormaPagoElegida,
            BancoConsignacion = Banco,
            NumeroCuenta = NumeroCuenta,
            TipoCuenta = TipoCuenta,
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
