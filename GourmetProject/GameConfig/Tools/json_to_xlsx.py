#!/usr/bin/env python3
"""把 GameConfig/Datas 下的旧 json 数据表转换为 Luban xlsx 表配置。

用法：
    python3 GameConfig/Tools/json_to_xlsx.py

本项目 Luban 版本要点（已实测）：
- 单元格内 list 多元素无法用 ##type 的 #sep 语法，必须用「流式多单元格」：把 list 放到行末，
  每个元素占一个单元格向右铺开。因此 dish.shapeRows、recipe.pool 放在各自表的最后一列。
- bean 在单元格内的字段分隔靠 schema 中 <bean sep=",">（见 RecipeEntry）。
- 其余 list 字段(inherentTags/startItems)数据均为 0/1 个元素，单格即可。
- recipe.fixedDishes 写成普通字符串，多个菜品用 `|` 分隔，避免与行末 `*pool` 冲突。
- bool 写 true/false；枚举写名字字符串。
依赖：openpyxl（pip install openpyxl）。
"""
import json
import os

try:
    from openpyxl import Workbook
except ImportError:
    raise SystemExit("缺少依赖 openpyxl，请先执行: python3 -m pip install openpyxl")

HERE = os.path.dirname(os.path.abspath(__file__))
DATAS = os.path.normpath(os.path.join(HERE, "..", "Datas"))

# 普通表（无多元素 list）: (字段名, luban类型, kind)。kind: s=标量, b=bool, l1=单元素list(单格)
TABLES = {
    "tag": [
        ("id", "string", "s"), ("name", "string", "s"), ("desc", "string", "s"),
        ("category", "TagCategory", "s"), ("effectType", "TagEffectType", "s"),
        ("effectValue", "float", "s"), ("effectParam", "string", "s"), ("termId", "string", "s"),
    ],
    "term": [("id", "string", "s"), ("name", "string", "s"), ("desc", "string", "s")],
    "character": [
        ("id", "string", "s"), ("name", "string", "s"), ("desc", "string", "s"),
        ("portrait", "string", "s"), ("initialRecipeId", "string", "s"),
        ("initialFragmentId", "string", "s"), ("maxStomachWidth", "int", "s"),
        ("maxStomachHeight", "int", "s"), ("timelinePool", "string", "s"),
        ("bossPool", "string", "s"),
        ("initialGold", "int", "s"),
        ("startItems", "list,string", "l1"),
    ],
    "item": [
        ("id", "string", "s"), ("name", "string", "s"), ("desc", "string", "s"),
        ("kind", "ItemKind", "s"), ("quality", "ItemQuality", "s"), ("specialTags", "string", "s"),
        ("maxLevel", "int", "s"), ("nextLevelWeightMultiplier", "float", "s"),
        ("unlockCondition", "string", "s"), ("triggerTiming", "ItemTriggerTiming", "s"),
        ("acquireLimit", "int", "s"), ("holdLimit", "int", "s"),
        ("baseWeight", "float", "s"), ("hiddenMin", "int", "s"), ("hiddenMax", "int", "s"),
        ("effectType", "string", "s"), ("effectValue", "float", "s"),
        ("effectParam", "string", "s"), ("icon", "string", "s"),
    ],
    "week": [
        ("id", "int", "s"), ("scoreProfileId", "string", "s"), ("rewardPackageId", "string", "s"),
        ("rewardHiddenScore", "int", "s"),
        ("isBoss", "bool", "b"), ("modifier", "string", "s"),
    ],
    "score_profile": [
        ("id", "string", "s"), ("baseScore", "int", "s"), ("difficultyMul", "float", "s"),
        ("bossMul", "float", "s"), ("endlessGrowthMul", "float", "s"), ("roundTo", "int", "s"),
    ],
    "reward_package": [
        ("id", "string", "s"), ("goldMin", "int", "s"), ("goldMax", "int", "s"),
        ("mainSlotGroupId", "string", "s"), ("extraSlotGroupId", "string", "s"),
        ("extraChance", "float", "s"), ("fallbackGold", "int", "s"),
    ],
    "reward_slot": [
        ("id", "string", "s"), ("groupId", "string", "s"), ("kind", "RewardKind", "s"),
        ("choiceCount", "int", "s"), ("weight", "float", "s"), ("poolId", "string", "s"),
        ("hiddenOffset", "int", "s"), ("fallbackGold", "int", "s"),
    ],
    "reward_pool": [
        ("id", "string", "s"), ("kind", "RewardPoolKind", "s"), ("specialTags", "string", "s"),
        ("qualityWeights", "string", "s"), ("allowFallback", "bool", "b"), ("distanceFloor", "int", "s"),
    ],
    "event": [
        ("id", "string", "s"), ("name", "string", "s"), ("desc", "string", "s"),
        ("timeCost", "int", "s"), ("effectType", "string", "s"), ("effectValue", "float", "s"),
        ("category", "string", "s"), ("weight", "float", "s"), ("repeatable", "bool", "b"),
        ("preconditions", "string", "s"),
    ],
    "globalconst": [
        ("id", "string", "s"), ("intValue", "int", "s"), ("floatValue", "float", "s"),
        ("desc", "string", "s"),
    ],
}


def fmt(value, kind):
    if kind == "b":
        return "true" if value else "false"
    if kind == "l1":
        lst = value or []
        if len(lst) > 1:
            raise SystemExit(f"字段含多个元素但被当作单格 list: {lst}（需改为行末流式）")
        return str(lst[0]) if lst else ""
    return "" if value is None else str(value)


def write_simple(ws, fields, rows):
    ws.append(["##var"] + [name for name, _t, _k in fields])
    ws.append(["##type"] + [t for _name, t, _k in fields])
    for row in rows:
        ws.append([""] + [fmt(row.get(name), kind) for name, _t, kind in fields])


def write_dish(ws, rows):
    # shapeRows(list,string) 用多行模式 *shapeRows：每个形状行占一行，续行前导列留空。
    head = ["id", "name", "deliciousness", "hiddenMin", "hiddenMax", "baseWeight",
            "inherentTags", "icon", "allowRotate"]
    types = ["string", "string", "int", "int", "int", "float",
             "list,string", "string", "bool"]
    ws.append(["##var"] + head + ["*shapeRows"])
    ws.append(["##type"] + types + ["list,string"])
    for r in rows:
        tags = r.get("inherentTags") or []
        base = ["", r.get("id", ""), r.get("name", ""), r.get("deliciousness", ""),
                r.get("hiddenMin", ""), r.get("hiddenMax", ""), r.get("baseWeight", ""),
                (tags[0] if tags else ""), r.get("icon", ""),
                "true" if r.get("allowRotate") else "false"]
        shapes = r.get("shapeRows") or [""]
        ws.append(base + [shapes[0]])
        for s in shapes[1:]:
            ws.append([""] * len(base) + [s])


def write_recipe(ws, rows):
    # pool(list,RecipeEntry) 用多行模式 *pool：每个元素占一行，续行前导列留空。
    # 单元格内靠 schema <bean sep=","> 切出 dishId,weight,maxCount,initScore。
    ws.title = "recipe"
    ws.append(["##var", "id", "fixedDishes", "requiredInitScore", "*pool"])
    ws.append(["##type", "string", "string", "int", "list,RecipeEntry"])
    for r in rows:
        fixed = "|".join(r.get("fixedDishes") or [])
        base = ["", r.get("id", ""), fixed, r.get("requiredInitScore", "")]
        pool = r.get("pool") or [{}]
        cells = [f"{e.get('dishId', '')},{e.get('weight', '')},{e.get('maxCount', '')},{e.get('initScore', '')}" for e in pool]
        ws.append(base + [cells[0]])
        for c in cells[1:]:
            ws.append([""] * len(base) + [c])


CUSTOM = {"dish": write_dish, "recipe": write_recipe}


def convert(table_name):
    with open(os.path.join(DATAS, table_name + ".json"), "r", encoding="utf-8") as f:
        rows = json.load(f)

    wb = Workbook()
    ws = wb.active
    ws.title = table_name

    if table_name in CUSTOM:
        CUSTOM[table_name](ws, rows)
    else:
        write_simple(ws, TABLES[table_name], rows)

    wb.save(os.path.join(DATAS, table_name + ".xlsx"))
    print(f"  {table_name}.json -> {table_name}.xlsx ({len(rows)} 行)")


def main():
    print(f"Datas 目录: {DATAS}")
    for name in list(TABLES.keys()) + ["dish", "recipe"]:
        convert(name)
    print("转换完成。请运行 bash GameConfig/gen.sh 验证，再删除旧 json。")


if __name__ == "__main__":
    main()
