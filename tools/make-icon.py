#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 penguinwokrs
"""Draws assets/app.ico: a dark keyboard with red keys, the same glyph the tray draws at run time.

Standard library only (zlib + struct). Each size is a PNG inside the ICO container, which Windows
Vista and later read. Shapes are drawn on a 4x supersampled grid for smooth edges.

    python3 tools/make-icon.py
"""
import pathlib
import struct
import zlib

SIZES = (16, 24, 32, 48, 64, 128, 256)
BODY = (0x26, 0x2A, 0x30)
KEY = (0xFF, 0x30, 0x30)
SS = 4


def rounded_rect(x0, y0, x1, y1, r):
    def inside(x, y):
        if not (x0 <= x <= x1 and y0 <= y <= y1):
            return False
        cx = min(max(x, x0 + r), x1 - r)
        cy = min(max(y, y0 + r), y1 - r)
        return (x - cx) ** 2 + (y - cy) ** 2 <= r * r
    return inside


def rect(x0, y0, x1, y1):
    return lambda x, y: x0 <= x <= x1 and y0 <= y <= y1


def shapes():
    """In a 16x16 design space, matching TrayIcons.cs."""
    yield rounded_rect(0.5, 3.5, 15.5, 13.0, 2.0), BODY
    for row in range(2):
        for col in range(4):
            x, y = 2.2 + col * 3.0, 5.4 + row * 2.6
            yield rect(x, y, x + 2.0, y + 1.6), KEY
    yield rect(4.6, 10.6, 11.4, 11.8), KEY


def render(size):
    grid = size * SS
    scale = 16.0 / grid
    drawn = list(shapes())
    rows = []
    for py in range(size):
        row = bytearray([0])  # PNG filter: none
        for px in range(size):
            r = g = b = a = 0
            for sy in range(SS):
                for sx in range(SS):
                    x = (px * SS + sx + 0.5) * scale
                    y = (py * SS + sy + 0.5) * scale
                    color = None
                    for hit, c in drawn:
                        if hit(x, y):
                            color = c
                    if color:
                        r += color[0]; g += color[1]; b += color[2]; a += 255
            n = SS * SS
            if a:
                row += bytes((r * 255 // a, g * 255 // a, b * 255 // a, a // n))
            else:
                row += bytes(4)
        rows.append(bytes(row))
    return png(size, b"".join(rows))


def png(size, raw):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def main():
    images = [render(s) for s in SIZES]
    offset = 6 + 16 * len(images)
    directory = b""
    for size, data in zip(SIZES, images):
        dim = 0 if size == 256 else size
        directory += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    out = pathlib.Path(__file__).resolve().parent.parent / "assets" / "app.ico"
    out.parent.mkdir(exist_ok=True)
    out.write_bytes(struct.pack("<HHH", 0, 1, len(images)) + directory + b"".join(images))
    print(f"wrote {out} ({out.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
