using System.Globalization;
using NeuroSky.Sdk;

namespace NeuroSky.Sample;

/// <summary>
/// One on-device session that collects everything needed before v7.0.0:
/// <list type="number">
/// <item>blink calibration — 30 cued blinks, then 60 s without blinking</item>
/// <item>raw EEG notifications as received (hex, unparsed)</item>
/// <item>0xEC packets with non-zero bands</item>
/// <item>PoorSignal 200 — headset taken off</item>
/// <item>0xEB → 0xEC arrival order and spacing</item>
/// </list>
/// Every BLE notification is logged byte-for-byte (<c>pkt</c> rows) alongside the parsed values.
/// Analyse with <c>tools/blink-calibration/analyze.py</c> (blink threshold) and
/// <c>tools/blink-calibration/inspect_packets.py</c> (packet layout). Procedure: <c>tools/blink-calibration/README.md</c>.
/// </summary>
internal static class BlinkCalibration
{
    private const int CueCount = 30;
    private const int CueIntervalMs = 3_000;
    private const int SettleSeconds = 10;
    private const int StillSeconds = 60;
    private const int SignalWaitSeconds = 60;
    private const int OffWaitSeconds = 60;
    private const int OffHoldSeconds = 10;
    private const int MaxPoorSignal = ThinkGearParser.DefaultMaxPoorSignal;

    public static async Task RunAsync(string deviceAddress, string outputPath, int notchHz)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await using var file = new StreamWriter(outputPath);
        var log = new CsvLog(file);
        var stats = new CaptureStats();
        string phase = "connect";
        int poorSignal = -1;  // -1 until the first eSense packet

        await using var sdk = new NeuroSkySdk();
        sdk.StateChanged += (_, s) => Console.WriteLine($"[State] {s}");

        // Raw notifications exactly as received — subscribed before connecting so nothing is missed.
        sdk.PacketReceived += (uuid, bytes) =>
        {
            long t = Now();
            string kind = uuid == NeuroSkyUuid.RawEeg ? "raw" : uuid == NeuroSkyUuid.ESense ? "esense" : uuid.ToString();
            log.Write(t, phase, "pkt", poorSignal, $"{kind}:{Convert.ToHexString(bytes)}");
            stats.Record(kind, bytes, t);
        };

        log.Write(Now(), phase, "config", -1, $"sdk_threshold={BlinkDetector.DefaultThreshold}");

        Console.WriteLine($"Connecting to {deviceAddress} ...");
        await sdk.ConnectAsync(deviceAddress);
        if (sdk.State != ConnectionState.Connected)
        {
            Console.WriteLine("Connection failed — check headset power and MAC address.");
            return;
        }

        await sdk.SendCommandAsync(notchHz == 50 ? NeuroSkyCommand.Notch50Hz : NeuroSkyCommand.Notch60Hz);
        await sdk.SendCommandAsync(NeuroSkyCommand.StartRawEeg);

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
                    stats.RecordPoorSignal(d.PoorSignal);
                    log.Write(d.Timestamp, phase, "esense", d.PoorSignal, "");
                }
            }
        });

        var blinkTask = Task.Run(async () =>
        {
            await foreach (var b in sdk.BlinkStream(cts.Token))
                log.Write(b.TimestampMs, phase, "sdk_blink", -1, b.Strength.ToString(CultureInfo.InvariantCulture));
        });

        bool completed = false;
        try
        {
            Step(1, 6, "Put the headset on",
                $"Wear it as usual. Waiting for good contact (PoorSignal ≤ {MaxPoorSignal}).",
                "If it takes long: moisten the forehead sensor, press it to the skin, check the ear clip.");
            var waitUntil = DateTime.UtcNow.AddSeconds(SignalWaitSeconds);
            while (poorSignal < 0 || poorSignal > MaxPoorSignal)
            {
                if (DateTime.UtcNow > waitUntil)
                {
                    Console.WriteLine("No usable signal within 60 s. Reseat the headset and run the tool again.");
                    return;
                }
                await Task.Delay(200, cts.Token);
            }

            phase = "settle";
            Step(2, 6, "Settle", $"Signal OK. Look at the screen and blink normally for {SettleSeconds} s.");
            await Task.Delay(SettleSeconds * 1_000, cts.Token);

            Step(3, 6, "Cued blinks",
                $"Blink ONCE, firmly, each time you see  >>> BLINK <<<  ({CueCount} cues, {CueIntervalMs / 1000} s apart).",
                "Try not to blink between cues. Keep your head and jaw still.");
            await Countdown(cts.Token);
            phase = "cued";
            for (int i = 1; i <= CueCount; i++)
            {
                log.Write(Now(), phase, "cue", poorSignal, i.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine($"  >>> BLINK <<<   ({i}/{CueCount})   PoorSignal {poorSignal}");
                Console.Beep(880, 120);
                await Task.Delay(CueIntervalMs, cts.Token);
            }

            Step(4, 6, "No blinking",
                $"Keep your eyes open and try NOT to blink for {StillSeconds} s.",
                "If you blink anyway, press SPACE right away so that blink is excluded.");
            await Countdown(cts.Token);
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

            phase = "off";
            Step(5, 6, "Take the headset off",
                "Take it off and lay it on the table. Keep it switched ON.",
                $"Waiting for PoorSignal 200 (no contact), then recording {OffHoldSeconds} s more.");
            var offUntil = DateTime.UtcNow.AddSeconds(OffWaitSeconds);
            while (poorSignal != 200 && DateTime.UtcNow < offUntil)
                await Task.Delay(200, cts.Token);
            if (poorSignal == 200)
            {
                Console.WriteLine("  PoorSignal 200 received.");
                await Task.Delay(OffHoldSeconds * 1_000, cts.Token);
            }
            else
            {
                Console.WriteLine($"  PoorSignal 200 did not arrive within {OffWaitSeconds} s (last: {poorSignal}).");
            }

            phase = "done";
            completed = true;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nCancelled — the partial recording is not usable.");
        }
        finally
        {
            cts.Cancel();
            try { await Task.WhenAll(dataTask, blinkTask); } catch (OperationCanceledException) { }
            await sdk.SendCommandAsync(NeuroSkyCommand.StopRawEeg);
            await sdk.DisconnectAsync();
        }

        if (!completed) return;

        Step(6, 6, "Check what was collected", $"Recording: {Path.GetFullPath(outputPath)}");
        stats.PrintChecklist();
        Console.WriteLine("\nNext:");
        Console.WriteLine($"  python tools/blink-calibration/inspect_packets.py \"{outputPath}\"   (packet layout)");
        Console.WriteLine($"  python tools/blink-calibration/analyze.py \"{outputPath}\"           (blink threshold)");
    }

    private static void Step(int n, int total, string title, params string[] lines)
    {
        Console.WriteLine();
        Console.WriteLine($"── Step {n}/{total}: {title} " + new string('─', Math.Max(4, 50 - title.Length)));
        foreach (var line in lines) Console.WriteLine("  " + line);
    }

    private static async Task Countdown(CancellationToken ct)
    {
        for (int i = 3; i > 0; i--) { Console.WriteLine($"  starting in {i} ..."); await Task.Delay(1_000, ct); }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Live counts for the end-of-session checklist. The eSense type is read at byte 2 here only for the
    /// checklist; inspect_packets.py checks that assumption against the recorded bytes.
    /// </summary>
    private sealed class CaptureStats
    {
        private readonly object _gate = new();
        private int _raw, _rawNegative, _eb, _ec, _ecNonZero, _ebEcPairs, _poor200;
        private readonly SortedSet<int> _rawLengths = [];
        private long _lastEbAt = -1;
        private readonly List<long> _ebToEcMs = [];

        public void Record(string kind, byte[] b, long t)
        {
            lock (_gate)
            {
                if (kind == "raw")
                {
                    _raw++;
                    _rawLengths.Add(b.Length);
                    for (int i = 0; i + 1 < b.Length; i += 2)
                        if ((b[i] & 0x80) != 0) { _rawNegative++; break; }
                }
                else if (kind == "esense" && b.Length >= 3)
                {
                    switch (b[2])
                    {
                        case 0xEB:
                            _eb++;
                            _lastEbAt = t;
                            break;
                        case 0xEC:
                            _ec++;
                            if (b.Skip(3).Any(x => x != 0)) _ecNonZero++;
                            if (_lastEbAt >= 0 && t - _lastEbAt <= 2_000) { _ebEcPairs++; _ebToEcMs.Add(t - _lastEbAt); }
                            _lastEbAt = -1;
                            break;
                    }
                }
            }
        }

        public void RecordPoorSignal(int poor)
        {
            if (poor == 200) lock (_gate) _poor200++;
        }

        public void PrintChecklist()
        {
            lock (_gate)
            {
                Check(_raw >= 100, $"Raw EEG packets: {_raw} (lengths: {string.Join(", ", _rawLengths)})");
                Check(_rawNegative > 0, $"Raw packets with a negative-looking sample: {_rawNegative}");
                Check(_ecNonZero > 0, $"0xEC packets: {_ec}, with non-zero bands: {_ecNonZero}");
                Check(_poor200 > 0, $"eSense updates with PoorSignal 200: {_poor200}");
                string spacing = _ebToEcMs.Count > 0
                    ? $"median {_ebToEcMs.OrderBy(x => x).ElementAt(_ebToEcMs.Count / 2)} ms, max {_ebToEcMs.Max()} ms"
                    : "-";
                Check(_ebEcPairs > 0, $"0xEB → 0xEC pairs: {_ebEcPairs} of {_eb} 0xEB ({spacing})");
            }
        }

        private static void Check(bool ok, string text) => Console.WriteLine($"  [{(ok ? "OK" : "MISSING")}] {text}");
    }

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
