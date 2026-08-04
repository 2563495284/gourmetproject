#!/usr/bin/env python3

import argparse
from pathlib import Path

from PIL import Image


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dish", required=True)
    parser.add_argument("--shape", required=True, help="Rows separated with '/', for example .X/XX")
    parser.add_argument("--cell-sprite", required=True)
    parser.add_argument("--cell-size", type=int, default=512)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    rows = args.shape.split("/")
    width = max(len(row) for row in rows)
    height = len(rows)
    canvas_size = (width * args.cell_size, height * args.cell_size)

    canvas = Image.new("RGBA", canvas_size, (246, 225, 174, 255))
    cell = Image.open(args.cell_sprite).convert("RGBA").resize(
        (args.cell_size, args.cell_size),
        Image.Resampling.LANCZOS,
    )
    for y in range(height):
        for x in range(width):
            if x >= len(rows[y]) or rows[y][x] != "X":
                continue
            canvas.alpha_composite(cell, (x * args.cell_size, y * args.cell_size))

    dish = Image.open(args.dish).convert("RGBA").resize(
        canvas_size,
        Image.Resampling.LANCZOS,
    )
    alpha = dish.getchannel("A")
    outside_pixels = 0
    for y in range(canvas_size[1]):
        row = rows[y // args.cell_size]
        for x in range(canvas_size[0]):
            if row[x // args.cell_size] != "X" and alpha.getpixel((x, y)) != 0:
                outside_pixels += 1
    if outside_pixels:
        raise ValueError(
            f"Dish exceeds configured footprint by {outside_pixels} visible pixel(s)."
        )

    canvas.alpha_composite(dish, (0, 0))

    output = Path(args.out)
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.convert("RGB").save(output, quality=95, subsampling=0)
    print(f"footprint_outside_pixels=0 output={output}")


if __name__ == "__main__":
    main()
