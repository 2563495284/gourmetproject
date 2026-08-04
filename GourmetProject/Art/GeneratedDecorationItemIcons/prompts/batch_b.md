# 装饰品图标生成提示词：批次 B

本批次覆盖 `tbpassiveitem.json` 顺序第 21–40 项，从 `item_req_feast_down` 到 `item_flavor_remove_gold`，共 20 张。每个资产均通过 built-in `imagegen` 独立生成一次。

统一参考图：

- `gold_boss_sample_v2.png`：装饰品主体比例、暖深棕描线、赛璐璐层次。
- `req_super_up_sample_v2.png`：同套装饰品的材质、轮廓和紧凑构图。
- `active_season_sour.png`：消耗品的清晰轮廓、轻俯视三分之四视角和 80px 可读性。
- `card_action_food_normal_passive.png`：游戏整体的二维手绘、轻纸纹和清新餐厅配色。

## 统一提示词骨架

以下骨架与每项代码块原样组合后提交；第 26 项开始额外加入“palette variation is important, do not make the whole icon brown”。

```text
Use case: stylized-concept
Asset type: square game UI icon for a restaurant decoration/passive item
Input images: Image 1 gold trophy sample is the closest icon finish and centering reference; Image 2 restaurant rush alert sample is a supporting material and outline reference; Image 3 lemon-in-wooden-box consumable is the main linework, cel-shading, three-quarter-view, and padding reference; Image 4 bakery action-card illustration is the warm palette, hand-painted paper texture, and overall game-art reference.
Style/medium: cohesive 2D hand-painted cartoon game icon matching the references; warm dark-brown outlines; rounded simplified construction; two to three cel-shaded value steps; subtle paper grain; restrained highlights, not glossy 3D rendering.
Composition/framing: one centered compact subject or tightly grouped set in a light overhead three-quarter view; complete silhouette inside about 70% of the square canvas; generous uniform padding; legible at 80 pixels.
Constraints: crisp separated edges; no cast or contact shadow; no text, letters, written labels, digits, numbers, percent signs, mathematical symbols, logo, watermark, or extra scenery.
Avoid: photorealism, 3D render look, metallic bloom, excessive specular streaks, thin fragile details, clutter.
```

绿幕统一追加：

```text
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for background removal.
Background constraints: one exact uniform #00ff00 with no shadow, gradient, texture, reflection, floor plane, or lighting variation; do not use #00ff00 anywhere in the subject.
```

绿色主体统一改用：

```text
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background because the subject contains green.
Background constraints: one exact uniform #ff00ff with no shadow, gradient, texture, reflection, floor plane, or lighting variation; do not use #ff00ff or magenta anywhere in the subject.
```

## 逐项实际提示词与色键

### item_req_feast_down｜金边证书框

- 色键：`#00ff00`
- 源文件：`sources/req_feast_down_chroma.png`
- 成品：`final/req_feast_down.png`

~~~text
Primary request: Create a single "gold-rimmed certificate frame" restaurant decoration. Show an ornate warm wooden standing certificate frame with a clear gold rim, a blank cream certificate sheet, a small embossed serving-cloche medallion and restrained laurel ornament. It should suggest a prestigious star-rating evaluation award without any written content.
Color palette: warm honey gold, orange-brown wood, cream paper, tiny muted teal accent.
~~~

### item_count_le_mult｜素色桌旗

- 色键：`#00ff00`
- 源文件：`sources/count_le_mult_chroma.png`
- 成品：`final/count_le_mult.png`

~~~text
Primary request: Create a single "plain table runner" restaurant decoration. Show a neatly folded blue-gray and warm-cream woven linen table runner draped over a short warm wooden display rail, with restrained honey-gold edging and one tiny serving-cloche medallion. Keep the cloth broad, simple, and mostly undecorated so it clearly reads as a tasteful low-key table runner.
Color palette: muted blue-gray linen, cream, warm orange-brown wood, honey-gold trim.
~~~

### item_count_ge_mult｜多层餐盘架

- 色键：`#00ff00`
- 源文件：`sources/count_ge_mult_chroma.png`
- 成品：`final/count_ge_mult.png`

~~~text
Primary request: Create a single "multi-tier plate stand" restaurant decoration. Show a sturdy three-tier warm brass and wood dessert stand with three broad round plates, holding many small colorful pastries arranged in dense but simple clusters. The three large tiers and plentiful food should remain readable as bold shapes at icon size.
Color palette: warm brass, orange-brown wood, cream plates, restrained strawberry red and pastry gold accents.
~~~

### item_perma_flat_all｜青花调味罐

- 色键：`#00ff00`
- 源文件：`sources/perma_flat_all_chroma.png`
- 成品：`final/perma_flat_all.png`

~~~text
Primary request: Create a single "blue-and-white seasoning jar" restaurant decoration. Show one rounded porcelain seasoning jar with a fitted brass-rimmed lid and a small spoon, painted with simple cobalt-blue steam curls and floral shapes only, never written characters. Add one restrained sparkle to suggest a permanent food improvement.
Color palette: warm ivory porcelain, cobalt blue glaze, honey-gold brass, tiny muted teal accent.
~~~

### item_perma_flat_all_plus｜成套调味罐

- 色键：`#00ff00`
- 源文件：`sources/perma_flat_all_plus_chroma.png`
- 成品：`final/perma_flat_all_plus.png`

~~~text
Primary request: Create a compact "matching seasoning jar set" restaurant decoration. Show a small warm wooden tray holding exactly three coordinated blue-and-white porcelain seasoning jars in slightly different rounded shapes, with brass-rimmed lids and simple cobalt steam or floral motifs only. Make the set clearly richer and more complete than a single jar.
Color palette: warm ivory porcelain, cobalt-blue glaze, orange-brown wood, honey-gold brass.
~~~

### item_gold_on_shop｜供货商合影

- 色键：`#00ff00`
- 源文件：`sources/gold_on_shop_chroma.png`
- 成品：`final/gold_on_shop.png`

~~~text
Primary request: Create a single "supplier group photograph" restaurant decoration. Show a coral-red lacquered tabletop photo frame containing a simplified cheerful sepia snapshot of an apron-wearing restaurant owner and a delivery supplier shaking hands while holding one produce crate. Faces are friendly simple shapes; the photograph contains no shop sign or writing.
Color palette: coral-red frame as the dominant color, cream photograph, sky-blue clothing accents, honey-gold corners, very little brown.
~~~

### item_extra_interest｜挂墙周历

- 色键：`#00ff00`
- 源文件：`sources/extra_interest_chroma.png`
- 成品：`final/extra_interest.png`

~~~text
Primary request: Create a single "wall weekly calendar" restaurant decoration. Show a violet enamel wall calendar board with one row of exactly seven blank cream day tabs; the final tab carries only a small raised gold coin-and-serving-cloche pictogram. Add a small sky-blue hanging ribbon and brass pegs. Absolutely no written day names or digits.
Color palette: violet and cream dominant, sky blue secondary, honey-gold accents, no brown-dominant wood.
~~~

### item_interest_cap｜聚宝盆摆件

- 色键：`#00ff00`
- 源文件：`sources/interest_cap_chroma.png`
- 成品：`final/interest_cap.png`

~~~text
Primary request: Create a single "wealth bowl ornament" for a restaurant counter. Show a rounded honey-gold prosperity bowl filled with large readable gold coins, sitting on a coral-red quilted cushion, with two small violet gemstone accents and a tiny cloche emblem. Keep the bowl toy-like and matte, not mirror-metal.
Color palette: honey gold dominant, coral red cushion, violet accents, cream highlights.
~~~

### item_loan｜挂账记录板

- 色键：`#00ff00`
- 源文件：`sources/loan_chroma.png`
- 成品：`final/loan.png`

~~~text
Primary request: Create a single "restaurant charge-account record board" decoration. Show a compact warm wooden ledger board with two pinned blank cream receipt slips, a coral-red binding cord and wax seal, a small sky-blue clip, and a modest gold coin pouch attached at one corner. The papers must be entirely blank with no marks.
Color palette: cream paper and coral red are prominent, sky blue and honey gold accents; brown is allowed only for the actual wooden board.
~~~

### item_extra_day｜第八日启程

- 色键：`#00ff00`
- 源文件：`sources/extra_day_chroma.png`
- 成品：`final/extra_day.png`

~~~text
Primary request: Create a single "departure for an extra day" restaurant decoration. Show a sky-blue enamel wall calendar plaque with exactly eight blank cream day tabs arranged as bold tiles; the final tab is raised and carries only a simple coral-red sunrise pictogram. Attach one small violet chef travel satchel at the bottom edge. Do not render any digits or writing.
Color palette: sky blue dominant, cream tabs, coral-red sunrise, violet satchel, honey-gold fasteners.
~~~

### item_week_minus｜黄铜沙漏

- 色键：`#00ff00`
- 源文件：`sources/week_minus_chroma.png`
- 成品：`final/week_minus.png`

~~~text
Primary request: Create a single "brass hourglass" restaurant decoration that suggests time moving backward. Show a rounded honey-gold brass hourglass with violet sand visibly flowing upward from the lower bulb to the upper bulb, held by two sky-blue enamel side posts. No arrows or written symbols are needed.
Color palette: honey gold dominant, violet sand, sky blue supports, ceramic-white glass highlights.
~~~

### item_every3_next_mult｜木制节拍器

- 色键：`#00ff00`
- 源文件：`sources/every3_next_mult_chroma.png`
- 成品：`final/every3_next_mult.png`

~~~text
Primary request: Create a single "wooden metronome" restaurant decoration. Show a coral-red painted wooden metronome with a cream face, a honey-gold pendulum, exactly three large brass beat beads lined along its base, and one tiny pastry medallion at the pendulum tip. It must read immediately as a metronome, with no scale markings or writing.
Color palette: coral red dominant painted wood, cream face, honey gold hardware, tiny sky-blue accent; visible brown only on narrow exposed wood edges.
~~~

### item_first_+2｜迎客灯笼

- 色键：`#00ff00`
- 源文件：`sources/first_+2_chroma.png`
- 成品：`final/first_+2.png`

~~~text
Primary request: Create a single "welcome lantern" restaurant decoration. Show a plump coral-red hanging restaurant lantern with cream translucent panels, honey-gold caps and tassel, and one simple serving-cloche cutout glowing warmly in the front panel. No characters or shop writing.
Color palette: coral red dominant, cream light, honey gold trim, tiny violet tassel accent.
~~~

### item_last_+2｜餐后茶具

- 色键：`#00ff00`
- 源文件：`sources/last_+2_chroma.png`
- 成品：`final/last_+2.png`

~~~text
Primary request: Create a compact "after-dinner tea set" restaurant decoration. Show one rounded ceramic-white teapot and one final matching teacup on a violet oval serving tray, decorated only with simple sky-blue steam curls and a coral-red berry knob. A single honey-gold sparkle rises from the cup to emphasize the final serving.
Color palette: ceramic white dominant, sky blue pattern, violet tray, coral-red accent, honey-gold sparkle.
~~~

### item_extra_active_slots｜墙面置物架

- 色键：`#00ff00`
- 源文件：`sources/extra_active_slots_chroma.png`
- 成品：`final/extra_active_slots.png`

~~~text
Primary request: Create a single "wall item shelf" restaurant decoration. Show a violet-painted wall shelf with exactly two large empty rounded cubbies side by side, each lined in cream and fitted with a small sky-blue ticket-shaped holder. Use thick simple dividers and honey-gold corner caps so the two available slots read clearly at icon size.
Color palette: violet dominant, cream interiors, sky-blue holders, honey-gold trim; minimal exposed wood.
~~~

### item_extra_active_slots_max｜整墙储物架

- 色键：`#00ff00`
- 源文件：`sources/extra_active_slots_max_chroma.png`
- 成品：`final/extra_active_slots_max.png`

~~~text
Primary request: Create a single "full-wall storage rack" restaurant decoration. Show a larger sky-blue painted storage rack with exactly three large empty rounded cubbies in one clear row, cream interiors, coral-red side panels, and honey-gold corner hardware. Make it visibly broader and more substantial than a two-slot shelf.
Color palette: sky blue dominant, cream interiors, coral-red sides, honey-gold trim; no brown-dominant wood.
~~~

### item_gold_on_active｜皮面账单夹

- 色键：`#00ff00`
- 源文件：`sources/gold_on_active_chroma.png`
- 成品：`final/gold_on_active.png`

~~~text
Primary request: Create a single "leather bill folder" restaurant decoration. Show an open deep-violet leather restaurant check presenter with a blank cream receipt, one cyan active-item ticket tucked into the inner pocket, and a compact fan of several honey-gold coins. The receipt must be entirely blank.
Color palette: deep violet dominant, cyan ticket, cream paper, honey gold coins, tiny coral-red stitch accent.
~~~

### item_block_active｜封条备品柜

- 色键：`#00ff00`
- 源文件：`sources/block_active_chroma.png`
- 成品：`final/block_active.png`

~~~text
Primary request: Create a single "sealed spare-item cabinet" restaurant decoration. Show a compact ceramic-white and sky-blue painted cabinet with its doors tightly crossed by broad coral-red sealing bands and one large violet wax seal. A cyan active-item ticket is visibly locked behind a small window, while a heavy honey-gold coin pouch and a few large coins rest at the base. No markings on seal or ticket.
Color palette: ceramic white and sky blue dominant, coral red bands, violet seal, cyan ticket, honey-gold coins; almost no brown.
~~~

### item_flavor_enhance｜彩釉调料瓶组

- 色键：`#ff00ff`
- 源文件：`sources/flavor_enhance_chroma.png`
- 成品：`final/flavor_enhance.png`

~~~text
Primary request: Create one compact "color-glazed seasoning bottle set" restaurant decoration. Show exactly two matching rounded ceramic seasoning bottles on a small cream tray: one turquoise-green bottle with a honey-gold cap and one coral-red bottle with a sky-blue cap. Each bottle has a simple fruit-or-spice relief shape with no writing, and small warm sparkles suggest adding random flavor to two foods.
Color palette: turquoise green and coral red equally dominant, sky blue, ceramic white, honey gold; no violet or magenta.
~~~

### item_flavor_remove_gold｜复古台秤

- 色键：`#ff00ff`
- 源文件：`sources/flavor_remove_gold_chroma.png`
- 成品：`final/flavor_remove_gold.png`

~~~text
Primary request: Create a single "vintage countertop scale" restaurant decoration. Show a rounded teal-green enamel restaurant scale with a ceramic-white dial that has only simple blank tick marks and no digits. Its top brass pan holds one removable coral-red spice charm, while a neat cluster of large honey-gold coins sits in the lower collection tray, clearly suggesting trading away one flavor for money.
Color palette: teal green dominant, ceramic white dial, coral-red charm, honey-gold pan and coins, tiny sky-blue accent; no violet or magenta.
~~~

## 后处理

所有源图均使用 `remove_chroma_key.py --auto-key border --soft-matte --transparent-threshold 12 --opaque-threshold 220 --despill` 去除色键，再统一缩放为 `512×512 RGBA`。绿色主体的两张图使用洋红色键，其余使用绿色键。

为统一安全边距，`perma_flat_all_plus`、`extra_interest`、`first_+2`、`extra_active_slots_max` 的透明成品在 512 画布内额外等比缩至 460 像素并居中，没有改变内容。

全量校验结果：20 张均为 `512×512 RGBA`，四角 alpha 为 0，主体 bbox 未接触画布边缘，未检出残留色键像素；80px 联系表检查均可辨识。

## 调色复核

- 建议重做：`req_feast_down.png`。它是在新增“避免整批偏棕”要求前生成，证书框主体为大面积橙棕木色；内容和轮廓合格，但若严格执行新调色要求，建议改为奶油白或紫罗兰漆面框体，仅保留蜂蜜金边。
- 其余 19 张：主色已在奶油黄、青绿、天蓝、珊瑚红、紫罗兰、陶瓷白、蜂蜜金之间轮换；`loan.png` 的棕色来自明确的木制记录板，属于允许情形。
