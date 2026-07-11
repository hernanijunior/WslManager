using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WslManager.Core;

namespace WslManager.ViewModels;

/// <summary>
/// Editor do %UserProfile%\.wslconfig. Lê os valores para o formulário e, no
/// Salvar, escreve de volta pelo <see cref="WslConfigDocument"/> (round-trip:
/// comentários e chaves desconhecidas sobrevivem). Presets só preenchem o form.
/// </summary>
public sealed partial class WslConfigViewModel : ObservableObject
{
    private const string Wsl2 = "wsl2";
    private const string Experimental = "experimental";
    private const string Default = "(padrão)";

    private static readonly Regex SizeRe =
        new(@"^(?<n>\d+(?:[.,]\d+)?)\s*(?<u>[KMGT]?B)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly WslConfigService _service;
    private readonly Func<Task> _restartWsl;
    private WslConfigDocument _doc;

    public double MaxMemoryGb { get; }
    public int MaxProcessors { get; }
    public bool MirroredSupported { get; }
    public string ConfigPath => _service.ConfigPath;

    public IReadOnlyList<string> AutoReclaimOptions { get; } = [Default, "disabled", "gradual", "dropcache"];
    public IReadOnlyList<string> NetworkingOptions { get; } = [Default, "NAT", "mirrored"];

    public WslConfigViewModel(WslConfigService service, double maxMemoryGb, int maxProcessors,
        bool mirroredSupported, Func<Task> restartWsl)
    {
        _service = service;
        _restartWsl = restartWsl;
        MaxMemoryGb = Math.Max(1, maxMemoryGb);
        MaxProcessors = Math.Max(1, maxProcessors);
        MirroredSupported = mirroredSupported;

        _doc = _service.Load();
        LoadFromDoc();
    }

    // ─────────────────────── campos do formulário ───────────────────────

    [ObservableProperty] private double _memoryGb;
    [ObservableProperty] private int _processors;
    [ObservableProperty] private double _swapGb;
    [ObservableProperty] private int _vmIdleTimeout;
    [ObservableProperty] private bool _sparseVhd;
    [ObservableProperty] private string _autoMemoryReclaim = Default;
    [ObservableProperty] private string _networkingMode = Default;

    // ─────────────────────────── carga/gravação ─────────────────────────

    private void LoadFromDoc()
    {
        MemoryGb = ParseSizeGb(_doc.Get(Wsl2, "memory"));
        Processors = ParseInt(_doc.Get(Wsl2, "processors"));
        SwapGb = ParseSizeGb(_doc.Get(Wsl2, "swap"));
        VmIdleTimeout = ParseInt(_doc.Get(Wsl2, "vmIdleTimeout"));
        NetworkingMode = NormalizeOption(_doc.Get(Wsl2, "networkingMode"), NetworkingOptions);
        AutoMemoryReclaim = NormalizeOption(_doc.Get(Experimental, "autoMemoryReclaim"), AutoReclaimOptions);
        SparseVhd = string.Equals(_doc.Get(Experimental, "sparseVhd"), "true", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyToDoc()
    {
        _doc.SetOrRemove(Wsl2, "memory", MemoryGb >= 1 ? $"{(int)Math.Round(MemoryGb)}GB" : null);
        _doc.SetOrRemove(Wsl2, "processors", Processors >= 1 ? Processors.ToString() : null);
        _doc.SetOrRemove(Wsl2, "swap", SwapGb >= 0.5 ? $"{(int)Math.Round(SwapGb)}GB" : null);
        _doc.SetOrRemove(Wsl2, "vmIdleTimeout", VmIdleTimeout >= 1 ? VmIdleTimeout.ToString() : null);
        _doc.SetOrRemove(Wsl2, "networkingMode", NetworkingMode == Default ? null : NetworkingMode);
        _doc.SetOrRemove(Experimental, "autoMemoryReclaim", AutoMemoryReclaim == Default ? null : AutoMemoryReclaim);
        _doc.SetOrRemove(Experimental, "sparseVhd", SparseVhd ? "true" : null);
    }

    private bool Persist()
    {
        ApplyToDoc();
        try
        {
            var backup = _service.Save(_doc);
            _doc = _service.Load(); // recarrega para refletir o estado gravado
            LoadFromDoc();
            MessageBox.Show(
                backup is null
                    ? $"Configuração salva em:\n{ConfigPath}"
                    : $"Configuração salva.\nBackup criado: {Path.GetFileName(backup)}",
                "Configuração", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao salvar o .wslconfig:\n{ex.Message}",
                "Configuração", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    [RelayCommand]
    private void Save() => Persist();

    [RelayCommand]
    private async Task SaveAndRestartAsync()
    {
        if (Persist())
            await _restartWsl();
    }

    // ─────────────────────────────── presets ────────────────────────────

    [RelayCommand]
    private void PresetLeve()
    {
        MemoryGb = Math.Min(MaxMemoryGb, 4);
        Processors = Math.Max(2, MaxProcessors / 2);
        SwapGb = 2;
        VmIdleTimeout = 60000;
        AutoMemoryReclaim = "gradual";
        SparseVhd = true;
        NetworkingMode = Default;
    }

    [RelayCommand]
    private void PresetDev()
    {
        MemoryGb = Math.Min(MaxMemoryGb, Math.Max(8, Math.Round(MaxMemoryGb / 2)));
        Processors = MaxProcessors;
        SwapGb = 8;
        VmIdleTimeout = 0;
        AutoMemoryReclaim = "disabled";
        SparseVhd = true;
        NetworkingMode = MirroredSupported ? "mirrored" : Default;
    }

    // ───────────────────────────── parsing ──────────────────────────────

    private static int ParseInt(string? v)
        => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static double ParseSizeGb(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return 0;
        var m = SizeRe.Match(v.Trim());
        if (!m.Success) return 0;

        var n = double.Parse(m.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return m.Groups["u"].Value.ToUpperInvariant() switch
        {
            "KB" => n / (1024 * 1024),
            "MB" => n / 1024,
            "TB" => n * 1024,
            _ => n, // GB ou sem unidade
        };
    }

    private static string NormalizeOption(string? value, IReadOnlyList<string> options)
    {
        if (string.IsNullOrWhiteSpace(value)) return Default;
        var match = options.FirstOrDefault(o => o.Equals(value, StringComparison.OrdinalIgnoreCase));
        return match ?? value; // valor não-canônico do arquivo: mostra como está
    }
}
