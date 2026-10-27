# AutoEngineerBrakeFix - notes for Claude Code

A mod for Railroader's built-in mod loader (Unity/Mono, game 0.6.0; entry point `ModEntryPoint` in Main.cs, asset-pack Info.json, settings in Settings.xml) that improves the auto-engineer (AI): brake-drag fix, shorter cutoff, smooth throttle, gentle signal braking, speed-limit handling, stall guard, optional realistic fuel use, optional starting resistance. Namespace `AutoEngineerBrakeFix`, author Exp232.

## Build
- `dotnet build -c Release /p:DeployToGame=false` compiles against the game's DLLs (path set by `RailroaderDir` in the csproj; pass `/p:RailroaderDir=...` if the install is elsewhere).
- Do not deploy into the game's Mods folder unless asked.
- Your usual job here: build, read compile errors, fix them minimally without changing behaviour, repeat until clean, then show the diff.

## Conventions
- Harmony patches (0Harmony.dll ships with the mod). Every patch target and reflected member must be listed in `SelfCheck.cs` under the feature that needs it; private game types are taken as `object __0` and read by reflection (see `StartingResistance.cs`, `SignalPlanning.cs`).
- Target speeds and `MaxSpeedMph` are signed by direction of travel: compare magnitudes.
- Waypoint and Yard modes must keep the game's stopping accuracy; reduced accuracy only in Road mode.
- Keep defaults stable. When changing behaviour or a setting, bump the version in `Info.json`, `BUILDING.md` and the csproj together and add a `CHANGELOG.md` entry (newest first, under `# Changelog`). Update `README.md` for user-visible features.
- Game physics notes: the game uses the Davis equation for resistance, journal bearings for everything, slack of about 0.04 m per coupler, per-car static friction via `IntegrationSet.ApplyBrakes`.

## Workflow
Logs (CSV) and decompiled game code come from the user in another chat. This repo is the source of truth: if you fix something here, tell the user so they can pass the diff back.
