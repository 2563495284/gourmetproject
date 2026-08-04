#!/usr/bin/env python3

import argparse
from pathlib import Path

from PIL import Image


def fit_to_canvas(image: Image.Image, width: int, height: int, padding: float) -> Image.Image:
    rgba = image.convert("RGBA")
    alpha = rgba.getchannel("A")
    bbox = alpha.getbbox()
    if bbox is None:
        raise ValueError("Input image has no visible pixels.")

    subject = rgba.crop(bbox)
    max_width = max(1, round(width * (1 - 2 * padding)))
    max_height = max(1, round(height * (1 - 2 * padding)))
    scale = min(max_width / subject.width, max_height / subject.height)
    resized = subject.resize(
        (max(1, round(subject.width * scale)), max(1, round(subject.height * scale))),
        Image.Resampling.LANCZOS,
    )

    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    x = (width - resized.width) // 2
    y = (height - resized.height) // 2
    canvas.alpha_composite(resized, (x, y))
    return canvas


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--width", type=int, required=True)
    parser.add_argument("--height", type=int, required=True)
    parser.add_argument("--padding", type=float, default=0.06)
    args = parser.parse_args()

    if not 0 <= args.padding < 0.5:
        raise ValueError("padding must be in [0, 0.5)")

    output = Path(args.out)
    output.parent.mkdir(parents=True, exist_ok=True)
    result = fit_to_canvas(
        Image.open(args.input),
        args.width,
        args.height,
        args.padding,
    )
    result.save(output)
    print(f"output={output} size={result.size} alpha_bbox={result.getchannel('A').getbbox()}")


if __name__ == "__main__":
    main()
