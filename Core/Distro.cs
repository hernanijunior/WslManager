using System.IO;

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
    public string VhdSizeText => ByteSize.Humanize(VhdBytes);

    /// <summary>Caminho completo do disco virtual (ext4.vhdx) da distro.</summary>
    public string VhdxPath => string.IsNullOrEmpty(BasePath)
        ? "—"
        : Path.Combine(BasePath, "ext4.vhdx");
}
