#!/usr/bin/env python3

import argparse
from pathlib import Path

from PIL import Image


def save(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path)
    print(f"output={path} size={image.size} alpha_bbox={image.getchannel('A').getbbox()}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--right", required=True)
    parser.add_argument("--output-dir", required=True)
    args = parser.parse_args()

    right = Image.open(args.right).convert("RGBA")
    output_dir = Path(args.output_dir)
    save(right, output_dir / "arrow_cookie_1_sample.png")
    save(right.transpose(Image.Transpose.ROTATE_180), output_dir / "arrow_cookie_2_sample.png")
    save(right.transpose(Image.Transpose.ROTATE_270), output_dir / "arrow_cookie_3_sample.png")
    save(right.transpose(Image.Transpose.ROTATE_90), output_dir / "arrow_cookie_4_sample.png")


if __name__ == "__main__":
    main()
