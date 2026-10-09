# On-device capture session (v7.0.0 release gate)

One run of the tool collects everything v7.0.0 still needs from a real MindWave Mobile 2:

| # | What | Why |
|---|---|---|
| 1 | Blink calibration: 30 cued blinks + 60 s without blinking | The blink threshold (**3000**) is a provisional estimate |
| 2 | Raw EEG notifications, byte-for-byte | All SDKs assume raw packets have **no prefix** (offset 0, big-endian). No capture has confirmed this. Blink detection builds on raw EEG, and so does 6 EI in v7.1.0. **v7.0.0 does not ship until this is confirmed.** |
| 3 | 0xEC packets with non-zero bands | No 0xEC capture exists yet, so the parser tests for 0xEC are synthetic |
| 4 | PoorSignal 200 (headset off) | Signal-quality tests, and the blink gate |
| 5 | 0xEB → 0xEC order and spacing | Decides how to fix the 0xEB emission mismatch between platforms (on hold until this data exists) |

Every BLE notification is written **exactly as received**, as hex (`pkt` rows), next to the parsed
values. The raw bytes are captured before any SDK parser touches them.

All three SDKs (Android, Apple, Windows) parse the same BLE bytes the same way and run the same
`BlinkDetector`. **One session sets the threshold and checks the layout for all three.**

## What you need

- A MindWave Mobile 2, charged, with a clean sensor tip
- A Windows 10/11 PC with Bluetooth LE and the **.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8`;
  open a new terminal afterwards so `dotnet` is on PATH, then check with `dotnet --list-sdks`)
- **Python 3.8 or later.** Standard library only: no packages, no `requirements.txt`. On Windows, run
  `py` instead of `python` if `python` is not found.
- The `feat/eye-blink` branch checked out. No other setup is needed; the first `dotnet run` restores
  and builds everything.
- About 10 minutes per session. Do at least **2 sessions**; 2–3 different people is better.

## 1. Record

```powershell
# from the repository root
dotnet run --project NeuroSky.Sample -- calibrate-blink AA:BB:CC:DD:EE:FF session1.csv 60
#                                                     ^ headset MAC    ^ file      ^ mains Hz (50 or 60)
```

Don't know the headset's MAC address? Put `scan` in its place: the tool finds the headset by its name
"MindWave Mobile", prints the address, and continues:
`dotnet run --project NeuroSky.Sample -- calibrate-blink scan session1.csv 60`

The tool prints each step and what to do; you can run it on your own.

| Step | Length | What to do | Collects |
|---|---|---|---|
| 1. Put the headset on | up to 60 s | Wear it until PoorSignal ≤ 50 (moisten the sensor if needed) | — |
| 2. Settle | 10 s | Look at the screen, blink normally | 2, 3, 5 |
| 3. **Cued blinks** | 30 × 3 s | Blink **once, firmly** at each `>>> BLINK <<<` + beep; don't blink between cues | 1, 2, 3, 5 |
| 4. **No blinking** | 60 s | Keep your eyes open; if you blink anyway, press **SPACE** immediately | 1, 2, 3, 5 |
| 5. **Take the headset off** | until PoorSignal 200, then 10 s | Lay it on the table, keep it switched **on** | 4 |
| 6. Checklist | — | Read the summary | — |

Raw and eSense packets (items 2, 3, 5) are recorded during the whole session. The checklist at the end
shows `[OK]` / `[MISSING]` for each item. If something is missing, run the session again.

Keep the conditions realistic: sit as you would during normal use, and don't clench your jaw or move
your head. Jaw and head movement also produce large raw EEG swings.

The CSV stays outside the repository: the Owner keeps the originals. Only the analysis output goes into
PRs.

## 2. Check the packet layout (items 2–5)

```powershell
python tools/blink-calibration/inspect_packets.py session1.csv --fixtures session1-fixtures.txt
```

This reads only the `pkt` rows and does not rely on the SDK parsers.

**Raw EEG layout:** the verdict is **CONFIRMED** only if all of these hold:
- Packets are always 20 bytes
- No byte position stays constant. A constant position would look like a prefix or header.
- High bytes sit at even positions: they change slowly, while the low bytes look random. That means
  big-endian from byte 0.
- Big-endian decoding is much smoother than little-endian.
- No jump at packet boundaries under offset-0 decoding. A jump would mean extra bytes at the start or
  end of each packet.
- **2-byte prefix + 9 samples is excluded** (also 9 samples + 2-byte suffix). These are the dangerous
  cases: they fill exactly 20 bytes, 9 of 10 samples decode correctly, and the waveform looks fine.
  Their only trace is a glitch at the same sample index in every packet. A prefix that changes, such as
  a counter, also passes the constant-byte check. So the script measures roughness per sample index,
  and it re-decodes under each hypothesis. The hypothesis is ruled out only when the SDK decoding is
  flat **and** both hypotheses show a glitch. The output says `EXCLUDED`, `NOT EXCLUDED`, or
  `CANNOT TELL`.

It also reports how many decoded samples are negative and whether `0x8000` (−32768) occurs.

**eSense:**
- The first two bytes of every packet (the SDKs assume `00 00`)
- The type distribution
- 0xEC packets with non-zero bands
- PoorSignal values, including 200
- The code bytes in front of each value
- 0xEB → 0xEC pairing, spacing, and orphans (0xEB with no 0xEC, which matters for the emission fix)

**Fixtures** (`--fixtures`): representative packets as hex — 0xEA worn and off, 0xEB, 0xEC, and five
consecutive raw packets with negative samples. These become the capture-based parser tests, in the
same format as the Windows v2.0.4 tests.

If the raw verdict is **NOT CONFIRMED**, stop: the raw parsers of all three SDKs, blink detection, and
the threshold analysis would all be built on the wrong decoding.

## 3. Choose the blink threshold (item 1)

Only after the raw layout is confirmed:

```powershell
python tools/blink-calibration/analyze.py session1.csv session2.csv session3.csv
```

The script replays the SDK's detection algorithm over the recordings for every threshold from 500 to
10 000 (step 250). It prints:

- **Amplitude profile**: the peak-to-peak of each cued blink, and of the no-blink signal. A good
  threshold sits between the no-blink maximum and the weakest blink.
- **Self-check**: replays each recording at the threshold the SDK used while recording, and compares
  the result with the blinks the SDK itself reported (`sdk_blink` rows). "identical" shows on real data
  that the script's algorithm matches the SDK's. If it says MISMATCH, don't use the sweep.
- **Threshold sweep**: for each threshold, the hit rate (cued blinks detected), false positives in
  the cued phase (detections outside any cue window), and false positives in the no-blink phase
  (detections not explained by a SPACE mark).
- **Recommendation**: the middle of the range that has zero false positives (both phases) and a hit
  rate of at least 90 %.

| Window | Counts as |
|---|---|
| Cue − 200 ms … cue + 1500 ms | Hit for that cue (at most one per cue) |
| Cued phase, outside every cue window | False positive |
| No-blink phase, SPACE − 1500 ms … SPACE + 300 ms | Marked involuntary blink, excluded |
| No-blink phase, anything else | False positive |

### Acceptance criteria

`analyze.py` applies exactly these:

- Hit rate **≥ 90 %** over all cued blinks (27/30 per session)
- **0** false positives in the no-blink phase
- **0** false positives in the cued phase (detections outside every cue window)
- Self-check reports **identical** for every session

Then:

1. Set `DefaultThreshold` / `DEFAULT_THRESHOLD` / `defaultThreshold` to the chosen value in all three
   SDKs, and update the "Threshold" row in each README.
2. Paste the analysis output (amplitude profile, self-check, the sweep rows around the chosen value,
   and the recommendation) into the Task 3 PR descriptions as the basis for the value.

If no threshold meets the criteria, the analysis says so. Usually the cause is poor electrode contact or
movement artifacts. Record again rather than relaxing the criteria.

## Recording format

`t_ms,phase,type,poor_signal,value` · phases: `connect`, `settle`, `cued`, `still`, `off`, `done`

| `type` | `value` |
|---|---|
| `pkt` | `<characteristic>:<hex>`: a notification exactly as received; `raw` or `esense`, otherwise the UUID |
| `raw` | The SDK's parsed raw EEG samples for that packet, space-separated |
| `esense` | — (`poor_signal` holds the parsed PoorSignal) |
| `cue` | Cue number 1–30 |
| `mark` | SPACE press number |
| `config` | `sdk_threshold=<n>`: the SDK's blink threshold while recording |
| `sdk_blink` | Strength of a blink the SDK reported at that threshold (used by the self-check) |
