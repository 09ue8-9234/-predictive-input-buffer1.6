# History

## v2.88.5
The black live-console window is off by default and really opens and closes. Every `src/` change:
- `cfg.hpp`: `console-window` now defaults to OFF (it was ON). A `config.json` written by an older version still holds `true`, so switch it off once (Shift+F5 in the game, or the Testing app) and it stays off;
- `main.cpp`: the mod no longer opens a console window at startup. `applyConsoleSettings()` opens it with `AllocConsole` when the setting is on and closes it with `FreeConsole` when it is off, instead of only hiding it (hiding did nothing visible when Windows Terminal is the default terminal). If the game already has a console of its own (Geode's), that one is only hidden / shown. The log writer thread gets lines only while the window is on; the pipe (the app's live view) and the F8 export always get them.

## v2.88.4 (Testing app 1.0.1)
The practice-mode rule is removed (personal use only), and key bindings in config.json are loaded correctly. Every `src/` change:
- `cfg.hpp`: removed the settings `practice-only` (and its Shift+F10 toggle key), `auto-release-normal-runs` and `fix-practice-queue`. Old keys in config.json are ignored and dropped at the next save; config-version stays 3.
- `cfg.hpp`: descriptions no longer say "practice mode only", "not legit" or "verified / leaderboard" (wave / robot / dash auto-release, auto-click, TPS, TPS text, AUTO INPUTS tag).
- `cfg.hpp` fix: bindings are loaded in two steps. Each binding is first checked only for valid key text; then clashes are resolved together, so a swap such as stage F8 / export F6 is no longer lost. Rules: bindings the file did not set keep their default; a file binding that uses one of those defaults is rejected (its action goes back to its default); two file bindings with the same key: the first in table order keeps it; a rejected or losing binding gets its default if free, else it is unbound. Every change is written to the load note (console line `config: key conflicts in config.json: ...`). Live `set` still rejects a clash, as before. Defaults, `writeFileAtomic` and the stage migration are unchanged.
- `main.cpp`: removed the fields `practiceOnly`, `autoNormalRuns`, `fixPracticeQueue` and their reads; removed the practice-mode gate in front of the buffer, the two gates in wave / robot / dash auto-release and auto-click (they now act in every run), and the fix-practice-queue block.
- `main.cpp`: the AUTO INPUTS ON tag only shows when `show-auto-tag` is on and an auto option is on (no forced display). The state message's `warnings` field stays but no longer gets the TPS / outside-practice warnings. The console export no longer prints "practice only" and "normal runs". Comments reworded. Header says v2.88.4.
- The `"practice"` flag in the state message is unchanged (it reports whether practice mode is active).
- New `tests/cfg_test.cpp` (pure C++17); the app workflow runs it and `tests/prediction_policy_test.cpp` before building.
- Testing app 1.0.1: an offline save checks right before writing whether the game started (if so the change stays pending); the live-schema comparison covers the whole schema (descriptions, actions, stages, slots, presets, per-level keys, limits); a key that appears twice in config.json is saved into its last copy (the one the mod reads) and the earlier copies are removed; README fixes.

## v2.88.3 (Testing app 1.0.0)
Additive changes for the companion app. With the two new settings left on (their defaults) the behaviour is exactly v2.88.2.
- New `console-link` (on, Ctrl+F5): the named pipe is closed while it is off. New `console-window` (on, Shift+F5): hides / shows the console window. (Ctrl+F4 is taken by `unlimited-delay`.)
- The mod writes `schema.json` next to `config.json` at load. The `schema` message and that file now also carry the config version, the stage table, the presets, the custom slot keys, the per-level keys and the mod version (new fields at the end).
- `cfg.hpp` now holds the stage table, the custom slot key lists, `PROFILE_KEYS` and the preset values (`PRESETS`). `main.cpp` uses the lists and presets from there and checks its STAGES table against it at compile time. Preset values are unchanged.
- The startup line shows the real mod version.
- New `tools/schema-dump.cpp` (builds the schema from `cfg.hpp` for the app) and the `app/` folder: Testing app 1.0.0 (C# WPF, one exe built by the GitHub workflow, artifact "Testing App").

## v2.88.2
- Build fix: v2.88.1 did not compile (`healGhosts` was defined after the place that calls it). It is now defined before `intercept`. No behaviour change from v2.88.1.

## v2.88.1
- Fix: the buffer could silently stop working until the next level load. The mod's state is shared by every level, and a level's quit cleanup (`onQuit`) tore it down even when a different level had already taken it over, deleting that level's ghost players; without ghosts every prediction answers "safe" and every input passes through. A quitting level now leaves the state alone when another level owns it, and if the ghost players are ever missing while the mod is on they are rebuilt (one console line: "ghost players were missing ... rebuilt them", or "could not be rebuilt").
- The startup line says v2.88.1.

## v2.88.0
Ported from BASU's "Click Before Frames" v2.84.0 (id basu.input-buffer, the older version of this mod's lineage), by meaning, each behind its own setting. With all five new settings off the behaviour is exactly v2.87.2.
- `unlimited-delay` (off, key Ctrl+F4): no cap on how early an input may be. An early input is held back tick by tick until the first timing whose path survives, up to 24000 ticks (100 s). The stage then only matters as on / off. The search is still limited by the prediction time budget; while it is on, 'Auto time budget' no longer cuts the budget to one frame. The log / state text shows "unlimited". (A separate setting, not a new stage, so no stage number moved and config-version stays 3.)
- `ship-uses-stage` (on): the ship may use the stage's delay (or unlimited) like every other mode, never less than `ship-max-delay`. Nothing changes below the 1f stage.
- `orb-press-passthrough` (on): a press that already touches an orb goes to the game unchanged (never past a queued input), and orb alignment never re-times a press whose timing already activates the orb. This replaces the v2.26 re-timing of presses inside an orb while on; turn it off to get that back.
- `orb-same-target` (on): orb alignment only counts a timing that activates the FIRST orb ahead, for the fully safe pick and for the fallback pick; a learned timing is tried only for that orb.
- `inverted-wave` (on): upside-down wave uses a spam gap of at most 6 ticks, a minimum turn of 12 ticks, an escape of 24 ticks and a full-safety save target.
- New `src/prediction_policy.hpp` (no Geode includes) holds these rules; `tests/prediction_policy_test.cpp` tests them and the GitHub build runs the tests.
- Comment fix: the dash orb layer cleanup is labelled v2.87.2.

## v2.87.2
- Dash orb animation fix, ported from an older build of mine that was labelled v2.88 (not this version): anything a prediction (or the run check) adds to the real level's layers, such as the dash orb start effect, is removed again afterwards, so dash orbs no longer flash before you reach them. Visual cleanup only: no change to the prediction engine, hooks, settings or defaults.

## v2.87.1
- New 0.25f stage (1 tick, wave 1 tick) in front of the ladder. Every stage number is one higher: 0 = 0.25f, 1 = 0.5f, 2 = 1f, 3 = 2f, 4 = 4f, 5 = 6f, 6 = 8f, 7-10 = custom A-D, 11 = off. A fresh install still starts on 0.5f; Safe / Balanced / Aggressive keep their windows (2f / 4f with 2f wave / 8f). config.json is version 3: a stage saved by version 1-2 is converted by meaning, and the import from the old mod folder lands on the same stage as before.
- Fix toggles in config.json, off by default except the first: `fix-restart-state` (on: a manual restart drops the unscored level-memory outcome, the moving-object watch history and the spam timing), `fix-dual-harmless`, `fix-lowlat-queue`, `fix-practice-queue`, `fix-group-on`, `fix-orb-checks`, `fix-more-time-checks`, `fix-training-auto`, `fix-mover-conflict`, `fix-auto-throttle`.
- Safer files: config.json, level memory and run-check recordings are written to a temporary file and moved into place, with the results checked. A failed save is retried and noted once. An unreadable config.json is kept as config.corrupt.json before it is replaced.
- Console link: the named pipe is retried with a growing pause if it can't be created. A full command queue answers with an error and is counted (`droppedCmds`). `config-version` can't be changed with `set`.
- Keys no longer fire while the live console window is in front.
- The AUTO INPUTS tag now shows by itself whenever an automatic input can act outside practice mode (display only).
- Statistics: the ghost tick count no longer counts one tick too many on full-length predictions.
- Text: performance mode, path view and run-check descriptions say what they really do; stale comments about moving objects and portals corrected; the CBF Extrapolate note says it is assumed, not verified.

## v2.87.0
- Renamed: developer abhyu, id `abhyu.input-buffer`, pipe `abhyu-input-buffer-testing`. In-game name stays "Testing". The mod picture is blank.
- First start copies the data of the previous mod folder once (config, level memory, run-check data, per-level profiles, stage). The old folder is not changed.
- Four named custom slots A-D (stages 6-9), up to 128 ticks (wave up to 64), live up/down keys and Alt+1..4 slot keys. The stage key cycles through them. The stage is stored in config.json and the "off" stage is now number 10.
- Default keys for the in-game output toggles; new off-by-default toggles for the watermark and the level complete label.
- config.json has a version number; a file from another version is backed up before it is upgraded.
