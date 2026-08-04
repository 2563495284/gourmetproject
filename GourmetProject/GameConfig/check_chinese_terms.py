#!/usr/bin/env python3
"""Read-only Chinese terminology audit for project text and Excel workbooks."""

from __future__ import annotations

import argparse
import re
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

PROJECT_ROOT = Path(__file__).resolve().parents[1]
SCAN_ROOTS = (
    PROJECT_ROOT / "Assets" / "GameMain",
    PROJECT_ROOT / "Assets" / "StreamingAssets" / "Config",
    PROJECT_ROOT / "GameConfig",
    PROJECT_ROOT / "docs",
    PROJECT_ROOT / "Art",
)
TEXT_SUFFIXES = {
    ".asset", ".asmdef", ".command", ".cs", ".csv", ".html", ".js", ".json",
    ".jsx", ".md", ".prefab", ".py", ".sh", ".toml", ".ts", ".txt", ".unity",
    ".uss", ".uxml", ".xml", ".yaml", ".yml",
}
SKIP_FILES = {
    PROJECT_ROOT / "docs" / "中文术语规范.md",
    PROJECT_ROOT / "Assets" / "GameMain" / "Tests" / "EditMode" / "ChineseTerminologyConfigTests.cs",
    Path(__file__).resolve(),
}
RULES = (
    ("美食（仅“美食街”例外）", re.compile(r"美食(?!街)")),
    ("菜品", re.compile(r"菜品")),
    ("菜谱", re.compile(r"菜谱")),
    ("出餐", re.compile(r"出餐")),
    ("中文战斗", re.compile(r"战斗")),
    ("品鉴", re.compile(r"品鉴")),
    ("旧分值术语", re.compile(r"美味度|最终分数|目标分")),
    ("旧餐桌格术语", re.compile(r"餐桌碎片|胃部碎片|餐桌格子")),
    ("旧时间轴术语", re.compile(r"行动轴|日程轴")),
    ("旧行动术语", re.compile(r"日常行动|随机行动|节点事件")),
    ("旧物品术语", re.compile(r"被动道具|主动道具|负面道具|道具")),
    ("游戏 Character 的旧中文称呼", re.compile(r"角色(?!身份)")),
    ("拾取", re.compile(r"拾取")),
    ("玩家文案中的乘区", re.compile(r"乘区|倍率倍率")),
    ("错误复合词", re.compile(r"经营挑战挑战|食物挑战|装饰品和消耗品和")),
    ("旧红心术语", re.compile(r"扣心|生命耗尽|恢复血量")),
    ("旧食物计数", re.compile(r"一道菜|\d+\s*(?:个|道)菜")),
    ("旧单局结果术语", re.compile(r"本局胜利|单局胜利|本局失败|单局失败")),
    ("旧 Run 称呼", re.compile(r"完整\s*Run")),
    ("旧玩法并列", re.compile(r"普通/超级|普通、超级")),
)
NS = {"x": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
REL_NS = {"r": "http://schemas.openxmlformats.org/package/2006/relationships"}
DOC_REL = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id"


def should_skip(path: Path) -> bool:
    if path in SKIP_FILES:
        return True
    text = path.as_posix()
    return (
        path.stat().st_size > 5 * 1024 * 1024
        or "/Content/Resources/Fonts/" in text
        or "/GameConfig/Tools/" in text
        or path.name.endswith(".inspect.ndjson")
        or any(part in {"Temp", "Logs", "obj", "bin", "Packages"} for part in path.parts)
    )


def findings_in_text(label: str, text: str) -> list[str]:
    findings: list[str] = []
    for rule_name, pattern in RULES:
        for match in pattern.finditer(text):
            line = text.count("\n", 0, match.start()) + 1
            excerpt = text[max(0, match.start() - 24):match.end() + 36].replace("\n", "\\n")
            findings.append(f"{label}:{line}: {rule_name}: {excerpt}")
    return findings


def shared_strings(archive: zipfile.ZipFile) -> list[str]:
    try:
        root = ET.fromstring(archive.read("xl/sharedStrings.xml"))
    except KeyError:
        return []
    return ["".join(node.text or "" for node in item.findall(".//x:t", NS)) for item in root.findall("x:si", NS)]


def workbook_strings(path: Path):
    with zipfile.ZipFile(path) as archive:
        strings = shared_strings(archive)
        workbook = ET.fromstring(archive.read("xl/workbook.xml"))
        relationships = ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))
        rel_targets = {rel.attrib["Id"]: rel.attrib["Target"] for rel in relationships.findall("r:Relationship", REL_NS)}
        for sheet in workbook.findall("x:sheets/x:sheet", NS):
            sheet_name = sheet.attrib.get("name", "")
            yield f"{path}#{sheet_name}!sheet", sheet_name
            target = rel_targets.get(sheet.attrib.get(DOC_REL, ""), "")
            if not target:
                continue
            target = target.lstrip("/")
            xml_path = target if target.startswith("xl/") else f"xl/{target}"
            try:
                root = ET.fromstring(archive.read(xml_path))
            except KeyError:
                continue
            for cell in root.findall(".//x:c", NS):
                if cell.find("x:f", NS) is not None:
                    continue
                cell_type = cell.attrib.get("t")
                value = ""
                if cell_type == "s":
                    raw = cell.findtext("x:v", default="", namespaces=NS)
                    if raw.isdigit() and int(raw) < len(strings):
                        value = strings[int(raw)]
                elif cell_type == "inlineStr":
                    value = "".join(node.text or "" for node in cell.findall(".//x:t", NS))
                elif cell_type == "str":
                    value = cell.findtext("x:v", default="", namespaces=NS)
                if value:
                    yield f"{path}#{sheet_name}!{cell.attrib.get('r', '?')}", value


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-only", action="store_true", help="生成前跳过 Luban 输出目录")
    args = parser.parse_args()
    findings: list[str] = []
    visited: set[Path] = set()
    for root in SCAN_ROOTS:
        if not root.exists():
            continue
        for path in root.rglob("*"):
            if not path.is_file() or path in visited or should_skip(path):
                continue
            visited.add(path)
            text_path = path.as_posix()
            if args.source_only and (
                "/Assets/GameMain/Scripts/Config/Gen/" in text_path
                or "/Assets/StreamingAssets/Config/" in text_path
            ):
                continue
            if path.suffix.lower() == ".xlsx":
                try:
                    for label, value in workbook_strings(path):
                        findings.extend(findings_in_text(label, value))
                except (OSError, zipfile.BadZipFile, ET.ParseError) as error:
                    findings.append(f"{path}: 无法读取 Excel：{error}")
                continue
            if path.suffix.lower() not in TEXT_SUFFIXES:
                continue
            try:
                text = path.read_text(encoding="utf-8")
            except (OSError, UnicodeDecodeError):
                continue
            findings.extend(findings_in_text(str(path), text))
    if findings:
        print("中文术语检查失败：", file=sys.stderr)
        for finding in findings:
            print(f"- {finding}", file=sys.stderr)
        print(f"共 {len(findings)} 处。标准见 docs/中文术语规范.md。", file=sys.stderr)
        return 1
    print("中文术语检查通过。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
