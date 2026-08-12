# 食物视觉四风格打样

本目录是独立评审资产，不覆盖 `Assets/GameMain/Content/Resources/Sprites/Dishes` 中的正式游戏图片。

## 内容

- `cel`：厚描边赛璐璐
- `clay`：软陶立体
- `storybook`：暖色绘本半厚涂
- `papercut`：分层纸雕

每套均包含：

- `sources`：图像生成得到的纯色抠图底稿。
- `final`：按格子原生尺寸整理的透明PNG。
- `review_board.png`：带名称、流派和格子示意的评审板；不是正式Sprite。

## 六个固定样本

| baseId | 名称 | 流派 | 格子 | 图片尺寸 |
|---|---|---|---|---|
| `matcha_cake` | 抹茶蛋糕 | 蛋糕流 | 2×2 | 1024×1024 |
| `black_forest_cake` | 黑森林蛋糕 | 蛋糕流 | 3×2 | 1536×1024 |
| `chocolate_truffle` | 松露巧克力 | 甜蜜传递流 | 2×2 | 1024×1024 |
| `chocolate_wafer` | 巧克力威化 | 甜蜜传递流 | 1×3 | 512×1536 |
| `egg_yolk_pastry` | 蛋黄酥 | 东方点心流 | 2×2 | 1024×1024 |
| `double_skin_milk` | 双皮奶 | 东方点心流 | 3×3 | 1536×1536 |

## 使用约定

用户选定画风后，才按该风格制作正式食物与隐藏奶黄包。正式替换时沿用 `baseId.png` 文件名并保留现有 `.meta`，导入设置为透明PNG、100 PPU、双线性过滤、关闭MipMap。
