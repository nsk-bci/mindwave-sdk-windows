namespace NeuroSky.Sdk.Model;

public record BrainWaveData
{
    public long Timestamp  { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public int PoorSignal  { get; init; } = 0;   // 0=완벽, 200=무신호
    public int Attention   { get; init; } = 0;   // 0~100
    public int Meditation  { get; init; } = 0;   // 0~100
    public int Delta       { get; init; } = 0;
    public int Theta       { get; init; } = 0;
    public int LowAlpha    { get; init; } = 0;
    public int HighAlpha   { get; init; } = 0;
    public int LowBeta     { get; init; } = 0;
    public int HighBeta    { get; init; } = 0;
    public int LowGamma    { get; init; } = 0;
    public int MidGamma    { get; init; } = 0;
    public IReadOnlyList<int> RawEeg { get; init; } = [];  // 10샘플/패킷, 512Hz
    public int EyeBlink    { get; init; } = 0;

    public SignalQuality SignalQuality => PoorSignal switch
    {
        200          => SignalQuality.NoSignal,
        > 50         => SignalQuality.Poor,
        > 0          => SignalQuality.Fair,
        _            => SignalQuality.Good
    };
}

public enum SignalQuality { NoSignal, Poor, Fair, Good }
