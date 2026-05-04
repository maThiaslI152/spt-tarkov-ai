# Mod build outputs and install locations

This workspace builds **BepInEx client plugins**, optional **BepInEx patchers**, and **SPT server** DLLs. Paths below use **`{SPT}`** as your SPT / Tarkov install root (the folder that contains `BepInEx/` and `EscapeFromTarkov.exe`).

**Automated copy targets:** `OptimizedMod/Directory.Build.props` defines `SPTRoot` (default `E:\SPT 4.0 Dev`). Many `.csproj` files copy build outputs into `{SPTRoot}\...` after build. If copies go to the wrong place, edit `SPTRoot` to match your install, or copy the DLLs manually using the table.

**Relative to repo:** all `OptimizedMod/` projects live under `e:\spt-tarkov-ai\OptimizedMod\` (adjust for your clone).

---

## What to rebuild when code changes

Use this section as the default rule during development:

- If you edit code in a mod project, rebuild that project’s `.csproj`.
- If you edit shared contracts/types in `OptimizationCore`, rebuild `OptimizationCore` and every consumer that references those types.
- If you edit SAIN code used by AILimit/SAINPerfLog integration paths, rebuild `SAIN`, then rebuild `AILimit` and `SAINPerfLog`.

### Recommended compile set for the optimized runtime stack

For runtime `E:\SPT 4.0 Dev`, this is the standard client stack to rebuild when updated:

| Code changed in | Build this project | Deploy to |
|-----------------|--------------------|-----------|
| `OptimizedMod/SAIN/SAIN/**` | `OptimizedMod/SAIN/SAIN.csproj` | `E:\SPT 4.0 Dev\BepInEx\plugins\SAIN\` |
| `OptimizedMod/SAINPerfLog/**` | `OptimizedMod/SAINPerfLog/SAINPerfLog.csproj` | `E:\SPT 4.0 Dev\BepInEx\plugins\SAINPerfLog\` |
| `OptimizedMod/AILimit/**` | `OptimizedMod/AILimit/AILimit.csproj` | `E:\SPT 4.0 Dev\BepInEx\plugins\` (root) |
| `OptimizedMod/BigBrain/**` | `OptimizedMod/BigBrain/DrakiaXYZ-BigBrain.csproj` | `E:\SPT 4.0 Dev\BepInEx\plugins\` (root) |
| `OptimizedMod/Waypoints/**` | `OptimizedMod/Waypoints/DrakiaXYZ-Waypoints.csproj` | `E:\SPT 4.0 Dev\BepInEx\plugins\DrakiaXYZ-Waypoints\` |
| `OptimizedMod/LootingBots/LootingBots/**` | `OptimizedMod/LootingBots/LootingBots/LootingBots.csproj` | `E:\SPT 4.0 Dev\BepInEx\plugins\` (root) |
| `OptimizedMod/OptimizationCore/**` | `OptimizedMod/OptimizationCore/OptimizationCore.csproj` | `E:\SPT 4.0 Dev\BepInEx\plugins\` (root) |

### Full-stack command (client runtime)

Run from repo root:

```powershell
$projects = @(
  'OptimizedMod/OptimizationCore/OptimizationCore.csproj',
  'OptimizedMod/SAIN/SAIN.csproj',
  'OptimizedMod/AILimit/AILimit.csproj',
  'OptimizedMod/SAINPerfLog/SAINPerfLog.csproj',
  'OptimizedMod/BigBrain/DrakiaXYZ-BigBrain.csproj',
  'OptimizedMod/Waypoints/DrakiaXYZ-Waypoints.csproj',
  'OptimizedMod/LootingBots/LootingBots/LootingBots.csproj'
)
foreach ($p in $projects) {
  dotnet build $p -c Release
  if ($LASTEXITCODE -ne 0) { break }
}
```

`SPTRoot` in `OptimizedMod/Directory.Build.props` controls post-build copy targets. Current default is `E:\SPT 4.0 Dev`.

---

## Quick reference table (OptimizedMod)

| Source project (build this) | Output DLL (typical Release) | Copy to (game / SPT) |
|----------------------------|------------------------------|----------------------|
| `SAIN/SAIN.csproj` | `SAIN.dll` | `{SPT}\BepInEx\plugins\SAIN\` (entire output tree: DLL + satellites next to it) |
| `SAINPerfLog/SAINPerfLog.csproj` | `SAINPerfLog.dll` | `{SPT}\BepInEx\plugins\SAINPerfLog\` |
| `BigBrain/DrakiaXYZ-BigBrain.csproj` | `DrakiaXYZ-BigBrain.dll` | `{SPT}\BepInEx\plugins\` (**root** of `plugins`, not a subfolder) |
| `Waypoints/DrakiaXYZ-Waypoints.csproj` | `DrakiaXYZ-Waypoints.dll` | `{SPT}\BepInEx\plugins\DrakiaXYZ-Waypoints\` |
| `AILimit/AILimit.csproj` | `dvize.AILimit.dll` | `{SPT}\BepInEx\plugins\` (**root** of `plugins`) |
| `LootingBots/LootingBots/LootingBots.csproj` | `skwizzy.LootingBots.dll` | `{SPT}\BepInEx\plugins\` (**root** of `plugins`) — matches this fork’s post-build |
| `ABPS/Client/acidphantasm-botplacementsystem.csproj` | `acidphantasm-botplacementsystem.dll` | `{SPT}\BepInEx\plugins\acidphantasm-botplacementsystem\` |
| `ABPS/Server/acidphantasm-botplacementsystem.csproj` | `acidphantasm-botplacementsystem.dll` (+ server payload) | Pulled into `{SPT}\SPT\user\mods\acidphantasm-botplacementsystem\` when you build the **client** project (see client `PostBuild` target) |
| `MoreBotsAPI/Plugin/Plugin.csproj` | `MoreBotsPlugin.dll` | `{SPT}\BepInEx\plugins\MoreBotsAPI\` |
| `MoreBotsAPI/Prepatch/Prepatch.csproj` | `MoreBotsPrepatch.dll` | `{SPT}\BepInEx\patchers\` |
| `MoreBotsAPI/Server/MoreBotsServer.csproj` | `MoreBotsServer.dll` | `{SPT}\SPT\user\mods\MoreBotsServer\` (post-build uses mod folder name = target assembly name) |
| `OptimizationCore/OptimizationCore.csproj` | `OptimizationCore.dll` | `{SPT}\BepInEx\plugins\` (**root** of `plugins`) — reference library; ship only if your stack expects it beside other plugins |

### Legacy / alternate MoreBots client project

| Source | Output | Copy to |
|--------|--------|---------|
| `MoreBotsAPI/MoreBotsPlugin.csproj` (old-style csproj) | `TacticalToasterUNTARGH.dll` | `{SPT}\BepInEx\plugins\` (root) — prefer `Plugin/Plugin.csproj` + `MoreBotsAPI` folder for new layouts |

---

## Server-only projects (manual copy to SPT unless you wire your own path)

These write under **`OptimizedMod/.../Build/SPT/user/mods/...`** by default (staging). For a real server, mirror that layout under **`{SPT}\SPT\user\mods\<ModFolder>\`**.

| Source project | Staging / mod folder name | Contents |
|----------------|---------------------------|----------|
| `SAIN/SAINServerMod/SAINServerMod.csproj` | `Solarint-SAIN-ServerMod` | Server DLL, `Data\`, `wwwroot\` as produced by the project |
| `LootingBots/LootingBotsServerMod/LootingBotsServerMod.csproj` | `Skwizzy-LootingBots-ServerMod` | Server DLL + `Config\config.json` (Release post-build copies to staging) |

Debug-only `SAINServerMod` can also copy toward a local `SPTarkov.Server` debug tree; see `SAINServerMod.csproj` target `CopyToServerMods`.

---

## SPTQuestingBots (sibling folder `SPTQuestingBots/`)

**Not part of the OptimizedMod stack:** this tree is kept for **reading** and **pattern copy**; you only build/install it if you want QuestingBots as a separate mod. Full-stack Release deploys under [AGENTS.md](AGENTS.md) cover **`OptimizedMod/`** only.

If you do build QuestingBots yourself, paths are:

Build variable **`AssemblyTitle`** = `QuestingBots` (see `SPTQuestingBots/Directory.Build.props`).

| Project | Output | Copy to (per PowerShell scripts) |
|---------|--------|----------------------------------|
| `Client/QuestingBots-Client.csproj` | `QuestingBots-Client.dll` | `{SPT}\BepInEx\plugins\QuestingBots\` plus `Quests\` subtree from `Shared/Quests` |
| `Server/QuestingBots-Server.csproj` | `QuestingBots-Server.dll` | `{SPT}\SPT\user\mods\QuestingBots\` |

Interop example projects copy to their own plugin subfolders (`QuestingBots-CustomBotGenExample`, etc.); see each project’s `CopyFilesAfterBuild.ps1`.

---

## Local `bin` paths (when not using post-build copy)

Typical SDK outputs (framework may vary):

- **netstandard2.1 client mods:** `OptimizedMod/<Mod>/bin/Release/netstandard2.1/<AssemblyName>.dll`
- **ABPS client:** `OptimizedMod/ABPS/Client/bin/Release/acidphantasm-botplacementsystem.dll`
- **.NET 9 server mods:** `OptimizedMod/<Mod>/Server/bin/Release/<project>/<assembly>/...`

---

## Repo `build-output/` folder

`INDEX.md` mentions **`build-output/`** for compiled DLLs used in some workflows. It is **not** automatically populated by every project; prefer the table above or each `.csproj` post-build for authoritative deploy paths.

---

## Related docs

- [AGENTS.md](AGENTS.md) — primary `dotnet build` commands  
- [SAIN_PERFLOG.md](SAIN_PERFLOG.md) — telemetry output under `BepInEx/LogOutput/sain_perf/`  
- [SAIN_FORK_PRESET.md](SAIN_FORK_PRESET.md) — SAIN preset files next to `SAIN.dll`
