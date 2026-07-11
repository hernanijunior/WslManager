using System.Windows;
using Wpf.Ui.Controls;

namespace WslManager.Views;

/// <summary>
/// Fluxo blindado de apagar (unregister) — regra do CLAUDE.md: mostra o que
/// será perdido, oferece exportar antes e só habilita "Apagar" quando o usuário
/// digita o nome EXATO da distro. Distros de sistema nunca chegam aqui.
/// </summary>
public partial class DeleteDistroDialog : FluentWindow
{
    private readonly string _expectedName;

    public bool ExportFirst => ExportCheck.IsChecked == true;

    public DeleteDistroDialog(string name, string vhdxPath, string sizeText)
    {
        _expectedName = name;
        InitializeComponent();

        WarnBar.Message = $"Apagar \"{name}\" e todo o seu conteúdo.";
        DetailText.Text = $"Disco: {vhdxPath}\nTamanho atual: {sizeText}";

        Loaded += (_, _) => ConfirmBox.Focus();
    }

    private void OnConfirmChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => DeleteButton.IsEnabled = string.Equals(ConfirmBox.Text, _expectedName, StringComparison.Ordinal);

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        // guarda extra: nunca apagar sem o nome exato batido
        if (!string.Equals(ConfirmBox.Text, _expectedName, StringComparison.Ordinal)) return;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
