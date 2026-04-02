using NeuroSky.Sdk.Model;
using NeuroSky.Sdk.Transport;

namespace NeuroSky.Sdk;

/// <summary>
/// Entry point for the NeuroSky MindWave Windows SDK.
/// Tries BLE first; automatically falls back to BT Classic if not connected within 5 seconds.
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
    /// <see cref="TransportMode.Auto"/> — BLE first, falls back to BT Classic (default).<br/>
    /// <see cref="TransportMode.Ble"/> — BLE only, no pairing required.<br/>
    /// <see cref="TransportMode.BtClassic"/> — BT Classic only, requires Windows pairing.
    /// </param>
    public async Task ConnectAsync(string deviceAddress, TransportMode mode = TransportMode.Auto, CancellationToken ct = default)
    {
        switch (mode)
        {
            case TransportMode.Ble:
                _active = _ble;
                _ble.StateChanged += ForwardState;
                await _ble.ConnectAsync(deviceAddress, ct);
                break;

            case TransportMode.BtClassic:
                _active = _bt;
                _bt.StateChanged += ForwardState;
                await _bt.ConnectAsync(deviceAddress, ct);
                break;

            default: // Auto: BLE first, BT Classic fallback
                using (var bleCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    bleCts.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        _active = _ble;
                        _ble.StateChanged += ForwardState;
                        await _ble.ConnectAsync(deviceAddress, bleCts.Token);
                        if (_ble.State == ConnectionState.Connected) return;
                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        _ble.StateChanged -= ForwardState;
                    }
                }

                // BT Classic fallback
                await _ble.DisconnectAsync();
                _active = _bt;
                _bt.StateChanged += ForwardState;
                await _bt.ConnectAsync(deviceAddress, ct);
                break;
        }
    }

    public async Task DisconnectAsync() => await _active.DisconnectAsync();

    public async Task SendCommandAsync(byte cmd) => await _active.SendCommandAsync(cmd);

    private void ForwardState(object? sender, ConnectionState s) => StateChanged?.Invoke(this, s);

    public async ValueTask DisposeAsync() => await _active.DisposeAsync();
}
