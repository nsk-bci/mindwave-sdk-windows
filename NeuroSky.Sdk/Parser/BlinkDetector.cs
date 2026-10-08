namespace NeuroSky.Sdk;

/// <summary>
/// Detects eye blinks in the raw EEG stream.
/// </summary>
/// <remarks>
/// A blink shows up as a short, large deflection in raw EEG. The detector keeps the most recent
/// <see cref="WindowSize"/> samples (about 200 ms at 512 Hz) and reports a blink when their
/// peak-to-peak amplitude reaches <see cref="Threshold"/>, at most once per <see cref="CooldownMs"/>.
/// For <see cref="WarmupMs"/> after the stream starts (or resumes after a gap longer than
/// <see cref="RestartGapMs"/>) detection is suppressed so settling transients are not counted.
/// Thresholds use the same units as BLE <see cref="BrainWaveData.RawEeg"/> values.
/// The web tutorial's 600 is in Web Bluetooth raw units (about 1/5 of native) and over-detects here;
/// <see cref="DefaultThreshold"/> is provisional until measured on a device.
/// </remarks>
public sealed class BlinkDetector
{
    /// <summary>
    /// Provisional threshold — the web tutorial value (600) converted to native units.
    /// Not for release until confirmed by on-device measurement (30 deliberate blinks + 1 minute without blinking).
    /// </summary>
    public const int DefaultThreshold = 3_000;

    /// <summary>A gap longer than this between samples is treated as a stream restart.</summary>
    public const long RestartGapMs = 1_000;
    private const int MinSamples = 8;
    private const long Never = long.MinValue / 2;

    private readonly int[] _window;
    private readonly Func<long> _clock;
    private int _count;
    private int _head;
    private long _armedAt;
    private long _lastSampleAt;
    private long _lastBlinkAt = Never;

    public int WindowSize { get; }
    public int Threshold { get; }
    public long CooldownMs { get; }
    public long WarmupMs { get; }

    public BlinkDetector(
        int windowSize = 100,
        int threshold = DefaultThreshold,
        long cooldownMs = 600,
        long warmupMs = 500,
        Func<long>? clock = null)
    {
        if (windowSize < MinSamples)
            throw new ArgumentOutOfRangeException(nameof(windowSize), $"windowSize must be >= {MinSamples}");

        WindowSize = windowSize;
        Threshold = threshold;
        CooldownMs = cooldownMs;
        WarmupMs = warmupMs;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _window = new int[windowSize];
    }

    /// <summary>Feeds new raw samples and checks for a blink.</summary>
    /// <returns>The window's peak-to-peak amplitude if a blink was detected; otherwise 0.</returns>
    public int Process(IReadOnlyList<int> samples)
    {
        if (samples.Count == 0) return 0;
        long now = _clock();
        if (_count == 0 || now - _lastSampleAt > RestartGapMs) Arm(now);
        _lastSampleAt = now;

        foreach (int s in samples)
        {
            _window[_head] = s;
            _head = (_head + 1) % WindowSize;
            if (_count < WindowSize) _count++;
        }

        if (_count < MinSamples) return 0;
        if (now - _armedAt < WarmupMs) return 0;
        if (now - _lastBlinkAt <= CooldownMs) return 0;

        int min = int.MaxValue, max = int.MinValue;
        for (int i = 0; i < _count; i++)
        {
            int v = _window[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }
        int peakToPeak = max - min;
        if (peakToPeak < Threshold) return 0;

        _lastBlinkAt = now;
        return peakToPeak;
    }

    /// <summary>Clears the window. Warm-up restarts with the next sample.</summary>
    public void Reset()
    {
        _count = 0;
        _head = 0;
    }

    private void Arm(long now)
    {
        Reset();
        _armedAt = now;
        _lastBlinkAt = Never;
    }
}
