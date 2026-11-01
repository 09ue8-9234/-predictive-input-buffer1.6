# Testing app (version 1.0.1)

A Windows desktop app for the "Testing" mod. It lists every setting, key and action of the mod, so you never edit `config.json` or type pipe commands.

## Get it
GitHub -> Actions -> latest run of "Build Testing App" -> artifact **Testing App** -> `Testing.exe`. One file, nothing to install (.NET is inside). Windows may show a SmartScreen warning because the exe is not signed: "More info" -> "Run anyway".

## Use it
1. First start: it looks for `...\Geometry Dash\geode\config\abhyu.input-buffer\` (Steam). If it can't find it, "Browse..." and pick your Geometry Dash folder. The choice is remembered (`%AppData%\Testing\app.json`).
2. **Game closed**: change anything; it is saved by itself (about a second after the last change) after a backup `config.app-backup-<time>.json`. Start the game afterwards.
3. **Game running**: file editing is locked (the game would overwrite it). Press **Connect to game** (top right) to change things live over the console link; the file is not touched, the game saves its own changes. Only one client can use the link at a time: close `tools/pipe-test.ps1` first.
4. **Console link** switch (top right) = the `console-link` setting. In the game: Ctrl+F5 (link) and Shift+F5 (console window). If the link is off while the game runs, only that key brings it back.

## Where the settings come from
Nothing is listed inside the app. It reads the mod's own schema: `schema.json` written by the mod next to `config.json` (matches the installed mod), else the copy generated from `src/cfg.hpp` by the build. A running game's schema wins while connected. `Assets/groups.json` only holds key patterns that decide the page a setting appears on; a setting without a rule shows under "More settings".

## Notes
- Per-level profiles are kept by Geode, not in `config.json`; the app edits only the global `per-level-settings` switch and marks the settings that can be per level.
- Key bindings: the mod rejects a key already used by another action, so the app does too (free the key first; swapping two keys needs two steps).
- If `config.json` has another version than the mod expects, the app is read-only: start the game once so it upgrades the file.
- Crash details: `%AppData%\Testing\crash.log`.
