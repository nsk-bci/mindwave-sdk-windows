using System.Globalization;
using NeuroSky.Sdk;

namespace NeuroSky.Sample;

/// <summary>
/// Records the raw EEG needed to choose the blink threshold: 30 cued blinks, then 60 s without blinking.
/// Analyse the CSV with <c>tools/blink-calibration/analyze.py</c>; the procedure is in
/// <c>tools/blink-calibration/README.md</c>.
/// </summary>
internal static class BlinkCalibration
{
    private const int CueCount = 30;
    private const int CueIntervalMs = 3_000;
    private const int SettleSeconds = 10;
    private const int StillSeconds = 60;
    private const int SignalWaitSeconds = 60;
    private const int MaxPoorSignal = ThinkGearParser.DefaultMaxPoorSignal;

    public static async Task RunAsync(string deviceAddress, string outputPath, int notchHz)
    {
        await using var sdk = new NeuroSkySdk();
        sdk.StateChanged += (_, s) => Console.WriteLine($"[State] {s}");

        Console.WriteLine($"Connecting to {deviceAddress} ...");
        await sdk.ConnectAsync(deviceAddress);
        if (sdk.State != ConnectionState.Connected)
        {
            Console.WriteLine("Connection failed — check headset power and MAC address.");
            return;
        }

        await sdk.SendCommandAsync(notchHz == 50 ? NeuroSkyCommand.Notch50Hz : NeuroSkyCommand.Notch60Hz);
        await sdk.SendCommandAsync(NeuroSkyCommand.StartRawEeg);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await using var file = new StreamWriter(outputPath);
        var log = new CsvLog(file);
        string phase = "connect";
        int poorSignal = -1;  // -1 until the first eSense packet

        var dataTask = Task.Run(async () =>
        {
            // eSense packets carry the previous RawEeg list along (records copy the reference),
            // so a new raw packet is recognised by a new RawEeg instance.
            IReadOnlyList<int>? lastRaw = null;
            await foreach (var d in sdk.DataStream(cts.Token))
            {
                if (!ReferenceEquals(d.RawEeg, lastRaw) && d.RawEeg.Count > 0)
                {
                    lastRaw = d.RawEeg;
                    log.Write(d.Timestamp, phase, "raw", d.PoorSignal, string.Join(' ', d.RawEeg));
                }
                else
                {
                    poorSignal = d.PoorSignal;
                    log.Write(d.Timestamp, phase, "esense", d.PoorSignal, "");
                }
            }
        });

        var blinkTask = Task.Run(async () =>
        {
            await foreach (var b in sdk.BlinkStream(cts.Token))
                log.Write(b.TimestampMs, phase, "sdk_blink", -1, b.Strength.ToString(CultureInfo.InvariantCulture));
        });

        try
        {
            // 1. Wait for good electrode contact.
            Console.WriteLine($"\nWaiting for signal quality (PoorSignal ≤ {MaxPoorSignal}) — adjust the headset if needed ...");
            var waitUntil = DateTime.UtcNow.AddSeconds(SignalWaitSeconds);
            while (poorSignal < 0 || poorSignal > MaxPoorSignal)
            {
                if (DateTime.UtcNow > waitUntil)
                {
                    Console.WriteLine("No usable signal within 60 s. Moisten the sensor, reseat the headset, and try again.");
                    return;
                }
                await Task.Delay(200, cts.Token);
            }

            // 2. Settle — lets the blink detector warm up and records a normal baseline.
            phase = "settle";
            Console.WriteLine($"\nSignal OK. Relax and look at the screen for {SettleSeconds} s, blinking normally.");
            await Task.Delay(SettleSeconds * 1_000, cts.Token);

            // 3. Cued blinks.
            Console.WriteLine($"\nNext: blink ONCE, firmly, each time you see  >>> BLINK <<<  ({CueCount} cues, {CueIntervalMs / 1000} s apart).");
            Console.WriteLine("Try not to blink between cues.");
            for (int i = 3; i > 0; i--) { Console.WriteLine($"  starting in {i} ..."); await Task.Delay(1_000, cts.Token); }

            phase = "cued";
            for (int i = 1; i <= CueCount; i++)
            {
                log.Write(Now(), phase, "cue", poorSignal, i.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine($"  >>> BLINK <<<   ({i}/{CueCount})   PoorSignal {poorSignal}");
                Console.Beep(880, 120);
                await Task.Delay(CueIntervalMs, cts.Token);
            }

            // 4. No blinking.
            Console.WriteLine($"\nNext: keep your eyes open and try NOT to blink for {StillSeconds} s.");
            Console.WriteLine("If you blink anyway, press SPACE right away so that blink is excluded.");
            for (int i = 3; i > 0; i--) { Console.WriteLine($"  starting in {i} ..."); await Task.Delay(1_000, cts.Token); }

            phase = "still";
            var stillUntil = DateTime.UtcNow.AddSeconds(StillSeconds);
            int marks = 0, lastShown = -1;
            while (DateTime.UtcNow < stillUntil)
            {
                while (Console.KeyAvailable)
                {
                    if (Console.ReadKey(intercept: true).Key == ConsoleKey.Spacebar)
                    {
                        marks++;
                        log.Write(Now(), phase, "mark", poorSignal, marks.ToString(CultureInfo.InvariantCulture));
                        Console.WriteLine($"  marked blink #{marks}");
                    }
                }
                int left = (int)Math.Ceiling((stillUntil - DateTime.UtcNow).TotalSeconds);
                if (left != lastShown && left % 10 == 0) { Console.WriteLine($"  {left} s left"); lastShown = left; }
                await Task.Delay(20, cts.Token);
            }

            phase = "done";
            Console.WriteLine($"\nDone. Recording saved to {Path.GetFullPath(outputPath)}");
            Console.WriteLine($"Analyse it with:  python tools/blink-calibration/analyze.py \"{outputPath}\"");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nCancelled — the partial recording is not usable for calibration.");
        }
        finally
        {
            cts.Cancel();
            try { await Task.WhenAll(dataTask, blinkTask); } catch (OperationCanceledException) { }
            await sdk.SendCommandAsync(NeuroSkyCommand.StopRawEeg);
            await sdk.DisconnectAsync();
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Thread-safe CSV writer: t_ms,phase,type,poor_signal,value.</summary>
    private sealed class CsvLog
    {
        private readonly StreamWriter _writer;
        private readonly object _gate = new();

        public CsvLog(StreamWriter writer)
        {
            _writer = writer;
            _writer.WriteLine("t_ms,phase,type,poor_signal,value");
        }

        public void Write(long tMs, string phase, string type, int poorSignal, string value)
        {
            lock (_gate)
                _writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{tMs},{phase},{type},{poorSignal},{value}"));
        }
    }
}
