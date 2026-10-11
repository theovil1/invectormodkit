# AGENTS.md

Instructions for coding agents working in this repository. Humans: see [CONTRIBUTING.md](CONTRIBUTING.md).

## Project

InvectorModKit is a mod framework for the Unity game Invector: Rhythm Galaxy (Unity 2021.3, Mono, Steam, Windows):
custom songs with generated charts, a chart marketplace with leaderboards, a level editor, and a one-click installer.
Players are not technical: everything they see must be simple and translated.

Stack:

- Game plugins: C# for `net472`, BepInEx 5.4.23.5, HarmonyX patches, the game's publicized `Assembly-CSharp`
- Libraries without Unity: C# `netstandard2.0` (charting, marketplace client, editor core, installer)
- Level editor: WinForms `net48` + WebView2, interface in plain ES modules, three.js 0.170 vendored in `tools/ChartEditor/ui/vendor`
- Installer: WinForms `net48`, one standalone exe
- Tests: xUnit on `net8.0`, Node's built-in test runner for the editor interface

## Commands

Run from the repository root (PowerShell or Git Bash). Verify the result before saying a change works.

```powershell
dotnet build InvectorModKit.slnx -c Release -p:DeployToGame=false   # everything; plugins need the game installed
dotnet test tests/InvectorModKit.Charting.Tests
dotnet test tests/InvectorModKit.Marketplace.Client.Tests
dotnet test tests/InvectorModKit.ChartEditor.Tests
dotnet test tests/InvectorModKit.Installer.Tests
cd tools/ChartEditor/ui; node --test tests/model.test.js tests/i18n.test.js   # run from that folder
./tools/release.ps1 -Version <x.y.z>   # builds the installer and zips into artifacts/, never publishes
```

- Without `-p:DeployToGame=false`, building a plugin copies it into the game's `BepInEx/plugins`, which fails while
  the game runs (its DLLs are locked).
- The game's folder comes from `GameDir` in `Directory.Build.props` (override in `Directory.Build.user.props`).
- CI (`.github/workflows/ci.yml`) cannot build the four plugin projects: it has no game. Test logic in the
  `netstandard2.0` libraries, where CI runs it.

## Map

- `src/InvectorModKit`: core plugin, public API in `Api/`, online safety in `Internal/` (official scores kept apart)
- `src/InvectorModKit.Charting`: chart format (`ChartJson`), generator, playability rules
- `src/InvectorModKit.CustomSongs`, `src/InvectorModKit.Marketplace`: game plugins (Harmony patches, uGUI built from the game's own prefabs)
- `src/InvectorModKit.Installer` + `src/InvectorModKit.Setup`: install logic (tested) and the installer window
- `tools/ChartEditor`: level editor host (`MainForm.cs`) and interface (`ui/`, texts in `ui/i18n.js`)
- `tools/release.ps1`, `tools/release/`: release build and the files shipped with it

## Rules

- Commits and pull request titles: Conventional Commits in English, `type(scope): description`.
- Branches: `type/short-description` (`feat/`, `fix/`, `docs/`, `refactor/`, `test/`, `build/`, `ci/`, `chore/`...).
- Never write a `Co-Authored-By` line, a "generated with" note, or any mention of an AI tool in commits, pull
  requests or code. CI rejects them.
- Every text a player sees exists in the 11 languages, in this order: en, fr, de, it, es, pt-BR, pl, ru, ja, ko, zh
  (game plugins: `Loc.cs`; editor: `ui/i18n.js`; installer: `SetupTexts.cs`). Tests check every row is complete.
- Never use em dashes in any text (UI, docs, commits): use a colon, a comma or a new sentence.
- Write a failing test first for new behaviour in a tested project, then make it pass.
- Comments say what code is for, in the same terse style as the surrounding code.

## Gotchas

- Game plugins target `net472` and bind to the game's Newtonsoft.Json 12. The marketplace client pins 12.0.3 for that reason.
- Never put mod data into the game's save (`UserData`): a failed load silently resets the player's save.
- Official leaderboards and achievements must stay untouched while the mod is active (`ModKit.IsModdedSession`, `Internal/OnlineGuard.cs`).
- The installer must stay a single exe: `src/InvectorModKit.Installer` has no NuGet dependency and its sources are
  compiled into the Setup project.
- Steam stores library paths in lower case: show paths to players with their real casing.

## Boundaries

Always:

- Run the tests of every project you touched, and read their output.
- Keep each change focused; follow the existing patterns of the file you edit.

Ask first:

- Adding a NuGet or npm dependency, changing a workflow in `.github/`, changing the chart file format.
- Anything that runs or closes the game, or changes files in the game's folder.
- Creating a release, a tag, or uploading files to GitHub.

Never:

- Commit anything from the game (its assemblies, assets, audio) or redistribute it.
- Commit secrets, tokens or Steam tickets; never read `server/.dev.vars`.
- Push to `main` directly or force-push it: everything goes through a pull request.
- Touch the game's original files or the player's save.
