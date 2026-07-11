namespace WslManager.Core;

/// <summary>
/// Atualização de progresso emitida por uma operação longa. Permite trocar o
/// texto de status entre etapas (ex.: "Exportando…" → "Importando…") e indicar
/// qual arquivo está crescendo, para a UI mostrar o tamanho atual.
/// </summary>
public sealed class LongOpUpdate
{
    /// <summary>Novo texto de status, ou null para manter o atual.</summary>
    public string? Status { get; init; }

    /// <summary>Arquivo cujo tamanho deve ser exibido enquanto cresce (poll de 1s).</summary>
    public string? WatchFilePath { get; init; }

    /// <summary>Para de monitorar o arquivo atual (ex.: etapa que não gera arquivo).</summary>
    public bool ClearWatch { get; init; }

    public static LongOpUpdate WithStatus(string status) => new() { Status = status };
    public static LongOpUpdate Watch(string status, string filePath) => new() { Status = status, WatchFilePath = filePath };
}

/// <summary>
/// Impede duas operações longas simultâneas sobre a MESMA distro (regra do
/// CLAUDE.md — export/import/install/compactação não podem se sobrepor). A
/// chave costuma ser o nome da distro, ou o nome alvo numa criação por import.
/// </summary>
public sealed class LongOperationManager
{
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool IsBusy(string key)
    {
        lock (_gate) return _active.Contains(key);
    }

    /// <summary>
    /// Reserva a distro para uma operação longa. Retorna null se já houver uma
    /// em andamento — o chamador deve abortar. Libere com Dispose() ao terminar.
    /// </summary>
    public IDisposable? TryAcquire(string key)
    {
        lock (_gate)
        {
            if (!_active.Add(key)) return null;
        }
        return new Lease(this, key);
    }

    private void Release(string key)
    {
        lock (_gate) _active.Remove(key);
    }

    private sealed class Lease(LongOperationManager owner, string key) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            owner.Release(key);
        }
    }
}
