# Discord rich presence

## Intent

The game has no Discord integration: Discord only detects the process and shows "Playing Invector: Rhythm Galaxy".
The mod should show what the player is actually doing, mainly the game in progress, so friends see the song, the
difficulty, the score and how much is left.

## Goals

- During a level: song title and artist (official or custom), difficulty (Casual to Ultra), live score and combo,
  time remaining shown by Discord's own countdown.
- Outside a level: one line per screen (main menu, song select, marketplace, results), with total elapsed play
  time.
- The level editor (InvectorLevelEditor.exe, a separate app that runs with or without the game) shows its own
  presence: "Making a level", the song being edited, the difficulty being worked on and its note count, and the time
  since the editor opened. While the editor runs, the game clears its own activity so only the editor's card shows.
  Same buttons, texts in the editor's language.
- Two buttons: "Get InvectorModKit" (latest release page) and "Join the Discord" (the existing `Community.Discord`
  setting; the button is dropped when the setting is empty).
- Texts in the game's language, the 11 languages the mod already supports.

## Non-goals

- Song covers or per-world images: the large image is always the game logo (one asset on the Discord application).
  A small image may carry the difficulty (one asset per difficulty).
- Join/spectate invites, Discord login, any server change.

## Anti-goals

- Anything that slows the game or stutters a level: all Discord traffic runs off the main thread.
- Errors or log spam when Discord is closed, not installed, or restarted mid-game: the mod retries quietly.
- Shipping a new native or third-party DLL.

## Constraints

- Discord accepts about one activity update every 15 seconds: score/combo refresh at that pace, never faster.
  Screen and song changes are sent as soon as the rate limit allows.
- A Discord application (developer portal, Theo's account) named "Invector: Rhythm Galaxy" holds the name shown and the
  image assets. Its application ID becomes a config setting with that default.
- Players can turn the feature off with a config setting.

## Approaches

1. **Own IPC client (chosen).** Talk to the local Discord client over its named pipe (`discord-ipc-0` to `-9`,
   handshake then `SET_ACTIVITY` frames, JSON via the game's Newtonsoft). About 200 lines, no extra file, unit-testable,
   and we control rate limiting and reconnects.
2. Lachee's DiscordRichPresence library: less code, but one more DLL for the installer to ship and keep current.
3. Discord's native Game SDK: heavy, being replaced by Discord, stricter license.

## Design

- A pure, tested part (in the marketplace client, `Discord/` folder, run by CI): the pipe framing, the activity builder (game state in,
  Discord activity JSON out), and the update scheduler (rate limit, coalescing, reconnect with backoff).
- A thin game part, inside the Marketplace plugin (it already holds `Loc` and the Discord invite), that reads `ModKit.CurrentState`, the level and difficulty from
  `StateGame.gameSession` (as the marketplace leaderboard already does), score/combo and song position while playing,
  and hands snapshots to the scheduler. Custom songs get their title/artist from CustomSongs.
- The editor host references the same client library: its interface sends the song, difficulty, note count and
  localized texts to the host, which feeds the same scheduler.
- No new plugin or DLL: the installer is unchanged.

## Left to the builder

Exact screen wording, where the score/combo/position live in the game's code,
and the asset names on the Discord application.
