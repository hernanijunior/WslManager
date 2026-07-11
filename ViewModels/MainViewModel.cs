using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WslManager.Core;

namespace WslManager.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    internal WslService Wsl { get; } = new();

    private readonly DispatcherTimer _timer;

    public ObservableCollection<DistroViewModel> Distros { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Pronto";

    [ObservableProperty]
    private bool _hasNoDistros;

    public bool IsIdle => !IsBusy;

    public MainViewModel()
    {
        // "--list --running" e leitura de registro não acordam nada: polling seguro.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return; // não empilha refresh sobre ação em andamento
        try
        {
            var distros = await Wsl.ListAsync();

            Distros.Clear();
            foreach (var d in distros)
                Distros.Add(new DistroViewModel(d, this));

            HasNoDistros = Distros.Count == 0;
            var running = distros.Count(d => d.IsRunning);
            StatusText = HasNoDistros
                ? "Nenhuma distro registrada"
                : $"{distros.Count} distro(s) · {running} em execução · atualizado {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText = $"Erro ao listar: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task ShutdownAllAsync() => ExecuteAsync(
        "Desligando a VM do WSL…",
        (wsl, ct) => wsl.ShutdownAsync(ct),
        confirm: "Isso derruba a VM inteira: TODAS as distros, incluindo docker-desktop se estiver em uso.\n\nDesligar tudo?");

    /// <summary>
    /// Caminho único para toda ação: confirma (opcional), trava a UI,
    /// executa, mostra erro do wsl.exe se houver e atualiza a lista.
    /// </summary>
    internal async Task ExecuteAsync(
        string statusWhileRunning,
        Func<WslService, CancellationToken, Task<WslResult>> action,
        string? confirm = null)
    {
        if (confirm is not null &&
            MessageBox.Show(confirm, "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        StatusText = statusWhileRunning;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await action(Wsl, cts.Token);

            if (!result.Ok)
            {
                var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
                MessageBox.Show(
                    $"wsl.exe retornou código {result.ExitCode}.\n\n{detail.Trim()}",
                    "Falha na operação", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show("A operação excedeu 60 segundos e foi cancelada.",
                "Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }
}
