#!/usr/bin/env python3
"""Generate original BlockCraft voxel textures, a manifest, and CC0-style synthetic audio.

Only Python's standard library is used. All visual and audio data is generated
procedurally by this script; no third-party or game-vendor assets are included.
"""
from __future__ import annotations

import hashlib
import json
import math
import random
import struct
import wave
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BLOCKS = ROOT / "assets" / "data" / "blocks.json"
OUTPUT = ROOT / "assets" / "generated"
TILE_SIZE = 16
ATLAS_COLUMNS = 8
SAMPLE_RATE = 22050

PALETTES: dict[str, tuple[int, int, int]] = {
    "air": (0, 0, 0), "grass": (94, 166, 76), "dirt": (127, 83, 52),
    "stone": (116, 119, 126), "deepslate": (67, 72, 83), "sand": (218, 194, 128),
    "sandstone": (205, 180, 115), "log": (133, 91, 52), "wood": (169, 122, 73),
    "planks": (174, 127, 76), "leaves": (64, 137, 72), "water": (59, 126, 218),
    "lava": (245, 88, 28), "glass": (177, 218, 231), "coal": (67, 68, 73),
    "iron": (180, 133, 108), "gold": (217, 179, 54), "diamond": (56, 205, 195),
    "snow": (232, 239, 247), "ice": (157, 208, 240), "bedrock": (55, 57, 63),
    "cobble": (111, 113, 119), "gravel": (143, 137, 127), "clay": (164, 166, 161),
    "cactus": (68, 137, 72), "birch": (194, 174, 139), "spruce": (89, 75, 57),
    "obsidian": (47, 36, 72), "torch": (255, 194, 87), "glowstone": (241, 204, 120),
    "redstone": (157, 54, 48), "lapis": (55, 91, 177), "copper": (183, 110, 70),
    "wool": (222, 221, 215), "bookshelf": (153, 100, 57), "crafting": (159, 113, 70),
    "furnace": (101, 101, 105), "bricks": (164, 86, 73), "mossy": (91, 122, 79),
    "glass_pane": (177, 218, 231),
}


def base_color(name: str) -> tuple[int, int, int]:
    lowered = name.lower()
    for key, color in PALETTES.items():
        if key in lowered:
            return color
    return (150, 150, 150)


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    body = kind + payload
    return struct.pack(">I", len(payload)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)


def write_png(path: Path, width: int, height: int, rgba: bytes) -> None:
    rows = b"".join(b"\x00" + rgba[y * width * 4:(y + 1) * width * 4] for y in range(height))
    header = struct.pack(">2I5B", width, height, 8, 6, 0, 0, 0)
    payload = b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", header)
    payload += png_chunk(b"IDAT", zlib.compress(rows, 9)) + png_chunk(b"IEND", b"")
    path.write_bytes(payload)


def generate_atlas() -> None:
    block_data = json.loads(BLOCKS.read_text(encoding="utf-8"))
    names = sorted({texture for block in block_data for texture in block.get("textures", {}).values() if texture != "air"})
    tile_count = len(names)
    rows = (tile_count + ATLAS_COLUMNS - 1) // ATLAS_COLUMNS
    width, height = ATLAS_COLUMNS * TILE_SIZE, rows * TILE_SIZE
    pixels = bytearray(width * height * 4)
    manifest: dict[str, dict[str, float | int]] = {}
    for index, name in enumerate(names):
        tile_x, tile_y = index % ATLAS_COLUMNS, index // ATLAS_COLUMNS
        digest = hashlib.sha256(name.encode("utf-8")).digest()
        rng = random.Random(int.from_bytes(digest[:8], "big"))
        base = base_color(name)
        transparent = any(token in name for token in ("glass", "water", "leaves", "ice"))
        for y in range(TILE_SIZE):
            for x in range(TILE_SIZE):
                variation = rng.randint(-18, 18)
                accent = 10 if (x + y + (digest[8] % 5)) % 7 == 0 else 0
                rgb = tuple(max(0, min(255, channel + variation + accent)) for channel in base)
                alpha = 155 if transparent else 255
                if "glass" in name and (x in (0, 1, 14, 15) or y in (0, 1, 14, 15)):
                    alpha = 225
                if "water" in name:
                    alpha = 185
                if "leaves" in name and rng.random() < 0.12:
                    alpha = 0
                if "torch" in name and 5 <= x <= 10 and 2 <= y <= 14:
                    rgb = (255, 220 + rng.randint(0, 25), 110)
                offset = ((tile_y * TILE_SIZE + y) * width + tile_x * TILE_SIZE + x) * 4
                pixels[offset:offset + 4] = bytes((*rgb, alpha))
        manifest[name] = {
            "index": index,
            "x": tile_x,
            "y": tile_y,
            "u0": tile_x / ATLAS_COLUMNS,
            "v0": tile_y / rows,
            "u1": (tile_x + 1) / ATLAS_COLUMNS,
            "v1": (tile_y + 1) / rows,
        }
    OUTPUT.mkdir(parents=True, exist_ok=True)
    write_png(OUTPUT / "block_atlas.png", width, height, bytes(pixels))
    (OUTPUT / "atlas_manifest.json").write_text(
        json.dumps({"tile_size": TILE_SIZE, "columns": ATLAS_COLUMNS, "rows": rows, "tiles": manifest}, indent=2) + "\n",
        encoding="utf-8",
    )


def write_wav(path: Path, samples: list[float]) -> None:
    with wave.open(str(path), "wb") as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(SAMPLE_RATE)
        data = bytearray()
        for sample in samples:
            value = max(-1.0, min(1.0, sample))
            data.extend(struct.pack("<h", int(value * 32767)))
        stream.writeframes(data)


def generate_audio() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    ambient_seconds = 6
    ambient: list[float] = []
    for frame in range(SAMPLE_RATE * ambient_seconds):
        time = frame / SAMPLE_RATE
        fade = min(1.0, frame / 500, (SAMPLE_RATE * ambient_seconds - frame) / 500)
        chord = (
            0.055 * math.sin(2 * math.pi * 110 * time)
            + 0.035 * math.sin(2 * math.pi * 165 * time)
            + 0.025 * math.sin(2 * math.pi * 220 * time)
            + 0.018 * math.sin(2 * math.pi * 330 * time)
        )
        ambient.append(chord * max(0.0, fade))
    write_wav(OUTPUT / "ambient_loop.wav", ambient)

    rng = random.Random(0xB10C)
    break_length = int(SAMPLE_RATE * 0.14)
    breaking = []
    for frame in range(break_length):
        time = frame / SAMPLE_RATE
        envelope = math.exp(-time * 26)
        grit = rng.uniform(-1.0, 1.0)
        tone = math.sin(2 * math.pi * (180 - 700 * time) * time)
        breaking.append((0.45 * grit + 0.25 * tone) * envelope)
    write_wav(OUTPUT / "block_break.wav", breaking)


def main() -> None:
    if not BLOCKS.is_file():
        raise FileNotFoundError(f"Missing block registry: {BLOCKS}")
    generate_atlas()
    generate_audio()
    print(f"Generated procedural atlas and audio in {OUTPUT}")


if __name__ == "__main__":
    main()
