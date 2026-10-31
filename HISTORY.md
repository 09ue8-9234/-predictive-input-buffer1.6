# History

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
