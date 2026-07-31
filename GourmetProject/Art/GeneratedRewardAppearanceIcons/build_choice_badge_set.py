from pathlib import Path
from shutil import copyfile

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).parent
PROJECT = ROOT.parents[1]
UI_SPRITES = (
    PROJECT
    / "Assets/GameMain/Content/Resources/Sprites/UI"
)
SOURCE_FINAL = ROOT / "final"
OUTPUT = ROOT / "final_v2"

ITEMS = [
    ("reward_appearance_gold_small", "一些金币"),
    ("reward_appearance_table_cell_small", "餐桌格"),
    ("reward_appearance_passive_choice_2", "道具2选1"),
    ("reward_appearance_strengthen_choice_2", "强化箱2选1"),
    ("reward_appearance_adjust_choice_2", "调整单2选1"),
    ("reward_appearance_gold_large", "大量金币"),
    ("reward_appearance_table_cell_large", "大餐桌格"),
    ("reward_appearance_passive_choice_4", "道具4选1"),
    ("reward_appearance_strengthen_choice_4", "强化箱4选1"),
    ("reward_appearance_adjust_choice_4", "调整单4选1"),
]

BASE_ICONS = {
    "passive": UI_SPRITES / "reward_badge_passive_item.png",
    "strengthen": UI_SPRITES / "reward_badge_active_strengthen.png",
    "adjust": UI_SPRITES / "reward_badge_active_adjust.png",
}

FONT_PATH = "/System/Library/Fonts/STHeiti Medium.ttc"


def load_subject(path: Path) -> Image.Image:
    image = Image.open(path).convert("RGBA")
    bbox = image.getchannel("A").getbbox()
    if bbox is None:
        raise RuntimeError(f"{path} has no visible pixels")
    return image.crop(bbox)


def draw_check(draw: ImageDraw.ImageDraw, cx: int, cy: int, scale: float) -> None:
    width = max(7, round(15 * scale))
    draw.line(
        (
            cx - round(21 * scale),
            cy - round(1 * scale),
            cx - round(7 * scale),
            cy + round(17 * scale),
        ),
        fill="#ffffff",
        width=width,
    )
    draw.line(
        (
            cx - round(7 * scale),
            cy + round(17 * scale),
            cx + round(24 * scale),
            cy - round(22 * scale),
        ),
        fill="#ffffff",
        width=width,
    )


def draw_choice_badge(canvas: Image.Image, count: int) -> None:
    draw = ImageDraw.Draw(canvas)
    if count == 2:
        box = (270, 16, 502, 144)
        centers = [340, 443]
        selected_radius = 42
        empty_radius = 29
    else:
        box = (190, 16, 502, 144)
        centers = [239, 310, 381, 452]
        selected_radius = 36
        empty_radius = 24

    draw.rounded_rectangle(
        (box[0] + 8, box[1] + 10, box[2] + 8, box[3] + 10),
        radius=46,
        fill=(57, 31, 20, 135),
    )
    draw.rounded_rectangle(
        box,
        radius=46,
        fill="#fff0c8",
        outline="#3b2116",
        width=13,
    )
    draw.rounded_rectangle(
        (box[0] + 12, box[1] + 12, box[2] - 12, box[3] - 12),
        radius=34,
        outline="#e2a63f",
        width=7,
    )

    cy = 80
    selected_x = centers[0]
    draw.ellipse(
        (
            selected_x - selected_radius,
            cy - selected_radius,
            selected_x + selected_radius,
            cy + selected_radius,
        ),
        fill="#0a9daf",
        outline="#3b2116",
        width=9,
    )
    inner = selected_radius - 9
    draw.ellipse(
        (
            selected_x - inner,
            cy - inner,
            selected_x + inner,
            cy + inner,
        ),
        fill="#16d7df",
    )
    draw_check(draw, selected_x, cy, selected_radius / 42)

    for cx in centers[1:]:
        draw.ellipse(
            (
                cx - empty_radius,
                cy - empty_radius,
                cx + empty_radius,
                cy + empty_radius,
            ),
            fill="#f4dfad",
            outline="#80583a",
            width=7,
        )
        inner = empty_radius - 12
        draw.ellipse(
            (cx - inner, cy - inner, cx + inner, cy + inner),
            fill="#fff8e6",
        )


def make_choice_icon(base: Path, count: int) -> Image.Image:
    canvas = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    subject = load_subject(base)
    subject.thumbnail((436, 436), Image.Resampling.LANCZOS)
    x = 18 + (436 - subject.width) // 2
    y = 58 + (436 - subject.height) // 2
    canvas.alpha_composite(subject, (x, y))
    draw_choice_badge(canvas, count)
    return canvas


def create_icons() -> dict[str, Image.Image]:
    OUTPUT.mkdir(parents=True, exist_ok=True)

    for name in (
        "reward_appearance_gold_small",
        "reward_appearance_table_cell_small",
        "reward_appearance_gold_large",
        "reward_appearance_table_cell_large",
    ):
        copyfile(SOURCE_FINAL / f"{name}.png", OUTPUT / f"{name}.png")

    choices = [
        ("reward_appearance_passive_choice_2", "passive", 2),
        ("reward_appearance_strengthen_choice_2", "strengthen", 2),
        ("reward_appearance_adjust_choice_2", "adjust", 2),
        ("reward_appearance_passive_choice_4", "passive", 4),
        ("reward_appearance_strengthen_choice_4", "strengthen", 4),
        ("reward_appearance_adjust_choice_4", "adjust", 4),
    ]
    for name, kind, count in choices:
        make_choice_icon(BASE_ICONS[kind], count).save(OUTPUT / f"{name}.png")

    return {
        name: Image.open(OUTPUT / f"{name}.png").convert("RGBA")
        for name, _ in ITEMS
    }


def create_preview(icons: dict[str, Image.Image]) -> None:
    cell_w, cell_h = 252, 246
    preview = Image.new("RGB", (cell_w * 5, cell_h * 2), "#241c18")
    draw = ImageDraw.Draw(preview)
    font = ImageFont.truetype(FONT_PATH, 25)

    for index, (name, label) in enumerate(ITEMS):
        column = index % 5
        row = index // 5
        x0 = column * cell_w
        y0 = row * cell_h
        icon = icons[name].copy()
        icon.thumbnail((176, 176), Image.Resampling.LANCZOS)
        x = x0 + (cell_w - icon.width) // 2
        y = y0 + 8 + (178 - icon.height) // 2
        preview.paste(icon, (x, y), icon)
        bbox = draw.textbbox((0, 0), label, font=font)
        tx = x0 + (cell_w - (bbox[2] - bbox[0])) // 2
        draw.text((tx, y0 + 198), label, fill="#f7ead1", font=font)

    preview.save(ROOT / "reward-appearances-v2-preview.png")


def create_80px_check(icons: dict[str, Image.Image]) -> None:
    cell_w, cell_h = 140, 132
    preview = Image.new("RGB", (cell_w * 5, cell_h * 2), "#f3e2bd")
    draw = ImageDraw.Draw(preview)
    font = ImageFont.truetype(FONT_PATH, 17)

    for index, (name, label) in enumerate(ITEMS):
        column = index % 5
        row = index // 5
        x0 = column * cell_w
        y0 = row * cell_h
        icon = icons[name].resize((80, 80), Image.Resampling.LANCZOS)
        preview.paste(icon, (x0 + 30, y0 + 6), icon)
        bbox = draw.textbbox((0, 0), label, font=font)
        tx = x0 + (cell_w - (bbox[2] - bbox[0])) // 2
        draw.text((tx, y0 + 98), label, fill="#382317", font=font)

    preview.save(ROOT / "reward-appearances-v2-80px-check.png")


def main() -> None:
    icons = create_icons()
    create_preview(icons)
    create_80px_check(icons)


if __name__ == "__main__":
    main()
