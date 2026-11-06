namespace UCaldas.Granjas.App.Infraestructura;

/// <summary>ViewModel que se muestra en una ventana modal.</summary>
public interface IDialogViewModel
{
    string Titulo { get; }

    double Ancho { get; }

    /// <summary>0 = la ventana se ajusta al contenido.</summary>
    double Alto { get; }

    /// <summary>Pide cerrar la ventana: true = aceptado, false = cancelado.</summary>
    event Action<bool>? CierreSolicitado;
}

public abstract class DialogViewModelBase : ViewModelBase, IDialogViewModel
{
    protected DialogViewModelBase(IDialogService dialogos)
        : base(dialogos)
    {
        CancelarCommand = new RelayCommand(() => Cerrar(false));
    }

    public abstract string Titulo { get; }

    public virtual double Ancho => 640;

    public virtual double Alto => 0;

    public event Action<bool>? CierreSolicitado;

    public IRelayCommand CancelarCommand { get; }

    protected void Cerrar(bool aceptado) => CierreSolicitado?.Invoke(aceptado);
}
