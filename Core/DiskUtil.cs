using System.IO;
using System.Runtime.InteropServices;

namespace WslManager.Core;

/// <summary>Utilidades de disco para o vhdx.</summary>
public static class DiskUtil
{
    // TODO (fase futura): compactação profunda via diskpart ("compact vdisk") ou
    // Optimize-VHD (módulo Hyper-V do PowerShell). Ambos exigem elevação (admin)
    // e/ou o recurso Hyper-V instalado, então ficam fora desta fase. O método
    // padrão continua sendo `wsl --manage <distro> --set-sparse true`.

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    /// <summary>
    /// Tamanho REALMENTE alocado em disco (bytes) — diferente de
    /// <see cref="FileInfo.Length"/> quando o vhdx é esparso. Retorna -1 se
    /// indisponível.
    /// </summary>
    public static long GetSizeOnDisk(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return -1;

        var low = GetCompressedFileSizeW(path, out var high);
        if (low == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0)
            return -1;

        return ((long)high << 32) | low;
    }
}
