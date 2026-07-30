#!/usr/bin/env python3
"""Regenerate and post-process the active-item icon set.

Generation calls the bundled Codex image CLI. It requires OPENAI_API_KEY.
Use --postprocess-only to rebuild transparent 512px outputs from existing raw PNGs.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent
MANIFEST = ROOT / "prompts.json"
MASTERS = ROOT / "masters"
RAW = ROOT / "raw"
FINAL = ROOT / "final"
PREVIEWS = ROOT / "previews"


def codex_home() -> Path:
    configured = os.environ.get("CODEX_HOME")
    return Path(configured).expanduser() if configured else Path.home() / ".codex"


def run(command: list[str]) -> None:
    subprocess.run(command, check=True)


def generation_prompt(item: dict[str, object]) -> str:
    change = str(item["change"])
    if item["family"] == "strengthen":
        return f"""Use case: precise-object-edit
Asset type: game UI icon designed for an 80x80 display, authored at high resolution
Input images: Image 1 is the fixed Strengthen supply-box master.
Primary request: {change}
Constraints: preserve the exact orange box silhouette, raised rim, two front plank divisions, four broad corner caps, centered hexagonal front inset, perspective, placement, moderate painterly cartoon style, and perfectly flat solid #ff00ff chroma-key background. Keep one dominant content symbol only, with at most one small supporting shape. Bold near-black outlines; readable at 80x80. No text, letters, logos, watermark, cast shadow, paper, ticket, tiny texture, or clutter. Do not use #ff00ff inside the icon."""

    return f"""Use case: precise-object-edit
Asset type: game UI icon designed for an 80x80 display, authored at high resolution
Input images: Image 1 is the fixed Adjust command-ticket master.
Primary request: {change}
Constraints: preserve the exact pale-cyan ticket silhouette, tilt, edge notches, thin inner border, right dashed tear line, tiny blank circular seal, centered placement, moderate painterly cartoon style, and perfectly flat solid #ff00ff chroma-key background. Use one large central pictogram only. Bold near-black outlines; readable at 80x80. No words, letters, numbers, handwriting, extra stamps, logos, watermark, cast shadow, boxes, crates, tiny texture, or clutter. Do not use #ff00ff inside the icon."""


def master_for(item: dict[str, object]) -> Path:
    filename = (
        "strengthen_box.png"
        if item["family"] == "strengthen"
        else "adjust_ticket.png"
    )
    return MASTERS / filename


def postprocess(raw_path: Path, final_path: Path, chroma_helper: Path) -> None:
    alpha_path = final_path.with_name(f"{final_path.stem}-alpha.png")
    run(
        [
            sys.executable,
            str(chroma_helper),
            "--input",
            str(raw_path),
            "--out",
            str(alpha_path),
            "--auto-key",
            "border",
            "--soft-matte",
            "--transparent-threshold",
            "12",
            "--opaque-threshold",
            "220",
            "--despill",
            "--edge-contract",
            "1",
        ]
    )
    with Image.open(alpha_path) as image:
        rgba = image.convert("RGBA")
        resized = rgba.resize((512, 512), Image.Resampling.LANCZOS)
        resized.save(final_path)
    alpha_path.unlink()


def make_contact_sheet(items: list[dict[str, object]]) -> None:
    columns = 5
    icon_size = 80
    cell_width = 150
    cell_height = 112
    rows = (len(items) + columns - 1) // columns
    sheet = Image.new("RGBA", (columns * cell_width, rows * cell_height), "#20252B")
    draw = ImageDraw.Draw(sheet)

    for index, item in enumerate(items):
        x = (index % columns) * cell_width
        y = (index // columns) * cell_height
        with Image.open(FINAL / f"{item['resource']}.png") as icon:
            preview = icon.convert("RGBA").resize(
                (icon_size, icon_size), Image.Resampling.LANCZOS
            )
        sheet.alpha_composite(preview, (x + (cell_width - icon_size) // 2, y + 4))
        label = str(item["resource"]).removeprefix("active_")
        draw.text((x + 5, y + 88), label, fill="#E8EDF2")

    PREVIEWS.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(PREVIEWS / "active-item-icons-80px.jpg", quality=95)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--postprocess-only", action="store_true")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--quality", choices=["low", "medium", "high", "auto"], default="medium")
    args = parser.parse_args()

    items = json.loads(MANIFEST.read_text(encoding="utf-8"))
    RAW.mkdir(parents=True, exist_ok=True)
    FINAL.mkdir(parents=True, exist_ok=True)

    home = codex_home()
    image_cli = home / "skills/.system/imagegen/scripts/image_gen.py"
    chroma_helper = home / "skills/.system/imagegen/scripts/remove_chroma_key.py"

    if not chroma_helper.exists():
        raise SystemExit(f"Missing chroma helper: {chroma_helper}")

    if not args.postprocess_only:
        if not os.environ.get("OPENAI_API_KEY"):
            raise SystemExit("OPENAI_API_KEY is not set.")
        if not image_cli.exists():
            raise SystemExit(f"Missing image CLI: {image_cli}")

    for item in items:
        resource = str(item["resource"])
        raw_path = RAW / f"{resource}.png"
        final_path = FINAL / f"{resource}.png"
        master_path = master_for(item)

        if item.get("master"):
            if not raw_path.exists() or args.force:
                shutil.copy2(master_path, raw_path)
        elif not args.postprocess_only and (args.force or not raw_path.exists()):
            command = [
                sys.executable,
                str(image_cli),
                "edit",
                "--image",
                str(master_path),
                "--prompt",
                generation_prompt(item),
                "--model",
                "gpt-image-2",
                "--quality",
                args.quality,
                "--size",
                "1024x1024",
                "--out",
                str(raw_path),
            ]
            if args.force:
                command.append("--force")
            run(command)

        if not raw_path.exists():
            raise SystemExit(f"Missing raw image: {raw_path}")
        postprocess(raw_path, final_path, chroma_helper)

    make_contact_sheet(items)
    print(f"Wrote {len(items)} icons to {FINAL}")
    print(f"80px preview: {PREVIEWS / 'active-item-icons-80px.jpg'}")


if __name__ == "__main__":
    main()
