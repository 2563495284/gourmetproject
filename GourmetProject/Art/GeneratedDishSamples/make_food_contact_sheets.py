#!/usr/bin/env python3
"""Build review sheets showing each transparent sprite beside its grid preview."""

from __future__ import annotations

import json
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


BASE = Path(__file__).resolve().parent
CONFIG = BASE.parents[1] / "Assets/StreamingAssets/Config/tbdishbase.json"
OUT = BASE / "contact_sheets"
FONT_PATH = "/System/Library/Fonts/STHeiti Medium.ttc"
PAGE_W, PAGE_H = 1800, 1960
COLS, ROWS = 2, 4
CARD_W, CARD_H = 840, 420


def font(size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_PATH, size)


def checker(size: tuple[int, int], step: int = 24) -> Image.Image:
    image = Image.new("RGB", size, "#f7f1e6")
    draw = ImageDraw.Draw(image)
    for y in range(0, size[1], step):
        for x in range(0, size[0], step):
            if (x // step + y // step) % 2:
                draw.rectangle((x, y, x + step - 1, y + step - 1), fill="#e9e0d2")
    return image


def fit(image: Image.Image, box: tuple[int, int]) -> Image.Image:
    copy = image.copy()
    copy.thumbnail(box, Image.Resampling.LANCZOS)
    return copy


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    dishes = json.loads(CONFIG.read_text(encoding="utf-8"))
    per_page = COLS * ROWS
    page_count = math.ceil(len(dishes) / per_page)

    for page_index in range(page_count):
        canvas = Image.new("RGB", (PAGE_W, PAGE_H), "#f4ead7")
        draw = ImageDraw.Draw(canvas)
        draw.text((70, 44), f"食物新图总览  {page_index + 1}/{page_count}", font=font(48), fill="#392a22")
        draw.text((70, 106), "左：透明原图　　右：实际格子预览", font=font(27), fill="#765d4e")

        start = page_index * per_page
        for local_index, dish in enumerate(dishes[start : start + per_page]):
            col = local_index % COLS
            row = local_index // COLS
            x = 55 + col * 890
            y = 170 + row * 440
            draw.rounded_rectangle((x, y, x + CARD_W, y + CARD_H), radius=28, fill="#fffaf0", outline="#cbb697", width=3)
            shape = "/".join(dish.get("shapeRows", []))
            draw.text((x + 28, y + 18), f"{dish['name']}  ·  {shape}", font=font(30), fill="#33251e")
            draw.text((x + 28, y + 58), dish["id"], font=font(20), fill="#8a6e5b")

            original = Image.open(BASE / "final" / f"{dish['id']}_sample.png").convert("RGBA")
            preview = Image.open(BASE / "previews" / f"{dish['id']}_grid.png").convert("RGBA")
            left_bg = checker((370, 305))
            right_bg = Image.new("RGB", (370, 305), "#f8efdc")
            original = fit(original, (345, 280))
            preview = fit(preview, (345, 280))
            left_bg.paste(original, ((370 - original.width) // 2, (305 - original.height) // 2), original)
            right_bg.paste(preview, ((370 - preview.width) // 2, (305 - preview.height) // 2), preview)
            canvas.paste(left_bg, (x + 28, y + 98))
            canvas.paste(right_bg, (x + 442, y + 98))

        path = OUT / f"food_review_{page_index + 1:02d}.png"
        canvas.save(path, optimize=True)
        print(path)


if __name__ == "__main__":
    main()
