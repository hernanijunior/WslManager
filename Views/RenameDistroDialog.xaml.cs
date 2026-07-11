using System.Text.RegularExpressions;
using System.Windows;
using Wpf.Ui.Controls;

namespace WslManager.Views;

/// <summary>
/// Pede o novo nome da distro, validando em tempo real: regex, diferente do
/// atual e sem colisão com as demais. "Renomear" só habilita com nome válido.
/// </summary>
public partial class RenameDistroDialog : FluentWindow
{
    private static readonly Regex ValidName = new(@"^[A-Za-z0-9._-]+$");

    private readonly string _currentName;
    private readonly HashSet<string> _takenNames;

    public string? NewName { get; private set; }

    public RenameDistroDialog(string currentName, IEnumerable<string> existingNames)
    {
        _currentName = currentName;
        _takenNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        InitializeComponent();

        NameBox.Text = currentName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnNameChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var name = NameBox.Text.Trim();

        string? problem = null;
        if (name.Length == 0)
            problem = "Informe o novo nome.";
        else if (!ValidName.IsMatch(name))
            problem = "Use apenas letras, números, ponto, hífen e sublinhado.";
        else if (name.Equals(_currentName, StringComparison.Ordinal))
            problem = "O nome é igual ao atual.";
        else if (_takenNames.Contains(name) && !name.Equals(_currentName, StringComparison.OrdinalIgnoreCase))
            problem = $"Já existe uma distro chamada \"{name}\".";

        HintText.Text = problem ?? $"\"{_currentName}\" será renomeada para \"{name}\".";
        OkButton.IsEnabled = problem is null;
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        NewName = NameBox.Text.Trim();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
