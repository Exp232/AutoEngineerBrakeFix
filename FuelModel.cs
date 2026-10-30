using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model.Physics;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Realistic fuel and water use for steam locomotives (optional, off by default).
    ///
    /// The game computes water from regulator x wheel speed x cylinder volume x cutoff (x 1.3), and coal from that
    /// water through an empirical curve. That makes use rise with speed even though a locomotive's power falls
    /// with speed, so fuel per horsepower-hour is about right at 10-17 mph and far too high at 35-40 mph.
    ///
    /// This replaces it with a power-based model: steam = steam rate (lb per indicated hp-hour) x indicated power,
    /// where indicated power = the engine's own tractive effort x speed / mechanical efficiency. Coal follows from
    /// the steam through an evaporation curve: 8 lb water per lb coal at light firing, 7 at moderate firing
    /// (about 650 lb of water per sq ft of grate per hour, roughly 90 lb of coal) and 5.5 at heavy firing (935 lb of
    /// water, 170 lb of coal), falling slowly to 5 beyond. Anchored between Ralph Johnson's road figures (6.5 at
    /// 100 lb of coal, 5.5 above 170) and Goss's superheated test-plant result (about 7.3 at 200 psi, 1910).
    /// </summary>
    internal static class FuelModel
    {
        internal const float WaterLbPerGallon = 8.33f;
        private const float MinWorkMph = 2f; // a locomotive with the regulator open uses steam even when standing

        internal static float EvaporationRatio(float waterLbPerSqFtHour)
        {
            float x = waterLbPerSqFtHour;
            if (x <= 450f) return 8f;
            if (x <= 650f) return 8f - (x - 450f) / 200f * 1f;
            if (x <= 935f) return 7f - (x - 650f) / 285f * 1.5f;
            return Mathf.Max(5f, 5.5f - (x - 935f) / 1000f * 0.5f);
        }

        /// <summary>Steam use in lb/s and coal use in lb/s for the given tractive effort, speed and cutoff (a fraction of the stroke, 0.15 to 0.85; not the reverser lever).</summary>
        internal static void Compute(float tractiveEffortLbf, float speedMph, float cutoff, float grateSqFt, out float waterGps, out float coalLbs)
        {
            float te = Mathf.Abs(tractiveEffortLbf);
            float railHp = te * Mathf.Max(Mathf.Abs(speedMph), MinWorkMph) / 375f;
            float efficiency = Mathf.Clamp(Main.Settings.MechanicalEfficiency, 0.6f, 1f);
            float ihp = railHp / efficiency;
            // Longer cutoff means poorer expansion, so more steam for the same power; shorter cutoff means less.
            float slope = Mathf.Clamp(Main.Settings.CutoffSteamSlope, 0f, 1.5f);
            float cutoffFactor = Mathf.Clamp(1f + slope * (Mathf.Abs(cutoff) - 0.30f), 0.8f, 1.4f);
            float steamLbPerSecond = ihp * Mathf.Clamp(Main.Settings.SteamLbPerIhpHour, 10f, 30f) * cutoffFactor / 3600f;
            waterGps = steamLbPerSecond / WaterLbPerGallon;

            float grate = Mathf.Max(grateSqFt, 10f);
            float ratio = EvaporationRatio(steamLbPerSecond * 3600f / grate);
            float fade = Mathf.InverseLerp(0f, 0.1f, waterGps); // as the game does, so coal fades out near zero steam
            coalLbs = steamLbPerSecond / ratio * fade * Mathf.Clamp(Main.Settings.CoalScale, 0.5f, 1.5f);
        }
    }

    /// <summary>Runs after SteamEngine.UpdateConsumption and replaces the rates it just set.</summary>
    [HarmonyPatch(typeof(SteamEngine), "UpdateConsumption")]
    internal static class SteamEngine_UpdateConsumption_Patch
    {
        private sealed class Rates
        {
            public float Water;
            public float Coal;
        }

        private static readonly FieldInfo FGrate = AccessTools.Field(typeof(SteamEngine), "_estimatedGrateSqFt");
        private static readonly MethodInfo SetWater = AccessTools.PropertySetter(typeof(SteamEngine), "WaterConsumptionRate");
        private static readonly MethodInfo SetCoal = AccessTools.PropertySetter(typeof(SteamEngine), "CoalConsumptionRate");
        private static readonly ConditionalWeakTable<SteamEngine, Rates> State = new ConditionalWeakTable<SteamEngine, Rates>();
        private static bool _warned;

        // Parameter name must match the original: UpdateConsumption(float wheelVelocityMph).
        private static void Postfix(SteamEngine __instance, float wheelVelocityMph)
        {
            if (!Main.Active || !Main.Settings.RealisticFuel) return;
            if (FGrate == null || SetWater == null || SetCoal == null)
            {
                if (!_warned) { _warned = true; Main.Mod.Logger.Warning("Realistic fuel: engine members not found; stock fuel use kept."); }
                return;
            }

            float grate = (float)FGrate.GetValue(__instance);
            // The reverser is the lever position (0.1 to 1); the game maps it to a cutoff of 0.15 to 0.85 (TrainMath.ReverserToCutoff).
            float cutoff = TrainMath.ReverserToCutoff(Mathf.Abs(__instance.reverser));
            FuelModel.Compute(__instance.tractiveEffort, wheelVelocityMph, cutoff, grate, out float waterTarget, out float coalTarget);

            // Same smoothing and zeroing as the game's UpdateConsumption, applied to our own state.
            Rates st = State.GetOrCreateValue(__instance);
            st.Water = Mathf.Lerp(st.Water, waterTarget, Time.deltaTime);
            st.Coal = Mathf.Lerp(st.Coal, coalTarget, Time.deltaTime);
            if (st.Water < 0.001f && waterTarget < st.Water) st.Water = 0f;
            if (st.Coal < 0.001f && coalTarget < st.Coal) st.Coal = 0f;

            SetWater.Invoke(__instance, new object[] { st.Water });
            SetCoal.Invoke(__instance, new object[] { st.Coal });
        }
    }
}
