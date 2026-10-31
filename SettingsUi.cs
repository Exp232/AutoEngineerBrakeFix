using System;
using TMPro;
using UI.Builder;
using UI.Windows;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// The mod's in-game settings window, built with the game's own panel builder (the same one its windows use) and
    /// opened with a hotkey (Ctrl+F8 by default; see SettingsHotkey in Settings.xml). Sliders change the live
    /// settings at once and every change is written back to Settings.xml. Only settings that are read while the
    /// game runs are offered here; the ones that choose which patches are installed still need a restart.
    /// </summary>
    internal static class SettingsUi
    {
        internal const string WindowId = "exp232.autoengineerbrakefix.settings";

        internal static void Toggle()
        {
            var windows = WindowManager.Shared;
            if (windows == null) return;   // main menu: the window system is not up yet
            EnsureRegistered(windows);
            windows.ToggleWindow(WindowId);
        }

        // The game forgets custom windows when a map unloads, so register again whenever the definition is gone.
        private static void EnsureRegistered(WindowManager windows)
        {
            if (windows.HasDefinition(WindowId)) return;
            windows.RegisterCustomWindow(new CustomWindowDefinition
            {
                Identifier = WindowId,
                Title = "Auto Engineer " + Main.Mod.Info.Version,
                Config = WindowConfig.Resizable(new Vector2Int(480, 640), new Vector2Int(380, 260)),
                BuilderAction = Build,
            });
        }

        private static void Build(UIPanelBuilder builder)
        {
            try
            {
                var s = Main.Settings;
                builder.VScrollView(page =>
                {
                    page.AddTitle("Auto Engineer", "AutoEngineerBrakeFix " + Main.Mod.Info.Version);

                    page.AddSection("Fuel saving", b =>
                    {
                        Note(b, "Brake taps: when the train is no more than this far over its target and not speeding up, a first brake application becomes a coast.");
                        Slider(b, "Brake tap limit", () => s.BrakeTapSuppressMph, v => s.BrakeTapSuppressMph = v, 0f, 5f, 0.5f,
                            v => v <= 0f ? "off" : v.ToString("0.0") + " mph");
                        Note(b, "Coast band: closes the throttle when coasting holds the speed within this band below the target (or the grade is still picking the train up), and waits to brake until the train is this far over. 0 = off.");
                        Slider(b, "Coast band", () => s.CoastBandMph, v => s.CoastBandMph = v, 0f, 3f, 0.5f,
                            v => v <= 0f ? "off" : v.ToString("0.0") + " mph");
                        Note(b, "Early slowing: how many seconds before a lower limit the engineer plans to be at that speed.");
                        Slider(b, "Slowing lead", () => s.SpeedReductionLeadSeconds, v => s.SpeedReductionLeadSeconds = v, 0f, 15f, 1f,
                            v => v <= 0f ? "off" : v.ToString("0") + " s");
                        Note(b, "Shorter cutoff: how much power it may give up for a shorter cutoff.");
                        Slider(b, "Cutoff tolerance", () => s.CutoffTolerancePercent, v => s.CutoffTolerancePercent = v, 0f, 10f, 0.5f,
                            v => v.ToString("0.0") + " %");
                    });

                    page.AddSection("Fuel use", b =>
                    {
                        b.AddFieldToggle("Realistic fuel", () => s.RealisticFuel, v => { s.RealisticFuel = v; Save(); });
                        Note(b, "Steam and coal follow the engine's own power instead of the game's speed-based curve, so fast running no longer costs too much. Off = the game's own use. Takes effect at once; steam rate, efficiency and coal scale are in Settings.xml.");
                    });

                    page.AddSection("Water stop (Road mode)", b =>
                    {
                        b.AddFieldToggle("Water stop", () => s.WaterStop, v => { s.WaterStop = v; Save(); });
                        Note(b, "When the tender runs low, a Road-mode engineer takes water at the next column ahead on the lined route. It switches to Waypoint mode only in the column's signal block, then goes back to Road mode.");
                        Slider(b, "Stop below", () => s.WaterStopPercent, v => s.WaterStopPercent = v, 10f, 60f, 5f,
                            v => v.ToString("0") + " % water");
                        Slider(b, "Approach speed", () => s.WaterStopApproachMph, v => s.WaterStopApproachMph = v, 10f, 35f, 5f,
                            v => v.ToString("0") + " mph");
                    });

                    page.AddSection("Engineer status", b =>
                    {
                        b.AddFieldToggle("Detailed status", () => s.DetailedStatus, v => { s.DetailedStatus = v; Save(); });
                        Note(b, "The status line under the auto engineer controls names what sets the speed (posted or railroad limit, track class, curve, curve held down by the equipment, engine top speed, this mod's water stop and approach hold) and how far ahead it is. Hover it for the details and the targets ahead.");
                        b.AddFieldToggle("Metric distances", () => s.StatusMetric, v => { s.StatusMetric = v; Save(); });
                        Note(b, "Off: feet and miles. On: metres and kilometres.");
                    });

                    page.AddSection("Planner weights (1 = the game's own)", b =>
                    {
                        Slider(b, "Overspeed", () => s.PlanOverspeedScale, v => s.PlanOverspeedScale = v, 0f, 3f, 0.05f, Weight);
                        Slider(b, "Undershoot", () => s.PlanUndershootScale, v => s.PlanUndershootScale = v, 0f, 3f, 0.05f, Weight);
                        Slider(b, "Undershoot margin", () => s.PlanUndershootDeadbandScale, v => s.PlanUndershootDeadbandScale = v, 0f, 3f, 0.05f, Weight);
                        Slider(b, "Ceiling", () => s.PlanCeilingScale, v => s.PlanCeilingScale = v, 0f, 3f, 0.05f, Weight);
                        Slider(b, "Locomotive brake use", () => s.PlanIndependentUseScale, v => s.PlanIndependentUseScale = v, 0f, 3f, 0.05f, Weight);
                    });

                    page.AddSection("Logging", b =>
                    {
                        b.AddFieldToggle("Fuel summary", () => s.LogFuelSummary, v => { s.LogFuelSummary = v; Save(); });
                    });

                    page.AddSection("Fixes", b =>
                    {
                        b.AddFieldToggle("Fuel in fast-forward", () => s.FuelFollowsTimeWarp, v => { s.FuelFollowsTimeWarp = v; Save(); });
                        Note(b, "The game charges one second of fuel per update however much time passed, so fast-forward used about a third of the coal. This charges the real game time.");
                        b.AddFieldToggle("Fusee in fast-forward", () => s.GuardFuseeCamera, v => { s.GuardFuseeCamera = v; Save(); });
                        Note(b, "Fast-forward switches the main camera off and the game's fusee code then throws an error on every frame. This skips the fusee update while there is no camera.");
                    });

                    page.AddSection("", b =>
                    {
                        b.AddButton("Reset these to defaults", ResetDefaults);
                        Note(b, "Saved to Settings.xml as you change them. Other settings, and anything that switches a feature on or off at start-up, are edited in that file and need a restart.");
                    });

                    page.AddSection("Self-check", b =>
                    {
                        foreach (string line in SelfCheck.Report) Note(b, line);
                    });
                });
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }

        // Explanatory text: smaller and dimmer than the controls, so the sliders stay in view.
        private static void Note(UIPanelBuilder b, string text)
        {
            b.AddLabel(text, (TMP_Text t) =>
            {
                t.fontSize *= 0.75f;
                t.alpha = 0.8f;
            });
        }

        private static string Weight(float v)
        {
            return v.ToString("0.00");
        }

        private static void Slider(UIPanelBuilder b, string label, Func<float> get, Action<float> set, float min, float max, float step, Func<float, string> format)
        {
            b.AddField(label, b.AddSliderQuantized(
                get,
                () => format(get()),
                v => set(Mathf.Clamp(Mathf.Round(v / step) * step, min, max)),
                step, min, max,
                v => Save()));
        }

        private static void ResetDefaults()
        {
            var d = new Settings();
            var s = Main.Settings;
            s.BrakeTapSuppressMph = d.BrakeTapSuppressMph;
            s.CoastBandMph = d.CoastBandMph;
            s.SpeedReductionLeadSeconds = d.SpeedReductionLeadSeconds;
            s.CutoffTolerancePercent = d.CutoffTolerancePercent;
            s.PlanOverspeedScale = d.PlanOverspeedScale;
            s.PlanUndershootScale = d.PlanUndershootScale;
            s.PlanUndershootDeadbandScale = d.PlanUndershootDeadbandScale;
            s.PlanCeilingScale = d.PlanCeilingScale;
            s.PlanIndependentUseScale = d.PlanIndependentUseScale;
            Save();
        }

        private static void Save()
        {
            try
            {
                Main.Settings.Save(Main.SettingsPath);
                Main.Mod.Logger.Log("Settings saved: " + Main.Settings.Describe());
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Could not save settings: " + e.Message);
            }
        }
    }

    /// <summary>Listens for the settings hotkey. Lives on its own object for as long as the mod is loaded.</summary>
    internal sealed class SettingsHotkey : MonoBehaviour
    {
        private InputAction _action;

        private void OnEnable()
        {
            try
            {
                string key = Main.Settings.SettingsHotkey;
                if (string.IsNullOrWhiteSpace(key)) return;

                _action = new InputAction("AutoEngineerBrakeFix.Settings", InputActionType.Button);
                if (Main.Settings.SettingsHotkeyCtrl)
                {
                    _action.AddCompositeBinding("ButtonWithOneModifier")
                        .With("Modifier", "<Keyboard>/ctrl")
                        .With("Button", key);
                }
                else
                {
                    _action.AddBinding(key);
                }
                _action.performed += OnPerformed;
                _action.Enable();
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Settings hotkey not available: " + e.Message);
                _action = null;
            }
        }

        private void OnPerformed(InputAction.CallbackContext context)
        {
            if (!Main.Active) return;
            try
            {
                SettingsUi.Toggle();
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }

        private void OnDisable()
        {
            if (_action == null) return;
            _action.performed -= OnPerformed;
            _action.Disable();
            _action.Dispose();
            _action = null;
        }
    }
}
