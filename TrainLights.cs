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
            public bool Stopped;
            public float Arrived;
            public readonly List<Plan> Plans = new List<Plan>();
            public Car ObsLit;
            public string MarkerHead;
        }

        /// <summary>One car's doors at a stop: when they open, and when they close as the car finishes loading.</summary>
        private sealed class Plan
        {
            public Car Car;
            public float OpenAt, CloseAt;
            public bool Opened, Closed;
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

            if (settings.DoorsAtStations) DoorsTick(ae, loco, cars, speed, s);

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

        private const float OpenSpacingSeconds = 0.8f;   // car after car along the train
        private const float CloseEarliest = 0.55f;        // share of the minimum stop time at which the first car finishes
        private const float CloseLatest = 0.95f;          // and the last

        /// <summary>
        /// Doors open car by car when the train stops at a station, and each car closes its doors at its own time late in
        /// the stop (as if its loading were done). When the engineer decides to depart, any car still open closes in turn.
        /// </summary>
        private static void DoorsTick(AutoEngineer ae, Car loco, List<Car> cars, float speed, State s)
        {
            var stopper = ae.GetComponent<AutoEngineerPassengerStopper>();
            // The stopper registers the station stop while the train is within 10 m of it (its own arrival radius); the train
            // usually halts a metre or so short, so IsStoppedAtStation (under 0.1 m) is too strict.
            var info = stopper != null ? stopper.NextStopInfo : null;
            bool atStation = info.HasValue && info.Value.distance < 10f && speed < 0.3f;
            float now = Time.time;

            if (atStation && !s.Stopped)
            {
                s.Stopped = true;
                s.Arrived = now;
                s.Plans.Clear();
                float dwell = MinimumStopSeconds();
                var random = new System.Random(unchecked((int)(now * 1000f)));
                int order = 0;
                foreach (Car car in cars)
                {
                    if (!IsPassenger(car) || InfoFor(car).DoorKeys.Count == 0) continue;
                    float open = now + order * OpenSpacingSeconds + (float)random.NextDouble() * 0.6f;
                    float close = now + dwell * Mathf.Lerp(CloseEarliest, CloseLatest, (float)random.NextDouble());
                    s.Plans.Add(new Plan { Car = car, OpenAt = open, CloseAt = Mathf.Max(close, open + 3f) });
                    order++;
                }
                Main.Mod.Logger.Log($"{loco.DisplayName}: stopped at a station stop ({info.Value.text}), doors on {s.Plans.Count} car(s).");
            }
            else if (!atStation && s.Stopped)
            {
                // Departing: close what is still open, one car every half second.
                s.Stopped = false;
                float at = now;
                foreach (Plan plan in s.Plans)
                {
                    if (plan.Opened && !plan.Closed) { plan.CloseAt = at; at += 0.5f; }
                    else if (!plan.Opened) plan.Closed = true;
                }
            }

            int changed = 0;
            for (int i = 0; i < s.Plans.Count; i++)
            {
                Plan plan = s.Plans[i];
                if (!plan.Opened && !plan.Closed && s.Stopped && now >= plan.OpenAt)
                {
                    plan.Opened = true;
                    changed += SetCarDoors(plan.Car, true);
                }
                if (plan.Opened && !plan.Closed && now >= plan.CloseAt)
                {
                    plan.Closed = true;
                    changed += SetCarDoors(plan.Car, false);
                }
            }
            if (!s.Stopped && s.Plans.Count > 0 && s.Plans.TrueForAll(p => p.Closed)) s.Plans.Clear();
            if (changed > 0 && !_loggedDoors)
            {
                _loggedDoors = true;
                Main.Mod.Logger.Log($"{loco.DisplayName}: doors and gates are opened car by car at a station stop and closed as each car finishes (further ones are not logged).");
            }
        }

        private static bool _loggedDoors;

        private static float MinimumStopSeconds()
        {
            try
            {
                float v = Game.State.StateManager.Shared.Storage.AIPassengerStopMinimumStopDuration;
                if (v >= 5f) return v;
            }
            catch (Exception) { /* use the default below */ }
            return 30f;
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

        private static int SetCarDoors(Car car, bool open)
        {
            int n = 0;
            foreach (string key in InfoFor(car).DoorKeys)
            {
                car.KeyValueObject[key] = KeyValue.Runtime.Value.Bool(open);
                n++;
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
