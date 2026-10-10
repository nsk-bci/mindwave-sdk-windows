using Windows.Devices.Bluetooth.Advertisement;

namespace NeuroSky.Sdk;

/// <summary>
/// Entry point for the NeuroSky MindWave Windows SDK.
/// Talks to the MindWave Mobile 2 over BLE.
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

    public ConnectionState State => _ble.State;
    public event EventHandler<ConnectionState>? StateChanged;

    public NeuroSkySdk()
    {
        _ble.StateChanged += (_, s) => StateChanged?.Invoke(this, s);
    }

    /// <summary>Real-time EEG data stream.</summary>
    public IAsyncEnumerable<BrainWaveData> DataStream(CancellationToken ct = default)
        => _ble.DataStream(ct);

    /// <summary>
    /// Eye blink events, one per detected blink. Detection runs on raw EEG, so enable it with
    /// <see cref="NeuroSkyCommand.StartRawEeg"/>; nothing is emitted while signal quality is Poor or NoSignal.
    /// </summary>
    public IAsyncEnumerable<BlinkEvent> BlinkStream(CancellationToken ct = default)
        => _ble.BlinkStream(ct);

    /// <summary>
    /// Connect to a MindWave headset over BLE. No pairing required.
    /// </summary>
    /// <param name="deviceAddress">Bluetooth MAC address (e.g. "AA:BB:CC:DD:EE:FF")</param>
    public Task ConnectAsync(string deviceAddress, CancellationToken ct = default)
        => _ble.ConnectAsync(deviceAddress, ct);

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
                // TrySetResult 먼저 → Stop 나중: Stopped 이벤트가 null로 덮어쓰는 race 방지
                if (tcs.TrySetResult(mac))
                    watcher.Stop();
            }
        };

        watcher.Stopped += (_, _) => tcs.TrySetResult(null);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeoutMs);
        linked.Token.Register(() => { watcher.Stop(); tcs.TrySetResult(null); });

        watcher.Start();
        return await tcs.Task;
    }

    /// <summary>Raw notifications before parsing. Internal: packet-capture tool only.</summary>
    internal event Action<Guid, byte[]>? PacketReceived
    {
        add => _ble.PacketReceived += value;
        remove => _ble.PacketReceived -= value;
    }

    public async Task DisconnectAsync() => await _ble.DisconnectAsync();

    public async Task SendCommandAsync(byte cmd) => await _ble.SendCommandAsync(cmd);

    public async ValueTask DisposeAsync() => await _ble.DisposeAsync();
}
