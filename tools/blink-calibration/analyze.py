#!/usr/bin/env python3
"""Choose the blink threshold from on-device recordings.

Replays the SDK's BlinkDetector (identical on Android, Apple, and Windows) over one or more
recordings made with `NeuroSky.Sample calibrate-blink`, sweeps the threshold, and reports
hit rate and false positives for each value. Standard library only.

    python analyze.py recording1.csv [recording2.csv ...] [--min 500 --max 10000 --step 250]
"""
import argparse
import csv
import statistics
import sys

# Must match BlinkDetector / ThinkGearParser defaults in the SDKs.
WINDOW = 100            # samples
COOLDOWN_MS = 600
WARMUP_MS = 500
RESTART_GAP_MS = 1_000
MIN_SAMPLES = 8
MAX_POOR_SIGNAL = 50
NEVER = -(1 << 62)

# Scoring windows.
CUE_BEFORE_MS = 200     # a blink may start slightly before the cue is noticed
CUE_AFTER_MS = 1_500    # reaction time + blink duration
MARK_BEFORE_MS = 1_500  # SPACE is pressed after an involuntary blink
MARK_AFTER_MS = 300

TARGET_HIT_RATE = 0.90
SELF_CHECK_TOLERANCE_MS = 50   # replay vs SDK detection: same packet, so timestamps nearly match


class Recording:
    def __init__(self, path):
        self.path = path
        self.packets = []   # (t, phase, signal_ok, samples)
        self.cues = []      # t
        self.marks = []     # t
        self.sdk_blinks = []  # t of blinks the SDK reported while recording
        self.sdk_threshold = None
        self.phase_span = {}
        signal_known = False
        with open(path, newline="", encoding="utf-8") as f:
            for row in csv.DictReader(f):
                t, phase, kind = int(row["t_ms"]), row["phase"], row["type"]
                poor = int(row["poor_signal"])
                lo, hi = self.phase_span.get(phase, (t, t))
                self.phase_span[phase] = (min(lo, t), max(hi, t))
                if kind == "esense":
                    signal_known = True
                elif kind == "raw":
                    samples = [int(v) for v in row["value"].split()]
                    ok = signal_known and 0 <= poor <= MAX_POOR_SIGNAL
                    self.packets.append((t, phase, ok, samples))
                elif kind == "cue":
                    self.cues.append(t)
                elif kind == "mark":
                    self.marks.append(t)
                elif kind == "sdk_blink":
                    self.sdk_blinks.append(t)
                elif kind == "config" and row["value"].startswith("sdk_threshold="):
                    self.sdk_threshold = int(row["value"].split("=", 1)[1])
        self.packets.sort(key=lambda p: p[0])
        if len(self.cues) == 0 or "still" not in self.phase_span:
            sys.exit(f"{path}: incomplete recording (needs the cued and still phases)")

    def in_cue_window(self, t):
        return any(c - CUE_BEFORE_MS <= t <= c + CUE_AFTER_MS for c in self.cues)

    def near_mark(self, t):
        return any(m - MARK_BEFORE_MS <= t <= m + MARK_AFTER_MS for m in self.marks)


def replay(packets, threshold):
    """Same algorithm as ThinkGearParser + BlinkDetector. Yields (t, phase, peak_to_peak)."""
    window = [0] * WINDOW
    count = head = 0
    armed_at = last_sample_at = 0
    last_blink_at = NEVER
    for t, phase, signal_ok, samples in packets:
        if not signal_ok:
            count = head = 0          # parser resets the detector while the signal is poor
            continue
        if count == 0 or t - last_sample_at > RESTART_GAP_MS:
            count = head = 0
            armed_at = t
            last_blink_at = NEVER
        last_sample_at = t
        for s in samples:
            window[head] = s
            head = (head + 1) % WINDOW
            count = min(count + 1, WINDOW)
        if count < MIN_SAMPLES or t - armed_at < WARMUP_MS or t - last_blink_at <= COOLDOWN_MS:
            continue
        filled = window[:count]
        p2p = max(filled) - min(filled)
        if p2p >= threshold:
            last_blink_at = t
            yield t, phase, p2p


def rolling_p2p(packets):
    """Peak-to-peak of the last WINDOW samples at each packet (signal-gated, no cooldown)."""
    buf, out = [], []
    for t, phase, signal_ok, samples in packets:
        if not signal_ok:
            buf = []
            continue
        buf = (buf + samples)[-WINDOW:]
        if len(buf) >= MIN_SAMPLES:
            out.append((t, phase, max(buf) - min(buf)))
    return out


def score(rec, threshold):
    hits, marked, cued_fp, still_fp = set(), set(), 0, 0
    for t, phase, _ in replay(rec.packets, threshold):
        if phase == "cued":
            matched = [c for c in rec.cues if c - CUE_BEFORE_MS <= t <= c + CUE_AFTER_MS]
            if matched:
                hits.add(matched[0])
            else:
                cued_fp += 1
        elif phase == "still":
            near = [m for m in rec.marks if m - MARK_BEFORE_MS <= t <= m + MARK_AFTER_MS]
            if near:
                marked.add(near[0])
            else:
                still_fp += 1
    return len(hits), cued_fp, still_fp, len(marked)


def percentile(values, q):
    if not values:
        return 0
    values = sorted(values)
    return values[min(len(values) - 1, int(round(q * (len(values) - 1))))]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("recordings", nargs="+")
    ap.add_argument("--min", type=int, default=500)
    ap.add_argument("--max", type=int, default=10_000)
    ap.add_argument("--step", type=int, default=250)
    args = ap.parse_args()

    recs = [Recording(p) for p in args.recordings]
    total_cues = sum(len(r.cues) for r in recs)
    total_marks = sum(len(r.marks) for r in recs)
    still_minutes = sum((r.phase_span["still"][1] - r.phase_span["still"][0]) / 60_000 for r in recs)

    # Amplitude profile, independent of the threshold.
    cue_peaks, still_p2p = [], []
    for r in recs:
        rp = rolling_p2p(r.packets)
        for c in r.cues:
            in_win = [p for t, _, p in rp if c - CUE_BEFORE_MS <= t <= c + CUE_AFTER_MS]
            cue_peaks.append(max(in_win) if in_win else 0)
        still_p2p += [p for t, ph, p in rp if ph == "still" and not r.near_mark(t)]

    print("# Blink threshold calibration\n")
    print(f"Recordings: {', '.join(r.path for r in recs)}")
    print(f"Cued blinks: {total_cues}, still phase: {still_minutes:.1f} min, marked involuntary blinks: {total_marks}\n")
    print("## Amplitude profile (raw EEG peak-to-peak, 100-sample window)\n")
    print("| | min | median | max |")
    print("|---|---|---|---|")
    print(f"| Cued blink peak (per cue) | {min(cue_peaks)} | {int(statistics.median(cue_peaks))} | {max(cue_peaks)} |")
    if still_p2p:
        print(f"| Still phase, no blink (p50 / p99 / max) | {percentile(still_p2p, .5)} | "
              f"{percentile(still_p2p, .99)} | {max(still_p2p)} |")
    print()

    rows = []
    for thr in range(args.min, args.max + 1, args.step):
        hits = cued_fp = still_fp = marked = 0
        for r in recs:
            h, cf, sf, m = score(r, thr)
            hits, cued_fp, still_fp, marked = hits + h, cued_fp + cf, still_fp + sf, marked + m
        rows.append((thr, hits, cued_fp, still_fp, marked))

    print("## Self-check: replay vs SDK\n")
    print("Replays each recording at the threshold the SDK used and compares with the blinks the SDK reported.\n")
    print("| Recording | SDK threshold | SDK blinks | Replay blinks | Matched | Result |")
    print("|---|---|---|---|---|---|")
    for r in recs:
        if r.sdk_threshold is None:
            print(f"| {r.path} | - | - | - | - | no config row (older recording) |")
            continue
        replayed = [t for t, _, _ in replay(r.packets, r.sdk_threshold)]
        matched = sum(1 for t in replayed if any(abs(t - s) <= SELF_CHECK_TOLERANCE_MS for s in r.sdk_blinks))
        ok = matched == len(replayed) == len(r.sdk_blinks)
        print(f"| {r.path} | {r.sdk_threshold} | {len(r.sdk_blinks)} | {len(replayed)} | {matched} | "
              f"{'identical' if ok else 'MISMATCH - do not trust the sweep'} |")
    print()

    print("## Threshold sweep\n")
    print("| Threshold | Hits | Hit rate | False + (cued phase) | False + (still phase) | Marked blinks detected |")
    print("|---|---|---|---|---|---|")
    for thr, hits, cfp, sfp, m in rows:
        print(f"| {thr} | {hits}/{total_cues} | {hits / total_cues:.0%} | {cfp} | {sfp} | {m}/{total_marks} |")
    print()

    clean = [r for r in rows if r[2] == 0 and r[3] == 0]
    good = [r for r in rows if r[1] / total_cues >= TARGET_HIT_RATE]
    print("## Recommendation\n")
    if not clean:
        print("No threshold in range has zero false positives - record again with better electrode contact.")
        return
    if not good:
        print(f"No threshold reaches a {TARGET_HIT_RATE:.0%} hit rate - check that blinks were firm and on cue.")
        return
    lowest_clean, highest_good = clean[0][0], good[-1][0]
    if lowest_clean > highest_good:
        print(f"Conflict: zero false positives needs >= {lowest_clean}, but a {TARGET_HIT_RATE:.0%} hit rate needs "
              f"<= {highest_good}. Collect more recordings before choosing.")
        return
    mid = (lowest_clean + highest_good) / 2
    passing = [r[0] for r in rows if r in clean and r in good]
    if not passing:
        print("No single threshold has both zero false positives and a 90% hit rate. Collect more recordings.")
        return
    pick = min(passing, key=lambda v: abs(v - mid))
    hit = next(r for r in rows if r[0] == pick)
    print(f"- Zero false positives from **{lowest_clean}**; hit rate >= {TARGET_HIT_RATE:.0%} up to **{highest_good}**")
    print(f"- Suggested threshold: **{pick}** (sweep value nearest the middle of the safe range; {hit[1] / total_cues:.0%} hits, 0 false positives)")


if __name__ == "__main__":
    main()
