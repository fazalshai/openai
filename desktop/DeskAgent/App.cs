using System.Windows;

namespace DeskAgent;
public static class App
{
    [STAThread]
    public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(e.Exception.Message, "Desk Agent"); e.Handled = true; };
        app.Run(new MainWindow());
    }
}
