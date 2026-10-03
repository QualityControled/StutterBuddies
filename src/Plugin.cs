using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StutterBuddies
{
    [BepInPlugin(Id, "Stutter Buddies", Version)]
    [BepInProcess("OnTogether.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "community.ontogether.stutterbuddies";
        public const string Version = "0.1.0";
        private const string FrameCareId = "com.sleepeydev.ontogether.framecare";
        private readonly FrameWindow frames = new FrameWindow(12000);
        private ConfigEntry<bool> saver, graphics, reportsEnabled;
        private ConfigEntry<int> backgroundFps, foregroundFps, msaa;
        private ConfigEntry<float> renderScale, shadowDistance;
        private ConfigEntry<KeyboardShortcut> panelKey;
        private SettingLease<int> fpsLease, syncLease;
        private SettingLease<float> scaleLease, shadowLease;
        private SettingLease<int> msaaLease;
        private UniversalRenderPipelineAsset pipeline;
        private Process process;
        private FrameSummary summary;
        private double nextSample, windowStart, lastCpuSeconds, lastCpuAt;
        private double workingMb, privateMb, managedMb, cpuPercent;
        private bool ready, panel, frameCare, focused, dirty, graphicsSuspended, capsSuspended;
        private AsyncOperation cleanupOperation;
        private double cleanupArmedUntil, cleanupAllowedAt;
        private Rect panelRect = new Rect(24, 80, 500, 580);
        private string status = "Ready. Ctrl+F7 opens this panel.";
        private string statsText = "Collecting a 10-second sample...";
        private GUIStyle panelStyle;
        private GUISkin panelSkin;
        private Texture2D panelBackground;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private void Awake()
        {
            saver = Config.Bind("Power", "Enabled", true, "Limit background rendering when another frame limiter is not detected.");
            backgroundFps = Config.Bind("Power", "BackgroundFps", 30, new ConfigDescription("Unfocused FPS limit. Very low limits can reduce network responsiveness.", new AcceptableValueRange<int>(15, 120)));
            foregroundFps = Config.Bind("Power", "ForegroundFps", 0, new ConfigDescription("0 preserves the game. Nonzero cap applies only with vSync already off; never raises a lower game cap.", new AcceptableValueRange<int>(0, 360)));
            graphics = Config.Bind("Graphics", "Enabled", false, "Opt-in GPU saver: limits URP render scale, shadow distance and MSAA. Can soften the image and reduce shadows.");
            renderScale = Config.Bind("Graphics", "MaximumRenderScale", 0.85f, new ConfigDescription("Upper limit; lower values reduce 3D rendering resolution. Does not raise an existing lower scale.", new AcceptableValueRange<float>(0.5f, 1f)));
            shadowDistance = Config.Bind("Graphics", "MaximumShadowDistance", 35f, new ConfigDescription("Upper limit in world units. Closer shadows reduce rendering work.", new AcceptableValueRange<float>(0f, 150f)));
            msaa = Config.Bind("Graphics", "MaximumMsaa", 2, new ConfigDescription("1 disables MSAA; 2/4/8 cap samples. Other numbers round down to a valid level.", new AcceptableValueRange<int>(1, 8)));
            reportsEnabled = Config.Bind("Diagnostics", "Enabled", true, "Collect bounded frame timing and sample process memory/CPU once per second. Reports are written only by request.");
            panelKey = Config.Bind("Controls", "PanelKey", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftControl), "Toggle panel. Use modifier keys to avoid accidental activation in chat.");
            fpsLease = new SettingLease<int>(() => Application.targetFrameRate, x => Application.targetFrameRate = x);
            syncLease = new SettingLease<int>(() => QualitySettings.vSyncCount, x => QualitySettings.vSyncCount = x);
            Config.SettingChanged += OnSettingChanged;
            process = Process.GetCurrentProcess();
        }

        private void Start()
        {
            frameCare = Chainloader.PluginInfos.ContainsKey(FrameCareId);
            focused = Application.isFocused;
            ready = true;
            ResetSample();
            ApplyCaps();
            ApplyGraphics();
            Logger.LogInfo("Stutter Buddies " + Version + " ready. Unity " + Application.unityVersion + "; Ctrl+F7 for panel.");
            if (frameCare) Logger.LogInfo("FrameCare detected: Stutter Buddies frame caps are inactive to avoid competing limiters. Graphics and diagnostics remain available.");
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs args) { dirty = true; }

        private void Update()
        {
            if (!ready) return;
            if (panelKey.Value.IsDown()) panel = !panel;
            if (focused != Application.isFocused)
            {
                focused = Application.isFocused;
                ResetSample();
                ApplyCaps();
            }
            if (dirty)
            {
                dirty = false;
                ResetSample();
                ApplyCaps();
                ApplyGraphics();
            }
            if (reportsEnabled.Value) frames.Add(Time.unscaledDeltaTime * 1000.0);
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextSample) return;
            nextSample = now + 1;
            if (!fpsLease.StillOwned || !syncLease.StillOwned)
            {
                ReleaseCaps();
                capsSuspended = true;
                ResetSample();
                status = "Frame cap yielded to a game/mod setting change.";
                Logger.LogInfo(status);
            }
            var current = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (current != pipeline) { ApplyGraphics(); ResetSample(); }
            else if (pipeline && ((!scaleLease.StillOwned) || (!shadowLease.StillOwned) || (!msaaLease.StillOwned)))
            {
                ReleaseGraphics();
                graphicsSuspended = true;
                ResetSample();
                status = "Graphics controls yielded to a game/mod setting change.";
                Logger.LogInfo(status);
            }
            if (reportsEnabled.Value) Sample(now);
        }

        private void ApplyCaps()
        {
            ReleaseCaps();
            capsSuspended = false;
            if (!saver.Value || frameCare) return;
            int cap = focused ? foregroundFps.Value : backgroundFps.Value;
            if (cap <= 0 || (focused && QualitySettings.vSyncCount != 0)) return;
            int current = Application.targetFrameRate;
            if (current > 0) cap = Math.Min(cap, current);
            if (!focused && QualitySettings.vSyncCount > 0)
            {
                double refresh = Screen.currentResolution.refreshRateRatio.value;
                if (refresh > 0 && !double.IsNaN(refresh) && !double.IsInfinity(refresh))
                    cap = Math.Min(cap, Math.Max(1, (int)Math.Floor(refresh / QualitySettings.vSyncCount)));
            }
            if (!focused) syncLease.Apply(0);
            fpsLease.Apply(cap);
        }

        private void ReleaseCaps()
        {
            fpsLease?.Release();
            syncLease?.Release();
        }

        private void ApplyGraphics()
        {
            ReleaseGraphics();
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            graphicsSuspended = false;
            if (!pipeline) return;
            var target = pipeline;
            scaleLease = new SettingLease<float>(() => target.renderScale, x => target.renderScale = x);
            shadowLease = new SettingLease<float>(() => target.shadowDistance, x => target.shadowDistance = x);
            msaaLease = new SettingLease<int>(() => target.msaaSampleCount, x => target.msaaSampleCount = x);
            if (!graphics.Value) return;
            int samples = msaa.Value >= 8 ? 8 : msaa.Value >= 4 ? 4 : msaa.Value >= 2 ? 2 : 1;
            scaleLease.Apply(Math.Min(target.renderScale, renderScale.Value));
            shadowLease.Apply(Math.Min(target.shadowDistance, shadowDistance.Value));
            msaaLease.Apply(Math.Min(target.msaaSampleCount, samples));
        }

        private void ReleaseGraphics()
        {
            if (pipeline)
            {
                scaleLease?.Release();
                shadowLease?.Release();
                msaaLease?.Release();
            }
        }

        private void ResetSample()
        {
            frames.Clear();
            summary = default;
            windowStart = Time.realtimeSinceStartupAsDouble;
            lastCpuAt = windowStart;
            try { lastCpuSeconds = process.TotalProcessorTime.TotalSeconds; } catch { }
            statsText = "Collecting a 10-second sample...";
        }

        private void Sample(double now)
        {
            summary = frames.Summarize();
            try
            {
                process.Refresh();
                if (!WindowsMemory.TryRead(out workingMb, out privateMb))
                {
                    workingMb = process.WorkingSet64 > 0 ? process.WorkingSet64 / 1048576.0 : double.NaN;
                    privateMb = process.PrivateMemorySize64 > 0 ? process.PrivateMemorySize64 / 1048576.0 : double.NaN;
                }
                double total = process.TotalProcessorTime.TotalSeconds;
                double elapsed = now - lastCpuAt;
                cpuPercent = elapsed > 0 ? Math.Max(0, Math.Min(100, 100 * (total - lastCpuSeconds) / (elapsed * Environment.ProcessorCount))) : 0;
                lastCpuSeconds = total;
                lastCpuAt = now;
                managedMb = GC.GetTotalMemory(false) / 1048576.0;
            }
            catch (Exception e) { status = "Process counters unavailable: " + e.GetType().Name; }
            statsText = string.Format(Invariant, "{0:F1} FPS | mean {1:F2} ms | p95 {2:F2} ms | p99 {3:F2} ms\nFrames >50 ms: {4} | samples: {5}\nRAM working/private: {6:F0}/{7:F0} MiB | managed: {8:F0} MiB\nCPU: {9:F1}% of all logical processors", summary.AverageFps, summary.AverageMs, summary.P95Ms, summary.P99Ms, summary.Over50MsCount, summary.Count, workingMb, privateMb, managedMb, cpuPercent);
            // Non-overlapping windows: bounded storage also handles unusually high frame rates.
            if (now - windowStart >= 10)
            {
                frames.Clear();
                windowStart = now;
            }
        }

        private void OnGUI()
        {
            if (!ready || !panel) return;
            if (panelSkin == null)
            {
                panelSkin = Instantiate(GUI.skin);
                panelBackground = new Texture2D(1, 1);
                panelBackground.SetPixel(0, 0, new Color(0.07f, 0.12f, 0.18f, 0.97f));
                panelBackground.Apply();
                panelSkin.window.normal.background = panelBackground;
                panelSkin.window.onNormal.background = panelBackground;
                panelSkin.window.fontSize = 16;
                panelSkin.window.normal.textColor = Color.white;
                panelSkin.window.onNormal.textColor = Color.white;
                panelSkin.window.padding = new RectOffset(12, 12, 28, 12);
                panelSkin.label.fontSize = 15;
                panelSkin.label.normal.textColor = Color.white;
                panelSkin.label.wordWrap = true;
                panelSkin.button.fontSize = 15;
                panelSkin.button.fixedHeight = 28;
                panelSkin.toggle.fontSize = 15;
                panelSkin.toggle.fixedHeight = 28;
                panelSkin.toggle.normal.textColor = Color.white;
                panelSkin.toggle.onNormal.textColor = Color.white;
                panelStyle = panelSkin.label;
            }
            panelRect.x = Mathf.Clamp(panelRect.x, 0, Mathf.Max(0, Screen.width - panelRect.width));
            panelRect.y = Mathf.Clamp(panelRect.y, 0, Mathf.Max(0, Screen.height - 80));
            var previousSkin = GUI.skin;
            GUI.skin = panelSkin;
            try { panelRect = GUILayout.Window(0x74554E, panelRect, DrawPanel, "Stutter Buddies 0.1.0 - experimental"); }
            finally { GUI.skin = previousSkin; }
        }

        private void DrawPanel(int id)
        {
            GUILayout.Label(reportsEnabled.Value ? statsText : "Diagnostics disabled in config.", panelStyle, GUILayout.Height(90));
            GUILayout.Space(8);
            GUILayout.Label(focused ? "Game focused" : "Game in background");
            GUILayout.Label("Current target: " + Application.targetFrameRate + " FPS | vSync: " + QualitySettings.vSyncCount);
            if (frameCare) GUILayout.Label("FrameCare detected: its limiter owns frame pacing.", panelStyle);
            else if (capsSuspended) GUILayout.Label("Frame cap paused after external settings changed.", panelStyle);
            bool power = GUILayout.Toggle(saver.Value, " Background saver (" + backgroundFps.Value + " FPS)");
            if (power != saver.Value) saver.Value = power;
            bool gpu = GUILayout.Toggle(graphics.Value, " GPU saver (lower resolution / shadows / MSAA)");
            if (gpu != graphics.Value) graphics.Value = gpu;
            if (graphicsSuspended) GUILayout.Label("Graphics paused after external settings changed; toggle off/on to reapply.", panelStyle);
            if (pipeline) GUILayout.Label(string.Format(Invariant, "URP scale {0:F2} | shadow range {1:F0} | MSAA {2}x", pipeline.renderScale, pipeline.shadowDistance, pipeline.msaaSampleCount));
            GUILayout.Space(8);
            if (GUILayout.Button("Save local performance report")) WriteReport();
            if (GUILayout.Button("Reset measurement window")) ResetSample();
            if (GUILayout.Button("Reload config file"))
            {
                try { Config.Reload(); dirty = true; status = "Config reloaded."; }
                catch (Exception e) { status = "Reload failed: " + e.Message; }
            }
            GUILayout.Space(8);
            GUILayout.Label("Unused-asset cleanup may briefly freeze the game. It cannot fix all memory growth. No automatic sweeps or forced GC.", panelStyle);
            bool cleaning = cleanupOperation != null && !cleanupOperation.isDone;
            bool armed = Time.realtimeSinceStartupAsDouble < cleanupArmedUntil;
            GUI.enabled = !cleaning && Time.realtimeSinceStartupAsDouble >= cleanupAllowedAt;
            if (GUILayout.Button(cleaning ? "Cleanup running..." : armed ? "Confirm cleanup now (may hitch)" : "Prepare unused-asset cleanup"))
            {
                if (armed) StartCoroutine(Cleanup());
                else cleanupArmedUntil = Time.realtimeSinceStartupAsDouble + 5;
            }
            GUI.enabled = true;
            GUILayout.Label(status, panelStyle);
            if (GUILayout.Button("Close (Ctrl+F7)")) panel = false;
            GUI.DragWindow(new Rect(0, 0, 500, 24));
        }

        private IEnumerator Cleanup()
        {
            cleanupArmedUntil = 0;
            cleanupAllowedAt = double.PositiveInfinity;
            status = "Releasing assets with no live Unity references...";
            Logger.LogInfo("User requested unused-asset cleanup; a temporary hitch is expected.");
            yield return null;
            cleanupOperation = Resources.UnloadUnusedAssets();
            yield return cleanupOperation;
            cleanupOperation = null;
            cleanupAllowedAt = Time.realtimeSinceStartupAsDouble + 60;
            ResetSample();
            status = "Cleanup complete. Memory may not fall if assets are still in use. Cooldown: 60 seconds.";
            Logger.LogInfo(status);
        }

        private void WriteReport()
        {
            try
            {
                var currentSummary = reportsEnabled.Value ? summary : default;
                string directory = Path.Combine(Paths.BepInExRootPath, "StutterBuddies", "reports");
                Directory.CreateDirectory(directory);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", Invariant);
                string path = Path.Combine(directory, "report-" + stamp + ".txt");
                var report = new StringBuilder();
                report.AppendLine("Stutter Buddies " + Version + " - local diagnostic snapshot");
                report.AppendLine("UTC: " + DateTime.UtcNow.ToString("O", Invariant));
                report.AppendLine("Game: " + Application.version + " | Unity: " + Application.unityVersion);
                report.AppendLine("Focused: " + focused + " | diagnostics: " + reportsEnabled.Value);
                report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " | screen: " + Screen.width + "x" + Screen.height);
                report.AppendLine("Power enabled: " + saver.Value + " | FrameCare compatibility: " + frameCare + " | cap yielded: " + capsSuspended);
                report.AppendLine("Graphics enabled: " + graphics.Value + " | graphics yielded: " + graphicsSuspended);
                report.AppendLine("Current target FPS: " + Application.targetFrameRate + " | vSync: " + QualitySettings.vSyncCount);
                if (pipeline) report.AppendLine(string.Format(Invariant, "URP renderScale={0}; shadowDistance={1}; MSAA={2}", pipeline.renderScale, pipeline.shadowDistance, pipeline.msaaSampleCount));
                report.AppendLine(statsText);
                report.AppendLine("Window: latest 1 Hz snapshot of up to 10 seconds / 12,000 frames; percentiles use nearest rank.");
                report.AppendLine("Frame time includes waiting/pacing and is not a GPU timing. Focus/settings changes reset the window.");
                report.AppendLine("CPU is normalized across logical processors. RAM metrics overlap; do not add them together.");
                report.AppendLine("Loaded plugins (no player IDs or chat content):");
                foreach (var info in Chainloader.PluginInfos.Values) report.AppendLine(info.Metadata.GUID + " " + info.Metadata.Version);
                File.WriteAllText(path, report.ToString(), Encoding.UTF8);
                string csv = "utc,focused,graphics_enabled,frames,average_fps,average_ms,p95_ms,p99_ms,frames_over_50ms,working_mib,private_mib,managed_mib,cpu_percent\n" + string.Format(Invariant, "{0},{1},{2},{3},{4:F3},{5:F3},{6:F3},{7:F3},{8},{9:F3},{10:F3},{11:F3},{12:F3}\n", stamp, focused, graphics.Value, currentSummary.Count, currentSummary.AverageFps, currentSummary.AverageMs, currentSummary.P95Ms, currentSummary.P99Ms, currentSummary.Over50MsCount, workingMb, privateMb, managedMb, cpuPercent);
                File.WriteAllText(Path.ChangeExtension(path, ".csv"), csv, Encoding.UTF8);
                status = "Report saved under BepInEx/StutterBuddies/reports.";
                Logger.LogInfo("Report saved: " + path);
            }
            catch (Exception e) { status = "Report failed: " + e.Message; Logger.LogWarning(status); }
        }

        private void OnDisable()
        {
            ReleaseCaps();
            ReleaseGraphics();
        }

        private void OnEnable()
        {
            if (!ready) return;
            focused = Application.isFocused;
            ResetSample();
            ApplyCaps();
            ApplyGraphics();
        }

        private void OnDestroy()
        {
            ready = false;
            Config.SettingChanged -= OnSettingChanged;
            ReleaseCaps();
            ReleaseGraphics();
            process?.Dispose();
            if (panelSkin) Destroy(panelSkin);
            if (panelBackground) Destroy(panelBackground);
        }
    }
}
