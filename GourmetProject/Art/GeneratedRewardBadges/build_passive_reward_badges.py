from collections import deque
from pathlib import Path

from PIL import Image


ROOT = Path("/Users/hcm-b0451/gourmetproject/GourmetProject")
ART_DIR = ROOT / "Art/GeneratedRewardBadges"
UI_DIR = ROOT / "Assets/GameMain/Content/Resources/Sprites/UI"
CUTOUT = ART_DIR / "passive_item_reward_cutout.png"


def extract_progress_bar(source: Image.Image) -> Image.Image:
    alpha = source.getchannel("A")
    width, height = source.size
    pixels = alpha.load()
    visited = bytearray(width * height)
    components = []

    for y in range(height):
        for x in range(width):
            index = y * width + x
            if visited[index] or pixels[x, y] == 0:
                continue
            queue = deque([(x, y)])
            visited[index] = 1
            points = []
            min_x = max_x = x
            min_y = max_y = y
            while queue:
                px, py = queue.popleft()
                points.append((px, py))
                min_x = min(min_x, px)
                max_x = max(max_x, px)
                min_y = min(min_y, py)
                max_y = max(max_y, py)
                for nx, ny in ((px - 1, py), (px + 1, py), (px, py - 1), (px, py + 1)):
                    if nx < 0 or ny < 0 or nx >= width or ny >= height:
                        continue
                    neighbor = ny * width + nx
                    if visited[neighbor] or pixels[nx, ny] == 0:
                        continue
                    visited[neighbor] = 1
                    queue.append((nx, ny))
            components.append((len(points), (min_x, min_y, max_x + 1, max_y + 1), points))

    candidates = [
        component
        for component in components
        if component[0] > 1000
        and component[1][0] >= 180
        and component[1][1] <= 40
        and component[1][3] <= 190
    ]
    if not candidates:
        raise RuntimeError("Could not isolate reward progress bar")

    _, _, points = max(candidates, key=lambda component: component[0])
    mask = Image.new("L", source.size, 0)
    mask_pixels = mask.load()
    for x, y in points:
        mask_pixels[x, y] = alpha.getpixel((x, y))

    overlay = Image.new("RGBA", source.size, (0, 0, 0, 0))
    overlay.paste(source, (0, 0), mask)
    return overlay


def build_variant(source_name: str, output_name: str) -> None:
    source = Image.open(ART_DIR / source_name).convert("RGBA")
    if source.size != (512, 512):
        raise RuntimeError(f"Unexpected source size: {source.size}")

    subject = Image.open(CUTOUT).convert("RGBA")
    bbox = subject.getchannel("A").getbbox()
    if bbox is None:
        raise RuntimeError("Generated decoration cutout is empty")
    subject = subject.crop(bbox)
    subject.thumbnail((430, 430), Image.Resampling.LANCZOS)

    canvas = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    subject_x = 4
    subject_y = 512 - subject.height - 4
    canvas.alpha_composite(subject, (subject_x, subject_y))
    canvas.alpha_composite(extract_progress_bar(source))

    art_output = ART_DIR / output_name
    formal_output = UI_DIR / output_name
    canvas.save(art_output, optimize=True)
    canvas.save(formal_output, optimize=True)


build_variant("progress_reference_passive_item.png", "reward_badge_passive_item.png")
build_variant("progress_reference_passive_item_4.png", "reward_badge_passive_item_4.png")
