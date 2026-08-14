#!/usr/bin/env python3
"""食物技能 Scope GM：只读正式配置并提供本地调试页面。"""

from __future__ import annotations

import argparse
import json
import mimetypes
import threading
import webbrowser
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlparse


PROJECT_ROOT = Path(__file__).resolve().parents[2]
CONFIG_ROOT = PROJECT_ROOT / "Assets" / "StreamingAssets" / "Config"
HTML_PATH = Path(__file__).with_name("gm-scope-debugger.html")
FOOD_IMAGE_ROOT = PROJECT_ROOT / "Art" / "FoodVisualSamples" / "storybook_full" / "final"
SERVER_VERSION = 1


def read_json(name: str):
    with (CONFIG_ROOT / name).open("r", encoding="utf-8") as stream:
        return json.load(stream)


def split_pipe(value: object) -> list[str]:
    return [part.strip() for part in str(value or "").split("|") if part.strip()]


def build_payload() -> dict:
    dish_bases = read_json("tbdishbase.json")
    skills = {item["id"]: item for item in read_json("tbskill.json")}
    subskills = {item["id"]: item for item in read_json("tbsubskill.json")}

    foods = []
    missing_refs: list[str] = []
    for dish in sorted(dish_bases, key=lambda item: (item.get("sortOrder", 0), item.get("id", ""))):
        skill_ids = split_pipe(dish.get("skills"))
        rules = []
        for skill_id in skill_ids:
            skill = skills.get(skill_id)
            if not skill:
                missing_refs.append(f"{dish['id']} -> missing skill {skill_id}")
                continue
            for order, subskill_id in enumerate(split_pipe(skill.get("subSkills"))):
                raw_rule = subskills.get(subskill_id)
                if not raw_rule:
                    missing_refs.append(f"{skill_id} -> missing subskill {subskill_id}")
                    continue
                rule = dict(raw_rule)
                rule["skillId"] = skill_id
                rule["order"] = order
                rules.append(rule)

        image_path = FOOD_IMAGE_ROOT / f"{dish['id']}.png"
        foods.append(
            {
                "id": dish["id"],
                "name": dish.get("name", ""),
                "category": dish.get("category", ""),
                "countAs": dish.get("countAs", 1),
                "deliciousness": dish.get("deliciousness", 0),
                "shapeRows": dish.get("shapeRows", ["X"]),
                "skillIds": skill_ids,
                "rules": rules,
                "sortOrder": dish.get("sortOrder", 0),
                "imageUrl": f"/food-image/{dish['id']}.png" if image_path.is_file() else "",
            }
        )

    mtimes = {}
    for name in ("tbdishbase.json", "tbskill.json", "tbsubskill.json"):
        path = CONFIG_ROOT / name
        mtimes[name] = int(path.stat().st_mtime)

    return {
        "serverVersion": SERVER_VERSION,
        "projectRoot": str(PROJECT_ROOT),
        "sources": mtimes,
        "foods": foods,
        "missingReferences": missing_refs,
    }


class ScopeGmHandler(BaseHTTPRequestHandler):
    server_version = "GourmetScopeGM/1"

    def do_GET(self) -> None:  # noqa: N802
        route = urlparse(self.path).path
        if route in {"/", "/index.html", "/gm-scope-debugger.html"}:
            self.send_file(HTML_PATH, "text/html; charset=utf-8")
            return
        if route == "/api/scope-data":
            try:
                self.send_json(build_payload())
            except Exception as exc:  # pragma: no cover - surfaced to GM
                self.send_json({"error": str(exc)}, HTTPStatus.INTERNAL_SERVER_ERROR)
            return
        if route.startswith("/food-image/"):
            name = Path(unquote(route.removeprefix("/food-image/"))).name
            if not name.endswith(".png"):
                self.send_error(HTTPStatus.NOT_FOUND)
                return
            self.send_file(FOOD_IMAGE_ROOT / name, mimetypes.guess_type(name)[0] or "image/png")
            return
        if route == "/api/health":
            self.send_json({"ok": True, "version": SERVER_VERSION})
            return
        self.send_error(HTTPStatus.NOT_FOUND)

    def send_file(self, path: Path, content_type: str) -> None:
        if not path.is_file():
            self.send_error(HTTPStatus.NOT_FOUND)
            return
        body = path.read_bytes()
        self.send_response(HTTPStatus.OK)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def send_json(self, value: object, status: HTTPStatus = HTTPStatus.OK) -> None:
        body = json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format_string: str, *args: object) -> None:
        print(f"[scope-gm] {self.address_string()} {format_string % args}")


def main() -> None:
    parser = argparse.ArgumentParser(description="启动食物技能 Scope GM")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8766)
    parser.add_argument("--no-open", action="store_true", help="不自动打开浏览器")
    args = parser.parse_args()

    server = ThreadingHTTPServer((args.host, args.port), ScopeGmHandler)
    url = f"http://{args.host}:{args.port}/"
    print(f"Scope GM: {url}")
    print("按 Ctrl+C 停止。页面只读，不会修改配置。")
    if not args.no_open:
        threading.Timer(0.35, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
