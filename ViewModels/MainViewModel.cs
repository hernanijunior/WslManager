using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WslManager.Core;
using WslManager.Views;

namespace WslManager.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    internal WslService Wsl { get; } = new();
    internal LongOperationManager LongOps { get; } = new();

    private readonly AppSettingsService _settings = new();
    private readonly DispatcherTimer _timer;

    /// <summary>Uma distro está acima do limiar de alerta de disco?</summary>
    internal bool IsDiskAlert(long vhdBytes) => vhdBytes > _settings.Current.DiskAlertThresholdBytes;

    /// <summary>Limiar de alerta de disco, em GB (editável na página Configuração).</summary>
    public double DiskAlertThresholdGb
    {
        get => _settings.Current.DiskAlertThresholdBytes / (1024.0 * 1024 * 1024);
        set
        {
            var bytes = (long)Math.Round(Math.Max(1, value) * 1024 * 1024 * 1024);
            if (bytes == _settings.Current.DiskAlertThresholdBytes) return;
            _settings.Current.DiskAlertThresholdBytes = bytes;
            _settings.Save();
            OnPropertyChanged();
            foreach (var d in Distros) d.RaiseDiskAlert();
        }
    }

    public ObservableCollection<DistroViewModel> Distros { get; } = [];

    /// <summary>Ligado pela shell (MainWindow) para navegar até a página de detalhe.</summary>
    internal Action<DistroViewModel>? NavigateToDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Pronto";

    [ObservableProperty]
    private bool _hasNoDistros;

    public bool IsIdle => !IsBusy;

    /// <summary>Editor do .wslconfig (página Configuração).</summary>
    public WslConfigViewModel Config { get; }

    public MainViewModel()
    {
        Config = new WslConfigViewModel(
            new WslConfigService(),
            SystemInfo.TotalPhysicalMemoryGb(),
            SystemInfo.ProcessorCount,
            SystemInfo.MirroredNetworkingSupported,
            RestartWslAsync);

        // "--list --running" e leitura de registro não acordam nada: polling seguro.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    internal void RequestDetail(DistroViewModel distro) => NavigateToDetail?.Invoke(distro);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return; // não empilha refresh sobre ação em andamento
        try
        {
            var distros = await Wsl.ListAsync();
            Reconcile(distros);

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

    /// <summary>
    /// Atualiza a coleção no lugar quando o layout (nomes/ordem) não mudou — evita
    /// piscar a lista a cada 5s e mantém viva a página de detalhe (mesma instância
    /// de <see cref="DistroViewModel"/>). Só reconstrói quando a ordem muda.
    /// </summary>
    private void Reconcile(IReadOnlyList<Distro> distros)
    {
        var sameLayout = Distros.Count == distros.Count &&
            Distros.Zip(distros).All(p => p.First.Name.Equals(p.Second.Name, StringComparison.OrdinalIgnoreCase));

        if (sameLayout)
        {
            for (var i = 0; i < distros.Count; i++)
                Distros[i].Apply(distros[i]);
        }
        else
        {
            Distros.Clear();
            foreach (var d in distros)
                Distros.Add(new DistroViewModel(d, this));
        }
    }

    [RelayCommand]
    private Task ShutdownAllAsync() => ExecuteAsync(
        "Desligando a VM do WSL…",
        (wsl, ct) => wsl.ShutdownAsync(ct),
        confirm: "Isso derruba a VM inteira: TODAS as distros, incluindo docker-desktop se estiver em uso.\n\nDesligar tudo?");

    /// <summary>Usado por "Salvar e reiniciar" na página Configuração.</summary>
    internal Task RestartWslAsync() => ExecuteAsync(
        "Reiniciando o WSL…",
        (wsl, ct) => wsl.ShutdownAsync(ct),
        confirm: "Reiniciar o WSL encerra a VM inteira (todas as distros, inclusive docker-desktop).\n\nContinuar?");

    [RelayCommand]
    private async Task NewDistroAsync()
    {
        var dialogVm = new NewDistroViewModel(
            Wsl,
            Distros.Select(d => d.Name),
            Distros.Where(d => !d.IsSystem).Select(d => d.Name),
            LongOps);

        var dialog = new NewDistroDialog(dialogVm) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || dialogVm.Result is not { } req) return;

        var ok = await RunLongOperationAsync(req.Title, req.LockKey, req.Action);

        if (ok && req.SetupUserForDistro is { } distro)
            await OfferUserSetupAsync(distro);
    }

    /// <summary>
    /// "Recuperar espaço" (detalhe da distro): se rodando, oferece encerrar;
    /// marca o vhdx como esparso e mostra o tamanho em disco antes/depois.
    /// </summary>
    internal async Task ReclaimSpaceAsync(DistroViewModel distro)
    {
        var wasRunning = distro.IsRunning;
        if (wasRunning &&
            MessageBox.Show(
                $"Para recuperar espaço, \"{distro.Name}\" precisa ser encerrada primeiro.\n\nEncerrar agora e continuar?",
                "Recuperar espaço", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var vhdx = distro.VhdxPath;
        var before = DiskUtil.GetSizeOnDisk(vhdx);

        var ok = await RunLongOperationAsync(
            $"Recuperando espaço em {distro.Name}…",
            distro.Name,
            async (wsl, progress, ct) =>
            {
                if (wasRunning)
                {
                    progress.Report(LongOpUpdate.WithStatus($"Encerrando {distro.Name}…"));
                    await wsl.TerminateAsync(distro.Name, ct); // resultado ignorado
                }
                progress.Report(LongOpUpdate.WithStatus("Compactando o disco (set-sparse)…"));
                return await wsl.SetSparseAsync(distro.Name, ct);
            });

        if (!ok) return;

        var after = DiskUtil.GetSizeOnDisk(vhdx);
        if (before < 0 || after < 0)
        {
            MessageBox.Show("Compactação concluída.", "Recuperar espaço",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var freed = Math.Max(0, before - after);
        MessageBox.Show(
            $"Tamanho em disco antes:  {ByteSize.Humanize(before)}\n" +
            $"Tamanho em disco depois: {ByteSize.Humanize(after)}\n\n" +
            $"Espaço recuperado: {ByteSize.Humanize(freed)}",
            "Recuperar espaço", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Exporta a distro para um .tar (SaveFileDialog + operação longa com
    /// progresso por tamanho do arquivo). Retorna true se exportou com sucesso.
    /// </summary>
    internal async Task<bool> ExportDistroAsync(DistroViewModel distro)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar distro",
            FileName = $"{distro.Name}_{DateTime.Now:yyyy-MM-dd}.tar",
            Filter = "Tarball (*.tar)|*.tar|Todos os arquivos (*.*)|*.*",
            DefaultExt = ".tar",
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog() != true) return false;

        var path = dlg.FileName;
        return await RunLongOperationAsync(
            $"Exportando {distro.Name}…",
            distro.Name,
            (wsl, progress, ct) =>
            {
                progress.Report(LongOpUpdate.Watch($"Exportando {distro.Name}…", path));
                return wsl.ExportAsync(distro.Name, path, ct);
            });
    }

    /// <summary>
    /// Apagar (unregister) com o fluxo blindado do CLAUDE.md: nunca para distros
    /// de sistema; mostra o que será perdido; oferece exportar antes; exige
    /// digitar o nome exato para habilitar. Irreversível.
    /// </summary>
    internal async Task DeleteDistroAsync(DistroViewModel distro)
    {
        if (distro.IsSystem) return; // trava dura: nunca apagar distro de sistema

        var dialog = new DeleteDistroDialog(distro.Name, distro.VhdxPath, distro.VhdSizeText)
        {
            Owner = Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true) return;

        // Exportar antes (se marcado): se o usuário cancelar/falhar, aborta o apagar.
        if (dialog.ExportFirst && !await ExportDistroAsync(distro))
            return;

        await RunLongOperationAsync(
            $"Apagando {distro.Name}…",
            distro.Name,
            (wsl, _, ct) => wsl.UnregisterAsync(distro.Name, ct));
    }

    /// <summary>Pós-import: oferece criar o usuário padrão da distro.</summary>
    private async Task OfferUserSetupAsync(string distro)
    {
        var dlg = new SetupUserDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.UserName)) return;

        await ExecuteAsync(
            $"Configurando usuário em {distro}…",
            async (wsl, ct) =>
            {
                var r = await wsl.SetupDefaultUserAsync(distro, dlg.UserName!, dlg.Password, ct);
                // termina a distro para o [user] do wsl.conf valer no próximo start
                if (r.Ok) await wsl.TerminateAsync(distro, ct);
                return r;
            });
    }

    /// <summary>
    /// Caminho único para toda ação rápida: confirma (opcional), trava a UI,
    /// executa com timeout de 60s, mostra erro do wsl.exe se houver e atualiza a lista.
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

    /// <summary>
    /// Caminho para operações longas (export/import/install/compactação): sem
    /// timeout, cancelável (mata o processo filho), com janela de progresso e
    /// trava por distro. O <paramref name="lockKey"/> é o nome da distro (ou o
    /// nome alvo numa criação). Retorna true se a operação terminou com sucesso.
    /// </summary>
    internal async Task<bool> RunLongOperationAsync(
        string title,
        string lockKey,
        Func<WslService, IProgress<LongOpUpdate>, CancellationToken, Task<WslResult>> action,
        string? confirm = null)
    {
        if (confirm is not null &&
            MessageBox.Show(confirm, "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
            return false;

        var lease = LongOps.TryAcquire(lockKey);
        if (lease is null)
        {
            MessageBox.Show(
                $"Já existe uma operação longa em andamento para \"{lockKey}\". Aguarde ela terminar.",
                "Operação em andamento", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        IsBusy = true;
        StatusText = title;

        var vm = new LongOperationViewModel(title, title);
        var dialog = new LongOperationDialog(vm) { Owner = Application.Current.MainWindow };

        // Arquivo a exibir enquanto cresce (.tar de export, por exemplo). Escrito
        // pelo callback de progresso (thread da UI) e lido pelo poller (outra thread).
        string? watchPath = null;
        var progress = new Progress<LongOpUpdate>(u =>
        {
            if (u.Status is not null) vm.Status = u.Status;
            if (u.ClearWatch) { watchPath = null; vm.SizeText = null; }
            else if (u.WatchFilePath is not null) watchPath = u.WatchFilePath;
        });

        using var pollCts = new CancellationTokenSource();
        var poller = Task.Run(async () =>
        {
            while (!pollCts.IsCancellationRequested)
            {
                try
                {
                    var path = watchPath;
                    if (path is not null && File.Exists(path))
                    {
                        var len = new FileInfo(path).Length;
                        Application.Current.Dispatcher.Invoke(
                            () => vm.SizeText = $"{ByteSize.Humanize(len)} gravados");
                    }
                }
                catch { /* arquivo pode sumir ou estar em uso */ }

                try { await Task.Delay(1000, pollCts.Token); }
                catch (OperationCanceledException) { break; }
            }
        });

        dialog.Show();

        WslResult? result = null;
        Exception? error = null;
        var canceled = false;
        try
        {
            result = await action(Wsl, progress, vm.Token);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            vm.IsFinished = true;
            pollCts.Cancel();
            try { await poller; } catch { /* ignorado */ }
            dialog.Close();
            vm.Dispose();
            lease.Dispose();
            IsBusy = false;
        }

        if (canceled)
        {
            StatusText = "Operação cancelada.";
        }
        else if (error is not null)
        {
            MessageBox.Show(error.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else if (result is { Ok: false })
        {
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            MessageBox.Show(
                $"wsl.exe retornou código {result.ExitCode}.\n\n{detail.Trim()}",
                "Falha na operação", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        await RefreshAsync();
        return result is { Ok: true };
    }
}
