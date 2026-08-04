# 装饰品图标生成提示词：批次 E

本批次覆盖 `tbpassiveitem.json` 新增的 11 项装饰品，仅生成下列 ID：

- `item_discount_active`
- `item_discount_adjust`
- `item_discount_active_festival`
- `item_discount_adjust_festival`
- `item_famous_knife`
- `item_heart_flat_all`
- `item_empty_heart_mult_all`
- `item_timeline_random`
- `item_lucky_chance`
- `item_more_events`
- `item_copy_food`

统一参考图：

- `gold_boss_sample_v2.png`：装饰品主体比例、暖深棕描线、赛璐璐层次。
- `req_super_up_sample_v2.png`：负面装饰品仍保持同一材质与视觉语言。
- `active_season_sour.png`：消耗品的清晰轮廓、材质高光和 80px 可读性。
- `card_action_food_normal_passive.png`：游戏整体的二维手绘、轻纸纹和清新暖色气质。

## 统一提示词骨架

```text
Use case: stylized-concept
Asset type: single restaurant-decoration game UI icon, authored at high resolution for a 70–80 px display
Input images: Image 1 is the approved positive-decoration style reference; Image 2 is the approved negative-decoration style reference; Image 3 is the current consumable-icon finish reference; Image 4 is the game's overall 2D hand-drawn restaurant art reference. Use them only for style, linework, color handling, material rendering, perspective, and visual density; do not copy their subjects.
Primary request: create one specific restaurant decoration matching the configured item name and gameplay meaning.
Scene/backdrop: perfectly flat solid chroma-key background for local removal.
Style/medium: fresh 2D hand-drawn game illustration; warm dark-brown outline; two to three cel-shaded value steps; restrained broad highlights; very light paper-grain texture.
Composition/framing: single dominant object or one tight inseparable object group, centered, slight top-down three-quarter view, occupying about 70% of the square canvas with generous padding and a strong silhouette readable at 80 px.
Constraints: no words, letters, numbers, logos, watermark, background scene, floor plane, cast shadow, contact shadow, reflection, glow cloud, effect arrow, loose particles, or decorative clutter. Plaques, photos, menus, receipts, tickets, and boards may contain only simple wordless pictograms. Keep the whole subject inside the canvas.
```

下列各项均与统一提示词骨架完整组合后，作为一次独立的内置图像生成调用提交。

## 逐项提示词

### item_discount_active｜强化券匣

- 色键：`#00ff00`
- 源文件：`sources/discount_active_chroma.png`
- 成品：`final/discount_active.png`

~~~text
Primary request: create the restaurant decoration “强化券匣” for an item that discounts strengthening-type consumable purchases.
Subject: one compact countertop voucher storage box with a rounded hinged lid, shown half-open, holding exactly three broad blank strengthening vouchers as one tidy inseparable group. Each voucher may carry only one large wordless covered-dish-and-spark pictogram; no writing or numbers. Make the box unmistakably physical restaurant counter storage, not a floating UI panel.
Color palette: saturated orange-gold lacquered box, coral-red lid and side panels, cream voucher faces, tiny muted sky-blue clasp; keep brown limited to outlines and the darkest seams.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_discount_adjust｜调整单夹

- 色键：`#ff00ff`
- 源文件：`sources/discount_adjust_chroma.png`
- 成品：`final/discount_adjust.png`

~~~text
Primary request: create the restaurant decoration “调整单夹” for an item that discounts adjustment-type consumable purchases.
Subject: one sturdy standing restaurant ticket clip on a compact ceramic base, holding exactly two broad blank adjustment slips. Use one large wordless circular-switch pictogram on the front slip; no writing, numbers, arrows, or tiny markings. Keep clip, slips, and base as one bold silhouette.
Color palette: clear sky-blue clip body, turquoise and cool teal ceramic base, pale cream slips, small coral clasp accent; keep brown limited to outlines and the darkest seams.
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background for local removal; do not use #ff00ff anywhere in the subject.
~~~

### item_discount_active_festival｜金边强化券

- 色键：`#00ff00`
- 源文件：`sources/discount_active_festival_chroma.png`
- 成品：`final/discount_active_festival.png`

~~~text
Primary request: create the restaurant decoration “金边强化券” for a stronger item that heavily discounts strengthening-type consumable purchases.
Subject: one premium restaurant counter voucher caddy with a broad violet lacquer body, scalloped gold edging, and exactly three large strengthening vouchers fanned tightly inside. The vouchers are blank except for one large wordless cloche-and-spark pictogram. No writing or numbers. Make it richer than the ordinary voucher box but still simple and readable.
Color palette: dominant violet and lavender lacquer, bright warm gold trim and voucher borders, cream voucher centers, one small coral seal; keep brown limited to outlines and the darkest seams.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_discount_adjust_festival｜烫金调整单

- 色键：`#00ff00`
- 源文件：`sources/discount_adjust_festival_chroma.png`
- 成品：`final/discount_adjust_festival.png`

~~~text
Primary request: create the restaurant decoration “烫金调整单” for a stronger item that heavily discounts adjustment-type consumable purchases.
Subject: one premium standing ticket organizer with a deep blue-teal enamel body, broad gold-stamped borders, and exactly three large blank adjustment slips held as one compact group. The front slip has one large wordless circular-switch pictogram; no writing, numbers, arrows, or tiny markings. Make it feel like an elegant restaurant stationery display.
Color palette: dominant deep teal-blue and navy-cyan, bright warm-gold stamped trim and clasp, pale cyan slips, small cream highlights; avoid bright green and keep brown limited to outlines.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_famous_knife｜名刀展示架

- 色键：`#00ff00`
- 源文件：`sources/famous_knife_chroma.png`
- 成品：`final/famous_knife.png`

~~~text
Primary request: create the restaurant decoration “名刀展示架” for a one-use item that rescues the player from failure at one heart.
Subject: one broad silver-blue chef knife resting safely and horizontally in a sculpted coral-red enamel display stand. The blade has one simple heart-shaped maker relief near the handle, not text. Use a short protective rail beneath the cutting edge so the icon reads as a prized restaurant display rather than a weapon in action.
Color palette: cool silver-blue blade, deep blue handle, coral-red and cream display stand, two small warm-gold fittings; keep brown limited to outlines and seams.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_heart_flat_all｜暖心壁灯

- 色键：`#00ff00`
- 源文件：`sources/heart_flat_all_chroma.png`
- 成品：`final/heart_flat_all.png`

~~~text
Primary request: create the restaurant decoration “暖心壁灯” for an item that increases every food score for each filled heart at settlement.
Subject: one self-contained wall sconce without an actual wall: a rounded coral ceramic backplate, two short cream brass arms, and one large opaque heart-shaped cream-yellow glass shade. Show broad painted highlight bands on the shade but no emitted glow, aura, rays, particles, or light pool.
Color palette: dominant coral pink and soft rose ceramic, creamy yellow and butter-gold lamp shade, tiny sky-blue screw caps; keep brown limited to outlines and the darkest seams.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_empty_heart_mult_all｜裂纹心形镜

- 色键：`#00ff00`
- 源文件：`sources/empty_heart_mult_all_chroma.png`
- 成品：`final/empty_heart_mult_all.png`

~~~text
Primary request: create the restaurant decoration “裂纹心形镜” for an item that increases every food multiplier for each empty heart at settlement.
Subject: one freestanding heart-shaped vanity mirror on a compact ceramic foot. The mirror surface is stylized opaque pale blue with exactly three bold violet crack lines radiating from one small upper-corner chip. Do not show any real reflection, room, face, text, shards, or loose broken pieces.
Color palette: violet and indigo-blue frame, pale periwinkle mirror face, cream ceramic foot, small coral connector; keep brown limited to outlines and seams.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_timeline_random｜命运骰子

- 色键：`#00ff00`
- 源文件：`sources/timeline_random_chroma.png`
- 成品：`final/timeline_random.png`

~~~text
Primary request: create the restaurant decoration “命运骰子” for an item that shuffles future actions when picked up.
Subject: one oversized ivory ceramic die displayed in a shallow violet-and-gold restaurant trinket tray. Show a clear three-quarter view with three visible faces and a few large recessed pips. The pips use several distinct colors—coral, sky blue, violet, honey gold, and muted teal—without forming text or numbers beyond normal die pips. Keep die and tray as one tight group.
Color palette: dominant ivory and ceramic white, multicolor pips, violet tray rim, warm-gold corner accents; keep brown limited to outlines and the darkest seams.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_lucky_chance｜四叶草福袋

- 色键：`#ff00ff`
- 源文件：`sources/lucky_chance_chroma.png`
- 成品：`final/lucky_chance.png`

~~~text
Primary request: create the restaurant decoration “四叶草福袋” for an item that increases the random weight of reward events.
Subject: one plump restaurant lucky drawstring pouch with a broad four-leaf-clover applique centered on the front, a thick honey-gold cord, and exactly two small blank ticket corners peeking from the top. The clover is a physical fabric patch, not a magical floating symbol. No writing, numbers, coins, sparkles, or loose particles.
Color palette: dominant fresh teal-green and jade fabric, slightly darker green clover patch, honey-gold cord and trim, cream ticket corners, one tiny sky-blue bead.
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background for local removal; do not use #ff00ff anywhere in the subject.
~~~

### item_more_events｜街角路牌

- 色键：`#00ff00`
- 源文件：`sources/more_events_chroma.png`
- 成品：`final/more_events.png`

~~~text
Primary request: create the restaurant decoration “街角路牌” for an item that makes event nodes more likely.
Subject: one compact freestanding street-corner restaurant signpost on a broad cream ceramic base, with exactly two chunky directional boards crossing at different heights. One board carries a large wordless cloche pictogram, the other a large wordless balloon pictogram. Do not use letters, numbers, arrows, maps, or a street background; the board shapes themselves indicate direction.
Color palette: dominant clear sky blue on one board, coral red on the other, cream post and base, warm-gold fittings, tiny violet edge accents; keep brown limited to outlines.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_copy_food｜双盖餐盘

- 色键：`#00ff00`
- 源文件：`sources/copy_food_chroma.png`
- 成品：`final/copy_food.png`

~~~text
Primary request: create the restaurant decoration “双盖餐盘” for an item that copies one owned food when picked up.
Subject: one elongated ceramic serving tray carrying exactly two matching small covered serving cloches side by side. The two domed lids should be near-identical twins but slightly offset in perspective, with large round knobs and bold cyan-blue rim bands. Keep all three pieces touching as one compact restaurant display.
Color palette: dominant ceramic white and warm ivory, clear cyan and medium blue rim bands, small coral knob caps, warm-gold tray corners; keep brown limited to outlines and seams.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~
