#!/usr/bin/env python3
"""Force a square PNG to a circular alpha silhouette, keeping the canvas size."""

from __future__ import annotations

import math
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter


def circle_mask(path: Path, padding_ratio: float = 0.02, feather: float = 1.2) -> None:
    with Image.open(path) as source:
        image = source.convert("RGBA")

    width, height = image.size
    size = min(width, height)
    left = (width - size) // 2
    top = (height - size) // 2
    image = image.crop((left, top, left + size, top + size))

    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    inset = max(1, int(round(size * padding_ratio)))
    draw.ellipse((inset, inset, size - 1 - inset, size - 1 - inset), fill=255)
    if feather > 0:
        mask = mask.filter(ImageFilter.GaussianBlur(feather))

    image.putalpha(ImageChops.multiply(image.getchannel("A"), mask))
    image.save(path)
    opaque = sum(1 for value in image.getchannel("A").getdata() if value > 8)
    coverage = opaque / float(size * size)
    print(f"  {path.name}: circular mask {size}x{size}, coverage={coverage:.2f}")
    if coverage < 0.42:
        print(f"  WARNING: {path.name} looks sparse after circular mask (coverage={coverage:.2f})")


def main() -> None:
    if len(sys.argv) < 2:
        raise SystemExit("usage: circle_mask.py <png> [png...]")
    for arg in sys.argv[1:]:
        path = Path(arg)
        if not path.exists():
            raise SystemExit(f"missing: {path}")
        circle_mask(path)


if __name__ == "__main__":
    main()
