#!/usr/bin/env python3
"""Validate generated decoration icons and build a compact review sheet."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--icons", type=Path, required=True)
    parser.add_argument("--preview", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--columns", type=int, default=5)
    return parser.parse_args()


def load_font(size: int) -> ImageFont.ImageFont:
    candidates = (
        Path("/System/Library/Fonts/Supplemental/Arial.ttf"),
        Path("/System/Library/Fonts/Helvetica.ttc"),
    )
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size=size)
    return ImageFont.load_default()


def inspect_icon(path: Path) -> dict[str, object]:
    with Image.open(path) as image:
        image.load()
        rgba = image.convert("RGBA")
        alpha = rgba.getchannel("A")
        bbox = alpha.getbbox()
        corners = [
            alpha.getpixel((0, 0)),
            alpha.getpixel((rgba.width - 1, 0)),
            alpha.getpixel((0, rgba.height - 1)),
            alpha.getpixel((rgba.width - 1, rgba.height - 1)),
        ]
        margin = None
        coverage = 0.0
        if bbox:
            margin = min(
                bbox[0],
                bbox[1],
                rgba.width - bbox[2],
                rgba.height - bbox[3],
            )
            coverage = ((bbox[2] - bbox[0]) * (bbox[3] - bbox[1])) / (
                rgba.width * rgba.height
            )
        return {
            "size": list(rgba.size),
            "mode": image.mode,
            "bbox": list(bbox) if bbox else None,
            "minimum_margin": margin,
            "bbox_coverage": round(coverage, 4),
            "corner_alpha": corners,
            "valid": (
                rgba.size == (512, 512)
                and image.mode == "RGBA"
                and bbox is not None
                and all(value == 0 for value in corners)
                and margin is not None
                and margin >= 12
                and 0.20 <= coverage <= 0.90
            ),
        }


def main() -> None:
    args = parse_args()
    rows = json.loads(args.config.read_text(encoding="utf-8"))
    args.preview.parent.mkdir(parents=True, exist_ok=True)
    args.report.parent.mkdir(parents=True, exist_ok=True)

    thumb_size = 96
    cell_width = 176
    cell_height = 132
    columns = args.columns
    row_count = (len(rows) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * cell_width, row_count * cell_height), "#f8f0dc")
    draw = ImageDraw.Draw(sheet)
    font = load_font(13)
    small_font = load_font(11)

    report: list[dict[str, object]] = []
    for index, row in enumerate(rows):
        item_id = row["id"]
        base = item_id.removeprefix("item_")
        path = args.icons / f"{base}.png"
        result: dict[str, object] = {
            "index": index + 1,
            "id": item_id,
            "name": row["name"],
            "path": str(path),
            "exists": path.exists(),
        }
        column = index % columns
        line = index // columns
        x = column * cell_width
        y = line * cell_height
        draw.rounded_rectangle(
            (x + 4, y + 4, x + cell_width - 4, y + cell_height - 4),
            radius=8,
            fill="#fffaf0",
            outline="#dfcfaa",
            width=1,
        )

        if path.exists():
            details = inspect_icon(path)
            result.update(details)
            with Image.open(path) as image:
                rgba = image.convert("RGBA")
                rgba.thumbnail((thumb_size, thumb_size), Image.Resampling.LANCZOS)
                px = x + (cell_width - rgba.width) // 2
                py = y + 7 + (thumb_size - rgba.height) // 2
                sheet.paste(rgba, (px, py), rgba)
        else:
            result["valid"] = False
            draw.rectangle((x + 40, y + 18, x + 136, y + 90), outline="#c5534b", width=3)
            draw.line((x + 40, y + 18, x + 136, y + 90), fill="#c5534b", width=3)
            draw.line((x + 136, y + 18, x + 40, y + 90), fill="#c5534b", width=3)

        label = f"{index + 1:02d} {base}"
        if len(label) > 25:
            label = label[:24] + "…"
        draw.text((x + 9, y + 104), label, font=font, fill="#2d2820")
        status = "OK" if result.get("valid") else "CHECK"
        status_color = "#21864a" if status == "OK" else "#c5534b"
        draw.text((x + 9, y + 119), status, font=small_font, fill=status_color)
        report.append(result)

    sheet.save(args.preview, quality=92)
    summary = {
        "expected": len(rows),
        "present": sum(1 for row in report if row["exists"]),
        "valid": sum(1 for row in report if row.get("valid")),
        "items": report,
    }
    args.report.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({key: summary[key] for key in ("expected", "present", "valid")}, ensure_ascii=False))


if __name__ == "__main__":
    main()
