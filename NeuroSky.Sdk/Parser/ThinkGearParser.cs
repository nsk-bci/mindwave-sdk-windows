namespace NeuroSky.Sdk;

/// <summary>
/// NeuroSky ThinkGear packet parser.
/// BLE mode: use <see cref="Parse"/>.
/// BT Classic mode: feed stream bytes one at a time into <see cref="ParseByte"/>.
/// </summary>
public sealed class ThinkGearParser
{
    private BrainWaveData _current = new();

    // ── BLE Mode ──────────────────────────────────────────────────────────────

    public BrainWaveData? Parse(Guid uuid, byte[] bytes)
    {
        if (uuid == NeuroSkyUuid.ESense) return ParseESense(bytes);
        if (uuid == NeuroSkyUuid.RawEeg) return ParseRawEeg(bytes);
        return null;
    }

    private BrainWaveData? ParseESense(byte[] bytes)
    {
        if (bytes.Length == 0) return null;

        return (bytes[0] & 0xFF) switch
        {
            0xEA when bytes.Length >= 11 => _current = _current with
            {
                PoorSignal = bytes[6] & 0xFF,
                Attention  = bytes[8] & 0xFF,
                Meditation = bytes[10] & 0xFF,
                Timestamp  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            },
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
        return _current;
    }

    // ── BT Classic Mode (ThinkGear Serial Protocol) ────────────────────────

    private enum SerialState { Sync1, Sync2, PLength, Payload, Checksum }
    private SerialState _serialState = SerialState.Sync1;
    private int _payloadLength;
    private readonly List<byte> _payloadBuffer = [];
    private int _checksum;

    public BrainWaveData? ParseByte(byte b)
    {
        int val = b & 0xFF;
        return _serialState switch
        {
            SerialState.Sync1 => Transition(val == 0xAA, SerialState.Sync2),
            SerialState.Sync2 => Transition(val == 0xAA, SerialState.PLength, SerialState.Sync1),
            SerialState.PLength => InitPayload(val),
            SerialState.Payload => AccumulatePayload(b, val),
            SerialState.Checksum => VerifyAndParse(val),
            _ => null
        };
    }

    private BrainWaveData? Transition(bool condition, SerialState next, SerialState fallback = SerialState.Sync1)
    {
        _serialState = condition ? next : fallback;
        return null;
    }

    private BrainWaveData? InitPayload(int val)
    {
        if (val == 0xAA) return null;
        _payloadLength = val;
        _payloadBuffer.Clear();
        _checksum = 0;
        _serialState = SerialState.Payload;
        return null;
    }

    private BrainWaveData? AccumulatePayload(byte b, int val)
    {
        _payloadBuffer.Add(b);
        _checksum = (_checksum + val) & 0xFF;
        if (_payloadBuffer.Count >= _payloadLength)
            _serialState = SerialState.Checksum;
        return null;
    }

    private BrainWaveData? VerifyAndParse(int val)
    {
        _serialState = SerialState.Sync1;
        int expected = (_checksum ^ 0xFF) & 0xFF;
        return val == expected ? ParseSerialPayload([.. _payloadBuffer]) : null;
    }

    private BrainWaveData? ParseSerialPayload(byte[] payload)
    {
        int i = 0;
        while (i < payload.Length)
        {
            int code = payload[i++] & 0xFF;
            switch (code)
            {
                case 0x02: if (i < payload.Length) _current = _current with { PoorSignal = payload[i++] & 0xFF }; break;
                case 0x04: if (i < payload.Length) _current = _current with { Attention  = payload[i++] & 0xFF }; break;
                case 0x05: if (i < payload.Length) _current = _current with { Meditation = payload[i++] & 0xFF }; break;
                case 0x16: if (i < payload.Length) _current = _current with { EyeBlink   = payload[i++] & 0xFF }; break;
                case 0x80:
                {
                    if (i >= payload.Length) break;
                    int len = payload[i++] & 0xFF;
                    if (i + len <= payload.Length && len >= 2)
                    {
                        int raw = ((payload[i] & 0xFF) << 8) | (payload[i + 1] & 0xFF);
                        if (raw >= 32768) raw -= 65536;
                        _current = _current with { RawEeg = [raw] };
                    }
                    i += len;
                    break;
                }
                case 0x83:
                {
                    if (i >= payload.Length) break;
                    int len = payload[i++] & 0xFF;
                    if (i + len <= payload.Length && len >= 24)
                    {
                        _current = _current with
                        {
                            Delta     = Read3Bytes(payload, i),
                            Theta     = Read3Bytes(payload, i + 3),
                            LowAlpha  = Read3Bytes(payload, i + 6),
                            HighAlpha = Read3Bytes(payload, i + 9),
                            LowBeta   = Read3Bytes(payload, i + 12),
                            HighBeta  = Read3Bytes(payload, i + 15),
                            LowGamma  = Read3Bytes(payload, i + 18),
                            MidGamma  = Read3Bytes(payload, i + 21)
                        };
                        i += len;
                    }
                    break;
                }
                default:
                    if (code >= 0x80) { if (i < payload.Length) { int len = payload[i++] & 0xFF; i += len; } }
                    else              { if (i < payload.Length) i++; }
                    break;
            }
        }
        return _current with { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
    }

    private static int Read3Bytes(byte[] bytes, int offset)
    {
        if (offset + 2 >= bytes.Length) return 0;
        return ((bytes[offset] & 0xFF) << 16)
             | ((bytes[offset + 1] & 0xFF) << 8)
             | (bytes[offset + 2] & 0xFF);
    }
}
