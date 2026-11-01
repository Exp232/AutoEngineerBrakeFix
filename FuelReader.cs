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

        /// <summary>The steam locomotive's tender (its fuel car), or null.</summary>
        internal static Car Tender(BaseLocomotive loco)
        {
            var steam = loco as SteamLocomotive;
            if (steam == null || MFuelCar == null) return null;
            return MFuelCar.Invoke(steam, null) as Car;
        }

        /// <summary>Index of the tender's water slot, or -1.</summary>
        internal static int WaterSlot(Car tender)
        {
            if (tender == null) return -1;
            return tender.Definition.LoadSlots.FindIndex((LoadSlot loadSlot) => IsLoad(loadSlot.RequiredLoadIdentifier, "water"));
        }

        /// <summary>Water aboard the tender as a fraction of its capacity (0 to 1), or NaN when it cannot be read.</summary>
        internal static float WaterFraction(Car tender)
        {
            int slot = WaterSlot(tender);
            if (slot < 0) return float.NaN;
            float capacity = tender.Definition.LoadSlots[slot].MaximumCapacity;
            if (capacity <= 0f) return float.NaN;
            return (tender.GetLoadInfo(slot)?.Quantity ?? 0f) / capacity;
        }

        /// <summary>
        /// Diesel fuel use in gallons per second (DieselLocomotive.PeriodicUpdate takes PrimeMover.FuelConsumptionRate x dt
        /// out of the fuel slot), or NaN for any other locomotive.
        /// </summary>
        internal static float DieselRate(BaseLocomotive loco)
        {
            var diesel = loco as DieselLocomotive;
            if (diesel == null || diesel.primeMover == null) return float.NaN;
            return diesel.primeMover.FuelConsumptionRate;
        }

        /// <summary>Gallons of fuel in a diesel locomotive's own tank (its FuelLoadId slot), or NaN.</summary>
        private static float DieselFuel(DieselLocomotive diesel)
        {
            string loadId = (diesel.Definition as LocomotiveDefinition)?.FuelLoadId;
            if (string.IsNullOrEmpty(loadId)) return float.NaN;
            int slot = diesel.Definition.LoadSlots.FindIndex((LoadSlot loadSlot) => loadSlot.RequiredLoadIdentifier == loadId);
            if (slot < 0) return float.NaN;
            return diesel.GetLoadInfo(slot)?.Quantity ?? 0f;
        }

        internal static Reading Read(BaseLocomotive loco)
        {
            Reading result = Reading.Empty();

            var diesel = loco as DieselLocomotive;
            if (diesel != null)
            {
                result.FuelId = "diesel";
                result.Fuel = DieselFuel(diesel);
                return result;
            }

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
