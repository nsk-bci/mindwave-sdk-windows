using Windows.Devices.Bluetooth.Advertisement;

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

    /// <summary>
    /// Scans for a MindWave headset by name and returns its MAC address.
    /// Cache the result locally — repeat scans add latency on every launch.
    /// </summary>
    /// <param name="deviceName">BLE advertisement name to match (e.g. "MindWave Mobile")</param>
    /// <param name="timeoutMs">Scan timeout in milliseconds (default: 10 000)</param>
    /// <returns>MAC address string "AA:BB:CC:DD:EE:FF", or <c>null</c> if not found within timeout.</returns>
    public async Task<string?> FindDeviceAddressAsync(string deviceName, int timeoutMs = 10_000, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<string?>();
        var watcher = new BluetoothLEAdvertisementWatcher();

        watcher.Received += (_, args) =>
        {
            if (args.Advertisement.LocalName == deviceName)
            {
                ulong addr = args.BluetoothAddress;
                var mac = string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                    (addr >> 40) & 0xFF, (addr >> 32) & 0xFF, (addr >> 24) & 0xFF,
                    (addr >> 16) & 0xFF, (addr >> 8) & 0xFF, addr & 0xFF);
                watcher.Stop();
                tcs.TrySetResult(mac);
            }
        };

        watcher.Stopped += (_, _) => tcs.TrySetResult(null);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeoutMs);
        linked.Token.Register(() => { watcher.Stop(); tcs.TrySetResult(null); });

        watcher.Start();
        return await tcs.Task;
    }

    public async Task DisconnectAsync() => await _active.DisconnectAsync();

    public async Task SendCommandAsync(byte cmd) => await _active.SendCommandAsync(cmd);

    private void ForwardState(object? sender, ConnectionState s) => StateChanged?.Invoke(this, s);

    public async ValueTask DisposeAsync() => await _active.DisposeAsync();
}
