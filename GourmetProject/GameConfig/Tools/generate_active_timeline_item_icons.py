#!/usr/bin/env python3
"""Regenerate the active timeline item icon set from its shared style manifest."""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
GENERATOR = (
    PROJECT_ROOT
    / ".cursor"
    / "skills"
    / "ai-asset-generate"
    / "scripts"
    / "generate.py"
)
MANIFEST = (
    PROJECT_ROOT
    / ".cursor"
    / "skills"
    / "ai-asset-generate"
    / "manifests"
    / "active-timeline-items-repair.json"
)


def main() -> int:
    command = [
        sys.executable,
        str(GENERATOR),
        "--manifest",
        str(MANIFEST),
        *sys.argv[1:],
    ]
    return subprocess.call(command, cwd=PROJECT_ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
