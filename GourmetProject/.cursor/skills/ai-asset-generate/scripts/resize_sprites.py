#!/usr/bin/env python3
"""Post-process generated sprites: resize to game dimensions + update Unity .meta PPU."""

import os
import sys
from pathlib import Path
from PIL import Image

PROJECT_ROOT = Path(__file__).resolve().parents[4]
SPRITE_BASE = PROJECT_ROOT / "Assets" / "GameMain" / "Resources" / "Sprites"

# (relative_path, target_w, target_h, ppu)
SPECS = [
    # Backgrounds — Tiled mode, wide format
    ("Backgrounds/bg_sky.png",           2048, 512, 100),
    ("Backgrounds/bg_far_mountains.png", 2048, 512, 100),
    ("Backgrounds/bg_mid_forest.png",    2048, 512, 100),
    ("Backgrounds/bg_near_trees.png",    2048, 512, 100),

    # Player static (1×2 units @ PPU=256)
    ("Characters/player_idle.png", 256, 512, 256),

    # Terrain
    ("Items/platform_tile.png",  256, 256, 256),
    ("Items/wall_tile.png",      256, 256, 256),
    ("Items/slope_left.png",    1024, 256, 256),
    ("Items/slope_right.png",   1024, 256, 256),
    ("Items/spikes.png",         256, 160, 256),

    # Checkpoints
    ("Items/checkpoint_inactive.png", 512, 512, 256),
    ("Items/checkpoint_active.png",   512, 512, 256),
    ("Items/checkpoint_endpoint.png", 768,1280, 256),
]

MONSTERS = [
    "mosquito", "shadow", "moth", "light_eater", "light_scale",
    "firefly", "vine", "ambush_spider", "mist_spirit", "echo_bat",
    "stone_eye_closed", "stone_eye_open", "light_shadow_bug",
]

for m in MONSTERS:
    SPECS.append((f"Characters/Monsters/{m}.png", 512, 512, 256))


def update_meta_ppu(meta_path, ppu):
    """Update or insert spritePixelsToUnits in a Unity .meta file."""
    if not meta_path.exists():
        print(f"  WARNING: .meta not found: {meta_path}")
        return

    text = meta_path.read_text()
    lines = text.splitlines()
    new_lines = []
    found = False
    in_sprite = False

    for line in lines:
        if line.strip().startswith("spritePixelsToUnits:"):
            new_lines.append(f"  spritePixelsToUnits: {ppu}")
            found = True
            continue
        new_lines.append(line)

    if not found:
        print(f"  WARNING: spritePixelsToUnits not found in meta, PPU not set")
    else:
        meta_path.write_text("\n".join(new_lines) + "\n")
        print(f"  PPU set to {ppu}")


def main():
    count = 0
    for rel_path, tw, th, ppu in SPECS:
        full = SPRITE_BASE / rel_path
        if not full.exists():
            print(f"SKIP (not found): {rel_path}")
            continue

        img = Image.open(full)
        old_w, old_h = img.size
        print(f"RESIZE: {rel_path}  {old_w}x{old_h} -> {tw}x{th}  (PPU={ppu})")

        # Keep aspect ratio, resize to fit within target box using LANCZOS
        img_resized = img.resize((tw, th), Image.LANCZOS)
        img_resized.save(full, "PNG")

        meta_path = Path(str(full) + ".meta")
        update_meta_ppu(meta_path, ppu)
        count += 1

    print(f"\nDone. {count} sprite(s) processed.")


if __name__ == "__main__":
    main()
