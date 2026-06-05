# Grotesque Cartoon URP 2D 渲染管线说明

## 目的

这套渲染管线用于把 Unity URP 2D 的最终画面统一处理成偏 **90 年代美式怪诞卡通** 的视觉效果：更重的轮廓感、更扁平的色块、更暖的整体色调，以及类似老电视动画的暗角氛围。

它不是 AI 生图提示词，而是真正挂在 URP 2D Renderer 上的全屏后处理管线。

## 接入位置

核心文件：

- `Assets/GameMain/Scripts/Runtime/Rendering/GrotesqueCartoonRenderFeature.cs`
- `Assets/GameMain/Shaders/GrotesqueCartoonPostProcess.shader`
- `Assets/GameMain/Materials/GrotesqueCartoonPostProcess.mat`
- `Assets/Settings/Renderer2D.asset`

`Renderer2D.asset` 中已经注册了：

- `Grotesque Cartoon Render Feature`

因此使用这个 2D Renderer 的相机会自动套用该后处理效果。

## 效果组成

### 1. 饱和度增强

参数：`saturation`

默认值：`1.18`

作用：让画面颜色更鲜明，接近复古美式动画里偏夸张、偏浓烈的卡通色彩。

数值建议：

- `1.0`：不增强
- `1.1 - 1.25`：推荐范围
- `1.4+`：可能过艳，容易脏或刺眼

---

### 2. 对比度增强

参数：`contrast`

默认值：`1.08`

作用：增加明暗反差，让角色、背景和 UI 的色块边界更清楚。

数值建议：

- `1.0`：不增强
- `1.05 - 1.15`：推荐范围
- `1.25+`：暗部可能丢细节

---

### 3. 色阶化 / 赛璐璐压色

参数：`posterizeSteps`

默认值：`18`

作用：减少连续渐变，把画面压成更接近赛璐璐动画的分层色块。

数值越低，色块越明显；数值越高，越接近原图。

数值建议：

- `10 - 14`：强烈卡通压色
- `16 - 24`：推荐范围
- `32+`：效果较轻

---

### 4. 边缘 / 暗线增强

参数：`inkStrength`

默认值：`0.28`

作用：通过屏幕颜色差异检测边缘，把边缘和高反差区域压暗，模拟粗黑描边、手绘墨线和怪诞卡通的线稿感。

这不是几何描边，不依赖模型法线或深度，因此适合 2D 背景图、Sprite 和 UI 合成画面。

数值建议：

- `0.0`：关闭边缘增强
- `0.18 - 0.35`：推荐范围
- `0.5+`：线条可能太脏、太重

---

### 5. 暖色调

参数：`warmTint`

默认值：`(1.05, 0.96, 0.86, 1)`

作用：给最终画面叠加轻微暖色，让餐厅、厨房、食物主题更有“开胃”和复古动画感。

如果后续场景不是餐厅/食物主题，可以把它调回接近白色：

```text
(1.0, 1.0, 1.0, 1.0)
```

---

### 6. 暗角

参数：`vignetteStrength`

默认值：`0.18`

作用：让画面边缘稍微变暗，把注意力集中到角色和主视觉区域，同时增加一点老电视动画帧的氛围。

数值建议：

- `0.0`：关闭暗角
- `0.1 - 0.22`：推荐范围
- `0.3+`：边缘可能过暗

## 当前默认风格倾向

当前默认参数偏向：

- 温暖
- 可爱但怪诞
- 轻微黑色幽默
- 2D 美式卡通
- 边缘更重
- 色块更扁平

适合：

- 主菜单背景
- 2D 卡通角色
- 餐厅/厨房/食物主题场景
- 怪诞幽默类 UI

不太适合：

- 写实场景
- 高级渐变质感 UI
- 需要非常干净透明色的纯图标
- 需要准确还原原图颜色的场景

## 技术实现方式

该管线是一个 URP `ScriptableRendererFeature`：

```text
Renderer2D.asset
  -> Grotesque Cartoon Render Feature
      -> GrotesqueCartoonPostProcess.mat
          -> GrotesqueCartoonPostProcess.shader
```

渲染流程：

1. URP 2D 正常渲染场景
2. 在 `AfterRenderingPostProcessing` 阶段执行自定义全屏 Pass
3. Shader 读取当前相机颜色纹理 `_BlitTexture`
4. 对整张画面做卡通化处理
5. 把处理后的结果写回 `cameraColor`

实现兼容 Unity 6000 / URP 17 的 RenderGraph 路径，没有使用旧版 `Execute()` 后处理方式。

## 注意事项

### Screen Space Overlay UI

如果 UI Canvas 是 `Screen Space Overlay`，某些相机后处理可能不会影响 Overlay UI。当前主菜单背景是 UI Image，如果你发现按钮或 Overlay UI 没被处理，这是 Unity 渲染顺序导致的。

如果希望 UI 也完全吃到这套效果，可以考虑：

- 把 Canvas 改成 `Screen Space Camera`
- 或把需要处理的画面放到相机渲染层
- 或保留 UI 不处理，只处理背景和游戏画面

### 边缘增强不是几何描边

`inkStrength` 是基于屏幕颜色差的边缘检测，不是基于 Sprite 外轮廓的真实描边。

优点：

- 对 2D 背景图有效
- 不需要 depth / normal
- 对 UI 和 Sprite 都比较通用

缺点：

- 复杂纹理里也可能产生暗线
- 不能像专用 Sprite Outline 那样只描角色外轮廓

### 如果画面太脏

优先调低：

```text
inkStrength: 0.28 -> 0.18
posterizeSteps: 18 -> 24
vignetteStrength: 0.18 -> 0.10
```

### 如果画面不够卡通

优先调高：

```text
saturation: 1.18 -> 1.25
contrast: 1.08 -> 1.15
posterizeSteps: 18 -> 12
inkStrength: 0.28 -> 0.35
```

## 推荐调参预设

### 柔和菜单风格

```text
saturation: 1.12
contrast: 1.05
posterizeSteps: 24
inkStrength: 0.18
vignetteStrength: 0.10
warmTint: (1.03, 0.98, 0.92, 1)
```

### 强烈怪诞卡通风格

```text
saturation: 1.28
contrast: 1.16
posterizeSteps: 12
inkStrength: 0.40
vignetteStrength: 0.22
warmTint: (1.08, 0.94, 0.82, 1)
```

### 接近原图但统一色调

```text
saturation: 1.05
contrast: 1.03
posterizeSteps: 32
inkStrength: 0.08
vignetteStrength: 0.05
warmTint: (1.0, 1.0, 1.0, 1)
```
