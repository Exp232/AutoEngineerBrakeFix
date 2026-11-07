using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Model;
using Model.AI;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Doors, gates and lamps on the cars of the engineer's train, written the way the game's own click controls
    /// write them (the car's key-value entries; the key names come from each car's definition):
    ///  - DoorsAtStations: passenger-car doors and gates open while the engineer is stopped at a station stop
    ///    (AutoEngineerPassengerStopper.IsStoppedAtStation) and close when the train moves off.
    ///  - ObservationHeadlight: when a car with its own headlight (the observation car) leads the train, as when the
    ///    engineer backs the train, its headlight is bright while moving and dim while stopped, like the locomotive's;
    ///    it goes off when the car stops leading.
    ///  - AutoMarkers (off by default; marker lamps are the player's): the marker lamp at the rear end of the train shows
    ///    red (MarkerRedPosition) and every other marker lamp on the train is switched off.
    /// </summary>
    internal static class TrainLights
    {
        private sealed class State
        {
            public float Next;
            public bool DoorsOpen;
            public Car ObsLit;
            public string MarkerHead;
        }

        private sealed class Info
        {
            public readonly List<string> DoorKeys = new List<string>();
            public readonly List<string> MarkerLetters = new List<string>();
            public bool Headlight;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();
        private static readonly Dictionary<string, Info> Infos = new Dictionary<string, Info>();
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;

        internal static void Tick(AutoEngineer ae)
        {
            var settings = Main.Settings;
            if (!Main.Active || ae == null || ae.Locomotive == null) return;
            if (!settings.DoorsAtStations && !settings.ObservationHeadlight && !settings.AutoMarkers) return;

            State s = States.GetOrCreateValue(ae);
            if (Time.time < s.Next) return;
            s.Next = Time.time + 0.5f;

            Car loco = ae.Locomotive;
            Orders orders = new AutoEngineerPersistence(loco.KeyValueObject).Orders;
            if (!orders.Enabled) return;

            List<Car> cars = TrainPrep.Consist(loco);
            Car.LogicalEnd head = loco.EndToLogical(orders.Forward ? Car.End.F : Car.End.R);
            Car leading = Walk(loco, head, out Car.LogicalEnd leadEnd);
            Car trailing = Walk(loco, Opposite(head), out Car.LogicalEnd trailEnd);
            float speed = Mathf.Abs(loco.velocity);

            if (settings.DoorsAtStations)
            {
                var stopper = ae.GetComponent<AutoEngineerPassengerStopper>();
                bool atStation = stopper != null && stopper.IsStoppedAtStation && speed < 0.3f;
                if (atStation != s.DoorsOpen)
                {
                    s.DoorsOpen = atStation;
                    int n = SetDoors(cars, atStation);
                    if (n > 0) Main.Mod.Logger.Log($"{loco.DisplayName}: {n} door/gate(s) {(atStation ? "opened" : "closed")} at the station stop.");
                }
            }

            if (settings.ObservationHeadlight)
            {
                bool leads = leading != null && leading != loco && IsOwnHeadlightCar(leading);
                if (s.ObsLit != null && (!leads || s.ObsLit != leading))
                {
                    s.ObsLit.KeyValueObject["headlight"] = KeyValue.Runtime.Value.Int(0);
                    s.ObsLit = null;
                }
                if (leads)
                {
                    int level = speed > 0.3f ? 2 : 1;
                    leading.KeyValueObject["headlight"] = KeyValue.Runtime.Value.Int(level);
                    s.ObsLit = leading;
                }
            }

            if (settings.AutoMarkers)
            {
                string signature = (trailing != null ? trailing.id : "") + (trailEnd == Car.LogicalEnd.A ? "A" : "B") + cars.Count;
                if (signature != s.MarkerHead && speed < 1f)
                {
                    s.MarkerHead = signature;
                    SetMarkers(cars, trailing, trailEnd);
                }
            }
        }

        private static Car.LogicalEnd Opposite(Car.LogicalEnd end)
        {
            return end == Car.LogicalEnd.A ? Car.LogicalEnd.B : Car.LogicalEnd.A;
        }

        /// <summary>The car at the far end of the train from the locomotive in the given direction, and the logical end of it that points away from the train.</summary>
        private static Car Walk(Car loco, Car.LogicalEnd start, out Car.LogicalEnd lastEnd)
        {
            Car car = loco;
            Car.LogicalEnd end = start;
            for (int i = 0; i < 200; i++)
            {
                Car next = car.CoupledTo(end);
                if (next == null) break;
                Car.LogicalEnd facing;
                if (next.CoupledTo(Car.LogicalEnd.A) == car) facing = Car.LogicalEnd.A;
                else if (next.CoupledTo(Car.LogicalEnd.B) == car) facing = Car.LogicalEnd.B;
                else break;
                car = next;
                end = Opposite(facing);
            }
            lastEnd = end;
            return car;
        }

        private static string TypeOf(Car car)
        {
            return Convert.ToString(car.CarType) ?? "";
        }

        private static bool IsPassenger(Car car)
        {
            return !car.IsLocomotive && TypeOf(car).StartsWith("P", StringComparison.Ordinal);
        }

        private static bool IsOwnHeadlightCar(Car car)
        {
            return !car.IsLocomotive && !TypeOf(car).StartsWith("LT", StringComparison.Ordinal) && InfoFor(car).Headlight;
        }

        private static string Str(object o, string name)
        {
            if (o == null) return null;
            Type t = o.GetType();
            PropertyInfo p = t.GetProperty(name, Flags);
            if (p != null) return p.GetValue(o, null)?.ToString();
            FieldInfo f = t.GetField(name, Flags);
            return f?.GetValue(o)?.ToString();
        }

        private static Info InfoFor(Car car)
        {
            string id = car.DefinitionInfo.Identifier;
            if (Infos.TryGetValue(id, out Info info)) return info;
            info = new Info();
            try
            {
                var components = car.Definition.Components;
                if (components != null)
                {
                    foreach (var c in components.Values)
                    {
                        string kind = Str(c, "Kind");
                        if (kind == "ToggleAnimation")
                        {
                            string key = Str(c, "Key");
                            string title = (Str(c, "Title") ?? "").ToLowerInvariant();
                            bool entry = title.Contains("door") || title.Contains("gate");
                            bool skip = title.Contains("bathroom") || title.Contains("toilet") || title.Contains("lavatory") || title.Contains("window");
                            if (entry && !skip && !string.IsNullOrEmpty(key) && !info.DoorKeys.Contains(key)) info.DoorKeys.Add(key);
                        }
                        else if (kind == "Headlight")
                        {
                            info.Headlight = true;
                        }
                        else if (kind == "MarkerLight")
                        {
                            string endText = Str(c, "End") ?? "";
                            string letter = endText == "1" || endText.Equals("R", StringComparison.OrdinalIgnoreCase) ? "r" : "f";
                            if (!info.MarkerLetters.Contains(letter)) info.MarkerLetters.Add(letter);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Reading the car definition for lamps and doors failed: " + e.Message);
            }
            Infos[id] = info;
            return info;
        }

        private static int SetDoors(List<Car> cars, bool open)
        {
            int n = 0;
            foreach (Car car in cars)
            {
                if (!IsPassenger(car)) continue;
                foreach (string key in InfoFor(car).DoorKeys)
                {
                    car.KeyValueObject[key] = KeyValue.Runtime.Value.Bool(open);
                    n++;
                }
            }
            return n;
        }

        private static void SetMarkers(List<Car> cars, Car trailing, Car.LogicalEnd trailEnd)
        {
            foreach (Car car in cars)
            {
                foreach (string letter in InfoFor(car).MarkerLetters)
                {
                    bool isRear = car == trailing && (car.EndToLogical(letter == "f" ? Car.End.F : Car.End.R) == trailEnd);
                    car.KeyValueObject["marker-" + letter + ".lit"] = KeyValue.Runtime.Value.Bool(isRear);
                    if (isRear) car.KeyValueObject["marker-" + letter + ".position"] = KeyValue.Runtime.Value.Int(Main.Settings.MarkerRedPosition);
                }
            }
        }
    }
}
