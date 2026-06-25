#!/usr/bin/env python3
"""生成「行动轴」相关 xlsx 表，并扩展 event.xlsx。

新表/工作簿：
- action.xlsx         : 行动池、行动组、行动组成员、整局行动日程规则。
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
    ("id", "string", "行动ID"), ("name", "string", "行动名称"), ("desc", "string", "行动描述"),
    ("actionType", "ActionType", "行动类型"), ("costDays", "int", "默认消耗天数"), ("weight", "float", "随机权重"),
    ("repeatable", "bool", "是否可重复"), ("preconditions", "string", "前置条件"),
    ("payloadType", "string", "载荷类型"), ("payloadValue", "float", "载荷数值"), ("payloadParam", "string", "载荷参数"),
    ("linkId", "string", "关联配置ID"), ("foodDifficulty", "string", "美食难度(Normal/Hard/Boss)"), ("rewardKind", "RewardKind", "美食奖励外观类型"),
    ("rewardPackageId", "string", "美食奖励包ID"), ("goldCurveId", "string", "金币曲线ID"), ("hiddenScoreBonus", "int", "行动隐藏分加成"),
]
ACTIONS = [
    # 美食挑战：payloadValue 保留为目标分倍率兼容字段；v3 主要读取难度、奖励包、金币曲线和隐藏分加成。
    ("act_food_gold", "街边小吃", "普通美食挑战，主要奖励金币。", "Food", 1, 120, "true", "", "", 1.0, "", "", "Normal", "Gold", "reward_food_gold", "gold_normal", 0),
    ("act_food_fragment", "胃口扩张", "普通美食挑战，主要奖励胃部碎片。", "Food", 2, 90, "true", "", "", 1.0, "", "", "Normal", "FragmentChoice", "reward_food_fragment", "gold_normal", 2),
    ("act_food_passive", "招牌菜试炼", "普通美食挑战，主要奖励被动道具。", "Food", 2, 85, "true", "", "", 1.0, "", "", "Normal", "PassiveItemChoice", "reward_food_passive", "gold_normal", 2),
    ("act_food_active", "秘制小物", "普通美食挑战，主要奖励主动道具。", "Food", 1, 75, "true", "", "", 1.0, "", "", "Normal", "ActiveItemGrant", "reward_food_active", "gold_normal", 1),
    ("act_food_dish", "新菜试做", "普通美食挑战，主要奖励菜品。", "Food", 2, 100, "true", "", "", 1.0, "", "", "Normal", "DishChoice", "reward_food_dish", "gold_normal", 1),
    ("act_food_hard_gold", "重口硬菜", "更难美食挑战，主要奖励金币。", "Food", 3, 70, "true", "", "", 1.2, "", "", "Hard", "Gold", "reward_food_hard_gold", "gold_hard", 7),
    ("act_food_hard_fragment", "硬菜扩胃", "更难美食挑战，主要奖励胃部碎片。", "Food", 3, 65, "true", "", "", 1.25, "", "", "Hard", "FragmentChoice", "reward_food_hard_fragment", "gold_hard", 8),
    ("act_food_hard_passive", "名厨考验", "更难美食挑战，主要奖励被动道具。", "Food", 3, 60, "true", "", "", 1.3, "", "", "Hard", "PassiveItemChoice", "reward_food_hard_passive", "gold_hard", 9),
    ("act_food_hard_active", "险中取巧", "更难美食挑战，主要奖励主动道具。", "Food", 2, 55, "true", "", "", 1.25, "", "", "Hard", "ActiveItemGrant", "reward_food_hard_active", "gold_hard", 7),
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
    ("id", "string", "行动组ID。"),
    ("name", "string", "行动组显示名。"),
    ("desc", "string", "行动组描述。"),
    ("groupType", "string", "行动组分类：food_normal/food_hard/event/reward 等。"),
    ("weekFilter", "string", "周筛选：空=任意，normal=非Boss周，boss=Boss周，或逗号分隔周号。"),
    ("weight", "float", "普通空位填充时的行动组权重。"),
    ("preconditions", "string", "行动组前置条件。"),
]
ACTION_GROUPS = [
    ("grp_food_normal", "普通美食组", "普通美食挑战，奖励外观混合金币、碎片、道具与菜品。", "food_normal", "normal", 100, ""),
    ("grp_food_hard", "困难美食组", "更难美食挑战，目标更高、奖励更厚。", "food_hard", "", 55, ""),
    ("grp_event_food", "事件美食组", "事件与美食穿插，调节局外节奏。", "event", "normal", 45, ""),
    ("grp_reward", "纯奖励组", "直接奖励、商店或低风险收益。", "reward", "", 30, ""),
]

ACTION_GROUP_MEMBER_FIELDS = [
    ("id", "string", "行动组成员ID。"),
    ("groupId", "string", "所属行动组ID。"),
    ("actionId", "string", "行动ID。"),
    ("weight", "float", "组内随机权重。"),
    ("minCostDays", "int", "本次行动最小耗时；<=0 使用 action.costDays。"),
    ("maxCostDays", "int", "本次行动最大耗时；<=0 使用 action.costDays。"),
]
ACTION_GROUP_MEMBERS = [
    ("agm_normal_gold", "grp_food_normal", "act_food_gold", 120, 1, 1),
    ("agm_normal_fragment", "grp_food_normal", "act_food_fragment", 90, 1, 2),
    ("agm_normal_passive", "grp_food_normal", "act_food_passive", 85, 1, 2),
    ("agm_normal_active", "grp_food_normal", "act_food_active", 75, 1, 2),
    ("agm_normal_dish", "grp_food_normal", "act_food_dish", 100, 1, 2),
    ("agm_hard_gold", "grp_food_hard", "act_food_hard_gold", 70, 2, 3),
    ("agm_hard_fragment", "grp_food_hard", "act_food_hard_fragment", 65, 2, 3),
    ("agm_hard_passive", "grp_food_hard", "act_food_hard_passive", 60, 2, 3),
    ("agm_hard_active", "grp_food_hard", "act_food_hard_active", 55, 2, 3),
    ("agm_hard_dish", "grp_food_hard", "act_food_hard_dish", 70, 2, 3),
    ("agm_event_market", "grp_event_food", "act_market", 60, 1, 1),
    ("agm_event_tasting", "grp_event_food", "act_tasting", 50, 1, 2),
    ("agm_event_rest", "grp_event_food", "act_rest", 40, 1, 1),
    ("agm_event_recruit", "grp_event_food", "act_recruit", 35, 2, 2),
    ("agm_event_study", "grp_event_food", "act_study", 30, 1, 2),
    ("agm_reward_tip", "grp_reward", "act_tip", 90, 1, 1),
    ("agm_reward_shop", "grp_reward", "act_shop", 70, 1, 1),
]

ACTION_SCHEDULE_RULE_FIELDS = [
    ("id", "string", "日程规则ID。"),
    ("priority", "int", "优先级，数值越大越先填充。"),
    ("groupIds", "string", "候选行动组ID，多个用 | 或逗号分隔。"),
    ("minRunStep", "int", "整局行动序号窗口起点，1-based。"),
    ("maxRunStep", "int", "整局行动序号窗口终点，含。"),
    ("minCount", "int", "该窗口内至少出现次数。"),
    ("maxCount", "int", "该窗口内最多出现次数。"),
    ("weight", "float", "同优先规则之间的权重。"),
    ("preconditions", "string", "规则前置条件。"),
]
ACTION_SCHEDULE_RULES = [
    ("rule_reward_early", 100, "grp_reward", 3, 6, 1, 1, 100, ""),
    ("rule_reward_mid", 95, "grp_reward", 10, 12, 1, 1, 100, ""),
    ("rule_event_opening", 80, "grp_event_food", 2, 8, 1, 2, 100, ""),
]

HIDDEN_SCORE_CURVE_FIELDS = [
    ("id", "string", "隐藏分曲线ID。"),
    ("purpose", "string", "曲线用途：Base/TargetScore/Dish/PassiveItem/ActiveItem/Fragment。"),
    ("segmentPriority", "int", "分段优先级，数值越大越优先。"),
    ("minWeek", "int", "适用最小周数；<=0 表示不限。"),
    ("maxWeek", "int", "适用最大周数；<=0 表示不限。"),
    ("minRunStep", "int", "适用最小整局行动序号；<=0 表示不限。"),
    ("maxRunStep", "int", "适用最大整局行动序号；<=0 表示不限。"),
    ("baseValue", "int", "基础值。"),
    ("baseMultiplier", "float", "基础倍率。"),
    ("perWeek", "float", "每周递增值。"),
    ("perDay", "float", "每推进一天递增值。"),
    ("perStep", "float", "每执行一次行动递增值。"),
    ("normalBonus", "int", "普通美食难度加成。"),
    ("hardBonus", "int", "困难美食难度加成。"),
    ("bossBonus", "int", "Boss难度加成。"),
    ("itemBonusMultiplier", "float", "道具隐藏分派生倍率。"),
    ("roundTo", "int", "向上取整粒度。"),
    ("minValue", "int", "最小值。"),
]
HIDDEN_SCORE_CURVES = [
    ("hidden_base_early", "Base", 10, 1, 3, 0, 12, 0, 0, 7, 1, 2, 0, 8, 16, 1, 1, 0),
    ("hidden_base_late", "Base", 20, 4, 0, 0, 0, 8, 0, 9, 1.2, 2.5, 0, 10, 20, 1, 1, 0),
    ("hidden_target_score_early", "TargetScore", 10, 1, 3, 0, 12, 80, 8, 10, 2, 4, 0, 30, 80, 0, 5, 30),
    ("hidden_target_score_late", "TargetScore", 20, 4, 0, 0, 0, 110, 9, 14, 3, 5, 0, 40, 100, 0, 5, 40),
    ("hidden_dish", "Dish", 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 4, 8, 0, 1, 0),
    ("hidden_passive_item", "PassiveItem", 0, 0, 0, 0, 0, 2, 1, 0, 0, 0, 0, 5, 10, 0, 1, 0),
    ("hidden_active_item", "ActiveItem", 0, 0, 0, 0, 0, 0, 0.75, 0, 0, 0, 0, 2, 4, 0, 1, 0),
    ("hidden_fragment", "Fragment", 0, 0, 0, 0, 0, 3, 1, 0, 0, 0, 0, 6, 12, 0, 1, 0),
]


GOLD_REWARD_CURVE_FIELDS = [
    ("id", "string", "金币奖励曲线ID。"),
    ("minBase", "int", "金币下限基础值。"),
    ("maxBase", "int", "金币上限基础值。"),
    ("minPerWeek", "float", "金币下限每周递增值。"),
    ("maxPerWeek", "float", "金币上限每周递增值。"),
    ("minPerDay", "float", "金币下限每行动轴天数递增值。"),
    ("maxPerDay", "float", "金币上限每行动轴天数递增值。"),
    ("normalMinBonus", "int", "普通难度金币下限加成。"),
    ("normalMaxBonus", "int", "普通难度金币上限加成。"),
    ("hardMinBonus", "int", "困难难度金币下限加成。"),
    ("hardMaxBonus", "int", "困难难度金币上限加成。"),
    ("bossMinBonus", "int", "Boss难度金币下限加成。"),
    ("bossMaxBonus", "int", "Boss难度金币上限加成。"),
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
    ("reward_food_active", 24, 40, "main_active", "extra_mixed", 0.15, 35),
    ("reward_food_fragment", 32, 52, "main_fragment", "extra_mixed", 0.18, 45),
    ("reward_food_hard_gold", 40, 66, "main_gold", "extra_mixed", 0.25, 50),
    ("reward_food_hard_dish", 46, 72, "main_dish", "extra_mixed", 0.28, 55),
    ("reward_food_hard_passive", 50, 78, "main_passive", "extra_mixed", 0.3, 60),
    ("reward_food_hard_active", 42, 68, "main_active", "extra_mixed", 0.25, 55),
    ("reward_food_hard_fragment", 52, 82, "main_fragment", "extra_mixed", 0.3, 65),
]


TIMELINE_FIELDS = [
    ("id", "string", "行动轴模板 id；运行态只保存该 id，读档时按它重建节点。"),
    ("weekFilter", "string", "周筛选：空=任意，normal=非 Boss 周，boss=Boss 周，也可填逗号分隔周号。"),
    ("weight", "float", "同一周筛选命中的行动轴之间按该权重随机。"),
    ("baseLengthDays", "int", "行动轴基础长度；当前主循环按7天时间轴推进，行动只移动天数游标。"),
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
    ("id", "string", "Boss配置ID。"),
    ("name", "string", "Boss显示名称。"),
    ("characterPool", "string", "Boss自身角色池，空=任意角色；逗号分隔 character.id。"),
    ("unlockCondition", "string", "解锁条件，空=默认解锁。"),
    ("weight", "float", "同一候选池内按该权重随机。"),
    ("week", "int", "限定周序号；0=任意Boss周。"),
    ("scoreProfileId", "string", "Boss目标分曲线；空=使用当前周目标分曲线。"),
    ("modifier", "string", "Boss特殊机制标识。"),
]
BOSSES = [
    ("boss_glutton", "大胃王挑战", "", "", 100, 0, "", "limit_serve"),
    ("boss_iron", "铁胃霸主", "", "", 80, 0, "", "small_board"),
    ("boss_final", "终极美食家", "", "", 100, 8, "", "small_board"),
]


# —— event_option.xlsx ——
OPTION_FIELDS = [
    ("id", "string", "事件选项ID。"),
    ("eventId", "string", "所属事件ID。"),
    ("text", "string", "选项显示文本。"),
    ("resultType", "string", "选项结果类型。"),
    ("resultValue", "float", "选项结果数值。"),
    ("resultParam", "string", "选项结果参数。"),
]
OPTIONS = [
    ("opt_market_cheap", "ev_market", "省着点逛（+20 金币）", "GainGold", 20, ""),
    ("opt_market_spree", "ev_market", "大手笔扫货（+55 金币）", "GainGold", 55, ""),
    ("opt_gamble_small", "ev_gamble", "小赌怡情", "Gamble", 30, ""),
    ("opt_gamble_big", "ev_gamble", "豪赌一把", "Gamble", 90, ""),
]


# —— event.xlsx（重写，追加 category/repeatable/preconditions）——
EVENT_FIELDS = [
    ("id", "string", "事件ID。"),
    ("name", "string", "事件名称。"),
    ("desc", "string", "事件描述。"),
    ("timeCost", "int", "事件默认耗时；事件行动仍以行动costDays推进。"),
    ("effectType", "string", "无选项事件的直接效果类型。"),
    ("effectValue", "float", "无选项事件的直接效果数值。"),
    ("category", "string", "事件分类，用于后续事件池扩展。"),
    ("weight", "float", "事件节点随机权重；<=0时运行时按1处理。"),
    ("repeatable", "bool", "是否可重复触发；false命中后写入UsedEventIds。"),
    ("preconditions", "string", "前置条件表达式，空=无条件。"),
]
EVENTS = [
    ("ev_market", "集市采购", "花一点时间逛集市，获得金币。", 1, "GainGold", 30, "reward", 80, "true", ""),
    ("ev_tasting", "试菜会", "举办试菜会，向菜谱加入一道新菜。", 1, "AddDish", 1, "food", 60, "true", ""),
    ("ev_rest", "歇业一天", "短暂休息，降低下一关要求分。", 1, "LowerReq", 0.1, "reward", 45, "true", ""),
    ("ev_recruit", "招募帮厨", "招募帮厨，获得一件被动道具。", 2, "GainItem", 1, "reward", 35, "false", ""),
    ("ev_gamble", "豪赌一桌", "高风险高回报：可能大赚也可能亏本。", 1, "Gamble", 50, "negative", 30, "true", ""),
    ("ev_upgrade", "钻研食谱", "钻研食谱，提升一道菜的美味度。", 2, "UpgradeDish", 5, "food", 50, "true", ""),
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
