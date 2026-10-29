using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Makes the game's fuel and water deduction follow game time when time is sped up (the fast-forward of 0.6.1).
    ///
    /// BaseLocomotive.PeriodicUpdateBody waits WaitForSeconds(1f) and then calls PeriodicUpdate(1f). A coroutine
    /// resumes at most once per frame, so when one frame covers more than a second of game time (fast-forward) it
    /// still calls PeriodicUpdate once, with dt = 1, and the rest of the time is never charged. Logged on the
    /// Bryson to Sylva runs: at normal speed the tender lost 6,315 lb of coal against 6,322 lb computed from the
    /// burn rate; in fast-forward it lost 36% of the computed amount (coal and water alike).
    ///
    /// The prefixes below replace dt with the game time that has really passed since the previous call for the same
    /// locomotive, but only when that is clearly more than a second, so at normal speed nothing changes.
    /// </summary>
    internal static class TimeWarpFuel
    {
        private const double NormalSeconds = 1.25;   // the coroutine's own wait is 1 s; anything up to this is left alone
        private const double MaxSeconds = 60.0;      // never charge more than a minute at once (a hitch, not a time warp)

        private sealed class Clock
        {
            public double Last = double.NaN;
        }

        private static readonly ConditionalWeakTable<BaseLocomotive, Clock> Clocks = new ConditionalWeakTable<BaseLocomotive, Clock>();
        private static bool _logged;

        internal static void Scale(BaseLocomotive loco, ref float dt)
        {
            if (!Main.Active || !Main.Settings.FuelFollowsTimeWarp || loco == null) return;
            try
            {
                Clock clock = Clocks.GetOrCreateValue(loco);
                double now = Time.timeAsDouble;
                double last = clock.Last;
                clock.Last = now;
                if (double.IsNaN(last)) return;

                double elapsed = now - last;
                if (elapsed <= NormalSeconds) return;

                dt = (float)Math.Min(elapsed, MaxSeconds);
                if (!_logged)
                {
                    _logged = true;
                    Main.Mod.Logger.Log("Fuel use follows game time: " + loco.DisplayName + " last updated " + elapsed.ToString("0.0") + " s of game time ago, charging that instead of 1 s (time is sped up; further cases are not logged).");
                }
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SteamLocomotive), "PeriodicUpdate", new[] { typeof(float) })]
    internal static class SteamLocomotive_PeriodicUpdate_Patch
    {
        // Parameter name must match the original: PeriodicUpdate(float dt).
        private static void Prefix(SteamLocomotive __instance, ref float dt)
        {
            TimeWarpFuel.Scale(__instance, ref dt);
        }
    }

    [HarmonyPatch(typeof(DieselLocomotive), "PeriodicUpdate", new[] { typeof(float) })]
    internal static class DieselLocomotive_PeriodicUpdate_Patch
    {
        private static void Prefix(DieselLocomotive __instance, ref float dt)
        {
            TimeWarpFuel.Scale(__instance, ref dt);
        }
    }
}
