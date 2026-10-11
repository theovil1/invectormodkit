# InvectorModKit

Mod framework for **Invector: Rhythm Galaxy**: play your own songs, share charts on a marketplace with
leaderboards, and edit levels with a visual level editor.

**[Download the installer](https://github.com/theovil1/invectormodkit/releases/latest/download/InvectorModKitSetup.exe)**
(Windows, free, one click)

## What you get

- **Custom songs**: drop an MP3, OGG or WAV file in the game's `CustomSongs` folder and play it. A chart is
  generated for every difficulty, following the vocals and the beat.
- **Marketplace**: in the main menu, browse charts made by other players, install them in one click and publish
  your own. Each chart has its own leaderboards, with your Steam friends.
- **Level editor** (optional): place notes, long notes, turns and zones on a timeline, hear the music, preview the
  level in 3D the way the game shows it, then send it to the game, where it reloads by itself.
- **Updates in the game**: when a new version is out, the main menu offers it. It installs when you quit the game.
- **Everything in the game's 11 languages.**

Official leaderboards, achievements and your save are never touched: while the mod is active, official scores are
kept apart.

## Install

1. [Download `InvectorModKitSetup.exe`](https://github.com/theovil1/invectormodkit/releases/latest/download/InvectorModKitSetup.exe).
2. Run it. If Windows shows "Windows protected your PC", click **More info**, then **Run anyway** (the installer
   is not signed, which costs money every year).
3. The installer finds the game through Steam. Click **Install**, then **Play**.

You need Windows 10 or 11 and the Steam version of the game.

### Manual install

If your antivirus blocks the installer, download `InvectorModKit-<version>.zip` from the
[latest release](https://github.com/theovil1/invectormodkit/releases/latest) and extract it into the game's folder
(in Steam: right-click the game, **Manage**, **Browse local files**). The zip contains a README with the details.

## Uninstall

Open Windows **Settings**, **Apps**, **Installed apps**, find **InvectorModKit** and choose **Uninstall**, or run the
installer again and click **Uninstall**. Your songs stay in the `CustomSongs` folder.

## Questions

- **My antivirus deleted `winhttp.dll`.** That file is how BepInEx, the mod loader, starts with the game. Allow it in
  your antivirus, or use the manual zip.
- **The game was updated and the mod stopped working.** Check for a new version of the mod (the main menu offers it,
  or download the installer again).
- **Something does not work.** [Open an issue](https://github.com/theovil1/invectormodkit/issues/new/choose) and
  attach `BepInEx/LogOutput.log` from the game's folder.

## Building from source

See [CONTRIBUTING.md](CONTRIBUTING.md). The plugins are compiled against the game installed on your computer.

## Licence

InvectorModKit is released under the [MIT licence](LICENSE). The installer ships BepInEx and other third-party
components under their own licences, listed in [THIRD-PARTY-NOTICES.txt](tools/release/THIRD-PARTY-NOTICES.txt).

This is a fan-made mod. It is not affiliated with or endorsed by Hello There Games or the publishers of Invector:
Rhythm Galaxy. You need your own copy of the game.
