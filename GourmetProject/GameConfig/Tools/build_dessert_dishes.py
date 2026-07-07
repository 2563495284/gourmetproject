#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""重建 dish.xlsx 的 dish_base / dish_variant / skill / skill_rule 四表为甜品阵容，
并重写 recipe.xlsx 的菜谱池引用。技能规则按阶段逐步补齐（本脚本含 P1~P5 已支持的规则）。
形状按设计表 shapeRows 原样，禁旋转。"""
import openpyxl

DISH_XLSX = 'GameConfig/Datas/dish.xlsx'
RECIPE_XLSX = 'GameConfig/Datas/recipe.xlsx'

# ---------------------------------------------------------------------------
# 菜品数据：id, 名称, 美味度, 形状行, 分类, 视为食物数, 技能id列表, 阶段
# 阶段用于变体隐藏分/权重取值。
DISHES = [
    ("cookie",       "曲奇",     10, ["X"],                                     "",     1, ["sk_cookie"], "e"),
    ("macaron",      "马卡龙",   8,  ["X"],                                     "",     1, ["sk_macaron", "sk_tf_row_1"], "e"),
    ("pudding",      "布丁",     30, ["X"],                                     "",     1, ["sk_pudding"], "e"),
    ("puff",         "泡芙",     8,  ["X"],                                     "",     1, ["sk_puff"], "e"),
    ("eggtart",      "蛋挞",     6,  ["X"],                                     "",     1, ["sk_eggtart"], "e"),
    ("mochi",        "麻薯",     10, ["X"],                                     "",     1, ["sk_mochi"], "em"),
    ("daifuku",      "大福",     6,  ["X"],                                     "",     1, ["sk_daifuku"], "e"),
    ("choco_candy",  "巧克力糖", 10, ["X"],                                     "",     1, ["sk_choco_candy"], "em"),
    ("jelly",        "果冻",     4,  ["X"],                                     "",     1, ["sk_jelly"], "e"),
    ("cream_cell",   "奶油小格", 10, ["X"],                                     "cake", 1, ["sk_cream_cell"], "e"),
    ("sugarbean",    "糖豆",     10, ["X"],                                     "",     1, ["sk_sugarbean", "sk_tf_any_1"], "emh"),
    ("gold_choco",   "金巧克力", 6,  ["X"],                                     "",     1, ["sk_gold_choco"], "em"),
    ("milk_slice",   "奶片",     8,  ["X"],                                     "cake", 1, ["sk_milk_slice"], "em"),
    ("sundae",       "圣代",     20, ["XX"],                                    "",     2, ["sk_sundae"], "m"),
    ("cone",         "甜筒",     20, ["X", "X"],                                "cake", 1, ["sk_cone_layer", "sk_cone_copy"], "m"),
    ("lollipop",     "棒棒糖",   10, ["XX"],                                    "",     1, ["sk_lollipop", "sk_tf_col_2"], "m"),
    ("gummy",        "软糖",     8,  ["XX"],                                    "",     1, ["sk_gummy", "sk_tf_adj_2"], "m"),
    ("cupcake",      "杯子蛋糕", 14, ["XX"],                                    "cake", 1, ["sk_cupcake_copy", "sk_cupcake_layer"], "mh"),
    ("mousse",       "慕斯蛋糕", 16, ["X", "X"],                                "cake", 1, ["sk_mousse"], "mh"),
    ("toffee",       "太妃糖",   15, ["X", "X"],                                "",     1, ["sk_toffee", "sk_tf_any_2"], "m"),
    ("nougat",       "牛轧糖",   20, ["X", "X"],                                "",     1, ["sk_nougat", "sk_tf_adj_2"], "m"),
    ("eggroll",      "蛋卷",     30, ["XXX"],                                   "",     4, ["sk_eggroll"], "m"),
    ("choco_bar",    "巧克力棒", 40, ["XXX"],                                   "",     1, ["sk_choco_bar", "sk_tf_any_3"], "m"),
    ("eclair",       "闪电泡芙", 30, ["X", "X", "X"],                           "cake", 1, ["sk_eclair"], "m"),
    ("tanghulu",     "糖葫芦",   20, ["X", "X", "X"],                           "",     1, ["sk_tanghulu", "sk_tf_row_3"], "m"),
    ("souffle",      "舒芙蕾",   30, ["XX", "XX"],                              "cake", 1, ["sk_souffle"], "m"),
    ("taosu",        "桃酥",     20, ["XX", "XX"],                              "",     1, ["sk_taosu"], "mh"),
    ("mooncake",     "月饼",     25, ["XX", "XX"],                              "",     5, ["sk_mooncake_copy"], "mh"),
    ("waffle",       "华夫饼",   20, ["XX", "XX"],                              "",     1, ["sk_waffle_mult", "sk_waffle_countas"], "mh"),
    ("dorayaki",     "铜锣烧",   20, ["XX", "XX"],                              "",     1, ["sk_dorayaki_countas", "sk_dorayaki_mult"], "mh"),
    ("cream_cake",   "奶油蛋糕", 50, ["XX", "XX"],                              "cake", 1, ["sk_cream_cake_perm", "sk_cream_cake_spread"], "mh"),
    ("donut",        "甜甜圈",   40, ["XX", "XX"],                              "cake", 1, ["sk_donut_layer", "sk_donut_mult"], "mh"),
    ("pengtang",     "椪糖",     30, ["XX", "XX"],                              "",     1, ["sk_pengtang", "sk_tf_col_all"], "m"),
    ("parfait",      "芭菲",     50, ["XXX", "XXX"],                            "",     10, ["sk_parfait"], "h"),
    ("sundae_big",   "圣代",     40, ["XX", "XX", "XX"],                        "",     5, ["sk_sundae_big"], "h"),
    ("swiss_roll",   "瑞士卷",   30, ["XX", "XX", "XX"],                        "cake", 1, ["sk_swiss_layer", "sk_swiss_mult"], "m"),
    ("mango_sago",   "杨枝甘露", 20, ["XXX", "XXX"],                            "",     8, ["sk_mango_copy"], "mh"),
    ("chiffon",      "戚风蛋糕", 30, ["XXX", "XXX"],                            "cake", 1, ["sk_chiffon_mult", "sk_chiffon_spread"], "mh"),
    ("apple_pie",    "苹果派",   50, ["XX", "XX", "XX"],                        "",     1, ["sk_apple_pie"], "m"),
    ("fruit_cake",   "水果蛋糕", 40, ["XX", "XX", "XX"],                        "cake", 1, ["sk_fruit_mult", "sk_fruit_layer"], "m"),
    ("chocolate",    "巧克力",   30, ["XXX", "XXX"],                            "",     1, ["sk_chocolate"], "mh"),
    ("croissant",    "牛角包",   20, ["XX", "X."],                             "cake", 1, ["sk_croissant"], "m"),
    ("palmier",      "蝴蝶酥",   30, [".X", "XX"],                             "",     4, ["sk_palmier"], "m"),
    ("candycane",    "拐杖糖",   40, ["XX", "X.", "X."],                       "",     1, ["sk_candycane", "sk_tf_adj_all"], "mh"),
    ("big_lollipop", "大棒棒糖", 100,["XXX", "XXX", "XXX", ".X.", ".X."],      "",     1, ["sk_big_lollipop"], "h"),
    ("marshmallow",  "棉花糖",   80, ["XXX", "XXX", "XXX"],                     "",     1, ["sk_marshmallow", "sk_tf_row_all"], "h"),
    ("double_cake",  "双层蛋糕", 140,[".XXX.", ".XXX.", "XXXXX", "XXXXX"],     "cake", 1, ["sk_double_cake"], "h"),
]

# ---------------------------------------------------------------------------
# 技能：id -> (名称, 描述, termId, [规则])
# 规则元组: (order, trigger, condType, condScope, condUnit, condMode, condCompare,
#            condThreshold, condParam, actionType, actionScope, actionCount,
#            actionValue, actionParam)
TERM_TRANSFER = "term_sweet_transfer"
def r(order, trigger, ctype, cscope, cunit, cmode, ccmp, cthr, cparam,
      atype, ascope, acount, aval, aparam):
    return (order, trigger, ctype, cscope, cunit, cmode, ccmp, cthr, cparam,
            atype, ascope, acount, aval, aparam)

# 通用甜蜜传递技能（OnServe，把自身非传递技能给目标）
def tf(scope, count):
    return [r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "",
              "TransferSkills", scope, count, 0, "")]

SKILLS = {
    # ---- 甜蜜传递（共用） ----
    "sk_tf_row_1":   ("甜蜜传递·同行1", "上菜时，将本菜技能甜蜜传递给同行 1 个食物。", TERM_TRANSFER, tf("Row", 1)),
    "sk_tf_row_3":   ("甜蜜传递·同行3", "上菜时，将本菜技能甜蜜传递给同行 3 个食物。", TERM_TRANSFER, tf("Row", 3)),
    "sk_tf_row_all": ("甜蜜传递·同行",  "上菜时，将本菜技能甜蜜传递给同行所有食物。", TERM_TRANSFER, tf("Row", 0)),
    "sk_tf_col_2":   ("甜蜜传递·同列2", "上菜时，将本菜技能甜蜜传递给同列 2 个食物。", TERM_TRANSFER, tf("Column", 2)),
    "sk_tf_col_all": ("甜蜜传递·同列",  "上菜时，将本菜技能甜蜜传递给同列所有食物。", TERM_TRANSFER, tf("Column", 0)),
    "sk_tf_adj_2":   ("甜蜜传递·周围2", "上菜时，将本菜技能甜蜜传递给周围 2 个食物。", TERM_TRANSFER, tf("Adjacent", 2)),
    "sk_tf_adj_all": ("甜蜜传递·周围",  "上菜时，将本菜技能甜蜜传递给周围所有食物。", TERM_TRANSFER, tf("Adjacent", 0)),
    "sk_tf_any_1":   ("甜蜜传递·1",     "上菜时，将本菜技能甜蜜传递给 1 个食物。",     TERM_TRANSFER, tf("All", 1)),
    "sk_tf_any_2":   ("甜蜜传递·2",     "上菜时，将本菜技能甜蜜传递给 2 个食物。",     TERM_TRANSFER, tf("All", 2)),
    "sk_tf_any_3":   ("甜蜜传递·3",     "上菜时，将本菜技能甜蜜传递给 3 个食物。",     TERM_TRANSFER, tf("All", 3)),

    # ---- P1 计分技能 ----
    "sk_cookie":   ("酥脆边角", "处在棋盘边缘时，倍率 +3。", "", [
        r(0, "OnSettle", "Edge", "Self", "Instances", "Gate", "None", 0, "", "AddMultFlat", "Self", 0, 3, "")]),
    "sk_macaron":  ("缤纷马卡龙", "周围每有一种食物，分数 +8。", "", [
        r(0, "OnSettle", "DishCount", "Adjacent", "Kinds", "Per", "None", 0, "", "AddFlat", "Self", 0, 8, "")]),
    "sk_pudding":  ("颤颤布丁", "周围每有 1 个空格，分数 -2。", "", [
        r(0, "OnSettle", "EmptyCell", "Adjacent", "Instances", "Per", "None", 0, "", "AddFlat", "Self", 0, -2, "")]),
    "sk_eggtart":  ("回味蛋挞", "本局每结算过 1 次本种，分数 +6。", "", [
        r(0, "OnSettle", "SameKindInRun", "Self", "Instances", "Per", "None", 0, "", "AddFlat", "Self", 0, 6, "")]),
    "sk_mochi":    ("弹韧麻薯", "同列每有 1 个食物，倍率 +1。", "", [
        r(0, "OnSettle", "DishCount", "Column", "Instances", "Per", "None", 0, "", "AddMultFlat", "Self", 0, 1, "")]),
    "sk_daifuku":  ("满盈大福", "同行每有 1 个食物，分数 +10。", "", [
        r(0, "OnSettle", "DishCount", "Row", "Instances", "Per", "None", 0, "", "AddFlat", "Self", 0, 10, "")]),
    "sk_jelly":    ("晶莹果冻", "全场每有 1 种食物，周围食物倍率 +0.2。", "", [
        r(0, "OnSettle", "DishCount", "All", "Kinds", "Per", "None", 0, "", "AddMultFlat", "Adjacent", 0, 0.2, "")]),
    "sk_gold_choco": ("财运金砖", "若周围被填满，获得 30 金币。", "", [
        r(0, "OnSettle", "PositionFilled", "Adjacent", "Instances", "Gate", "None", 0, "", "GrantGold", "Self", 0, 30, "")]),
    "sk_sundae":   ("双层圣代", "视为 2 个食物；同行每有 1 个食物，倍率 +2。", "", [
        r(0, "OnSettle", "DishCount", "Row", "Instances", "Per", "None", 0, "", "AddMultFlat", "Self", 0, 2, "")]),
    "sk_lollipop": ("小巧棒棒糖", "周围有占 1 格的食物时，倍率 ×1.5。", "", [
        r(0, "OnSettle", "ShapeMatch", "Adjacent", "Instances", "Gate", "None", 0, "1x1", "AddMult", "Self", 0, 1.5, "")]),
    "sk_gummy":    ("弹弹软糖", "周围每有 1 个食物，倍率 ×1.2。", "", [
        r(0, "OnSettle", "DishCount", "Adjacent", "Instances", "Per", "None", 0, "", "AddMult", "Self", 0, 1.2, "")]),
    "sk_mousse":   ("绵密慕斯", "倍率 ×1.5；技能额外结算 1 次。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMult", "Self", 0, 1.5, ""),
        r(1, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "ExtraSettlement", "Self", 0, 1, "")]),
    "sk_nougat":   ("绵软牛轧", "技能额外结算 1 次。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "ExtraSettlement", "Self", 0, 1, "")]),
    "sk_eggroll":  ("酥脆蛋卷", "视为 4 个食物；每有 1 个食物，倍率 +0.2。", "", [
        r(0, "OnSettle", "DishCount", "All", "Instances", "Per", "None", 0, "self", "AddMultFlat", "Self", 0, 0.2, "")]),
    "sk_choco_bar": ("浓醇巧克力棒", "周围食物倍率 +5。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMultFlat", "Adjacent", 0, 5, "")]),
    "sk_tanghulu": ("串串糖葫芦", "同列食物倍率 +3。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMultFlat", "Column", 0, 3, "")]),
    "sk_pengtang": ("蓬松椪糖", "本菜每有 1 个技能，倍率 +5。", "", [
        r(0, "OnSettle", "TagCount", "Self", "Instances", "Per", "None", 0, "", "AddMultFlat", "Self", 0, 5, "")]),
    "sk_parfait":  ("华丽芭菲", "视为 10 个食物；周围每有 1 个食物，倍率 ×1.2。", "", [
        r(0, "OnSettle", "DishCount", "Adjacent", "Instances", "Per", "None", 0, "", "AddMult", "Self", 0, 1.2, "")]),
    "sk_sundae_big": ("巨型圣代", "视为 5 个食物；同列食物倍率 ×2。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMult", "Column", 0, 2, "")]),
    "sk_apple_pie": ("暖心苹果派", "同行每有 1 个食物，周围食物倍率 +0.3。", "", [
        r(0, "OnSettle", "DishCount", "Row", "Instances", "Per", "None", 0, "", "AddMultFlat", "Adjacent", 0, 0.3, "")]),
    "sk_palmier":  ("千层蝴蝶酥", "视为 4 个食物；周围食物倍率 ×1.5。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMult", "Adjacent", 0, 1.5, "")]),
    "sk_candycane": ("薄荷拐杖糖", "同列食物倍率 ×1.4。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMult", "Column", 0, 1.4, "")]),
    "sk_marshmallow": ("蓬蓬棉花糖", "本菜每有 1 个技能，倍率 ×1.5。", "", [
        r(0, "OnSettle", "TagCount", "Self", "Instances", "Per", "None", 0, "", "AddMult", "Self", 0, 1.5, "")]),
    "sk_sugarbean": ("缤纷糖豆", "技能额外结算 1 次。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "ExtraSettlement", "Self", 0, 1, "")]),

    # 复合菜的 P1 已支持部分
    "sk_waffle_mult": ("格纹华夫", "倍率 ×3。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMult", "Self", 0, 3, "")]),
    "sk_dorayaki_mult": ("满月铜锣烧", "倍率 +4。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMultFlat", "Self", 0, 4, "")]),
    "sk_donut_mult": ("甜圈光环", "倍率 +5。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMultFlat", "Self", 0, 5, "")]),
    "sk_swiss_mult": ("卷卷瑞士", "所有食物倍率 +0.5。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMultFlat", "All", 0, 0.5, "")]),
    "sk_chiffon_mult": ("轻盈戚风", "倍率 +5。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMultFlat", "Self", 0, 5, "")]),
    "sk_fruit_mult": ("缤纷水果", "倍率 ×2。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMult", "Self", 0, 2, "")]),

    # ---- P2 全局欢乐蛋糕层数 ----
    "sk_puff":         ("绵柔泡芙", "周围每有 1 个食物，欢乐蛋糕 +2 层。", "", [
        r(0, "OnServe", "DishCount", "Adjacent", "Instances", "Per", "None", 0, "", "AddLayer", "Self", 0, 2, "")]),
    "sk_cream_cell":   ("奶油小格", "欢乐蛋糕 +5 层。", "", [
        r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "", "AddLayer", "Self", 0, 5, "")]),
    "sk_milk_slice":   ("清凉奶片", "若同行被填满，欢乐蛋糕 +10 层。", "", [
        r(0, "OnServe", "PositionFilled", "Row", "Instances", "Gate", "None", 0, "", "AddLayer", "Self", 0, 10, "")]),
    "sk_cone_layer":   ("蛋筒叠层", "欢乐蛋糕 +10 层。", "", [
        r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "", "AddLayer", "Self", 0, 10, "")]),
    "sk_cone_copy":    ("蛋筒复制", "临时复制本菜品至空格中。", "", [
        r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "", "TempCopyDish", "Self", 0, 1, "")]),
    "sk_cupcake_copy": ("模仿纸杯", "获得周围食物的 1 个技能。", "", [
        r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "", "CopySkill", "Adjacent", 0, 1, "")]),
    "sk_cupcake_layer":("纸杯叠层", "自身每有 1 个技能，欢乐蛋糕 +6 层。", "", [
        r(0, "OnServe", "TagCount", "Self", "Instances", "Per", "None", 0, "", "AddLayer", "Self", 0, 6, "")]),
    "sk_eclair":       ("闪电充能", "最多消耗 3 层欢乐蛋糕，每层额外结算 1 次。", "", [
        r(0, "OnSettle", "LayerCount", "Self", "Instances", "Per", "None", 0, "cap:3", "ExtraSettlement", "Self", 0, 1, ""),
        r(1, "OnSettle", "LayerCount", "Self", "Instances", "Per", "None", 0, "cap:3", "ConsumeLayer", "Self", 0, 1, "")]),
    "sk_souffle":      ("蓬松舒芙蕾", "欢乐蛋糕 ×1.5 层，至少 +5 层。", "", [
        r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "", "AddLayer", "Self", 0, 1.5, "multfloor:5")]),
    "sk_croissant":    ("酥层牛角", "周围每有 1 种食物，欢乐蛋糕 +2 层。", "", [
        r(0, "OnServe", "DishCount", "Adjacent", "Kinds", "Per", "None", 0, "", "AddLayer", "Self", 0, 2, "")]),
    "sk_choco_candy":  ("永恒巧克力糖", "本食物每有 1 个技能，分数永久 +5。", "", [
        r(0, "OnSettle", "TagCount", "Self", "Instances", "Per", "None", 0, "", "PermanentAddFlat", "Self", 0, 5, "")]),
    "sk_toffee":       ("醇厚太妃", "分数永久 ×1.2。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "PermanentAddMult", "Self", 0, 1.2, "")]),
    "sk_mooncake_copy":("团圆月饼", "视为 5 个食物；复制周围食物的 1 个技能。", "", [
        r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "", "CopySkill", "Adjacent", 0, 1, "")]),
    "sk_cream_cake_perm":  ("奶油永恒", "分数永久 +10。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "PermanentAddFlat", "Self", 0, 10, "")]),
    "sk_cream_cake_spread":("奶油浇灌", "将分数加到所有蛋糕上（每个蛋糕 +10 分）。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddFlat", "Category", 0, 10, "cat:cake")]),
    "sk_donut_layer":  ("甜圈叠层", "欢乐蛋糕 +15 层。", "", [
        r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "", "AddLayer", "Self", 0, 15, "")]),
    "sk_swiss_layer":  ("瑞士叠层", "欢乐蛋糕 +20 层。", "", [
        r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "", "AddLayer", "Self", 0, 20, "")]),
    "sk_waffle_countas":("填满华夫", "同行被填满时，视为 10 个食物。", "", [
        r(0, "OnSettle", "PositionFilled", "Row", "Instances", "Gate", "None", 0, "", "AddCountAs", "Self", 0, 9, "")]),
    "sk_dorayaki_countas":("铜锣叠影", "同列食物额外被视为 2 个食物。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddCountAs", "Column", 0, 2, "")]),
    "sk_mango_copy":   ("层次杨枝甘露", "视为 8 个食物；复制周围的两个技能。", "", [
        r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "", "CopySkill", "Adjacent", 0, 2, "")]),
    "sk_chiffon_spread":("戚风浇灌", "将倍率 +5 加到所有蛋糕上。", "", [
        r(0, "OnSettle", "None", "Self", "Instances", "Per", "None", 0, "", "AddMultFlat", "Category", 0, 5, "cat:cake")]),
    "sk_fruit_layer":  ("水果叠层", "蛋糕数量达 3/5/8 时，欢乐蛋糕 +10/20/40 层。", "", [
        r(0, "OnServe", "CategoryCount", "All", "Instances", "Reach", "None", 0, "cake;tiers:3|5|8", "AddLayer", "Self", 0, 0, "tiervals:10|20|40")]),
    "sk_taosu":        ("阶梯桃酥", "同行食物达 5/15/25 个时，同行食物倍率 ×1.5/2.5/5。", "", [
        r(0, "OnSettle", "DishCount", "Row", "Instances", "Reach", "None", 0, "tiers:5|15|25", "AddMult", "Row", 0, 0, "tiervals:1.5|2.5|5")]),
    "sk_chocolate":    ("甜蜜巧克力", "带甜蜜传递的食物达 3/5/8 种时，此类食物倍率 ×1.5/2.5/5。", "", [
        r(0, "OnSettle", "SkillTypeCount", "All", "Kinds", "Reach", "None", 0, "TransferSkills;tiers:3|5|8", "AddMult", "All", 0, 0, "tiervals:1.5|2.5|5;skilltype:TransferSkills")]),
    "sk_big_lollipop": ("巨型棒棒糖", "同行同列带甜蜜传递的食物，各执行一次它们的甜蜜传递。", TERM_TRANSFER, []),
    "sk_double_cake":  ("华丽双层蛋糕", "获得 3 种不同的蛋糕技能。", "", [
        r(0, "OnServe", "None", "Self", "Instances", "Per", "None", 0, "", "CopySkill", "Self", 0, 3, "cat:cake")]),
}


# ---------------------------------------------------------------------------
# 欢乐蛋糕层数分段 buff：结算时读全局层数，layers>=threshold 的档累计应用到该分类所有食物。
# effectType 复用 SkillActionType：AddFlat=基础分+系数×层数；AddMultFlat=倍率+系数×层数；AddMult=倍率×(系数×层数)。
# 元组：(id, order, threshold, category, effectType, valuePerLayer, desc)
CAKE_LAYER_BUFFS = [
    ("clb_flat",     0, 1,   "cake", "AddFlat",     5,    "欢乐蛋糕≥1层：所有蛋糕基础分 +5×层数。"),
    ("clb_multflat", 1, 50,  "cake", "AddMultFlat", 0.1,  "欢乐蛋糕≥50层：所有蛋糕倍率 +0.1×层数。"),
    ("clb_mult",     2, 100, "cake", "AddMult",     0.02, "欢乐蛋糕≥100层：所有蛋糕倍率 ×(0.02×层数)。"),
]


def hidden_and_econ(deli, phase):
    hmin = max(1, deli // 2)
    hmax = deli + max(3, deli // 2)
    if "h" == phase or phase.endswith("h"):
        weight = 60
    elif "m" in phase:
        weight = 90
    else:
        weight = 120
    price = max(2, round(deli / 6))
    return hmin, hmax, weight, price


def clear_and_headers(ws, headers):
    """删除全部行，写入 3 行表头 (##var / ##comment / ##type)。"""
    if ws.max_row > 0:
        ws.delete_rows(1, ws.max_row)
    for ri, row in enumerate(headers, start=1):
        for ci, val in enumerate(row, start=1):
            if val is not None and val != "":
                ws.cell(row=ri, column=ci, value=val)


def main():
    wb = openpyxl.load_workbook(DISH_XLSX)

    # ---------------- dish_base ----------------
    wb_base = wb["dish_base"]
    clear_and_headers(wb_base, [
        ["##var", "id", "name", "deliciousness", "icon", "allowRotate", "skills", "category", "countAs", "*shapeRows"],
        ["##comment", "菜品基础ID", "菜品名称", "基础美味度", "图标资源路径", "是否允许旋转摆放", "技能ID列表(| 分隔)", "菜品分类(如cake;空=无)", "视为食物数(默认1)", "菜品形状行列表"],
        ["##type", "string", "string", "int", "string", "bool", "string", "string", "int", "list,string"],
    ])
    row = 4
    for (did, name, deli, shape, cat, cas, skills, phase) in DISHES:
        wb_base.cell(row=row, column=2, value=did)
        wb_base.cell(row=row, column=3, value=name)
        wb_base.cell(row=row, column=4, value=deli)
        wb_base.cell(row=row, column=5, value=f"Sprites/Dishes/{did}")
        wb_base.cell(row=row, column=6, value="false")
        wb_base.cell(row=row, column=7, value="|".join(skills))
        wb_base.cell(row=row, column=8, value=cat)
        wb_base.cell(row=row, column=9, value=cas)
        wb_base.cell(row=row, column=10, value=shape[0])
        row += 1
        for extra in shape[1:]:
            wb_base.cell(row=row, column=10, value=extra)
            row += 1

    # ---------------- dish_variant ----------------
    wb_var = wb["dish_variant"]
    clear_and_headers(wb_var, [
        ["##var", "id", "baseId", "flavorId", "baseWeight", "price", "hiddenRange", "rotation"],
        ["##comment", "菜品变体ID", "引用的菜品基础ID", "风味标签ID(可空,单槽)", "随机基础权重", "商店价格", "出现隐藏分区间(单元格: min,max)", "固定旋转朝向(整格四向)"],
        ["##type", "string", "string", "string", "float", "int", "HiddenRange", "DishRotation"],
    ])
    row = 4
    for (did, name, deli, shape, cat, cas, skills, phase) in DISHES:
        hmin, hmax, weight, price = hidden_and_econ(deli, phase)
        wb_var.cell(row=row, column=2, value=did)
        wb_var.cell(row=row, column=3, value=did)
        wb_var.cell(row=row, column=4, value="")
        wb_var.cell(row=row, column=5, value=weight)
        wb_var.cell(row=row, column=6, value=price)
        wb_var.cell(row=row, column=7, value=f"{hmin},{hmax}")
        wb_var.cell(row=row, column=8, value="Deg0")
        row += 1

    # ---------------- skill ----------------
    wb_skill = wb["skill"]
    clear_and_headers(wb_skill, [
        ["##var", "id", "name", "desc", "termId"],
        ["##comment", "标签ID", "标签名称", "标签描述", "关联术语ID"],
        ["##type", "string", "string", "string", "string"],
    ])
    row = 4
    for sid, (sname, sdesc, sterm, _rules) in SKILLS.items():
        wb_skill.cell(row=row, column=2, value=sid)
        wb_skill.cell(row=row, column=3, value=sname)
        wb_skill.cell(row=row, column=4, value=sdesc)
        wb_skill.cell(row=row, column=5, value=sterm)
        row += 1

    # ---------------- skill_rule ----------------
    wb_rule = wb["skill_rule"]
    clear_and_headers(wb_rule, [
        ["##var", "id", "skillId", "order", "trigger", "condType", "condScope", "condUnit", "condMode", "condCompare", "condThreshold", "condParam", "actionType", "actionScope", "actionCount", "actionValue", "actionParam"],
        ["##comment", "规则ID(主键)", "所属技能ID", "同技能内执行顺序", "触发时机", "前提类型", "前提作用域", "计数单位", "计数模式", "比较符", "阈值", "前提附加参数", "行为类型", "行为作用域", "随机目标数(0=全部)", "行为数值", "行为参数"],
        ["##type", "string", "string", "int", "SkillTrigger", "SkillConditionType", "SkillScope", "CountUnit", "CountMode", "CompareOp", "int", "string", "SkillActionType", "SkillScope", "int", "list,float", "list,string"],
    ])
    row = 4
    for sid, (_n, _d, _t, rules) in SKILLS.items():
        for rule in rules:
            (order, trigger, ctype, cscope, cunit, cmode, ccmp, cthr, cparam,
             atype, ascope, acount, aval, aparam) = rule
            wb_rule.cell(row=row, column=2, value=f"rr_{sid}_{order}")
            wb_rule.cell(row=row, column=3, value=sid)
            wb_rule.cell(row=row, column=4, value=order)
            wb_rule.cell(row=row, column=5, value=trigger)
            wb_rule.cell(row=row, column=6, value=ctype)
            wb_rule.cell(row=row, column=7, value=cscope)
            wb_rule.cell(row=row, column=8, value=cunit)
            wb_rule.cell(row=row, column=9, value=cmode)
            wb_rule.cell(row=row, column=10, value=ccmp)
            wb_rule.cell(row=row, column=11, value=cthr)
            wb_rule.cell(row=row, column=12, value=cparam)
            wb_rule.cell(row=row, column=13, value=atype)
            wb_rule.cell(row=row, column=14, value=ascope)
            wb_rule.cell(row=row, column=15, value=acount)
            wb_rule.cell(row=row, column=16, value=aval)
            wb_rule.cell(row=row, column=17, value=aparam)
            row += 1

    # ---------------- cake_layer_buff ----------------
    wb_clb = wb["cake_layer_buff"] if "cake_layer_buff" in wb.sheetnames else wb.create_sheet("cake_layer_buff")
    clear_and_headers(wb_clb, [
        ["##var", "id", "order", "threshold", "category", "effectType", "valuePerLayer", "desc"],
        ["##comment", "档位ID", "执行顺序", "层数阈值(>=生效)", "作用分类", "效果类型(SkillActionType)", "每层系数", "描述"],
        ["##type", "string", "int", "int", "string", "SkillActionType", "float", "string"],
    ])
    row = 4
    for (cid, order, threshold, category, effect_type, value_per_layer, desc) in CAKE_LAYER_BUFFS:
        wb_clb.cell(row=row, column=2, value=cid)
        wb_clb.cell(row=row, column=3, value=order)
        wb_clb.cell(row=row, column=4, value=threshold)
        wb_clb.cell(row=row, column=5, value=category)
        wb_clb.cell(row=row, column=6, value=effect_type)
        wb_clb.cell(row=row, column=7, value=value_per_layer)
        wb_clb.cell(row=row, column=8, value=desc)
        row += 1

    wb.save(DISH_XLSX)
    print(f"dish.xlsx rebuilt: {len(DISHES)} dishes, {len(SKILLS)} skills, {len(CAKE_LAYER_BUFFS)} cake-layer buffs")

    # ---------------- recipe.xlsx : 重写菜谱池为甜品 ----------------
    rwb = openpyxl.load_workbook(RECIPE_XLSX)
    rws = rwb["recipe"]
    # 保留表头 3 行，重写数据
    if rws.max_row > 3:
        rws.delete_rows(4, rws.max_row - 3)

    def write_recipe(start_row, rid, fixed, req, pool):
        rws.cell(row=start_row, column=2, value=rid)
        rws.cell(row=start_row, column=3, value=fixed)
        rws.cell(row=start_row, column=4, value=req)
        rws.cell(row=start_row, column=5, value=pool[0])
        rr = start_row + 1
        for entry in pool[1:]:
            rws.cell(row=rr, column=5, value=entry)
            rr += 1
        return rr

    row = 4
    row = write_recipe(row, "recipe_glutton", "cookie", 40, [
        "cookie,150,0,10", "macaron,120,0,8", "daifuku,110,0,6",
        "eggtart,110,0,6", "jelly,100,0,4", "sundae,60,2,20", "parfait,30,1,50"])
    row = write_recipe(row, "recipe_wok", "mochi", 45, [
        "mochi,130,0,10", "gummy,110,0,8", "lollipop,100,0,10",
        "mousse,80,0,16", "eggroll,60,2,30", "cream_cake,40,1,50"])
    row = write_recipe(row, "recipe_chili", "pudding", 38, [
        "pudding,130,0,30", "choco_candy,120,0,10", "gold_choco,100,0,6",
        "tanghulu,90,0,20", "choco_bar,60,1,40", "marshmallow,30,1,80"])
    rwb.save(RECIPE_XLSX)
    print("recipe.xlsx rewritten (recipe_glutton/wok/chili)")


if __name__ == "__main__":
    main()
