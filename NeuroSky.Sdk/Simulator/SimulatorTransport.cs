using System.Runtime.CompilerServices;
using NeuroSky.Sdk.Model;
using NeuroSky.Sdk.Transport;

namespace NeuroSky.Sdk.Simulator;

public sealed class SimulatorTransport : ITransport
{
    public enum Mode { Random, Focused, Relaxed, PoorSignal }

    private Mode _mode = Mode.Random;
    private static readonly Random Rng = Random.Shared;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event EventHandler<ConnectionState>? StateChanged;

    public void SetMode(Mode mode) => _mode = mode;

    public async IAsyncEnumerable<BrainWaveData> DataStream(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            yield return Generate();
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }

    public async Task ConnectAsync(string deviceAddress, CancellationToken ct = default)
    {
        SetState(ConnectionState.Connecting);
        await Task.Delay(500, ct);
        SetState(ConnectionState.Connected);
    }

    public Task DisconnectAsync()
    {
        SetState(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public Task SendCommandAsync(byte cmd) => Task.CompletedTask;

    private BrainWaveData Generate() => _mode switch
    {
        Mode.Focused => new BrainWaveData
        {
            PoorSignal = 0,
            Attention  = Rng.Next(70, 100),
            Meditation = Rng.Next(40, 60),
            Delta      = Rng.Next(10_000, 50_000),
            Theta      = Rng.Next(5_000, 20_000),
            LowAlpha   = Rng.Next(3_000, 10_000),
            HighAlpha  = Rng.Next(3_000, 10_000),
            LowBeta    = Rng.Next(15_000, 40_000),
            HighBeta   = Rng.Next(10_000, 30_000),
            LowGamma   = Rng.Next(5_000, 15_000),
            MidGamma   = Rng.Next(5_000, 15_000),
            RawEeg     = Enumerable.Range(0, 10).Select(_ => Rng.Next(-2048, 2048)).ToList()
        },
        Mode.Relaxed => new BrainWaveData
        {
            PoorSignal = 0,
            Attention  = Rng.Next(20, 50),
            Meditation = Rng.Next(70, 100),
            Delta      = Rng.Next(20_000, 80_000),
            Theta      = Rng.Next(15_000, 40_000),
            LowAlpha   = Rng.Next(10_000, 30_000),
            HighAlpha  = Rng.Next(10_000, 30_000),
            LowBeta    = Rng.Next(3_000, 10_000),
            HighBeta   = Rng.Next(3_000, 10_000),
            LowGamma   = Rng.Next(2_000, 8_000),
            MidGamma   = Rng.Next(2_000, 8_000),
            RawEeg     = Enumerable.Range(0, 10).Select(_ => Rng.Next(-1024, 1024)).ToList()
        },
        Mode.PoorSignal => new BrainWaveData
        {
            PoorSignal = Rng.Next(150, 200),
            RawEeg     = Enumerable.Range(0, 10).Select(_ => Rng.Next(-4096, 4096)).ToList()
        },
        _ => new BrainWaveData   // Random
        {
            PoorSignal = Rng.Next(0, 30),
            Attention  = Rng.Next(0, 100),
            Meditation = Rng.Next(0, 100),
            Delta      = Rng.Next(0, 100_000),
            Theta      = Rng.Next(0, 100_000),
            LowAlpha   = Rng.Next(0, 50_000),
            HighAlpha  = Rng.Next(0, 50_000),
            LowBeta    = Rng.Next(0, 50_000),
            HighBeta   = Rng.Next(0, 50_000),
            LowGamma   = Rng.Next(0, 30_000),
            MidGamma   = Rng.Next(0, 30_000),
            RawEeg     = Enumerable.Range(0, 10).Select(_ => Rng.Next(-2048, 2048)).ToList()
        }
    };

    private void SetState(ConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public ValueTask DisposeAsync()
    {
        DisconnectAsync();
        return ValueTask.CompletedTask;
    }
}
