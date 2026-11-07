using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Model;
using Model.AI;
using RollingStock.Controls;
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
    ///  - AutoMarkers: the marker lamp at the rear end of the train shows
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

        /// <summary>One door or gate of a car: its key, the car end it is at (f or r, or a space for the sides) and whether it closes off an end of the car.</summary>
        private sealed class Door
        {
            public string Key;
            public char End = ' ';
            public bool EndClosure;
            public string Label = "";
        }

        private sealed class Info
        {
            public readonly List<Door> Doors = new List<Door>();
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

            if (settings.DoorsAtStations) DoorsTick(ae, loco, cars, speed, orders.MaxSpeedMph > 0, s);

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
        private static void DoorsTick(AutoEngineer ae, Car loco, List<Car> cars, float speed, bool going, State s)
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
                    if (!IsServed(car) || InfoFor(car).Doors.Count == 0) continue;
                    float open = now + order * OpenSpacingSeconds + (float)random.NextDouble() * 0.6f;
                    float close = now + dwell * Mathf.Lerp(CloseEarliest, CloseLatest, (float)random.NextDouble());
                    s.Plans.Add(new Plan { Car = car, OpenAt = open, CloseAt = Mathf.Max(close, open + 3f) });
                    order++;
                }
                Main.Mod.Logger.Log($"{loco.DisplayName}: stopped at a station stop ({info.Value.text}), doors on {s.Plans.Count} car(s).");
            }
            else if (!atStation && s.Stopped)
            {
                // Departing: close what is still open, one car every 0.15 s.
                s.Stopped = false;
                float at = now;
                foreach (Plan plan in s.Plans)
                {
                    if (plan.Opened && !plan.Closed) { plan.CloseAt = at; at += 0.15f; }
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
            // Nothing stays open when the train is to move, in any mode and at any place; a station stop in progress is left to its plan.
            bool pending = s.Plans.Exists(p => p.Opened && !p.Closed);
            bool closeAll = speed > 0.3f || (going && !s.Stopped && !pending);
            changed += ForceClose(cars, closeAll);
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

        /// <summary>Cars whose doors the station stop works: passenger cars, baggage cars, cabooses and work cars.</summary>
        private static bool IsServed(Car car)
        {
            if (car.IsLocomotive) return false;
            string type = TypeOf(car);
            return type.StartsWith("P", StringComparison.Ordinal) || type.StartsWith("CB", StringComparison.Ordinal)
                || type.StartsWith("NE", StringComparison.Ordinal) || type.StartsWith("MW", StringComparison.Ordinal);
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

        /// <summary>The last element of the toggle's collider object path (for example "Rear Door"), lower case, or "".</summary>
        private static string ColliderName(object component)
        {
            try
            {
                Type t = component.GetType();
                object target = t.GetProperty("TargetColliderObject", Flags)?.GetValue(component, null) ?? t.GetField("TargetColliderObject", Flags)?.GetValue(component);
                if (target == null) return "";
                Type tt = target.GetType();
                object path = tt.GetProperty("Path", Flags)?.GetValue(target, null) ?? tt.GetField("Path", Flags)?.GetValue(target);
                string last = "";
                if (path is System.Collections.IEnumerable items)
                {
                    foreach (object item in items) last = item?.ToString() ?? last;
                }
                return last.Trim().ToLowerInvariant();
            }
            catch (Exception)
            {
                return "";
            }
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
                            string name = (Str(c, "Name") ?? "").Trim().ToLowerInvariant();
                            string collider = ColliderName(c);
                            // Many definitions leave the title empty, so the name and the key are read too (door.b, main.door, gate).
                            string label = title + " " + name + " " + collider + " " + (key ?? "").ToLowerInvariant();
                            bool entry = label.Contains("door") || label.Contains("gate");
                            bool skip = label.Contains("window");
                            if (!entry || skip || string.IsNullOrEmpty(key) || info.Doors.Exists(d => d.Key == key)) continue;
                            var door = new Door { Key = key, Label = label };
                            string lowKey = key.ToLowerInvariant();
                            if (name.StartsWith("front", StringComparison.Ordinal) || collider.StartsWith("front", StringComparison.Ordinal) || lowKey.EndsWith(".f", StringComparison.Ordinal)) door.End = 'f';
                            else if (name.StartsWith("rear", StringComparison.Ordinal) || collider.StartsWith("rear", StringComparison.Ordinal) || lowKey.EndsWith(".r", StringComparison.Ordinal)) door.End = 'r';
                            // Gates, and the vestibule door at an end ("Front Door", "Rear Door"), close off the car end.
                            door.EndClosure = door.End != ' ' && (label.Contains("gate") || name == "front door" || name == "rear door" || collider == "front door" || collider == "rear door");
                            info.Doors.Add(door);
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

        /// <summary>True when the neighbour on that end of the car (f or r) is a locomotive or tender, or there is none.</summary>
        private static bool EndBlocked(Car car, char end)
        {
            Car neighbour = car.CoupledTo(car.EndToLogical(end == 'f' ? Car.End.F : Car.End.R));
            return IsPower(neighbour);
        }

        private static readonly ConditionalWeakTable<Car, Dictionary<string, Transform>> Toggles = new ConditionalWeakTable<Car, Dictionary<string, Transform>>();

        /// <summary>The world object the game's click toggle for this key sits on (the door or gate itself), or null.</summary>
        private static Transform ToggleTransform(Car car, string key)
        {
            Dictionary<string, Transform> map = Toggles.GetOrCreateValue(car);
            if (map.Count == 0)
            {
                foreach (KeyValuePickableToggle toggle in car.GetComponentsInChildren<KeyValuePickableToggle>(true))
                {
                    if (toggle != null && !string.IsNullOrEmpty(toggle.key) && !map.ContainsKey(toggle.key)) map[toggle.key] = toggle.transform;
                }
            }
            return map.TryGetValue(key, out Transform t) && t != null ? t : null;
        }

        /// <summary>
        /// True when the end of the car this door or gate is on is coupled to a locomotive or tender (the vestibule door or gate immediately behind the loco). The end is found
        /// from where the door sits in the world relative to the coupled neighbours, not from its "front" or "rear" name, because
        /// a car can be placed either way round.
        /// </summary>
        private static bool Blocked(Car car, Door door)
        {
            Transform t = ToggleTransform(car, door.Key);
            Car a = car.CoupledTo(Car.LogicalEnd.A);
            Car b = car.CoupledTo(Car.LogicalEnd.B);
            if (t == null) return door.End != ' ' && EndBlocked(car, door.End);
            if (a == null && b == null) return false;
            Vector3 centre = car.transform.position;
            Vector3 g = t.position;
            bool atA;
            if (a != null && b != null) atA = (g - a.transform.position).sqrMagnitude <= (g - b.transform.position).sqrMagnitude;
            else if (a != null) atA = Vector3.Dot(g - centre, a.transform.position - centre) > 0f;
            else atA = Vector3.Dot(g - centre, b.transform.position - centre) <= 0f;
            Car neighbour = atA ? a : b;
            return IsPower(neighbour);
        }

        private static bool IsPower(Car car)
        {
            return car != null && (car.IsLocomotive || TypeOf(car).StartsWith("LT", StringComparison.Ordinal));
        }

        private static bool IsBaggage(Car car)
        {
            return TypeOf(car).StartsWith("CB", StringComparison.Ordinal);
        }

        private static bool IsOpen(Car car, string key)
        {
            try { return car.KeyValueObject[key].BoolValue; }
            catch (Exception) { return false; }
        }

        /// <summary>Opens (never at a blocked car end) or closes every door and gate of the car; returns how many were changed.</summary>
        private static int SetCarDoors(Car car, bool open)
        {
            int n = 0;
            foreach (Door door in InfoFor(car).Doors)
            {
                if (open && door.EndClosure && Blocked(car, door)) continue;
                if (IsOpen(car, door.Key) == open) continue;
                car.KeyValueObject[door.Key] = KeyValue.Runtime.Value.Bool(open);
                n++;
            }
            return n;
        }

        /// <summary>
        /// Closes what must not be open: the gate or vestibule door on a car end coupled to a locomotive or tender, at any time; and every door and gate of the passenger, baggage, caboose and work cars as soon as the engineer is to move (or is moving)
        /// unless a station stop is being served (those close as each car finishes).
        /// </summary>
        private static int ForceClose(List<Car> cars, bool closeAll)
        {
            int n = 0;
            foreach (Car car in cars)
            {
                if (!IsServed(car)) continue;
                foreach (Door door in InfoFor(car).Doors)
                {
                    bool must = (door.EndClosure && Blocked(car, door)) || closeAll;
                    if (!must || !IsOpen(car, door.Key)) continue;
                    car.KeyValueObject[door.Key] = KeyValue.Runtime.Value.Bool(false);
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
