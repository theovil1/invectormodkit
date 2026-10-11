# Contributing

Thanks for helping! This guide covers building, testing and the rules every change follows.

## Building

You need:

- Windows 10 or 11
- the [.NET SDK](https://dotnet.microsoft.com/download) 8 or later (it also builds the .NET Framework 4.8 projects)
- Node.js 20 or later (level editor tests)
- Invector: Rhythm Galaxy installed through Steam, with [BepInEx 5](https://github.com/BepInEx/BepInEx) in the game's
  folder (running the installer once is the easiest way)

The plugins reference the game's own assemblies, which cannot be redistributed. The build looks for the game in
`C:\Program Files (x86)\Steam\steamapps\common\Invector Rhythm Galaxy`. If yours is elsewhere, create
`Directory.Build.user.props` (ignored by git) next to `InvectorModKit.slnx`:

```xml
<Project>
  <PropertyGroup>
    <GameDir>D:\SteamLibrary\steamapps\common\Invector Rhythm Galaxy</GameDir>
  </PropertyGroup>
</Project>
```

Then:

```powershell
dotnet build InvectorModKit.slnx -c Release
```

Building a plugin copies it into the game's `BepInEx/plugins` folder. Pass `-p:DeployToGame=false` to skip that,
for example while the game is running.

## Layout

| Path | What it is |
| --- | --- |
| `src/InvectorModKit` | Core plugin: the API other plugins use, online safety (official scores kept apart) |
| `src/InvectorModKit.Charting` | Chart format, generator, playability rules (no Unity, tested) |
| `src/InvectorModKit.ChartGen` | Out-of-process chart generator started by the game |
| `src/InvectorModKit.CustomSongs` | Custom songs plugin |
| `src/InvectorModKit.Marketplace` | Marketplace plugin (window, leaderboards, updates) |
| `src/InvectorModKit.Marketplace.Client` | Marketplace API client (no Unity, tested) |
| `src/InvectorModKit.ChartEditor.Core` | Level editor logic (no window, tested) |
| `src/InvectorModKit.Installer` | Install, update and uninstall logic (no window, tested) |
| `src/InvectorModKit.Setup` | `InvectorModKitSetup.exe`, the installer players download |
| `tools/ChartEditor` | `InvectorLevelEditor.exe` (WinForms and WebView2) and its HTML interface |
| `tests` | xUnit tests |

## Tests

```powershell
dotnet test tests/InvectorModKit.Charting.Tests
dotnet test tests/InvectorModKit.Marketplace.Client.Tests
dotnet test tests/InvectorModKit.ChartEditor.Tests
dotnet test tests/InvectorModKit.Installer.Tests
cd tools/ChartEditor/ui
node --test tests/model.test.js tests/i18n.test.js
```

New behaviour comes with a test that fails without it.

## Rules

- **Commits and pull request titles** follow [Conventional Commits](https://www.conventionalcommits.org/), in
  English: `type(scope): description`, for example `fix(marketplace): keep the search text after closing`.
  Types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`.
- **Branches** are named `type/short-description` with the same types, for example `feat/level-editor-zoom` or
  `fix/hot-reload-selection`. Releases use `release/x.y.z` and urgent fixes `hotfix/short-description`.
- **No mention of AI tools** in commits, pull requests or code (no `Co-Authored-By` lines for assistants, no
  "generated with" notes). The CI checks it.
- **Texts players see** exist in the game's 11 languages: English, French, German, Italian, Spanish, Portuguese
  (Brazil), Polish, Russian, Japanese, Korean and Simplified Chinese.
- **No em dashes** in any text: use a colon, a comma, parentheses or a new sentence.
- Nothing from the game is committed or redistributed.

## Pull requests

1. Branch from `main` with a conventional name.
2. Open a pull request whose title is the commit message you want on `main`: pull requests are squashed.
3. The CI must pass: build and tests, level editor tests, title, branch name, no AI mention.
4. A review is required before merging.

## Releases

Releases are prepared by [Release Please](https://github.com/googleapis/release-please): it reads the commits on
`main` and keeps a pull request open that raises the version and updates `CHANGELOG.md`. Merging it creates the tag
and the GitHub release.

The files players download are built on a computer with the game installed, then attached to that release:

```powershell
./tools/release.ps1 -Version 0.2.0
gh release upload v0.2.0 artifacts/InvectorModKitSetup.exe artifacts/InvectorModKit-0.2.0.zip artifacts/InvectorLevelEditor.zip
```

The game offers an update only once `InvectorModKitSetup.exe` is attached.
