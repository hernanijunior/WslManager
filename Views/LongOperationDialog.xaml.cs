using System.ComponentModel;
using WslManager.ViewModels;
using Wpf.Ui.Controls;

namespace WslManager.Views;

/// <summary>
/// Janela modal de progresso de uma operação longa. Fechá-la manualmente antes
/// do fim equivale a cancelar (não deixa o processo filho órfão); a própria
/// operação fecha a janela ao terminar.
/// </summary>
public partial class LongOperationDialog : FluentWindow
{
    public LongOperationDialog(LongOperationViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private LongOperationViewModel Vm => (LongOperationViewModel)DataContext;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Vm.IsFinished)
        {
            // Ainda rodando: transforma o "fechar" em "cancelar" e mantém a
            // janela até a operação realmente encerrar (quem fecha é o orquestrador).
            e.Cancel = true;
            if (Vm.CancelCommand.CanExecute(null))
                Vm.CancelCommand.Execute(null);
        }

        base.OnClosing(e);
    }
}
