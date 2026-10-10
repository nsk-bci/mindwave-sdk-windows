namespace NeuroSky.Sdk;

/// <summary>
/// NeuroSky ThinkGear packet parser.
/// Feed BLE characteristic notifications into <see cref="Parse"/>.
/// Raw EEG samples also run through a <see cref="BlinkDetector"/>; each detected blink is passed to the
/// <c>onBlink</c> callback as a <see cref="BlinkEvent"/>. Detection is off until the first 0xEA packet reports
/// signal quality, and while <see cref="BrainWaveData.PoorSignal"/> exceeds <c>maxPoorSignal</c> — electrode
/// contact noise looks like a blink.
/// </summary>
public sealed class ThinkGearParser
{
    /// <summary>PoorSignal above this (Poor, NoSignal) pauses blink detection.</summary>
    public const int DefaultMaxPoorSignal = 50;

    private BrainWaveData _current = new();
    private readonly BlinkDetector _blinkDetector;
    private readonly int _maxPoorSignal;
    private readonly Action<BlinkEvent>? _onBlink;
    private bool _signalKnown;
    private int _blinkSequence;

    public ThinkGearParser(
        BlinkDetector? blinkDetector = null,
        int maxPoorSignal = DefaultMaxPoorSignal,
        Action<BlinkEvent>? onBlink = null)
    {
        _blinkDetector = blinkDetector ?? new BlinkDetector();
        _maxPoorSignal = maxPoorSignal;
        _onBlink = onBlink;
    }

    /// <summary>Clears accumulated data, the blink count, and the detector for a new connection.</summary>
    public void Reset()
    {
        _current = new BrainWaveData();
        _signalKnown = false;
        _blinkSequence = 0;
        _blinkDetector.Reset();
    }

    public BrainWaveData? Parse(Guid uuid, byte[] bytes)
    {
        if (uuid == NeuroSkyUuid.ESense) return ParseESense(bytes);
        if (uuid == NeuroSkyUuid.RawEeg) return ParseRawEeg(bytes);
        return null;
    }

    private BrainWaveData? ParseESense(byte[] bytes)
    {
        // MWM2 BLE eSense 패킷은 2바이트 프리픽스(00 00) 뒤에 패킷 타입이 온다.
        // 따라서 타입은 bytes[2] 에서 읽어야 한다. (필드 오프셋 6/8/10·5/9/13/17 은 프리픽스 기준)
        // 레퍼런스 SDK(MWMleService: `byte packType = data[2]`)와 일치.
        if (bytes.Length < 3) return null;

        return (bytes[2] & 0xFF) switch
        {
            0xEA when bytes.Length >= 11 => OnSignalQuality(_current = _current with
            {
                PoorSignal = bytes[6] & 0xFF,
                Attention  = bytes[8] & 0xFF,
                Meditation = bytes[10] & 0xFF,
                Timestamp  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }),
            0xEB when bytes.Length >= 20 => _current = _current with
            {
                Delta    = Read3Bytes(bytes, 5),
                Theta    = Read3Bytes(bytes, 9),
                LowAlpha = Read3Bytes(bytes, 13),
                HighAlpha= Read3Bytes(bytes, 17)
            },
            0xEC when bytes.Length >= 20 => _current = _current with
            {
                LowBeta  = Read3Bytes(bytes, 5),
                HighBeta = Read3Bytes(bytes, 9),
                LowGamma = Read3Bytes(bytes, 13),
                MidGamma = Read3Bytes(bytes, 17),
                Timestamp= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            },
            _ => null
        };
    }

    private BrainWaveData? ParseRawEeg(byte[] bytes)
    {
        if (bytes.Length < 20) return null;

        var samples = new int[10];
        for (int i = 0; i < 10; i++)
        {
            int raw = ((bytes[i * 2] & 0xFF) << 8) | (bytes[i * 2 + 1] & 0xFF);
            if (raw >= 32768) raw -= 65536;
            samples[i] = raw;
        }

        _current = _current with
        {
            RawEeg    = samples,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        DetectBlink(samples);
        return _current;
    }

    private BrainWaveData OnSignalQuality(BrainWaveData data)
    {
        _signalKnown = true;
        return data;
    }

    private void DetectBlink(IReadOnlyList<int> samples)
    {
        if (!_signalKnown || _current.PoorSignal > _maxPoorSignal)
        {
            _blinkDetector.Reset();  // warm up again once the signal recovers
            return;
        }
        int strength = _blinkDetector.Process(samples);
        if (strength > 0) _onBlink?.Invoke(new BlinkEvent(_current.Timestamp, strength, ++_blinkSequence));
    }

    private static int Read3Bytes(byte[] bytes, int offset)
    {
        if (offset + 2 >= bytes.Length) return 0;
        return ((bytes[offset] & 0xFF) << 16)
             | ((bytes[offset + 1] & 0xFF) << 8)
             | (bytes[offset + 2] & 0xFF);
    }
}
