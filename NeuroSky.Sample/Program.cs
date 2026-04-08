using NeuroSky.Sdk;

// ── Simulator test ────────────────────────────────────────────────────────────
Console.WriteLine("=== NeuroSky MindWave Windows SDK - Simulator ===");

var simulator = new SimulatorTransport();
simulator.SetMode(SimulatorTransport.Mode.Focused);
simulator.StateChanged += (_, state) => Console.WriteLine($"[State] {state}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await simulator.ConnectAsync("simulator");

await foreach (var data in simulator.DataStream(cts.Token))
{
    Console.Clear();
    Console.WriteLine("=== NeuroSky MindWave Windows SDK ===");
    Console.WriteLine();
    Console.WriteLine($"Signal Quality : {data.SignalQuality,-10} (PoorSignal: {data.PoorSignal})");
    Console.WriteLine($"Attention      : {data.Attention,3}");
    Console.WriteLine($"Meditation     : {data.Meditation,3}");
    Console.WriteLine();
    Console.WriteLine($"Delta          : {data.Delta,8}");
    Console.WriteLine($"Theta          : {data.Theta,8}");
    Console.WriteLine($"Low Alpha      : {data.LowAlpha,8}");
    Console.WriteLine($"High Alpha     : {data.HighAlpha,8}");
    Console.WriteLine($"Low Beta       : {data.LowBeta,8}");
    Console.WriteLine($"High Beta      : {data.HighBeta,8}");
    Console.WriteLine($"Low Gamma      : {data.LowGamma,8}");
    Console.WriteLine($"Mid Gamma      : {data.MidGamma,8}");
    Console.WriteLine();
    Console.WriteLine($"Raw EEG        : [{string.Join(", ", data.RawEeg)}]");
    Console.WriteLine();
    Console.WriteLine("Ctrl+C to exit");
}

// ── Real device example (uncomment to use) ───────────────────────────────────
/*
await using var sdk = new NeuroSkySdk();
sdk.StateChanged += (_, state) => Console.WriteLine($"[State] {state}");

await sdk.ConnectAsync("AA:BB:CC:DD:EE:FF");  // ← enter actual MAC address

// Set notch filter for Korea/USA
await sdk.SendCommandAsync(NeuroSkyCommand.Notch60Hz);

await foreach (var data in sdk.DataStream(cts.Token))
{
    Console.WriteLine($"Attention: {data.Attention} | Meditation: {data.Meditation} | Signal: {data.SignalQuality}");
}
*/
