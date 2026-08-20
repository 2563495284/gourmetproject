# 食物范围 GM

双击 `docs/tools/打开范围GM.command` 即可启动，默认地址为：

`http://127.0.0.1:8766/`

页面只读，不会修改策划配置。点击右上角“刷新正式配置”会重新读取：

- `Assets/StreamingAssets/Config/tbdishbase.json`
- `Assets/StreamingAssets/Config/tbskill.json`
- `Assets/StreamingAssets/Config/tbsubskill.json`

## 调试方式

- 左侧可搜索全部食物，并筛选有范围、无范围或配置异常。
- 中间可切换棋盘尺寸、移动来源食物、布置普通/蛋糕/甜蜜传递示例目标。
- 蓝色区域是游戏最终会绘制的唯一范围，绿色框是当前局面下命中的食物目标；分类目标（如蛋糕卷随机选择两个蛋糕）不显示合并范围，每个命中食物分别显示自身占格轮廓，相邻目标也不会合并。
- 分类目标的原生技能轮廓为黄色；同一技能由甜蜜传递执行时，游戏中改用粉色轮廓。
- 右侧可逐条切换子技能，并单独查看最终范围、原始条件范围和原始作用范围。
- “全量范围审计”会列出正式配置中所有子技能的范围类型、隐藏原因和冲突状态。

命令行启动方式：

```bash
python3 docs/tools/gm-scope-debugger-server.py
```

停止服务时，在启动窗口按 `Ctrl+C`。
