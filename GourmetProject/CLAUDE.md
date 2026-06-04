# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

GourmetProject is a Unity 6 project (`6000.3.14f1`) built on an embedded GameFramework UPM package, Luban config generation, URP 2D lighting, and Unity MCP tooling. The current gameplay is a Limbo-style 2D platformer roguelike layer named 光影.

## Common commands

Prefer Unity MCP/editor validation over standalone `dotnet` builds.

```bash
# Regenerate Luban config code/data after editing GameConfig/Defines/*.xml or GameConfig/Datas/*.xlsx
bash GameConfig/gen.sh

# Clear Unity MCP log cache
unity-mcp-cli run-tool console-clear-logs --input '{}'

# Force Unity asset refresh / script recompilation
unity-mcp-cli run-tool assets-refresh --input '{"options":"ForceSynchronousImport"}'

# Check recent Unity compile errors
unity-mcp-cli run-tool console-get-logs --input-file - <<'EOF' | grep -i "error CS" | sort -u || echo NONE
{"maxEntries":200,"logTypeFilter":"Error","includeStackTrace":false}
EOF

# Run all EditMode tests
unity-mcp-cli run-tool tests-run --input-file - <<'EOF'
{"testMode":"EditMode","testAssembly":"GourmetProject.Tests.EditMode"}
EOF

# Run one EditMode test class
unity-mcp-cli run-tool tests-run --input-file - <<'EOF'
{"testMode":"EditMode","testAssembly":"GourmetProject.Tests.EditMode","testClass":"GourmetProject.Tests.RngDeterminismTests"}
EOF

# Run one EditMode test method
unity-mcp-cli run-tool tests-run --input-file - <<'EOF'
{"testMode":"EditMode","testAssembly":"GourmetProject.Tests.EditMode","testMethod":"GourmetProject.Tests.RngDeterminismTests.Xoshiro_SameSeed_IsBitwiseIdentical"}
EOF

# Start and stop Play Mode for launch-flow validation
unity-mcp-cli run-tool editor-application-set-state --input '{"isPlaying":true}'
unity-mcp-cli run-tool editor-application-set-state --input '{"isPlaying":false}'
```

When using the in-process MCP tools available to Claude Code, prefer `assets-refresh`, `console-get-logs`, `tests-run`, scene/gameobject/component tools, and screenshots over manual YAML edits or shelling out to Unity files.

## Architecture

### Assembly boundaries

The repository is intentionally split by `asmdef` boundaries:

- `GourmetProject.Core` (`Assets/GameMain/Scripts/Core`) is pure C# with `noEngineReferences: true`. It contains deterministic RNG, save serialization, checksum/hash utilities, service locator, and the logging facade. Do not reference `UnityEngine`, GameFramework, `Debug`, `MonoBehaviour`, `Vector*`, `Application`, or time-based nondeterminism here.
- `GourmetProject.Config` (`Assets/GameMain/Scripts/Config`) wraps Luban generated tables. `Config/Gen/` is generated and must not be hand-edited.
- `GourmetProject.Runtime` (`Assets/GameMain/Scripts/Runtime`) integrates Unity + GameFramework and exposes infrastructure through `GameApp`.
- `GourmetProject.Game` (`Assets/GameMain/Scripts/Game`) contains gameplay, UI, roguelike systems, and platformer code. It depends on Runtime/Core/Config, not the other way around.
- `GourmetProject.Tests.EditMode` contains editor tests for Core-level behavior and explicitly references NUnit/Newtonsoft.

Dependency direction should remain `Game -> Runtime -> Config/Core`; Core and Config should not depend on gameplay.

### Startup and service access

The boot scene is `Assets/GameMain/Scenes/Launch.unity`. Startup flow is:

`ProcedureLaunch` initializes base services via `GameApp.InitializeServices()` → `ProcedurePreload` loads Luban config with `GameApp.Config.LoadAll()` → `ProcedureMain` reflectively switches to `GourmetProject.Game.Procedure.ProcedureMenu` if present.

Use `GourmetProject.Runtime.GameApp` as the single access point for infrastructure:

- Project services: `GameApp.Random`, `GameApp.Save`, `GameApp.Settings`, `GameApp.Config`
- GameFramework components: `GameApp.Event`, `GameApp.UI`, `GameApp.Sound`, `GameApp.Scene`, etc.
- Wrappers: `GameApp.Assets`, `GameApp.Audio`, `GameApp.Scenes`, `GameApp.L10n`

Runtime deliberately does not compile-time reference the Game assembly; it uses reflection in `ProcedureMain` to keep dependencies one-way.

### Luban config

Config source lives under `GameConfig/`:

- Schemas: `GameConfig/Defines/*.xml`
- Data: `GameConfig/Datas/*.xlsx`
- Generated C#: `Assets/GameMain/Scripts/Config/Gen/`
- Generated JSON data: `Assets/StreamingAssets/Config/`

After changing schemas or `.xlsx` data, run `bash GameConfig/gen.sh` and include both generated code and generated JSON in the change. Runtime access should go through `GameApp.Config.Tables`, for example `GameApp.Config.Tables.TbXxx.Get(id)`.

The xlsx convention is: column A is the marker column; row 1 starts with `##var`; row 2 may use `##comment`; data starts on row 3; data rows whose first column starts with `##` are ignored.

### Gameplay layer

Current gameplay lives in `Assets/GameMain/Scripts/Game`:

- `Procedure/ProcedureMenu.cs` opens GameFramework UI forms and transitions to gameplay when `GameplayLauncher` requests a start.
- `Procedure/ProcedureGameplay.cs` prepares the run seed/save state, creates a `GameWorld`, and returns to menu on Escape.
- `Platformer/` is the 光影 platformer layer. `GameWorld` builds the level, camera, background, lighting, player, checkpoints, monsters, HUD, and skill controller in code.
- `Roguelike/` holds meta progression, run save/session state, skill catalog/drafting/runtime state.
- `UI/` contains GameFramework UI form logic and form IDs; prefabs are under `Assets/GameMain/UI/`.

Platformer levels are assembled from chunk prefabs in `Assets/GameMain/Resources/Chunks`. Chunk roots use `ChunkInfo`; markers such as `SpawnMarker`, `CheckpointMarker`, and `MonsterMarker` drive assembly metadata. Terrain and hazards live in the prefabs rather than being generated procedurally.

### Platformer invariants

- Design measurements use pixels with `PPU = 16`; convert with `GameConst.Px(float)` instead of scattering manual divisions.
- Deterministic gameplay random must use `GameApp.Random.Stream(name)`; level assembly uses the `"level"` stream. Do not use `UnityEngine.Random` or `System.Random` for reproducible game logic.
- `GameWorld.Update` owns update order: input → energy → player tick → monsters → checkpoints/progress/death state. Add new gameplay systems to this explicit order rather than independent `Update` methods when ordering matters.
- Player physics uses kinematic `Rigidbody2D`, `BoxCollider2D`, manual integration, axis-separated `Physics2D.BoxCast`, and `Physics2D.SyncTransforms()` after level assembly/teleports.
- URP 2D Light2D is in `Unity.RenderPipelines.Universal.2D.Runtime`; assemblies using `Light2D` need the corresponding asmdef reference. Runtime-created `Light2D` components must apply sorting layers, and lit sprites should use `WorldRender.LitMaterial`.
- The art direction is monochrome Limbo-style silhouettes. Background layers are unlit, low sorting order, horizontally parallaxed, and vertically fixed.

## Embedded GameFramework package

`Packages/com.jiangyin.gameframework` is an embedded UPM package and may be edited for Unity 6 compatibility, but keep such edits minimal and documented in Chinese with the reason. Existing local compatibility constraints include semver package version formatting, removal of deprecated `DeterministicAssetBundle`, and lazy initialization for `DebuggerComponent` `TextEditor` usage.

If `UnityGameFramework.Runtime.Log` conflicts with this project's logging facade, use an alias such as:

```csharp
using Log = GourmetProject.Core.Diagnostics.Log;
```
