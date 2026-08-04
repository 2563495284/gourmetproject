#!/usr/bin/env python3
"""Turn one chroma-key ImageGen output into a transparent sprite and grid preview."""

from __future__ import annotations

import argparse
import shutil
import subprocess
from pathlib import Path


def run(*args: str) -> None:
    subprocess.run(args, check=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--id", required=True)
    parser.add_argument("--input", required=True)
    parser.add_argument("--shape", required=True)
    parser.add_argument("--width", type=int, required=True)
    parser.add_argument("--height", type=int, required=True)
    args = parser.parse_args()

    base = Path(__file__).resolve().parent
    source = base / "sources" / f"{args.id}_chroma.png"
    transparent = base / ".codex-tmp" / f"{args.id}_transparent.png"
    final = base / "final" / f"{args.id}_sample.png"
    preview = base / "previews" / f"{args.id}_grid.png"
    python = "/Users/hcm-b0451/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3"
    chroma = "/Users/hcm-b0451/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py"
    cell = base.parents[1] / "Assets/GameMain/Content/Resources/Sprites/UI/board_cell.png"

    source.parent.mkdir(parents=True, exist_ok=True)
    transparent.parent.mkdir(parents=True, exist_ok=True)
    final.parent.mkdir(parents=True, exist_ok=True)
    preview.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(args.input, source)

    run(
        python,
        chroma,
        "--input",
        str(source),
        "--out",
        str(transparent),
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
        "--force",
    )
    run(
        python,
        str(base / "prepare_dish_sprite.py"),
        "--input",
        str(transparent),
        "--out",
        str(final),
        "--width",
        str(args.width),
        "--height",
        str(args.height),
        "--padding",
        "0.04",
    )
    run(
        python,
        str(base / "make_grid_preview.py"),
        "--dish",
        str(final),
        "--shape",
        args.shape,
        "--cell-sprite",
        str(cell),
        "--out",
        str(preview),
    )


if __name__ == "__main__":
    main()
