using NeuroSky.Sdk.Model;
using NeuroSky.Sdk.Transport;

namespace NeuroSky.Sdk;

/// <summary>
/// NeuroSky MindWave Windows SDK 진입점.
/// BLE를 우선 시도하고, 5초 내 실패 시 BT Classic으로 자동 폴백.
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

    /// <summary>실시간 뇌파 데이터 스트림.</summary>
    public IAsyncEnumerable<BrainWaveData> DataStream(CancellationToken ct = default)
        => _active.DataStream(ct);

    /// <summary>
    /// BLE 우선 연결. 5초 내 연결 실패 시 BT Classic 자동 폴백.
    /// </summary>
    /// <param name="deviceAddress">Bluetooth MAC 주소 (예: "AA:BB:CC:DD:EE:FF")</param>
    public async Task ConnectAsync(string deviceAddress, CancellationToken ct = default)
    {
        // BLE 시도
        using var bleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
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

        // BT Classic 폴백
        await _ble.DisconnectAsync();
        _active = _bt;
        _bt.StateChanged += ForwardState;
        await _bt.ConnectAsync(deviceAddress, ct);
    }

    public async Task DisconnectAsync() => await _active.DisconnectAsync();

    public async Task SendCommandAsync(byte cmd) => await _active.SendCommandAsync(cmd);

    private void ForwardState(object? sender, ConnectionState s) => StateChanged?.Invoke(this, s);

    public async ValueTask DisposeAsync() => await _active.DisposeAsync();
}
