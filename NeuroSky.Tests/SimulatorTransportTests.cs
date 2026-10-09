using NeuroSky.Sdk;
using Xunit;

namespace NeuroSky.Tests;

public class SimulatorTransportTests
{
    private const int Samples = 200;

    private static List<BrainWaveData> Draw(SimulatorTransport.Mode mode, int seed = SimulatorTransport.DefaultSeed)
    {
        var sim = new SimulatorTransport(seed);
        sim.SetMode(mode);
        return Enumerable.Range(0, Samples).Select(_ => sim.Generate()).ToList();
    }

    // ── Connection lifecycle ────────────────────────────────────────────────

    [Fact]
    public async Task Connect_TransitionsConnectingThenConnected()
    {
        var sim = new SimulatorTransport();
        var states = new List<ConnectionState>();
        sim.StateChanged += (_, s) => states.Add(s);

        await sim.ConnectAsync("any");

        Assert.Equal(new[] { ConnectionState.Connecting, ConnectionState.Connected }, states);
        Assert.Equal(ConnectionState.Connected, sim.State);
    }

    [Fact]
    public async Task Disconnect_SetsDisconnected()
    {
        var sim = new SimulatorTransport();
        await sim.ConnectAsync("any");

        await sim.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, sim.State);
    }

    [Fact]
    public async Task DataStream_YieldsImmediately_AndStopsOnCancel()
    {
        var sim = new SimulatorTransport();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        BrainWaveData? first = null;
        await foreach (var data in sim.DataStream(cts.Token))
        {
            first = data;
            break;
        }

        Assert.NotNull(first);
        Assert.Equal(10, first!.RawEeg.Count);
    }

    [Fact]
    public async Task SendCommand_IsNoOp()
    {
        var sim = new SimulatorTransport();
        await sim.SendCommandAsync(NeuroSkyCommand.StartRawEeg);
        Assert.Equal(ConnectionState.Disconnected, sim.State);
    }

    // ── Determinism ─────────────────────────────────────────────────────────

    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var a = Draw(SimulatorTransport.Mode.Random);
        var b = Draw(SimulatorTransport.Mode.Random);

        Assert.Equal(a.Select(d => d.Attention), b.Select(d => d.Attention));
        Assert.Equal(a.SelectMany(d => d.RawEeg), b.SelectMany(d => d.RawEeg));
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentSequence()
    {
        var a = Draw(SimulatorTransport.Mode.Random, seed: 1);
        var b = Draw(SimulatorTransport.Mode.Random, seed: 2);

        Assert.NotEqual(a.Select(d => d.Attention), b.Select(d => d.Attention));
    }

    // ── Mode ranges ─────────────────────────────────────────────────────────

    [Fact]
    public void Focused_HighAttention_MidMeditation_GoodSignal()
    {
        foreach (var d in Draw(SimulatorTransport.Mode.Focused))
        {
            Assert.InRange(d.Attention, 70, 99);
            Assert.InRange(d.Meditation, 40, 59);
            Assert.Equal(SignalQuality.Good, d.SignalQuality);
        }
    }

    [Fact]
    public void Relaxed_LowAttention_HighMeditation_GoodSignal()
    {
        foreach (var d in Draw(SimulatorTransport.Mode.Relaxed))
        {
            Assert.InRange(d.Attention, 20, 49);
            Assert.InRange(d.Meditation, 70, 99);
            Assert.Equal(SignalQuality.Good, d.SignalQuality);
        }
    }

    [Fact]
    public void PoorSignal_PoorQuality_NoESense()
    {
        foreach (var d in Draw(SimulatorTransport.Mode.PoorSignal))
        {
            Assert.InRange(d.PoorSignal, 150, 199);
            Assert.Equal(SignalQuality.Poor, d.SignalQuality);
            Assert.Equal(0, d.Attention);
            Assert.Equal(0, d.Meditation);
        }
    }

    [Fact]
    public void Random_StaysWithinDocumentedRanges()
    {
        foreach (var d in Draw(SimulatorTransport.Mode.Random))
        {
            Assert.InRange(d.PoorSignal, 0, 29);
            Assert.InRange(d.Attention, 0, 99);
            Assert.InRange(d.Meditation, 0, 99);
            Assert.Equal(10, d.RawEeg.Count);
            Assert.All(d.RawEeg, v => Assert.InRange(v, -2048, 2047));
        }
    }
}
