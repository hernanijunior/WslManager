using System.Runtime.InteropServices;

namespace WslManager.Core;

/// <summary>Dados do host usados para limitar os sliders do .wslconfig.</summary>
public static class SystemInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    /// <summary>RAM física total (bytes); 0 se indisponível.</summary>
    public static ulong TotalPhysicalMemoryBytes()
    {
        var s = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref s) ? s.ullTotalPhys : 0;
    }

    /// <summary>RAM física total em GB (arredondada).</summary>
    public static double TotalPhysicalMemoryGb()
        => Math.Round(TotalPhysicalMemoryBytes() / (1024.0 * 1024 * 1024));

    public static int ProcessorCount => Environment.ProcessorCount;

    /// <summary>
    /// networkingMode=mirrored exige Windows 11 22H2+ (build 22000+). No Windows 10
    /// a opção fica desabilitada com tooltip explicativo.
    /// </summary>
    public static bool MirroredNetworkingSupported => Environment.OSVersion.Version.Build >= 22000;
}
