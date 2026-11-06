namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>Diálogo genérico que pide un texto corto (año de una vigencia, porcentaje de reajuste, motivo...).</summary>
public sealed partial class DialogoEntradaViewModel(IDialogService dialogos, string titulo, string pregunta, string valorInicial)
    : DialogViewModelBase(dialogos)
{
    public override string Titulo { get; } = titulo;

    public override double Ancho => 460;

    public string Pregunta { get; } = pregunta;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AceptarCommand))]
    private string _valor = valorInicial;

    [RelayCommand(CanExecute = nameof(PuedeAceptar))]
    private void Aceptar() => Cerrar(true);

    private bool PuedeAceptar() => !string.IsNullOrWhiteSpace(Valor);
}
