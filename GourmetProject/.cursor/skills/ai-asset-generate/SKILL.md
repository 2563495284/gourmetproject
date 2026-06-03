---
name: ai-asset-generate
description: Generate 2D game assets (sprites, items, backgrounds, UI elements, animation frames) for Unity using OpenAI's gpt-image-1 model. Use this skill when the user asks to create, generate, or batch-produce visual game assets — item icons, character sprites, backgrounds, buttons, panels, animation frames, etc.
---

# AI Asset Generator

Generates 2D game assets via OpenAI's gpt-image-1 API and saves them directly into the Unity project's `Assets/` folder for immediate use.

## When to use this skill

- User asks to generate a game asset ("make a health potion sprite", "generate a forest background")
- User wants to batch-generate assets from a list
- User asks about AI image generation for their game

## How it works

1. Understand what the user wants — asset type (item / background / character / UI / animation-frame), style, quantity
2. Pick the right output directory and resolution
3. Run the generation script: `.cursor/skills/ai-asset-generate/scripts/generate.py`
4. Report what was generated and where it was saved

## Asset directories

| Asset Type | Output Directory | Default Size |
|------------|-----------------|---------------|
| 道具 / 物品 / Item | `Assets/GameMain/Resources/Sprites/Items/` | 1024x1024 |
| 背景 / Background | `Assets/GameMain/Resources/Sprites/Backgrounds/` | 1536x1024 |
| 角色 / Character | `Assets/GameMain/Resources/Sprites/Characters/` | 1024x1024 |
| UI / 按钮 / 面板 | `Assets/GameMain/Resources/Sprites/UI/` | 1024x1024 |
| 动画帧 / Animation | `Assets/GameMain/Resources/Sprites/Animations/{name}/` | 1024x1024 |

## Usage

### Prerequisites

The script requires `openai` Python package. If not installed, run:

```bash
pip install openai
```

API key must be set as an environment variable:

```bash
export OPENAI_API_KEY="your-key-here"
```

The base URL defaults to `https://sapi-ai.hortorgames.com`. Override with `OPENAI_BASE_URL` if needed.

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
--type          Asset type: item | background | character | ui | animation
--name          Output filename (without extension)
--size          Override default size (supported: 1024x1024, 1024x1536, 1536x1024, auto)
--quality       low | medium | high | auto (default: high)
--n             Number of variants (1-10, default: 1)
--manifest      Path to a JSON manifest file for batch generation
--style-prefix  Optional global style prefix appended to every prompt
--transparent   Attempt transparent background via prompt engineering (adds "transparent background, isolated on alpha" to prompt)
```

## Manifest format

Create a JSON file with an array of asset requests:

```json
{
  "stylePrefix": "pixel art, top-down view, fantasy game, 32-bit style",
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
      "name": "forest_bg",
      "type": "background",
      "prompt": "dense enchanted forest with glowing mushrooms, parallax-ready, distant layers",
      "size": "1792x1024"
    },
    {
      "name": "btn_start",
      "type": "ui",
      "prompt": "wooden button with gold trim, medieval fantasy UI, 9-slice compatible with clear border area"
    }
  ]
}
```

## Best practices for prompts

When crafting prompts for gpt-image-1, include these elements for usable game assets:

- **Style anchor**: "pixel art", "hand-drawn", "flat vector", "cartoon", "realistic"
- **Perspective**: "top-down", "isometric", "side view", "front view"
- **Clean separation**: "clean edges", "solid white background", or "transparent background"
- **Game context**: "fantasy game item", "RPG icon", " mobile game UI element"
- **Technical hints for UI**: "9-slice compatible with clear border", "centered and symmetrical"
- **No text**: gpt-image-1 often garbles text — avoid requesting text in images

## After generation

Unity auto-imports new files in `Assets/`. The script outputs the file path. If the user needs the sprite settings adjusted (pixels per unit, filter mode, sprite border for 9-slice), use the `assets-modify` or `assetDatabase-refresh` MCP tools.
