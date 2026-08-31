using System.Windows;

namespace AirMic.Windows;

public partial class App : Application
{
    private ReceiverCoordinator? _coordinator;
    private TrayService? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("AirMic encountered an unexpected error. Open Diagnostics for details.", "AirMic", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        _coordinator = new ReceiverCoordinator();
        try { await _coordinator.StartAsync(); }
        catch (Exception ex)
        {
            await _coordinator.Log.WriteAsync("error", "startup_failed", new { error = ex.GetType().Name });
            MessageBox.Show("AirMic could not start. Check that ports 51243 and 51244 are allowed on your Private network.", "AirMic", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        var window = new MainWindow(_coordinator);
        MainWindow = window;
        _tray = new TrayService(window, _coordinator, ShutdownApplication);
        window.Show();
    }

    private async void ShutdownApplication()
    {
        _tray?.Dispose();
        if (_coordinator is not null) await _coordinator.DisposeAsync();
        Shutdown();
    }
}
