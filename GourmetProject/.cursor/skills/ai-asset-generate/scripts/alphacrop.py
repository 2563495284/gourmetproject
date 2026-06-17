#!/usr/bin/env python3
"""按 alpha 通道把图裁切到不透明内容包围盒（用于 9-slice UI 件去掉透明边距）。"""

import sys
from pathlib import Path

from PIL import Image


ALPHA_THRESHOLD = 40


def crop(path: Path) -> None:
    img = Image.open(path).convert("RGBA")
    r, g, b, a = img.split()
    # 把低于阈值的半透明残影直接清零，避免噪点撑大包围盒。
    cleaned_alpha = a.point(lambda v: v if v >= ALPHA_THRESHOLD else 0)
    img.putalpha(cleaned_alpha)
    bbox = cleaned_alpha.getbbox()
    if not bbox:
        print(f"  SKIP empty alpha: {path.name}")
        return
    cropped = img.crop(bbox)
    cropped.save(path)
    print(f"  {path.name}: {img.size} -> {cropped.size}")


def main():
    for arg in sys.argv[1:]:
        p = Path(arg)
        if p.exists():
            crop(p)
        else:
            print(f"  SKIP missing: {p}")


if __name__ == "__main__":
    main()
