using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model.AI;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// The AI alternates brake pulses and throttle bursts around its setpoint: a brake pulse overshoots a
    /// little under the setpoint, the throttle PID sees the train under it and opens the throttle in a burst,
    /// the train drifts back over, and the brake is applied again. This shows as a repeating brake/throttle
    /// cycle every 10 s or so on a gentle downhill at the speed limit, and while slowing for a stop.
    ///
    /// Three rules:
    ///  - Throttle: no throttle burst while the brake is on, was on in the last few seconds, or the target is
    ///    falling, unless the train is clearly under the setpoint.
    ///  - Brake: while the target is falling, a light application is not released until the train is clearly
    ///    under the target (so the application builds instead of flickering).
    ///  - Stop line: no throttle at all within a few metres of a stop target.
    /// </summary>
    internal static class DecelHold
    {
        private const float MpsToMph = 2.23694f;
        private const float FallingMphPerSecond = 0.15f; // target dropping faster than this counts as slowing down
        private const float WindowSeconds = 2.5f;
        private const float MinSpeedMph = 1.5f;          // below this the throttle is left alone (unless at the stop line)
        private const float RecentBrakeSeconds = 10f;    // after the brake was last on
        private const float CruiseExtraMph = 0.75f;      // under the padded setpoint before a throttle burst is allowed
        private const float LightBrakeMax = 0.07f;       // brake holding only applies to light applications
        private const float StopLineMeters = 3f;
        private const float TrendLeadSeconds = 3f;

        private static readonly FieldInfo FCtx = AccessTools.Field(typeof(AutoEngineer), "_debugContextualTargetVelocity");
        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");
        private static readonly MethodInfo MAvgCylinder = AccessTools.Method(typeof(AutoEngineer), "AverageTrainBrakeCylinder");

        internal static float DebugResidual(AutoEngineer ae) => States.TryGetValue(ae, out State st) ? st.Residual : float.NaN;
        internal static float DebugNeed(AutoEngineer ae) => States.TryGetValue(ae, out State st) ? st.Need : float.NaN;

        internal static bool IsFalling(AutoEngineer ae) => States.TryGetValue(ae, out State st) && st.Falling;

        private sealed class State
        {
            public float PrevSpeed;
            public float PrevSpeedTime = -100f;
            public float Trend; // mph per second, smoothed
            public bool HasMark;
            public float MarkCtx;
            public float MarkTime;
            public bool Falling;
            public float LastBrakeTime = -100f;
            public float CylPrev;
            public float CylTime = -100f;
            public float CylRate;      // psi per second, smoothed
            public float LowerTime = -100f;
            public float Residual = float.NaN;
            public float Need = float.NaN;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States =
            new ConditionalWeakTable<AutoEngineer, State>();

        private static float Padding(AutoEngineer ae, float speedMph)
        {
            var config = BrakeFix.GetConfig(ae);
            return config != null && config.paddingForSpeedMph != null ? config.paddingForSpeedMph.Evaluate(speedMph) : 0f;
        }

        /// <summary>Metres to the nearest stop (0 mph) target still ahead, or a large number.</summary>
        internal static float DistanceToStopTarget(AutoEngineer ae)
        {
            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            var list = targets?.AllTargets as IList<AutoEngineer.Targets.Target>;
            float best = 1e9f;
            if (list == null) return best;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (Mathf.Abs(t.SpeedMph) < 0.1f && t.Distance > -5f && t.Distance < best) best = t.Distance;
            }
            return best;
        }

        /// <summary>True in Road mode (signals, track speed). Waypoint and Yard keep the game's own stopping accuracy.</summary>
        internal static bool IsRoad(AutoEngineer ae)
        {
            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            return targets != null && targets.Mode.ToString() == "Road";
        }

        /// <summary>
        /// The nearest speed limit ahead (5 mph or more, so not a stop) that is lower than where the train is
        /// headed. The planner only lists targets within its lookahead, so "ahead" is whatever the AI can see.
        /// Speeds are signed by direction of travel, so compare magnitudes.
        /// </summary>
        internal static bool LowerLimitAhead(AutoEngineer ae, float ctxMph, out float limitMph)
        {
            limitMph = 0f;
            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            var list = targets?.AllTargets as IList<AutoEngineer.Targets.Target>;
            if (list == null) return false;
            float bestDistance = 1e9f;
            bool found = false;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                float v = Mathf.Abs(t.SpeedMph);
                if (v < 5f || t.Distance <= 0f || v >= ctxMph - 0.5f) continue;
                if (t.Distance < bestDistance) { bestDistance = t.Distance; limitMph = v; found = true; }
            }
            return found;
        }

        /// <summary>
        /// Train-brake PID postfix. A release while the target is falling is turned into "leave it as it is"
        /// until the train is clearly under the target. Only for light applications: a heavy one slows the
        /// train too fast to hold on to.
        /// </summary>
        internal static void HoldBrake(AutoEngineer ae, AutoEngineerConfig config, ref float result)
        {
            if (config == null || ae.Locomotive == null) return;
            if (EarlyRelease(ae, config, ref result)) return;
            if (!Main.Settings.HoldBrakeWhileSlowing) return;
            if (!States.TryGetValue(ae, out State st) || !st.Falling) return;
            if (result >= config.trainBrakeReleaseBelowOutput) return;           // not a release
            float level = BrakeFix.ReadControl(ae, "TrainBrake");
            if (level <= 0.001f || level > LightBrakeMax) return;                // nothing applied, or too heavy
            float speedMph = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;
            float ctxMph = ((float)FCtx.GetValue(ae)) * MpsToMph;
            float pad = config.paddingForSpeedMph != null ? config.paddingForSpeedMph.Evaluate(speedMph) : 0f;
            float margin = Mathf.Clamp(0.08f * speedMph, 0.5f, 1.2f) + 0.5f * pad;
            if (ctxMph - speedMph < margin) result = 0f;                          // 0 = neither apply nor release
        }

        /// <summary>
        /// The brakes keep working for several seconds after a release (a long train bleeds off slowly), so a
        /// train released exactly at the target speed ends up well under it. Release once the speed it is
        /// heading for, a few seconds ahead, reaches the target. Not near a stop.
        /// </summary>
        private static bool EarlyRelease(AutoEngineer ae, AutoEngineerConfig config, ref float result)
        {
            float lead = Main.Settings.BrakeReleaseLeadSeconds;
            if (lead <= 0f || !States.TryGetValue(ae, out State st) || st.Trend > -0.2f) return false;
            if (result < config.trainBrakeReleaseBelowOutput) return false;          // already a release
            if (BrakeFix.ReadControl(ae, "TrainBrake") <= 0.001f) return false;      // nothing to release
            float speedMph = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;
            float ctxMph = ((float)FCtx.GetValue(ae)) * MpsToMph;
            if (ctxMph < 8f || DistanceToStopTarget(ae) < 150f) return false;
            if (speedMph + st.Trend * lead > ctxMph) return false;
            result = config.trainBrakeReleaseBelowOutput - 0.01f;
            return true;
        }


        /// <summary>
        /// A long train's brake cylinders bleed down slowly after a release (about 2 psi/s above 30 psi, under 1 psi/s
        /// below that), and until they are empty they keep taking speed off: roughly cylinder-psi squared / 60 mph
        /// from a 52-car train. The stock logic ignores that and keeps the brake on until the train is at the limit, so
        /// it ends up well under it (a 5 mph reduction became 9-12 mph in the logs).
        ///
        /// While slowing for a lower limit (and for 45 s after), this compares the speed the cylinders will still take
        /// off with the reduction still needed. Once they cover it, the train brake is released if it is on, and a new
        /// application is held off. Not near a stop, not while the train is gaining speed (a descent).
        /// Returns true when it has taken over the output.
        /// </summary>
        internal static bool ResidualCover(AutoEngineer ae, AutoEngineerConfig config, ref float result)
        {
            if (!Main.Settings.BrakeResidual || Main.Settings.BrakeResidualDivisor <= 1f) return false;
            if (config == null || ae.Locomotive == null || MAvgCylinder == null || FCtx == null) return false;
            State st = States.GetOrCreateValue(ae);
            st.Residual = float.NaN; st.Need = float.NaN;

            float cyl;
            try { cyl = (float)MAvgCylinder.Invoke(ae, null); } catch { return false; }
            if (float.IsNaN(cyl) || cyl < 0f) return false;

            float now = Time.time;
            if (now - st.CylTime > 3f) { st.CylRate = 0f; st.CylPrev = cyl; st.CylTime = now; }
            else if (now - st.CylTime >= 0.3f)
            {
                st.CylRate = Mathf.Lerp(st.CylRate, (cyl - st.CylPrev) / (now - st.CylTime), 0.5f);
                st.CylPrev = cyl; st.CylTime = now;
            }

            float speedMph = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;
            float ctxMph = ((float)FCtx.GetValue(ae)) * MpsToMph;
            if (ctxMph < 8f || DistanceToStopTarget(ae) < 150f) return false;
            if (st.Trend > 0.1f) return false;                       // gaining speed: not for us to hold the brake off

            float limit;
            if (LowerLimitAhead(ae, ctxMph, out float ahead)) { st.LowerTime = now; limit = ahead; }
            else if (now - st.LowerTime <= 45f) limit = ctxMph;       // just passed it: the new limit
            else return false;

            // Cylinders still filling will peak a little higher than they read now.
            float peak = cyl + 0.5f * Mathf.Max(0f, st.CylRate);
            float residual = peak * peak / Main.Settings.BrakeResidualDivisor;
            float need = speedMph - (limit + Main.Settings.BrakeResidualMarginMph);
            st.Residual = residual; st.Need = need;
            if (residual < need) return false;                         // still has to brake: stock logic

            if (BrakeFix.ReadControl(ae, "TrainBrake") > 0.001f)
            {
                result = config.trainBrakeReleaseBelowOutput - 0.01f;  // release
                return true;
            }
            if (result > 0f) { result = 0f; return true; }             // hold off a new application
            return false;
        }

        /// <summary>
        /// Brake PID postfix: while holding a steady limit, a few tenths over it is coasting, not a reason to
        /// brake. Only blocks a NEW application (brake currently released) while the contextual target is not
        /// falling; once the brake is on, or the train is slowing for a lower target, the stock logic runs.
        /// </summary>
        internal static void HoldCoast(AutoEngineer ae, AutoEngineerConfig config, float error, bool trainBrake, ref float result)
        {
            float band = Main.Settings.BrakeStartOverMph;
            if (band <= 0f || ae.Locomotive == null || config == null || error >= 0f) return;
            if (IsFalling(ae)) return;
            if (BrakeFix.ReadControl(ae, trainBrake ? "TrainBrake" : "LocomotiveBrake") > 0.001f) return;
            float power = Mathf.Max(1f, config.brakeErrorPower);
            float overMph = Mathf.Pow(-error, 1f / power) * MpsToMph;
            if (overMph < band && result > 0f) result = 0f;
        }

        /// <summary>Called with the throttle PID's output before smoothing. Sets it to 0 while holding.</summary>
        internal static void Apply(AutoEngineer ae, ref float result)
        {
            if (!Main.Settings.HoldThrottleWhileSlowing || FCtx == null || ae.Locomotive == null) return;

            var st = States.GetOrCreateValue(ae);
            float ctxMph = ((float)FCtx.GetValue(ae)) * MpsToMph;
            float now = Time.time;
            if (!st.HasMark || now - st.MarkTime > 6f)
            {
                st.HasMark = true; st.MarkCtx = ctxMph; st.MarkTime = now; st.Falling = false;
            }
            else if (now - st.MarkTime >= WindowSeconds)
            {
                st.Falling = (ctxMph - st.MarkCtx) / (now - st.MarkTime) < -FallingMphPerSecond;
                st.MarkCtx = ctxMph; st.MarkTime = now;
            }

            float speedNow = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;
            if (now - st.PrevSpeedTime > 3f) { st.Trend = 0f; st.PrevSpeed = speedNow; st.PrevSpeedTime = now; }
            else if (now - st.PrevSpeedTime >= 0.4f)
            {
                float instant = (speedNow - st.PrevSpeed) / (now - st.PrevSpeedTime);
                st.Trend = Mathf.Lerp(st.Trend, instant, 0.5f);
                st.PrevSpeed = speedNow; st.PrevSpeedTime = now;
            }

            bool brakeOn = BrakeFix.ReadControl(ae, "TrainBrake") > 0.001f;
            if (brakeOn) st.LastBrakeTime = now;

            if (result <= 0f) return;
            float speedMph = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;

            // Stop line: the planner's speed curve never reaches 0 (about 1 mph at the target), so the AI creeps
            // past a stop target. Keep the throttle closed once the train is at it.
            if (IsRoad(ae) && DistanceToStopTarget(ae) < StopLineMeters) { result = 0f; return; }

            // A lower limit is in sight and the train is already at or above that speed: no power. It coasts
            // down (the brake curve takes over later); power returns if it falls a mph under the lower limit.
            if (Main.Settings.AvoidPowerIntoReductions && ctxMph > 0f && LowerLimitAhead(ae, ctxMph, out float lowerMph)
                && speedMph > lowerMph - 1f)
            {
                result = 0f;
                return;
            }

            if (speedMph < MinSpeedMph || ctxMph <= 0f) return;

            bool recentBrake = brakeOn || now - st.LastBrakeTime < RecentBrakeSeconds;
            if (!st.Falling && !recentBrake) return;

            // After a brake release the train keeps slowing for a few seconds (the brakes bleed off slowly), and
            // waiting for it to be well under the setpoint lets it undershoot. Open the throttle once the speed
            // it is heading for is under the setpoint.
            if (!st.Falling && !brakeOn && st.Trend < -0.1f && ctxMph - (speedMph + st.Trend * TrendLeadSeconds) >= 0.5f) return;

            float deadband = st.Falling
                ? Mathf.Clamp(0.12f * speedMph, 0.75f, 2f)
                : Padding(ae, speedMph) + CruiseExtraMph;
            if (ctxMph - speedMph < deadband) result = 0f;
        }
    }

    /// <summary>
    /// The planner's speed curve allows about 0.5 m/s (1.1 mph) even at 1 m from a stop target, and AutoEngineer
    /// only treats a target as "the" target once it is within 1 m (TargetSpeedMph). A train that stops 1-3 m
    /// short of its target therefore counts as "stopped but should be moving": the start-up routine runs
    /// (reverser to full, throttle 25-50%) and the train creeps past the signal until the brake catches it.
    /// Reporting a contextual target of 0 within a couple of metres of a stop target makes the AI hold the stop.
    /// </summary>
    [HarmonyPatch(typeof(AutoEngineer), "ContextualTargetVelocity")]
    internal static class AutoEngineer_ContextualTargetVelocity_Patch
    {
        private const float StopWithinMeters = 2.5f;

        private static void Postfix(AutoEngineer __instance, ref float __result)
        {
            if (!Main.Active || __result == 0f || !Main.Settings.HoldStopAtStopTargets) return;
            if (!DecelHold.IsRoad(__instance)) return; // Waypoint/Yard: the game's own stopping accuracy
            if (DecelHold.DistanceToStopTarget(__instance) < StopWithinMeters) __result = 0f;
        }
    }
}
