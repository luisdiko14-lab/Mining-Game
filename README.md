# BlockCraft

**BlockCraft** is an original voxel sandbox prototype for Windows 10/11 x64. It uses procedurally generated terrain, visuals, and audio. It does not use Mojang/Microsoft code, names, sounds, logos, or textures.

> Current status: playable foundation / MVP in active development. The current client supports a single local world, first-person movement, basic block mining and placement, a starter hotbar, a simple quick-craft action, world saving, and settings. It is not yet a feature-complete survival sandbox.

## Rendering and architecture

BlockCraft uses **MonoGame DesktopGL** for its game loop, windowing, graphics, keyboard/mouse input, and audio playback. This keeps the client focused on game code while providing a consistent C# API. The core library has no rendering dependency, so world generation, block registries, inventory logic, raycasting, and persistence can be tested separately.

- src/BlockCraft.Core: chunks, deterministic noise, terrain generation, block/item registries, inventory, crafting recipes, player physics, raycasting, and region-file storage.
- src/BlockCraft.Client: MonoGame Windows client, camera/input loop, mesh rendering, menus, HUD, settings, sound playback, and screenshots.
- tests/BlockCraft.Tests: xUnit coverage for registries, inventory, recipes, generation, mesh output, and storage.
- assets/data: JSON block, item, and recipe definitions.
- tools: Python 3 procedural asset generator (standard library only).
- scripts: Windows build, run, clean, and package scripts.
- installer: Inno Setup and WiX installer definitions.
- .github/workflows: continuous integration and tagged release pipelines.

## Implemented gameplay foundation

- 16 × 16 × 256 chunks with deterministic seeded terrain, negative-coordinate support, asynchronous chunk requests, chunk revision tracking, and compressed region-file persistence.
- Smooth deterministic multi-octave noise, plains/forest/desert/snow/ocean biomes, cave pockets, coal/iron/gold/diamond ore, water fill, and oak trees.
- JSON-driven block and item registries. The included registry defines 44 blocks and more than 50 items, including tool tiers and food definitions.
- Greedy meshing with exposed-face merging, transparent/non-solid surface support, view-frustum culling, and render-distance chunk requests.
- First-person WASD movement, mouse look, jumping, sprinting, sneaking, creative flight, axis-separated AABB collision, and voxel DDA targeting.
- Block breaking with hardness/tool speed progress, drops, block placement, a nine-slot hotbar, inventory display, basic quick crafting, pause/settings display, day/night tint, F2 screenshots, and F3 diagnostics.
- Sunlight columns and emitted block-light flood propagation, light-aware vertex shading, and corner ambient-occlusion sampling.
- Survival health, hunger, saturation/exhaustion, food consumption, natural regeneration, starvation limits, fall damage, and a game-over screen.
- Player position, camera orientation, hotbar/inventory state, world seed, and chunks are saved locally.

## Controls

| Input | Action |
| --- | --- |
| W / A / S / D | Move |
| Mouse | Look |
| Space | Jump |
| Left Shift | Sprint |
| Left Control | Sneak |
| F5 | Toggle creative flight |
| Left mouse | Mine targeted block |
| Right mouse | Place selected block |
| 1–9 / mouse wheel | Select hotbar slot |
| E | Open inventory |
| C (inventory) | Convert one oak log into four planks |
| F2 | Save screenshot to Pictures/BlockCraft |
| F3 | Toggle debug overlay |
| F11 | Toggle fullscreen |
| Esc | Pause / resume |

The pause screen exposes render distance (Page Up/Page Down), FOV (+/-), and VSync (V). Settings are stored under %LOCALAPPDATA%/BlockCraft/settings.json. The first world slot is stored under %LOCALAPPDATA%/BlockCraft/Worlds/World-1.

## Build prerequisites

- Windows 10/11 x64.
- .NET 10 SDK (the repository pins SDK 10.0.404 in global.json; a later 10.0 patch is accepted).
- Python 3.10 or newer (standard library only).
- An internet connection for the first NuGet restore.

## Build, run, and clean

From the repository root, run scripts/build.bat, scripts/run.bat, or scripts/clean.bat. PowerShell equivalents are scripts/build.ps1, scripts/run.ps1, and scripts/clean.ps1.

The build script generates the texture atlas and original WAV sound assets, restores NuGet packages, builds the solution in Release mode, and runs the tests. No separately installed Python packages are required.

To publish a portable self-contained Windows build and ZIP, run scripts/package.ps1 -Version 1.0.0. The publish output and ZIP are written to artifacts/publish/win-x64 and artifacts/BlockCraft-1.0.0-win-x64-portable.zip.

## Release process

1. Push changes to main and wait for BlockCraft CI to pass.
2. Create and push a semantic version tag, for example: git tag v1.0.0 followed by git push origin v1.0.0.
3. The BlockCraft Release workflow restores, builds, tests, generates assets, publishes the self-contained win-x64 build, creates a portable ZIP, compiles the Inno Setup setup.exe and WiX setup.msi, writes SHA-256 checksums, and publishes the files to a GitHub Release.
4. A workflow-dispatch run can also be used to validate the packaging pipeline without creating a Git tag.

Release files are versioned as BlockCraft-<version>-win-x64-portable.zip, BlockCraft-<version>-win-x64-setup.exe, BlockCraft-<version>-win-x64-setup.msi, and SHA256SUMS.txt.

## Asset policy

All included textures and sounds are generated by tools/gen_textures.py. The generator uses only Python's standard library. The repository contains no borrowed Minecraft assets or third-party game content. Generated files are build outputs and are recreated during CI and packaging.

## Current limitations

The current deliverable is the first playable foundation, not the complete feature list. Passive/hostile mobs and AI, interactive furnace/smelting UI, complete click-and-drag 2×2/3×3 crafting UI, multiple world slots, LAN multiplayer, and mod/data-pack loading are not yet implemented. The client also uses a single local player and does not include a dedicated server. These are intentionally identified as future milestones rather than represented as finished features.
