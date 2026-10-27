using HarmonyLib;
using Model.AI;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Experimental planning tweaks, both off (1.0) by default.
    ///
    /// Lookahead: AutoEngineer.CalculateLookaheadDistance() says how far ahead the planner looks for speed
    /// limits and signals. Scaling it up makes the AI see targets sooner, if the planner uses it for discovery.
    ///
    /// Brake planning: the AI works out the fastest speed it may carry toward a known target as
    /// sqrt(vTarget^2 + 2 * a * distance), where a is the train's full available braking divided by its mass. It
    /// therefore waits until the last moment and then brakes at full effort. Scaling the braking it assumes down
    /// makes it start slowing earlier and brake more gently for targets it already knows about.
    /// </summary>
    [HarmonyPatch(typeof(AutoEngineer), "CalculateLookaheadDistance")]
    internal static class AutoEngineer_CalculateLookaheadDistance_Patch
    {
        private static void Postfix(ref float __result)
        {
            if (!Main.Active) return;
            float m = Mathf.Clamp(Main.Settings.LookaheadMultiplier, 1f, 4f);
            if (m > 1.001f) __result *= m;
        }
    }

    [HarmonyPatch(typeof(AutoEngineer), "CalculateMaxVelocityToSlowToSpeedAtDistance")]
    internal static class AutoEngineer_CalculateMaxVelocityToSlowToSpeedAtDistance_Patch
    {
        // Parameter name must match the original: (float velocityFinalMpsAbs, float distance, float totalAvailableBraking).
        private static void Prefix(ref float totalAvailableBraking)
        {
            if (!Main.Active) return;
            float f = Mathf.Clamp(Main.Settings.BrakePlanningFraction, 0.2f, 1f);
            if (f < 0.999f) totalAvailableBraking *= f;
        }
    }
}
