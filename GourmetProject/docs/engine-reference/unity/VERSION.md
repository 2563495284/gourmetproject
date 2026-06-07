# Unity — Version Reference

| Field | Value |
|-------|-------|
| **Engine Version** | Unity 6000.3.14f1 (Unity 6.3 LTS) |
| **Project Pinned** | 2026-06-07 |
| **LLM Knowledge Cutoff** | May 2025 |
| **Risk Level** | MEDIUM — Unity 6.0 is within training data; 6.3 LTS patches may include API changes not fully covered |

## Knowledge Gap Warning

Unity rebranded from year-based versioning (2023.x) to Unity 6 (6000.x) in October 2024.
The LLM has solid coverage of Unity 2023 and Unity 6.0, but 6.1–6.3 patches and LTS
refinements should be cross-referenced against official docs before applying suggestions.

## Post-Cutoff Risk Areas

Always verify before using these APIs or patterns:

| Subsystem | Risk | What to Check |
|-----------|------|---------------|
| **URP Render Graph** | HIGH | Unity 6 replaced the old `ScriptableRenderPass` approach with a new Render Graph API. `AddRenderPasses` override and old `Execute()` signatures are deprecated. Verify your custom render features use the new `RecordRenderGraph()` API. |
| **Input System** | MEDIUM | New Input System 1.7+ may have binding path changes. Verify action map format and binding composite syntax. |
| **Addressables** | MEDIUM | Addressables 2.x has breaking changes from 1.x (RuntimeKey, LoadAssetAsync return types). Confirm version in use. |
| **Physics (PhysicsX)** | MEDIUM | Unity 6 ships PhysicsX (formerly Havok Physics for Unity). Some Rigidbody and collider APIs may have behavioural differences from Classic PhysX. |
| **UI Toolkit** | LOW | Broadly stable in Unity 6, but runtime panel event routing changed in 6.0. Verify USS selectors and runtime document ordering. |
| **Build Pipeline** | LOW | New Build Profile system replaces Build Settings in Unity 6. `BuildPipeline.BuildPlayer` still works but new APIs exist. |
| **Awaitable / async-await** | MEDIUM | Unity 6 introduced `Awaitable` as a first-class async pattern. Prefer `Awaitable` over custom coroutine wrappers for new code, but verify API surface matches training data. |

## Render Feature Note (Project-Specific)

This project uses `GrotesqueCartoonRenderFeature` — a custom URP Render Feature.
Unity 6 URP uses **Render Graph** which deprecates the old Compatibility Mode API:

- `OnCameraSetup()` → `RecordRenderGraph()` (new required override)
- `ScriptableRenderPass.Execute()` → `IUnsafeRenderGraphBuilder` or `IRasterRenderGraphBuilder` pattern

If this render feature was written for Unity 2022/2023, verify it uses Compatibility Mode or
migrate it to Render Graph. Check: `https://docs.unity3d.com/6000.0/Documentation/Manual/urp/render-graph-introduction.html`

## Verified Sources

- Official docs: https://docs.unity3d.com/6000.3/Documentation/Manual/
- Unity 6 upgrade guide: https://docs.unity3d.com/6000.0/Documentation/Manual/UpgradeGuide6.html
- URP Render Graph: https://docs.unity3d.com/6000.0/Documentation/Manual/urp/render-graph-introduction.html
- Unity 6 changelog: https://unity.com/releases/editor/whats-new/6000.0.0

## Re-verification

Run `/setup-engine refresh` to update this file with live docs when web search is available.
Last verified: 2026-06-07 (based on LLM training data — not live web fetch)
