using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace AutoEngineerBrakeFix
{
    public static class Main
    {
        internal static UnityModManager.ModEntry Mod;
        internal static Settings Settings;

        /// <summary>True while the mod is enabled in UMM. The fix and the samplers check this every tick.</summary>
        internal static bool Active;

        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = OnUnload;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            if (value)
            {
                Active = true;
                if (_harmony == null)
                {
                    _harmony = new Harmony(entry.Info.Id);
                    _harmony.PatchAll(Assembly.GetExecutingAssembly());
                    entry.Logger.Log("Patches applied.");
                    foreach (var engineer in UnityEngine.Object.FindObjectsOfType<Model.AI.AutoEngineer>())
                    {
                        AutoEngineer_OnEnable_Patch.Attach(engineer);
                    }
                }
            }
            else
            {
                Shutdown();
            }
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry entry)
        {
            Shutdown();
            return true;
        }

        private static void Shutdown()
        {
            Active = false;
            if (_harmony != null)
            {
                _harmony.UnpatchAll(_harmony.Id);
                _harmony = null;
            }

            foreach (var sampler in UnityEngine.Object.FindObjectsOfType<AutoEngineerSampler>())
            {
                UnityEngine.Object.Destroy(sampler);
            }
            foreach (var glider in UnityEngine.Object.FindObjectsOfType<ThrottleGlider>())
            {
                UnityEngine.Object.Destroy(glider);
            }

            LogWriter.Close();
        }

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            GUILayout.Label("Brake fix");
            GUILayout.Label("Keeps the auto-engineer from dragging the brakes while the throttle is open. Real overspeed braking is untouched.");

            Settings.FixEnabled = GUILayout.Toggle(Settings.FixEnabled,
                "Release the brake while the throttle is open and speed is within the margin below");

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Overspeed margin: {Settings.OverspeedMarginMph:0.00} mph", GUILayout.Width(220));
            float margin = GUILayout.HorizontalSlider(Settings.OverspeedMarginMph, 0.25f, 2f, GUILayout.Width(220));
            Settings.OverspeedMarginMph = Mathf.Round(margin * 20f) / 20f;
            GUILayout.EndHorizontal();

            Settings.FixLocomotiveBrake = GUILayout.Toggle(Settings.FixLocomotiveBrake,
                "Also apply to the locomotive (independent) brake used when running light");

            GUILayout.Space(12);
            GUILayout.Label("Shorter cutoff");
            Settings.CutoffEnabled = GUILayout.Toggle(Settings.CutoffEnabled,
                "Use a shorter cutoff when it keeps nearly the same power (steam locomotives, above 5 mph)");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Power given up: {Settings.CutoffTolerancePercent:0}%", GUILayout.Width(220));
            float tol = GUILayout.HorizontalSlider(Settings.CutoffTolerancePercent, 0f, 10f, GUILayout.Width(220));
            Settings.CutoffTolerancePercent = Mathf.Round(tol);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Use stock cutoff on climbs of: {Settings.CutoffRevertGradePercent:0.0}% or more", GUILayout.Width(300));
            float g = GUILayout.HorizontalSlider(Settings.CutoffRevertGradePercent, 0.5f, 5f, GUILayout.Width(200));
            Settings.CutoffRevertGradePercent = Mathf.Round(g * 10f) / 10f;
            GUILayout.EndHorizontal();

            GUILayout.Space(12);
            GUILayout.Label("Throttle smoothing");
            Settings.SmoothThrottle = GUILayout.Toggle(Settings.SmoothThrottle,
                "Move the throttle more gradually (removes the back-and-forth hunting)");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Smoothing: {Settings.ThrottleSmoothingSeconds:0.0} s", GUILayout.Width(220));
            float sm = GUILayout.HorizontalSlider(Settings.ThrottleSmoothingSeconds, 0.5f, 5f, GUILayout.Width(220));
            Settings.ThrottleSmoothingSeconds = Mathf.Round(sm * 10f) / 10f;
            GUILayout.EndHorizontal();
            Settings.GlideThrottle = GUILayout.Toggle(Settings.GlideThrottle,
                "Glide the lever between the AI's half-second updates (smooth motion in the cab)");
            Settings.SlamThrottleOnOverspeed = GUILayout.Toggle(Settings.SlamThrottleOnOverspeed,
                "Close the throttle at once when overspeed needs the brake");
            Settings.HoldThrottleWhileSlowing = GUILayout.Toggle(Settings.HoldThrottleWhileSlowing,
                "Keep the throttle closed while slowing for a stop or limit (no throttle bursts between brake pulses)");
            Settings.HoldBrakeWhileSlowing = GUILayout.Toggle(Settings.HoldBrakeWhileSlowing,
                "Keep the brake applied while slowing until the train is clearly under the target (no brake flicker)");
            Settings.HoldStopAtStopTargets = GUILayout.Toggle(Settings.HoldStopAtStopTargets,
                "Stay stopped at a stop signal or stop target (no creeping past it)");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Brake only when over the limit by: {Settings.BrakeStartOverMph:0.0} mph", GUILayout.Width(300));
            float bs = GUILayout.HorizontalSlider(Settings.BrakeStartOverMph, 0f, 2f, GUILayout.Width(200));
            Settings.BrakeStartOverMph = Mathf.Round(bs * 10f) / 10f;
            GUILayout.EndHorizontal();
            Settings.AvoidPowerIntoReductions = GUILayout.Toggle(Settings.AvoidPowerIntoReductions,
                "No power while a lower speed limit is in sight and the train is at or above that speed");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Start slowing for a lower limit early by: {Settings.SpeedReductionLeadSeconds:0.0} s", GUILayout.Width(300));
            float lead = GUILayout.HorizontalSlider(Settings.SpeedReductionLeadSeconds, 0f, 12f, GUILayout.Width(200));
            Settings.SpeedReductionLeadSeconds = Mathf.Round(lead * 2f) / 2f;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Release the brake early by: {Settings.BrakeReleaseLeadSeconds:0.0} s", GUILayout.Width(300));
            float rel = GUILayout.HorizontalSlider(Settings.BrakeReleaseLeadSeconds, 0f, 6f, GUILayout.Width(200));
            Settings.BrakeReleaseLeadSeconds = Mathf.Round(rel * 2f) / 2f;
            GUILayout.EndHorizontal();
            Settings.StallGuard = GUILayout.Toggle(Settings.StallGuard,
                "Stall guard: when the train stalls on a grade, hold it with the brakes and announce it");

            GUILayout.Space(12);
            GUILayout.Label("Train resistance (experimental)");
            Settings.StartingResistance = GUILayout.Toggle(Settings.StartingResistance,
                "Starting resistance (adds the standing-start drag of plain-bearing cars; heavy trains start harder)");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Extra resistance at 0 mph: {Settings.StartingResistanceLbPerTon:0.0} lb/ton", GUILayout.Width(300));
            float sres = GUILayout.HorizontalSlider(Settings.StartingResistanceLbPerTon, 0f, 10f, GUILayout.Width(200));
            Settings.StartingResistanceLbPerTon = Mathf.Round(sres * 2f) / 2f;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Fades out over about: {Settings.StartingResistanceFadeMph:0.0} mph", GUILayout.Width(300));
            float sfade = GUILayout.HorizontalSlider(Settings.StartingResistanceFadeMph, 0.5f, 4f, GUILayout.Width(200));
            Settings.StartingResistanceFadeMph = Mathf.Round(sfade * 2f) / 2f;
            GUILayout.EndHorizontal();
            Settings.StartingResistanceStandingTime = GUILayout.Toggle(Settings.StartingResistanceStandingTime,
                "Standing time: a car that has just stopped starts easier than one that has sat (game time; cars count as settled after a load)");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Drag just after stopping: {Settings.StartingResistanceFloorPercent:0}% of full", GUILayout.Width(300));
            float sfloor = GUILayout.HorizontalSlider(Settings.StartingResistanceFloorPercent, 0f, 100f, GUILayout.Width(200));
            Settings.StartingResistanceFloorPercent = Mathf.Round(sfloor / 5f) * 5f;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Settles back over about: {Settings.StartingResistanceSettleMinutes:0} game min", GUILayout.Width(300));
            float ssettle = GUILayout.HorizontalSlider(Settings.StartingResistanceSettleMinutes, 1f, 60f, GUILayout.Width(200));
            Settings.StartingResistanceSettleMinutes = Mathf.Round(ssettle);
            GUILayout.EndHorizontal();

            GUILayout.Space(12);
            GUILayout.Label("Fuel use (experimental)");
            Settings.RealisticFuel = GUILayout.Toggle(Settings.RealisticFuel,
                "Realistic coal and water use (steam locomotives; replaces the game's figures with a power-based model)");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Steam per indicated hp-hour: {Settings.SteamLbPerIhpHour:0.0} lb", GUILayout.Width(300));
            float sr = GUILayout.HorizontalSlider(Settings.SteamLbPerIhpHour, 12f, 26f, GUILayout.Width(200));
            Settings.SteamLbPerIhpHour = Mathf.Round(sr * 2f) / 2f;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Cutoff effect on steam: {Settings.CutoffSteamSlope:0.00}", GUILayout.Width(300));
            float cutSteam = GUILayout.HorizontalSlider(Settings.CutoffSteamSlope, 0f, 1f, GUILayout.Width(200));
            Settings.CutoffSteamSlope = Mathf.Round(cutSteam * 20f) / 20f;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Coal burned: x{Settings.CoalScale:0.00}", GUILayout.Width(300));
            float cs = GUILayout.HorizontalSlider(Settings.CoalScale, 0.5f, 1.5f, GUILayout.Width(200));
            Settings.CoalScale = Mathf.Round(cs * 20f) / 20f;
            GUILayout.EndHorizontal();

            GUILayout.Space(12);
            GUILayout.Label("Signals");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"React to signal aspects from: {Settings.SignalSightMeters:0} m (game: 200)", GUILayout.Width(300));
            float ss = GUILayout.HorizontalSlider(Settings.SignalSightMeters, 200f, 900f, GUILayout.Width(200));
            Settings.SignalSightMeters = Mathf.Round(ss / 50f) * 50f;
            GUILayout.EndHorizontal();
            Settings.ApproachTargetAtSignal = GUILayout.Toggle(Settings.ApproachTargetAtSignal,
                "Apply the Approach speed limit at the signal, not instantly (avoids full braking)");
            Settings.HoldApproachSpeed = GUILayout.Toggle(Settings.HoldApproachSpeed,
                "Keep that limit after the signal until the next signal is registered (no speeding up in between)");

            GUILayout.Space(12);
            GUILayout.Label("Planning (experimental, 1.00 = stock)");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Look-ahead distance: x{Settings.LookaheadMultiplier:0.00}", GUILayout.Width(220));
            float la = GUILayout.HorizontalSlider(Settings.LookaheadMultiplier, 1f, 3f, GUILayout.Width(220));
            Settings.LookaheadMultiplier = Mathf.Round(la * 20f) / 20f;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Assumed braking when planning: {Settings.BrakePlanningFraction:0.00}", GUILayout.Width(220));
            float bp = GUILayout.HorizontalSlider(Settings.BrakePlanningFraction, 0.3f, 1f, GUILayout.Width(220));
            Settings.BrakePlanningFraction = Mathf.Round(bp * 20f) / 20f;
            GUILayout.EndHorizontal();
            GUILayout.Label("Lower braking = start slowing earlier and brake more gently for targets the AI already knows about.");

            GUILayout.Space(12);
            GUILayout.Label("Diagnostic logger (off by default; only needed for troubleshooting or testing)");

            Settings.LogEnabled = GUILayout.Toggle(Settings.LogEnabled,
                "Write CSV log while an auto-engineer is running");

            Settings.LogFuel = GUILayout.Toggle(Settings.LogFuel,
                "Log fuel and water aboard the tender (best effort)");

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Sample interval: {Settings.SampleIntervalSeconds:0.00} s", GUILayout.Width(220));
            float raw = GUILayout.HorizontalSlider(Settings.SampleIntervalSeconds, 0.1f, 1f, GUILayout.Width(220));
            Settings.SampleIntervalSeconds = Mathf.Round(raw * 20f) / 20f;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Logs are written to:");
            GUILayout.Label(LogWriter.LogDir);
        }

        private static void OnSaveGUI(UnityModManager.ModEntry entry)
        {
            Settings.Save(entry);
        }
    }
}
