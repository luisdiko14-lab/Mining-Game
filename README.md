# BlockCraft

**Mining Game** (internal client assembly: BlockCraft) is an original voxel sandbox adventure for Windows 10/11 x64. It uses procedurally generated terrain, visuals, and audio. It does not use Mojang/Microsoft code, names, sounds, logos, or textures.

> Current status: playable foundation / MVP in active development. The current client supports a single local world, first-person movement, basic block mining and placement, a starter hotbar, a simple quick-craft action, world saving, and settings. It is not yet a feature-complete survival sandbox.

## Rendering and architecture

BlockCraft uses **MonoGame WindowsDX / Direct3D 11** for its game loop, windowing, graphics, keyboard/mouse input, and audio playback. This keeps the client focused on game code while providing a consistent C# API. The core library has no rendering dependency, so world generation, block registries, inventory logic, raycasting, and persistence can be tested separately.

- src/BlockCraft.Core: chunks, deterministic noise, terrain generation, block/item registries, inventory, crafting recipes, player physics, raycasting, and region-file storage.
- src/BlockCraft.Client: MonoGame Windows client, camera/input loop, mesh rendering, menus, HUD, settings, sound playback, and screenshots.
- tests/BlockCraft.Tests: xUnit coverage for registries, inventory, recipes, generation, mesh output, and storage.
- assets/data: JSON block, item, and recipe definitions.
- tools: Python 3 procedural asset generator (standard library only).
- scripts: Windows build, run, clean, and package scripts.
- installer: Inno Setup and WiX installer definitions.
- .github/workflows: continuous integration and tagged release pipelines.

## Implemented gameplay foundation

- 16 × 16 × 256 chunks with deterministic seeded terrain, negative-coordinate support, asynchronous chunk requests, chunk revision tracking, distance-based chunk unloading, and batched compressed region-file persistence.
- Smooth deterministic multi-octave noise, plains/forest/desert/snow/ocean biomes, cave pockets, coal/iron/gold/diamond ore, water fill, and oak trees.
- Expanded JSON-driven registries with 163 blocks and 220 items: eight wood families and planks, tree species, stone variants, deepslate ores, Nether/End-inspired materials, utility blocks, colored wool/concrete, extra tools, and survival foods.
- Greedy meshing with exposed-face merging, transparent/non-solid surface support, neighbor-only remeshing, stale-mesh rejection, view-frustum culling, and render-distance chunk streaming.
- First-person WASD movement, mouse look, jumping, sprinting, sneaking, creative flight, axis-separated AABB collision, and voxel DDA targeting.
- Block breaking with hardness/tool speed progress, minimum pickaxe harvest tiers, selectable non-solid torches, block-specific drops, block placement, a nine-slot hotbar, inventory display, recipe browsing and transactional quick crafting from stored ingredients, pause/settings display, day/night tint, F2 screenshots, and F3 diagnostics.
- Additional jungle/savanna regions, mixed tree species, stone veins, deep ores, and rare ancient-debris deposits.
- Amethyst geodes with hollow crystal chambers, abandoned timber mineshafts, ceiling-grown glowcaps, fossil fragments, echo ore, resonance lanterns, and relic crafting.
- Persistent local achievements and an exploration journal (press J) tracking mining milestones, discoveries, placed blocks, explored chunks, and deepest depth.
- Eighteen original 60-second stereo soundtrack tracks that adapt to biome, depth, night, underwater areas, crystal caves, abandoned mines, and rare discoveries.
- A Windows launch guard: Mining_Game.exe opens the diagnostic GUI, starts BlockCraft.exe, monitors its exit, and collects the game crash report and standard output/error into a support log when startup fails or the game exits with an error.
- A Java 21 content-pack audit for block/item references, crafting and smelting recipes, and stack limits.
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
| E (inventory) | Close inventory |
| Up / Down (inventory) | Select a crafting recipe |
| C (inventory) | Quick-craft the selected recipe from inventory ingredients |
| F2 | Save screenshot to Pictures/BlockCraft |
| F3 | Toggle debug overlay |
| F11 | Toggle fullscreen |
| Esc | Pause / resume |

### Touchscreen controls

On devices that expose a MonoGame touch panel, BlockCraft shows a virtual control overlay during gameplay:

- Drag the left thumbstick to move. The thumb follows your finger, movement is analog, and travel is clamped to the stick radius.
- Release the left thumbstick to snap it back to the center and stop movement.
- Swipe on the right side of the screen to look around.
- Use the on-screen JUMP, MINE, and PLACE buttons for the main actions.

Keyboard and mouse controls remain available. The current release target is still Windows x64; this touch layer is ready for touch-enabled MonoGame hosts, but an Android/iOS app project and mobile installer are not part of this Windows release.

The pause screen exposes render distance (Page Up/Page Down), FOV (+/-), and VSync (V). Settings are stored under %LOCALAPPDATA%/BlockCraft/settings.json. The first world slot is stored under %LOCALAPPDATA%/BlockCraft/Worlds/World-1.

## Build prerequisites

- Windows 10/11 x64.
- .NET 10 SDK (the repository pins SDK 10.0.404 in global.json; a later 10.0 patch is accepted).
- Python 3.10 or newer, plus Pillow and NumPy from tools/requirements.txt for icon, texture, and soundtrack generation.
- Java 21 and Maven for the optional Java content-pack audit (also run in CI).
- An internet connection for the first NuGet restore.

## Build, run, and clean

From the repository root, run scripts/build.bat, scripts/run.bat, or scripts/clean.bat. PowerShell equivalents are scripts/build.ps1, scripts/run.ps1, and scripts/clean.ps1.

The build script generates the original texture atlas, the game icon, the short sound effects, and the 18-track original soundtrack (about 190 MB of uncompressed WAV audio), restores NuGet packages, builds the solution in Release mode, and runs the tests. The Java audit is an additional build-time check and does not add a Java runtime dependency to the game. No separately installed Python packages are required.

To publish a portable self-contained Windows build and ZIP, run scripts/package.ps1 -Version 1.0.0. The package contains BlockCraft.exe, the Mining_Game.exe launch guard, Mining_Game_Error_Checker.exe, the original generated textures, and the full soundtrack. The soundtrack payload is checked to exceed 180 MB before packaging.

## Release process

1. Push changes to main and wait for BlockCraft CI to pass.
2. Create and push a semantic version tag, for example: git tag v1.0.0 followed by git push origin v1.0.0.
3. The BlockCraft Release workflow restores, builds, tests, generates assets, publishes the self-contained win-x64 build, creates a portable ZIP, compiles the Inno Setup setup.exe and WiX setup.msi, writes SHA-256 checksums, and publishes the files to a GitHub Release.
4. A workflow-dispatch run can also be used to validate the packaging pipeline without creating a Git tag.

Release files are versioned as BlockCraft-<version>-win-x64-portable.zip, BlockCraft-<version>-win-x64-setup.exe, BlockCraft-<version>-win-x64-setup.msi, and SHA256SUMS.txt.

## Asset policy

All included textures and sounds are generated by tools/gen_textures.py. The generator uses only Python's standard library and deterministic per-texture seeds. The repository contains no borrowed Minecraft assets, Mojang code, game-vendor sounds, or third-party game content. Generated files are build outputs and are recreated during CI and packaging.

## Current limitations

The current deliverable is still an actively developed single-player voxel sandbox, not a feature-complete commercial-scale game. Passive/hostile mobs and AI, interactive furnace/smelting UI, complete click-and-drag 2×2/3×3 crafting UI, multiple world slots, LAN multiplayer, and mod/data-pack loading are not yet implemented. The client also uses a single local player and does not include a dedicated server. These are intentionally identified as future milestones rather than represented as finished features.


## Graphics compatibility

The Windows build uses MonoGame's **WindowsDX / Direct3D 11** backend instead of DesktopGL/OpenGL. This avoids requiring the OpenGL `ARB_framebuffer_object` or `EXT_framebuffer_object` extensions that are missing on some older graphics drivers. Windows 10/11 systems still need a graphics adapter and driver capable of Direct3D 10 or newer. Update the graphics driver from the GPU manufacturer if Direct3D device creation fails.
