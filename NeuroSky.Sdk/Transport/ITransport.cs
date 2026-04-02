using NeuroSky.Sdk.Model;

namespace NeuroSky.Sdk.Transport;

public interface ITransport : IAsyncDisposable
{
    ConnectionState State { get; }
    event EventHandler<ConnectionState> StateChanged;

    /// <summary>실시간 뇌파 데이터 스트림. CancellationToken으로 중단.</summary>
    IAsyncEnumerable<BrainWaveData> DataStream(CancellationToken ct = default);

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
