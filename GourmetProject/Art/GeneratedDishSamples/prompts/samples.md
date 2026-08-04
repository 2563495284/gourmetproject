# 食物新风格样图生成记录

- 模式：内置 ImageGen
- 用途：食物画风样图；已确认的样图可同步到正式 `Sprites/Dishes` 资源
- 透明处理：纯绿色键背景，经 `remove_chroma_key.py` 的 soft matte、despill 处理

## 法棍 `baguette`

- 主体参考：`Assets/GameMain/Content/Resources/Sprites/Dishes/baguette.png`
- 风格参考：`cake_retain.png`、`cake_init_bonus.png`
- 成品：`../final/baguette_sample.png`（1536×512 RGBA）

```text
Use case: stylized-concept
Asset type: horizontal game dish sprite sample
Input images: Image 1 is the subject and silhouette reference; Images 2 and 3 are style references only.
Primary request: redraw one freshly baked French baguette in the clean new game-icon style.
Subject: one long horizontal baguette with a softly rounded loaf shape, three clear diagonal scoring cuts, golden crust, toasted orange edges, pale cream split dough.
Style/medium: polished hand-painted game UI sprite matching Images 2 and 3; fresh and cheerful; simplified rounded forms; medium dark warm-brown outer contour; sparse controlled inner lines; smooth cel-shaded volume; crisp cream highlights; clean edges; no sketch texture and no dense detail.
Composition/framing: preserve Image 1's long three-cell horizontal silhouette and proportions; centered side-three-quarter view; the loaf occupies about 86% of the canvas width and 55% of its height; generous padding; one isolated object only.
Color palette: honey gold, warm orange, pale cream, with restrained warm-brown outline. Natural bread colors are allowed here, but avoid a muddy all-brown result.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for later removal.
Constraints: background is one uniform color with no gradient, texture, floor plane, shadow, reflection, or lighting variation; keep the baguette fully separated from the background; do not use #00ff00 anywhere in the subject; no plate, basket, cloth, crumbs, steam, face, limbs, text, numbers, logo, watermark, cast shadow, contact shadow, or extra objects.
```

## 芭菲 `parfait`

- 主体参考：`Assets/GameMain/Content/Resources/Sprites/Dishes/parfait.png`
- 风格参考：`cake_retain.png`、`cake_to_gold.png`
- 成品：`../final/parfait_sample.png`（1024×1536 RGBA）

```text
Use case: stylized-concept
Asset type: vertical game dish sprite sample
Input images: Image 1 is the subject, tall silhouette, and ingredient reference; Images 2 and 3 are style references only.
Primary request: redraw one tall berry-and-cream parfait in the clean new game-icon style.
Subject: one tall parfait with clear stacked layers of vanilla cream, raspberry sauce, blueberry cream, and small golden cake pieces; topped with one coral-red strawberry, two dark-blue blueberries, a cream swirl, and two slim golden wafer sticks. Use a solid pale sky-blue and lavender illustrated dessert cup with an opaque graphic surface—do not render real transparent glass.
Style/medium: polished hand-painted game UI sprite matching Images 2 and 3; fresh and cheerful; simplified rounded forms; medium dark warm-brown outer contour; sparse controlled inner lines; smooth cel-shaded volume; crisp highlights; clean edges; rich but organized ingredient layers; no sketch texture and no dense decoration.
Composition/framing: preserve Image 1's tall two-by-three-cell silhouette; centered slight three-quarter front view; the parfait occupies about 56% of canvas width and 88% of canvas height; generous padding; one isolated object only.
Color palette: cream white, coral red, raspberry, blueberry navy, sky blue, lavender, and small honey-gold accents. No green in the subject. Keep color blocks clearly separated and avoid brown dominance.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for later removal.
Constraints: background is one uniform color with no gradient, texture, floor plane, shadow, reflection, or lighting variation; keep the parfait fully separated from the background; do not use #00ff00 anywhere in the subject; no true transparency or translucent glass; no plate, spoon, napkin, table, face, limbs, text, numbers, logo, watermark, cast shadow, contact shadow, or extra objects.
```

## 蝴蝶酥 `palmier`

- 风格参考：本目录已确认的 `baguette_sample.png`、`eggtart_sample.png`
- 成品：`../final/palmier_sample.png`（1024×1024 RGBA）
- 形状：`XX/XX`
- 占格校验：完整落在 2×2 边界内

```text
Use case: stylized-concept
Asset type: square game dish sprite
Input images: Images 1 and 2 are approved food-style references only.
Primary request: create exactly ONE classic palmier pastry.
Subject: one single standard heart-shaped butterfly palmier made from one continuous folded piece of laminated puff pastry. It must have exactly TWO curled spiral wings, one left curl and one right curl, joined together at one tapered bottom point. The result must unmistakably read as one individual pastry, not three pastries, not three lobes, and not a cluster.
Style/medium: polished hand-painted game UI sprite matching the reference images; fresh and cheerful; simplified rounded forms; medium dark warm-brown outer contour; sparse controlled inner lines; smooth cel-shaded volume; crisp butter-cream highlights; clean edges; visible laminated pastry folds and a few sugar crystals; no sketch texture and no dense linework.
Composition/framing: square canvas; one large heart/butterfly-shaped pastry centered and rotated about 20 degrees clockwise; generous transparent padding; simple strong silhouette readable at small size.
Color palette: bright honey gold, buttery cream, light caramel, tiny white sugar highlights; brown restricted to the thin outline and narrow toasted accents.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for later removal.
Constraints: background is one uniform color with no gradient, texture, floor plane, shadow, reflection, or lighting variation; do not use #00ff00 in the subject; exactly ONE pastry; exactly TWO spiral wings; one connected bottom point; no third curl, no third lobe, no repeated copy, no group, no plate, tray, paper, crumbs outside the pastry, face, limbs, text, numbers, logo, watermark, cast shadow, contact shadow, or extra objects.
```

## 蛋挞 `eggtart`

- 主体参考：`Assets/GameMain/Content/Resources/Sprites/Dishes/eggtart.png`
- 风格参考：本目录已确认的 `baguette_sample.png`、`parfait_sample.png`
- 成品：`../final/eggtart_sample.png`（1024×512 RGBA）

```text
Use case: stylized-concept
Asset type: wide horizontal game dish sprite sample
Input images: Image 1 is the subject, count, and two-cell composition reference; Images 2 and 3 are the approved new food style references.
Primary request: redraw exactly two egg tarts in the approved clean new food-icon style.
Subject: exactly two equal-sized Portuguese-style egg tarts placed side by side, each with a crisp layered flaky crust, glossy bright golden custard center, and a few small coral-brown caramelized spots. Present them in a shallow pale sky-blue serving tray with a cream interior and a restrained lavender rim so the image is colorful without distracting from the food.
Style/medium: polished hand-painted game UI sprite matching Images 2 and 3; fresh and cheerful; simplified rounded forms; medium dark warm-brown outer contours; sparse controlled inner lines; smooth cel-shaded volume; crisp cream highlights; clean edges; no sketch texture and no dense linework.
Composition/framing: wide two-to-one canvas; preserve Image 1's horizontal two-cell silhouette; centered slight three-quarter top view; the pair and tray occupy about 88% of canvas width and 72% of canvas height; generous padding; the two tarts must remain clearly separate and equally prominent.
Color palette: lemon yellow custard, honey-gold crust, pale sky blue, cream white, restrained lavender, tiny coral toasted accents. Brown is restricted to thin outlines and small baked details, avoiding a muddy all-brown result.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for later removal.
Constraints: background is one uniform color with no gradient, texture, floor plane, shadow, reflection, or lighting variation; do not use #00ff00 anywhere in the subject; exactly two egg tarts; one shallow tray only; no utensils, napkin, table, crumbs outside the tray, face, limbs, text, numbers, logo, watermark, cast shadow, contact shadow, or extra objects.
```
