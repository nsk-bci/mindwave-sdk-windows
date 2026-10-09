using NeuroSky.Sdk;

// Usage: dotnet run --project NeuroSky.Sample -- [MAC address] [50|60]
// Without a MAC address the sample scans for "MindWave Mobile" first.
Console.WriteLine("=== NeuroSky MindWave Windows SDK ===");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await using var sdk = new NeuroSkySdk();
sdk.StateChanged += (_, state) => Console.WriteLine($"[State] {state}");

string? address = args.Length > 0 ? args[0] : null;
if (address is null)
{
    Console.WriteLine("Scanning for \"MindWave Mobile\" (10 s) ...");
    address = await sdk.FindDeviceAddressAsync("MindWave Mobile", ct: cts.Token);
    if (address is null)
    {
        Console.WriteLine("Headset not found — turn it on, or pass its MAC address as the first argument.");
        return;
    }
    Console.WriteLine($"Found {address}");
}

await sdk.ConnectAsync(address, cts.Token);
if (sdk.State != ConnectionState.Connected)
{
    Console.WriteLine("Connection failed — verify power / MAC address.");
    return;
}

// Notch filter for mains noise: 60 Hz (Korea/USA) by default, pass 50 for Europe/China.
bool notch50 = args.Length > 1 && args[1] == "50";
await sdk.SendCommandAsync(notch50 ? NeuroSkyCommand.Notch50Hz : NeuroSkyCommand.Notch60Hz);

try
{
    await foreach (var data in sdk.DataStream(cts.Token))
    {
        if (data.Attention == 0 && data.Meditation == 0 && data.Delta == 0) continue;  // wait for eSense

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
        Console.WriteLine("Ctrl+C to exit");
    }
}
catch (OperationCanceledException)
{
}
