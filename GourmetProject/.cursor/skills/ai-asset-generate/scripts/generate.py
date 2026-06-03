#!/usr/bin/env python3
"""Generate 2D game assets via OpenAI gpt-image-1 API, saved into Unity Assets/ folder."""

import argparse
import base64
import json
import os
import sys
import time
from pathlib import Path
from urllib.request import Request, urlopen, urlretrieve

try:
    from openai import OpenAI
except ImportError:
    print("ERROR: 'openai' package not found. Install with: pip install openai")
    sys.exit(1)

# project root
PROJECT_ROOT = Path(__file__).resolve().parents[4]

TYPE_DIRS = {
    "item": "Assets/GameMain/Resources/Sprites/Items",
    "background": "Assets/GameMain/Resources/Sprites/Backgrounds",
    "character": "Assets/GameMain/Resources/Sprites/Characters",
    "ui": "Assets/GameMain/Resources/Sprites/UI",
    "animation": "Assets/GameMain/Resources/Sprites/Animations",
}

TYPE_SIZES = {
    "item": "1024x1024",
    "character": "1024x1024",
    "background": "1536x1024",
    "ui": "1024x1024",
    "animation": "1024x1024",
}


def build_prompt(raw_prompt: str, asset_type: str, style_prefix: str | None, transparent: bool) -> str:
    """Assemble the full prompt with optional style prefix and transparency hints."""
    parts = []
    if style_prefix:
        parts.append(style_prefix)
    parts.append(raw_prompt)
    if transparent:
        parts.append("transparent background, isolated on alpha channel")
    full = ", ".join(parts)
    return full


def generate(client: OpenAI, prompt: str, size: str, quality: str, n: int) -> list[bytes]:
    """Call gpt-image-1 and return list of image bytes."""
    print(f"  Generating {n} image(s) at {size} (quality={quality})...")
    print(f"  Prompt: {prompt[:120]}{'...' if len(prompt) > 120 else ''}")

    t0 = time.time()
    resp = client.images.generate(
        model="gpt-image-1",
        prompt=prompt,
        n=n,
        size=size,
        quality=quality,
    )
    elapsed = time.time() - t0

    images: list[bytes] = []
    for i, img in enumerate(resp.data):
        if img.b64_json:
            raw = base64.b64decode(img.b64_json)
        elif img.url:
            req = Request(img.url, headers={"User-Agent": "ai-asset-generate/1.0"})
            raw = urlopen(req).read()
        else:
            print(f"  WARNING: no image data in response for image {i+1}")
            continue
        images.append(raw)
        print(f"  [{i+1}/{n}] {len(raw)} bytes received ({elapsed:.1f}s)")

    return images


def save_images(images: list[bytes], output_dir: Path, base_name: str) -> list[str]:
    """Write images to disk, auto-numbering when n > 1. Returns list of saved paths."""
    output_dir.mkdir(parents=True, exist_ok=True)
    saved = []

    for i, data in enumerate(images):
        if len(images) == 1:
            filename = f"{base_name}.png"
        else:
            filename = f"{base_name}_{i+1:02d}.png"
        filepath = output_dir / filename
        filepath.write_bytes(data)
        saved.append(str(filepath))
        print(f"  Saved: {filepath}")

    return saved


def main():
    parser = argparse.ArgumentParser(description="Generate 2D game assets via gpt-image-1")
    parser.add_argument("--prompt", help="Image generation prompt")
    parser.add_argument("--type", dest="asset_type", default="item",
                        choices=["item", "background", "character", "ui", "animation"])
    parser.add_argument("--name", default="asset", help="Output filename (without extension)")
    parser.add_argument("--size", help="Image size (e.g. 1024x1024, 1792x1024)")
    parser.add_argument("--quality", default="high", choices=["low", "medium", "high", "auto"])
    parser.add_argument("--n", type=int, default=1, help="Number of variants (1-10)")
    parser.add_argument("--manifest", help="Path to JSON manifest for batch generation")
    parser.add_argument("--style-prefix", help="Global style prefix prepended to every prompt")
    parser.add_argument("--transparent", action="store_true",
                        help="Add transparent background hint to prompt")
    parser.add_argument("--dry-run", action="store_true",
                        help="Print what would be generated without calling the API")

    args = parser.parse_args()

    client = None
    if not args.dry_run:
        api_key = os.environ.get("OPENAI_API_KEY")
        if not api_key:
            print("ERROR: OPENAI_API_KEY environment variable is not set.")
            print("  export OPENAI_API_KEY='your-key-here'")
            sys.exit(1)
        base_url = os.environ.get("OPENAI_BASE_URL", "https://sapi-ai.hortorgames.com/v1")
        client = OpenAI(api_key=api_key, base_url=base_url)

    # --- Batch mode ---
    if args.manifest:
        manifest_path = Path(args.manifest)
        if not manifest_path.exists():
            print(f"ERROR: manifest file not found: {args.manifest}")
            sys.exit(1)

        manifest = json.loads(manifest_path.read_text())
        style_prefix = manifest.get("stylePrefix", args.style_prefix)
        entries = manifest.get("assets", [])

        if not entries:
            print("Manifest has no assets to generate.")
            return

        print(f"Batch generating {len(entries)} asset(s)...")
        total = 0
        for entry in entries:
            asset_type = entry.get("type", "item")
            name = entry.get("name", "asset")
            prompt = entry.get("prompt", "")
            size = entry.get("size", TYPE_SIZES.get(asset_type, "512x512"))
            quality = entry.get("quality", args.quality)
            n = entry.get("n", 1)
            transparent = entry.get("transparent", False)

            full_prompt = build_prompt(prompt, asset_type, style_prefix, transparent)
            output_dir = PROJECT_ROOT / TYPE_DIRS.get(asset_type, TYPE_DIRS["item"])

            if asset_type == "animation":
                output_dir = output_dir / name

            print(f"\n[{name}] ({asset_type})")
            if args.dry_run:
                print(f"  WOULD generate {n} image(s) at {size}")
                print(f"  Output: {output_dir}")
            else:
                images = generate(client, full_prompt, size, quality, n)
                saved = save_images(images, output_dir, name)
                total += len(saved)

        if not args.dry_run:
            print(f"\nDone. {total} asset(s) generated.")
        return

    # --- Single asset mode ---
    if not args.prompt:
        print("ERROR: --prompt is required (or use --manifest for batch mode).")
        sys.exit(1)

    size = args.size or TYPE_SIZES.get(args.asset_type, "512x512")
    full_prompt = build_prompt(args.prompt, args.asset_type, args.style_prefix, args.transparent)
    output_dir = PROJECT_ROOT / TYPE_DIRS.get(args.asset_type, TYPE_DIRS["item"])

    if args.asset_type == "animation":
        output_dir = output_dir / args.name

    if args.dry_run:
        print(f"[{args.name}] ({args.asset_type})")
        print(f"  WOULD generate {args.n} image(s) at {size}")
        print(f"  Output: {output_dir}")
        return

    images = generate(client, full_prompt, size, args.quality, args.n)
    save_images(images, output_dir, args.name)


if __name__ == "__main__":
    main()
