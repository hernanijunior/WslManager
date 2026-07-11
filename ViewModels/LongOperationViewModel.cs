using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WslManager.ViewModels;

/// <summary>
/// Estado da janela de progresso de uma operação longa: status textual, tamanho
/// de arquivo crescendo e o botão Cancelar (que sinaliza o token, matando o
/// processo filho no <see cref="Core.WslService.RunLongAsync"/>).
/// </summary>
public sealed partial class LongOperationViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public LongOperationViewModel(string title, string initialStatus)
    {
        _title = title;
        _status = initialStatus;
    }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private string? _sizeText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancelling;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isFinished;

    public CancellationToken Token => _cts.Token;

    private bool CanCancel => !IsCancelling && !IsFinished;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        IsCancelling = true;
        Status = "Cancelando…";
        _cts.Cancel();
    }

    public void Dispose() => _cts.Dispose();
}
