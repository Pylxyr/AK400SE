#!/usr/bin/env python3
"""Independent reference for the AK400 DIGITAL SE packet layout (alarm off, celsius).

Line-by-line port of status_message() and the init packet from the AK400 DIGITAL SE support in
deepcool-digital-linux (GPL-3.0): v1nvn's commit 5d70047, src/devices/ak_se.rs, which is the same
byte layout that Nortank12/deepcool-digital-linux now ships in src/devices/ak_series.rs (se_mode,
every byte shifted down by one because the report ID is not sent).

This file is a derivative of that GPL-3.0 code and stays under GPL-3.0; it is test-only and is not
part of the AK400SE.exe that gets released. The rest of the repository is MIT (see LICENSE).

Usage: python tests/ref_se.py <output file>
"""
import sys

def rust_round(x):            # f32::round: half away from zero (inputs are >= 0)
    return int(x + 0.5)

def status_message(show_usage, temp, usage):
    data = [0] * 64
    if not show_usage:        # Mode::CpuTemperature
        data[0] = 19
        data[2] = temp // 100
        data[3] = temp % 100 // 10
        data[4] = temp % 10
    else:                     # Mode::CpuUsage
        data[0] = 76
        data[2] = usage // 100
        data[3] = usage % 100 // 10
        data[4] = usage % 10
    data[1] = 1 if usage < 15 else rust_round(usage / 10.0)
    data[5] = 0               # alarm off
    return bytes(data)

out = []
for su in (0, 1):
    for t in range(256):
        for u in range(101):
            out.append(f"{su} {t} {u} {status_message(su == 1, t, u).hex().upper()}")
init = bytearray(64); init[0] = 170
out.append("init " + init.hex().upper())
# newline="\n": same bytes on every OS (Windows would otherwise write \r\n), so the dump can be hash-compared.
with open(sys.argv[1], "w", newline="\n") as f:
    f.write("\n".join(out) + "\n")
