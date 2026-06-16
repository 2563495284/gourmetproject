#!/usr/bin/env python3
"""把 Gemini 生成图里"假透明"的中性灰棋盘背景抠成真 alpha，并裁切到内容包围盒。

做法：从图像四边的"棋盘色"像素出发做 4 邻接 flood-fill，命中的像素 alpha 置 0。
连通填充能保住被黑描边包围的内部浅色（如米饭白），不会误删。
完成后按 alpha>0 自动裁切，写回 RGBA PNG。
"""

import sys
from collections import deque
from pathlib import Path

from PIL import Image

# 中性灰判定：通道极差小（接近灰）且不太暗（保住黑描边/深阴影）。
NEUTRAL_SPREAD = 24
MIN_BRIGHT = 82
# flood-fill 容差：邻居与种子亮度差在此范围内继续扩散。
NEIGHBOR_TOL = 60


def is_bg(px):
    r, g, b = px[0], px[1], px[2]
    mx = max(r, g, b)
    mn = min(r, g, b)
    return (mx - mn) <= NEUTRAL_SPREAD and mn >= MIN_BRIGHT


def dealpha(path: Path) -> bool:
    img = Image.open(path).convert("RGBA")
    w, h = img.size
    px = img.load()
    visited = bytearray(w * h)
    q = deque()

    def seed(x, y):
        i = y * w + x
        if not visited[i] and is_bg(px[x, y]):
            visited[i] = 1
            q.append((x, y))

    for x in range(w):
        seed(x, 0)
        seed(x, h - 1)
    for y in range(h):
        seed(0, y)
        seed(w - 1, y)

    removed = 0
    while q:
        x, y = q.popleft()
        r, g, b, a = px[x, y]
        px[x, y] = (r, g, b, 0)
        removed += 1
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h:
                i = ny * w + nx
                if not visited[i] and is_bg(px[nx, ny]):
                    visited[i] = 1
                    q.append((nx, ny))

    bbox = img.getbbox()
    if bbox:
        img = img.crop(bbox)
    img.save(path)
    print(f"  {path.name}: removed {removed}px bg, cropped to {img.size}")
    return True


def demagenta(path: Path) -> bool:
    """抠掉纯品红(#FF00FF)实底，带紫边淡出与 despill，再裁切到内容包围盒。"""
    img = Image.open(path).convert("RGBA")
    w, h = img.size
    px = img.load()
    removed = 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            diff = min(r, b) - g  # 绿被压得越狠越像品红
            if r > 100 and b > 100 and diff > 45:
                px[x, y] = (r, g, b, 0)
                removed += 1
            elif r > 110 and b > 110 and 18 < diff <= 45:
                fade = int(255 * (45 - diff) / (45 - 18))
                ng = min(255, g + diff // 2)  # despill：把绿补回来，淡化紫边
                px[x, y] = (r, ng, b, min(a, fade))

    bbox = img.getbbox()
    if bbox:
        img = img.crop(bbox)
    img.save(path)
    print(f"  {path.name}: removed {removed}px magenta, cropped to {img.size}")
    return True


def main():
    args = sys.argv[1:]
    mode = "neutral"
    if args and args[0] in ("magenta", "neutral"):
        mode = args[0]
        args = args[1:]
    fn = demagenta if mode == "magenta" else dealpha
    for arg in args:
        p = Path(arg)
        if p.exists():
            fn(p)
        else:
            print(f"  SKIP missing: {p}")


if __name__ == "__main__":
    main()
