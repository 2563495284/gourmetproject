#!/usr/bin/env python3
"""Generate 2D game assets, saved into Unity Assets/ folder."""

from __future__ import annotations

import argparse
import base64
import json
import os
import socket
import ssl
import sys
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

PROJECT_ROOT = Path(__file__).resolve().parents[4]
GEMINI_MODEL = "gemini-3.1-flash-image-preview"
OPENAI_IMAGE_MODEL = "gpt-image-2"
DEFAULT_GEMINI_BASE_URL = "https://sapi-ai.hortorgames.com/gemini"
DEFAULT_OPENAI_BASE_URL = "https://sapi-ai.hortorgames.com/v1"
# 注意：明文密钥兜底，仅供本地生成使用；切勿将本文件提交到公开仓库。
# 优先使用环境变量 GEMINI_API_KEY，留空时回退到此默认值。
DEFAULT_API_KEY = "sk-hortor-09f38819c14fa3e2a3551b6de96c3168952239efbc0b04fd"

TYPE_DIRS = {
    "item": "Assets/GameMain/Content/Resources/Sprites/Items",
    "dish": "Assets/GameMain/Content/Resources/Sprites/Dishes",
    "background": "Assets/GameMain/Content/Resources/Sprites/Backgrounds",
    "character": "Assets/GameMain/Content/Resources/Sprites/Characters",
    "ui": "Assets/GameMain/Content/Resources/Sprites/UI",
    "animation": "Assets/GameMain/Content/Resources/Sprites/Animations",
}

TYPE_SIZES = {
    "item": "512x512",
    "dish": "512x512",
    "character": "1024x1024",
    "background": "1536x1024",
    "ui": "1024x1024",
    "animation": "1024x1024",
}

OPENAI_FIXED_SIZES = {"1024x1024", "1024x1536", "1536x1024", "auto"}
GPT_IMAGE_2_MIN_PIXELS = 655_360
GPT_IMAGE_2_MAX_PIXELS = 8_294_400
GPT_IMAGE_2_MAX_EDGE = 3840
ALLOW_INSECURE_SSL = False


def parse_size(size: str) -> tuple[int, int] | None:
    if not size or size == "auto":
        return None
    try:
        width, height = size.lower().split("x", 1)
        return int(width), int(height)
    except ValueError as error:
        raise RuntimeError(f"Invalid size '{size}'. Expected WIDTHxHEIGHT or auto.") from error


def validate_openai_size(model: str, size: str) -> None:
    if size == "auto":
        return

    parsed = parse_size(size)
    if parsed is None:
        return

    width, height = parsed
    if model == "gpt-image-2":
        total = width * height
        long_edge = max(width, height)
        short_edge = min(width, height)
        if long_edge > GPT_IMAGE_2_MAX_EDGE:
            raise RuntimeError("gpt-image-2 request size maximum edge must be <= 3840px.")
        if width % 16 != 0 or height % 16 != 0:
            raise RuntimeError("gpt-image-2 request size width and height must be multiples of 16px.")
        if long_edge / short_edge > 3:
            raise RuntimeError("gpt-image-2 request size long edge to short edge ratio must be <= 3:1.")
        if total < GPT_IMAGE_2_MIN_PIXELS or total > GPT_IMAGE_2_MAX_PIXELS:
            raise RuntimeError("gpt-image-2 request size total pixels must be between 655,360 and 8,294,400.")
        return

    if size not in OPENAI_FIXED_SIZES:
        allowed = ", ".join(sorted(OPENAI_FIXED_SIZES))
        raise RuntimeError(f"{model} request size must be one of: {allowed}.")


def build_prompt(raw_prompt: str, asset_type: str, style_prefix: str | None, transparent: bool, target_size: str) -> str:
    parts = []
    if style_prefix:
        parts.append(style_prefix)
    parts.append(raw_prompt)
    if transparent:
        parts.append("transparent background, isolated on alpha channel")
    if target_size:
        parts.append(f"final intended asset size {target_size}")
    return ", ".join(parts)


def shape_prompt(shape_rows: list[str], target_size: str | None) -> str:
    if not shape_rows:
        return ""

    width = max(len(row) for row in shape_rows)
    height = len(shape_rows)
    rows = " / ".join(shape_rows)
    size_hint = f" Final canvas: {target_size}." if target_size else ""
    return (
        f"Grid footprint constraint: one grid cell is 512 by 512 pixels; this sprite occupies a {width}x{height} cell bounding box. "
        f"shapeRows top to bottom are {rows}, where X cells contain food and . cells must remain empty. "
        f"Keep the full rectangular canvas for the whole bounding box; do not crop to the food silhouette. "
        f"Every . cell must be only the removable background color with no food, shadow, garnish, line, texture, or antialias spill. "
        f"Do not draw grid lines, cell borders, rectangles, guides, or checkerboards.{size_hint}"
    )


def gemini_endpoint_for(base_url: str, model: str) -> str:
    return f"{base_url.rstrip('/')}/v1beta/models/{model}:generateContent"


def openai_endpoint_for(base_url: str) -> str:
    base = base_url.rstrip("/")
    if base.endswith("/v1"):
        return f"{base}/images/generations"
    return f"{base}/v1/images/generations"


def open_url(request_or_url, timeout: int):
    context = ssl._create_unverified_context() if ALLOW_INSECURE_SSL else None
    return urlopen(request_or_url, timeout=timeout, context=context)


def read_inline_image(part: dict) -> tuple[bytes, str] | None:
    inline_data = part.get("inlineData") or part.get("inline_data")
    if not inline_data:
        return None

    image_data = inline_data.get("data")
    if not image_data:
        return None

    mime_type = inline_data.get("mimeType") or inline_data.get("mime_type") or "image/png"
    return base64.b64decode(image_data), mime_type


def request_gemini_image(api_key: str, base_url: str, model: str, prompt: str, timeout: int) -> list[tuple[bytes, str]]:
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
        gemini_endpoint_for(base_url, model),
        data=data,
        headers={
            "Content-Type": "application/json",
            "x-goog-api-key": api_key,
            "User-Agent": "ai-asset-generate/1.0",
        },
        method="POST",
    )

    try:
        with open_url(request, timeout) as response:
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


def request_openai_image(
    api_key: str,
    base_url: str,
    model: str,
    prompt: str,
    request_size: str,
    quality: str,
    background: str,
    output_format: str,
    timeout: int,
) -> list[tuple[bytes, str]]:
    validate_openai_size(model, request_size)
    if model == "gpt-image-2" and background == "transparent":
        raise RuntimeError("gpt-image-2 does not support background=transparent. Use chroma-key prompting or gpt-image-1.5.")

    payload = {
        "model": model,
        "prompt": prompt,
        "n": 1,
        "size": request_size,
        "quality": quality,
        "output_format": output_format,
    }
    if background != "auto":
        payload["background"] = background

    data = json.dumps(payload).encode("utf-8")
    request = Request(
        openai_endpoint_for(base_url),
        data=data,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {api_key}",
            "User-Agent": "ai-asset-generate/2.0",
        },
        method="POST",
    )

    try:
        with open_url(request, timeout) as response:
            body = response.read().decode("utf-8")
    except HTTPError as error:
        body = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"OpenAI image API error {error.code}: {body}") from error
    except (TimeoutError, socket.timeout) as error:
        raise RuntimeError(
            f"OpenAI image API request timed out after {timeout}s. Try a smaller size, lower quality, or a larger --timeout."
        ) from error
    except URLError as error:
        raise RuntimeError(f"OpenAI image API request failed: {error.reason}") from error

    response_json = json.loads(body)
    images: list[tuple[bytes, str]] = []
    for item in response_json.get("data", []):
        if item.get("b64_json"):
            images.append((base64.b64decode(item["b64_json"]), f"image/{output_format}"))
        elif item.get("url"):
            with open_url(item["url"], timeout) as image_response:
                images.append((image_response.read(), image_response.headers.get_content_type()))

    if not images:
        preview = json.dumps(response_json, ensure_ascii=False)[:800]
        raise RuntimeError(f"OpenAI image response did not include image data: {preview}")

    return images


def resolve_provider(provider: str, model: str) -> str:
    if provider != "auto":
        return provider
    if model.startswith("gpt-image-"):
        return "openai"
    return "gemini"


def generate(
    api_key: str,
    base_url: str,
    provider: str,
    model: str,
    prompt: str,
    request_size: str,
    quality: str,
    n: int,
    timeout: int,
    retries: int,
    background: str,
    output_format: str,
) -> list[tuple[bytes, str]]:
    print(f"  Generating {n} image(s) with {provider}/{model} at {request_size} (quality={quality}, timeout={timeout}s)...")
    print(f"  Prompt: {prompt[:120]}{'...' if len(prompt) > 120 else ''}")

    images: list[tuple[bytes, str]] = []
    attempts = max(1, retries + 1)
    for variant in range(n):
        last_error: RuntimeError | None = None
        for attempt in range(attempts):
            try:
                t0 = time.time()
                if provider == "openai":
                    generated = request_openai_image(
                        api_key, base_url, model, prompt, request_size, quality, background, output_format, timeout
                    )
                else:
                    generated = request_gemini_image(api_key, base_url, model, prompt, timeout)
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


def remove_magenta_background(path: Path) -> None:
    try:
        from PIL import Image
    except ImportError as error:
        raise RuntimeError("Pillow is required for magenta-alpha postprocess. Install Pillow or omit postprocess.") from error

    with Image.open(path) as source:
        image = source.convert("RGBA")

    pixels = image.load()
    width, height = image.size
    removed = 0
    softened = 0
    for y in range(height):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            magenta_strength = min(r, b) - g
            channel_balance = abs(r - b)
            strong_key = r >= 180 and b >= 180 and g <= 110 and magenta_strength >= 70
            dark_key_shadow = r >= 45 and b >= 45 and g <= 90 and magenta_strength >= 32 and channel_balance <= 90
            if strong_key or dark_key_shadow:
                pixels[x, y] = (r, g, b, 0)
                removed += 1
            elif r >= 100 and b >= 100 and magenta_strength >= 22 and channel_balance <= 110:
                fade = int(255 * max(0, 55 - magenta_strength) / 33)
                neutral_green = min(255, g + magenta_strength)
                pixels[x, y] = (r, neutral_green, b, min(a, fade))
                softened += 1

    if path.suffix.lower() != ".png":
        path = path.with_suffix(".png")
    image.save(path)
    print(f"  Postprocess magenta-alpha: cleared {removed}px, softened {softened}px, kept canvas {image.size}")


def postprocess_image_file(path: Path, mode: str | None) -> Path:
    if not mode or mode == "none":
        return path

    normalized = mode.lower().replace("_", "-")
    if normalized in {"magenta-alpha", "demagenta"}:
        remove_magenta_background(path)
        return path.with_suffix(".png")

    raise RuntimeError(f"Unknown postprocess mode '{mode}'.")


def enforce_shape_alpha(path: Path, shape_rows: list[str] | None) -> None:
    if not shape_rows:
        return

    try:
        from PIL import Image
    except ImportError as error:
        raise RuntimeError("Pillow is required for shape alpha enforcement.") from error

    with Image.open(path) as source:
        image = source.convert("RGBA")

    width, height = image.size
    cols = max(len(row) for row in shape_rows)
    rows = len(shape_rows)
    pixels = image.load()
    cleared = 0
    for row_index in range(rows):
        row = shape_rows[row_index]
        for col_index in range(cols):
            occupied = col_index < len(row) and row[col_index] == "X"
            if occupied:
                continue
            left = col_index * width // cols
            right = (col_index + 1) * width // cols
            top = row_index * height // rows
            bottom = (row_index + 1) * height // rows
            for y in range(top, bottom):
                for x in range(left, right):
                    r, g, b, a = pixels[x, y]
                    if a != 0:
                        cleared += 1
                        pixels[x, y] = (r, g, b, 0)

    if cleared > 0:
        image.save(path)
        print(f"  Shape alpha: cleared {cleared}px from empty footprint cell(s)")


def uses_magenta_alpha(postprocess: str | None) -> bool:
    if not postprocess:
        return False
    return postprocess.lower().replace("_", "-") in {"magenta-alpha", "demagenta"}


def resize_image_file(path: Path, target_size: str | None, target_mode: str = "resize") -> None:
    if not target_size:
        return

    parsed = parse_size(target_size)
    if parsed is None:
        return

    try:
        from PIL import Image
    except ImportError as error:
        raise RuntimeError("Pillow is required for targetSize resizing. Install Pillow or omit targetSize.") from error

    with Image.open(path) as image:
        if image.size == parsed:
            return
        resample = getattr(Image.Resampling, "LANCZOS", Image.LANCZOS)
        if target_mode == "crop":
            target_w, target_h = parsed
            src_w, src_h = image.size
            target_ratio = target_w / target_h
            src_ratio = src_w / src_h
            if src_ratio > target_ratio:
                crop_w = int(round(src_h * target_ratio))
                left = max(0, (src_w - crop_w) // 2)
                image = image.crop((left, 0, left + crop_w, src_h))
            else:
                crop_h = int(round(src_w / target_ratio))
                top = max(0, (src_h - crop_h) // 2)
                image = image.crop((0, top, src_w, top + crop_h))
            print(f"  Center-cropped: {path.name} {src_w, src_h} -> {image.size}")
        elif target_mode != "resize":
            raise RuntimeError(f"Unknown target mode '{target_mode}'.")

        resized = image.resize(parsed, resample)
        resized.save(path)
        print(f"  Resized: {path.name} -> {parsed}")


def save_images(
    images: list[tuple[bytes, str]],
    output_dir: Path,
    base_name: str,
    target_size: str | None = None,
    postprocess: str | None = None,
    target_mode: str = "resize",
    shape_rows: list[str] | None = None,
) -> list[str]:
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
        filepath = postprocess_image_file(filepath, postprocess)
        resize_image_file(filepath, target_size, target_mode)
        enforce_shape_alpha(filepath, shape_rows)
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


def output_dir_for(asset_type: str, name: str, output_dir: str | None) -> Path:
    if output_dir:
        path = Path(output_dir)
        if not path.is_absolute():
            path = PROJECT_ROOT / path
        return path

    path = PROJECT_ROOT / TYPE_DIRS.get(asset_type, TYPE_DIRS["item"])
    if asset_type == "animation":
        path = path / name
    return path


def expand_animation_entry(entry: dict) -> list[dict]:
    frames = entry.get("frames")
    if not frames:
        return [entry]

    expanded: list[dict] = []
    base_name = entry.get("name", "animation")
    base_prompt = entry.get("prompt", "")
    for index, frame in enumerate(frames):
        frame_number = index + 1
        if isinstance(frame, dict):
            frame_prompt = frame.get("prompt", "")
            frame_name = frame.get("name", f"{base_name}_{frame_number:03d}")
        else:
            frame_prompt = str(frame)
            frame_name = f"{base_name}_{frame_number:03d}"

        next_entry = dict(entry)
        next_entry.pop("frames", None)
        next_entry["name"] = frame_name
        next_entry["type"] = "animation"
        next_entry["outputDir"] = entry.get("outputDir") or str(Path(TYPE_DIRS["animation"]) / base_name)
        next_entry["prompt"] = f"{base_prompt}, animation frame {frame_number}: {frame_prompt}" if base_prompt else frame_prompt
        expanded.append(next_entry)

    return expanded


def expand_entries(entries: list[dict]) -> list[dict]:
    expanded: list[dict] = []
    for entry in entries:
        if entry.get("type") == "animation":
            expanded.extend(expand_animation_entry(entry))
        else:
            expanded.append(entry)
    return expanded


def merge_defaults(entry: dict, defaults: dict) -> dict:
    merged = dict(defaults)
    merged.update(entry)
    return merged


def prefixed_prompt(entry: dict, prompt_prefix: str | None) -> str:
    parts = []
    if prompt_prefix:
        parts.append(prompt_prefix)
    shape_rows = entry.get("shapeRows") or []
    if entry.get("type") == "dish" and shape_rows:
        parts.append(shape_prompt(shape_rows, entry.get("targetSize") or entry.get("finalSize") or entry.get("size")))
    if entry.get("prompt"):
        parts.append(entry["prompt"])
    return " ".join(parts)


def main():
    parser = argparse.ArgumentParser(description="Generate 2D game assets for Unity")
    parser.add_argument("--prompt", help="Image generation prompt")
    parser.add_argument("--type", dest="asset_type", default="item",
                        choices=["item", "dish", "background", "character", "ui", "animation"])
    parser.add_argument("--name", default="asset", help="Output filename (without extension)")
    parser.add_argument("--size", help="Request/final image size (e.g. 1024x1024, 1536x1024)")
    parser.add_argument("--request-size", help="API request size when it differs from the final target size")
    parser.add_argument("--target-size", help="Resize the saved image to this final pixel size")
    parser.add_argument("--target-mode", default="resize", choices=["resize", "crop"], help="How to fit saved image to --target-size")
    parser.add_argument("--quality", default="high", choices=["low", "medium", "high", "auto"])
    parser.add_argument("--n", type=int, default=1, help="Number of variants")
    parser.add_argument("--manifest", help="Path to JSON manifest for batch generation")
    parser.add_argument("--only", nargs="*", default=[], help="Generate only these manifest asset names")
    parser.add_argument("--style-prefix", help="Global style prefix prepended to every prompt")
    parser.add_argument("--transparent", action="store_true",
                        help="Add transparent background hint to prompt")
    parser.add_argument("--provider", default=os.environ.get("IMAGE_PROVIDER", "auto"),
                        choices=["auto", "gemini", "openai"], help="Image API provider")
    parser.add_argument("--model", help="Model name. Use gpt-image-2 for OpenAI Images API.")
    parser.add_argument("--base-url", help="Override provider base URL")
    parser.add_argument("--output-dir", help="Override output directory")
    parser.add_argument("--output-format", default="png", choices=["png", "jpeg", "webp"])
    parser.add_argument("--background", default="auto", choices=["auto", "transparent", "opaque"])
    parser.add_argument("--postprocess", choices=["none", "magenta-alpha", "demagenta"], help="Local postprocess for saved images")
    parser.add_argument("--insecure-ssl", action="store_true", help="Disable TLS certificate verification for internal image API endpoints")
    parser.add_argument("--timeout", type=int, default=600, help="Per-request timeout in seconds")
    parser.add_argument("--retries", type=int, default=1, help="Retry count per requested image")
    parser.add_argument("--dry-run", action="store_true",
                        help="Print what would be generated without calling the API")

    args = parser.parse_args()
    global ALLOW_INSECURE_SSL
    ALLOW_INSECURE_SSL = args.insecure_ssl or os.environ.get("IMAGE_API_INSECURE_SSL") in {"1", "true", "TRUE", "yes", "YES"}

    api_key = None
    requested_provider = args.provider or os.environ.get("IMAGE_PROVIDER", "auto")
    if args.model:
        model = args.model
    elif requested_provider == "openai":
        model = os.environ.get("OPENAI_IMAGE_MODEL") or os.environ.get("IMAGE_MODEL") or OPENAI_IMAGE_MODEL
    else:
        model = os.environ.get("IMAGE_MODEL") or os.environ.get("GEMINI_IMAGE_MODEL") or GEMINI_MODEL
    provider = resolve_provider(args.provider, model)
    base_url = DEFAULT_OPENAI_BASE_URL if provider == "openai" else DEFAULT_GEMINI_BASE_URL
    if not args.dry_run:
        if provider == "openai":
            api_key = os.environ.get("OPENAI_API_KEY") or os.environ.get("GEMINI_API_KEY") or DEFAULT_API_KEY
            base_url = args.base_url or os.environ.get("OPENAI_BASE_URL", DEFAULT_OPENAI_BASE_URL)
        else:
            api_key = os.environ.get("GEMINI_API_KEY") or os.environ.get("OPENAI_API_KEY") or DEFAULT_API_KEY
            base_url = args.base_url or os.environ.get("GEMINI_BASE_URL", DEFAULT_GEMINI_BASE_URL)
        if not api_key:
            print("ERROR: image API key environment variable is not set.")
            print("  export GEMINI_API_KEY='your-key-here'  # Gemini")
            print("  export OPENAI_API_KEY='your-key-here'  # OpenAI")
            sys.exit(1)

    try:
        if args.manifest:
            manifest_path = Path(args.manifest)
            if not manifest_path.exists():
                print(f"ERROR: manifest file not found: {args.manifest}")
                sys.exit(1)

            manifest = json.loads(manifest_path.read_text())
            style_prefix = manifest.get("stylePrefix", args.style_prefix)
            manifest_provider = manifest.get("provider", provider)
            manifest_model = manifest.get("model", model)
            type_defaults = manifest.get("typeDefaults", {})
            type_prompt_prefixes = manifest.get("typePromptPrefixes", {})
            entries = expand_entries(selected_entries(manifest.get("assets", []), set(args.only)))

            if not entries:
                print("Manifest has no assets to generate.")
                return

            print(f"Batch generating {len(entries)} asset(s)...")
            total = 0
            for entry in entries:
                asset_type = entry.get("type", "item")
                entry = merge_defaults(entry, type_defaults.get(asset_type, {}))
                name = entry.get("name", "asset")
                prompt = prefixed_prompt(entry, type_prompt_prefixes.get(asset_type))
                target_size = entry.get("targetSize") or entry.get("finalSize") or args.target_size
                target_mode = entry.get("targetMode", args.target_mode)
                size = entry.get("size", args.size or TYPE_SIZES.get(asset_type, "512x512"))
                request_size = entry.get("requestSize") or args.request_size or size
                prompt_size = target_size or size
                quality = entry.get("quality", args.quality)
                n = entry.get("n", 1)
                transparent = entry.get("transparent", False)
                entry_model = entry.get("model", manifest_model)
                entry_provider = resolve_provider(entry.get("provider", manifest_provider), entry_model)
                entry_base_url = entry.get("baseUrl") or base_url
                output_format = entry.get("outputFormat", args.output_format)
                background = entry.get("background", args.background)
                postprocess = entry.get("postprocess")
                if uses_magenta_alpha(postprocess):
                    transparent = False
                if transparent and background == "auto" and entry_provider == "openai" and entry_model != "gpt-image-2":
                    background = "transparent"

                full_prompt = build_prompt(prompt, asset_type, style_prefix, transparent, prompt_size)
                output_dir = output_dir_for(asset_type, entry.get("animationName", name), entry.get("outputDir") or args.output_dir)

                print(f"\n[{name}] ({asset_type})")
                if args.dry_run:
                    print(f"  WOULD generate {n} image(s) with {entry_provider}/{entry_model}")
                    print(f"  Request size: {request_size}")
                    if target_size:
                        print(f"  Target size: {target_size}")
                    print(f"  Output: {output_dir}")
                else:
                    images = generate(
                        api_key,
                        entry_base_url,
                        entry_provider,
                        entry_model,
                        full_prompt,
                        request_size,
                        quality,
                        n,
                        args.timeout,
                        args.retries,
                        background,
                        output_format,
                    )
                    saved = save_images(images, output_dir, name, target_size, postprocess, target_mode, entry.get("shapeRows"))
                    total += len(saved)

            if not args.dry_run:
                print(f"\nDone. {total} asset(s) generated.")
            return

        if not args.prompt:
            print("ERROR: --prompt is required (or use --manifest for batch mode).")
            sys.exit(1)

        target_size = args.target_size
        target_mode = args.target_mode
        size = args.size or TYPE_SIZES.get(args.asset_type, "512x512")
        request_size = args.request_size or size
        prompt_size = target_size or size
        background = args.background
        if uses_magenta_alpha(args.postprocess):
            args.transparent = False
        if args.transparent and background == "auto" and provider == "openai" and model != "gpt-image-2":
            background = "transparent"
        full_prompt = build_prompt(args.prompt, args.asset_type, args.style_prefix, args.transparent, prompt_size)
        output_dir = output_dir_for(args.asset_type, args.name, args.output_dir)

        if args.dry_run:
            print(f"[{args.name}] ({args.asset_type})")
            print(f"  WOULD generate {args.n} image(s) with {provider}/{model}")
            print(f"  Request size: {request_size}")
            if target_size:
                print(f"  Target size: {target_size}")
            print(f"  Output: {output_dir}")
            return

        images = generate(
            api_key,
            base_url,
            provider,
            model,
            full_prompt,
            request_size,
            args.quality,
            args.n,
            args.timeout,
            args.retries,
            background,
            args.output_format,
        )
        save_images(images, output_dir, args.name, target_size, args.postprocess, target_mode)
    except RuntimeError as error:
        print(f"ERROR: {error}")
        sys.exit(1)


if __name__ == "__main__":
    main()
