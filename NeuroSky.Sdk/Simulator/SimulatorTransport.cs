using System.Runtime.CompilerServices;

namespace NeuroSky.Sdk;

/// <summary>
/// Test-only transport that generates synthetic <see cref="BrainWaveData"/> once per second.
/// Internal: visible to NeuroSky.Tests only. Uses a fixed seed, so a given seed and mode
/// always produce the same sequence.
/// </summary>
internal sealed class SimulatorTransport : ITransport
{
    public enum Mode { Random, Focused, Relaxed, PoorSignal }

    public const int DefaultSeed = 7;

    private Mode _mode = Mode.Random;
    private readonly Random _rng;

    public SimulatorTransport(int seed = DefaultSeed)
    {
        _rng = new Random(seed);
    }

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

    // Internal so tests can draw samples without the 1 s stream delay.
    internal BrainWaveData Generate() => _mode switch
    {
        Mode.Focused => new BrainWaveData
        {
            PoorSignal = 0,
            Attention  = _rng.Next(70, 100),
            Meditation = _rng.Next(40, 60),
            Delta      = _rng.Next(10_000, 50_000),
            Theta      = _rng.Next(5_000, 20_000),
            LowAlpha   = _rng.Next(3_000, 10_000),
            HighAlpha  = _rng.Next(3_000, 10_000),
            LowBeta    = _rng.Next(15_000, 40_000),
            HighBeta   = _rng.Next(10_000, 30_000),
            LowGamma   = _rng.Next(5_000, 15_000),
            MidGamma   = _rng.Next(5_000, 15_000),
            RawEeg     = Enumerable.Range(0, 10).Select(_ => _rng.Next(-2048, 2048)).ToList()
        },
        Mode.Relaxed => new BrainWaveData
        {
            PoorSignal = 0,
            Attention  = _rng.Next(20, 50),
            Meditation = _rng.Next(70, 100),
            Delta      = _rng.Next(20_000, 80_000),
            Theta      = _rng.Next(15_000, 40_000),
            LowAlpha   = _rng.Next(10_000, 30_000),
            HighAlpha  = _rng.Next(10_000, 30_000),
            LowBeta    = _rng.Next(3_000, 10_000),
            HighBeta   = _rng.Next(3_000, 10_000),
            LowGamma   = _rng.Next(2_000, 8_000),
            MidGamma   = _rng.Next(2_000, 8_000),
            RawEeg     = Enumerable.Range(0, 10).Select(_ => _rng.Next(-1024, 1024)).ToList()
        },
        Mode.PoorSignal => new BrainWaveData
        {
            PoorSignal = _rng.Next(150, 200),
            RawEeg     = Enumerable.Range(0, 10).Select(_ => _rng.Next(-4096, 4096)).ToList()
        },
        _ => new BrainWaveData   // Random
        {
            PoorSignal = _rng.Next(0, 30),
            Attention  = _rng.Next(0, 100),
            Meditation = _rng.Next(0, 100),
            Delta      = _rng.Next(0, 100_000),
            Theta      = _rng.Next(0, 100_000),
            LowAlpha   = _rng.Next(0, 50_000),
            HighAlpha  = _rng.Next(0, 50_000),
            LowBeta    = _rng.Next(0, 50_000),
            HighBeta   = _rng.Next(0, 50_000),
            LowGamma   = _rng.Next(0, 30_000),
            MidGamma   = _rng.Next(0, 30_000),
            RawEeg     = Enumerable.Range(0, 10).Select(_ => _rng.Next(-2048, 2048)).ToList()
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
