using System.Text.RegularExpressions;
using System.Windows;
using Wpf.Ui.Controls;

namespace WslManager.Views;

/// <summary>
/// Diálogo opcional pós-import: cria o usuário padrão da distro. A senha vem de
/// um PasswordBox (não é bindável), então o resultado é lido no code-behind.
/// </summary>
public partial class SetupUserDialog : FluentWindow
{
    private static readonly Regex ValidUser = new("^[a-z_][a-z0-9_-]*$");

    public string? UserName { get; private set; }
    public string? Password { get; private set; }

    public SetupUserDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => UserBox.Focus();
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        var user = UserBox.Text.Trim();
        if (user.Length is 0 or > 32 || !ValidUser.IsMatch(user))
        {
            System.Windows.MessageBox.Show(
                "Nome de usuário inválido. Use minúsculas, começando por letra ou '_', até 32 caracteres.",
                "Usuário padrão", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        UserName = user;
        Password = PassBox.Password.Length > 0 ? PassBox.Password : null;
        DialogResult = true;
    }

    private void OnSkip(object sender, RoutedEventArgs e) => DialogResult = false;
}
