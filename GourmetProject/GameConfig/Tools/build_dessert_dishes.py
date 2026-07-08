#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""重建 dish.xlsx 的 dish_base / dish_variant / sub_skill / skill 表为甜品阵容，
并重写 recipe.xlsx 的菜谱池引用。

技能两层模型 + 一菜一 skill：
- sub_skill：具体子技能，一行=一条完整效果（全部 SkillRuleDef 字段 + isPassive/signed/descTemplate）。
  参数不同即不同子技能；内容完全相同的子技能按内容去重复用。
- skill：技能元信息（id/termId/descOverride）+ 有序引用的子技能 id 列表；描述由子技能占位符模板按序自动拼接。
- 一菜一 skill：每道菜的多个技能片段(SKILLS)在输出阶段合并成单一菜品技能 `sk_<did>`，
  甜蜜传递作为其中一个子技能；dish_base.skills 只引用这一个技能。
  运行时甜蜜传递把「本 skill 内其他子技能」作为外来子技能传给目标，结算时对目标生效一次。

作者友好写法：DISHES 仍按「多个 skill 片段」书写，SKILLS 为片段库，build_merged_skills() 负责合并。
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
    ("sundae",       "圣代",     20, ["XX"],                                    "",     1, ["sk_sundae", "sk_countas2"], "m"),
    ("cone",         "甜筒",     20, ["X", "X"],                                "cake", 1, ["sk_cone_layer", "sk_cone_copy"], "m"),
    ("lollipop",     "棒棒糖",   10, ["XX"],                                    "",     1, ["sk_lollipop", "sk_tf_col_2"], "m"),
    ("gummy",        "软糖",     8,  ["XX"],                                    "",     1, ["sk_gummy", "sk_tf_adj_2"], "m"),
    ("cupcake",      "杯子蛋糕", 14, ["XX"],                                    "cake", 1, ["sk_cupcake_copy", "sk_cupcake_layer"], "mh"),
    ("mousse",       "慕斯蛋糕", 16, ["X", "X"],                                "cake", 1, ["sk_mousse"], "mh"),
    ("toffee",       "太妃糖",   15, ["X", "X"],                                "",     1, ["sk_toffee", "sk_tf_any_2"], "m"),
    ("nougat",       "牛轧糖",   20, ["X", "X"],                                "",     1, ["sk_nougat", "sk_tf_adj_2"], "m"),
    ("eggroll",      "蛋卷",     30, ["XXX"],                                   "",     1, ["sk_eggroll", "sk_countas4"], "m"),
    ("choco_bar",    "巧克力棒", 40, ["XXX"],                                   "",     1, ["sk_choco_bar", "sk_tf_any_3"], "m"),
    ("eclair",       "闪电泡芙", 30, ["X", "X", "X"],                           "cake", 1, ["sk_eclair"], "m"),
    ("tanghulu",     "糖葫芦",   20, ["X", "X", "X"],                           "",     1, ["sk_tanghulu", "sk_tf_row_3"], "m"),
    ("souffle",      "舒芙蕾",   30, ["XX", "XX"],                              "cake", 1, ["sk_souffle"], "m"),
    ("taosu",        "桃酥",     20, ["XX", "XX"],                              "",     1, ["sk_taosu"], "mh"),
    ("mooncake",     "月饼",     25, ["XX", "XX"],                              "",     1, ["sk_mooncake_copy", "sk_countas5"], "mh"),
    ("waffle",       "华夫饼",   20, ["XX", "XX"],                              "",     1, ["sk_waffle_mult", "sk_waffle_countas"], "mh"),
    ("dorayaki",     "铜锣烧",   20, ["XX", "XX"],                              "",     1, ["sk_dorayaki_countas", "sk_dorayaki_mult"], "mh"),
    ("cream_cake",   "奶油蛋糕", 50, ["XX", "XX"],                              "cake", 1, ["sk_cream_cake_perm", "sk_cream_cake_spread"], "mh"),
    ("donut",        "甜甜圈",   40, ["XX", "XX"],                              "cake", 1, ["sk_donut"], "mh"),
    ("pengtang",     "椪糖",     30, ["XX", "XX"],                              "",     1, ["sk_pengtang", "sk_tf_col_all"], "m"),
    ("parfait",      "芭菲",     50, ["XXX", "XXX"],                            "",     1, ["sk_parfait", "sk_countas10"], "h"),
    ("sundae_big",   "圣代",     40, ["XX", "XX", "XX"],                        "",     1, ["sk_sundae_big", "sk_countas5"], "h"),
    ("swiss_roll",   "瑞士卷",   30, ["XX", "XX", "XX"],                        "cake", 1, ["sk_swiss_layer", "sk_swiss_mult"], "m"),
    ("mango_sago",   "杨枝甘露", 20, ["XXX", "XXX"],                            "",     1, ["sk_mango_copy", "sk_countas8"], "mh"),
    ("chiffon",      "戚风蛋糕", 30, ["XXX", "XXX"],                            "cake", 1, ["sk_chiffon_mult", "sk_chiffon_spread"], "mh"),
    ("apple_pie",    "苹果派",   50, ["XX", "XX", "XX"],                        "",     1, ["sk_apple_pie"], "m"),
    ("fruit_cake",   "水果蛋糕", 40, ["XX", "XX", "XX"],                        "cake", 1, ["sk_fruit_mult", "sk_fruit_layer"], "m"),
    ("chocolate",    "巧克力",   30, ["XXX", "XXX"],                            "",     1, ["sk_chocolate"], "mh"),
    ("croissant",    "牛角包",   20, ["XX", "X."],                             "cake", 1, ["sk_croissant"], "m"),
    ("palmier",      "蝴蝶酥",   30, [".X", "XX"],                             "",     1, ["sk_palmier", "sk_countas4"], "m"),
    ("candycane",    "拐杖糖",   40, ["XX", "X.", "X."],                       "",     1, ["sk_candycane", "sk_tf_adj_all"], "mh"),
    ("big_lollipop", "大棒棒糖", 100,["XXX", "XXX", "XXX", ".X.", ".X."],      "",     1, ["sk_big_lollipop"], "h"),
    ("marshmallow",  "棉花糖",   80, ["XXX", "XXX", "XXX"],                     "",     1, ["sk_marshmallow", "sk_tf_row_all"], "h"),
    ("double_cake",  "双层蛋糕", 140,[".XXX.", ".XXX.", "XXXXX", "XXXXX"],     "cake", 1, ["sk_double_cake"], "h"),
]

# ---------------------------------------------------------------------------
# 子技能模板库（可复用的效果原型 + 占位符描述模板）。
# 元组: (name, trigger, condType, condMode, actionType, isPassive, signed, descTemplate)
#
# 描述占位符（运行时由 SkillDescComposer 回填组合层参数）：
#   {0}{1}..  actionValue[i]（signed=True 补正负号，signed=False 原样）
#   {cscope}  前提作用域词（自身/周围/同行/同列/全场）
#   {ascope}  行为作用域词（周围/同行/同列…）
#   {atargets} 行为作用域「X食物」前缀（Self→空，Adjacent→周围食物…）
#   {unit}    计数单位（个/种）  {thr} 阈值  {count} 目标数
#   {targets} 甜蜜传递目标短语（作用域+目标数，0=所有）
#   {tiers}   condParam tiers 解析为 3/5/8   {tiervals} actionParam tiervals 解析为 1.5/2.5/5
#   {floor}   actionParam multfloor/floor 值   {cat} 分类名（cake→蛋糕）
SUB_SKILLS = {
    "ss_flat_per_dish":       ("每食物加分",     "OnSettle", "DishCount",     "Per",   "AddFlat",          False, True,  "{cscope}每有 1 {unit}食物，分数 {0}。"),
    "ss_multflat_per_dish":   ("每食物加倍率",   "OnSettle", "DishCount",     "Per",   "AddMultFlat",      False, True,  "{cscope}每有 1 {unit}食物，{atargets}倍率 {0}。"),
    "ss_mult_per_dish":       ("每食物乘倍率",   "OnSettle", "DishCount",     "Per",   "AddMult",          False, False, "{cscope}每有 1 {unit}食物，{atargets}倍率 ×{0}。"),
    "ss_flat_per_empty":      ("每空格加分",     "OnSettle", "EmptyCell",     "Per",   "AddFlat",          False, True,  "{cscope}每有 1 个空格，分数 {0}。"),
    "ss_flat_per_run":        ("每结算加分",     "OnSettle", "SameKindInRun", "Per",   "AddFlat",          False, True,  "本局每结算过 1 次本种，分数 {0}。"),
    "ss_multflat_per_tag":    ("每技能加倍率",   "OnSettle", "TagCount",      "Per",   "AddMultFlat",      False, True,  "本菜每有 1 个技能，{atargets}倍率 {0}。"),
    "ss_mult_per_tag":        ("每技能乘倍率",   "OnSettle", "TagCount",      "Per",   "AddMult",          False, False, "本菜每有 1 个技能，{atargets}倍率 ×{0}。"),
    "ss_permflat_per_tag":    ("每技能永久加分", "OnSettle", "TagCount",      "Per",   "PermanentAddFlat", False, True,  "本食物每有 1 个技能，分数永久 {0}。"),
    "ss_edge_multflat":       ("边缘加倍率",     "OnSettle", "Edge",          "Gate",  "AddMultFlat",      False, True,  "处在棋盘边缘时，倍率 {0}。"),
    "ss_shape_mult":          ("邻格形状乘倍率", "OnSettle", "ShapeMatch",    "Gate",  "AddMult",          False, False, "{cscope}有占 1 格的食物时，倍率 ×{0}。"),
    "ss_grant_gold_filled":   ("填满获金",       "OnSettle", "PositionFilled","Gate",  "GrantGold",        False, False, "若{cscope}被填满，获得 {0} 金币。"),
    "ss_mult":                ("乘倍率",         "OnSettle", "None",          "Per",   "AddMult",          False, False, "{atargets}倍率 ×{0}。"),
    "ss_multflat":            ("加倍率",         "OnSettle", "None",          "Per",   "AddMultFlat",      False, True,  "{atargets}倍率 {0}。"),
    "ss_perm_mult":           ("永久乘分",       "OnSettle", "None",          "Per",   "PermanentAddMult", False, False, "{atargets}分数永久 ×{0}。"),
    "ss_perm_flat":           ("永久加分",       "OnSettle", "None",          "Per",   "PermanentAddFlat", False, True,  "{atargets}分数永久 {0}。"),
    "ss_extra_settle":        ("额外结算",       "OnSettle", "None",          "Per",   "ExtraSettlement",  True,  False, "技能额外结算 {0} 次。"),
    # 本体「视为N个食物」：dish_base.countAs 恒为 1，本子技能对自身 AddCountAs 补 (N-1)，令有效视为食物数=N。
    # 描述用 {countas}=actionValue+1 显示总数 N（运行时是加性叠加：base 1 + (N-1)）。
    "ss_count_as_base":       ("视为多食物(本体)","OnSettle","None",          "Per",   "AddCountAs",       True,  False, "视为 {countas} 个食物。"),
    "ss_count_as":            ("视为多食物",     "OnSettle", "None",          "Per",   "AddCountAs",       True,  False, "{atargets}额外视为 {0} 个食物。"),
    "ss_count_as_filled":     ("填满视为多食物", "OnSettle", "PositionFilled","Gate",  "AddCountAs",       True,  False, "若{cscope}被填满，额外视为 {0} 个食物。"),
    "ss_layer_add":           ("加蛋糕层",       "OnServe",  "None",          "Per",   "AddLayer",         False, True,  "欢乐蛋糕 {0} 层。"),
    "ss_layer_per_dish":      ("每食物加层",     "OnServe",  "DishCount",     "Per",   "AddLayer",         False, True,  "{cscope}每有 1 {unit}食物，欢乐蛋糕 {0} 层。"),
    "ss_layer_per_tag":       ("每技能加层",     "OnServe",  "TagCount",      "Per",   "AddLayer",         False, True,  "自身每有 1 个技能，欢乐蛋糕 {0} 层。"),
    "ss_layer_filled":        ("填满加层",       "OnServe",  "PositionFilled","Gate",  "AddLayer",         False, True,  "若{cscope}被填满，欢乐蛋糕 {0} 层。"),
    "ss_layer_mult":          ("蛋糕层乘",       "OnServe",  "None",          "Per",   "AddLayer",         False, False, "欢乐蛋糕 ×{0} 层，至少 +{floor} 层。"),
    "ss_layer_tier":          ("阶梯加层",       "OnServe",  "CategoryCount", "Reach", "AddLayer",         False, False, "蛋糕数量达 {tiers} 时，欢乐蛋糕 +{tiervals} 层。"),
    "ss_copy_skill":          ("复制技能",       "OnServe",  "None",          "Per",   "CopySkill",        False, False, "复制{ascope}食物的 {0} 个技能。"),
    "ss_copy_cat_skill":      ("获得分类技能",   "OnServe",  "None",          "Per",   "CopySkill",        False, False, "获得 {0} 种不同的{cat}技能。"),
    "ss_temp_copy":           ("临时复制",       "OnServe",  "None",          "Per",   "TempCopyDish",     False, False, "临时复制本菜品至空格中。"),
    "ss_flat_category":       ("浇灌加分",       "OnSettle", "None",          "Per",   "AddFlat",          False, True,  "将分数加到所有{cat}上（每个{cat} {0} 分）。"),
    "ss_multflat_category":   ("浇灌加倍率",     "OnSettle", "None",          "Per",   "AddMultFlat",      False, True,  "将倍率 {0} 加到所有{cat}上。"),
    "ss_mult_tier_dishcount": ("阶梯乘倍率",     "OnSettle", "DishCount",     "Reach", "AddMult",          False, False, "{cscope}食物达 {tiers} 个时，{atargets}倍率 ×{tiervals}。"),
    "ss_mult_tier_skilltype": ("阶梯类型乘倍率", "OnSettle", "SkillTypeCount","Reach", "AddMult",          False, False, "带甜蜜传递的食物达 {tiers} 种时，此类食物倍率 ×{tiervals}。"),
    "ss_transfer":            ("甜蜜传递",       "OnServe",  "None",          "Per",   "TransferSkills",   False, False, "上菜时，将本菜的其他子技能甜蜜传递给{targets}（结算时对目标生效一次）。"),
    "ss_consume_layer":       ("消耗蛋糕层",     "OnSettle", "None",          "Per",   "ConsumeLayer",     False, False, "消耗 {0} 层欢乐蛋糕。"),
}

# ---------------------------------------------------------------------------
# 技能组合项（作者友好写法）。c(...) = 引用一个 SUB_SKILLS 原型 + 填入可变参数。
# 输出阶段 flatten_skills() 把「原型枚举字段 + 这些参数」展平为一条具体子技能行，
# 内容完全相同的组件复用同一条子技能。参数: subSkillId, condScope, condUnit,
#       condCompare, condThreshold, condParam, actionScope, actionCount, actionValue, actionParam
def c(sub, cscope="Self", cunit="Instances", ccmp="None", cthr=0, cparam="",
      ascope="Self", acount=0, aval=0, aparam=""):
    return (sub, cscope, cunit, ccmp, cthr, cparam, ascope, acount, aval, aparam)

TERM_TRANSFER = "term_sweet_transfer"

# 通用甜蜜传递（OnServe，把自身非传递技能给目标）
def tf(scope, count):
    return [c("ss_transfer", ascope=scope, acount=count)]

# 技能: id -> (内部标签, termId, descOverride, [组合项])
# 内部标签仅用于本脚本可读性，不写入 skill 表（表标题按 termId 术语名，无则空）。
# descOverride 非空时覆盖自动拼接的描述（用于多子技能读起来割裂、或无子技能的技能）。
SKILLS = {
    # ---- 甜蜜传递（共用同一子技能模板，仅作用域/目标数不同） ----
    "sk_tf_row_1":   ("甜蜜传递·同行1", TERM_TRANSFER, "", tf("Row", 1)),
    "sk_tf_row_3":   ("甜蜜传递·同行3", TERM_TRANSFER, "", tf("Row", 3)),
    "sk_tf_row_all": ("甜蜜传递·同行",  TERM_TRANSFER, "", tf("Row", 0)),
    "sk_tf_col_2":   ("甜蜜传递·同列2", TERM_TRANSFER, "", tf("Column", 2)),
    "sk_tf_col_all": ("甜蜜传递·同列",  TERM_TRANSFER, "", tf("Column", 0)),
    "sk_tf_adj_2":   ("甜蜜传递·周围2", TERM_TRANSFER, "", tf("Adjacent", 2)),
    "sk_tf_adj_all": ("甜蜜传递·周围",  TERM_TRANSFER, "", tf("Adjacent", 0)),
    "sk_tf_any_1":   ("甜蜜传递·1",     TERM_TRANSFER, "", tf("All", 1)),
    "sk_tf_any_2":   ("甜蜜传递·2",     TERM_TRANSFER, "", tf("All", 2)),
    "sk_tf_any_3":   ("甜蜜传递·3",     TERM_TRANSFER, "", tf("All", 3)),

    # ---- 本体「视为N个食物」被动子技能（countAs 恒 1，用 AddCountAs 补 N-1） ----
    "sk_countas2":   ("视为2个食物",  "", "", [c("ss_count_as_base", ascope="Self", aval=1)]),
    "sk_countas4":   ("视为4个食物",  "", "", [c("ss_count_as_base", ascope="Self", aval=3)]),
    "sk_countas5":   ("视为5个食物",  "", "", [c("ss_count_as_base", ascope="Self", aval=4)]),
    "sk_countas8":   ("视为8个食物",  "", "", [c("ss_count_as_base", ascope="Self", aval=7)]),
    "sk_countas10":  ("视为10个食物", "", "", [c("ss_count_as_base", ascope="Self", aval=9)]),

    # ---- P1 计分技能 ----
    "sk_cookie":     ("酥脆边角",     "", "", [c("ss_edge_multflat", ascope="Self", aval=3)]),
    "sk_macaron":    ("缤纷马卡龙",   "", "", [c("ss_flat_per_dish", cscope="Adjacent", cunit="Kinds", ascope="Self", aval=8)]),
    "sk_pudding":    ("颤颤布丁",     "", "", [c("ss_flat_per_empty", cscope="Adjacent", ascope="Self", aval=-2)]),
    "sk_eggtart":    ("回味蛋挞",     "", "", [c("ss_flat_per_run", cscope="Self", ascope="Self", aval=6)]),
    "sk_mochi":      ("弹韧麻薯",     "", "", [c("ss_multflat_per_dish", cscope="Column", cunit="Instances", ascope="Self", aval=1)]),
    "sk_daifuku":    ("满盈大福",     "", "", [c("ss_flat_per_dish", cscope="Row", cunit="Instances", ascope="Self", aval=10)]),
    "sk_jelly":      ("晶莹果冻",     "", "", [c("ss_multflat_per_dish", cscope="All", cunit="Kinds", ascope="Adjacent", aval=0.2)]),
    "sk_gold_choco": ("财运金砖",     "", "", [c("ss_grant_gold_filled", cscope="Adjacent", ascope="Self", aval=30)]),
    "sk_sundae":     ("双层圣代",     "", "", [c("ss_multflat_per_dish", cscope="Row", cunit="Instances", ascope="Self", aval=2)]),
    "sk_lollipop":   ("小巧棒棒糖",   "", "", [c("ss_shape_mult", cscope="Adjacent", cparam="1x1", ascope="Self", aval=1.5)]),
    "sk_gummy":      ("弹弹软糖",     "", "", [c("ss_mult_per_dish", cscope="Adjacent", cunit="Instances", ascope="Self", aval=1.2)]),
    "sk_mousse":     ("绵密慕斯",     "", "", [c("ss_mult", ascope="Self", aval=1.5), c("ss_extra_settle", ascope="Self", aval=1)]),
    "sk_nougat":     ("绵软牛轧",     "", "", [c("ss_extra_settle", ascope="Self", aval=1)]),
    "sk_eggroll":    ("酥脆蛋卷",     "", "", [c("ss_multflat_per_dish", cscope="All", cunit="Instances", cparam="self", ascope="Self", aval=0.2)]),
    "sk_choco_bar":  ("浓醇巧克力棒", "", "", [c("ss_multflat", ascope="Adjacent", aval=5)]),
    "sk_tanghulu":   ("串串糖葫芦",   "", "", [c("ss_multflat", ascope="Column", aval=3)]),
    "sk_pengtang":   ("蓬松椪糖",     "", "", [c("ss_multflat_per_tag", cscope="Self", ascope="Self", aval=5)]),
    "sk_parfait":    ("华丽芭菲",     "", "", [c("ss_mult_per_dish", cscope="Adjacent", cunit="Instances", ascope="Self", aval=1.2)]),
    "sk_sundae_big": ("巨型圣代",     "", "", [c("ss_mult", ascope="Column", aval=2)]),
    "sk_apple_pie":  ("暖心苹果派",   "", "", [c("ss_multflat_per_dish", cscope="Row", cunit="Instances", ascope="Adjacent", aval=0.3)]),
    "sk_palmier":    ("千层蝴蝶酥",   "", "", [c("ss_mult", ascope="Adjacent", aval=1.5)]),
    "sk_candycane":  ("薄荷拐杖糖",   "", "", [c("ss_mult", ascope="Column", aval=1.4)]),
    "sk_marshmallow":("蓬蓬棉花糖",   "", "", [c("ss_mult_per_tag", cscope="Self", ascope="Self", aval=1.5)]),
    "sk_sugarbean":  ("缤纷糖豆",     "", "", [c("ss_extra_settle", ascope="Self", aval=1)]),
    "sk_waffle_mult":  ("格纹华夫",   "", "", [c("ss_mult", ascope="Self", aval=3)]),
    "sk_dorayaki_mult":("满月铜锣烧", "", "", [c("ss_multflat", ascope="Self", aval=4)]),
    "sk_donut":        ("甜甜圈",     "", "", [c("ss_layer_add", ascope="Self", aval=15), c("ss_multflat", ascope="Self", aval=5)]),
    "sk_swiss_mult":   ("卷卷瑞士",   "", "", [c("ss_multflat", ascope="All", aval=0.5)]),
    "sk_chiffon_mult": ("轻盈戚风",   "", "", [c("ss_multflat", ascope="Self", aval=5)]),
    "sk_fruit_mult":   ("缤纷水果",   "", "", [c("ss_mult", ascope="Self", aval=2)]),

    # ---- P2 全局欢乐蛋糕层数 ----
    "sk_puff":          ("绵柔泡芙",     "", "", [c("ss_layer_per_dish", cscope="Adjacent", cunit="Instances", ascope="Self", aval=2)]),
    "sk_cream_cell":    ("奶油小格",     "", "", [c("ss_layer_add", ascope="Self", aval=5)]),
    "sk_milk_slice":    ("清凉奶片",     "", "", [c("ss_layer_filled", cscope="Row", ascope="Self", aval=10)]),
    "sk_cone_layer":    ("蛋筒叠层",     "", "", [c("ss_layer_add", ascope="Self", aval=10)]),
    "sk_cone_copy":     ("蛋筒复制",     "", "", [c("ss_temp_copy", ascope="Self", aval=1)]),
    "sk_cupcake_copy":  ("模仿纸杯",     "", "", [c("ss_copy_skill", ascope="Adjacent", aval=1)]),
    "sk_cupcake_layer": ("纸杯叠层",     "", "", [c("ss_layer_per_tag", cscope="Self", ascope="Self", aval=6)]),
    "sk_eclair":        ("闪电充能",     "", "", [c("ss_consume_layer", ascope="Self", aval=10), c("ss_multflat", ascope="Self", aval=5)]),
    "sk_souffle":       ("蓬松舒芙蕾",   "", "", [c("ss_layer_mult", ascope="Self", aval=1.5, aparam="multfloor:5")]),
    "sk_croissant":     ("酥层牛角",     "", "", [c("ss_layer_per_dish", cscope="Adjacent", cunit="Kinds", ascope="Self", aval=2)]),
    "sk_choco_candy":   ("永恒巧克力糖", "", "", [c("ss_permflat_per_tag", cscope="Self", ascope="Self", aval=5)]),
    "sk_toffee":        ("醇厚太妃",     "", "", [c("ss_perm_flat", ascope="Self", aval=10)]),
    "sk_mooncake_copy": ("团圆月饼",     "", "", [c("ss_copy_skill", ascope="Adjacent", aval=1)]),
    "sk_cream_cake_perm":  ("奶油永恒",  "", "", [c("ss_perm_flat", ascope="Self", aval=10)]),
    "sk_cream_cake_spread":("奶油浇灌",  "", "", [c("ss_flat_category", ascope="Category", aval=10, aparam="cat:cake")]),
    "sk_swiss_layer":   ("瑞士叠层",     "", "", [c("ss_layer_add", ascope="Self", aval=20)]),
    "sk_waffle_countas":("填满华夫",     "", "", [c("ss_count_as_filled", cscope="Row", ascope="Self", aval=9)]),
    "sk_dorayaki_countas":("铜锣叠影",   "", "", [c("ss_count_as", ascope="Column", aval=2)]),
    "sk_mango_copy":    ("层次杨枝甘露", "", "", [c("ss_copy_skill", ascope="Adjacent", aval=2)]),
    "sk_chiffon_spread":("戚风浇灌",     "", "", [c("ss_multflat_category", ascope="Category", aval=5, aparam="cat:cake")]),
    "sk_fruit_layer":   ("水果叠层",     "", "", [c("ss_layer_tier", cscope="All", cunit="Instances", cparam="cake;tiers:3|5|8", ascope="Self", aval=0, aparam="tiervals:10|20|40")]),
    "sk_taosu":         ("阶梯桃酥",     "", "", [c("ss_mult_tier_dishcount", cscope="Row", cunit="Instances", cparam="tiers:5|15|25", ascope="Row", aval=0, aparam="tiervals:1.5|2.5|5")]),
    "sk_chocolate":     ("甜蜜巧克力",   "", "", [c("ss_mult_tier_skilltype", cscope="All", cunit="Kinds", cparam="TransferSkills;tiers:3|5|8", ascope="All", aval=0, aparam="tiervals:1.5|2.5|5;skilltype:TransferSkills")]),
    "sk_big_lollipop":  ("巨型棒棒糖",   TERM_TRANSFER, "同行同列带甜蜜传递的食物，各执行一次它们的甜蜜传递。", []),
    "sk_double_cake":   ("华丽双层蛋糕", "", "", [c("ss_copy_cat_skill", cscope="Self", ascope="Self", aval=3, aparam="cat:cake")]),
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


def get_or_create_sheet(wb, name):
    return wb[name] if name in wb.sheetnames else wb.create_sheet(name)


# ---------------------------------------------------------------------------
# 两表合并：把「SUB_SKILLS 原型 + SKILLS 组件参数」展平成一批「具体子技能」。
# 每个组件 = 原型枚举字段 + 组件参数字段，构成一条完整 SkillRuleDef 语义的子技能行；
# 内容完全相同的组件去重复用同一条（如「倍率×5 作用自身」被多个技能共享）。
# 返回:
#   concrete: 有序 dict, subId -> 全字段 dict（按首次出现顺序）
#   skill_subs: dict, skillId -> [subId,...]（有序，即原组件顺序）
_SCOPE_SLUG = {"Self": "self", "Adjacent": "adj", "Row": "row",
               "Column": "col", "All": "all", "Category": "cat"}


def _num_slug(v):
    s = str(v)
    return s.replace("-", "m").replace(".", "p")


def _slug(sub, row):
    parts = [sub]
    # 有前提时带上前提作用域（区分同效果不同来源，如 Row/Column 计数）
    if row["condType"] != "None" and row["condScope"] != "Self":
        parts.append(_SCOPE_SLUG.get(row["condScope"], row["condScope"].lower()))
    if row["actionScope"] != "Self":
        parts.append(_SCOPE_SLUG.get(row["actionScope"], row["actionScope"].lower()))
    if row["actionValue"] not in (0, 0.0):
        parts.append("v" + _num_slug(row["actionValue"]))
    if row["actionCount"]:
        parts.append("n" + str(row["actionCount"]))
    return "_".join(parts)


def build_merged_skills():
    """一菜一 skill：把每道菜引用的多个 skill 片段合并成单一菜品技能。
    甜蜜传递作为其中一个子技能，与计分/被动子技能同处一个 skill。
    返回:
      merged: ordered dict, mid('sk_<did>') -> (termId, descOverride, [组合项...])
      dish_skill: did -> mid（写入 dish_base.skills 的唯一技能 id）
    """
    merged = {}
    dish_skill = {}
    for (did, _name, _deli, _shape, _cat, _cas, skills, _phase) in DISHES:
        mid = f"sk_{did}"
        comps = []
        term = ""
        overrides = []
        for sid in skills:
            (_n, sterm, soverride, scomps) = SKILLS[sid]
            comps.extend(scomps)
            if not term and sterm:
                term = sterm
            if soverride:
                overrides.append(soverride)
        merged[mid] = (term, "；".join(overrides), comps)
        dish_skill[did] = mid
    return merged, dish_skill


def flatten_skills(merged):
    concrete = {}       # subId -> full row dict
    sig_to_id = {}      # content signature -> subId
    skill_subs = {}     # skillId -> [subId,...]
    for sid, (_term, _override, comps) in merged.items():
        ids = []
        for comp in comps:
            (sub, cscope, cunit, ccmp, cthr, cparam, ascope, acount, aval, aparam) = comp
            (_pn, trigger, ctype, cmode, atype, is_passive, signed, tmpl) = SUB_SKILLS[sub]
            row = {
                "trigger": trigger, "condType": ctype, "condScope": cscope,
                "condUnit": cunit, "condMode": cmode, "condCompare": ccmp,
                "condThreshold": cthr, "condParam": cparam, "actionType": atype,
                "actionScope": ascope, "actionCount": acount, "actionValue": aval,
                "actionParam": aparam, "isPassive": is_passive, "signed": signed,
                "descTemplate": tmpl,
            }
            sig = tuple(sorted((k, str(v)) for k, v in row.items()))
            cid = sig_to_id.get(sig)
            if cid is None:
                base = _slug(sub, row)
                cid = base
                i = 2
                while cid in concrete:
                    cid = f"{base}_{i}"
                    i += 1
                sig_to_id[sig] = cid
                concrete[cid] = row
            ids.append(cid)
        skill_subs[sid] = ids
    return concrete, skill_subs


def main():
    wb = openpyxl.load_workbook(DISH_XLSX)

    # 一菜一 skill：先合并出每道菜的唯一技能与其子技能组合。
    merged_skills, dish_skill = build_merged_skills()

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
        wb_base.cell(row=row, column=7, value=dish_skill[did])
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

    # 两表合并：展平出「具体子技能」与「技能→子技能有序列表」。
    concrete_subs, skill_subs = flatten_skills(merged_skills)

    # ---------------- sub_skill（合并后=具体子技能，一行=一个完整效果） ----------------
    wb_sub = get_or_create_sheet(wb, "sub_skill")
    clear_and_headers(wb_sub, [
        ["##var", "id", "trigger", "condType", "condScope", "condUnit", "condMode", "condCompare", "condThreshold", "condParam", "actionType", "actionScope", "actionCount", "actionValue", "actionParam", "isPassive", "signed", "descTemplate"],
        ["##comment", "子技能ID(主键)", "触发时机", "前提类型", "前提作用域", "计数单位", "计数模式", "比较符", "阈值", "前提附加参数", "行为类型", "行为作用域", "随机目标数(0=全部)", "行为数值", "行为参数", "是否被动", "描述数值是否补正负号", "占位符描述模板"],
        ["##type", "string", "SkillTrigger", "SkillConditionType", "SkillScope", "CountUnit", "CountMode", "CompareOp", "int", "string", "SkillActionType", "SkillScope", "int", "list,float", "list,string", "bool", "bool", "string"],
    ])
    row = 4
    for sub_id, r in concrete_subs.items():
        wb_sub.cell(row=row, column=2, value=sub_id)
        wb_sub.cell(row=row, column=3, value=r["trigger"])
        wb_sub.cell(row=row, column=4, value=r["condType"])
        wb_sub.cell(row=row, column=5, value=r["condScope"])
        wb_sub.cell(row=row, column=6, value=r["condUnit"])
        wb_sub.cell(row=row, column=7, value=r["condMode"])
        wb_sub.cell(row=row, column=8, value=r["condCompare"])
        wb_sub.cell(row=row, column=9, value=r["condThreshold"])
        wb_sub.cell(row=row, column=10, value=r["condParam"])
        wb_sub.cell(row=row, column=11, value=r["actionType"])
        wb_sub.cell(row=row, column=12, value=r["actionScope"])
        wb_sub.cell(row=row, column=13, value=r["actionCount"])
        wb_sub.cell(row=row, column=14, value=r["actionValue"])
        wb_sub.cell(row=row, column=15, value=r["actionParam"])
        wb_sub.cell(row=row, column=16, value="true" if r["isPassive"] else "false")
        wb_sub.cell(row=row, column=17, value="true" if r["signed"] else "false")
        wb_sub.cell(row=row, column=18, value=r["descTemplate"])
        row += 1

    # ---------------- skill（技能元信息，正向有序引用子技能） ----------------
    wb_skill = wb["skill"]
    clear_and_headers(wb_skill, [
        ["##var", "id", "termId", "descOverride", "subSkills"],
        ["##comment", "技能ID", "关联术语ID(可空)", "描述覆盖(空=由子技能自动拼接)", "有序引用的子技能ID列表(| 分隔)"],
        ["##type", "string", "string", "string", "string"],
    ])
    row = 4
    for sid, (sterm, soverride, _comps) in merged_skills.items():
        wb_skill.cell(row=row, column=2, value=sid)
        wb_skill.cell(row=row, column=3, value=sterm)
        wb_skill.cell(row=row, column=4, value=soverride)
        wb_skill.cell(row=row, column=5, value="|".join(skill_subs[sid]))
        row += 1

    # 移除已合并/废弃的旧 sheet。
    for old in ("skill_component", "skill_rule"):
        if old in wb.sheetnames:
            del wb[old]

    # ---------------- cake_layer_buff ----------------
    wb_clb = get_or_create_sheet(wb, "cake_layer_buff")
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
    comp_count = sum(len(v[2]) for v in merged_skills.values())
    print(f"dish.xlsx rebuilt: {len(DISHES)} dishes, {len(merged_skills)} skills (一菜一skill), "
          f"{comp_count} components -> {len(concrete_subs)} distinct sub-skills "
          f"(from {len(SUB_SKILLS)} templates), {len(CAKE_LAYER_BUFFS)} cake-layer buffs")

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
