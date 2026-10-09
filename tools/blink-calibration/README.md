# Blink threshold calibration

The blink detector's threshold (minimum raw EEG peak-to-peak amplitude) must be measured on a real
MindWave Mobile 2 before v7.0.0 ships. The current value, **3000**, is a provisional estimate: the web
tutorial's 600 is in Web Bluetooth raw units, roughly 1/5 of the native units the SDKs use.

All three SDKs (Android, Apple, Windows) parse the same BLE bytes the same way and run the same
`BlinkDetector`. **One measurement sets the threshold for all three.**

## What you need

- A MindWave Mobile 2, charged, with a clean sensor tip
- A Windows 10/11 PC with Bluetooth LE and the .NET 8 SDK
- Python 3.8 or later (standard library only)
- About 10 minutes per recording. Do at least **2 recordings**; 2–3 different people is better.

## Procedure

### 1. Record

```powershell
# from the repository root
dotnet run --project NeuroSky.Sample -- calibrate-blink AA:BB:CC:DD:EE:FF rec1.csv 60
#                                                     ^ headset MAC    ^ file  ^ mains Hz (50 or 60)
```

The program walks you through four phases:

| Phase | Length | What to do |
|---|---|---|
| Signal check | up to 60 s | Wear the headset until PoorSignal ≤ 50 (moisten the sensor if needed) |
| Settle | 10 s | Look at the screen, blink normally |
| **Cued blinks** | 30 cues × 3 s | Blink **once, firmly** at each `>>> BLINK <<<` + beep; try not to blink between cues |
| **No blinking** | 60 s | Keep your eyes open; if you blink anyway, press **SPACE** immediately |

Keep the conditions realistic: sit as you would during normal use, and don't clench your jaw or
move your head. Jaw and head movement also produce large raw EEG swings.

### 2. Analyse

```powershell
python tools/blink-calibration/analyze.py rec1.csv rec2.csv rec3.csv
```

The script replays the SDK's detection algorithm over the recordings for every threshold from 500 to
10 000 (step 250). It prints:

- **Amplitude profile**: the peak-to-peak of each cued blink, and of the no-blink signal. A good
  threshold sits between the no-blink maximum and the weakest blink.
- **Threshold sweep**: for each threshold, the hit rate (cued blinks detected), false positives in
  the cued phase (detections outside any cue window), and false positives in the no-blink phase
  (detections not explained by a SPACE mark).
- **Self-check**: replays each recording at the threshold the SDK used while recording, and compares
  the result with the blinks the SDK itself reported (`sdk_blink` rows). "identical" shows on real data
  that the script's algorithm matches the SDK's. If it says MISMATCH, don't use the sweep.
- **Recommendation**: the middle of the range that has zero false positives (both phases) and a hit
  rate of at least 90 %.

| Window | Counts as |
|---|---|
| Cue − 200 ms … cue + 1500 ms | Hit for that cue (at most one per cue) |
| Cued phase, outside every cue window | False positive |
| No-blink phase, SPACE − 1500 ms … SPACE + 300 ms | Marked involuntary blink, excluded |
| No-blink phase, anything else | False positive |

### 3. Decide and record

Acceptance criteria (the recommendation in `analyze.py` applies exactly these):

- Hit rate **≥ 90 %** over all cued blinks (27/30 per recording)
- **0** false positives in the no-blink phase
- **0** false positives in the cued phase (detections outside every cue window)
- Self-check reports **identical** for every recording (see below)

Then:

1. Set `DefaultThreshold` / `DEFAULT_THRESHOLD` / `defaultThreshold` to the chosen value in all three
   SDKs, and update the "Threshold" row in each README.
2. Paste the analysis output (amplitude profile, the sweep rows around the chosen value, and the
   recommendation) into the Task 3 PR descriptions as the basis for the value.

If no threshold meets the criteria, the analysis says so. Usually the cause is poor electrode contact
or movement artifacts. Record again rather than relaxing the criteria.

## Recording format

`t_ms,phase,type,poor_signal,value`

| `type` | `value` |
|---|---|
| `raw` | The packet's 10 raw EEG samples, space-separated |
| `esense` | — (`poor_signal` holds the latest PoorSignal) |
| `cue` | Cue number 1–30 |
| `mark` | SPACE press number |
| `config` | `sdk_threshold=<n>`: the SDK's threshold while recording |
| `sdk_blink` | Strength of a blink the SDK reported at that threshold (used by the self-check) |
