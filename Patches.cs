using System;
using HarmonyLib;
using Model.AI;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Runs as each AutoEngineer is enabled: attaches the logger's sampler, registers the engineer's brake PIDs
    /// with the brake fix, and dumps the live config once.
    /// </summary>
    [HarmonyPatch(typeof(AutoEngineer), "OnEnable")]
    internal static class AutoEngineer_OnEnable_Patch
    {
        private static void Postfix(AutoEngineer __instance)
        {
            Attach(__instance);
        }

        /// <summary>Also called for engineers that were already running when the mod was switched on.</summary>
        internal static void Attach(AutoEngineer __instance)
        {
            try
            {
                if (__instance.GetComponent<AutoEngineerSampler>() == null)
                {
                    __instance.gameObject.AddComponent<AutoEngineerSampler>();
                }

                if (__instance.GetComponent<ThrottleGlider>() == null)
                {
                    __instance.gameObject.AddComponent<ThrottleGlider>();
                }

                BrakeFix.Register(__instance);
                if (Main.Settings.LogEnabled)
                {
                    ConfigDump.DumpOnce(__instance);
                    ConfigDump.DumpEngineOnce(__instance);
                }
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }
}
