using System.Windows.Controls;

namespace WslManager.Views.Pages;

/// <summary>
/// Página principal: toolbar + lista de distros em cards. O DataContext é o
/// <see cref="ViewModels.MainViewModel"/> compartilhado (definido pela shell).
/// </summary>
public partial class DistrosPage : Page
{
    public DistrosPage()
    {
        InitializeComponent();
    }
}
