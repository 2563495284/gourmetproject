#!/usr/bin/env python3
"""Copy approved dish samples into Resources and keep Unity sprite rects valid."""

from __future__ import annotations

import json
import re
import shutil
import uuid
from pathlib import Path

from PIL import Image


BASE = Path(__file__).resolve().parent
PROJECT = BASE.parents[1]
CONFIG = PROJECT / "Assets/StreamingAssets/Config/tbdishbase.json"
DEST = PROJECT / "Assets/GameMain/Content/Resources/Sprites/Dishes"
TEMPLATE_META = DEST / "eggtart.png.meta"


def set_first_sprite_rect(text: str, width: int, height: int) -> str:
    lines = text.splitlines()
    in_sheet = False
    in_rect = False
    updated = set()
    for index, line in enumerate(lines):
        if line == "  spriteSheet:":
            in_sheet = True
            continue
        if in_sheet and line == "      rect:":
            in_rect = True
            continue
        if not in_rect:
            continue
        match = re.match(r"(        (x|y|width|height): )(-?\d+)$", line)
        if not match:
            continue
        key = match.group(2)
        value = {"x": 0, "y": 0, "width": width, "height": height}[key]
        lines[index] = f"{match.group(1)}{value}"
        updated.add(key)
        if len(updated) == 4:
            return "\n".join(lines) + "\n"
    raise RuntimeError("Could not locate the first Unity sprite rect")


def make_meta(dish_id: str, width: int, height: int) -> str:
    text = TEMPLATE_META.read_text(encoding="utf-8")
    old_internal = "-5003873946472326112"
    internal = str(-int(uuid.uuid4().hex[:15], 16))
    sprite_id = uuid.uuid4().hex
    main_sprite_id = uuid.uuid4().hex
    text = re.sub(r"^guid: [0-9a-f]+$", f"guid: {uuid.uuid4().hex}", text, flags=re.MULTILINE)
    text = text.replace("eggtart_0", f"{dish_id}_0")
    text = text.replace(old_internal, internal)
    ids = re.findall(r"(?m)^(\s+spriteID: )[0-9a-f]+$", text)
    replacement_ids = iter((sprite_id, main_sprite_id))
    text = re.sub(
        r"(?m)^(\s+spriteID: )[0-9a-f]+$",
        lambda match: match.group(1) + next(replacement_ids),
        text,
    )
    return set_first_sprite_rect(text, width, height)


def main() -> None:
    dishes = json.loads(CONFIG.read_text(encoding="utf-8"))
    for dish in dishes:
        dish_id = dish["id"]
        source = BASE / "final" / f"{dish_id}_sample.png"
        target = DEST / f"{dish_id}.png"
        meta = DEST / f"{dish_id}.png.meta"
        shutil.copy2(source, target)
        with Image.open(source) as image:
            width, height = image.size
        if meta.exists():
            text = set_first_sprite_rect(meta.read_text(encoding="utf-8"), width, height)
        else:
            text = make_meta(dish_id, width, height)
        meta.write_text(text, encoding="utf-8")
        print(f"{dish_id}: {width}x{height}")


if __name__ == "__main__":
    main()
