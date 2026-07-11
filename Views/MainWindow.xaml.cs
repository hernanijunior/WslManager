using WslManager.ViewModels;
using WslManager.Views.Pages;
using Wpf.Ui.Controls;

namespace WslManager.Views;

/// <summary>
/// Shell da aplicação: hospeda o NavigationView (Distros / Configuração) e a
/// barra de status. Mantém uma única instância do <see cref="MainViewModel"/>,
/// compartilhada pelas páginas via <see cref="PageProvider"/>.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel = new();
    private readonly DistroDetailPage _detailPage = new();

    public MainWindow()
    {
        DataContext = _viewModel;
        InitializeComponent();

        var distrosPage = new DistrosPage { DataContext = _viewModel };
        var settingsPage = new SettingsPage();

        RootNavigation.SetPageProviderService(
            new PageProvider(distrosPage, settingsPage, _detailPage));

        // Clique num card → navega para o detalhe passando o DistroViewModel como DataContext.
        _viewModel.NavigateToDetail = distro =>
            RootNavigation.Navigate(typeof(DistroDetailPage), distro);

        // Navegar só depois que o NavigationView aplicou o template, senão a
        // seleção inicial não "pega" e a página de abertura fica indefinida.
        RootNavigation.Loaded += (_, _) => RootNavigation.Navigate(typeof(DistrosPage));
    }
}
