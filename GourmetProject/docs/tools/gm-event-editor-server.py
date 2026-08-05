#!/usr/bin/env python3
"""本地事件编辑器服务：读取/校验/原位写回 GameConfig/Datas/event.xlsx。"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import webbrowser
import zipfile
from copy import deepcopy
from datetime import datetime
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path, PurePosixPath
from typing import Any
from urllib.parse import urlparse
from xml.etree import ElementTree as ET


PROJECT_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_WORKBOOK = PROJECT_ROOT / "GameConfig" / "Datas" / "event.xlsx"
EDITOR_HTML = Path(__file__).with_name("gm-event-editor.html")
GENERATED_EVENT_JSON = PROJECT_ROOT / "Assets" / "StreamingAssets" / "Config" / "tbevent.json"
EFFECT_ENUM_CS = PROJECT_ROOT / "Assets" / "GameMain" / "Scripts" / "Config" / "Gen" / "EffectType.cs"
ACTION_ENUM_CS = PROJECT_ROOT / "Assets" / "GameMain" / "Scripts" / "Config" / "Gen" / "ActionBehavior.cs"

NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
REL_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
PKG_REL_NS = "http://schemas.openxmlformats.org/package/2006/relationships"
XML_NS = "http://www.w3.org/XML/1998/namespace"
N = f"{{{NS}}}"

ET.register_namespace("", NS)
ET.register_namespace("r", REL_NS)


EVENT_COLUMNS = [
    "id", "name", "desc", "eventTypes", "preconditions", "weight", "repeatable",
    "bgSprite", "resultText", "slotRewardGroupId", "slotEmptyWeight", "slotFreeSpins",
    "slotPaidCost", "slotMaxSpins",
]
EVENT_COMMENTS = {
    "id": "事件ID", "name": "事件名称", "desc": "事件描述(初始/根页正文)",
    "eventTypes": "事件分类列表（|分隔；一个事件可属于多个分类）", "preconditions": "出现前置条件",
    "weight": "随机权重", "repeatable": "是否可重复", "bgSprite": "事件背景 Sprite(Resources 路径,空=默认占位)",
    "resultText": "终止型事件结果文本模板", "slotRewardGroupId": "Slot 奖励槽组ID→reward_slot.groupId",
    "slotEmptyWeight": "空奖权重；与奖励槽组权重总和共同计算", "slotFreeSpins": "每个抽奖机节点免费抽奖次数",
    "slotPaidCost": "免费次数用完后的单次金币价格", "slotMaxSpins": "每个抽奖机节点最多抽奖次数",
}
EVENT_TYPES = {
    "id": "string", "name": "string", "desc": "string", "eventTypes": "(list#sep=|),ActionBehavior",
    "preconditions": "string", "weight": "float", "repeatable": "bool", "bgSprite": "string",
    "resultText": "string", "slotRewardGroupId": "string", "slotEmptyWeight": "float",
    "slotFreeSpins": "int", "slotPaidCost": "int", "slotMaxSpins": "int",
}

OPTION_COLUMNS = [
    "id", "eventId", "parentId", "text", "resultText", "condition", "conditionText",
    "*effectTypes", "*effectValues", "*effectParams", "autoEnd",
]
OPTION_COMMENTS = {
    "id": "选项ID", "eventId": "所属事件ID→event.id", "parentId": "父选项ID→event_option.id",
    "text": "选项文本(按钮)", "resultText": "选中后的结果/子页正文",
    "condition": "选项前置条件", "conditionText": "选项前置条件显示文案",
    "*effectTypes": "效果类型列表", "*effectValues": "效果数值列表",
    "*effectParams": "效果参数列表", "autoEnd": "选中并结算效果后立即结束事件，不显示结果确认页",
}
OPTION_TYPES = {
    "id": "string", "eventId": "string", "parentId": "string", "text": "string", "resultText": "string",
    "condition": "string", "conditionText": "string", "*effectTypes": "list,EffectType",
    "*effectValues": "list,float", "*effectParams": "list,string", "autoEnd": "bool",
}

FOLLOW_UP_EFFECTS = {"FoodBattle", "Shop", "GameOver", "Victory"}
LOGIC_REVIEW_EFFECTS = {
    "UiTodo": "该效果仍是 UI 占位，需要先确认并实现交互逻辑",
    "UpgradeDish": "该效果当前会折算成金币，并非真正提升食物，需要确认是否接受占位行为",
}
KNOWN_CONDITIONS = {
    "minGold", "maxGold", "minWeek", "eventCounterReached", "hasItem", "hasRecipeDish",
    "hasFlavoredRecipeDish", "minRecipeDish", "minFlavoredRecipeDish",
}
NO_VALUE_CONDITIONS = {"hasRecipeDish", "hasFlavoredRecipeDish"}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def column_index(reference: str) -> int:
    match = re.match(r"([A-Z]+)", reference or "")
    if not match:
        return 0
    value = 0
    for char in match.group(1):
        value = value * 26 + ord(char) - 64
    return value - 1


def column_name(index: int) -> str:
    value = index + 1
    result = ""
    while value:
        value, remainder = divmod(value - 1, 26)
        result = chr(65 + remainder) + result
    return result


def parse_number(text: str) -> int | float:
    if re.fullmatch(r"[-+]?\d+", text):
        return int(text)
    try:
        return float(text)
    except ValueError:
        return 0


class XlsxDocument:
    def __init__(self, path: Path):
        self.path = path
        with zipfile.ZipFile(path, "r") as archive:
            self.entries = {name: archive.read(name) for name in archive.namelist()}
        self.shared_strings = self._read_shared_strings()
        self.sheet_paths = self._read_sheet_paths()

    def _read_shared_strings(self) -> list[str]:
        raw = self.entries.get("xl/sharedStrings.xml")
        if not raw:
            return []
        root = ET.fromstring(raw)
        return ["".join(node.text or "" for node in item.iter(f"{N}t")) for item in root.findall(f"{N}si")]

    def _read_sheet_paths(self) -> dict[str, str]:
        workbook = ET.fromstring(self.entries["xl/workbook.xml"])
        rels = ET.fromstring(self.entries["xl/_rels/workbook.xml.rels"])
        targets = {
            rel.attrib["Id"]: rel.attrib["Target"]
            for rel in rels.findall(f"{{{PKG_REL_NS}}}Relationship")
        }
        result: dict[str, str] = {}
        for sheet in workbook.findall(f".//{N}sheet"):
            rel_id = sheet.attrib.get(f"{{{REL_NS}}}id", "")
            target = targets.get(rel_id, "")
            if not target:
                continue
            path = str(PurePosixPath("xl") / target) if not target.startswith("xl/") else target
            result[sheet.attrib.get("name", "")] = str(PurePosixPath(path))
        return result

    def rows(self, sheet_name: str) -> tuple[list[list[Any]], ET.Element, str]:
        path = self.sheet_paths[sheet_name]
        root = ET.fromstring(self.entries[path])
        rows: list[list[Any]] = []
        for row in root.findall(f".//{N}sheetData/{N}row"):
            values: dict[int, Any] = {}
            for cell in row.findall(f"{N}c"):
                index = column_index(cell.attrib.get("r", "A1"))
                cell_type = cell.attrib.get("t", "")
                if cell_type == "inlineStr":
                    value: Any = "".join(node.text or "" for node in cell.iter(f"{N}t"))
                else:
                    raw = cell.findtext(f"{N}v")
                    if raw is None:
                        value = ""
                    elif cell_type == "s":
                        shared_index = int(raw)
                        value = self.shared_strings[shared_index] if shared_index < len(self.shared_strings) else ""
                    elif cell_type in {"str", "e"}:
                        value = raw
                    elif cell_type == "b":
                        value = raw == "1"
                    else:
                        value = parse_number(raw)
                values[index] = value
            max_index = max(values.keys(), default=-1)
            rows.append([values.get(i, "") for i in range(max_index + 1)])
        return rows, root, path

    def replace_sheet(self, sheet_name: str, matrix: list[list[Any]]) -> None:
        _, root, path = self.rows(sheet_name)
        sheet_data = root.find(f"{N}sheetData")
        if sheet_data is None:
            raise ValueError(f"工作表缺少 sheetData：{sheet_name}")
        old_rows = list(sheet_data.findall(f"{N}row"))
        header_templates = old_rows[:3]
        data_template = old_rows[3] if len(old_rows) > 3 else (old_rows[-1] if old_rows else None)
        continuation_template = next(
            (row for row in old_rows[4:] if not any(
                cell.attrib.get("r", "").startswith("B") and self._cell_has_value(cell)
                for cell in row.findall(f"{N}c")
            )),
            data_template,
        )
        for child in list(sheet_data):
            sheet_data.remove(child)

        for row_index, values in enumerate(matrix, start=1):
            if row_index <= 3 and row_index <= len(header_templates):
                template = header_templates[row_index - 1]
            elif row_index > 4 and values and not str(values[1] if len(values) > 1 else "").strip():
                template = continuation_template
            else:
                template = data_template
            row = ET.Element(f"{N}row", {"r": str(row_index)})
            if template is not None:
                for key in ("ht", "customHeight", "s", "customFormat"):
                    if key in template.attrib:
                        row.set(key, template.attrib[key])
            template_cells = list(template.findall(f"{N}c")) if template is not None else []
            style_by_col = {
                column_index(cell.attrib.get("r", "A1")): cell.attrib.get("s")
                for cell in template_cells
            }
            fallback_style = next((value for value in reversed(list(style_by_col.values())) if value is not None), None)
            for col_index, value in enumerate(values):
                if value is None or value == "":
                    continue
                attrs = {"r": f"{column_name(col_index)}{row_index}"}
                style = style_by_col.get(col_index, fallback_style)
                if style is not None:
                    attrs["s"] = style
                cell = ET.SubElement(row, f"{N}c", attrs)
                self._write_cell(cell, value)
            sheet_data.append(row)

        dimension = root.find(f"{N}dimension")
        if dimension is not None:
            max_cols = max((len(row) for row in matrix), default=1)
            dimension.set("ref", f"A1:{column_name(max_cols - 1)}{max(1, len(matrix))}")
        self.entries[path] = ET.tostring(root, encoding="utf-8", xml_declaration=True)

    @staticmethod
    def _cell_has_value(cell: ET.Element) -> bool:
        return cell.find(f"{N}v") is not None or cell.find(f"{N}is") is not None

    @staticmethod
    def _write_cell(cell: ET.Element, value: Any) -> None:
        if isinstance(value, bool):
            cell.set("t", "n")
            ET.SubElement(cell, f"{N}v").text = "1" if value else "0"
        elif isinstance(value, (int, float)) and not isinstance(value, bool):
            cell.set("t", "n")
            text = str(value)
            if isinstance(value, float) and value.is_integer():
                text = str(int(value))
            ET.SubElement(cell, f"{N}v").text = text
        else:
            cell.set("t", "inlineStr")
            inline = ET.SubElement(cell, f"{N}is")
            text_node = ET.SubElement(inline, f"{N}t")
            text = str(value)
            if text != text.strip() or "\n" in text:
                text_node.set(f"{{{XML_NS}}}space", "preserve")
            text_node.text = text

    def save_atomic(self, destination: Path) -> Path:
        backup_dir = PROJECT_ROOT / "Temp" / "EventEditorBackups"
        backup_dir.mkdir(parents=True, exist_ok=True)
        timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
        backup = backup_dir / f"event-{timestamp}.xlsx"
        shutil.copy2(destination, backup)
        with tempfile.NamedTemporaryFile(prefix="event-editor-", suffix=".xlsx", dir=destination.parent, delete=False) as temp:
            temp_path = Path(temp.name)
        try:
            with zipfile.ZipFile(temp_path, "w", zipfile.ZIP_DEFLATED) as archive:
                for name, data in self.entries.items():
                    archive.writestr(name, data)
            os.replace(temp_path, destination)
        finally:
            temp_path.unlink(missing_ok=True)
        return backup


def parse_enum(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    text = path.read_text(encoding="utf-8")
    comments: list[str] = []
    result: list[dict[str, Any]] = []
    for line in text.splitlines():
        summary = re.search(r"///\s*(?:<summary>)?\s*(.*?)(?:</summary>)?\s*$", line)
        if summary:
            value = summary.group(1).strip()
            if value and value not in {"<summary>", "</summary>"}:
                comments.append(value)
            continue
        item = re.match(r"\s*([A-Za-z_]\w*)\s*=\s*(-?\d+)\s*,", line)
        if item:
            result.append({"name": item.group(1), "value": int(item.group(2)), "comment": " ".join(comments[-2:])})
            comments.clear()
    return result


def as_text(value: Any) -> str:
    if value is None:
        return ""
    return str(value).strip()


def as_float(value: Any, default: float = 0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def as_int(value: Any, default: int = 0) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return default


def as_bool(value: Any) -> bool:
    if isinstance(value, bool):
        return value
    return as_text(value).lower() in {"1", "true", "yes"}


def row_dict(headers: list[str], row: list[Any]) -> dict[str, Any]:
    return {header: row[index] if index < len(row) else "" for index, header in enumerate(headers) if header}


def load_generated_events() -> dict[str, dict[str, Any]]:
    if not GENERATED_EVENT_JSON.exists():
        return {}
    try:
        data = json.loads(GENERATED_EVENT_JSON.read_text(encoding="utf-8"))
        return {str(item.get("id", "")): item for item in data if item.get("id")}
    except (json.JSONDecodeError, OSError):
        return {}


def load_model(workbook_path: Path) -> dict[str, Any]:
    document = XlsxDocument(workbook_path)
    event_rows, _, _ = document.rows("event")
    option_rows, _, _ = document.rows("event_option")
    if len(event_rows) < 3 or len(option_rows) < 3:
        raise ValueError("event.xlsx 缺少 Luban 三行表头")

    raw_event_headers = [as_text(value).lstrip("*") for value in event_rows[0][1:]]
    generated = load_generated_events()
    action_behaviors = parse_enum(ACTION_ENUM_CS)
    action_by_value = {item["value"]: item["name"] for item in action_behaviors}
    events: list[dict[str, Any]] = []
    for row in event_rows[3:]:
        record = row_dict(raw_event_headers, row[1:])
        event_id = as_text(record.get("id"))
        if not event_id:
            continue
        fallback = generated.get(event_id, {})
        raw_types = record.get("eventTypes", "")
        if not as_text(raw_types) and isinstance(fallback.get("eventTypes"), list):
            event_types = [action_by_value.get(int(value), str(value)) for value in fallback["eventTypes"]]
        else:
            event_types = [value.strip() for value in as_text(raw_types).split("|") if value.strip()]
        event = {
            "id": event_id,
            "name": as_text(record.get("name")),
            "desc": as_text(record.get("desc")),
            "eventTypes": event_types,
            "preconditions": as_text(record.get("preconditions")),
            "weight": as_float(record.get("weight"), as_float(fallback.get("weight"))),
            "repeatable": as_bool(record.get("repeatable")) if "repeatable" in record else bool(fallback.get("repeatable", True)),
            "bgSprite": as_text(record.get("bgSprite") or fallback.get("bgSprite")),
            "resultText": as_text(record.get("resultText") or fallback.get("resultText")),
            "slotRewardGroupId": as_text(record.get("slotRewardGroupId") or fallback.get("slotRewardGroupId")),
            "slotEmptyWeight": as_float(record.get("slotEmptyWeight"), as_float(fallback.get("slotEmptyWeight"))),
            "slotFreeSpins": as_int(record.get("slotFreeSpins"), as_int(fallback.get("slotFreeSpins"))),
            "slotPaidCost": as_int(record.get("slotPaidCost"), as_int(fallback.get("slotPaidCost"))),
            "slotMaxSpins": as_int(record.get("slotMaxSpins"), as_int(fallback.get("slotMaxSpins"))),
        }
        events.append(event)

    option_headers = [as_text(value) for value in option_rows[0][1:]]
    options: list[dict[str, Any]] = []
    current: dict[str, Any] | None = None
    for row in option_rows[3:]:
        record = row_dict(option_headers, row[1:])
        option_id = as_text(record.get("id"))
        if option_id:
            current = {
                "id": option_id,
                "eventId": as_text(record.get("eventId")),
                "parentId": as_text(record.get("parentId")),
                "text": as_text(record.get("text")),
                "resultText": as_text(record.get("resultText")),
                "condition": as_text(record.get("condition")),
                "conditionText": as_text(record.get("conditionText")),
                "autoEnd": as_bool(record.get("autoEnd")),
                "effects": [],
            }
            options.append(current)
        if current is None:
            continue
        effect_type = as_text(record.get("*effectTypes") or record.get("effectTypes"))
        if effect_type:
            current["effects"].append({
                "type": effect_type,
                "value": as_float(record.get("*effectValues") or record.get("effectValues")),
                "param": as_text(record.get("*effectParams") or record.get("effectParams")),
            })

    for option in options:
        if not option["effects"]:
            option["effects"] = [{"type": "None", "value": 0, "param": "-"}]

    meta = {
        "effectTypes": parse_enum(EFFECT_ENUM_CS),
        "actionBehaviors": action_behaviors,
        "knownConditions": sorted(KNOWN_CONDITIONS),
        "sourcePath": str(workbook_path),
        "sourceHash": sha256_file(workbook_path),
        "sourceModifiedAt": datetime.fromtimestamp(workbook_path.stat().st_mtime).isoformat(timespec="seconds"),
        "missingEventColumns": [column for column in EVENT_COLUMNS if column not in raw_event_headers],
    }
    model = {"events": events, "options": options, "meta": meta}
    model["audit"] = validate_model(model)
    return model


def issue(severity: str, code: str, message: str, event_id: str = "", option_id: str = "") -> dict[str, str]:
    return {"severity": severity, "code": code, "message": message, "eventId": event_id, "optionId": option_id}


def condition_issues(expression: str, event_id: str, option_id: str = "") -> list[dict[str, str]]:
    result: list[dict[str, str]] = []
    for raw in (expression or "").split("|"):
        clause = raw.strip()
        if not clause:
            continue
        key, separator, value = clause.partition(":")
        key = key.strip()
        value = value.strip()
        if key not in KNOWN_CONDITIONS:
            result.append(issue("review", "unknown-condition", f"条件“{key}”没有现成运行时逻辑，需要先确认实现方式。", event_id, option_id))
        elif key not in NO_VALUE_CONDITIONS and (not separator or not value):
            result.append(issue("error", "condition-value", f"条件“{key}”缺少参数。", event_id, option_id))
        elif key in NO_VALUE_CONDITIONS and separator:
            result.append(issue("warning", "condition-extra-value", f"条件“{key}”不需要参数，冒号后的内容会被忽略。", event_id, option_id))
    return result


def validate_model(model: dict[str, Any]) -> dict[str, Any]:
    events = model.get("events") if isinstance(model.get("events"), list) else []
    options = model.get("options") if isinstance(model.get("options"), list) else []
    effect_types = {item["name"] for item in parse_enum(EFFECT_ENUM_CS)}
    action_types = {item["name"] for item in parse_enum(ACTION_ENUM_CS)}
    issues: list[dict[str, str]] = []
    event_ids: set[str] = set()
    option_ids: set[str] = set()

    for event in events:
        event_id = as_text(event.get("id"))
        if not event_id:
            issues.append(issue("error", "event-id-empty", "事件 ID 不能为空。"))
            continue
        if event_id in event_ids:
            issues.append(issue("error", "event-id-duplicate", f"事件 ID 重复：{event_id}", event_id))
        event_ids.add(event_id)
        if not re.fullmatch(r"ev_[a-z0-9_]+", event_id):
            issues.append(issue("warning", "event-id-style", "事件 ID 建议使用 ev_ 开头的小写英文与下划线。", event_id))
        if not as_text(event.get("name")):
            issues.append(issue("error", "event-name-empty", "事件名称不能为空。", event_id))
        if not as_text(event.get("desc")):
            issues.append(issue("warning", "event-desc-empty", "事件根页正文为空。", event_id))
        categories = event.get("eventTypes") if isinstance(event.get("eventTypes"), list) else []
        if not categories:
            issues.append(issue("error", "event-type-empty", "事件至少需要一个分类。", event_id))
        for category in categories:
            if category not in action_types:
                issues.append(issue("error", "event-type-unknown", f"未知事件分类：{category}", event_id))
        if as_float(event.get("weight")) < 0:
            issues.append(issue("error", "event-weight-negative", "随机权重不能小于 0。", event_id))
        if any(category in {"Event", "Reward", "Negative"} for category in categories) and as_float(event.get("weight")) == 0:
            issues.append(issue("warning", "event-zero-weight", "该事件属于随机池但权重为 0，不会被自然抽到。", event_id))
        issues.extend(condition_issues(as_text(event.get("preconditions")), event_id))
        if "Slot" in categories:
            if not as_text(event.get("slotRewardGroupId")):
                issues.append(issue("error", "slot-group-empty", "Slot 事件必须配置奖励槽组。", event_id))
            if as_int(event.get("slotMaxSpins")) <= 0:
                issues.append(issue("error", "slot-max-spins", "Slot 最大抽奖次数必须大于 0。", event_id))
            if as_int(event.get("slotFreeSpins")) < 0 or as_int(event.get("slotPaidCost")) < 0 or as_float(event.get("slotEmptyWeight")) < 0:
                issues.append(issue("error", "slot-negative", "Slot 次数、价格和空奖权重不能为负数。", event_id))

    option_by_id: dict[str, dict[str, Any]] = {}
    for option in options:
        option_id = as_text(option.get("id"))
        event_id = as_text(option.get("eventId"))
        if not option_id:
            issues.append(issue("error", "option-id-empty", "选项 ID 不能为空。", event_id))
            continue
        if option_id in option_ids:
            issues.append(issue("error", "option-id-duplicate", f"选项 ID 重复：{option_id}", event_id, option_id))
        option_ids.add(option_id)
        option_by_id[option_id] = option
        if not re.fullmatch(r"opt_[a-z0-9_]+", option_id):
            issues.append(issue("warning", "option-id-style", "选项 ID 建议使用 opt_ 开头的小写英文与下划线。", event_id, option_id))
        if event_id not in event_ids:
            issues.append(issue("error", "option-event-missing", f"所属事件不存在：{event_id}", event_id, option_id))
        if not as_text(option.get("text")):
            issues.append(issue("error", "option-text-empty", "选项按钮文字不能为空。", event_id, option_id))
        condition = as_text(option.get("condition"))
        condition_text = as_text(option.get("conditionText"))
        issues.extend(condition_issues(condition, event_id, option_id))
        if condition and not condition_text:
            issues.append(issue("error", "condition-text-empty", "配置了选项条件时，必须填写玩家看到的条件文案。", event_id, option_id))
        if condition_text and not condition:
            issues.append(issue("warning", "condition-text-unused", "没有条件却填写了条件文案，游戏不会用它控制可选状态。", event_id, option_id))
        if condition_text and re.fullmatch(r"\d+(?:\.\d+)?", condition_text):
            issues.append(issue("warning", "condition-text-number", f"条件文案“{condition_text}”看起来像占位数字，请确认玩家文案。", event_id, option_id))
        effects = option.get("effects") if isinstance(option.get("effects"), list) else []
        if not effects:
            issues.append(issue("warning", "effect-empty", "该选项没有效果；如只用于进入子页可忽略。", event_id, option_id))
        follow_ups = 0
        for effect in effects:
            effect_type = as_text(effect.get("type")) or "None"
            if effect_type not in effect_types:
                issues.append(issue("error", "effect-unknown", f"未知效果类型：{effect_type}", event_id, option_id))
            if effect_type in FOLLOW_UP_EFFECTS:
                follow_ups += 1
            if effect_type in LOGIC_REVIEW_EFFECTS:
                issues.append(issue("review", "effect-needs-logic", f"{effect_type}：{LOGIC_REVIEW_EFFECTS[effect_type]}。", event_id, option_id))
        if follow_ups > 1:
            issues.append(issue("error", "multiple-follow-ups", "同一选项不能配置多个终止型跟进效果。", event_id, option_id))

    children: dict[str, list[str]] = {}
    for option in options:
        option_id = as_text(option.get("id"))
        event_id = as_text(option.get("eventId"))
        parent_id = as_text(option.get("parentId"))
        if not parent_id:
            continue
        parent = option_by_id.get(parent_id)
        if parent is None:
            issues.append(issue("error", "parent-missing", f"父选项不存在：{parent_id}", event_id, option_id))
            continue
        if as_text(parent.get("eventId")) != event_id:
            issues.append(issue("error", "parent-cross-event", "父选项属于另一个事件，不能跨事件连线。", event_id, option_id))
        children.setdefault(parent_id, []).append(option_id)

    for option in options:
        option_id = as_text(option.get("id"))
        event_id = as_text(option.get("eventId"))
        effect_names = {as_text(effect.get("type")) for effect in option.get("effects", [])}
        if option_id in children and effect_names & FOLLOW_UP_EFFECTS:
            issues.append(issue("error", "child-after-follow-up", "该选择会直接进入经营挑战/商店/结局，后续子选择永远不会出现。", event_id, option_id))
        if option_id in children and bool(option.get("autoEnd")):
            issues.append(issue("error", "child-after-auto-end", "该选择设置了立即结束，后续子选择永远不会出现。", event_id, option_id))
        if option_id in children and not as_text(option.get("resultText")):
            issues.append(issue("warning", "child-page-text-empty", "该选择会进入下一页，但下一页正文为空。", event_id, option_id))

    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(option_id: str) -> None:
        if option_id in visited:
            return
        if option_id in visiting:
            option = option_by_id.get(option_id, {})
            issues.append(issue("error", "option-cycle", "选项连接形成循环；事件页面必须是树。", as_text(option.get("eventId")), option_id))
            return
        visiting.add(option_id)
        for child in children.get(option_id, []):
            visit(child)
        visiting.remove(option_id)
        visited.add(option_id)

    for option_id in option_by_id:
        visit(option_id)

    missing_columns = model.get("meta", {}).get("missingEventColumns", [])
    if missing_columns:
        issues.append(issue("warning", "schema-columns-missing", "event 表缺少字段：" + "、".join(missing_columns) + "；保存时编辑器会按当前生成配置补齐。"))

    counts = {severity: sum(1 for item in issues if item["severity"] == severity) for severity in ("error", "review", "warning", "info")}
    return {"issues": issues, "counts": counts, "canSave": counts["error"] == 0, "needsLogicReview": counts["review"] > 0}


def normalize_event(event: dict[str, Any]) -> dict[str, Any]:
    return {
        "id": as_text(event.get("id")), "name": as_text(event.get("name")), "desc": as_text(event.get("desc")),
        "eventTypes": [as_text(value) for value in event.get("eventTypes", []) if as_text(value)],
        "preconditions": as_text(event.get("preconditions")), "weight": as_float(event.get("weight")),
        "repeatable": bool(event.get("repeatable")), "bgSprite": as_text(event.get("bgSprite")),
        "resultText": as_text(event.get("resultText")), "slotRewardGroupId": as_text(event.get("slotRewardGroupId")),
        "slotEmptyWeight": as_float(event.get("slotEmptyWeight")), "slotFreeSpins": as_int(event.get("slotFreeSpins")),
        "slotPaidCost": as_int(event.get("slotPaidCost")), "slotMaxSpins": as_int(event.get("slotMaxSpins")),
    }


def normalize_option(option: dict[str, Any]) -> dict[str, Any]:
    effects = []
    for effect in option.get("effects", []):
        effects.append({"type": as_text(effect.get("type")) or "None", "value": as_float(effect.get("value")), "param": as_text(effect.get("param")) or "-"})
    if not effects:
        effects = [{"type": "None", "value": 0, "param": "-"}]
    return {
        "id": as_text(option.get("id")), "eventId": as_text(option.get("eventId")),
        "parentId": as_text(option.get("parentId")), "text": as_text(option.get("text")),
        "resultText": as_text(option.get("resultText")), "condition": as_text(option.get("condition")),
        "conditionText": as_text(option.get("conditionText")), "autoEnd": bool(option.get("autoEnd")), "effects": effects,
    }


def workbook_matrices(model: dict[str, Any]) -> tuple[list[list[Any]], list[list[Any]]]:
    event_matrix: list[list[Any]] = [
        ["##var", *EVENT_COLUMNS],
        ["##comment", *[EVENT_COMMENTS[column] for column in EVENT_COLUMNS]],
        ["##type", *[EVENT_TYPES[column] for column in EVENT_COLUMNS]],
    ]
    for raw in model["events"]:
        event = normalize_event(raw)
        event_matrix.append([
            None, event["id"], event["name"], event["desc"], "|".join(event["eventTypes"]), event["preconditions"],
            event["weight"], event["repeatable"], event["bgSprite"], event["resultText"], event["slotRewardGroupId"],
            event["slotEmptyWeight"], event["slotFreeSpins"], event["slotPaidCost"], event["slotMaxSpins"],
        ])

    option_matrix: list[list[Any]] = [
        ["##var", *OPTION_COLUMNS],
        ["##comment", *[OPTION_COMMENTS[column] for column in OPTION_COLUMNS]],
        ["##type", *[OPTION_TYPES[column] for column in OPTION_COLUMNS]],
    ]
    for raw in model["options"]:
        option = normalize_option(raw)
        for effect_index, effect in enumerate(option["effects"]):
            first = effect_index == 0
            option_matrix.append([
                None,
                option["id"] if first else None,
                option["eventId"] if first else None,
                option["parentId"] if first else None,
                option["text"] if first else None,
                option["resultText"] if first else None,
                option["condition"] if first else None,
                option["conditionText"] if first else None,
                effect["type"], effect["value"], effect["param"], option["autoEnd"] if first else None,
            ])
    return event_matrix, option_matrix


def save_model(workbook_path: Path, model: dict[str, Any], source_hash: str) -> dict[str, Any]:
    current_hash = sha256_file(workbook_path)
    if source_hash and source_hash != current_hash:
        raise FileExistsError("event.xlsx 在编辑器加载后又被其他程序修改。请重新读取后再保存，避免覆盖新内容。")
    normalized = {
        "events": [normalize_event(event) for event in model.get("events", [])],
        "options": [normalize_option(option) for option in model.get("options", [])],
        "meta": model.get("meta", {}),
    }
    audit = validate_model(normalized)
    if not audit["canSave"]:
        raise ValueError("审核存在阻塞问题，修正后才能保存。")
    document = XlsxDocument(workbook_path)
    event_matrix, option_matrix = workbook_matrices(normalized)
    document.replace_sheet("event", event_matrix)
    document.replace_sheet("event_option", option_matrix)
    backup = document.save_atomic(workbook_path)
    reloaded = load_model(workbook_path)
    return {"model": reloaded, "backupPath": str(backup), "audit": audit}


class EditorHandler(BaseHTTPRequestHandler):
    server_version = "GourmetEventEditor/1.0"

    @property
    def workbook_path(self) -> Path:
        return self.server.workbook_path  # type: ignore[attr-defined]

    def log_message(self, format_string: str, *args: Any) -> None:
        sys.stdout.write("[事件编辑器] " + format_string % args + "\n")

    def _json(self, payload: Any, status: int = 200) -> None:
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def _error(self, message: str, status: int = 400, details: Any = None) -> None:
        self._json({"ok": False, "message": message, "details": details}, status)

    def _body(self) -> dict[str, Any]:
        length = int(self.headers.get("Content-Length", "0"))
        if length > 8 * 1024 * 1024:
            raise ValueError("请求过大")
        return json.loads(self.rfile.read(length).decode("utf-8") or "{}")

    def do_GET(self) -> None:
        path = urlparse(self.path).path
        try:
            if path in {"/", "/gm-event-editor.html"}:
                data = EDITOR_HTML.read_bytes()
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(data)))
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                self.wfile.write(data)
                return
            if path == "/api/model":
                self._json({"ok": True, "model": load_model(self.workbook_path)})
                return
            if path == "/api/health":
                self._json({"ok": True, "workbook": str(self.workbook_path)})
                return
            self._error("页面不存在", 404)
        except Exception as exc:  # noqa: BLE001
            self._error(str(exc), 500)

    def do_POST(self) -> None:
        path = urlparse(self.path).path
        if self.headers.get("X-Event-Editor") != "1":
            self._error("缺少事件编辑器请求标记", 403)
            return
        try:
            body = self._body()
            if path == "/api/validate":
                self._json({"ok": True, "audit": validate_model(body.get("model", {}))})
                return
            if path == "/api/save":
                result = save_model(self.workbook_path, body.get("model", {}), as_text(body.get("sourceHash")))
                self._json({"ok": True, **result})
                return
            if path == "/api/generate":
                process = subprocess.run(
                    ["bash", "GameConfig/gen.sh"], cwd=PROJECT_ROOT, capture_output=True, text=True, timeout=240,
                )
                output = (process.stdout + "\n" + process.stderr).strip()
                self._json({"ok": process.returncode == 0, "exitCode": process.returncode, "output": output}, 200)
                return
            self._error("接口不存在", 404)
        except FileExistsError as exc:
            self._error(str(exc), HTTPStatus.CONFLICT)
        except subprocess.TimeoutExpired:
            self._error("配置生成超过 240 秒，已停止等待。", 504)
        except ValueError as exc:
            self._error(str(exc), 422)
        except Exception as exc:  # noqa: BLE001
            self._error(str(exc), 500)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="GourmetProject 事件可视化编辑器")
    parser.add_argument("--port", type=int, default=8766)
    parser.add_argument("--workbook", type=Path, default=Path(os.environ.get("EVENT_EDITOR_WORKBOOK", DEFAULT_WORKBOOK)))
    parser.add_argument("--no-open", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    workbook = args.workbook.expanduser().resolve()
    if not workbook.exists():
        print(f"找不到事件表：{workbook}", file=sys.stderr)
        return 2
    if not EDITOR_HTML.exists():
        print(f"找不到编辑器页面：{EDITOR_HTML}", file=sys.stderr)
        return 2
    server = ThreadingHTTPServer(("127.0.0.1", args.port), EditorHandler)
    server.workbook_path = workbook  # type: ignore[attr-defined]
    url = f"http://127.0.0.1:{args.port}/"
    print(f"事件编辑器已启动：{url}")
    print(f"当前事件表：{workbook}")
    print("按 Ctrl+C 停止。")
    if not args.no_open:
        threading.Timer(0.4, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
