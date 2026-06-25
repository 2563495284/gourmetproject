#!/usr/bin/env python3
"""生成「行动轴」相关 xlsx 表，并扩展 event.xlsx。

新表/工作簿：
- action.xlsx         : 行动、v2 行动组合、行动组合成员、行动序列约束。
- reward_curve.xlsx   : v2 隐藏分曲线、金币曲线。
- timeline.xlsx       : 周配置、目标分曲线、奖励包、行动轴库、行动轴节点。
- boss.xlsx                 : Boss 池。
重写：
- event.xlsx         : 事件、事件选项；事件追加 category / repeatable / preconditions 三列。

所有表均为标量字段（无多元素单格 list），用 ##var/##type + 数据行（首列留空）即可。
bool 写 true/false；枚举写名字字符串。

用法：python3 GameConfig/Tools/gen_timeline_tables.py
依赖：openpyxl。
"""
import os

from openpyxl import Workbook

HERE = os.path.dirname(os.path.abspath(__file__))
DATAS = os.path.normpath(os.path.join(HERE, "..", "Datas"))


def field_names(fields):
    return [field[0] for field in fields]


def field_types(fields):
    return [field[1] for field in fields]


def field_comments(fields):
    return [field[2] if len(field) > 2 else "" for field in fields]


def has_comments(fields):
    return any(len(field) > 2 and field[2] for field in fields)


def write_table(name, fields, rows):
    """fields: [(col, type[, comment])]; rows: [ [v0, v1, ...], ... ]（不含首列标记列）。"""
    wb = Workbook()
    ws = wb.active
    ws.title = name
    ws.append(["##var"] + field_names(fields))
    if has_comments(fields):
        ws.append(["##comment"] + field_comments(fields))
    ws.append(["##type"] + field_types(fields))
    for row in rows:
        ws.append([""] + list(row))
    wb.save(os.path.join(DATAS, name + ".xlsx"))
    print(f"  {name}.xlsx ({len(rows)} 行)")


def fill_sheet(ws, fields, rows):
    ws.append(["##var"] + field_names(fields))
    if has_comments(fields):
        ws.append(["##comment"] + field_comments(fields))
    ws.append(["##type"] + field_types(fields))
    for row in rows:
        ws.append([""] + list(row))


def write_workbook(filename, sheets):
    """sheets: [(sheet_name, fields, rows)]。"""
    wb = Workbook()
    for i, (sheet_name, fields, rows) in enumerate(sheets):
        ws = wb.active if i == 0 else wb.create_sheet()
        ws.title = sheet_name
        fill_sheet(ws, fields, rows)
    wb.save(os.path.join(DATAS, filename + ".xlsx"))
    print(f"  {filename}.xlsx ({len(sheets)} sheets)")


# —— action.xlsx ——
ACTION_FIELDS = [
    ("id", "string"), ("name", "string"), ("desc", "string"),
    ("actionType", "ActionType"), ("costDays", "int"), ("weight", "float"),
    ("repeatable", "bool"), ("preconditions", "string"),
    ("payloadType", "string"), ("payloadValue", "float"), ("payloadParam", "string"),
    ("linkId", "string"), ("foodDifficulty", "string"), ("rewardKind", "RewardKind"),
    ("rewardPackageId", "string"), ("goldCurveId", "string"), ("hiddenScoreBonus", "int"),
]
ACTIONS = [
    # 美食挑战：payloadValue 保留为目标分倍率兼容字段；v2 主要读取难度、奖励包、金币曲线和隐藏分加成。
    ("act_food_gold", "街边小吃", "普通美食挑战，主要奖励金币。", "Food", 1, 120, "true", "", "", 1.0, "", "", "Normal", "Gold", "reward_food_gold", "gold_normal", 0),
    ("act_food_fragment", "胃口扩张", "普通美食挑战，主要奖励胃部碎片。", "Food", 2, 90, "true", "", "", 1.0, "", "", "Normal", "FragmentChoice", "reward_food_fragment", "gold_normal", 2),
    ("act_food_passive", "招牌菜试炼", "普通美食挑战，主要奖励被动道具。", "Food", 2, 85, "true", "", "", 1.0, "", "", "Normal", "PassiveItemChoice", "reward_food_passive", "gold_normal", 2),
    ("act_food_dish", "新菜试做", "普通美食挑战，主要奖励菜品。", "Food", 2, 100, "true", "", "", 1.0, "", "", "Normal", "DishChoice", "reward_food_dish", "gold_normal", 1),
    ("act_food_hard_fragment", "硬菜扩胃", "更难美食挑战，主要奖励胃部碎片。", "Food", 3, 65, "true", "", "", 1.25, "", "", "Hard", "FragmentChoice", "reward_food_hard_fragment", "gold_hard", 8),
    ("act_food_hard_passive", "名厨考验", "更难美食挑战，主要奖励被动道具。", "Food", 3, 60, "true", "", "", 1.3, "", "", "Hard", "PassiveItemChoice", "reward_food_hard_passive", "gold_hard", 9),
    ("act_food_hard_dish", "稀有菜谱", "更难美食挑战，主要奖励菜品。", "Food", 3, 70, "true", "", "", 1.25, "", "", "Hard", "DishChoice", "reward_food_hard_dish", "gold_hard", 8),
    ("act_cook", "开火做菜", "兼容旧配置：普通美食挑战，达标得奖励。", "Food", 2, 80, "true", "", "", 1.0, "", "", "Normal", "DishChoice", "reward_food_dish", "gold_normal", 0),
    ("act_feast", "大宴宾客", "兼容旧配置：更耗时的硬仗，目标更高、奖励更厚。", "Food", 3, 55, "true", "", "", 1.5, "", "", "Hard", "PassiveItemChoice", "reward_food_hard_passive", "gold_hard", 10),
    # 事件行动：指向 TbEvent
    ("act_market", "逛集市", "花点时间逛集市采购。", "Event", 1, 60, "true", "", "", 0, "", "ev_market", "Normal", "Gold", "", "", 0),
    ("act_tasting", "办试菜会", "举办试菜会，给菜谱加菜。", "Event", 1, 50, "true", "", "", 0, "", "ev_tasting", "Normal", "DishChoice", "", "", 0),
    ("act_rest", "歇业一天", "短暂休息，降低下一关要求分。", "Event", 1, 40, "true", "", "", 0, "", "ev_rest", "Normal", "Gold", "", "", 0),
    ("act_recruit", "招募帮厨", "招募帮厨，获得一件被动道具。", "Event", 2, 35, "false", "", "", 0, "", "ev_recruit", "Normal", "PassiveItemChoice", "", "", 0),
    ("act_study", "钻研食谱", "钻研食谱，提升一道菜。", "Event", 2, 30, "true", "", "", 0, "", "ev_upgrade", "Normal", "DishChoice", "", "", 0),
    # 负面行动：直接结算 payload
    ("act_gamble", "豪赌一桌", "高风险高回报，可能大赚也可能亏本。", "Negative", 1, 30, "true", "", "Gamble", 50, "", "", "Normal", "Gold", "", "", 0),
    # 奖励行动：直接结算 payload
    ("act_tip", "收小费", "客人很满意，留下一笔小费。", "Reward", 1, 25, "true", "", "GainGold", 25, "", "", "Normal", "Gold", "", "", 0),
    # 商店行动：打开商店
    ("act_shop", "逛逛商店", "去食材店转转，买卖道具。", "Shop", 1, 40, "true", "", "", 0, "", "", "Normal", "Gold", "", "", 0),
]


ACTION_GROUP_FIELDS = [
    ("id", "string"), ("name", "string"), ("weight", "float"), ("weekFilter", "string"), ("preventRepeat", "bool"),
]
ACTION_GROUPS = [
    ("grp_food_normal", "普通美食组", 100, "normal", "true"),
    ("grp_food_hard", "普通更难美食组", 65, "", "true"),
    ("grp_event_food", "事件美食组", 55, "", "true"),
    ("grp_reward", "纯奖励组", 35, "", "true"),
    ("grp_boss_food", "Boss美食组", 100, "boss", "true"),
]


ACTION_GROUP_MEMBER_FIELDS = [
    ("id", "string"), ("groupId", "string"), ("actionId", "string"),
    ("costDaysMin", "int"), ("costDaysMax", "int"), ("weight", "float"),
]
ACTION_GROUP_MEMBERS = [
    ("gm_normal_gold", "grp_food_normal", "act_food_gold", 1, 1, 100),
    ("gm_normal_fragment", "grp_food_normal", "act_food_fragment", 1, 2, 85),
    ("gm_normal_passive", "grp_food_normal", "act_food_passive", 1, 2, 75),
    ("gm_hard_gold", "grp_food_hard", "act_food_gold", 1, 1, 90),
    ("gm_hard_fragment", "grp_food_hard", "act_food_hard_fragment", 2, 3, 80),
    ("gm_hard_passive", "grp_food_hard", "act_food_hard_passive", 2, 3, 75),
    ("gm_event_market", "grp_event_food", "act_market", 1, 1, 70),
    ("gm_event_food", "grp_event_food", "act_food_passive", 1, 2, 100),
    ("gm_reward_tip", "grp_reward", "act_tip", 1, 1, 100),
    ("gm_boss_food", "grp_boss_food", "act_food_hard_passive", 2, 3, 100),
    ("gm_boss_fragment", "grp_boss_food", "act_food_hard_fragment", 2, 3, 90),
]


ACTION_SCHEDULE_RULE_FIELDS = [
    ("id", "string"), ("priority", "int"), ("ruleType", "string"), ("groupId", "string"),
    ("startIndex", "int"), ("endIndex", "int"), ("windowSize", "int"), ("windowStart", "int"),
    ("windowEnd", "int"), ("minCount", "int"), ("maxCount", "int"), ("weekFilter", "string"),
]
ACTION_SCHEDULE_RULES = [
    ("rule_reward_early", 10, "RangeCount", "grp_reward", 3, 6, 0, 0, 0, 1, 1, ""),
    ("rule_reward_late", 20, "RangeCount", "grp_reward", 10, 12, 0, 0, 0, 1, 1, ""),
    ("rule_event_decade", 30, "WindowCount", "grp_event_food", 0, 0, 10, 2, 8, 1, 2, ""),
    ("rule_boss_group", 5, "RangeCount", "grp_boss_food", 11, 12, 0, 0, 0, 1, 1, "boss"),
]


HIDDEN_SCORE_CURVE_FIELDS = [
    ("id", "string"), ("purpose", "string"), ("baseValue", "int"), ("baseMultiplier", "float"),
    ("perWeek", "float"), ("perDay", "float"), ("perStep", "float"),
    ("normalBonus", "int"), ("hardBonus", "int"), ("bossBonus", "int"),
    ("itemBonusMultiplier", "float"), ("roundTo", "int"), ("minValue", "int"),
]
HIDDEN_SCORE_CURVES = [
    ("hidden_base", "Base", 0, 0, 7, 1, 2, 0, 8, 16, 1, 1, 0),
    ("hidden_target_score", "TargetScore", 80, 8, 10, 2, 4, 0, 30, 80, 0, 5, 30),
    ("hidden_dish", "Dish", 0, 1, 0, 0, 0, 0, 4, 8, 0, 1, 0),
    ("hidden_passive_item", "PassiveItem", 2, 1, 0, 0, 0, 0, 5, 10, 0, 1, 0),
    ("hidden_active_item", "ActiveItem", 0, 0.75, 0, 0, 0, 0, 2, 4, 0, 1, 0),
    ("hidden_fragment", "Fragment", 3, 1, 0, 0, 0, 0, 6, 12, 0, 1, 0),
]


GOLD_REWARD_CURVE_FIELDS = [
    ("id", "string"), ("minBase", "int"), ("maxBase", "int"),
    ("minPerWeek", "float"), ("maxPerWeek", "float"), ("minPerDay", "float"), ("maxPerDay", "float"),
    ("normalMinBonus", "int"), ("normalMaxBonus", "int"),
    ("hardMinBonus", "int"), ("hardMaxBonus", "int"),
    ("bossMinBonus", "int"), ("bossMaxBonus", "int"),
]
GOLD_REWARD_CURVES = [
    ("gold_normal", 18, 30, 4, 6, 0.5, 1, 0, 0, 10, 16, 30, 45),
    ("gold_hard", 26, 44, 5, 8, 0.5, 1, 0, 0, 14, 24, 36, 55),
    ("gold_boss", 60, 95, 8, 12, 1, 2, 0, 0, 0, 0, 45, 70),
    ("gold_fragment_convert", 22, 42, 5, 7, 0.5, 1, 0, 0, 12, 20, 35, 55),
]


# —— timeline.xlsx ——
WEEK_FIELDS = [
    ("id", "int", "周序号，也是 GameRun.WeekIndex；按 id 顺序决定总周数。"),
    ("scoreProfileId", "string", "本周基础目标分曲线；普通美食与 Boss 目标分都会以它为默认来源。"),
    ("rewardPackageId", "string", "本周默认过关奖励包；行动配置有 rewardPackageId 时优先使用行动奖励包。"),
    ("rewardHiddenScore", "int", "本周默认奖励隐藏分；行动派生隐藏分大于 0 时优先使用行动隐藏分。"),
    ("isBoss", "bool", "是否 Boss 周；会匹配 weekFilter=boss 的行动轴和行动组。"),
    ("modifier", "string", "周级修饰符标识；当前主要用于表现/后续扩展，具体战斗仍可由行动或 Boss 单独传入 modifier。"),
]
WEEKS = [
    (1, "score_w1", "reward_w1", 8, "false", ""),
    (2, "score_w2", "reward_w2", 14, "false", ""),
    (3, "score_w3", "reward_w3", 22, "false", ""),
    (4, "score_w4", "reward_w4_boss", 30, "true", "limit_serve"),
    (5, "score_w5", "reward_w5", 36, "false", ""),
    (6, "score_w6", "reward_w6", 44, "false", ""),
    (7, "score_w7", "reward_w7", 52, "false", ""),
    (8, "score_w8", "reward_w8_boss", 60, "true", "small_board"),
]


SCORE_PROFILE_FIELDS = [
    ("id", "string", "目标分曲线 id；Week.scoreProfileId 和 Boss.scoreProfileId 引用它。"),
    ("baseScore", "int", "基础目标分。"),
    ("difficultyMul", "float", "普通难度倍率；当前周基础目标分先乘该倍率。"),
    ("bossMul", "float", "Boss 目标分倍率；Boss 节点结算时额外应用。"),
    ("endlessGrowthMul", "float", "无尽模式超出配置周数后的指数增长倍率。"),
    ("roundTo", "int", "目标分向上取整粒度，例如 10 表示取整到 10 的倍数。"),
]
SCORE_PROFILES = [
    ("score_w1", 80, 1, 1, 1.5, 10),
    ("score_w2", 130, 1, 1, 1.5, 10),
    ("score_w3", 200, 1, 1, 1.5, 10),
    ("score_w4", 320, 1, 1, 1.5, 10),
    ("score_w5", 480, 1, 1, 1.5, 10),
    ("score_w6", 700, 1, 1, 1.5, 10),
    ("score_w7", 1000, 1, 1, 1.5, 10),
    ("score_w8", 1500, 1, 1, 1.5, 10),
]


REWARD_PACKAGE_FIELDS = [
    ("id", "string", "奖励包 id；Week.rewardPackageId 或 Action.rewardPackageId 引用它。"),
    ("goldMin", "int", "基础金币奖励下限；实际奖励会结合隐藏分/行动曲线派生。"),
    ("goldMax", "int", "基础金币奖励上限；实际奖励会结合隐藏分/行动曲线派生。"),
    ("mainSlotGroupId", "string", "主奖励槽组 id，对应 reward.xlsx/reward_slot.groupId。"),
    ("extraSlotGroupId", "string", "额外奖励槽组 id；extraChance 命中时额外抽取。"),
    ("extraChance", "float", "额外奖励出现概率，0..1。"),
    ("fallbackGold", "int", "奖励候选无法生成或溢出时的兜底金币。"),
]
REWARD_PACKAGES = [
    ("reward_w1", 45, 60, "main_dish", "extra_mixed", 0.15, 35),
    ("reward_w2", 55, 75, "main_passive", "extra_mixed", 0.2, 40),
    ("reward_w3", 65, 85, "main_dish", "extra_mixed", 0.25, 45),
    ("reward_w4_boss", 90, 125, "main_boss", "extra_mixed", 0.5, 80),
    ("reward_w5", 85, 115, "main_fragment", "extra_mixed", 0.3, 55),
    ("reward_w6", 95, 130, "main_passive", "extra_mixed", 0.35, 60),
    ("reward_w7", 110, 145, "main_dish_or_passive", "extra_mixed", 0.4, 70),
    ("reward_w8_boss", 140, 185, "main_boss", "extra_mixed", 0.6, 100),
    ("reward_food_gold", 22, 36, "main_gold", "extra_mixed", 0.1, 30),
    ("reward_food_dish", 28, 44, "main_dish", "extra_mixed", 0.15, 35),
    ("reward_food_passive", 30, 48, "main_passive", "extra_mixed", 0.18, 40),
    ("reward_food_fragment", 32, 52, "main_fragment", "extra_mixed", 0.18, 45),
    ("reward_food_hard_dish", 46, 72, "main_dish", "extra_mixed", 0.28, 55),
    ("reward_food_hard_passive", 50, 78, "main_passive", "extra_mixed", 0.3, 60),
    ("reward_food_hard_fragment", 52, 82, "main_fragment", "extra_mixed", 0.3, 65),
]


TIMELINE_FIELDS = [
    ("id", "string", "行动轴模板 id；运行态只保存该 id，读档时按它重建节点。"),
    ("weekFilter", "string", "周筛选：空=任意，normal=非 Boss 周，boss=Boss 周，也可填逗号分隔周号。"),
    ("weight", "float", "同一周筛选命中的行动轴之间按该权重随机。"),
    ("baseLengthDays", "int", "行动轴基础长度；行动日程步数更多时，运行时会扩展本周长度以容纳日程。"),
]
TIMELINES = [
    ("tl_normal", "normal", 100, 7),
    ("tl_busy", "normal", 60, 7),
    ("tl_boss", "boss", 100, 7),
]


# —— timeline_node sheet ——
NODE_FIELDS = [
    ("id", "string", "节点 id；同一节点每周只触发一次，触发记录按 id 保存。"),
    ("timelineId", "string", "所属行动轴模板 id，对应 timeline.id。"),
    ("day", "int", "整天位置；行动从 prevDay 推进到 newDay 时触发 prevDay < day <= newDay 的节点。"),
    ("nodeType", "TimelineNodeType", "节点类型：Boss / Interest / Shop / Event。"),
    ("payloadValue", "float", "节点数值参数；Interest 表示金币阈值 N，其它节点暂未使用。"),
    ("payloadParam", "string", "节点字符串参数；Interest 表示每阈值金币数，Boss 表示 Boss id 池筛选，Event 表示指定事件 id。"),
]
NODES = [
    # tl_normal: 利息(D3, 每满10金币给1) + 商店(D5)
    ("nd_normal_d3", "tl_normal", 3, "Interest", 10, "1"),
    ("nd_normal_d5", "tl_normal", 5, "Shop", 0, ""),
    # tl_busy: 事件(D2) + 利息(D4) + 商店(D6)
    ("nd_busy_d2", "tl_busy", 2, "Event", 0, ""),
    ("nd_busy_d4", "tl_busy", 4, "Interest", 8, "1"),
    ("nd_busy_d6", "tl_busy", 6, "Shop", 0, ""),
    # tl_boss: 利息(D2) + 商店(D4) + Boss(D7)
    ("nd_boss_d2", "tl_boss", 2, "Interest", 10, "1"),
    ("nd_boss_d4", "tl_boss", 4, "Shop", 0, ""),
    ("nd_boss_d7", "tl_boss", 7, "Boss", 0, ""),
]


# —— boss.xlsx ——
BOSS_FIELDS = [
    ("id", "string"), ("name", "string"), ("characterPool", "string"),
    ("unlockCondition", "string"), ("weight", "float"), ("week", "int"),
    ("scoreProfileId", "string"), ("modifier", "string"),
]
BOSSES = [
    ("boss_glutton", "大胃王挑战", "", "", 100, 0, "", "limit_serve"),
    ("boss_iron", "铁胃霸主", "", "", 80, 0, "", "small_board"),
    ("boss_final", "终极美食家", "", "", 100, 8, "", "small_board"),
]


# —— event_option.xlsx ——
OPTION_FIELDS = [
    ("id", "string"), ("eventId", "string"), ("text", "string"),
    ("resultType", "string"), ("resultValue", "float"), ("resultParam", "string"),
]
OPTIONS = [
    ("opt_market_cheap", "ev_market", "省着点逛（+20 金币）", "GainGold", 20, ""),
    ("opt_market_spree", "ev_market", "大手笔扫货（+55 金币）", "GainGold", 55, ""),
    ("opt_gamble_small", "ev_gamble", "小赌怡情", "Gamble", 30, ""),
    ("opt_gamble_big", "ev_gamble", "豪赌一把", "Gamble", 90, ""),
]


# —— event.xlsx（重写，追加 category/repeatable/preconditions）——
EVENT_FIELDS = [
    ("id", "string"), ("name", "string"), ("desc", "string"),
    ("timeCost", "int"), ("effectType", "string"), ("effectValue", "float"),
    ("category", "string"), ("repeatable", "bool"), ("preconditions", "string"),
]
EVENTS = [
    ("ev_market", "集市采购", "花一点时间逛集市，获得金币。", 1, "GainGold", 30, "reward", "true", ""),
    ("ev_tasting", "试菜会", "举办试菜会，向菜谱加入一道新菜。", 1, "AddDish", 1, "food", "true", ""),
    ("ev_rest", "歇业一天", "短暂休息，降低下一关要求分。", 1, "LowerReq", 0.1, "reward", "true", ""),
    ("ev_recruit", "招募帮厨", "招募帮厨，获得一件被动道具。", 2, "GainItem", 1, "reward", "false", ""),
    ("ev_gamble", "豪赌一桌", "高风险高回报：可能大赚也可能亏本。", 1, "Gamble", 50, "negative", "true", ""),
    ("ev_upgrade", "钻研食谱", "钻研食谱，提升一道菜的美味度。", 2, "UpgradeDish", 5, "food", "true", ""),
]


def main():
    print(f"Datas 目录: {DATAS}")
    write_workbook("action", [
        ("action", ACTION_FIELDS, ACTIONS),
        ("action_group", ACTION_GROUP_FIELDS, ACTION_GROUPS),
        ("action_group_member", ACTION_GROUP_MEMBER_FIELDS, ACTION_GROUP_MEMBERS),
        ("action_schedule_rule", ACTION_SCHEDULE_RULE_FIELDS, ACTION_SCHEDULE_RULES),
    ])
    write_workbook("reward_curve", [
        ("hidden_score_curve", HIDDEN_SCORE_CURVE_FIELDS, HIDDEN_SCORE_CURVES),
        ("gold_reward_curve", GOLD_REWARD_CURVE_FIELDS, GOLD_REWARD_CURVES),
    ])
    write_workbook("timeline", [
        ("week", WEEK_FIELDS, WEEKS),
        ("score_profile", SCORE_PROFILE_FIELDS, SCORE_PROFILES),
        ("reward_package", REWARD_PACKAGE_FIELDS, REWARD_PACKAGES),
        ("timeline", TIMELINE_FIELDS, TIMELINES),
        ("timeline_node", NODE_FIELDS, NODES),
    ])
    write_table("boss", BOSS_FIELDS, BOSSES)
    write_workbook("event", [
        ("event", EVENT_FIELDS, EVENTS),
        ("event_option", OPTION_FIELDS, OPTIONS),
    ])
    print("生成完成。请运行 bash GameConfig/gen.sh。")


if __name__ == "__main__":
    main()
