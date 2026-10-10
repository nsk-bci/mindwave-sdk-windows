namespace NeuroSky.Sdk;

/// <summary>
/// One eye blink, emitted on <see cref="NeuroSkySdk.BlinkStream"/> each time a blink is detected.
/// </summary>
/// <param name="TimestampMs">Detection time (Unix epoch ms, UTC).</param>
/// <param name="Strength">Raw EEG peak-to-peak amplitude of the detection window (raw EEG units, at or above the threshold).</param>
/// <param name="Sequence">Blinks since the connection started, starting at 1. Resets on reconnect.</param>
public sealed record BlinkEvent(long TimestampMs, int Strength, int Sequence);
