#!/bin/bash
# Luban 配置生成脚本（PC/macOS/Linux）。需要 .NET SDK 8.0+。
# 代码输出到 Assets/GameMain/Scripts/Config/Gen（该目录会被 Luban 清空，勿放手写代码）。
# 数据输出到 Assets/StreamingAssets/Config（运行时按 JSON 读取）。
set -e

WORKSPACE="$(cd "$(dirname "$0")" && pwd)"
LUBAN_DLL="$WORKSPACE/Tools/Luban/Luban.dll"
CONF="$WORKSPACE/luban.conf"
PROJECT_ROOT="$(cd "$WORKSPACE/.." && pwd)"
CODE_OUT="$PROJECT_ROOT/Assets/GameMain/Scripts/Config/Gen"
DATA_OUT="$PROJECT_ROOT/Assets/StreamingAssets/Config"

echo "Luban codegen -> $CODE_OUT"
echo "Luban data    -> $DATA_OUT"

dotnet "$LUBAN_DLL" \
    -t client \
    -c cs-simple-json \
    -d json \
    --conf "$CONF" \
    -x outputCodeDir="$CODE_OUT" \
    -x outputDataDir="$DATA_OUT"

echo "Luban generation done."
