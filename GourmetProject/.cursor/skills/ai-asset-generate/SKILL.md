---
name: ai-asset-generate
description: Generate 2D game assets (sprites, items, backgrounds, UI elements, animation frames) for Unity using Gemini image models or OpenAI GPT Image models such as gpt-image-2. Use this skill when the user asks to create, generate, or batch-produce visual game assets — item icons, character sprites, backgrounds, buttons, panels, animation frames, etc.
---

# AI Asset Generator

Generates 2D game assets via Gemini's `gemini-3.1-flash-image-preview` API or OpenAI-compatible GPT Image API models and saves them directly into the Unity project's `Assets/` folder for immediate use.

## When to use this skill

- User asks to generate a game asset ("make a health potion sprite", "generate a forest background")
- User wants to batch-generate assets from a list
- User asks about AI image generation for their game

## How it works

1. Understand what the user wants — asset type (item / dish / background / character / UI / animation-frame), style, quantity
2. Pick the right output directory and resolution
3. Pick the image provider/model and request size
4. Run the generation script: `.cursor/skills/ai-asset-generate/scripts/generate.py`
5. Let Unity import policy normalize sprite settings such as PPU
6. Report what was generated and where it was saved

## Asset directories

| Asset Type | Output Directory | Default Size |
|------------|-----------------|---------------|
| 道具 / 物品 / Item | `Assets/GameMain/Resources/Sprites/Items/` | 1024x1024 |
| 菜品 / Dish | `Assets/GameMain/Resources/Sprites/Dishes/` | 512x512 per occupied grid cell |
| 背景 / Background | `Assets/GameMain/Resources/Sprites/Backgrounds/` | 1536x1024 |
| 角色 / Character | `Assets/GameMain/Resources/Sprites/Characters/` | 1024x1024 |
| UI / 按钮 / 面板 | `Assets/GameMain/Resources/Sprites/UI/` | 1024x1024 |
| 动画帧 / Animation | `Assets/GameMain/Resources/Sprites/Animations/{name}/` | 1024x1024 |

## Usage

### Prerequisites

The base API script uses Python's standard library. `--target-size` and `--postprocess magenta-alpha` require Pillow, which this project already uses in the helper scripts.

API key must be set as an environment variable:

```bash
export GEMINI_API_KEY="your-key-here"
```

The model defaults to `gemini-3.1-flash-image-preview`. The Gemini base URL defaults to `https://sapi-ai.hortorgames.com/gemini`. Override with `GEMINI_IMAGE_MODEL` or `GEMINI_BASE_URL` if needed.

The OpenAI-compatible GPT Image base URL defaults to `https://sapi-ai.hortorgames.com/v1`. Override with `OPENAI_IMAGE_MODEL` or `OPENAI_BASE_URL` if needed.

For OpenAI GPT Image models:

```bash
export OPENAI_API_KEY="your-key-here"
python .cursor/skills/ai-asset-generate/scripts/generate.py \
  --provider openai \
  --model gpt-image-2 \
  --prompt "chunky hand-drawn orange button frame, no text, transparent asset" \
  --type ui \
  --name ui_btn_primary_v2 \
  --size 1024x1024
```

`gpt-image-2` request sizes must use valid API dimensions. For small final assets, request a larger valid image and resize after generation:

```bash
python .cursor/skills/ai-asset-generate/scripts/generate.py \
  --provider openai \
  --model gpt-image-2 \
  --prompt "small round close button, bold black X, no text" \
  --type ui \
  --name ui_btn_close \
  --size 1024x1024 \
  --target-size 512x512
```

### Single asset

```bash
python .cursor/skills/ai-asset-generate/scripts/generate.py \
  --prompt "pixel art health potion, red glass bottle with cork, fantasy game item, clean edges, centered, white background" \
  --type item \
  --name potion_health
```

### Batch generation via manifest

```bash
python .cursor/skills/ai-asset-generate/scripts/generate.py \
  --manifest .cursor/skills/ai-asset-generate/manifest.json
```

### Full options

```
--prompt        Text prompt for the image (required unless --manifest)
--type          Asset type: item | dish | background | character | ui | animation
--name          Output filename (without extension)
--size          Override default size (supported: 1024x1024, 1024x1536, 1536x1024, auto)
--request-size  API request size when different from final target size
--target-size   Resize the saved image to this final pixel size
--target-mode   resize | crop
--quality       low | medium | high | auto (default: high)
--n             Number of variants (1-10, default: 1)
--manifest      Path to a JSON manifest file for batch generation
--style-prefix  Optional global style prefix appended to every prompt
--transparent   Attempt transparent background via prompt engineering (adds "transparent background, isolated on alpha" to prompt)
--provider      auto | gemini | openai
--model         Image model, e.g. gemini-3.1-flash-image-preview or gpt-image-2
--output-dir    Override output directory
--output-format png | jpeg | webp
--background    auto | transparent | opaque (OpenAI provider only)
--postprocess   none | magenta-alpha | demagenta
```

## Manifest format

Create a JSON file with an array of asset requests:

```json
{
  "stylePrefix": "pixel art, top-down view, fantasy game, 32-bit style",
  "typeDefaults": {
    "item": {
      "outputDir": "Assets/GameMain/Resources/Sprites/Items",
      "postprocess": "magenta-alpha"
    },
    "dish": {
      "outputDir": "Assets/GameMain/Resources/Sprites/Dishes",
      "postprocess": "magenta-alpha"
    }
  },
  "typePromptPrefixes": {
    "item": "Item icon rule: one centered prop object, square icon canvas, bold readable silhouette, not a board-grid dish.",
    "dish": "Dish sprite rule: grid-footprint food sprite; one cell is 512x512; shapeRows X cells contain food and . cells remain pure #FF00FF for alpha removal."
  },
  "assets": [
    {
      "name": "potion_health",
      "type": "item",
      "prompt": "health potion in red glass bottle with cork, glowing liquid"
    },
    {
      "name": "sword_iron",
      "type": "item",
      "prompt": "iron sword with leather-wrapped handle"
    },
    {
      "name": "rice",
      "type": "dish",
      "size": "512x512",
      "requestSize": "1024x1024",
      "targetSize": "512x512",
      "shapeRows": ["X"],
      "prompt": "one plump rice ball with sesame dots and seaweed wrap"
    },
    {
      "name": "noodle",
      "type": "dish",
      "size": "1024x1024",
      "shapeRows": ["X.", "XX"],
      "prompt": "crooked pile of stir-fried noodles arranged as an L-shaped connected food mass"
    },
    {
      "name": "forest_bg",
      "type": "background",
      "prompt": "dense enchanted forest with glowing mushrooms, parallax-ready, distant layers",
      "size": "1792x1024"
    },
    {
      "name": "btn_start",
      "type": "ui",
      "prompt": "wooden button with gold trim, medieval fantasy UI, 9-slice compatible with clear border area",
      "model": "gpt-image-2",
      "provider": "openai",
      "requestSize": "1024x1024",
      "targetSize": "512x512"
    },
    {
      "name": "serve_hand_wave",
      "type": "animation",
      "model": "gpt-image-2",
      "provider": "openai",
      "requestSize": "1024x1024",
      "targetSize": "512x512",
      "prompt": "same cartoon server hand, clean silhouette, consistent camera and line weight",
      "frames": [
        "frame 1 idle pose, hand at lower left",
        "frame 2 anticipation pose, wrist bends upward",
        "frame 3 wave peak, palm open",
        "frame 4 settle pose, hand returns toward idle"
      ]
    }
  ]
}
```

## Best practices for prompts

When crafting prompts for Gemini image generation, include these elements for usable game assets:

- **Style anchor**: "pixel art", "hand-drawn", "flat vector", "cartoon", "realistic"
- **Perspective**: "top-down", "isometric", "side view", "front view"
- **Clean separation**: "clean edges", "solid white background", or "transparent background"
- **Game context**: "fantasy game item", "RPG icon", " mobile game UI element"
- **Technical hints for UI**: "9-slice compatible with clear border", "centered and symmetrical"
- **No text**: image models often garble text — avoid requesting text in images

### Item rules

Use `type: "item"` only for non-dish props under `Assets/GameMain/Resources/Sprites/Items/`, such as knives, bells, plates, coupons, or other gameplay items.

- Treat each item as one centered icon object on a square canvas.
- Do not use `shapeRows` for item assets.
- Do not arrange item art to fill board cells or tetromino footprints.
- Prefer a flat removable `#FF00FF` background plus `postprocess: "magenta-alpha"` when using `gpt-image-2`.
- Keep padding comfortable so the icon reads in cards and rewards.

### Dish rules

Use `type: "dish"` only for board food sprites under `Assets/GameMain/Resources/Sprites/Dishes/`.

- Add `shapeRows` from `TbDishBase.shapeRows`.
- One occupied grid cell is `512x512`; final canvas should match the shape bounding box, for example `1x1=512x512`, `2x1=1024x512`, `2x2=1024x1024`, `3x2=1536x1024`.
- If the GPT Image request size cannot match an extreme final aspect ratio, request the nearest valid larger canvas and use `targetMode: "crop"` rather than stretching the dish.
- `X` cells contain food; `.` cells must be empty and become alpha after postprocess.
- Keep the full rectangular canvas for the shape bounding box. Do not crop a dish to its silhouette.
- Do not draw grid lines, guides, cell borders, checkerboards, or frame rectangles.
- Do not paint cast shadows outside occupied cells; `DishPieceView` renders the runtime contact shadow separately.
- The generator enforces `.` cells by clearing their alpha after resize/crop, so prompt the empty cells explicitly and still rely on `shapeRows` as the final hard mask.
- Food stays pure food: no face, no eyes, no mouth, no limbs, no mascot treatment.

## After generation

Unity auto-imports new files in `Assets/`. The script outputs the file path.

Sprite import settings are centralized in `Assets/GameMain/Editor/SpriteImportPolicy.cs`:

- Generated PNG sprites under `Assets/GameMain/Resources/Sprites/` import as sprites
- PPU is normalized to `100`
- mipmaps are disabled
- alpha transparency is enabled
- wrap mode is clamped

Do not use per-texture PPU to control visual size. For UI, control size with RectTransform dimensions and 9-slice borders. For world sprites, control size with transform scale, SpriteRenderer draw mode, or generated pixel dimensions. Use the Unity menu `Tools/GourmetProject/Assets/Report Non-100 Sprite PPU` before normalizing old resources, then `Tools/GourmetProject/Assets/Reimport Sprites With Import Policy` when ready to apply the policy to existing textures.

For 9-slice UI, keep sprite borders in Unity/importer data and keep the PNG's actual corner/border art aligned to those pixel borders. Avoid changing PPU to fake a different border thickness.

## Animation frames

Animation generation is supported as a frame sequence, not as a single magic `n` variants call.

Use `type: "animation"` with a stable base prompt and a `frames` array. The script expands each frame to a separate output PNG under `Assets/GameMain/Resources/Sprites/Animations/{name}/` unless `outputDir` is provided. Keep the frame prompts explicit and boring: describe pose, silhouette change, and camera consistency. Do not rely on `n` for frame animation; `n` is for alternate variants of the same frame.

## Style pipelines

Named style manifests live in `.cursor/skills/ai-asset-generate/manifests/`. Each manifest is a reusable visual style definition:

- `style-90s-grotesque-cartoon.json` — 90s American grotesque cartoon style for GourmetProject's menu/background/character assets

To regenerate all assets for a style:

```bash
python .cursor/skills/ai-asset-generate/scripts/generate.py \
  --manifest .cursor/skills/ai-asset-generate/manifests/style-90s-grotesque-cartoon.json
```

To add a new asset to an existing style, append an entry to that manifest's `assets` array and re-run the command. Keep the shared look in `stylePrefix`; keep each asset prompt focused on subject, composition, and technical constraints.
