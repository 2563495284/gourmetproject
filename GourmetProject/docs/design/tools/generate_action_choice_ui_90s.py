from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


W, H = 1920, 1080
S = 2
OUT = Path(__file__).resolve().parents[1] / "v2" / "局内" / "action_choice_ui_90s_cartoon.png"

rng = random.Random(1994)


def sc(v: float) -> int:
    return int(round(v * S))


def box(x0: float, y0: float, x1: float, y1: float) -> tuple[int, int, int, int]:
    return sc(x0), sc(y0), sc(x1), sc(y1)


def pt(x: float, y: float) -> tuple[int, int]:
    return sc(x), sc(y)


def jitter(v: float, amount: float) -> float:
    return v + rng.uniform(-amount, amount)


def rounded(draw: ImageDraw.ImageDraw, xy, r, fill, outline="#111111", width=6, shadow=True):
    if shadow:
        sx0, sy0, sx1, sy1 = xy
        draw.rounded_rectangle(
            (sx0 + sc(7), sy0 + sc(8), sx1 + sc(7), sy1 + sc(8)),
            radius=sc(r),
            fill="#bf7b2f66",
        )
    draw.rounded_rectangle(xy, radius=sc(r), fill=fill, outline=outline, width=sc(width))
    # A second slightly imperfect pass gives the clean ink outline a hand-drawn wobble.
    ox, oy = sc(rng.uniform(-1.3, 1.3)), sc(rng.uniform(-1.3, 1.3))
    x0, y0, x1, y1 = xy
    draw.rounded_rectangle(
        (x0 + ox, y0 + oy, x1 + ox, y1 + oy),
        radius=max(0, sc(r + rng.uniform(-1.0, 1.0))),
        outline=outline,
        width=max(1, sc(width * 0.34)),
    )


def line(draw, points, fill="#111111", width=6, joint="curve"):
    draw.line([pt(x, y) for x, y in points], fill=fill, width=sc(width), joint=joint)


def ellipse(draw, xy, fill, outline="#111111", width=6, shadow=False):
    if shadow:
        x0, y0, x1, y1 = xy
        draw.ellipse((x0 + sc(5), y0 + sc(6), x1 + sc(5), y1 + sc(6)), fill="#bf7b2f55")
    draw.ellipse(xy, fill=fill, outline=outline, width=sc(width))
    x0, y0, x1, y1 = xy
    draw.ellipse(
        (
            x0 + sc(rng.uniform(-1, 1)),
            y0 + sc(rng.uniform(-1, 1)),
            x1 + sc(rng.uniform(-1, 1)),
            y1 + sc(rng.uniform(-1, 1)),
        ),
        outline=outline,
        width=max(1, sc(width * 0.25)),
    )


def poly(draw, points, fill, outline="#111111", width=6):
    draw.polygon([pt(x, y) for x, y in points], fill=fill)
    draw.line([pt(x, y) for x, y in points + [points[0]]], fill=outline, width=sc(width), joint="curve")


def draw_star(draw, cx, cy, r_outer, r_inner, fill="#ffd24f", outline="#111111", width=5, rotation=-math.pi / 2):
    points = []
    for i in range(10):
        r = r_outer if i % 2 == 0 else r_inner
        a = rotation + i * math.pi / 5
        points.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    poly(draw, points, fill, outline, width)


def draw_coin(draw, cx, cy, r=18):
    ellipse(draw, box(cx - r, cy - r, cx + r, cy + r), "#e7ae31", width=5)
    ellipse(draw, box(cx - r * 0.52, cy - r * 0.52, cx + r * 0.52, cy + r * 0.52), "#ffd86d", width=3)
    line(draw, [(cx - r * 0.15, cy - r * 0.52), (cx - r * 0.05, cy + r * 0.48)], "#7d4b11", 3)


def draw_money_bag(draw, cx, cy, scale=1.0):
    s = scale
    poly(
        draw,
        [
            (cx - 28 * s, cy - 8 * s),
            (cx - 18 * s, cy - 36 * s),
            (cx + 18 * s, cy - 36 * s),
            (cx + 28 * s, cy - 7 * s),
            (cx + 22 * s, cy + 28 * s),
            (cx - 20 * s, cy + 29 * s),
        ],
        "#e6b23f",
        width=5,
    )
    rounded(draw, box(cx - 18 * s, cy - 50 * s, cx + 18 * s, cy - 28 * s), 7 * s, "#d38a2d", width=4, shadow=False)
    line(draw, [(cx - 24 * s, cy - 15 * s), (cx + 22 * s, cy - 19 * s)], "#111111", 4)
    draw_coin(draw, cx, cy + 3 * s, 10 * s)


def draw_stomach(draw, cx, cy, scale=1.0):
    s = scale
    pts = [
        (cx - 6 * s, cy - 40 * s),
        (cx + 15 * s, cy - 34 * s),
        (cx + 16 * s, cy - 14 * s),
        (cx + 42 * s, cy - 2 * s),
        (cx + 42 * s, cy + 28 * s),
        (cx + 15 * s, cy + 43 * s),
        (cx - 18 * s, cy + 32 * s),
        (cx - 35 * s, cy + 5 * s),
        (cx - 24 * s, cy - 20 * s),
    ]
    poly(draw, pts, "#ff8c53", width=6)
    line(draw, [(cx + 6 * s, cy - 22 * s), (cx + 7 * s, cy + 12 * s), (cx - 9 * s, cy + 21 * s)], "#9d392d", 4)


def draw_gear(draw, cx, cy, scale=1.0):
    s = scale
    pts = []
    for i in range(16):
        r = 36 * s if i % 2 == 0 else 28 * s
        a = i * math.pi / 8
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    poly(draw, pts, "#78b6d8", width=5)
    ellipse(draw, box(cx - 12 * s, cy - 12 * s, cx + 12 * s, cy + 12 * s), "#fff1c7", width=4)


def draw_eye(draw, cx, cy, scale=1.0):
    s = scale
    pts = []
    for i in range(18):
        a = math.pi * i / 17
        pts.append((cx - 46 * s + 92 * s * i / 17, cy - math.sin(a) * 23 * s))
    for i in range(18):
        a = math.pi * i / 17
        pts.append((cx + 46 * s - 92 * s * i / 17, cy + math.sin(a) * 23 * s))
    poly(draw, pts, "#fff6d8", width=5)
    ellipse(draw, box(cx - 18 * s, cy - 18 * s, cx + 18 * s, cy + 18 * s), "#2f8dd8", width=4)
    ellipse(draw, box(cx - 7 * s, cy - 7 * s, cx + 7 * s, cy + 7 * s), "#111111", "#111111", width=2)


def draw_bowl(draw, cx, cy, scale=1.0):
    s = scale
    line(draw, [(cx - 34 * s, cy - 55 * s), (cx - 43 * s, cy - 82 * s), (cx - 30 * s, cy - 104 * s)], "#111111", 5)
    line(draw, [(cx + 5 * s, cy - 50 * s), (cx + 2 * s, cy - 81 * s), (cx + 20 * s, cy - 101 * s)], "#111111", 5)
    line(draw, [(cx + 42 * s, cy - 48 * s), (cx + 54 * s, cy - 77 * s), (cx + 42 * s, cy - 99 * s)], "#111111", 5)
    ellipse(draw, box(cx - 82 * s, cy - 58 * s, cx + 82 * s, cy - 1 * s), "#fff0b8", width=6)
    draw.arc(box(cx - 80 * s, cy - 42 * s, cx + 80 * s, cy + 86 * s), 0, 180, fill="#111111", width=sc(7))
    draw.pieslice(box(cx - 82 * s, cy - 58 * s, cx + 82 * s, cy + 82 * s), 0, 180, fill="#f07f35", outline="#111111", width=sc(6))
    ellipse(draw, box(cx - 53 * s, cy - 42 * s, cx + 12 * s, cy + 2 * s), "#f5cc4b", width=4)
    ellipse(draw, box(cx + 15 * s, cy - 43 * s, cx + 58 * s, cy - 4 * s), "#83bb65", width=4)
    line(draw, [(cx - 82 * s, cy - 1 * s), (cx + 82 * s, cy - 1 * s)], "#111111", 6)


def draw_gift(draw, cx, cy, scale=1.0):
    s = scale
    rounded(draw, box(cx - 72 * s, cy - 31 * s, cx + 72 * s, cy + 70 * s), 9 * s, "#f7902e", width=7)
    rounded(draw, box(cx - 84 * s, cy - 58 * s, cx + 84 * s, cy - 18 * s), 8 * s, "#f6c052", width=7, shadow=False)
    line(draw, [(cx, cy - 58 * s), (cx, cy + 70 * s)], "#111111", 7)
    line(draw, [(cx - 72 * s, cy + 13 * s), (cx + 72 * s, cy + 9 * s)], "#111111", 5)
    ellipse(draw, box(cx - 47 * s, cy - 98 * s, cx - 3 * s, cy - 52 * s), "#f15d44", width=6)
    ellipse(draw, box(cx + 3 * s, cy - 98 * s, cx + 47 * s, cy - 52 * s), "#f15d44", width=6)


def draw_event_swirl(draw, cx, cy, scale=1.0):
    s = scale
    ellipse(draw, box(cx - 75 * s, cy - 70 * s, cx + 75 * s, cy + 70 * s), "#8b54ba", width=7, shadow=True)
    for k in range(3):
        off = k * 18 * s
        draw.arc(
            box(cx - 47 * s + off * 0.28, cy - 45 * s + off * 0.1, cx + 47 * s - off * 0.12, cy + 45 * s - off * 0.18),
            205,
            535,
            fill="#fff1c7",
            width=sc(7),
        )
    draw_star(draw, cx + 45 * s / S, cy - 52 * s / S, 18 * s / S, 8 * s / S, "#ffd85c", width=4)


def draw_shopping_bag(draw, cx, cy, scale=1.0, fill="#f15aa8"):
    s = scale
    rounded(draw, box(cx - 25 * s, cy - 31 * s, cx + 25 * s, cy + 36 * s), 6 * s, fill, width=5, shadow=False)
    draw.arc(box(cx - 15 * s, cy - 46 * s, cx + 15 * s, cy - 6 * s), 180, 360, fill="#111111", width=sc(5))
    line(draw, [(cx - 25 * s, cy - 6 * s), (cx + 25 * s, cy - 9 * s)], "#111111", 4)


def draw_imp(draw, cx, cy, scale=1.0):
    s = scale
    poly(draw, [(cx - 50 * s, cy - 42 * s), (cx - 80 * s, cy - 69 * s), (cx - 69 * s, cy - 20 * s)], "#7d2fa2", width=6)
    poly(draw, [(cx + 50 * s, cy - 42 * s), (cx + 80 * s, cy - 69 * s), (cx + 69 * s, cy - 20 * s)], "#7d2fa2", width=6)
    ellipse(draw, box(cx - 62 * s, cy - 52 * s, cx + 62 * s, cy + 70 * s), "#9f47c9", width=7, shadow=True)
    ellipse(draw, box(cx - 32 * s, cy - 12 * s, cx - 8 * s, cy + 14 * s), "#111111", "#111111", 2)
    ellipse(draw, box(cx + 8 * s, cy - 12 * s, cx + 32 * s, cy + 14 * s), "#111111", "#111111", 2)
    draw.arc(box(cx - 34 * s, cy + 8 * s, cx + 34 * s, cy + 48 * s), 20, 160, fill="#111111", width=sc(6))


def draw_hourglass(draw, cx, cy, scale=1.0):
    s = scale
    line(draw, [(cx - 22 * s, cy - 28 * s), (cx + 22 * s, cy - 28 * s)], width=5)
    line(draw, [(cx - 22 * s, cy + 28 * s), (cx + 22 * s, cy + 28 * s)], width=5)
    line(draw, [(cx - 18 * s, cy - 24 * s), (cx + 18 * s, cy + 24 * s)], width=5)
    line(draw, [(cx + 18 * s, cy - 24 * s), (cx - 18 * s, cy + 24 * s)], width=5)
    poly(draw, [(cx - 13 * s, cy - 17 * s), (cx + 13 * s, cy - 17 * s), (cx, cy - 3 * s)], "#f6c052", width=1)
    poly(draw, [(cx - 13 * s, cy + 17 * s), (cx + 13 * s, cy + 17 * s), (cx, cy + 3 * s)], "#f6c052", width=1)


def draw_card(draw, x, y, w, h, kind):
    rounded(draw, box(x, y, x + w, y + h), 18, "#fff5d2", width=7, shadow=True)
    # Top ornament without text.
    line(draw, [(x + 28, y + 58), (x + w * 0.34, y + 52)], "#111111", 3)
    line(draw, [(x + w * 0.66, y + 54), (x + w - 28, y + 58)], "#111111", 3)
    draw_star(draw, x + w * 0.5, y + 55, 18, 8, "#ffd24f", width=4)

    if kind == "reward":
        draw_gift(draw, x + w * 0.5, y + h * 0.46, 1.2)
        for i in range(5):
            draw_coin(draw, x + w * 0.34 + i * 26, y + h * 0.69 + rng.uniform(-5, 5), 11)
    elif kind == "meal":
        draw_bowl(draw, x + w * 0.5, y + h * 0.48, 1.12)
        ellipse(draw, box(x + w * 0.42, y + h * 0.68, x + w * 0.58, y + h * 0.82), "#fff9e5", width=5, shadow=False)
        draw_star(draw, x + w * 0.5, y + h * 0.75, 25, 12, "#ffcf46", width=4)
        poly(draw, [(x + w * 0.59, y + h * 0.66), (x + w * 0.65, y + h * 0.6), (x + w * 0.64, y + h * 0.73)], "#f04b32", width=5)
    else:
        draw_event_swirl(draw, x + w * 0.5, y + h * 0.48, 1.1)
        for i, col in enumerate(["#e95139", "#ffd24f", "#78b6d8"]):
            draw_star(draw, x + w * (0.35 + i * 0.15), y + h * 0.70 + (i % 2) * 16, 18, 8, col, width=4)

    # Icon-only time/action strip.
    rounded(draw, box(x + 55, y + h - 92, x + w - 55, y + h - 34), 20, "#ffe3a1", width=5, shadow=False)
    draw_hourglass(draw, x + 92, y + h - 63, 0.62)
    for i in range(5):
        fill = "#f08a2b" if (kind == "reward" and i < 4) or (kind != "reward" and i < 2) else "#fff8db"
        ellipse(draw, box(x + 145 + i * 38, y + h - 76, x + 170 + i * 38, y + h - 51), fill, width=3)


def draw_progress(draw):
    x, y, w, h = 420, 83, 1048, 52
    rounded(draw, box(x, y, x + w, y + h), 16, "#fff3cb", width=5, shadow=False)
    seg = w / 8
    for i in range(8):
        fill = "#b9ed9f" if i < 2 else "#fff8df"
        if i == 2:
            fill = "#fff0b8"
        rounded(draw, box(x + i * seg + 3, y + 4, x + (i + 1) * seg - 3, y + h - 4), 10, fill, "#111111", 2, False)
        if i > 0:
            line(draw, [(x + i * seg, y + 5), (x + i * seg, y + h - 4)], "#111111", 2)
    draw_shopping_bag(draw, x + seg * 2.8, y - 10, 0.72, "#f15aa8")
    draw_shopping_bag(draw, x + seg * 2.92, y - 4, 0.62, "#f6c052")
    draw_coin(draw, x + seg * 4.9, y - 1, 18)
    draw_imp(draw, x + seg * 7.08, y + 20, 0.6)
    poly(draw, [(x + seg * 1.62, y + h + 8), (x + seg * 1.47, y + h + 76), (x + seg * 1.77, y + h + 76)], "#2692dd", width=6)
    ellipse(draw, box(x + seg * 1.58, y + h + 78, x + seg * 1.67, y + h + 87), "#fff4d1", width=3)
    # Tiny dot markers replace the prototype's numerals.
    for i in range(8):
        ellipse(draw, box(x + seg * (i + 0.5) - 5, y + h / 2 - 5, x + seg * (i + 0.5) + 5, y + h / 2 + 5), "#111111", "#111111", 1)


def draw_left_panel(draw):
    rounded(draw, box(12, 10, 222, H - 12), 0, "#ffe5ad", width=7, shadow=False)
    rounded(draw, box(28, 36, 190, 132), 12, "#ffd38c", width=5)
    # Week/card icon without any numerals.
    rounded(draw, box(62, 55, 154, 113), 10, "#fff5d2", width=4, shadow=False)
    for i in range(5):
        x = 80 + i * 15
        ellipse(draw, box(x, 75, x + 9, 84), "#ef7f2d" if i == 0 else "#ffd35b", width=2)

    rounded(draw, box(32, 158, 184, 238), 12, "#fff0cc", width=5)
    draw_money_bag(draw, 82, 204, 0.62)
    for i in range(3):
        draw_coin(draw, 123 + i * 17, 210 - i * 7, 9)

    rounded(draw, box(28, 350, 190, 562), 12, "#fff0cc", width=5)
    draw_star(draw, 109, 405, 30, 14, "#ffd24f", width=5)
    for row in range(2):
        for col in range(4):
            ellipse(draw, box(63 + col * 26, 455 + row * 36, 82 + col * 26, 474 + row * 36), "#f08a2b" if row == 0 and col < 2 else "#fff8df", width=3)

    rounded(draw, box(28, 583, 190, 704), 12, "#fff0cc", width=5)
    draw_bowl(draw, 110, 661, 0.47)
    for i in range(2):
        draw_star(draw, 70 + i * 74, 616, 16, 7, "#78b6d8", width=3)

    rounded(draw, box(32, 806, 196, 913), 14, "#2f8dd8", width=6)
    draw_eye(draw, 114, 860, 0.68)
    rounded(draw, box(32, 940, 196, 1047), 14, "#2f8dd8", width=6)
    draw_gear(draw, 114, 994, 0.72)


def draw_right_panel(draw):
    rounded(draw, box(1690, 10, W - 12, H - 12), 0, "#fff5d2", width=7, shadow=False)
    for row in range(7):
        for col in range(2):
            cx = 1748 + col * 104
            cy = 92 + row * 105
            ellipse(draw, box(cx - 39, cy - 39, cx + 39, cy + 39), "#fff9e5", width=4, shadow=False)
            if row in (0, 2) and col == 0:
                draw_star(draw, cx, cy, 17, 8, "#ffd24f", width=3)
    line(draw, [(1690, 828), (W - 12, 828)], width=6)
    rounded(draw, box(1724, 848, 1878, 944), 4, "#fff9e5", width=5, shadow=False)
    draw_money_bag(draw, 1801, 897, 0.55)
    rounded(draw, box(1724, 966, 1878, 1062), 4, "#fff9e5", width=5, shadow=False)
    draw_bowl(draw, 1801, 1034, 0.4)


def draw_background_texture(img):
    d = ImageDraw.Draw(img, "RGBA")
    for _ in range(1100):
        x = rng.randrange(0, W * S)
        y = rng.randrange(0, H * S)
        col = rng.choice(["#fff9df22", "#cf823014", "#f0b84c18", "#ffffff22"])
        d.ellipse((x, y, x + rng.randrange(1, 4), y + rng.randrange(1, 4)), fill=col)


def main():
    img = Image.new("RGBA", (W * S, H * S), "#fff1c8")
    draw_background_texture(img)
    d = ImageDraw.Draw(img, "RGBA")

    # Full board surface.
    rounded(d, box(5, 5, W - 5, H - 5), 0, "#fff6da", width=7, shadow=False)
    draw_left_panel(d)
    draw_right_panel(d)
    draw_progress(d)

    draw_card(d, 295, 245, 410, 598, "reward")
    draw_card(d, 760, 245, 410, 598, "meal")
    draw_card(d, 1225, 245, 410, 598, "event")

    # Bottom recipe drawer.
    rounded(d, box(752, 1004, 1145, 1070), 6, "#fff9e5", width=6, shadow=True)
    poly(d, [(948, 980), (925, 1020), (971, 1020)], "#d0d0d0", "#111111", 3)
    draw_bowl(d, 875, 1049, 0.34)
    draw_star(d, 1018, 1038, 20, 9, "#ffd24f", width=4)

    # A subtle cel-shaded warm vignette.
    shade = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shade, "RGBA")
    sd.rectangle((0, 0, W * S, H * S), outline="#cf7d2f33", width=sc(20))
    shade = shade.filter(ImageFilter.GaussianBlur(sc(10)))
    img = Image.alpha_composite(img, shade)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    img = img.resize((W, H), Image.Resampling.LANCZOS).convert("RGBA")
    img.save(OUT)
    print(OUT)


if __name__ == "__main__":
    main()
