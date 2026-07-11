using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WslManager.Core;

namespace WslManager.ViewModels;

/// <summary>
/// Descreve a distro a ser criada. <see cref="Action"/> é a operação longa;
/// <see cref="SetupUserForDistro"/>, quando não-nulo, dispara o diálogo de
/// usuário padrão após o sucesso (só faz sentido em import de .tar).
/// </summary>
internal sealed record NewDistroRequest(
    string Title,
    string LockKey,
    Func<WslService, IProgress<LongOpUpdate>, CancellationToken, Task<WslResult>> Action,
    string? SetupUserForDistro);

/// <summary>
/// ViewModel do diálogo "Nova distro" (abas Catálogo / De arquivo / Clonar).
/// Valida antes de montar o <see cref="NewDistroRequest"/>; quem executa a
/// operação longa é o <see cref="MainViewModel"/>.
/// </summary>
public sealed partial class NewDistroViewModel : ObservableObject
{
    private static readonly Regex ValidName = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    private readonly WslService _wsl;
    private readonly LongOperationManager _longOps;
    private readonly HashSet<string> _existingNames;

    /// <summary>Disparado quando o diálogo deve fechar (true = criar).</summary>
    public event Action<bool>? CloseRequested;

    internal NewDistroRequest? Result { get; private set; }

    public IReadOnlyList<string> CloneSources { get; }

    public NewDistroViewModel(
        WslService wsl,
        IEnumerable<string> existingNames,
        IEnumerable<string> cloneableNames,
        LongOperationManager longOps)
    {
        _wsl = wsl;
        _longOps = longOps;
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        CloneSources = cloneableNames.ToList();

        if (CloneSources.Count > 0)
            SelectedCloneSource = CloneSources[0];
    }

    // ─────────────────────────── comum ───────────────────────────

    [ObservableProperty]
    private int _selectedTab;

    // ───────────────────────── catálogo ──────────────────────────

    public ObservableCollection<OnlineDistro> Catalog { get; } = [];

    [ObservableProperty]
    private OnlineDistro? _selectedOnline;

    [ObservableProperty]
    private bool _isLoadingCatalog;

    [ObservableProperty]
    private string? _catalogError;

    [RelayCommand]
    private async Task LoadCatalogAsync()
    {
        IsLoadingCatalog = true;
        CatalogError = null;
        Catalog.Clear();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var list = await _wsl.ListOnlineAsync(cts.Token);
            foreach (var d in list) Catalog.Add(d);
            if (Catalog.Count > 0) SelectedOnline = Catalog[0];
        }
        catch (OperationCanceledException)
        {
            CatalogError = "Tempo esgotado ao consultar o catálogo. Verifique a conexão.";
        }
        catch (Exception ex)
        {
            CatalogError = $"Não foi possível carregar o catálogo: {ex.Message}";
        }
        finally
        {
            IsLoadingCatalog = false;
        }
    }

    // ─────────────────────── de arquivo ──────────────────────────

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fileDestFolder = string.Empty;

    private bool _fileDestTouched;

    partial void OnFileNameChanged(string value)
    {
        if (!_fileDestTouched)
            FileDestFolder = DefaultDestFolder(value);
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecionar arquivo da distro",
            Filter = "Distros (*.tar;*.tar.gz;*.tar.xz;*.wsl)|*.tar;*.tar.gz;*.tar.xz;*.wsl|Todos os arquivos (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        FilePath = dlg.FileName;
        if (string.IsNullOrWhiteSpace(FileName))
        {
            // sugere um nome a partir do arquivo (sem extensões .tar/.tar.gz/…)
            var baseName = Path.GetFileName(dlg.FileName);
            foreach (var ext in new[] { ".tar.gz", ".tar.xz", ".tar", ".wsl" })
                if (baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName[..^ext.Length];
                    break;
                }
            FileName = Regex.Replace(baseName, @"[^A-Za-z0-9._-]", "-");
        }
    }

    [RelayCommand]
    private void BrowseFileDest()
    {
        var dlg = new OpenFolderDialog { Title = "Pasta destino do vhdx" };
        if (dlg.ShowDialog() != true) return;
        FileDestFolder = dlg.FolderName;
        _fileDestTouched = true;
    }

    private bool IsWslPackage =>
        FilePath.EndsWith(".wsl", StringComparison.OrdinalIgnoreCase);

    // ───────────────────────── clonar ────────────────────────────

    [ObservableProperty]
    private string? _selectedCloneSource;

    [ObservableProperty]
    private string _cloneNewName = string.Empty;

    [ObservableProperty]
    private string _cloneDestFolder = string.Empty;

    private bool _cloneDestTouched;

    partial void OnSelectedCloneSourceChanged(string? value)
    {
        if (value is null) return;
        if (string.IsNullOrWhiteSpace(CloneNewName) || CloneNewName.EndsWith("-clone", StringComparison.OrdinalIgnoreCase))
            CloneNewName = $"{value}-clone";
    }

    partial void OnCloneNewNameChanged(string value)
    {
        if (!_cloneDestTouched)
            CloneDestFolder = DefaultDestFolder(value);
    }

    [RelayCommand]
    private void BrowseCloneDest()
    {
        var dlg = new OpenFolderDialog { Title = "Pasta destino do vhdx" };
        if (dlg.ShowDialog() != true) return;
        CloneDestFolder = dlg.FolderName;
        _cloneDestTouched = true;
    }

    // ──────────────────────── criação ────────────────────────────

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private void Create()
    {
        Result = SelectedTab switch
        {
            0 => BuildCatalog(),
            1 => BuildFromFile(),
            2 => BuildClone(),
            _ => null,
        };
        if (Result is not null)
            CloseRequested?.Invoke(true);
    }

    private NewDistroRequest? BuildCatalog()
    {
        if (SelectedOnline is null)
        {
            Warn("Selecione uma distro do catálogo.");
            return null;
        }

        var name = SelectedOnline.Name;
        return new NewDistroRequest(
            $"Instalando {name}…",
            name,
            (wsl, _, ct) => wsl.InstallFromCatalogAsync(name, ct),
            SetupUserForDistro: null);
    }

    private NewDistroRequest? BuildFromFile()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
        {
            Warn("Selecione um arquivo existente (.tar, .tar.gz, .tar.xz ou .wsl).");
            return null;
        }

        if (IsWslPackage)
        {
            // .wsl carrega o próprio nome; só instala.
            var path = FilePath;
            return new NewDistroRequest(
                "Instalando a partir do arquivo .wsl…",
                Path.GetFileNameWithoutExtension(path),
                (wsl, _, ct) => wsl.InstallFromFileAsync(path, ct),
                SetupUserForDistro: null);
        }

        var name = FileName.Trim();
        if (!ValidateNewName(name)) return null;

        var dest = FileDestFolder.Trim();
        if (string.IsNullOrWhiteSpace(dest))
        {
            Warn("Informe a pasta destino do vhdx.");
            return null;
        }

        var tar = FilePath;
        return new NewDistroRequest(
            $"Importando {name}…",
            name,
            (wsl, progress, ct) =>
            {
                Directory.CreateDirectory(dest);
                progress.Report(LongOpUpdate.WithStatus($"Importando {name}…"));
                return wsl.ImportAsync(name, dest, tar, ct);
            },
            SetupUserForDistro: name);
    }

    private NewDistroRequest? BuildClone()
    {
        if (string.IsNullOrWhiteSpace(SelectedCloneSource))
        {
            Warn("Selecione a distro de origem.");
            return null;
        }

        var source = SelectedCloneSource;
        var name = CloneNewName.Trim();
        if (!ValidateNewName(name)) return null;

        if (_longOps.IsBusy(source))
        {
            Warn($"Já há uma operação longa em andamento para \"{source}\".");
            return null;
        }

        var dest = CloneDestFolder.Trim();
        if (string.IsNullOrWhiteSpace(dest))
            dest = DefaultDestFolder(name);

        return new NewDistroRequest(
            $"Clonando {source} → {name}…",
            name,
            async (wsl, progress, ct) =>
            {
                var tmp = Path.Combine(Path.GetTempPath(), $"wslmgr-clone-{Guid.NewGuid():N}.tar");
                try
                {
                    progress.Report(LongOpUpdate.Watch($"Exportando {source}…", tmp));
                    var export = await wsl.ExportAsync(source, tmp, ct);
                    if (!export.Ok) return export;

                    progress.Report(new LongOpUpdate { Status = $"Importando {name}…", ClearWatch = true });
                    Directory.CreateDirectory(dest);
                    return await wsl.ImportAsync(name, dest, tmp, ct);
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* melhor esforço */ }
                }
            },
            // Clone copia o usuário da origem — não precisa configurar usuário.
            SetupUserForDistro: null);
    }

    // ──────────────────────── validação ──────────────────────────

    private bool ValidateNewName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Warn("Informe o nome da nova distro.");
            return false;
        }
        if (!ValidName.IsMatch(name))
        {
            Warn("Nome inválido. Use apenas letras, números, ponto, hífen e sublinhado.");
            return false;
        }
        if (_existingNames.Contains(name))
        {
            Warn($"Já existe uma distro chamada \"{name}\".");
            return false;
        }
        return true;
    }

    private static string DefaultDestFolder(string name)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var safe = string.IsNullOrWhiteSpace(name) ? "distro" : name;
        return Path.Combine(local, "WslManager", "distros", safe);
    }

    private static void Warn(string message) =>
        System.Windows.MessageBox.Show(message, "Nova distro",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
}
