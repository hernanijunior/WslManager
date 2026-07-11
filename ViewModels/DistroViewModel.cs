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

    public Distro Model { get; }

    public string Name => Model.Name;
    public bool IsRunning => Model.IsRunning;
    public bool IsDefault => Model.IsDefault;
    public bool IsSystem => Model.IsSystem;
    public string VhdSizeText => Model.VhdSizeText;
    public string VersionText => $"WSL {Model.Version}";
    public string StateText => IsRunning ? "Em execução" : "Parada";

    public bool CanWake => !IsRunning;
    public bool CanTerminate => IsRunning;
    public bool CanSetDefault => !IsDefault && !IsSystem;
    public bool CanOpenExplorer => IsRunning; // \\wsl.localhost acorda a distro se parada

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
