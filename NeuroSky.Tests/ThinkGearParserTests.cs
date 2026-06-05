using NeuroSky.Sdk;
using Xunit;

namespace NeuroSky.Tests;

public class ThinkGearParserTests
{
    private readonly ThinkGearParser _parser = new();

    // ── BLE Mode: ESense (0xEA) ──────────────────────────────────────────────

    [Fact]
    public void ParseESense_0xEA_ReturnsAttentionMeditationPoorSignal()
    {
        var bytes = new byte[11];
        bytes[2]  = 0xEA; // 패킷 타입은 2바이트 프리픽스(00 00) 뒤, index 2
        bytes[6]  = 10;   // PoorSignal
        bytes[8]  = 75;   // Attention
        bytes[10] = 55;   // Meditation

        var result = _parser.Parse(NeuroSkyUuid.ESense, bytes);

        Assert.NotNull(result);
        Assert.Equal(10, result!.PoorSignal);
        Assert.Equal(75, result.Attention);
        Assert.Equal(55, result.Meditation);
    }

    [Fact]
    public void ParseESense_0xEA_TooShort_ReturnsNull()
    {
        var bytes = new byte[5];
        bytes[2] = 0xEA;

        var result = _parser.Parse(NeuroSkyUuid.ESense, bytes);

        Assert.Null(result);
    }

    // ── BLE Mode: ESense (0xEB) ──────────────────────────────────────────────

    [Fact]
    public void ParseESense_0xEB_ReturnsDeltaThetaAlpha()
    {
        var bytes = new byte[20];
        bytes[2] = 0xEB; // 타입은 index 2 (프리픽스 뒤)
        // Delta at bytes[5~7]
        bytes[5] = 0x00; bytes[6] = 0x01; bytes[7] = 0x00;   // 256
        // Theta at bytes[9~11]
        bytes[9] = 0x00; bytes[10] = 0x02; bytes[11] = 0x00;  // 512
        // LowAlpha at bytes[13~15]
        bytes[13] = 0x00; bytes[14] = 0x03; bytes[15] = 0x00; // 768
        // HighAlpha at bytes[17~19]
        bytes[17] = 0x00; bytes[18] = 0x04; bytes[19] = 0x00; // 1024

        var result = _parser.Parse(NeuroSkyUuid.ESense, bytes);

        Assert.NotNull(result);
        Assert.Equal(256, result!.Delta);
        Assert.Equal(512, result.Theta);
        Assert.Equal(768, result.LowAlpha);
        Assert.Equal(1024, result.HighAlpha);
    }

    // ── BLE Mode: ESense (0xEC) ──────────────────────────────────────────────

    [Fact]
    public void ParseESense_0xEC_ReturnsBetaGamma()
    {
        var bytes = new byte[20];
        bytes[2] = 0xEC; // 타입은 index 2 (프리픽스 뒤)
        bytes[5]  = 0x00; bytes[6]  = 0x05; bytes[7]  = 0x00; // LowBeta  = 1280
        bytes[9]  = 0x00; bytes[10] = 0x06; bytes[11] = 0x00; // HighBeta = 1536
        bytes[13] = 0x00; bytes[14] = 0x07; bytes[15] = 0x00; // LowGamma = 1792
        bytes[17] = 0x00; bytes[18] = 0x08; bytes[19] = 0x00; // MidGamma = 2048

        var result = _parser.Parse(NeuroSkyUuid.ESense, bytes);

        Assert.NotNull(result);
        Assert.Equal(1280, result!.LowBeta);
        Assert.Equal(1536, result.HighBeta);
        Assert.Equal(1792, result.LowGamma);
        Assert.Equal(2048, result.MidGamma);
    }

    // ── BLE Mode: ESense — 실기기 캡처 회귀 테스트 (2026-06-05, MWM2) ──────────
    // 실제 MWM2 BLE 가 보내는 20바이트 페이로드(2바이트 프리픽스 + type@2).
    // bytes[0] 기반 파서였다면 모두 null 을 반환해 Att/Med 가 0 으로 나오는 버그를 재현/검출한다.

    [Fact]
    public void ParseESense_0xEA_RealDevicePacket_ParsesAttentionMeditation()
    {
        // 00 00 EA 00 00 02 <poor> 04 <att> 05 <med> ...  (코드 0x02/0x04/0x05 뒤 값)
        byte[] bytes =
        {
            0x00, 0x00, 0xEA, 0x00, 0x00, 0x02, 0x00, 0x04, 0x40, 0x05,
            0x2F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        var result = _parser.Parse(NeuroSkyUuid.ESense, bytes);

        Assert.NotNull(result);
        Assert.Equal(0,  result!.PoorSignal);
        Assert.Equal(64, result.Attention);   // 0x40
        Assert.Equal(47, result.Meditation);  // 0x2F
    }

    [Fact]
    public void ParseESense_0xEB_RealDevicePacket_ParsesBands()
    {
        // 실제 캡처: 00 00 EB 00 06 03 E6 30 07 00 81 BA 08 00 13 40 09 00 27 4E
        byte[] bytes =
        {
            0x00, 0x00, 0xEB, 0x00, 0x06, 0x03, 0xE6, 0x30, 0x07, 0x00,
            0x81, 0xBA, 0x08, 0x00, 0x13, 0x40, 0x09, 0x00, 0x27, 0x4E
        };

        var result = _parser.Parse(NeuroSkyUuid.ESense, bytes);

        Assert.NotNull(result);
        Assert.Equal(0x03E630, result!.Delta);
        Assert.Equal(0x0081BA, result.Theta);
        Assert.Equal(0x001340, result.LowAlpha);
        Assert.Equal(0x00274E, result.HighAlpha);
    }

    // ── BLE Mode: RawEEG ────────────────────────────────────────────────────

    [Fact]
    public void ParseRawEeg_Returns10Samples()
    {
        var bytes = new byte[20];
        // Sample 0: 0x01 0x00 = 256
        bytes[0] = 0x01; bytes[1] = 0x00;

        var result = _parser.Parse(NeuroSkyUuid.RawEeg, bytes);

        Assert.NotNull(result);
        Assert.Equal(10, result!.RawEeg.Count);
        Assert.Equal(256, result.RawEeg[0]);
    }

    [Fact]
    public void ParseRawEeg_SignedConversion_NegativeValue()
    {
        var bytes = new byte[20];
        // 0x80 0x01 = 32769 → > 32768 → 32769 - 65536 = -32767
        bytes[0] = 0x80; bytes[1] = 0x01;

        var result = _parser.Parse(NeuroSkyUuid.RawEeg, bytes);

        Assert.NotNull(result);
        Assert.Equal(-32767, result!.RawEeg[0]);
    }

    [Fact]
    public void ParseRawEeg_TooShort_ReturnsNull()
    {
        var result = _parser.Parse(NeuroSkyUuid.RawEeg, new byte[10]);
        Assert.Null(result);
    }

    // ── BLE Mode: Unknown UUID ───────────────────────────────────────────────

    [Fact]
    public void Parse_UnknownUuid_ReturnsNull()
    {
        var result = _parser.Parse(Guid.NewGuid(), new byte[20]);
        Assert.Null(result);
    }

    // ── BT Classic Mode ──────────────────────────────────────────────────────

    [Fact]
    public void ParseByte_ValidPacket_ReturnsAttentionMeditation()
    {
        // Build a valid BT Classic packet:
        // [0xAA][0xAA][length][0x04][attention][0x05][meditation][checksum]
        byte attention  = 80;
        byte meditation = 60;
        byte[] payload  = [0x04, attention, 0x05, meditation];
        byte checksum   = (byte)(((payload.Sum(b => b) & 0xFF) ^ 0xFF) & 0xFF);

        var packet = new byte[]
        {
            0xAA, 0xAA,
            (byte)payload.Length,
            0x04, attention,
            0x05, meditation,
            checksum
        };

        var parser = new ThinkGearParser();
        BrainWaveData? result = null;
        foreach (var b in packet)
            result = parser.ParseByte(b) ?? result;

        Assert.NotNull(result);
        Assert.Equal(attention,  result!.Attention);
        Assert.Equal(meditation, result.Meditation);
    }

    [Fact]
    public void ParseByte_InvalidChecksum_ReturnsNull()
    {
        byte[] payload = [0x04, 80];
        var packet = new byte[]
        {
            0xAA, 0xAA,
            (byte)payload.Length,
            0x04, 80,
            0xFF  // wrong checksum
        };

        var parser = new ThinkGearParser();
        BrainWaveData? result = null;
        foreach (var b in packet)
            result = parser.ParseByte(b) ?? result;

        Assert.Null(result);
    }

    [Fact]
    public void ParseByte_PoorSignalCode_ReturnsPoorSignal()
    {
        byte poorSignal = 150;
        byte[] payload  = [0x02, poorSignal];
        byte checksum   = (byte)(((payload.Sum(b => b) & 0xFF) ^ 0xFF) & 0xFF);

        var packet = new byte[]
        {
            0xAA, 0xAA,
            (byte)payload.Length,
            0x02, poorSignal,
            checksum
        };

        var parser = new ThinkGearParser();
        BrainWaveData? result = null;
        foreach (var b in packet)
            result = parser.ParseByte(b) ?? result;

        Assert.NotNull(result);
        Assert.Equal(poorSignal, result!.PoorSignal);
    }

    // ── SignalQuality ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(200, "NoSignal")]
    [InlineData(100, "Poor")]
    [InlineData(25,  "Fair")]
    [InlineData(0,   "Good")]
    public void SignalQuality_Thresholds(int poorSignal, string expected)
    {
        var bytes = new byte[11];
        bytes[2] = 0xEA; // 타입은 index 2 (프리픽스 뒤)
        bytes[6] = (byte)poorSignal;

        var result = _parser.Parse(NeuroSkyUuid.ESense, bytes);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.SignalQuality.ToString());
    }
}
