using NeuroSky.Sdk;
using Xunit;

namespace NeuroSky.Tests;

public class BlinkDetectorTests
{
    // Fixed threshold so the tests check detection logic, not the provisional default.
    private const int Threshold = 1_000;

    private long _now = 10_000;
    private readonly BlinkDetector _detector;
    private readonly List<BlinkEvent> _events = [];
    private readonly ThinkGearParser _parser;

    private static readonly int[] Flat = new int[10];

    public BlinkDetectorTests()
    {
        _detector = new BlinkDetector(threshold: Threshold, clock: () => _now);
        _parser = new ThinkGearParser(_detector, onBlink: _events.Add);
    }

    private static int[] Spike(int amplitude)
    {
        var s = new int[10];
        s[5] = amplitude;
        return s;
    }

    /// <summary>Feeds a flat signal at the ~20 ms BLE packet interval until warm-up is over.</summary>
    private void WarmUp()
    {
        _detector.Process(Flat);
        for (int i = 0; i < 30; i++) { _now += 20; _detector.Process(Flat); }
    }

    // ── BlinkDetector ────────────────────────────────────────────────────────

    [Fact]
    public void DetectsBlink_WhenPeakToPeakReachesThreshold()
    {
        WarmUp();
        _now += 20;
        Assert.Equal(Threshold, _detector.Process(Spike(Threshold)));
    }

    [Fact]
    public void IgnoresDeflection_BelowThreshold()
    {
        WarmUp();
        _now += 20;
        Assert.Equal(0, _detector.Process(Spike(Threshold - 1)));
    }

    [Fact]
    public void SuppressesDetection_DuringWarmup()
    {
        _detector.Process(Flat);
        _now += 400;
        Assert.Equal(0, _detector.Process(Spike(Threshold * 5)));
    }

    [Fact]
    public void SuppressesRepeatedDetection_WithinCooldown()
    {
        WarmUp();
        _now += 20;
        Assert.Equal(1_200, _detector.Process(Spike(1_200)));
        _now += 600;
        Assert.Equal(0, _detector.Process(Spike(1_200)));
        _now += 1;
        Assert.Equal(1_200, _detector.Process(Spike(1_200)));
    }

    [Fact]
    public void PeakToPeak_CoversWholeWindow_NotOnlyLatestPacket()
    {
        WarmUp();
        _now += 20;
        _detector.Process(Enumerable.Repeat(-600, 10).ToArray());
        _now += 20;
        Assert.Equal(1_100, _detector.Process(Enumerable.Repeat(500, 10).ToArray()));
    }

    [Fact]
    public void RearmsWarmup_AfterStreamGap()
    {
        WarmUp();
        _now += BlinkDetector.RestartGapMs + 1;
        Assert.Equal(0, _detector.Process(Spike(Threshold * 5)));
    }

    // ── ThinkGearParser → BlinkEvent ─────────────────────────────────────────

    private static byte[] RawPacket(int[] samples)
    {
        var b = new byte[20];
        for (int i = 0; i < samples.Length; i++)
        {
            b[i * 2]     = (byte)(samples[i] >> 8);
            b[i * 2 + 1] = (byte)samples[i];
        }
        return b;
    }

    private static byte[] ESense(int poorSignal)
    {
        var b = new byte[11];
        b[2] = 0xEA;
        b[6] = (byte)poorSignal;
        return b;
    }

    private void FeedRaw(int[] samples)
    {
        _now += 20;
        _parser.Parse(NeuroSkyUuid.RawEeg, RawPacket(samples));
    }

    private void WarmUpParser()
    {
        for (int i = 0; i < 31; i++) FeedRaw(Flat);
    }

    [Fact]
    public void Parser_EmitsBlinkEvent_WithStrengthAndSequence()
    {
        _parser.Parse(NeuroSkyUuid.ESense, ESense(poorSignal: 0));
        WarmUpParser();

        FeedRaw(Spike(1_500));
        for (int i = 0; i < 10; i++) FeedRaw(Flat);  // push the first spike out of the 100-sample window
        _now += 400;                                 // cooldown (600 ms) elapsed
        FeedRaw(Spike(1_300));

        Assert.Equal(new[] { 1, 2 }, _events.Select(e => e.Sequence));
        Assert.Equal(new[] { 1_500, 1_300 }, _events.Select(e => e.Strength));
    }

    [Fact]
    public void Parser_DoesNotDetect_BeforeSignalQualityIsKnown()
    {
        WarmUpParser();
        FeedRaw(Spike(Threshold * 5));
        Assert.Empty(_events);
    }

    [Fact]
    public void Parser_DoesNotDetect_WhileSignalIsPoor()
    {
        _parser.Parse(NeuroSkyUuid.ESense, ESense(poorSignal: 200));
        WarmUpParser();
        FeedRaw(Spike(Threshold * 5));
        Assert.Empty(_events);
    }

    [Fact]
    public void Parser_RearmsWarmup_WhenSignalRecovers()
    {
        _parser.Parse(NeuroSkyUuid.ESense, ESense(poorSignal: 200));
        WarmUpParser();
        _parser.Parse(NeuroSkyUuid.ESense, ESense(poorSignal: 0));

        FeedRaw(Spike(Threshold * 5));  // right after recovery = still warming up
        Assert.Empty(_events);

        WarmUpParser();
        FeedRaw(Spike(1_500));
        Assert.Single(_events);
    }

    [Fact]
    public void Parser_Reset_RestartsSequence()
    {
        _parser.Parse(NeuroSkyUuid.ESense, ESense(poorSignal: 0));
        WarmUpParser();
        FeedRaw(Spike(1_500));

        _parser.Reset();
        _parser.Parse(NeuroSkyUuid.ESense, ESense(poorSignal: 0));
        WarmUpParser();
        FeedRaw(Spike(1_500));

        Assert.Equal(new[] { 1, 1 }, _events.Select(e => e.Sequence));
    }
}
