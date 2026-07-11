namespace WslManager.Core;

/// <summary>Formatação de tamanhos em bytes para exibição (pt-BR).</summary>
public static class ByteSize
{
    public static string Humanize(long bytes) => bytes switch
    {
        <= 0 => "—",
        < 1024L * 1024 => $"{bytes / 1024.0:N1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):N1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):N1} GB",
    };
}
