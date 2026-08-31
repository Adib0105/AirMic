using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AirMic.Windows;

public partial class MainWindow : Window
{
    private readonly ReceiverCoordinator _coordinator;
    private readonly DispatcherTimer _timer;

    public MainWindow(ReceiverCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator;
        PinText.Text = coordinator.PairingPin;
        StartupCheck.IsChecked = StartupRegistration.IsEnabled;
        _coordinator.StateChanged += CoordinatorOnStateChanged;
        _coordinator.LevelChanged += (_, level) => Dispatcher.InvokeAsync(() => LevelBar.Value = level);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshDiagnostics(), Dispatcher);
        Closing += HideInsteadOfClose;
        RefreshState();
        RefreshDiagnostics();
    }

    public void ShowDashboard()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void CoordinatorOnStateChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(RefreshState);

    private void RefreshState()
    {
        DeviceText.Text = _coordinator.ConnectedDevice;
        StatusText.Text = _coordinator.IsConnected ? "Connected" : "Ready to pair on this private network";
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_coordinator.IsConnected ? "#61E7C3" : "#F5B74D"));
        PinText.Text = _coordinator.PairingPin;
    }

    private void RefreshDiagnostics()
    {
        var snapshot = _coordinator.Diagnostics.Snapshot();
        LatencyText.Text = snapshot.AudioStreamActive ? $"Latency: {snapshot.LatencyMilliseconds:0} ms" : "Latency: -- ms";
        RateText.Text = snapshot.SampleRate == 0 ? "Sample Rate: --" : $"Sample Rate: {snapshot.SampleRate / 1000d:0.#} kHz";
        PacketText.Text = snapshot.AudioStreamActive ? $"Packets/sec: {snapshot.PacketsPerSecond:0}" : "Packets/sec: --";
        VirtualStatusText.Text = snapshot.VirtualMicrophoneAvailable ? "Status: Ready" : "Status: Driver unavailable — local preview can still verify the network stream";
    }

    private void GainSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_coordinator is not null) _coordinator.Audio.Gain = (float)e.NewValue;
    }

    private void PreviewCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_coordinator is not null) _coordinator.Audio.PreviewEnabled = PreviewCheck.IsChecked == true;
    }

    private void MuteButton_OnClick(object sender, RoutedEventArgs e)
    {
        _coordinator.Audio.Muted = !_coordinator.Audio.Muted;
        MuteButton.Content = _coordinator.Audio.Muted ? "UNMUTE" : "MUTE";
    }

    private async void DisconnectButton_OnClick(object sender, RoutedEventArgs e) => await _coordinator.DisconnectAsync();

    private void CopyDiagnosticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_coordinator.CopyableDiagnostics());
        StatusText.Text = "Diagnostics copied";
    }

    private void StartupCheck_OnClick(object sender, RoutedEventArgs e)
    {
        try { StartupRegistration.SetEnabled(StartupCheck.IsChecked == true); }
        catch
        {
            StartupCheck.IsChecked = StartupRegistration.IsEnabled;
            MessageBox.Show("AirMic could not update the Windows startup setting.", "AirMic", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HideInsteadOfClose(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
