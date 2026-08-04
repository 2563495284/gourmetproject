# 装饰品图标批次 D 提示词

生成方式：内置 `image_gen`，每个装饰品单独生成一次。

风格参考（仅参考画风，不复制主体）：

- `samples/gold_boss_sample_v2.png`
- `samples/req_super_up_sample_v2.png`
- `Assets/GameMain/Content/Resources/Sprites/Items/active_season_sour.png`
- `Art/GeneratedActionCards/final/card_action_food_normal_passive.png`

## 共用提示词

```text
Use case: stylized-concept
Asset type: 512×512 game UI icon for a restaurant decoration
Input images: all input images are style references only. Match their exact visual family, not their subjects.
Primary request: create “{name}”, a tangible restaurant decoration matching this gameplay meaning: {effect}.
Subject: {subject}
Style/medium: exact same clean 2D hand-painted cartoon style as the approved decoration samples, action tabs, and consumable icons; warm dark-brown outlines of moderate weight; simplified rounded geometry; two or three broad cel-shaded value steps; restrained highlights; subtle paper/brush texture; warm restaurant-world materials.
Composition/framing: one centered compact object or tightly unified object group in slight top-down three-quarter view, about 70% of the canvas, generous even padding, strong silhouette readable at 70–80 px.
Scene/backdrop: perfectly flat solid {key_color} chroma-key background for local removal.
Constraints: uniform background; do not use the key color in the subject; no cast shadow, floor, gradient, glow, reflection, text, letters, numbers, logo, watermark, people, or floating UI symbols.
Avoid: glossy 3D render, photorealism, industrial machinery, micro-details, thick pure-black sticker outline.
```

## 逐项变量

| id | name | effect | subject | key_color |
|---|---|---|---|---|
| item_no_remove | 上锁食品柜 | 无法再删除食物 | One compact vintage wooden pantry cabinet with rounded corners, two panel doors held shut by a large brass padlock, and two muted cream food jars visible behind a tiny upper window. | #00ff00 |
| item_skip_node | 落灰打卡钟 | 跳过下一个收取利息节点 | One dusty vintage restaurant punch clock with a blank time card partly inserted, stopped brass hands, rounded walnut case, and only a tiny cobweb accent. | #00ff00 |
| item_skip_reward_node | 褪色招财猫 | 跳过下一个幸运事件节点 | One faded and slightly chipped ceramic beckoning cat, lowered waving paw, washed-out cream and muted red paint, sitting on a small worn wooden plinth. | #00ff00 |
| item_choice_minus1 | 缺角食谱板 | 多选一食物时可选数量减一 | One small blank wooden menu chalkboard with a visibly broken upper corner, a single empty cream card clipped to it, and no writing or symbols. | #00ff00 |
| item_req_normal_up | 挑剔留言簿 | 日常营业目标美味值提高 | One open guest-comment ledger with sharp red proofreading strokes that are abstract marks rather than letters, a dark red ribbon, and a stern brass pen laid across it. | #00ff00 |
| item_req_super_up | 爆单警示灯 | 火热营业目标美味值提高 | Reuse the approved V2 sample: matte red warning dome on a warm wooden restaurant plinth with brass corners and two blank order slips. | transparent approved sample |
| item_req_feast_up | 评鉴整改书 | 星级评鉴目标美味值提高 | One formal red-bound restaurant correction book with cream page edges, brass corners, a blank embossed cover frame, and one plain red seal without text. | #00ff00 |
| item_gold_week_clear | 逾期账单夹 | 周末失去所有金币 | One worn dark-red bill clip holding several blank overdue invoices, with a small empty coin tray turned upside down beside the clip; no currency symbols. | #00ff00 |
| item_double_daily_cost_repeat_node | 双栏排班板 | 普通行动耗时加倍且节点可执行两次 | One warm wooden wall schedule board divided into two clear columns, each holding matching pairs of blank cream shift cards and two small brass clock pegs; no writing. | #00ff00 |
| item_timeline_stop_chance | 停摆挂钟 | 节点行动有概率停止时间轴 | One vintage restaurant wall clock in a rounded walnut frame, both brass hands visibly stopped against a blank cream face with simple unlabeled tick marks and a tiny jammed gear at the bottom. | #00ff00 |
| item_count_as_all | 九格拼盘 | 所有食物额外视为两个食物 | One square ceramic tasting platter divided into a clear three-by-three grid, each compartment holding one simple colorful dessert bite, tightly unified as one object. | #00ff00 |
| item_transfer_target_mult | 大号糖果盘 | 甜蜜传递时被传递方倍率提高 | One oversized shallow ceramic candy dish heaped with a few large wrapped candies and round sweets in warm red, amber, cream, and teal, with no green candy. | #00ff00 |
| item_transfer_source_mult | 长嘴糖浆壶 | 甜蜜传递时传递方倍率提高 | One charming long-spout ceramic syrup pitcher with amber syrup visible at the lip, cream glaze, warm orange bands, and a curved handle. | #00ff00 |
| item_skill_count_mult | 主厨刀架 | 技能越多结算倍率越高 | One compact walnut chef's knife rack holding three distinct rounded kitchen knives and one small sharpening steel, with brass feet and no loose blades. | #00ff00 |
| item_gold_on_transfer | 黄铜传菜铃 | 累计甜蜜传递后获得金币 | One polished-but-matte brass restaurant service bell on a low warm wooden base, with one small cream serving ticket tucked beneath it; no coin symbols. | #00ff00 |
| item_cake_retain | 玻璃蛋糕罩 | 保留部分蛋糕层数 | One pale-blue painted glass cake cloche with a strong dark-brown outline, covering a compact layered cake; depict the glass as opaque stylized cel-shaded material, not realistic transparency. | #00ff00 |
| item_cake_init_bonus | 厚底蛋糕托 | 蛋糕初始层数增加 | One thick sturdy ceramic pedestal cake stand with an exaggerated heavy base, supporting a small three-layer cream-and-berry cake. | #00ff00 |
| item_cake_accel | 可叠蛋糕托 | 蛋糕层数增长额外增加 | A tightly unified pair of stackable warm ceramic cake stands nested into two rising tiers, each holding one simple small pastry. | #00ff00 |
| item_cake_to_gold | 蛋糕价签架 | 蛋糕层数达到条件后获得金币 | One small brass bakery price-card holder beside a cake slice on a cream saucer, with a completely blank card and a tiny gold bead accent, no writing or currency symbol. | #00ff00 |
| item_cake_req_minus | 迷你蛋糕模 | 蛋糕效果需求层数降低 | One tiny fluted round cake mold beside a miniature sponge cake, compactly arranged on a warm wooden coaster, emphasizing its small scale without rulers or numbers. | #00ff00 |
