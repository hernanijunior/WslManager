namespace WslManager.Core;

/// <summary>
/// Snapshot imutável de uma distro WSL, montado a partir do registro
/// (HKCU\...\Lxss) + saída de "wsl --list --running".
/// </summary>
public sealed record Distro(
    string Guid,
    string Name,
    string BasePath,
    int Version,
    bool IsDefault,
    bool IsRunning,
    bool IsSystem,
    long VhdBytes)
{
    /// <summary>Tamanho do ext4.vhdx formatado (ex.: "12,4 GB").</summary>
    public string VhdSizeText => VhdBytes switch
    {
        <= 0 => "—",
        < 1024L * 1024 * 1024 => $"{VhdBytes / (1024.0 * 1024):N1} MB",
        _ => $"{VhdBytes / (1024.0 * 1024 * 1024):N1} GB",
    };
}
