using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Guidance for 0.6.0's rollout controller, which chooses throttle and brake actions by simulating the next
    /// few seconds and scoring them. Its cost function is compiled (Burst) code, so it cannot be changed; what
    /// can be changed is what goes into it: the brake limit it plans with and the score weights.
    /// </summary>
    internal static class BrakeGuidance
    {
        private const float MpsToMph = 2.23694f;

        /// <summary>Below this speed the direction-change limit lifts, so the train can still be held.</summary>
        internal const float FullBrakeBelowMph = 2f;

        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");
        internal static readonly Type TrainBrakeType = AccessTools.Inner(typeof(AutoEngineer), "TrainBrake");
        private static readonly FieldInfo FTrainBrakeAe = TrainBrakeType != null ? AccessTools.Field(TrainBrakeType, "_ae") : null;

        internal sealed class Record
        {
            public float Raw;
            public float Final;
            public bool Active;
        }

        /// <summary>What the last brake-limit query for each engineer returned (read by the logger).</summary>
        internal static readonly ConditionalWeakTable<AutoEngineer, Record> Records = new ConditionalWeakTable<AutoEngineer, Record>();

        /// <summary>
        /// True while the train is moving with no speed limit order and nothing ahead to stop for - what the
        /// engineer is left with when the direction is changed on a moving train. It ends by itself the moment a
        /// target or a speed limit appears again (a new order, a signal, a waypoint), or the train nearly stops.
        /// </summary>
        internal static bool StopWithoutTarget(AutoEngineer ae)
        {
            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            if (targets == null || ae.Locomotive == null) return false;
            if (Mathf.Abs(ae.Locomotive.velocity) * MpsToMph < FullBrakeBelowMph) return false;
            if (Mathf.Abs(targets.MaxSpeedMph) > 0.5f) return false;
            var list = targets.AllTargets as IList<AutoEngineer.Targets.Target>;
            return list != null && list.Count == 0;
        }

        /// <summary>Postfix body for TrainBrake.CalculateMaxEffectiveApplication.</summary>
        internal static void LimitApplication(object trainBrake, ref float result)
        {
            try
            {
                var ae = FTrainBrakeAe?.GetValue(trainBrake) as AutoEngineer;
                if (ae == null) return;

                float raw = result;
                float limit = Main.Settings.DirectionStopBrakeLimit;
                bool active = Main.Active && limit > 0f && StopWithoutTarget(ae);
                if (active) result = Mathf.Min(result, limit);

                var record = Records.GetOrCreateValue(ae);
                record.Raw = raw;
                record.Final = result;
                record.Active = active;
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }

    /// <summary>
    /// The planner reads this value as the deepest brake application it may use, and TrainBrake.DriveToward obeys
    /// it, so lowering it while a direction-change stop is under way makes the engineer plan a gentler stop
    /// instead of braking at the game's maximum.
    /// </summary>
    [HarmonyPatch]
    internal static class TrainBrake_CalculateMaxEffectiveApplication_Patch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(BrakeGuidance.TrainBrakeType, "CalculateMaxEffectiveApplication");
        }

        private static void Postfix(object __instance, ref float __result)
        {
            BrakeGuidance.LimitApplication(__instance, ref __result);
        }
    }

    /// <summary>
    /// Scales the rollout controller's score weights (each setting is a multiplier, 1 = the game's value, so a
    /// game update that retunes the stock weights still carries through).
    /// </summary>
    [HarmonyPatch(typeof(RolloutController), "WeightsFor", new[] { typeof(AutoEngineer) })]
    internal static class RolloutController_WeightsFor_Patch
    {
        private static void Postfix(ref RolloutScoreWeights __result)
        {
            try
            {
                var s = Main.Settings;
                __result.Overspeed *= Mathf.Max(0f, s.PlanOverspeedScale);
                __result.Undershoot *= Mathf.Max(0f, s.PlanUndershootScale);
                __result.UndershootDeadbandMps *= Mathf.Max(0f, s.PlanUndershootDeadbandScale);
                __result.Ceiling *= Mathf.Max(0f, s.PlanCeilingScale);
                __result.IndependentUse *= Mathf.Max(0f, s.PlanIndependentUseScale);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }
}
