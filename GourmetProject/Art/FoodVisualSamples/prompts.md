# 图像生成提示词记录

生成方式：内置图像生成；每张图独立生成。为获得稳定透明边缘，先生成纯色抠图背景，再移除色键并按原比例放入格子原生尺寸画布。

## 共用约束

```text
Use case: stylized-concept. Create one production-ready game food sprite of a real, Chinese-recognizable dessert. Object only, no scene. One coherent food object, not repeated copies. Natural proportions; no stretching. Leave 10-12% clear padding. Slight elevated three-quarter view when useful.

Hard exclusions: no people, no face, no eyes, no mouth, no limbs, no mascot, no text, no letters, no numbers, no logo, no watermark, no border, no grid, no cast shadow, no contact shadow, no floor plane, no repeated identical food pieces.
```

抹茶蛋糕使用纯色品红 `#FF00FF`，其余食物使用纯色绿 `#00FF00`；要求背景无渐变、纹理或光照变化，主体不得使用对应色键颜色。

## 四种画风

### 厚描边赛璐璐

```text
Polished thick-outline cel-shaded 2D game art, confident dark cocoa-brown outer contour, clean 2-3 tone color blocks, saturated but appetizing, cozy dessert strategy game quality.
```

### 软陶立体

```text
Charming handcrafted soft-clay 3D game asset, rounded sculpted forms, subtle fingerprint and tool textures, matte polymer-clay surfaces, softly beveled edges, warm diffuse light contained on the object, no drawn outline.
```

### 暖色绘本半厚涂

```text
Warm children's picture-book semi-painted food illustration, softly simplified realistic forms, visible gouache and dry-brush texture inside the object, warm cream highlights, muted terracotta shadows, selective soft colored contour rather than black line.
```

### 分层纸雕

```text
Premium layered paper-cut diorama game asset, clearly stacked colored-paper layers, crisp hand-cut edges, subtle paper fibers, tiny internal occlusion between paper layers only, clean simplified shapes, warm dimensional craft aesthetic.
```

## 六个食物设计段

### 抹茶蛋糕

```text
Exactly one low round matcha layer cake with visible green sponge and ivory cream layers, clean cut face, restrained piped cream and one small red-bean cluster, on an ivory scalloped cake board. Immediate cake silhouette. Centered near-square 2x2 footprint.
```

### 黑森林蛋糕

```text
Exactly one horizontally wide rectangular Black Forest layer cake on a dark understated cake board, cocoa sponge layers, ivory whipped cream bands, dark chocolate curls and irregularly placed red cherries. One whole dessert, not slices. Centered wide 3:2 silhouette for a 3x2 footprint.
```

### 松露巧克力

```text
Exactly one oversized round cocoa-dusted chocolate truffle in a short folded golden candy-foil cradle. One integrated broken cut face reveals dark ganache; still one candy. Strong round silhouette, no plate. Centered near-square 2x2 footprint.
```

### 巧克力威化

```text
Exactly one very long narrow vertical chocolate-coated wafer bar, upper end diagonally broken to reveal alternating wafer and cream layers. An unbranded red-and-gold candy wrapper gathers only around the bottom fifth. One continuous bar. Extremely tall slender 1:3 footprint crossing all three cells.
```

### 蛋黄酥

```text
Exactly one compact round golden flaky pastry on a small pale-blue celadon saucer. A natural cracked opening reveals dark red-bean paste around a bright orange salted egg-yolk center; a few black sesame seeds. Not mooncake, not bun. Centered near-square 2x2 footprint.
```

### 双皮奶

```text
One square-friendly deep pale-blue celadon bowl containing ivory double-skin milk pudding, with delicate caramel-beige wrinkled milk skin and a restrained off-center red-bean cluster. Clearly creamy pudding, not soup or yogurt. Broad 3x3 footprint with full rim visible.
```
