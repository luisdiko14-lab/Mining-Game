#!/usr/bin/env python3
"""Generate an original, biome-aware BlockCraft soundtrack library.

The tracks are synthesized specifically for this project. No commercial game,
film, or third-party recordings are sampled or redistributed.
Requires NumPy (see tools/requirements.txt).
"""
from __future__ import annotations

import json
import math
import struct
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets" / "generated" / "music"
SAMPLE_RATE = 44_100
DURATION_SECONDS = 60
CHANNELS = 2

# Each entry uses its own key, rhythm and scale so the world has distinct musical regions.
TRACKS = [
    ("title_theme", "A warm, spacious main-menu theme", 76, 146.83, [0, 2, 4, 7, 9], [0, 5, 3, 4]),
    ("plains_day", "Open-air piano and soft strings for plains", 82, 164.81, [0, 2, 4, 7, 9], [0, 5, 3, 4]),
    ("forest_canopy", "Woodland bells, mellow pads and gentle plucks", 72, 196.00, [0, 3, 5, 7, 10], [0, 3, 5, 4]),
    ("desert_wind", "Sparse warm notes and drifting desert air", 68, 146.83, [0, 2, 4, 7, 9], [0, 4, 5, 3]),
    ("snowfall", "Slow glassy tones for snowy landscapes", 64, 174.61, [0, 2, 5, 7, 9], [0, 5, 3, 4]),
    ("ocean_current", "Rolling low chords and shimmering high notes", 70, 130.81, [0, 2, 4, 7, 11], [0, 3, 5, 4]),
    ("jungle_mist", "Layered organic pulses for dense jungle", 88, 164.81, [0, 3, 5, 7, 10], [0, 5, 3, 6]),
    ("savanna_wind", "Wide, sunlit chords with a steady pulse", 78, 146.83, [0, 2, 4, 7, 9], [0, 4, 5, 3]),
    ("swamp_lights", "Mysterious hollow notes and soft low drones", 66, 123.47, [0, 3, 5, 8, 10], [0, 3, 6, 4]),
    ("cherry_grove", "Bright bell-like melody and airy strings", 74, 185.00, [0, 2, 4, 7, 11], [0, 5, 3, 4]),
    ("surface_night", "Quiet night music with a gentle heartbeat", 58, 110.00, [0, 3, 5, 7, 10], [0, 3, 5, 4]),
    ("cave_depths", "Low resonant drones and distant crystal tones", 56, 98.00, [0, 2, 5, 7, 9], [0, 5, 3, 4]),
    ("crystal_grotto", "Shimmering arpeggios for amethyst geodes", 84, 207.65, [0, 2, 5, 7, 11], [0, 5, 3, 6]),
    ("abandoned_mineshaft", "Old timber, echoing percussion and muted strings", 62, 116.54, [0, 3, 5, 7, 10], [0, 3, 4, 6]),
    ("echo_chamber", "Deep, spacious tones for rare underground finds", 60, 92.50, [0, 2, 4, 7, 11], [0, 5, 3, 4]),
    ("rare_discovery", "A short celebratory motif for rare discoveries", 96, 164.81, [0, 2, 4, 7, 9], [0, 5, 3, 4]),
    ("underwater_world", "Soft flowing layers for submerged exploration", 62, 130.81, [0, 2, 5, 7, 9], [0, 3, 5, 4]),
    ("mountain_pass", "Broad cinematic chords for high mountain terrain", 70, 146.83, [0, 2, 4, 7, 9], [0, 5, 3, 4]),
]


def synthesize_track(name: str, tempo: int, root_hz: float, scale: list[int], chords: list[int], seed: int) -> Path:
    sample_count = SAMPLE_RATE * DURATION_SECONDS
    t = np.arange(sample_count, dtype=np.float32) / np.float32(SAMPLE_RATE)
    beat_duration = 60.0 / tempo
    beat_position = t / np.float32(beat_duration)
    beat_index = np.floor(beat_position).astype(np.int32)
    beat_fraction = beat_position - beat_index
    bar_index = (beat_index // 4) % len(chords)
    note_pattern = np.array([0, 2, 1, 3, 2, 4, 3, 1], dtype=np.int32)
    scale_values = np.asarray(scale, dtype=np.float32)
    chord_values = np.asarray(chords, dtype=np.float32)
    semitones = scale_values[note_pattern[beat_index % len(note_pattern)]] + chord_values[bar_index]
    melody_hz = np.float32(root_hz) * np.exp2(semitones / np.float32(12.0))
    melody_phase = np.cumsum(melody_hz, dtype=np.float64) * (2.0 * math.pi / SAMPLE_RATE)
    note_envelope = np.exp(-beat_fraction * np.float32(2.9))
    melody = np.sin(melody_phase).astype(np.float32) * note_envelope

    pad_semitones = chord_values[bar_index]
    pad_hz = np.float32(root_hz * 0.5) * np.exp2(pad_semitones / np.float32(12.0))
    pad_phase = np.cumsum(pad_hz, dtype=np.float64) * (2.0 * math.pi / SAMPLE_RATE)
    fifth_phase = np.cumsum(pad_hz * np.float32(1.4983), dtype=np.float64) * (2.0 * math.pi / SAMPLE_RATE)
    pad = np.sin(pad_phase).astype(np.float32) * np.float32(0.13)
    pad += np.sin(fifth_phase).astype(np.float32) * np.float32(0.055)

    bass_hz = np.where((beat_index % 4) < 2, root_hz * 0.5, root_hz * 0.375).astype(np.float32)
    bass_phase = np.cumsum(bass_hz, dtype=np.float64) * (2.0 * math.pi / SAMPLE_RATE)
    bass = np.sin(bass_phase).astype(np.float32) * np.float32(0.095)

    # A soft, filtered noise bed gives each landscape a distinct atmosphere.
    rng = np.random.default_rng(seed)
    noise_points = rng.normal(0.0, 1.0, int(DURATION_SECONDS * 85) + 2).astype(np.float32)
    noise_times = np.arange(noise_points.size, dtype=np.float32) / np.float32(85.0)
    atmosphere = np.interp(t, noise_times, noise_points).astype(np.float32) * np.float32(0.018)
    pulse = np.exp(-beat_fraction * np.float32(13.0)) * np.sin(2.0 * math.pi * 48.0 * t).astype(np.float32)
    pulse *= np.float32(0.028)

    mono = melody * np.float32(0.25) + pad + bass + atmosphere + pulse
    fade_in = np.clip(t / np.float32(2.0), 0.0, 1.0)
    fade_out = np.clip((np.float32(DURATION_SECONDS) - t) / np.float32(2.0), 0.0, 1.0)
    mono *= np.minimum(fade_in, fade_out)
    peak = float(np.max(np.abs(mono)))
    if peak > 0:
        mono *= np.float32(0.84 / peak)

    # Slight channel decorrelation creates a wide stereo field without hard panning.
    width_phase = np.sin(2.0 * math.pi * (0.11 + (seed % 7) * 0.013) * t).astype(np.float32)
    left = mono * (np.float32(0.94) + width_phase * np.float32(0.045))
    right = mono * (np.float32(0.94) - width_phase * np.float32(0.045))
    stereo = np.empty((sample_count, CHANNELS), dtype="<i2")
    stereo[:, 0] = np.clip(left * np.float32(32767.0), -32767, 32767).astype("<i2")
    stereo[:, 1] = np.clip(right * np.float32(32767.0), -32767, 32767).astype("<i2")

    OUTPUT.mkdir(parents=True, exist_ok=True)
    path = OUTPUT / f"{name}.wav"
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(CHANNELS)
        wav.setsampwidth(2)
        wav.setframerate(SAMPLE_RATE)
        wav.writeframes(stereo.tobytes())
    return path


def main() -> None:
    manifest = {
        "format": "PCM WAV",
        "sample_rate": SAMPLE_RATE,
        "channels": CHANNELS,
        "duration_seconds": DURATION_SECONDS,
        "tracks": [],
    }
    total_bytes = 0
    for index, (name, description, tempo, root, scale, chords) in enumerate(TRACKS):
        path = synthesize_track(name, tempo, root, scale, chords, 0xB10C + index * 7919)
        size = path.stat().st_size
        total_bytes += size
        manifest["tracks"].append({
            "name": name,
            "description": description,
            "file": path.name,
            "bytes": size,
        })
        print(f"Generated {path.name}: {size / 1_000_000:.2f} MB")

    (OUTPUT / "music_manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {len(TRACKS)} original tracks: {total_bytes / 1_000_000:.1f} MB uncompressed")
    if total_bytes < 180_000_000:
        raise RuntimeError(f"Soundtrack library is unexpectedly small ({total_bytes} bytes).")


if __name__ == "__main__":
    main()
