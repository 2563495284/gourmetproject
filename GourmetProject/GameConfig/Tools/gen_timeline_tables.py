#!/usr/bin/env python3
"""生成「行动轴」相关 xlsx 表，并扩展 event.xlsx。

新表：
- action.xlsx        : 行动（三选一）。
- timeline.xlsx      : 行动轴库。
- timeline_node.xlsx : 行动轴节点（同 timelineId 多行）。
- boss.xlsx          : Boss 池。
- event_option.xlsx  : 事件选项（同 eventId 多行）。
重写：
- event.xlsx         : 追加 category / repeatable / preconditions 三列。

所有表均为标量字段（无多元素单格 list），用 ##var/##type + 数据行（首列留空）即可。
bool 写 true/false；枚举写名字字符串。

用法：python3 GameConfig/Tools/gen_timeline_tables.py
依赖：openpyxl。
"""
import os

from openpyxl import Workbook

HERE = os.path.dirname(os.path.abspath(__file__))
DATAS = os.path.normpath(os.path.join(HERE, "..", "Datas"))


def write_table(name, fields, rows):
    """fields: [(col, type)]; rows: [ [v0, v1, ...], ... ]（不含首列标记列）。"""
    wb = Workbook()
    ws = wb.active
    ws.title = name
    ws.append(["##var"] + [c for c, _t in fields])
    ws.append(["##type"] + [t for _c, t in fields])
    for row in rows:
        ws.append([""] + list(row))
    wb.save(os.path.join(DATAS, name + ".xlsx"))
    print(f"  {name}.xlsx ({len(rows)} 行)")


# —— action.xlsx ——
ACTION_FIELDS = [
    ("id", "string"), ("name", "string"), ("desc", "string"),
    ("actionType", "ActionType"), ("costDays", "int"), ("weight", "float"),
    ("repeatable", "bool"), ("preconditions", "string"),
    ("payloadType", "string"), ("payloadValue", "float"), ("payloadParam", "string"),
    ("linkId", "string"),
]
ACTIONS = [
    # 美食挑战：核心战斗行动。payloadValue=目标分倍率(<=0 视为 1)；linkId 空=用当前周曲线，否则指向 TbScoreProfile。
    ("act_cook", "开火做菜", "进行一场美食挑战，达标得奖励。", "Food", 2, 120, "true", "", "", 1.0, "", ""),
    ("act_feast", "大宴宾客", "更耗时的硬仗，目标更高、奖励更厚。", "Food", 3, 70, "true", "", "", 1.5, "", ""),
    # 事件行动：指向 TbEvent
    ("act_market", "逛集市", "花点时间逛集市采购。", "Event", 1, 60, "true", "", "", 0, "", "ev_market"),
    ("act_tasting", "办试菜会", "举办试菜会，给菜谱加菜。", "Event", 1, 50, "true", "", "", 0, "", "ev_tasting"),
    ("act_rest", "歇业一天", "短暂休息，降低下一关要求分。", "Event", 1, 40, "true", "", "", 0, "", "ev_rest"),
    ("act_recruit", "招募帮厨", "招募帮厨，获得一件被动道具。", "Event", 2, 35, "false", "", "", 0, "", "ev_recruit"),
    ("act_study", "钻研食谱", "钻研食谱，提升一道菜。", "Event", 2, 30, "true", "", "", 0, "", "ev_upgrade"),
    # 负面行动：直接结算 payload
    ("act_gamble", "豪赌一桌", "高风险高回报，可能大赚也可能亏本。", "Negative", 1, 30, "true", "", "Gamble", 50, "", ""),
    # 奖励行动：直接结算 payload
    ("act_tip", "收小费", "客人很满意，留下一笔小费。", "Reward", 1, 25, "true", "", "GainGold", 25, "", ""),
    # 商店行动：打开商店
    ("act_shop", "逛逛商店", "去食材店转转，买卖道具。", "Shop", 1, 40, "true", "", "", 0, "", ""),
]


# —— timeline.xlsx ——
TIMELINE_FIELDS = [
    ("id", "string"), ("weekFilter", "string"), ("weight", "float"), ("baseLengthDays", "int"),
]
TIMELINES = [
    ("tl_normal", "normal", 100, 7),
    ("tl_busy", "normal", 60, 7),
    ("tl_boss", "boss", 100, 7),
]


# —— timeline_node.xlsx ——
NODE_FIELDS = [
    ("id", "string"), ("timelineId", "string"), ("day", "int"),
    ("nodeType", "TimelineNodeType"), ("payloadValue", "float"), ("payloadParam", "string"),
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
    write_table("action", ACTION_FIELDS, ACTIONS)
    write_table("timeline", TIMELINE_FIELDS, TIMELINES)
    write_table("timeline_node", NODE_FIELDS, NODES)
    write_table("boss", BOSS_FIELDS, BOSSES)
    write_table("event_option", OPTION_FIELDS, OPTIONS)
    write_table("event", EVENT_FIELDS, EVENTS)
    print("生成完成。请运行 bash GameConfig/gen.sh。")


if __name__ == "__main__":
    main()
