using WslManager.ViewModels;
using Wpf.Ui.Controls;

namespace WslManager.Views;

/// <summary>
/// Diálogo "Nova distro" (Catálogo / De arquivo / Clonar). O ViewModel decide o
/// que criar; aqui só ligamos o fechamento e disparamos o load do catálogo.
/// </summary>
public partial class NewDistroDialog : FluentWindow
{
    private readonly NewDistroViewModel _viewModel;

    public NewDistroDialog(NewDistroViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.CloseRequested += ok =>
        {
            DialogResult = ok;
            Close();
        };

        // Carrega o catálogo ao abrir (a aba Catálogo é a primeira).
        Loaded += (_, _) =>
        {
            if (_viewModel.LoadCatalogCommand.CanExecute(null))
                _viewModel.LoadCatalogCommand.Execute(null);
        };
    }
}
