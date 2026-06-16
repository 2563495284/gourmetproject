#!/usr/bin/env python3
"""Generate 2D game assets via Gemini image generation, saved into Unity Assets/ folder."""

import argparse
import base64
import json
import os
import socket
import sys
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

PROJECT_ROOT = Path(__file__).resolve().parents[4]
GEMINI_MODEL = "gemini-3.1-flash-image-preview"
DEFAULT_GEMINI_BASE_URL = "https://sapi-ai.hortorgames.com/gemini"
# 注意：明文密钥兜底，仅供本地生成使用；切勿将本文件提交到公开仓库。
# 优先使用环境变量 GEMINI_API_KEY，留空时回退到此默认值。
DEFAULT_API_KEY = "sk-hortor-09f38819c14fa3e2a3551b6de96c3168952239efbc0b04fd"

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


def build_prompt(raw_prompt: str, asset_type: str, style_prefix: str | None, transparent: bool, size: str) -> str:
    parts = []
    if style_prefix:
        parts.append(style_prefix)
    parts.append(raw_prompt)
    if transparent:
        parts.append("transparent background, isolated on alpha channel")
    if size:
        parts.append(f"output image size {size}")
    return ", ".join(parts)


def endpoint_for(base_url: str, model: str) -> str:
    return f"{base_url.rstrip('/')}/v1beta/models/{model}:generateContent"


def read_inline_image(part: dict) -> tuple[bytes, str] | None:
    inline_data = part.get("inlineData") or part.get("inline_data")
    if not inline_data:
        return None

    image_data = inline_data.get("data")
    if not image_data:
        return None

    mime_type = inline_data.get("mimeType") or inline_data.get("mime_type") or "image/png"
    return base64.b64decode(image_data), mime_type


def request_image(api_key: str, base_url: str, model: str, prompt: str, timeout: int) -> list[tuple[bytes, str]]:
    payload = {
        "contents": [
            {
                "role": "user",
                "parts": [{"text": prompt}],
            }
        ],
        "generationConfig": {
            "responseModalities": ["TEXT", "IMAGE"],
        },
    }
    data = json.dumps(payload).encode("utf-8")
    request = Request(
        endpoint_for(base_url, model),
        data=data,
        headers={
            "Content-Type": "application/json",
            "x-goog-api-key": api_key,
            "User-Agent": "ai-asset-generate/1.0",
        },
        method="POST",
    )

    try:
        with urlopen(request, timeout=timeout) as response:
            body = response.read().decode("utf-8")
    except HTTPError as error:
        body = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"Gemini API error {error.code}: {body}") from error
    except (TimeoutError, socket.timeout) as error:
        raise RuntimeError(
            f"Gemini API request timed out after {timeout}s. Try a smaller size, fewer assets, or a larger --timeout."
        ) from error
    except URLError as error:
        raise RuntimeError(f"Gemini API request failed: {error.reason}") from error

    response_json = json.loads(body)
    images: list[tuple[bytes, str]] = []
    texts: list[str] = []

    for candidate in response_json.get("candidates", []):
        content = candidate.get("content", {})
        for part in content.get("parts", []):
            if "text" in part:
                texts.append(part["text"])
            image = read_inline_image(part)
            if image:
                images.append(image)

    for text in texts:
        print(f"  Gemini: {text[:160]}{'...' if len(text) > 160 else ''}")

    if not images:
        preview = json.dumps(response_json, ensure_ascii=False)[:800]
        raise RuntimeError(f"Gemini response did not include image data: {preview}")

    return images


def generate(api_key: str, base_url: str, model: str, prompt: str, size: str, quality: str, n: int, timeout: int, retries: int) -> list[tuple[bytes, str]]:
    print(f"  Generating {n} image(s) with {model} at {size} (quality hint={quality}, timeout={timeout}s)...")
    print(f"  Prompt: {prompt[:120]}{'...' if len(prompt) > 120 else ''}")

    images: list[tuple[bytes, str]] = []
    attempts = max(1, retries + 1)
    for variant in range(n):
        last_error: RuntimeError | None = None
        for attempt in range(attempts):
            try:
                t0 = time.time()
                generated = request_image(api_key, base_url, model, prompt, timeout)
                elapsed = time.time() - t0
                for raw, mime_type in generated:
                    images.append((raw, mime_type))
                    print(f"  [{len(images)}/{n}] {len(raw)} bytes received as {mime_type} ({elapsed:.1f}s)")
                    if len(images) >= n:
                        return images
                if len(generated) == 0:
                    print(f"  WARNING: no image data in response for variant {variant + 1}")
                break
            except RuntimeError as error:
                last_error = error
                if attempt >= attempts - 1:
                    break
                print(f"  Attempt {attempt + 1}/{attempts} failed: {error}")
                time.sleep(2 + attempt * 2)

        if last_error is not None and len(images) <= variant:
            raise last_error

    return images


def extension_for(mime_type: str) -> str:
    if mime_type == "image/jpeg":
        return ".jpg"
    if mime_type == "image/webp":
        return ".webp"
    return ".png"


def save_images(images: list[tuple[bytes, str]], output_dir: Path, base_name: str) -> list[str]:
    output_dir.mkdir(parents=True, exist_ok=True)
    saved = []

    for i, (data, mime_type) in enumerate(images):
        extension = extension_for(mime_type)
        if len(images) == 1:
            filename = f"{base_name}{extension}"
        else:
            filename = f"{base_name}_{i+1:02d}{extension}"
        filepath = output_dir / filename
        filepath.write_bytes(data)
        saved.append(str(filepath))
        print(f"  Saved: {filepath}")

    return saved


def selected_entries(entries: list[dict], only: set[str]) -> list[dict]:
    if not only:
        return entries

    selected = [entry for entry in entries if entry.get("name") in only]
    missing = sorted(only - {entry.get("name") for entry in selected})
    if missing:
        raise RuntimeError(f"Manifest does not contain requested asset(s): {', '.join(missing)}")
    return selected


def main():
    parser = argparse.ArgumentParser(description=f"Generate 2D game assets via {GEMINI_MODEL}")
    parser.add_argument("--prompt", help="Image generation prompt")
    parser.add_argument("--type", dest="asset_type", default="item",
                        choices=["item", "background", "character", "ui", "animation"])
    parser.add_argument("--name", default="asset", help="Output filename (without extension)")
    parser.add_argument("--size", help="Image size (e.g. 1024x1024, 1536x1024)")
    parser.add_argument("--quality", default="high", choices=["low", "medium", "high", "auto"])
    parser.add_argument("--n", type=int, default=1, help="Number of variants")
    parser.add_argument("--manifest", help="Path to JSON manifest for batch generation")
    parser.add_argument("--only", nargs="*", default=[], help="Generate only these manifest asset names")
    parser.add_argument("--style-prefix", help="Global style prefix prepended to every prompt")
    parser.add_argument("--transparent", action="store_true",
                        help="Add transparent background hint to prompt")
    parser.add_argument("--timeout", type=int, default=600, help="Per-request timeout in seconds")
    parser.add_argument("--retries", type=int, default=1, help="Retry count per requested image")
    parser.add_argument("--dry-run", action="store_true",
                        help="Print what would be generated without calling the API")

    args = parser.parse_args()

    api_key = None
    base_url = DEFAULT_GEMINI_BASE_URL
    model = GEMINI_MODEL
    if not args.dry_run:
        api_key = os.environ.get("GEMINI_API_KEY") or os.environ.get("OPENAI_API_KEY") or DEFAULT_API_KEY
        if not api_key:
            print("ERROR: GEMINI_API_KEY environment variable is not set.")
            print("  export GEMINI_API_KEY='your-key-here'")
            sys.exit(1)
        base_url = os.environ.get("GEMINI_BASE_URL", DEFAULT_GEMINI_BASE_URL)
        model = os.environ.get("GEMINI_IMAGE_MODEL", GEMINI_MODEL)

    try:
        if args.manifest:
            manifest_path = Path(args.manifest)
            if not manifest_path.exists():
                print(f"ERROR: manifest file not found: {args.manifest}")
                sys.exit(1)

            manifest = json.loads(manifest_path.read_text())
            style_prefix = manifest.get("stylePrefix", args.style_prefix)
            entries = selected_entries(manifest.get("assets", []), set(args.only))

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

                full_prompt = build_prompt(prompt, asset_type, style_prefix, transparent, size)
                output_dir = PROJECT_ROOT / TYPE_DIRS.get(asset_type, TYPE_DIRS["item"])

                if asset_type == "animation":
                    output_dir = output_dir / name

                print(f"\n[{name}] ({asset_type})")
                if args.dry_run:
                    print(f"  WOULD generate {n} image(s) at {size}")
                    print(f"  Output: {output_dir}")
                else:
                    images = generate(api_key, base_url, model, full_prompt, size, quality, n, args.timeout, args.retries)
                    saved = save_images(images, output_dir, name)
                    total += len(saved)

            if not args.dry_run:
                print(f"\nDone. {total} asset(s) generated.")
            return

        if not args.prompt:
            print("ERROR: --prompt is required (or use --manifest for batch mode).")
            sys.exit(1)

        size = args.size or TYPE_SIZES.get(args.asset_type, "512x512")
        full_prompt = build_prompt(args.prompt, args.asset_type, args.style_prefix, args.transparent, size)
        output_dir = PROJECT_ROOT / TYPE_DIRS.get(args.asset_type, TYPE_DIRS["item"])

        if args.asset_type == "animation":
            output_dir = output_dir / args.name

        if args.dry_run:
            print(f"[{args.name}] ({args.asset_type})")
            print(f"  WOULD generate {args.n} image(s) at {size}")
            print(f"  Output: {output_dir}")
            return

        images = generate(api_key, base_url, model, full_prompt, size, args.quality, args.n, args.timeout, args.retries)
        save_images(images, output_dir, args.name)
    except RuntimeError as error:
        print(f"ERROR: {error}")
        sys.exit(1)


if __name__ == "__main__":
    main()
