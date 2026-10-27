using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Model.Physics;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Optional starting resistance (off by default).
    ///
    /// The game's rolling resistance is the Davis equation, which is only valid for a moving train: at 0 mph a
    /// car meets about 1.6 lb per ton, where a real plain-bearing car needs 5-10 lb per ton or more to break
    /// away (cold oil, journals settled). This adds a standing-start term that fades out over the first few
    /// mph: extra = tons x lb-per-ton x exp(-mph / fade), in the same units (newtons) as the game's own sum.
    /// With standing time on, the drag is also scaled by how long the car has stood: a car that has only just
    /// stopped still has oil in its journals and starts easier than one that has sat.
    /// </summary>
    [HarmonyPatch(typeof(IntegrationSet), "CalculateRetardingForce")]
    internal static class IntegrationSet_CalculateRetardingForce_Patch
    {
        private const float LbToNewtons = 4.44822f;
        private const float MpsToMph = 2.23694f;

        private static FieldInfo _carField;
        private static PropertyInfo _carProperty;
        private static bool _resolved;
        private static bool _warned;

        private const float MovingMph = 0.1f;

        // Game hour at which each car was last seen moving. A car that is not in the table has not moved since the
        // mod loaded (a save was just loaded, or the car is new), and counts as fully settled.
        private sealed class Rest { public float LastMovedHours; }
        private static readonly ConditionalWeakTable<Car, Rest> Rests = new ConditionalWeakTable<Car, Rest>();
        private static int _clockFrame = -1;
        private static float _clockHours;

        private static float GameHours()
        {
            int frame = Time.frameCount;
            if (frame != _clockFrame)
            {
                _clockFrame = frame;
                _clockHours = global::Game.TimeWeather.Now.TotalHours;
            }
            return _clockHours;
        }

        /// <summary>1 for a fully settled car; down to the floor for one that has only just stopped.</summary>
        private static float SettleFactor(Car car, float mph)
        {
            if (!Main.Settings.StartingResistanceStandingTime) return 1f;
            float now = GameHours();
            if (mph > MovingMph)
            {
                Rests.GetOrCreateValue(car).LastMovedHours = now;
                return 1f; // moving: the speed fade has taken over
            }
            if (!Rests.TryGetValue(car, out Rest rest)) return 1f;
            float floor = Mathf.Clamp01(Main.Settings.StartingResistanceFloorPercent / 100f);
            float tau = Mathf.Max(1f, Main.Settings.StartingResistanceSettleMinutes) / 60f; // hours
            float stood = Mathf.Max(0f, now - rest.LastMovedHours);
            return floor + (1f - floor) * (1f - Mathf.Exp(-stood / tau));
        }

        // Parameter names must match the original: CalculateRetardingForce(Element entry, float absVelocity).
        // Element is a private nested type, so it is taken as object.
        private static void Postfix(object __0, float absVelocity, ref float __result)
        {
            if (!Main.Active || !Main.Settings.StartingResistance) return;
            float lbPerTon = Mathf.Clamp(Main.Settings.StartingResistanceLbPerTon, 0f, 15f);
            if (lbPerTon <= 0f) return;
            try
            {
                Car car = GetCar(__0);
                if (car == null) return;
                float mph = absVelocity * MpsToMph;
                float fade = Mathf.Clamp(Main.Settings.StartingResistanceFadeMph, 0.25f, 6f);
                float settle = SettleFactor(car, mph); // also records when the car last moved
                if (mph > fade * 8f) return; // negligible
                float tons = car.Weight / 2000f;
                __result += tons * lbPerTon * settle * Mathf.Exp(-mph / fade) * LbToNewtons;
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    Main.Mod.Logger.Warning("Starting resistance failed (further errors suppressed): " + e.Message);
                }
            }
        }

        private static Car GetCar(object entry)
        {
            if (entry == null) return null;
            if (!_resolved)
            {
                _resolved = true;
                Type t = entry.GetType();
                _carField = AccessTools.Field(t, "car");
                if (_carField == null) _carProperty = AccessTools.Property(t, "car");
                if (_carField == null && _carProperty == null)
                    Main.Mod.Logger.Warning("Starting resistance: Element.car not found; option disabled.");
            }
            if (_carField != null) return _carField.GetValue(entry) as Car;
            if (_carProperty != null) return _carProperty.GetValue(entry, null) as Car;
            return null;
        }
    }
}
