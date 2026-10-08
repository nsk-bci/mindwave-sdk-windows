namespace NeuroSky.Sdk;

public interface ITransport : IAsyncDisposable
{
    ConnectionState State { get; }
    event EventHandler<ConnectionState> StateChanged;

    /// <summary>Real-time EEG data stream. Cancel via CancellationToken.</summary>
    IAsyncEnumerable<BrainWaveData> DataStream(CancellationToken ct = default);

    /// <summary>Eye blink events. Transports that do not detect blinks (e.g. the simulator) yield nothing.</summary>
    IAsyncEnumerable<BlinkEvent> BlinkStream(CancellationToken ct = default);

    Task ConnectAsync(string deviceAddress, CancellationToken ct = default);
    Task DisconnectAsync();
    Task SendCommandAsync(byte cmd);
}

public enum ConnectionState
{
    Disconnected,
    Scanning,
    Connecting,
    Connected,
    Error
}
