using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Model.AI;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// A train that cannot climb a grade ends up in a loop: full throttle, speed falls to nothing, the train
    /// rolls back, the planner loses its targets, the AI brakes lightly, sees targets again, opens the
    /// throttle, and so on, until someone stops it. Neither the game nor the planner has a stall check.
    ///
    /// This one declares a stall when the throttle has been fully open for a while on an uphill grade without
    /// the train gaining speed, and it is under 4 mph. From then on the throttle stays shut, the brakes stay
    /// applied, the start-up routine is skipped and the engineer says so. It lasts until the engineer is
    /// switched off and on again.
    /// </summary>
    internal static class StallGuard
    {
        private const float MpsToMph = 2.23694f;
        private const float MaxSpeedMph = 4f;
        private const float MinUphillPercent = 1f;
        private const float StandMaxMph = 0.5f;
        private const float StandMinThrottle = 0.5f;
        private const float StandGainMph = 0.15f;

        private sealed class State
        {
            public bool Latched;
            public float Timer;
            public float LastTime;
            public float PrevSpeed;
            public float PrevSpeedTime = -100f;
            public float Trend;
            public float LatchTime;
            public float StandTimer;
            public float StandSmooth;
            public float StandRef;
            public string LatchMode = "";
            public int LatchSign;
            public float StillSince = -1f;
            public bool Settled;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();
        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");
        private static readonly MethodInfo MSay = AccessTools.Method(SignalPlanning.PlannerType, "Say", new[] { typeof(string) });
        private static readonly MethodInfo MNotice = AccessTools.Method(typeof(BaseLocomotive), "PostNotice", new[] { typeof(string), typeof(string) });

        internal static bool IsLatched(AutoEngineer ae) => Main.Settings.StallGuard && States.TryGetValue(ae, out State st) && st.Latched;

        /// <summary>
        /// Train brake level while latched: a firm 60% (with the locomotive brake full) while the train is still
        /// moving, so it stops without a jolt, then full once it has been still for a second or 8 s have passed.
        /// </summary>
        internal static float BrakeLevel(AutoEngineer ae)
        {
            if (!States.TryGetValue(ae, out State st) || !st.Latched) return 1f;
            if (st.Settled) return 1f;
            float now = Time.time;
            float speed = ae.Locomotive != null ? Mathf.Abs(ae.Locomotive.velocity) * MpsToMph : 0f;
            if (speed < 0.3f)
            {
                if (st.StillSince < 0f) st.StillSince = now;
            }
            else st.StillSince = -1f;
            if ((st.StillSince >= 0f && now - st.StillSince >= 1f) || now - st.LatchTime >= 8f) st.Settled = true;
            return st.Settled ? 1f : 0.6f;
        }

        internal static void Reset(AutoEngineer ae)
        {
            if (States.TryGetValue(ae, out State st) && st.Latched)
            {
                Main.Mod.Logger.Log("Stall guard cleared.");
            }
            if (States.TryGetValue(ae, out State s2)) { s2.Latched = false; s2.Timer = 0f; s2.Settled = false; s2.StandTimer = 0f; s2.StillSince = -1f; }
        }



        /// <summary>The engineer's mode and direction of travel (the sign of the speed limit, which the planner signs by direction).</summary>
        private static void ReadOrders(AutoEngineer ae, out string mode, out int sign)
        {
            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            mode = targets != null ? targets.Mode.ToString() : "";
            float m = targets != null ? targets.MaxSpeedMph : 0f;
            sign = m > 0.5f ? 1 : (m < -0.5f ? -1 : 0);
        }

        /// <summary>While latched: switching the engineer's direction or mode clears the stall.</summary>
        private static void CheckOrdersChanged(AutoEngineer ae, State st)
        {
            ReadOrders(ae, out string mode, out int sign);
            if (st.LatchSign == 0 && sign != 0) st.LatchSign = sign; // direction was not known yet
            bool modeChanged = !string.IsNullOrEmpty(st.LatchMode) && mode != st.LatchMode;
            bool dirChanged = st.LatchSign != 0 && sign != 0 && sign != st.LatchSign;
            if (!modeChanged && !dirChanged) return;
            Main.Mod.Logger.Log(dirChanged ? "Stall guard cleared: direction changed." : $"Stall guard cleared: mode changed to {mode}.");
            Reset(ae);
        }

        private static void Latch(AutoEngineer ae, State st, string logLine, string say, string notice)
        {
            float now = Time.time;
            st.Latched = true;
            st.LatchTime = now;
            st.Settled = false;
            st.StillSince = -1f;
            st.StandTimer = 0f;
            ReadOrders(ae, out st.LatchMode, out st.LatchSign);
            Main.Mod.Logger.Warning(logLine);
            try
            {
                BrakeFix.WriteControl(ae, "LocomotiveBrake", 1f);
                BrakeFix.WriteControl(ae, "TrainBrake", BrakeLevel(ae));
                BrakeFix.WriteControl(ae, "Throttle", 0f);
                MSay?.Invoke(ae.GetComponent(SignalPlanning.PlannerType), new object[] { say });
                MNotice?.Invoke(ae.Locomotive, new object[] { "ai-stop", notice });
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Stall guard: could not announce: " + e.Message);
            }
        }

        /// <summary>
        /// Called every frame while the engineer is on. Catches a train that cannot get moving at all: throttle at
        /// half or more, brakes off, still under 0.5 mph, on any grade, for the stall time. Runs outside the throttle
        /// PID because the PID does not run while the engineer is in its start-up state.
        /// </summary>
        internal static void Tick(AutoEngineer ae)
        {
            if (!Main.Settings.StallGuard || ae == null || ae.Locomotive == null) return;
            State st = States.GetOrCreateValue(ae);
            if (st.Latched) { CheckOrdersChanged(ae, st); return; }

            float speed = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;
            float frameDt = Mathf.Clamp(Time.deltaTime, 0f, 0.25f);
            st.StandSmooth = st.StandTimer <= 0f ? speed : Mathf.Lerp(st.StandSmooth, speed, 1f - Mathf.Exp(-frameDt / 0.5f));
            bool standing = speed < StandMaxMph
                && BrakeFix.ReadThrottle(ae) >= StandMinThrottle
                && !BrakeFix.ReadBrakeRequested(ae);
            if (!standing)
            {
                st.StandTimer = 0f;
            }
            else
            {
                // A train that is still gaining speed, however slowly, is starting, not stuck: gaining more than
                // StandGainMph from its lowest point restarts the clock.
                if (st.StandTimer <= 0f || st.StandSmooth > st.StandRef + StandGainMph)
                {
                    st.StandRef = st.StandSmooth;
                    st.StandTimer = 0f;
                }
                else
                {
                    st.StandRef = Mathf.Min(st.StandRef, st.StandSmooth);
                }
                st.StandTimer += frameDt;
            }

            if (st.StandTimer < Mathf.Max(3f, Main.Settings.StallSeconds)) return;

            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            float grade = targets != null ? targets.AverageGradeUnder : 0f;
            Latch(ae, st, $"Stall guard: could not get moving ({speed:0.00} mph with the throttle at {BrakeFix.ReadThrottle(ae):0.00}, grade {-grade:0.00}% up). Holding with the brakes.",
                "We can't get this train moving. Holding with the brakes - we need help or a lighter train.", "Can't start the train");
        }

        /// <summary>
        /// Called from the throttle PID postfix with its output. Returns true when the stall guard has taken
        /// over (the throttle is forced shut).
        /// </summary>
        internal static bool Check(AutoEngineer ae, ref float throttle)
        {
            if (!Main.Settings.StallGuard || ae.Locomotive == null) return false;
            State st = States.GetOrCreateValue(ae);
            if (st.Latched) { throttle = 0f; return true; }

            float now = Time.time;
            float speed = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;
            if (now - st.PrevSpeedTime > 3f) { st.Trend = 0f; st.PrevSpeed = speed; st.PrevSpeedTime = now; }
            else if (now - st.PrevSpeedTime >= 0.4f)
            {
                st.Trend = Mathf.Lerp(st.Trend, (speed - st.PrevSpeed) / (now - st.PrevSpeedTime), 0.5f);
                st.PrevSpeed = speed; st.PrevSpeedTime = now;
            }

            float dt = st.LastTime > 0f ? Mathf.Clamp(now - st.LastTime, 0f, 1.5f) : 0f;
            st.LastTime = now;

            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            float grade = targets != null ? targets.AverageGradeUnder : 0f; // percent, negative = uphill in the direction of travel
            bool stalling = throttle >= 0.95f && speed < MaxSpeedMph && grade <= -MinUphillPercent && st.Trend <= 0.05f;
            st.Timer = stalling ? st.Timer + dt : Mathf.Max(0f, st.Timer - 2f * dt);

            if (st.Timer < Mathf.Max(3f, Main.Settings.StallSeconds)) return false;

            Latch(ae, st, $"Stall guard: stalled at {speed:0.0} mph on a {-grade:0.0}% grade. Holding with the brakes.",
                "We're stalled on the grade. Holding with the brakes - we need help or a lighter train.", "Stalled on grade");
            throttle = 0f;
            return true;
        }
    }

    /// <summary>While the stall guard holds, the start-up routine (reverser, throttle 25-50%) does nothing but wait.</summary>
    [HarmonyPatch(typeof(AutoEngineer), "StartMovement")]
    internal static class AutoEngineer_StartMovement_Patch
    {
        private static bool Prefix(AutoEngineer __instance, ref IEnumerator __result)
        {
            if (!Main.Active || !StallGuard.IsLatched(__instance)) return true;
            __result = Hold(__instance);
            return false;
        }

        private static IEnumerator Hold(AutoEngineer ae)
        {
            BrakeFix.WriteControl(ae, "Throttle", 0f);
            BrakeFix.WriteControl(ae, "LocomotiveBrake", 1f);
            BrakeFix.WriteControl(ae, "TrainBrake", StallGuard.BrakeLevel(ae));
            yield return new WaitForSeconds(1f);
        }
    }
}
