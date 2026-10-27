using System.Reflection;
using HarmonyLib;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Physics;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Reads the coal and water aboard a steam locomotive's tender the same way SteamLocomotive.PeriodicUpdate does:
    /// FuelCar() gives the tender, the slot numbers come from its definition's LoadSlots (by RequiredLoadIdentifier),
    /// and GetLoadInfo(slot) gives the quantity. The using lines above match that class so GetLoadInfo resolves
    /// exactly as it does there.
    /// </summary>
    internal static class FuelReader
    {
        internal struct Reading
        {
            public string FuelId;
            public float Fuel;
            public float Water;
            public int Tenders;

            internal static Reading Empty()
            {
                return new Reading { FuelId = "", Fuel = float.NaN, Water = float.NaN, Tenders = 0 };
            }
        }

        // FuelCar() is not necessarily public, so it is called by reflection.
        private static readonly MethodInfo MFuelCar = AccessTools.Method(typeof(SteamLocomotive), "FuelCar");

        // 0.6.0 prefixes load identifiers with their pack ("rr/coal"); 0.5.x used the bare name ("coal").
        private static bool IsLoad(string identifier, string name)
        {
            return identifier == name || (identifier != null && identifier.EndsWith("/" + name));
        }

        internal static Reading Read(BaseLocomotive loco)
        {
            Reading result = Reading.Empty();

            var steam = loco as SteamLocomotive;
            if (steam == null || MFuelCar == null) return result;

            var tender = MFuelCar.Invoke(steam, null) as Car;
            if (tender == null) return result;

            int coalSlot = tender.Definition.LoadSlots.FindIndex((LoadSlot loadSlot) => IsLoad(loadSlot.RequiredLoadIdentifier, "coal"));
            int waterSlot = tender.Definition.LoadSlots.FindIndex((LoadSlot loadSlot) => IsLoad(loadSlot.RequiredLoadIdentifier, "water"));

            result.Tenders = 1;
            result.FuelId = "coal";
            if (coalSlot >= 0) result.Fuel = tender.GetLoadInfo(coalSlot)?.Quantity ?? 0f;
            if (waterSlot >= 0) result.Water = tender.GetLoadInfo(waterSlot)?.Quantity ?? 0f;
            return result;
        }
    }
}
