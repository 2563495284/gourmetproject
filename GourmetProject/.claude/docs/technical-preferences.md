# Technical Preferences

<!-- Populated by /setup-engine. Updated as the user makes decisions throughout development. -->
<!-- All agents reference this file for project-specific standards and conventions. -->

## Engine & Language

- **Engine**: Unity 6000.3.14f1 (Unity 6.3 LTS)
- **Language**: C#
- **Rendering**: Universal Render Pipeline (URP 17.3.0)
- **Physics**: Unity Physics (PhysicsX)

## Input & Platform

<!-- Written by /setup-engine. Read by /ux-design, /ux-review, /test-setup, /team-ui, and /dev-story -->
<!-- to scope interaction specs, test helpers, and implementation to the correct input methods. -->

- **Target Platforms**: PC (Steam/Epic)
- **Input Methods**: Keyboard/Mouse
- **Primary Input**: Keyboard/Mouse
- **Gamepad Support**: Partial (recommended — not required)
- **Touch Support**: None
- **Platform Notes**: Desktop-first design. Mouse hover states required. No touch interactions.

## Naming Conventions

- **Classes**: PascalCase (e.g., GameApp, JsonSaveService)
- **Public properties/fields**: PascalCase (e.g., MoveSpeed, Tag)
- **Private fields**: _camelCase (e.g., _servicesInitialized, _isGrounded)
- **Methods**: PascalCase (e.g., TakeDamage(), GetCurrentHealth())
- **Files**: PascalCase matching class (e.g., GameApp.cs)
- **Scenes/Prefabs**: PascalCase (e.g., MainMenuForm.prefab, Launch.unity)
- **Constants**: PascalCase (e.g., Tag) or UPPER_SNAKE_CASE
- **Namespaces**: GourmetProject.[Layer].[Module]

## Performance Budgets

- **Target Framerate**: 60 fps
- **Frame Budget**: 16.6 ms
- **Draw Calls**: 500 (PC target)
- **Memory Ceiling**: [TO BE CONFIGURED]

## Testing

- **Framework**: NUnit (Unity Test Framework — already in use)
- **Minimum Coverage**: [TO BE CONFIGURED]
- **Required Tests**: Save/load correctness, RNG determinism, gameplay balance formulas

## Forbidden Patterns

<!-- Add patterns that should never appear in this project's codebase -->
- [None configured yet — add as architectural decisions are made]

## Allowed Libraries / Addons

<!-- Add approved third-party dependencies here -->
- [None configured yet — add as dependencies are approved]

## Architecture Decisions Log

<!-- Quick reference linking to full ADRs in docs/architecture/ -->
- [No ADRs yet — use /architecture-decision to create one]

## Engine Specialists

<!-- Written by /setup-engine when engine is configured. -->
<!-- Read by /code-review, /architecture-decision, /architecture-review, and team skills -->
<!-- to know which specialist to spawn for engine-specific validation. -->

- **Primary**: unity-specialist
- **Language/Code Specialist**: unity-specialist (C# review — primary covers it)
- **Shader Specialist**: unity-shader-specialist (Shader Graph, HLSL, URP/custom render features)
- **UI Specialist**: unity-ui-specialist (UGUI Canvas, runtime UI prefabs)
- **Additional Specialists**: unity-dots-specialist (ECS, Jobs system, Burst compiler), unity-addressables-specialist (asset loading, memory management, content catalogs)
- **Routing Notes**: Invoke primary for architecture and general C# code review. Invoke shader specialist for URP render features, custom render passes, and VFX. Invoke UI specialist for all UGUI implementation. Invoke DOTS specialist for any ECS/Jobs/Burst code. Invoke Addressables specialist for asset management systems.

### File Extension Routing

<!-- Skills use this table to select the right specialist per file type. -->

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (.cs files) | unity-specialist |
| Shader / material files (.shader, .shadergraph, .mat) | unity-shader-specialist |
| UI / screen files (Canvas prefabs, UGUI components) | unity-ui-specialist |
| Scene / prefab / level files (.unity, .prefab) | unity-specialist |
| Native extension / plugin files (.dll, native plugins) | unity-specialist |
| General architecture review | unity-specialist |
