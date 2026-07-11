using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WslManager.Core;

namespace WslManager.ViewModels;

/// <summary>
/// Embrulha um <see cref="Distro"/> para binding e expõe os comandos por linha.
/// As ações delegam ao MainViewModel, que executa e dispara o refresh.
/// </summary>
public sealed partial class DistroViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public DistroViewModel(Distro distro, MainViewModel owner)
    {
        _owner = owner;
        Model = distro;
    }

    public Distro Model { get; private set; }

    /// <summary>
    /// Atualiza o snapshot no lugar (sem recriar o VM) para que o refresh de 5s
    /// não pisque a lista e a página de detalhe permaneça viva.
    /// </summary>
    public void Apply(Distro distro)
    {
        Model = distro;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(IsSystem));
        OnPropertyChanged(nameof(VhdSizeText));
        OnPropertyChanged(nameof(VhdBytes));
        OnPropertyChanged(nameof(DiskAlert));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(BasePath));
        OnPropertyChanged(nameof(Guid));
        OnPropertyChanged(nameof(VhdxPath));
        OnPropertyChanged(nameof(CanWake));
        OnPropertyChanged(nameof(CanTerminate));
        OnPropertyChanged(nameof(CanSetDefault));
        OnPropertyChanged(nameof(CanOpenExplorer));
        WakeCommand.NotifyCanExecuteChanged();
        TerminateCommand.NotifyCanExecuteChanged();
        SetDefaultCommand.NotifyCanExecuteChanged();
        OpenExplorerCommand.NotifyCanExecuteChanged();
    }

    public string Name => Model.Name;
    public bool IsRunning => Model.IsRunning;
    public bool IsDefault => Model.IsDefault;
    public bool IsSystem => Model.IsSystem;
    public string VhdSizeText => Model.VhdSizeText;
    public long VhdBytes => Model.VhdBytes;
    public string VersionText => $"WSL {Model.Version}";
    public string StateText => IsRunning ? "Em execução" : "Parada";

    // Metadados da página de detalhe
    public string BasePath => Model.BasePath;
    public string Guid => Model.Guid;
    public string VhdxPath => Model.VhdxPath;

    public bool CanWake => !IsRunning;
    public bool CanTerminate => IsRunning;
    public bool CanSetDefault => !IsDefault && !IsSystem;
    public bool CanOpenExplorer => IsRunning; // \\wsl.localhost acorda a distro se parada

    /// <summary>Disco acima do limiar configurado (alerta visual).</summary>
    public bool DiskAlert => _owner.IsDiskAlert(Model.VhdBytes);

    /// <summary>Chamado quando o limiar muda, para reavaliar <see cref="DiskAlert"/>.</summary>
    public void RaiseDiskAlert() => OnPropertyChanged(nameof(DiskAlert));

    /// <summary>Distros de sistema nunca podem ser apagadas por aqui.</summary>
    public bool CanDelete => !IsSystem;

    [RelayCommand]
    private void OpenDetail() => _owner.RequestDetail(this);

    [RelayCommand]
    private Task ReclaimSpaceAsync() => _owner.ReclaimSpaceAsync(this);

    [RelayCommand]
    private Task ExportAsync() => _owner.ExportDistroAsync(this);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => _owner.DeleteDistroAsync(this);

    [RelayCommand(CanExecute = nameof(CanWake))]
    private Task WakeAsync() => _owner.ExecuteAsync(
        $"Iniciando {Name}…",
        (wsl, ct) => wsl.WakeAsync(Name, ct));

    [RelayCommand(CanExecute = nameof(CanTerminate))]
    private Task TerminateAsync() => _owner.ExecuteAsync(
        $"Encerrando {Name}…",
        (wsl, ct) => wsl.TerminateAsync(Name, ct),
        confirm: IsSystem
            ? $"\"{Name}\" é uma distro de sistema (Docker/Rancher). Encerrá-la pode quebrar a ferramenta dona dela.\n\nEncerrar mesmo assim?"
            : null);

    [RelayCommand(CanExecute = nameof(CanSetDefault))]
    private Task SetDefaultAsync() => _owner.ExecuteAsync(
        $"Definindo {Name} como padrão…",
        (wsl, ct) => wsl.SetDefaultAsync(Name, ct));

    [RelayCommand]
    private void OpenTerminal() => _owner.Wsl.OpenTerminal(Name);

    [RelayCommand(CanExecute = nameof(CanOpenExplorer))]
    private void OpenExplorer() => _owner.Wsl.OpenExplorer(Name);
}
