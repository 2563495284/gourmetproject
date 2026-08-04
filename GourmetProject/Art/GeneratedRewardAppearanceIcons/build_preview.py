from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).parent
FINAL_RAW = ROOT / "final_raw"
FINAL = ROOT / "final"

ITEMS = [
    ("reward_appearance_gold_small", "一些金币"),
    ("reward_appearance_table_cell_small", "餐桌格"),
    ("reward_appearance_passive_choice_2", "装饰品和消耗品2选1"),
    ("reward_appearance_strengthen_choice_2", "强化箱2选1"),
    ("reward_appearance_adjust_choice_2", "调整单2选1"),
    ("reward_appearance_gold_large", "大量金币"),
    ("reward_appearance_table_cell_large", "大餐桌格"),
    ("reward_appearance_passive_choice_4", "装饰品和消耗品4选1"),
    ("reward_appearance_strengthen_choice_4", "强化箱4选1"),
    ("reward_appearance_adjust_choice_4", "调整单4选1"),
]

FONT_PATH = "/System/Library/Fonts/STHeiti Medium.ttc"


def normalized_icon(name: str) -> Image.Image:
    image = Image.open(FINAL_RAW / f"{name}.png").convert("RGBA")
    bbox = image.getchannel("A").getbbox()
    if bbox is None:
        raise RuntimeError(f"{name} has no visible pixels")

    subject = image.crop(bbox)
    subject.thumbnail((456, 456), Image.Resampling.LANCZOS)

    canvas = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    x = (canvas.width - subject.width) // 2
    y = (canvas.height - subject.height) // 2
    canvas.alpha_composite(subject, (x, y))
    canvas.save(FINAL / f"{name}.png")
    return canvas


def make_preview(icons: dict[str, Image.Image]) -> None:
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

        text_bbox = draw.textbbox((0, 0), label, font=font)
        text_x = x0 + (cell_w - (text_bbox[2] - text_bbox[0])) // 2
        draw.text((text_x, y0 + 198), label, fill="#f7ead1", font=font)

    preview.save(ROOT / "reward-appearances-preview.png")


def make_80px_check(icons: dict[str, Image.Image]) -> None:
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

        text_bbox = draw.textbbox((0, 0), label, font=font)
        text_x = x0 + (cell_w - (text_bbox[2] - text_bbox[0])) // 2
        draw.text((text_x, y0 + 98), label, fill="#382317", font=font)

    preview.save(ROOT / "reward-appearances-80px-check.png")


def main() -> None:
    FINAL.mkdir(parents=True, exist_ok=True)
    icons = {name: normalized_icon(name) for name, _ in ITEMS}
    make_preview(icons)
    make_80px_check(icons)


if __name__ == "__main__":
    main()
