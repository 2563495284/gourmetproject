#!/usr/bin/env python3
"""Convert a baked transparency-checkerboard background (as produced by some
image models that return JPEG) into a real alpha channel, then save as PNG.

Strategy: the generated art is fully enclosed by bold black outlines, and the
background is a light low-saturation checkerboard. We flood-fill from the image
borders through connected "background-like" pixels (near-gray and light) and set
those to alpha=0. Interior light areas (eyes, aprons) are protected by the black
outlines, so the flood fill never reaches them.
"""

import sys
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image

# Background detection thresholds (tuned for white/gray checkerboard).
MAX_CHANNEL_SPREAD = 26   # near-gray: max(R,G,B) - min(R,G,B) small
MIN_BRIGHTNESS = 150      # light pixels only


def background_mask(rgb: np.ndarray) -> np.ndarray:
    r = rgb[:, :, 0].astype(np.int16)
    g = rgb[:, :, 1].astype(np.int16)
    b = rgb[:, :, 2].astype(np.int16)
    spread = np.maximum(np.maximum(r, g), b) - np.minimum(np.minimum(r, g), b)
    brightness = (r + g + b) / 3.0
    return (spread <= MAX_CHANNEL_SPREAD) & (brightness >= MIN_BRIGHTNESS)


def flood_from_borders(candidate: np.ndarray) -> np.ndarray:
    h, w = candidate.shape
    visited = np.zeros((h, w), dtype=bool)
    q = deque()

    for x in range(w):
        for y in (0, h - 1):
            if candidate[y, x] and not visited[y, x]:
                visited[y, x] = True
                q.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if candidate[y, x] and not visited[y, x]:
                visited[y, x] = True
                q.append((y, x))

    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and candidate[ny, nx] and not visited[ny, nx]:
                visited[ny, nx] = True
                q.append((ny, nx))

    return visited


def process(path: Path) -> Path:
    img = Image.open(path).convert("RGB")
    rgb = np.asarray(img)

    candidate = background_mask(rgb)
    bg = flood_from_borders(candidate)

    alpha = np.where(bg, 0, 255).astype(np.uint8)
    rgba = np.dstack([rgb, alpha])

    out = path.with_suffix(".png")
    Image.fromarray(rgba, mode="RGBA").save(out)
    removed = int(bg.sum())
    total = bg.size
    print(f"  {path.name} -> {out.name}  (alpha-cleared {removed/total:.0%})")
    return out


def main():
    paths = [Path(p) for p in sys.argv[1:]]
    if not paths:
        print("usage: checker_to_alpha.py <image.jpg> [...]")
        sys.exit(1)
    for p in paths:
        if not p.exists():
            print(f"  SKIP missing {p}")
            continue
        process(p)


if __name__ == "__main__":
    main()
