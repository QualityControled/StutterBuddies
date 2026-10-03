# 0.1.0 validation

Checked on October 3, 2026, against the installed Windows Steam build 25576398, game v1.1.9, Unity 6000.0.60f1 and BepInEx 5.4.23.5.

Completed:

- Release compilation: zero warnings and errors, using local game/loader references.
- 15 pure helper tests: bounded frame storage, invalid samples, percentiles, hitch counts, allocation behavior, and settings ownership/restoration.
- Isolated BepInEx launch: only this plugin loaded; plugin startup confirmed in the log.
- In-game panel opening, live frame statistics, and final Ctrl+F7 shortcut.
- Native Windows working/private memory and managed memory display positive values in Unity. The original Mono process properties returned zero and were replaced with native Windows counters.
- GPU saver changed render scale 1.0 to 0.85, shadow distance 40 to 35, and MSAA 4x to 2x; disabling it restored 1.0 / 40 / 4x in the smoke test.
- Package file allowlist, root metadata, UTF-8 README, 256x256 PNG icon, BepInEx dependency and one plugin DLL.

Not established by this smoke test:

- Repeatable FPS, CPU or RAM improvements, long-session memory behavior, or comparisons across PCs.
- A complete compatibility matrix with other mods, voice, overlay modes, or large multiplayer lobbies.
- Runtime coverage of every background/vSync combination, report export, manual cleanup and settings-change conflict.

This is an experimental prerelease. Use the README's repeated A/B procedure before describing any specific performance gain. CI tests the pure helpers; it does not launch the proprietary game.
