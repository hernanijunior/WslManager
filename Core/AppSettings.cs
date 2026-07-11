using System.IO;
using System.Text.Json;

namespace WslManager.Core;

/// <summary>Preferências do próprio app (não confundir com o .wslconfig global).</summary>
public sealed class AppSettings
{
    /// <summary>Limiar do alerta de disco (padrão 30 GB).</summary>
    public long DiskAlertThresholdBytes { get; set; } = 30L * 1024 * 1024 * 1024;
}

/// <summary>
/// Carrega/salva <see cref="AppSettings"/> em
/// %LocalAppData%\WslManager\settings.json. Falhas de I/O são silenciosas —
/// preferências são "melhor esforço", nunca derrubam o app.
/// </summary>
public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WslManager", "settings.json");

    public AppSettings Current { get; }

    public AppSettingsService() => Current = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* JSON inválido/ausente → padrões */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch { /* melhor esforço */ }
    }
}
