using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace WslManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "wslmgr-crash.log"),
                $"{DateTime.Now:O}\n{e.Exception}\n\n");
        }
        catch { /* ignore */ }

        MessageBox.Show(e.Exception.Message, "Erro inesperado",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
