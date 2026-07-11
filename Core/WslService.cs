using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace WslManager.Core;

/// <summary>Resultado de uma chamada ao wsl.exe.</summary>
public sealed record WslResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Toda a interop com o WSL: enumeração via registro (fonte confiável,
/// sem elevação) e ações via wsl.exe (única API suportada).
///
/// Armadilhas tratadas aqui:
///  - wsl.exe escreve UTF-16LE por padrão → WSL_UTF8=1 + StandardOutputEncoding.
///  - ReadToEnd + WaitForExit trava se stderr encher o buffer → leitura async dos dois.
///  - Console preto piscando a cada chamada → CreateNoWindow.
///  - Redirecionamento WOW64 → caminho absoluto de System32 (e o csproj força x64).
/// </summary>
public sealed class WslService
{
    private const string LxssKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Lxss";

    /// <summary>Distros que pertencem a outras ferramentas. Terminá-las quebra o Docker.</summary>
    private static readonly string[] SystemDistros =
        ["docker-desktop", "docker-desktop-data", "rancher-desktop", "rancher-desktop-data", "podman-machine-default"];

    private static string WslExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

    // ─────────────────────────────── infra ───────────────────────────────

    /// <summary>Monta o ProcessStartInfo com todas as armadilhas de interop tratadas.</summary>
    private static ProcessStartInfo BuildStartInfo(string[] args)
    {
        var psi = new ProcessStartInfo(WslExePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["WSL_UTF8"] = "1"; // força UTF-8 (WSL >= 0.64)
        return psi;
    }

    public async Task<WslResult> RunAsync(CancellationToken ct = default, params string[] args)
    {
        var psi = BuildStartInfo(args);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Não foi possível iniciar wsl.exe.");

        // ler os dois streams em paralelo ANTES do WaitForExit evita deadlock de buffer
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);

        return new WslResult(p.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>
    /// Igual ao <see cref="RunAsync"/>, mas para operações longas (export,
    /// import, install de catálogo, compactação): SEM timeout e, se o token for
    /// cancelado, MATA a árvore do processo filho. Lança
    /// <see cref="OperationCanceledException"/> ao cancelar.
    /// </summary>
    public async Task<WslResult> RunLongAsync(CancellationToken ct, params string[] args)
    {
        var psi = BuildStartInfo(args);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Não foi possível iniciar wsl.exe.");

        // Sem ct nas leituras: queremos drenar os buffers mesmo se cancelarmos
        // pelo WaitForExit; o deadlock de buffer continua evitado (leitura async).
        var stdoutTask = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = p.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!p.HasExited) p.Kill(entireProcessTree: true);
            }
            catch { /* já saiu ou não pôde ser morto */ }
            throw;
        }

        return new WslResult(p.ExitCode, await stdoutTask, await stderrTask);
    }

    // ───────────────────────────── consulta ─────────────────────────────

    /// <summary>Nomes das distros em execução. Não acorda nenhuma distro.</summary>
    public async Task<HashSet<string>> GetRunningAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(ct, "--list", "--running", "--quiet").ConfigureAwait(false);
        // exit code 1 = "não há distribuições em execução" → conjunto vazio, não é erro
        if (!r.Ok) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return r.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Trim('\0', '\r')) // defesa extra caso WSL_UTF8 seja ignorado (WSL antigo)
            .Where(l => l.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Enumera as distros lendo o registro — não acorda a VM e funciona
    /// mesmo com o wslservice parado.
    /// </summary>
    public async Task<IReadOnlyList<Distro>> ListAsync(CancellationToken ct = default)
    {
        var running = await GetRunningAsync(ct).ConfigureAwait(false);
        var result = new List<Distro>();

        using var root = Registry.CurrentUser.OpenSubKey(LxssKeyPath);
        if (root is null) return result; // WSL nunca foi instalado/usado

        var defaultGuid = root.GetValue("DefaultDistribution") as string ?? string.Empty;

        foreach (var guid in root.GetSubKeyNames().Where(k => k.StartsWith('{')))
        {
            ct.ThrowIfCancellationRequested();
            using var key = root.OpenSubKey(guid);
            if (key?.GetValue("DistributionName") is not string name) continue;

            var basePath = (key.GetValue("BasePath") as string ?? string.Empty)
                .Replace(@"\\?\", string.Empty);

            result.Add(new Distro(
                Guid: guid,
                Name: name,
                BasePath: basePath,
                Version: key.GetValue("Version") is int v ? v : 2,
                IsDefault: guid.Equals(defaultGuid, StringComparison.OrdinalIgnoreCase),
                IsRunning: running.Contains(name),
                IsSystem: SystemDistros.Contains(name, StringComparer.OrdinalIgnoreCase),
                VhdBytes: GetVhdBytes(basePath)));
        }

        return result
            .OrderByDescending(d => d.IsDefault)
            .ThenBy(d => d.IsSystem)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long GetVhdBytes(string basePath)
    {
        try
        {
            var vhd = Path.Combine(basePath, "ext4.vhdx");
            return File.Exists(vhd) ? new FileInfo(vhd).Length : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    // ─────────────────────────────── ações ───────────────────────────────

    /// <summary>
    /// "Ligar" não existe como comando: uma distro sobe ao executar qualquer
    /// processo nela. `true` retorna imediatamente e deixa a distro de pé.
    /// </summary>
    public Task<WslResult> WakeAsync(string name, CancellationToken ct = default)
        => RunAsync(ct, "-d", name, "--", "true");

    /// <summary>Derruba apenas a distro informada.</summary>
    public Task<WslResult> TerminateAsync(string name, CancellationToken ct = default)
        => RunAsync(ct, "--terminate", name);

    /// <summary>Derruba a VM inteira — TODAS as distros, inclusive docker-desktop.</summary>
    public Task<WslResult> ShutdownAsync(CancellationToken ct = default)
        => RunAsync(ct, "--shutdown");

    public Task<WslResult> SetDefaultAsync(string name, CancellationToken ct = default)
        => RunAsync(ct, "--set-default", name);

    // ─────────────────────────── conveniências ───────────────────────────

    /// <summary>Abre um terminal na distro (Windows Terminal se houver, senão console do wsl.exe).</summary>
    public void OpenTerminal(string name)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe")
            {
                ArgumentList = { "wsl.exe", "-d", name, "--cd", "~" },
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception) // wt.exe não instalado
        {
            Process.Start(new ProcessStartInfo(WslExePath)
            {
                ArgumentList = { "-d", name, "--cd", "~" },
                UseShellExecute = true,
            });
        }
    }

    /// <summary>Abre o filesystem da distro no Explorer (\\wsl.localhost\nome).</summary>
    public void OpenExplorer(string name)
        => Process.Start(new ProcessStartInfo("explorer.exe", $@"\\wsl.localhost\{name}")
        {
            UseShellExecute = true,
        });
}
