# 装饰品图标生成提示词：批次 A

本批次覆盖 `tbpassiveitem.json` 顺序第 1–20 项，跳过已批准样图 `item_gold_boss`，共 19 张。

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
Constraints: no words, letters, numbers, logos, watermark, background scene, floor plane, cast shadow, contact shadow, reflection, glow cloud, effect arrow, loose particles, or decorative clutter. Plaques, photos, menus, receipts, and boards may contain only simple wordless pictograms. Keep the whole subject inside the canvas.
```

下列各项仅记录相对于统一骨架的实际主体要求与色键；生成时完整带入统一骨架。

## 逐项提示词

以下每个代码块均与上方统一提示词骨架原样组合后提交；未列出的 Style、Composition 与 Constraints 行均使用统一骨架。

### item_randomize_items｜洗好的扑克

- 色键：#00ff00
- 源文件：sources/randomize_items_chroma.png
- 成品：final/randomize_items.png

~~~text
Primary request: create the restaurant decoration “洗好的扑克” for an item whose gameplay effect replaces all owned items with random items.
Subject: one tight, tidy fan of three freshly cleaned playing cards resting in a small warm-wood tabletop card stand. The cards have rounded corners and only large simple suit-like geometric pictograms—no letters, numbers, words, or tiny markings. Add one small folded cream cleaning cloth tucked behind the stand as the only supporting shape. Make it clearly a real restaurant tabletop decoration, not a magical spell or floating UI symbol.
Color palette: warm cream cards, honey-brown wooden stand, small muted red and blue pictograms; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_gold_random｜陶瓷招财猫

- 色键：#00ff00
- 源文件：sources/gold_random_chroma.png
- 成品：final/gold_random.png

~~~text
Primary request: create the restaurant decoration “陶瓷招财猫” for an item that grants a random amount of coins.
Subject: one friendly seated cream-glazed ceramic beckoning cat made as a countertop ornament, with one raised paw and one round gold medallion on its belly embossed only with a simple covered-dish pictogram. Put the cat on a shallow warm-wood coin tray containing exactly two oversized gold coin discs. The cat and tray form one tight group; no separate floating coins.
Color palette: ivory ceramic, warm honey-gold, orange-brown wood, tiny muted red collar; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_gold_meal_bonus｜柜台小费罐

- 色键：#00ff00
- 源文件：sources/gold_meal_bonus_chroma.png
- 成品：final/gold_meal_bonus.png

~~~text
Primary request: create the restaurant decoration “柜台小费罐” for an item that gives extra coins after ordinary restaurant service.
Subject: one stout opaque cream-glazed ceramic tip jar with a rounded lid and a clear coin slot, placed on a small walnut counter tray. Exactly two oversized gold coins are visibly half-inserted into the slot. Add a simple covered-dish relief on the jar instead of any label. Make the jar feel like a real restaurant counter decoration, not a glass container.
Color palette: cream ceramic, warm gold coins, amber-brown wood, one muted teal rim accent; avoid green and transparent glass.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_gold_percent｜黄铜算盘

- 色键：#00ff00
- 源文件：sources/gold_percent_chroma.png
- 成品：final/gold_percent.png

~~~text
Primary request: create the restaurant decoration “黄铜算盘” for an item that increases restaurant profit.
Subject: one compact countertop abacus with a rounded polished brass frame on a small walnut display base. Use only four thick rods and a few large, widely spaced beads so it remains readable at 80 px; the beads must look physical and neatly grouped, not like a chart or UI. A tiny wordless cloche relief may sit in the base.
Color palette: aged warm brass, honey-brown walnut, muted brick-red and deep blue beads; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_trash_upgrade｜藤编纸篓

- 色键：#00ff00
- 源文件：sources/trash_upgrade_chroma.png
- 成品：final/trash_upgrade.png

~~~text
Primary request: create the restaurant decoration “藤编纸篓” for an item that adds one discard use.
Subject: one small tapered wicker wastebasket with a clearly woven rim and broad simplified basket weave, containing exactly one rolled cream paper and one folded paper corner. Keep the papers mostly inside the basket. It must look clean and suitable beside a restaurant counter.
Color palette: warm honey wicker, cream paper, tiny orange-brown bindings; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_trash_expand｜壁挂废物桶

- 色键：#00ff00
- 源文件：sources/trash_expand_chroma.png
- 成品：final/trash_expand.png

~~~text
Primary request: create the restaurant decoration “壁挂废物桶” for an item that adds one discard use.
Subject: one compact wall-mounted enamel waste bin as a self-contained object: rounded rectangular cream bin, broad muted-blue hinged lid, and an integrated dark-wood mounting backplate with two large brass hooks. Show exactly one folded cream paper peeking from the opening. Do not draw an actual wall.
Color palette: cream enamel, muted blue-gray lid, dark honey wood, brass fittings; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_trash_evolve｜脚踏垃圾桶

- 色键：#00ff00
- 源文件：sources/trash_evolve_chroma.png
- 成品：final/trash_evolve.png

~~~text
Primary request: create the restaurant decoration “脚踏垃圾桶” for an item that adds two discard uses.
Subject: one clean retro cylindrical pedal waste bin for a restaurant, with a domed cream-enamel lid slightly raised, a large obvious brass foot pedal at the front, and a broad warm-metal hinge. The pedal and cylindrical body must form one strong silhouette; no trash outside.
Color palette: cream enamel, warm brass and honey-orange metal accents, muted blue-gray seam; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_trash_mutate｜推盖垃圾桶

- 色键：#00ff00
- 源文件：sources/trash_mutate_chroma.png
- 成品：final/trash_mutate.png

~~~text
Primary request: create the restaurant decoration “推盖垃圾桶” for an item that adds two discard uses.
Subject: one stout retro push-lid restaurant waste bin with a rounded orange-red body and a large dark inset swinging flap on the upper front. Add two broad brass corner caps and a cream rim so it reads as a push bin rather than a cabinet. The flap is closed; no visible trash.
Color palette: warm orange-red enamel, deep brown flap, cream rim, brass corners; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_discount_food｜香草盆栽

- 色键：#ff00ff
- 源文件：sources/discount_food_chroma.png
- 成品：final/discount_food.png

~~~text
Primary request: create the restaurant decoration “香草盆栽” for an item that discounts food purchases.
Subject: one healthy countertop herb planter in a rounded terracotta pot, with a compact bouquet of broad basil leaves and two rosemary-like sprigs. Add a small cream ceramic band on the pot embossed with one simple covered-dish pictogram. Keep the plant lush but simplified into a few large leaf masses readable at 80 px.
Color palette: fresh basil and rosemary greens, warm terracotta orange, cream ceramic band, honey highlights.
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background for local removal; do not use #ff00ff anywhere in the subject.
~~~

### item_discount_fragment｜餐桌模型

- 色键：#00ff00
- 源文件：sources/discount_fragment_chroma.png
- 成品：final/discount_fragment.png

~~~text
Primary request: create the restaurant decoration “餐桌模型” for an item that discounts dining-table-grid purchases.
Subject: one miniature square restaurant dining table displayed on a low walnut presentation plinth. The model has a cream tabletop, four chunky short legs, a neatly folded brick-red table runner, and exactly two simple gold plates. Make the miniature scale obvious through the plinth; no surrounding room.
Color palette: cream tabletop, honey-brown walnut, brick-red runner, warm gold plates; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_discount_passive｜装饰样板墙

- 色键：#00ff00
- 源文件：sources/discount_passive_chroma.png
- 成品：final/discount_passive.png

~~~text
Primary request: create the restaurant decoration “装饰样板墙” for an item that discounts decoration purchases.
Subject: one portable freestanding restaurant decor sample rack shaped like a small wall panel on a broad wooden base. It holds exactly three large sample pieces arranged as one ceramic tile, one small empty picture frame, and one round decorative plate. Keep the samples bold and widely spaced, like a showroom display, not an actual room wall.
Color palette: warm cream panel, honey-brown frame and base, muted blue tile, brick-red small frame, gold plate; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_discount_remove｜抽页食谱夹

- 色键：#00ff00
- 源文件：sources/discount_remove_chroma.png
- 成品：final/discount_remove.png

~~~text
Primary request: create the restaurant decoration “抽页食谱夹” for an item that discounts food-removal service.
Subject: one sturdy ring-bound restaurant menu binder propped open on a small walnut stand, with one thick cream page visibly sliding halfway out from the rings. The page shows only one large wordless crossed-out plate pictogram made from simple shapes; no writing. Keep binder, page, and stand as one compact object.
Color palette: warm cream paper, brick-red leather binder, honey-brown stand, muted dark-blue pictogram; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_discount_food_festival｜丰收菜篮

- 色键：#ff00ff
- 源文件：sources/discount_food_festival_chroma.png
- 成品：final/discount_food_festival.png

~~~text
Primary request: create the restaurant decoration “丰收菜篮” for a stronger item that heavily discounts food purchases.
Subject: one generous oval wicker harvest basket as a restaurant counter centerpiece, packed into one tight mound with exactly four large produce forms: a red apple, an orange, a golden pear, and a purple eggplant, plus a few broad green leaves. Add a cream cloth lining folded over the rim. Keep each produce shape large and instantly distinct at 80 px.
Color palette: warm honey wicker, cream cloth, vivid red, orange, gold, purple, and fresh leaf greens.
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background for local removal; do not use #ff00ff anywhere in the subject.
~~~

### item_discount_fragment_festival｜宴会桌模型

- 色键：#00ff00
- 源文件：sources/discount_fragment_festival_chroma.png
- 成品：final/discount_fragment_festival.png

~~~text
Primary request: create the restaurant decoration “宴会桌模型” for a stronger item that heavily discounts dining-table-grid purchases.
Subject: one miniature long banquet table on a broad walnut presentation plinth. Use a cream tablecloth, a brick-red runner, exactly four large gold plates, and one central covered serving cloche. The table legs are chunky and clearly miniature; everything forms one tight display object, not a room scene.
Color palette: cream cloth, honey-brown walnut, brick red, warm gold; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_discount_passive_festival｜多层展柜

- 色键：#00ff00
- 源文件：sources/discount_passive_festival_chroma.png
- 成品：final/discount_passive_festival.png

~~~text
Primary request: create the restaurant decoration “多层展柜” for a stronger item that heavily discounts decoration purchases.
Subject: one compact three-tier restaurant display cabinet with an arched warm-wood frame and open shelves, presented as a single freestanding ornament. Place exactly one large object on each tier: a cream vase, a decorative blue plate, and a small gold covered serving cloche. Make the cabinet broad and readable rather than tall and detailed.
Color palette: honey-brown wood, cream ceramic, muted blue, warm gold, tiny brick-red trim; avoid green.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_discount_remove_festival｜可擦食谱板

- 色键：#ff00ff
- 源文件：sources/discount_remove_festival_chroma.png
- 成品：final/discount_remove_festival.png

~~~text
Primary request: create the restaurant decoration “可擦食谱板” for a stronger item that heavily discounts food-removal service.
Subject: one sturdy freestanding restaurant chalkboard menu on a warm-wood base, with a dark forest-green slate surface and a broad cream eraser clipped across its lower edge. On the board show only three large simple wordless food pictograms, with the rightmost pictogram visibly half-wiped into one clean empty patch. No chalk writing or lines.
Color palette: dark forest-green slate, honey-brown frame, cream eraser, muted gold and pale-blue pictograms.
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background for local removal; do not use #ff00ff anywhere in the subject.
~~~

### item_score_to_one｜开业纪念照

- 色键：#00ff00
- 源文件：sources/score_to_one_chroma.png
- 成品：final/score_to_one.png

~~~text
Primary request: create the restaurant decoration “开业纪念照” for an item that makes the next two service score requirements equal to one.
Subject: one freestanding rounded ceramic photo frame as a restaurant shelf ornament. The frame is violet with a cream inner rim. Inside is one simplified opening-day snapshot: a coral-red counter with a large white covered serving cloche, a sky-blue semicircle awning above it, and exactly two small honey-gold balloon circles at the sides. No people and no writing. Use a small white ceramic easel back integrated into the frame instead of a wooden stand.
Color palette: violet frame, ceramic white, sky blue, coral red, honey-gold accents; only the outline may be dark brown.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

### item_req_normal_down｜家常菜挂画

- 色键：#ff00ff
- 源文件：sources/req_normal_down_chroma.png
- 成品：final/req_normal_down.png

~~~text
Primary request: create the restaurant decoration “家常菜挂画” for an item that lowers ordinary-service score requirements.
Subject: one decorative wall picture as a self-contained object with a rounded sky-blue glazed ceramic frame and two small cream hanging loops at the top. Inside, show one large white bowl filled with coral-red home-style stew, one golden spoon, and exactly two broad green herb leaves. The picture is simple and cozy, with no actual wall behind it.
Color palette: sky blue, ceramic white, coral red, fresh green, honey gold; only the outline may be dark brown.
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background for local removal; do not use #ff00ff anywhere in the subject.
~~~

### item_req_super_down｜红格桌布

- 色键：#00ff00
- 源文件：sources/req_super_down_chroma.png
- 成品：final/req_super_down.png

~~~text
Primary request: create the restaurant decoration “红格桌布” for an item that lowers hot-service score requirements.
Subject: one thick coral-red and cream gingham restaurant tablecloth, neatly folded and draped over a compact violet enameled display rail fixed to a low ceramic-white base with two sky-blue end caps. Use a broad checker pattern with only a few large squares so it remains legible at 80 px. Make the cloth the clear dominant subject and keep the rail visually secondary.
Color palette: coral red, cream, violet enamel, sky-blue end caps, tiny honey-gold fasteners; only the outline may be dark brown.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal; do not use #00ff00 anywhere in the subject.
~~~

## 生成后检查

- 19/19 张均为 512×512 RGBA PNG。
- 四角 alpha 均为 0。
- 主体包围盒全部留有安全边距；96px 接触表见 previews/batch_a_96px.jpg。
- 绿色主体使用洋红色键：discount_food、discount_food_festival、discount_remove_festival、req_normal_down。
- 其余使用绿色键。

### 配色审计

新增“不要整批偏棕”的要求后，最后三张已改用紫罗兰、天蓝、珊瑚红、陶瓷白和青绿轮换。

下列早期成品在接触表中仍呈现明显的棕/金棕主色，建议后续优先换色重做：

- final/gold_percent.png
- final/discount_fragment.png
- final/discount_fragment_festival.png
- final/discount_passive_festival.png

其中后三张包含明确木制结构，题材上合理，但若以整批色彩均衡为准，仍是最值得调整的文件。
