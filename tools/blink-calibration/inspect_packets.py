#!/usr/bin/env python3
"""Check the BLE packet layout the SDKs assume, using the raw bytes from a capture session.

Reads the `pkt` rows (notifications exactly as received, hex) written by
`NeuroSky.Sample calibrate-blink` and reports, without relying on the SDK parsers:

1. Raw EEG layout: packet lengths, constant byte positions (a prefix shows up as bytes that never
   change), and which decoding (offset 0/1/2, big/little endian) gives the smoothest signal.
   The SDKs assume offset 0, big-endian, 10 signed 16-bit samples per 20-byte packet.
2. eSense layout: the first bytes of every packet (the SDKs assume `00 00` then the type at byte 2)
   and the type distribution.
3. 0xEC packets with non-zero bands.
4. PoorSignal 200 (headset off) in 0xEA packets.
5. 0xEB -> 0xEC order and spacing, and how often a 0xEB is not followed by a 0xEC.

With --fixtures FILE it also writes representative packets (hex) for capture-based parser tests.
Standard library only.

    python inspect_packets.py session.csv [--fixtures fixtures.txt]
"""
import argparse
import csv
import statistics
import sys
from collections import Counter

PAIR_WINDOW_MS = 2_000


def load(path):
    pkts = []  # (t, phase, kind, bytes)
    with open(path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if row["type"] != "pkt":
                continue
            kind, _, hexstr = row["value"].partition(":")
            pkts.append((int(row["t_ms"]), row["phase"], kind, bytes.fromhex(hexstr)))
    pkts.sort(key=lambda p: p[0])
    if not pkts:
        sys.exit(f"{path}: no pkt rows — record with the current calibrate-blink tool")
    return pkts


def decode(packet, offset, big_endian):
    out = []
    for i in range(offset, len(packet) - 1, 2):
        hi, lo = (packet[i], packet[i + 1]) if big_endian else (packet[i + 1], packet[i])
        v = (hi << 8) | lo
        out.append(v - 65536 if v >= 32768 else v)
    return out


def raw_section(raw):
    print("## 1. Raw EEG layout\n")
    lengths = Counter(len(p) for p in raw)
    print(f"Packets: {len(raw)} · lengths: {dict(lengths)}\n")
    if not raw:
        print("**MISSING** — no raw notifications recorded.\n")
        return None
    n = min(lengths)
    constant = [i for i in range(n) if len({p[i] for p in raw}) == 1]
    print(f"Byte positions that never change across all packets: {constant or 'none'}"
          f"{'  <- possible prefix/header' if constant else ''}\n")

    print("Smoothness by decoding (median |difference| between consecutive samples; lower = more plausible):\n")
    print("| Offset | Endian | Median |Δ| within packets | Median |Δ| across packet boundary |")
    print("|---|---|---|---|")
    results = []
    for offset in (0, 1, 2):
        for big in (True, False):
            within, boundary, prev_last = [], [], None
            for p in raw:
                s = decode(p, offset, big)
                if not s:
                    continue
                within += [abs(b - a) for a, b in zip(s, s[1:])]
                if prev_last is not None:
                    boundary.append(abs(s[0] - prev_last))
                prev_last = s[-1]
            w = statistics.median(within) if within else float("inf")
            b = statistics.median(boundary) if boundary else float("inf")
            results.append((w, offset, big, b))
            print(f"| {offset} | {'big' if big else 'little'} | {w:.0f} | {b:.0f} |")
    print()
    # Offsets 0 and 2 share the same byte alignment, so within-packet smoothness only tells the
    # alignment (even/odd start) and endianness apart. A prefix shows up as a jump at the packet
    # boundary under offset-0 decoding, and as byte positions that never change.
    by_key = {(off, big): (w, b) for w, off, big, b in results}

    # High bytes of a smooth signal change slowly; low bytes look random. Circular distance so that
    # crossing zero (FF xx -> 00 xx) does not count as a big jump.
    def byte_roughness(parity):
        d = []
        for p in raw:
            seq = p[parity::2]
            d += [min(abs(b - a), 256 - abs(b - a)) for a, b in zip(seq, seq[1:])]
        return statistics.median(d) if d else 0
    even_r, odd_r = byte_roughness(0), byte_roughness(1)
    high_bytes_even = even_r * 2 <= odd_r
    w0, b0 = by_key[(0, True)]
    big_endian_ok = w0 * 2 <= by_key[(0, False)][0]
    alignment_ok = high_bytes_even and big_endian_ok
    boundary_ok = b0 <= 2 * w0 if w0 else True
    lengths_ok = set(lengths) == {20}
    no_constant = not constant
    verdict = "CONFIRMED" if alignment_ok and boundary_ok and lengths_ok and no_constant else "NOT CONFIRMED"
    sdk = decode(raw[0], 0, True)
    print(f"- Byte roughness: even positions {even_r:.0f}, odd positions {odd_r:.0f} — "
          f"{'high bytes at even positions (big-endian from byte 0) - OK' if high_bytes_even else 'NOT the SDK layout'}")
    print(f"- Endianness at offset 0: big {w0:.0f} vs little {by_key[(0, False)][0]:.0f} — "
          f"{'big-endian - OK' if big_endian_ok else 'NOT big-endian'}")
    print(f"- Prefix check (offset 0, big-endian): boundary {b0:.0f} vs within {w0:.0f} — "
          f"{'continuous, no extra bytes' if boundary_ok else 'JUMP at packet boundary - extra bytes at start/end?'}")
    print(f"- Constant byte positions: {'none - OK' if no_constant else constant}")
    print(f"- Packet length: {'always 20 - OK' if lengths_ok else dict(lengths)}")
    print(f"- Samples per packet with the SDK decoding: {len(sdk)}")
    all_sdk = [v for p in raw for v in decode(p, 0, True)]
    print(f"- With the SDK decoding: {sum(v < 0 for v in all_sdk)} negative samples of {len(all_sdk)}, "
          f"range {min(all_sdk)} .. {max(all_sdk)}, exact -32768 (0x8000): {all_sdk.count(-32768)}")
    print(f"\n**Raw layout assumed by the SDKs (offset 0, big-endian, no prefix): {verdict}**\n")
    return verdict


def esense_section(esense):
    print("## 2. eSense layout\n")
    if not esense:
        print("**MISSING** — no eSense notifications recorded.\n")
        return
    prefixes = Counter(p[:2].hex() for p in esense)
    types = Counter(f"{p[2]:02X}" if len(p) > 2 else "short" for p in esense)
    print(f"Packets: {len(esense)} · lengths: {dict(Counter(len(p) for p in esense))}")
    print(f"First two bytes: {dict(prefixes)}  (SDKs assume always 0000)")
    print(f"Byte 2 (type): {dict(types)}\n")


def bands_section(esense):
    print("## 3. 0xEC with non-zero bands\n")
    ec = [p for p in esense if len(p) >= 20 and p[2] == 0xEC]
    nz = [p for p in ec if any(p[3:])]
    print(f"0xEC packets: {len(ec)}, non-zero: {len(nz)} — {'OK' if nz else 'MISSING'}")
    if nz:
        p = nz[len(nz) // 2]
        print(f"Example: {p.hex(' ')}")
        print("Bytes 4/8/12/16 (code bytes before each 3-byte band, if any): "
              f"{sorted({(q[4], q[8], q[12], q[16]) for q in nz})[:5]}")
    print()


def poor_section(esense):
    print("## 4. PoorSignal 200 (headset off)\n")
    ea = [p for p in esense if len(p) >= 11 and p[2] == 0xEA]
    poor = Counter(p[6] for p in ea)
    print(f"0xEA packets: {len(ea)} · PoorSignal values (byte 6): {dict(sorted(poor.items()))}")
    print(f"PoorSignal 200: {poor.get(200, 0)} — {'OK' if poor.get(200) else 'MISSING'}")
    codes = sorted({(p[5], p[7], p[9]) for p in ea})
    print(f"Bytes 5/7/9 (code bytes before poor/attention/meditation): {codes[:5]}\n")


def order_section(esense_t):
    print("## 5. 0xEB -> 0xEC order and spacing\n")
    seq = [(t, p[2]) for t, p in esense_t if len(p) > 2 and p[2] in (0xEA, 0xEB, 0xEC)]
    gaps, orphans_eb, orphans_ec = [], 0, 0
    i = 0
    while i < len(seq):
        t, ty = seq[i]
        if ty == 0xEB:
            nxt = next(((t2, ty2) for t2, ty2 in seq[i + 1:] if ty2 in (0xEB, 0xEC)), None)
            if nxt and nxt[1] == 0xEC and nxt[0] - t <= PAIR_WINDOW_MS:
                gaps.append(nxt[0] - t)
            else:
                orphans_eb += 1
        elif ty == 0xEC:
            prev = next(((t2, ty2) for t2, ty2 in reversed(seq[:i]) if ty2 in (0xEB, 0xEC)), None)
            if not prev or prev[1] != 0xEB or t - prev[0] > PAIR_WINDOW_MS:
                orphans_ec += 1
        i += 1
    eb_total = sum(1 for _, ty in seq if ty == 0xEB)
    print(f"0xEB: {eb_total} · paired with a following 0xEC: {len(gaps)} · 0xEB without 0xEC: {orphans_eb} · "
          f"0xEC without preceding 0xEB: {orphans_ec}")
    if gaps:
        print(f"0xEB -> 0xEC spacing: median {statistics.median(gaps):.0f} ms, min {min(gaps)} ms, max {max(gaps)} ms")
    pattern = Counter("".join({0xEA: "A", 0xEB: "B", 0xEC: "C"}[ty] for _, ty in seq[k:k + 3])
                      for k in range(0, max(0, len(seq) - 2)))
    print(f"Most common 3-packet sequences (A=EA, B=EB, C=EC): {pattern.most_common(4)}\n")


def write_fixtures(path, raw, esense):
    def pick(pred, k=1):
        return [p for p in esense if pred(p)][:k]
    lines = ["# Real-device MWM2 captures for parser tests (hex, as received)", ""]
    groups = [
        ("0xEA, worn", pick(lambda p: len(p) >= 11 and p[2] == 0xEA and p[6] == 0 and p[8] > 0, 2)),
        ("0xEA, PoorSignal 200", pick(lambda p: len(p) >= 11 and p[2] == 0xEA and p[6] == 200, 2)),
        ("0xEB", pick(lambda p: len(p) >= 20 and p[2] == 0xEB and any(p[3:]), 2)),
        ("0xEC", pick(lambda p: len(p) >= 20 and p[2] == 0xEC and any(p[3:]), 2)),
    ]
    neg = next((k for k, p in enumerate(raw) if any(v < 0 for v in decode(p, 0, True))), 0)
    groups.append(("Raw EEG, 5 consecutive (includes negative samples)", raw[neg:neg + 5]))
    for title, pkts in groups:
        lines.append(f"## {title}")
        lines += [p.hex(" ").upper() for p in pkts] or ["(none captured)"]
        lines.append("")
    open(path, "w", encoding="utf-8").write("\n".join(lines))
    print(f"Fixtures written to {path}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("recording")
    ap.add_argument("--fixtures")
    args = ap.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    pkts = load(args.recording)
    raw = [p for _, _, k, p in pkts if k == "raw"]
    esense_t = [(t, p) for t, _, k, p in pkts if k == "esense"]
    esense = [p for _, p in esense_t]

    print(f"# Packet layout check — {args.recording}\n")
    print(f"Notifications: {len(pkts)} ({dict(Counter(k for _, _, k, _ in pkts))})\n")
    raw_section(raw)
    esense_section(esense)
    bands_section(esense)
    poor_section(esense)
    order_section(esense_t)
    if args.fixtures:
        write_fixtures(args.fixtures, raw, esense)


if __name__ == "__main__":
    main()
