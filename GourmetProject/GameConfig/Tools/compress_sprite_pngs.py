#!/usr/bin/env python3
"""Compress item and dish PNG sprites in-place.

The tool keeps image dimensions and alpha intact, and only rewrites files when
the optimizer can make them smaller.
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

from PIL import Image


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SPRITE_DIRS = [
    PROJECT_ROOT / "Assets/GameMain/Resources/Sprites/Items",
    PROJECT_ROOT / "Assets/GameMain/Resources/Sprites/Dishes",
]


@dataclass
class Result:
    path: Path
    before: int
    after: int
    changed: bool

    @property
    def saved(self) -> int:
        return max(0, self.before - self.after)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Compress item and dish PNG sprites in-place.")
    parser.add_argument(
        "--min-bytes",
        type=int,
        default=0,
        help="Only process PNGs at or above this size. Default: 0, process all.",
    )
    parser.add_argument(
        "--over-1mb",
        action="store_true",
        help="Shortcut for --min-bytes 1048576.",
    )
    parser.add_argument(
        "--opt",
        default="4",
        help="oxipng optimization level. Default: 4.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Show what would be processed without writing files.",
    )
    parser.add_argument(
        "--palette",
        action="store_true",
        help="Lossy-compress with an optimized indexed palette before PNG optimization.",
    )
    parser.add_argument(
        "--palette-colors",
        type=int,
        default=256,
        help="Maximum colors for --palette. Default: 256.",
    )
    parser.add_argument(
        "--top",
        type=int,
        default=20,
        help="Show the largest reductions at the end. Default: 20.",
    )
    return parser.parse_args()


def image_size(path: Path) -> tuple[int, int]:
    with Image.open(path) as image:
        return image.size


def sprite_paths(min_bytes: int) -> list[Path]:
    paths: list[Path] = []
    for directory in SPRITE_DIRS:
        paths.extend(sorted(path for path in directory.glob("*.png") if path.stat().st_size >= min_bytes))
    return paths


def run_oxipng(path: Path, opt: str, dry_run: bool) -> None:
    command = [
        "oxipng",
        "--opt",
        opt,
        "--strip",
        "safe",
        "--alpha",
        "--quiet",
        str(path),
    ]
    if dry_run:
        command.insert(1, "--dry-run")
    subprocess.run(command, check=True)


def run_palette_optimize(path: Path, colors: int, dry_run: bool) -> None:
    if dry_run:
        return

    before = path.stat().st_size
    with Image.open(path) as source:
        image = source.convert("RGBA")
        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as handle:
            tmp_path = Path(handle.name)
        try:
            palette = image.quantize(
                colors=colors,
                method=Image.Quantize.FASTOCTREE,
                dither=Image.Dither.NONE,
            )
            palette.save(tmp_path, format="PNG", optimize=True, compress_level=9)
            if tmp_path.stat().st_size < before:
                shutil.move(str(tmp_path), path)
        finally:
            if tmp_path.exists():
                tmp_path.unlink()


def run_pillow_optimize(path: Path, dry_run: bool) -> None:
    if dry_run:
        return

    before = path.stat().st_size
    with Image.open(path) as source:
        image = source.convert("RGBA")
        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as handle:
            tmp_path = Path(handle.name)
        try:
            image.save(tmp_path, format="PNG", optimize=True, compress_level=9)
            if tmp_path.stat().st_size < before:
                shutil.move(str(tmp_path), path)
        finally:
            if tmp_path.exists():
                tmp_path.unlink()


def compress_one(path: Path, opt: str, dry_run: bool, has_oxipng: bool, palette: bool, palette_colors: int) -> Result:
    before_size = path.stat().st_size
    before_dimensions = image_size(path)

    if palette:
        run_palette_optimize(path, palette_colors, dry_run)

    if has_oxipng:
        run_oxipng(path, opt, dry_run)
    else:
        run_pillow_optimize(path, dry_run)

    after_size = path.stat().st_size
    after_dimensions = image_size(path)
    if after_dimensions != before_dimensions:
        raise RuntimeError(f"{path} dimensions changed: {before_dimensions} -> {after_dimensions}")

    return Result(path=path, before=before_size, after=after_size, changed=after_size != before_size)


def fmt_size(size: int) -> str:
    original = float(size)
    if original < 1024:
        return f"{size}B"
    if original < 1024 * 1024:
        return f"{original / 1024:.1f}KB"
    return f"{original / (1024 * 1024):.2f}MB"


def main() -> int:
    args = parse_args()
    min_bytes = 1024 * 1024 if args.over_1mb else args.min_bytes
    paths = sprite_paths(min_bytes)
    has_oxipng = shutil.which("oxipng") is not None

    if not paths:
        print("No PNG sprites matched.")
        return 0

    backend = "oxipng" if has_oxipng else "Pillow"
    print(f"Compressing {len(paths)} PNG sprite(s) with {backend}...")
    if args.dry_run:
        print("Dry run: no files will be written.")

    results: list[Result] = []
    for index, path in enumerate(paths, start=1):
        try:
            result = compress_one(path, args.opt, args.dry_run, has_oxipng, args.palette, args.palette_colors)
        except Exception as error:
            print(f"ERROR: {path}: {error}", file=sys.stderr)
            return 1
        results.append(result)
        if result.changed:
            print(
                f"[{index:03}/{len(paths)}] {path.relative_to(PROJECT_ROOT)} "
                f"{fmt_size(result.before)} -> {fmt_size(result.after)} "
                f"saved {fmt_size(result.saved)}"
            )

    total_before = sum(result.before for result in results)
    total_after = sum(result.after for result in results)
    changed = sum(1 for result in results if result.changed)
    print()
    print(
        f"Done. {changed}/{len(results)} file(s) smaller. "
        f"Total {fmt_size(total_before)} -> {fmt_size(total_after)}, saved {fmt_size(total_before - total_after)}."
    )

    top = sorted((result for result in results if result.saved > 0), key=lambda item: item.saved, reverse=True)[: args.top]
    if top:
        print()
        print("Top savings:")
        for result in top:
            print(
                f"  {result.path.relative_to(PROJECT_ROOT)}: "
                f"{fmt_size(result.before)} -> {fmt_size(result.after)} "
                f"saved {fmt_size(result.saved)}"
            )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
