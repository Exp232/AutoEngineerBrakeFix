using System;
using System.Reflection;
using HarmonyLib;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Optional fuel saving (BrakeTapSuppressMph, off by default). The rollout controller sometimes applies the train
    /// brake to shed a mph or two, which spends fuel to gain the speed and then throws it away as heat. When the
    /// train is only a little over its target, is not gaining speed, and nothing ahead needs it to slow, this turns
    /// that first application into a coast; if the speed then rises past the margin or a lower limit comes into
    /// range, the controller's brake application goes through as usual. Road mode only.
    /// </summary>
    internal static class BrakeTaps
    {
        private const float MpsToMph = 2.23694f;
        private const float MaxAccelMps2 = 0.03f;       // measured acceleration at or below this: not gaining speed
        private const float AppliedSetMin = 0.02f;      // the game's own "brake is applied" threshold
        private static bool _logged;

        internal static readonly Type LoopType = AccessTools.Inner(typeof(AutoEngineer), "SpeedControlLoop");
        internal static readonly FieldInfo FAe = LoopType != null ? AccessTools.Field(LoopType, "_ae") : null;
        internal static readonly FieldInfo FTrainBrake = LoopType != null ? AccessTools.Field(LoopType, "_trainBrake") : null;
        internal static readonly FieldInfo FMeasuredAccel = LoopType != null ? AccessTools.Field(LoopType, "_measuredAccel") : null;
        private static readonly PropertyInfo PApplied = BrakeGuidance.TrainBrakeType != null ? AccessTools.Property(BrakeGuidance.TrainBrakeType, "AppliedBrakeSet") : null;
        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");
        private static readonly FieldInfo FContextual = AccessTools.Field(typeof(AutoEngineer), "_debugContextualTargetVelocity");

        internal static bool Available => LoopType != null && FAe != null && FTrainBrake != null && FMeasuredAccel != null && PApplied != null && FTargets != null && FContextual != null;

        /// <summary>Returns true when the command was a brake tap and has been replaced by a coast.</summary>
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            float limitMph = Main.Settings.BrakeTapSuppressMph;
            if (limitMph <= 0f || !command.DrivesTrainBrake || command.BailOff || command.IndependentBrake01 > 0.01f) return false;

            var ae = FAe.GetValue(loop) as AutoEngineer;
            if (ae == null || ae.Locomotive == null) return false;
            float applied = Convert.ToSingle(PApplied.GetValue(FTrainBrake.GetValue(loop), null));
            if (applied > AppliedSetMin || command.TrainBrakeSet <= applied + 0.005f) return false;   // only a first application from released

            var targets = FTargets.GetValue(ae) as AutoEngineer.Targets;
            if (targets == null || targets.AllTargets == null || targets.Mode.ToString() != "Road") return false;

            float speed = Mathf.Abs(ae.Locomotive.velocity);
            float target = Mathf.Abs(Convert.ToSingle(FContextual.GetValue(ae)));
            if (target < 3f || speed < 3f) return false;
            if (speed - target > limitMph / MpsToMph) return false;                                   // really over: brake
            if (Convert.ToSingle(FMeasuredAccel.GetValue(loop)) > MaxAccelMps2) return false;         // still gaining speed: brake

            // Anything ahead that needs the train to slow (a lower limit or a stop) keeps the controller in charge.
            foreach (var t in targets.AllTargets)
            {
                if (t.Distance <= 0f) continue;
                float tv = Mathf.Abs(t.SpeedMph) / MpsToMph;
                if (tv >= speed - 0.9f) continue;
                float window = tv < 0.5f ? Mathf.Max(1500f, speed * 80f) : Mathf.Max(600f, speed * 40f);
                if (t.Distance < window) return false;
            }

            command = new RolloutCommand(RolloutActionKind.Hold, 0f, command.IndependentBrake01, applied, false, false, command.Cost, command.HoldCost);
            FuelSummary.CountSuppressed(ae);
            if (!_logged)
            {
                _logged = true;
                Main.Mod.Logger.Log("Brake tap suppressed for " + ae.Locomotive.DisplayName + " at " + (speed * MpsToMph).ToString("0.0") + " mph, target " + (target * MpsToMph).ToString("0.0") + " mph (further ones are only counted in fuel_summary).");
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class SpeedControlLoop_ApplyRolloutCommand_Patch
    {
        private static MethodBase TargetMethod()
        {
            return BrakeTaps.LoopType != null ? AccessTools.Method(BrakeTaps.LoopType, "ApplyRolloutCommand") : null;
        }

        // ApplyRolloutCommand(in RolloutCommand command): the first argument is patched by reference.
        private static void Prefix(object __instance, ref RolloutCommand __0)
        {
            if (!Main.Active) return;
            try
            {
                BrakeTaps.Filter(__instance, ref __0);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }
}
