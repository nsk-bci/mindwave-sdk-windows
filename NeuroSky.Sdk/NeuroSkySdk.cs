using NeuroSky.Sdk.Model;
using NeuroSky.Sdk.Transport;

namespace NeuroSky.Sdk;

/// <summary>
/// Entry point for the NeuroSky MindWave Windows SDK.
/// Uses BLE by default. Pass <see cref="TransportMode.BtClassic"/> to use BT Classic.
/// </summary>
/// <example>
/// <code>
/// var sdk = new NeuroSkySdk();
/// await sdk.ConnectAsync("AA:BB:CC:DD:EE:FF");
///
/// await foreach (var data in sdk.DataStream(cts.Token))
/// {
///     Console.WriteLine($"Attention: {data.Attention}");
/// }
/// </code>
/// </example>
public sealed class NeuroSkySdk : IAsyncDisposable
{
    private readonly BleTransport _ble = new();
    private readonly BtClassicTransport _bt = new();
    private ITransport _active;

    public ConnectionState State => _active.State;
    public event EventHandler<ConnectionState>? StateChanged;

    public NeuroSkySdk()
    {
        _active = _ble;
        _ble.StateChanged += (_, s) => StateChanged?.Invoke(this, s);
    }

    /// <summary>Real-time EEG data stream.</summary>
    public IAsyncEnumerable<BrainWaveData> DataStream(CancellationToken ct = default)
        => _active.DataStream(ct);

    /// <summary>
    /// Connect to a MindWave headset.
    /// </summary>
    /// <param name="deviceAddress">Bluetooth MAC address (e.g. "AA:BB:CC:DD:EE:FF")</param>
    /// <param name="mode">
    /// <see cref="TransportMode.Ble"/> — BLE only, no pairing required (default).<br/>
    /// <see cref="TransportMode.BtClassic"/> — BT Classic only, requires Windows pairing.
    /// </param>
    public async Task ConnectAsync(string deviceAddress, TransportMode mode = TransportMode.Ble, CancellationToken ct = default)
    {
        switch (mode)
        {
            case TransportMode.BtClassic:
                _active = _bt;
                _bt.StateChanged += ForwardState;
                await _bt.ConnectAsync(deviceAddress, ct);
                break;

            default: // Ble
                _active = _ble;
                _ble.StateChanged += ForwardState;
                await _ble.ConnectAsync(deviceAddress, ct);
                break;
        }
    }

    public async Task DisconnectAsync() => await _active.DisconnectAsync();

    public async Task SendCommandAsync(byte cmd) => await _active.SendCommandAsync(cmd);

    private void ForwardState(object? sender, ConnectionState s) => StateChanged?.Invoke(this, s);

    public async ValueTask DisposeAsync() => await _active.DisposeAsync();
}
