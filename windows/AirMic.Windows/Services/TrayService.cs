using Forms = System.Windows.Forms;

namespace AirMic.Windows;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _status;
    private readonly MainWindow _window;
    private readonly ReceiverCoordinator _coordinator;

    public TrayService(MainWindow window, ReceiverCoordinator coordinator, Action exit)
    {
        _window = window;
        _coordinator = coordinator;
        _status = new Forms.ToolStripMenuItem("● Ready to pair") { Enabled = false };
        var open = new Forms.ToolStripMenuItem("Open AirMic", null, (_, _) => _window.Dispatcher.Invoke(_window.ShowDashboard));
        var disconnect = new Forms.ToolStripMenuItem("Disconnect", null, async (_, _) => await _coordinator.DisconnectAsync());
        var settings = new Forms.ToolStripMenuItem("Settings", null, (_, _) => _window.Dispatcher.Invoke(_window.ShowDashboard));
        var quit = new Forms.ToolStripMenuItem("Exit", null, (_, _) => _window.Dispatcher.Invoke(exit));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange([_status, new Forms.ToolStripSeparator(), open, disconnect, settings, new Forms.ToolStripSeparator(), quit]);
        _icon = new Forms.NotifyIcon
        {
            Text = "AirMic",
            Icon = System.Drawing.SystemIcons.Information,
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => _window.Dispatcher.Invoke(_window.ShowDashboard);
        _coordinator.StateChanged += OnStateChanged;
        OnStateChanged(this, EventArgs.Empty);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        _window.Dispatcher.InvokeAsync(() =>
        {
            _status.Text = _coordinator.IsConnected ? $"● Connected: {_coordinator.ConnectedDevice}" : "● Ready to pair";
            _icon.Text = _coordinator.IsConnected ? "AirMic — Connected" : "AirMic — Ready";
        });
    }

    public void Dispose()
    {
        _coordinator.StateChanged -= OnStateChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
