using System.Windows.Controls;

namespace WslManager.Views.Pages;

/// <summary>
/// Página de detalhe de uma distro. O DataContext é o
/// <see cref="ViewModels.DistroViewModel"/> passado na navegação
/// (<c>NavigationView.Navigate(typeof(DistroDetailPage), distroVm)</c>).
/// </summary>
public partial class DistroDetailPage : Page
{
    public DistroDetailPage()
    {
        InitializeComponent();
    }
}
