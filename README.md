# Stutter Buddies 0.1.0

**Co-working shouldn't be co-stuttering.**

An original, experimental client-side performance tuning and diagnostics mod for **On-Together: Virtual Co-Working** on Windows, using BepInEx 5 and Unity Mono.

Stutter Buddies reduces background rendering work and offers optional graphics tradeoffs. It measures frame times and process resource use so you can compare settings on your own PC. **No performance improvement has been benchmarked for this release yet.** Passing the helper tests or compiling the plugin does not verify in-game behavior or compatibility.

The Windows build has passed a limited in-game smoke test; details and remaining coverage are in [VALIDATION.md](https://github.com/QualityControled/StutterBuddies/blob/main/VALIDATION.md).

## What it does

- **Background saver, enabled by default:** targets 30 FPS while the game is unfocused. Foreground settings are preserved by default. A lower existing positive FPS target is respected. Background mode temporarily turns off vSync so its cap can take effect; returning to the game restores settings still owned by this mod.
- **Optional GPU saver, disabled by default:** caps URP render scale at 0.85, shadow distance at 35 world units, and MSAA at 2x. This can soften the image, shorten shadows, and change edge quality. Existing lower values stay lower.
- **Local diagnostics:** mean frame time, average FPS, nearest-rank P95/P99, frames over 50 ms, process working/private memory, managed memory, and CPU usage. Storage is bounded to 12,000 frame samples, with process statistics sampled once per second.
- **Manual unused-asset cleanup:** requires a second click within five seconds, permits one cleanup at a time, and waits 60 seconds after completion before allowing another. It may cause a visible freeze. There is no automatic cleanup timer and no forced garbage collection.

The inspected game already schedules its own asset cleanup every 30 minutes. Stutter Buddies leaves that schedule intact. Manual cleanup is a troubleshooting option, not a demonstrated repair for memory leaks; assets still in use cannot be reclaimed this way.

## Installation

Use a separate test profile and close the game before adding or removing the plugin.

1. Set up a BepInEx 5 profile for On-Together in Thunderstore Mod Manager or r2modman, and launch it once to create its folders. The community BepInExPack version is `5.4.2305` (BepInEx `5.4.23.5`).
2. In the mod manager, open **Settings → Browse profile folder**. Create `BepInEx/plugins/StutterBuddies` inside that profile.
3. Copy **only `StutterBuddies.dll`** from `BepInEx/plugins/StutterBuddies/` in the ZIP into that folder. Do not copy Unity, game, or BepInEx reference DLLs.
4. Launch the modded profile and press **Ctrl+F7**. Look for `Stutter Buddies 0.1.0 ready` in `BepInEx/LogOutput.log` if the panel does not appear.

Typical Thunderstore profile location:

```text
%APPDATA%\Thunderstore Mod Manager\DataFolder\OnTogetherVirtualCoWorking\profiles\<profile>\BepInEx\plugins\StutterBuddies\StutterBuddies.dll
```

Manager versions, game folder naming, and custom data folders can differ; **Browse profile folder** is the reliable path. For a manual BepInEx installation, use `BepInEx/plugins/StutterBuddies` beside the game's `OnTogether.exe` instead. The Steam installation may contain a second `On-Together` subfolder: choose the folder actually containing that executable.

To uninstall, close the game and remove the `StutterBuddies` plugin folder. Its configuration and reports can be kept or removed separately. No game assembly is patched on disk.

## Controls and files

**Ctrl+F7** toggles a draggable panel with:

- Background saver and GPU saver toggles.
- **Save local performance report**, writing a text report and one-row CSV.
- **Reset measurement window** and **Reload config file**.
- **Prepare unused-asset cleanup**, followed by **Confirm cleanup now (may hitch)**.

The default shortcut uses the left Control key. It can be changed in the configuration file.

```text
BepInEx/config/community.ontogether.stutterbuddies.cfg
BepInEx/StutterBuddies/reports/report-<UTC timestamp>.txt
BepInEx/StutterBuddies/reports/report-<UTC timestamp>.csv
```

Reports are saved locally only when requested. They include game/Unity version, GPU name, resolution, active settings, resource statistics, and loaded plugin identifiers/versions. They do not collect player IDs or chat content. The separate BepInEx log may contain output from the game and other mods.

## Configuration

The configuration file is created on first launch. Edit it with the game closed, or use **Reload config file** after editing.

| Section / key | Default | Meaning |
| --- | --- | --- |
| `Power / Enabled` | `true` | Enables Stutter Buddies's frame-cap controls. |
| `Power / BackgroundFps` | `30` | Unfocused cap, 15–120. Very low values may reduce multiplayer responsiveness. |
| `Power / ForegroundFps` | `0` | Zero preserves the game. A positive cap, up to 360, applies only when foreground vSync is already off. |
| `Graphics / Enabled` | `false` | Enables the three graphics limits below. |
| `Graphics / MaximumRenderScale` | `0.85` | 0.5–1.0; limits 3D rendering resolution. |
| `Graphics / MaximumShadowDistance` | `35` | 0–150 world units. |
| `Graphics / MaximumMsaa` | `2` | Limits MSAA to 1, 2, 4, or 8 samples. Other allowed integers round down. 1 disables MSAA. |
| `Diagnostics / Enabled` | `true` | Enables frame statistics and once-per-second process sampling. |
| `Controls / PanelKey` | `F7 + LeftControl` | Panel shortcut. |

## Compatibility and limits

- When **FrameCare** is detected, Stutter Buddies backs off all frame-cap controls, including the background cap. Its graphics controls and diagnostics remain available. Do not expect the background saver toggle to override FrameCare.
- Setting overrides restore their captured values only while the current value still matches the value Stutter Buddies applied. If the game or another mod changes a value, Stutter Buddies yields instead of repeatedly overwriting it. Toggle the affected feature off/on to reapply deliberately.
- The UI identifies this release as experimental. Test avatar customization, desktop/overlay views, quality changes, joining/leaving lobbies, voice, and focus timers before depending on it for long sessions. Lower background FPS can affect work that the game runs once per frame.
- Graphics limits trade image quality for potential GPU savings. They are not a fix for CPU-bound game logic. No avatar, physics, networking, save data, or progression methods are replaced.
- Frame time includes waiting for vSync or a cap; it is not GPU execution time. CPU is normalized across all logical processors. Working, private, and managed memory overlap; do not add them together. A lower RAM reading alone does not demonstrate a repaired leak.
- Statistics use non-overlapping windows of roughly ten seconds. The displayed snapshot updates each second and can describe a partially filled window. Focus/config changes and the reset button clear the measurement. Saving a report writes the latest available snapshot.

## Compare performance

1. Keep the same game build, mods, resolution, quality, camera position/direction, lobby, and approximate player count. Let assets finish loading. Run a baseline without Stutter Buddies first if you need to measure the plugin's own overhead.
2. For a GPU-saver comparison, keep diagnostics on, the game focused, and the same cap/vSync settings. Start with GPU saver off (**A**). Reset the measurement window, close the panel, wait about eight seconds, reopen it and immediately save a report. Note the location and setting alongside the filename.
3. Enable GPU saver (**B**) and let the view settle. Repeat the same reset, panel-close, wait, and report steps. Alternate **A/B/A/B** several times instead of trusting one result. Keep the observation duration and panel interaction the same.
4. Compare mean/P95/P99 frame time and frames over 50 ms across similar sample counts. Lower frame times are better. If FPS stays at a cap, look for reduced CPU use, not a higher FPS claim; this mod does not measure GPU utilization directly.
5. Test the background saver separately with FrameCare absent. Compare saver off/on with the game unfocused for the same interval, using Task Manager or another external monitor. Returning focus resets Stutter Buddies's frame window, so a foreground report does not represent the preceding background interval.
6. For memory growth, compare equally long sessions with similar lobby and avatar activity. Record elapsed time and both working/private memory. Test manual cleanup separately and record its visible pause; do not run it during a frame-rate comparison.

If another player joins, a loading event occurs, or the camera/settings change, repeat that comparison. Report measured results with the settings and hardware; this release makes no fixed FPS or RAM-savings promise.

## Build and helper tests

Requires a .NET SDK plus your own local game and BepInEx reference assemblies. The plugin targets `netstandard2.1`; the standalone tests target `net8.0` and need no test packages.

```powershell
dotnet build StutterBuddies.csproj -c Release "-p:GameDir=C:\path\to\folder-containing-OnTogether.exe" "-p:BepInExDir=C:\path\to\profile\BepInEx"
dotnet run --project tests/StutterBuddies.Tests.csproj -c Release
```

The normal build output is `bin/Release/netstandard2.1/StutterBuddies.dll`. Reference assemblies have copying disabled and must not be redistributed with the plugin. Helper tests cover ring-buffer statistics, nearest-rank percentiles, invalid input, bounded allocations, and setting-ownership/restoration behavior. They do not launch Unity or test multiplayer compatibility.

## Release packaging

Download the installable `StutterBuddies-0.1.0.zip` from [GitHub Releases](https://github.com/QualityControled/StutterBuddies/releases). GitHub's automatically generated source archives are for development, not installation.

The release ZIP has `manifest.json`, `README.md`, a 256x256 `icon.png`, `CHANGELOG.md`, and `LICENSE` at its root, plus the plugin under `BepInEx/plugins/StutterBuddies/`. It declares the BepInExPack dependency and links this repository. It is prepared for the On-Together community on Thunderstore; a GitHub release does not itself create a Thunderstore listing.

To reproduce the release ZIP, use `scripts/Build.ps1 -GameDir '...folder containing OnTogether.exe...' -BepInExDir '...profile/BepInEx...'`. This builds the plugin, runs helper tests, and packages an explicit file allowlist. It never bundles local game assemblies. The GitHub workflow runs the pure helper tests on Windows and Linux; building the full plugin requires local game reference assemblies.

## API references

- [BepInEx plugin development and local assembly references](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html)
- [Unity targetFrameRate and its interaction with vSync](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-targetFrameRate.html)
- [Unity unused-asset cleanup semantics](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Resources.UnloadUnusedAssets.html)
- [Unity garbage collection modes](https://docs.unity3d.com/6000.0/Documentation/Manual/performance-incremental-garbage-collection.html)
