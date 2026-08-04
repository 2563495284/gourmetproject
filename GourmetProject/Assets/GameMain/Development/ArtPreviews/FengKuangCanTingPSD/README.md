# 《疯狂餐厅》PSD 切图说明

- 源文件：`/Users/yijin/Downloads/疯狂餐厅.psd`
- 设计画布：1920 × 1080
- Unity Sprite：`Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing`
- 切图数量：50
- Unity 导入：Sprite / Single、100 PPU、Bilinear、Clamp、关闭 Mipmap
- 九宫格：22 个可拉伸控件已写入 Sprite Border

## 目录

- `Backgrounds`：主菜单与经营挑战背景
- `Branding`：风格化游戏标题
- `Controls`：按钮、下拉框、滑条、开关、卡牌槽
- `Icons`：金币、爱心、齿轮、菜单箭头
- `Panels`：弹窗、侧栏、卡片与标题条
- `Timeline`：时间轴、进度线、刻度与日期标记

PSD 中的普通文字没有烘焙进 PNG，建议在 Unity 中使用 TMP 保持可编辑与可本地化；`logo_game_title.png` 是例外，它保留了 PSD 的风格化描边和阴影。

`manifest.json` 记录每个 Sprite 的 PSD 图层路径、源坐标、像素尺寸、1920 × 1080 中心锚点坐标和九宫格 Border。`References` 保存四个界面的完整对照图。
