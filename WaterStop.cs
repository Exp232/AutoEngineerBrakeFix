using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.AI;
using Model.Ops;
using RollingStock;
using RollingStock.Controls;
using Track;
using Track.Signals;
using UI.EngineControls;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Water stop for a Road-mode engineer (off by default, setting WaterStop).
    ///
    /// When the tender's water falls below the threshold, the engineer looks along the route as it is lined (Road mode
    /// does not throw switches) for the nearest water column or tower ahead, says where it will take water, and
    /// slows to the approach speed when it gets close. Distances are measured from the tender's water hatch, not the lead
    /// end, so a train being shoved (cars ahead of the engine) is judged by where the tender is. Only once a car of the
    /// train is in the column's signal block, or the lead end has already passed the column (or, where there is no CTC
    /// block, within the approach distance) does it switch to Waypoint mode with a waypoint
    /// that puts the tender's water hatch under the spout, so the player's routing is kept up to that block.
    /// Stopped there, it asks the column's own loader sequence to load (the same key value the game's loader
    /// sequencer reads), waits until the tender is full, puts the spout away and goes back to Road mode with the
    /// direction and speed it had.
    ///
    /// The spotting arithmetic and the use of CarLoaderSequencer's "wants loading" key follow the refuel feature of
    /// the Waypoint Queue mod by PrinceOfPluto (MIT licence, https://github.com/PrinceOfPluto/WaypointQueue).
    ///
    /// Host only. One component per engineer; it does nothing unless the setting is on, the engine is a steam
    /// locomotive with a tender, and the engineer is in Road mode (or in the Waypoint mode this component set).
    /// The player changing the orders at any point cancels the stop.
    /// </summary>
    public class WaterStop : MonoBehaviour
    {
        private enum Phase { Idle, Armed, Approaching, Filling }

        private const float TickSeconds = 1f;
        private const float SearchEverySeconds = 5f;
        private const float SlowDownWithinMeters = 1600f;
        private const float MinAheadMeters = 30f;         // a column closer than this to the hatch is not picked
        private const float MinAheadArmedMeters = 5f;     // once picked, the stop goes on until the hatch is about at the spout
        private const float StopPastSpoutMeters = 0.5f;   // Waypoint Queue's allowance for where the engineer stops
        private const float ApproachTimeoutSeconds = 1200f;
        private const float NoFlowTimeoutSeconds = 90f;
        private const float FullFraction = 0.995f;
        private const float ColumnCooldownSeconds = 600f;
        private const float SpotToleranceMeters = 0.35f;  // hatch reach is 0.6 m (column 0.2 + F-71 hatch 0.4); along the track
        private const int MaxSpotCorrections = 2;
        private const int CorrectionMph = 5;

        private sealed class Column
        {
            public CarLoadTargetLoader Loader;
            public CarLoaderSequencer Sequencer;
            public string Id;
            public string Place;
            public Location Location;
            public CTCBlock Block;
        }

        private static List<Column> _columns;
        private static bool _loggedColumns;

        private AutoEngineer _ae;
        private BaseLocomotive _loco;
        private Phase _phase = Phase.Idle;
        private float _nextTick;
        private float _nextSearch;
        private float _phaseStart;
        private float _graceUntil;      // our own order changes take a moment to show in the orders
        private bool _syncSpeed;

        private Column _column;
        private bool _origForward;
        private int _origMaxSpeed;
        private int _setMaxSpeed;
        private string _waypointString;
        private bool _requested;
        private string _hatchObjectId;   // the tender's key value object
        private string _hatchKey;
        private bool _hatchWasOpen;
        private float _lastWater;
        private float _lastFlow;
        private bool _saidNoColumn;
        private int _corrections;
        private float _correctedAt = -1f;   // a correction was ordered; wait for the train to move before measuring again
        private float _offsetTime = -1f;
        private float _offsetValue;
        private float _lastDistance = float.NaN;   // hatch to column at the last Armed check
        private float _lastDistanceTime;
        private readonly Dictionary<string, float> _cooldown = new Dictionary<string, float>();

        private void Awake()
        {
            _ae = GetComponent<AutoEngineer>();
            _loco = GetComponent<BaseLocomotive>();
        }

        private void OnDisable()
        {
            if (_phase == Phase.Filling) Request(false);
            _phase = Phase.Idle;
        }

        private void Update()
        {
            if (!Main.Active) return;
            float now = Time.time;
            if (now < _nextTick) return;
            _nextTick = now + TickSeconds;

            try
            {
                if (!Main.Settings.WaterStop || !StateManager.IsHost || _loco == null || _loco.AutoEngineerPlanner == null)
                {
                    if (_phase != Phase.Idle) Cancel("water stop switched off");
                    return;
                }
                Tick(now);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Water stop (" + Name + ") failed: " + e.Message);
                if (_phase == Phase.Filling) Request(false);
                _phase = Phase.Idle;
            }
        }

        private string Name => _loco != null ? _loco.DisplayName : "?";

        private AutoEngineerOrdersHelper Helper => new AutoEngineerOrdersHelper(_loco, new AutoEngineerPersistence(_loco.KeyValueObject));

        private Orders CurrentOrders => new AutoEngineerPersistence(_loco.KeyValueObject).Orders;

        private void Tick(float now)
        {
            Orders orders = CurrentOrders;
            Car tender = FuelReader.Tender(_loco);
            float water = FuelReader.WaterFraction(tender);

            switch (_phase)
            {
                case Phase.Idle:
                {
                    if (orders.Mode != AutoEngineerMode.Road || orders.MaxSpeedMph <= 0 || float.IsNaN(water)) return;
                    float threshold = Mathf.Clamp(Main.Settings.WaterStopPercent, 5f, 90f) / 100f;
                    if (water >= threshold) { _saidNoColumn = false; return; }
                    if (now < _nextSearch) return;
                    _nextSearch = now + SearchEverySeconds;

                    if (!TryFindColumnAhead(orders.Forward, out Column column, out float distance))
                    {
                        if (!_saidNoColumn)
                        {
                            _saidNoColumn = true;
                            Say($"Water's at {water * 100f:0}% and I don't see a water column ahead on this route.");
                            Main.Mod.Logger.Log($"Water stop ({Name}): water {water * 100f:0}%, no column ahead within {Main.Settings.WaterStopSearchKm:0} km.");
                        }
                        return;
                    }
                    _column = column;
                    _origForward = orders.Forward;
                    _origMaxSpeed = orders.MaxSpeedMph;
                    _setMaxSpeed = orders.MaxSpeedMph;
                    _syncSpeed = false;
                    _corrections = 0;
                    _correctedAt = -1f;
                    _lastDistance = distance;
                    _lastDistanceTime = now;
                    _phase = Phase.Armed;
                    _phaseStart = now;
                    Say($"Water's at {water * 100f:0}%. We'll take water at {column.Place}.");
                    Main.Mod.Logger.Log($"Water stop ({Name}): water {water * 100f:0}%, column {column.Id} at {column.Place}, {distance:0} m ahead of the hatch (block {column.Block?.id ?? "none"}).");
                    return;
                }

                case Phase.Armed:
                {
                    if (now < _graceUntil) return;
                    if (_syncSpeed)
                    {
                        _setMaxSpeed = orders.MaxSpeedMph;   // as the game clamped it
                        _syncSpeed = false;
                    }
                    if (orders.Mode != AutoEngineerMode.Road || orders.Forward != _origForward || orders.MaxSpeedMph != _setMaxSpeed)
                    {
                        Cancel("orders changed by the player");
                        return;
                    }
                    if (now < _nextSearch) return;
                    _nextSearch = now + SearchEverySeconds / 2f;

                    if (!TryDistanceTo(_column, orders.Forward, MinAheadArmedMeters, out float distance, out Location hatchAt, out float headToHatch))
                    {
                        // Passed it, or the route was lined away from it: give back the speed we took off.
                        if (_setMaxSpeed != _origMaxSpeed) Helper.SetOrdersValue(null, null, _origMaxSpeed, null, null);
                        Cancel("the column is no longer ahead on the lined route");
                        return;
                    }
                    _lastDistance = distance;
                    _lastDistanceTime = now;

                    int approach = Mathf.Clamp(Mathf.RoundToInt(Main.Settings.WaterStopApproachMph), 5, 45);
                    if (distance < SlowDownWithinMeters && _setMaxSpeed > approach)
                    {
                        _setMaxSpeed = approach;
                        _syncSpeed = true;
                        _graceUntil = now + 3f;
                        Helper.SetOrdersValue(null, null, approach, null, null);
                        Main.Mod.Logger.Log($"Water stop ({Name}): hatch {distance:0} m from {_column.Id}, slowing to {approach} mph.");
                        return;
                    }

                    // Shoving, the lead end can reach (and pass) a column near the block boundary before it is
                    // seen in the block, so any car of the train in the block counts, and so does the lead end being
                    // past the column (the hatch distance is then shorter than the train ahead of it).
                    bool headPast = distance <= headToHatch;
                    bool inBlock;
                    if (_column.Block != null)
                    {
                        inBlock = headPast || _column.Block.CarsInBlock().Overlaps(_loco.EnumerateCoupled());
                    }
                    else
                    {
                        inBlock = headPast || distance <= Mathf.Max(100f, Main.Settings.WaterStopApproachMeters);
                    }
                    if (!inBlock) return;

                    if (!TryWaypointFor(_column, hatchAt, headToHatch, out Location target))
                    {
                        Cancel("could not work out where to stop");
                        return;
                    }
                    _waypointString = Graph.LocationToString(target.Clamped());
                    Helper.SetOrdersValue(AutoEngineerMode.Waypoint, _origForward, Mathf.Min(_setMaxSpeed, approach), null, new OrderWaypoint(_waypointString, null));
                    _phase = Phase.Approaching;
                    _phaseStart = now;
                    _graceUntil = now + 3f;
                    Say($"Pulling up to the water column at {_column.Place}.");
                    Main.Mod.Logger.Log($"Water stop ({Name}): {(headPast ? "lead end past the column" : _column.Block != null ? "in block " + _column.Block.id : "in approach distance")}, hatch {distance:0} m from {_column.Id}; Waypoint mode to {_waypointString} (lead end to hatch {headToHatch:0.0} m).");
                    return;
                }

                case Phase.Approaching:
                {
                    if (now < _graceUntil) return;
                    if (orders.Mode != AutoEngineerMode.Waypoint || (orders.Waypoint.HasValue && orders.Waypoint.Value.LocationString != _waypointString))
                    {
                        Cancel("orders changed by the player");
                        return;
                    }
                    if (now - _phaseStart > ApproachTimeoutSeconds)
                    {
                        GiveUp("couldn't get to the column");
                        return;
                    }
                    if (_correctedAt >= 0f)
                    {
                        if (!_loco.IsStopped(1f) || now - _correctedAt > 30f) _correctedAt = -1f;
                        return;
                    }
                    if (!_loco.IsStopped(3f)) return;
                    // Stopped: either at the waypoint (the game may clear it on arrival) or held by something else.
                    if (orders.Waypoint.HasValue && !NearWaypoint(orders.Forward)) return;
                    if (TrySpotError(out float miss, out float lateral))
                    {
                        Main.Mod.Logger.Log($"Water stop ({Name}): hatch {Mathf.Abs(miss):0.00} m {(miss >= 0f ? "short of" : "past")} the spout along the track ({lateral:0.00} m to the side).");
                        if (Mathf.Abs(miss) > SpotToleranceMeters && Mathf.Abs(miss) < 50f && _corrections < MaxSpotCorrections)
                        {
                            _corrections++;
                            // Short: pull ahead. Past: back up, with the other end leading, so that end arrives at the waypoint.
                            bool fwd = miss > 0f ? _origForward : !_origForward;
                            Location lead = HeadLocation(fwd, out _);
                            Location target = Graph.Shared.LocationByMoving(lead, Mathf.Abs(miss), false, true);
                            if (lead.IsValid && target.IsValid)
                            {
                                _waypointString = Graph.LocationToString(target.Clamped());
                                Helper.SetOrdersValue(AutoEngineerMode.Waypoint, fwd, Mathf.Min(_setMaxSpeed, CorrectionMph), null, new OrderWaypoint(_waypointString, null));
                                _graceUntil = now + 3f;
                                _correctedAt = now;
                                Say(miss > 0f ? "Pulling up to the spout." : "Backing up to the spout.");
                                Main.Mod.Logger.Log($"Water stop ({Name}): correction {_corrections}, {(miss > 0f ? "ahead" : "back")} {Mathf.Abs(miss):0.00} m to {_waypointString}.");
                                return;
                            }
                        }
                    }
                    _phase = Phase.Filling;
                    _phaseStart = now;
                    _lastWater = water;
                    _lastFlow = now;
                    Request(true);
                    Say("Taking water.");
                    Main.Mod.Logger.Log($"Water stop ({Name}): stopped at {_column.Id}, water {water * 100f:0.0}%, loader requested.");
                    return;
                }

                case Phase.Filling:
                {
                    if (orders.Mode != AutoEngineerMode.Waypoint)
                    {
                        Request(false);
                        Cancel("orders changed by the player");
                        return;
                    }
                    if (water >= FullFraction)
                    {
                        Request(false);
                        Main.Mod.Logger.Log($"Water stop ({Name}): full ({water * 100f:0.0}%) after {now - _phaseStart:0} s at {_column.Id}.");
                        Finish("Tender's full. Back to road orders.");
                        return;
                    }
                    if (water > _lastWater + 0.0005f)
                    {
                        _lastWater = water;
                        _lastFlow = now;
                        return;
                    }
                    if (now - _lastFlow > NoFlowTimeoutSeconds)
                    {
                        Request(false);
                        if (water >= 0.95f)   // the loader stops at its own idea of full, which can sit a little under the slot size
                        {
                            Main.Mod.Logger.Log($"Water stop ({Name}): flow stopped at {water * 100f:0.0}%, taken as full.");
                            Finish("Tender's full. Back to road orders.");
                        }
                        else
                        {
                            GiveUp($"no water is coming in (tender at {water * 100f:0}%)");
                        }
                    }
                    return;
                }
            }
        }

        // ---- Status (for DetailedStatus) ----

        internal enum StatusKind { Planned, Slowing, Spotting, Filling }

        internal struct StatusInfo
        {
            public StatusKind Kind;
            public string Label;
            public float? DistanceM;
            public string Detail;
        }

        /// <summary>What this locomotive's water stop is doing, for the detailed status. False when no stop is under way.</summary>
        internal static bool TryStatus(BaseLocomotive loco, out StatusInfo info)
        {
            info = default;
            WaterStop ws = loco != null ? loco.GetComponent<WaterStop>() : null;
            if (ws == null || !Main.Settings.WaterStop || ws._column == null || ws._phase == Phase.Idle) return false;
            string place = ws._column.Place;
            switch (ws._phase)
            {
                case Phase.Armed:
                {
                    float? d = null;
                    if (!float.IsNaN(ws._lastDistance))
                    {
                        // Counted down between the 2.5 s checks at the current speed.
                        float travelled = Mathf.Abs(loco.velocity) * Mathf.Max(0f, Time.time - ws._lastDistanceTime);
                        d = Mathf.Max(0f, ws._lastDistance - travelled);
                    }
                    bool slowing = ws._setMaxSpeed < ws._origMaxSpeed;
                    info.Kind = slowing ? StatusKind.Slowing : StatusKind.Planned;
                    info.Label = $"Water stop approach (mod), {place}";
                    info.DistanceM = d;
                    info.Detail = $"Water stop planned at {place}{(d.HasValue ? ", hatch " + DetailedStatus.Distance(d.Value) + " from the column" : "")}"
                        + (slowing ? $"; speed held to {ws._setMaxSpeed} mph (orders were {ws._origMaxSpeed} mph)." : ".");
                    return true;
                }
                case Phase.Approaching:
                    info.Kind = StatusKind.Spotting;
                    info.Label = $"Water stop (mod): spotting the tender at {place}";
                    info.Detail = $"Waypoint mode to put the tender hatch under the spout at {place}{(ws._corrections > 0 ? $" (correction {ws._corrections})" : "")}; Road orders resume after.";
                    return true;
                case Phase.Filling:
                {
                    float water = FuelReader.WaterFraction(FuelReader.Tender(loco));
                    info.Kind = StatusKind.Filling;
                    info.Label = float.IsNaN(water) ? $"Taking water at {place} (mod)" : $"Taking water at {place} (mod): {water * 100f:0}%";
                    info.Detail = $"Filling to {FullFraction * 100f:0}%, then back to Road orders at {ws._origMaxSpeed} mph.";
                    return true;
                }
            }
            return false;
        }

        // ---- Phases ----

        private void Cancel(string why)
        {
            Main.Mod.Logger.Log($"Water stop ({Name}): cancelled, {why}.");
            if (_column != null) _cooldown[_column.Id] = Time.time + ColumnCooldownSeconds;
            _phase = Phase.Idle;
            _column = null;
            _requested = false;
        }

        private void GiveUp(string why)
        {
            Main.Mod.Logger.Log($"Water stop ({Name}): giving up at {_column?.Id}, {why}.");
            Finish($"Couldn't take water here: {why}. Back to road orders.");
        }

        private void Finish(string say)
        {
            if (_column != null) _cooldown[_column.Id] = Time.time + ColumnCooldownSeconds;
            Helper.SetOrdersValue(AutoEngineerMode.Road, _origForward, _origMaxSpeed, null, null);
            Say(say);
            _phase = Phase.Idle;
            _column = null;
            _requested = false;
        }

        private void Request(bool loading)
        {
            if (_column?.Sequencer == null || _column.Sequencer.keyValueObject == null) return;
            if (!loading && !_requested) return;
            string id = _column.Sequencer.keyValueObject.RegisteredId;
            if (string.IsNullOrEmpty(id)) return;
            StateManager.ApplyLocal(new PropertyChange(id, _column.Sequencer.readWantsLoadingKey, new BoolPropertyValue(loading)));
            _requested = loading;
            try { SetHatch(loading); }
            catch (Exception e) { Main.Mod.Logger.Warning($"Water stop ({Name}): tender hatch: {e.Message}"); }
        }

        /// <summary>
        /// Opens the tender's water hatch with the column (and puts it back as it was afterwards) by sending the same
        /// message as clicking it. The hatch is a KeyValuePickableToggle built from the tender definition's
        /// ToggleAnimation; its key differs between tenders ("water.hatch", "hatch.water", "hatch"), so the one used is a
        /// toggle with "hatch" in its key, preferring "water", then the one nearest the water LoadTarget. Cosmetic: the
        /// column's loader does not check it.
        /// </summary>
        private void SetHatch(bool open)
        {
            if (open)
            {
                _hatchObjectId = null;
                _hatchKey = null;
                Car tender = FuelReader.Tender(_loco);
                if (tender == null) return;
                int slot = FuelReader.WaterSlot(tender);
                CarLoadTarget target = tender.GetComponentsInChildren<CarLoadTarget>().FirstOrDefault(t => t.slotIndex == slot);
                KeyValuePickableToggle hatch = tender.GetComponentsInChildren<KeyValuePickableToggle>(true)
                    .Where(t => t != null && !string.IsNullOrEmpty(t.key) && t.key.IndexOf("hatch", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(t => t.key.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ThenBy(t => target != null ? Vector3.Distance(t.transform.position, target.transform.position) : 0f)
                    .FirstOrDefault();
                KeyValueObject kvo = hatch != null ? hatch.GetComponentInParent<KeyValueObject>() : null;
                if (kvo == null || string.IsNullOrEmpty(kvo.RegisteredId)) return;
                _hatchObjectId = kvo.RegisteredId;
                _hatchKey = hatch.key;
                _hatchWasOpen = kvo[hatch.key].BoolValue;
                if (!_hatchWasOpen) StateManager.ApplyLocal(new PropertyChange(_hatchObjectId, _hatchKey, new BoolPropertyValue(true)));
                Main.Mod.Logger.Log($"Water stop ({Name}): tender hatch '{_hatchKey}' {(_hatchWasOpen ? "already open" : "opened")}.");
            }
            else if (_hatchObjectId != null)
            {
                if (!_hatchWasOpen) StateManager.ApplyLocal(new PropertyChange(_hatchObjectId, _hatchKey, new BoolPropertyValue(false)));
                _hatchObjectId = null;
                _hatchKey = null;
            }
        }

        private void Say(string text)
        {
            try { _loco.AutoEngineerPlanner?.Say(text); }
            catch (Exception) { }
        }

        private bool NearWaypoint(bool forward)
        {
            Graph graph = Graph.Shared;
            if (graph == null || string.IsNullOrEmpty(_waypointString)) return true;
            Location target = graph.ResolveLocationString(_waypointString);
            Location head = HeadLocation(forward, out _);
            if (!head.IsValid || !target.IsValid) return true;
            return Vector3.Distance(graph.GetPosition(head), graph.GetPosition(target)) < 25f;
        }

        // ---- Track geometry ----

        /// <summary>The leading end of the consist in the direction of travel, facing ahead (as the planner's StartLocation).</summary>
        private Location HeadLocation(bool forward, out Car headCar)
        {
            Car.End end = forward ? Car.End.F : Car.End.R;
            List<Car> cars = _loco.EnumerateCoupled(end).ToList();
            headCar = cars.Count > 0 ? cars[0] : _loco;
            Car.LogicalEnd logical = _loco.EndToLogical(end);
            Location loc = headCar.LocationFor(logical);
            return logical == Car.LogicalEnd.A ? loc : loc.Flipped();
        }

        private bool TryFindColumnAhead(bool forward, out Column best, out float bestDistance)
        {
            best = null;
            bestDistance = float.MaxValue;
            List<Column> columns = Columns();
            if (columns.Count == 0) return false;

            float now = Time.time;
            foreach (Column c in columns)
            {
                if (_cooldown.TryGetValue(c.Id, out float until) && now < until) continue;
                if (!TryDistanceTo(c, forward, MinAheadMeters, out float d, out _, out _)) continue;
                if (d < bestDistance)
                {
                    best = c;
                    bestDistance = d;
                }
            }
            return best != null;
        }

        /// <summary>
        /// Distance from the tender's water hatch to the column along the route as it is lined now, if the column is at
        /// least minAhead ahead of the hatch. The walk starts at the hatch's point on the track (the lead end moved back
        /// by the lead-end-to-hatch distance), so the cars ahead of a shoving engine are counted. Falls back to the lead
        /// end when the hatch cannot be found (headToHatch is then 0). from is where the walk started.
        /// </summary>
        private bool TryDistanceTo(Column column, bool forward, float minAhead, out float distance, out Location from, out float headToHatch)
        {
            distance = float.MaxValue;
            Location head = HeadLocation(forward, out _);
            from = head;
            headToHatch = 0f;
            Graph graph = Graph.Shared;
            if (graph == null || !head.IsValid || column == null || !column.Location.IsValid) return false;

            float offset = HeadToHatch(head);
            if (offset > 0.5f)
            {
                Location hatch = graph.LocationByMoving(head, -offset, false, true);
                if (hatch.IsValid)
                {
                    from = hatch;
                    headToHatch = offset;
                }
            }

            float maxMeters = Mathf.Clamp(Main.Settings.WaterStopSearchKm, 1f, 100f) * 1000f + headToHatch;
            RouteWalker walker = graph.WalkRoute(from, maxMeters);
            while (walker.TryNext(out WalkSegment ws))
            {
                if (ws.Segment != column.Location.segment) continue;
                float fromEntry = graph.GetDistanceBetweenClose(ws.EntryLocation, column.Location);
                float toExit = graph.GetDistanceBetweenClose(column.Location, ws.ExitLocation);
                if (Mathf.Abs(fromEntry + toExit - ws.TraversedLength) > 1f) continue;   // on this segment but not in the walked part
                float d = ws.EntryDistance + fromEntry;
                if (d < minAhead) continue;
                distance = d;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The waypoint that stops the lead end so that the tender's water hatch is under the spout: the spout's track
        /// point moved ahead by the lead-end-to-hatch distance (plus Waypoint Queue's 0.5 m allowance). The spout is
        /// oriented toward the hatch, which is always short of it, so "ahead" stays the direction of travel even when
        /// the lead end of a shoved train has already passed the column.
        /// </summary>
        private bool TryWaypointFor(Column column, Location hatchAt, float headToHatch, out Location target)
        {
            target = Location.Invalid;
            Graph graph = Graph.Shared;
            if (graph == null || headToHatch <= 0.5f) return false;

            Location toward = graph.LocationOrientedToward(column.Location, hatchAt);
            target = graph.LocationByMoving(toward, -(headToHatch + StopPastSpoutMeters), false, true);
            return target.IsValid;
        }

        /// <summary>
        /// Distance along the track from the lead end back to the tender's water hatch (its LoadTarget), or 0 when
        /// there is no hatch to find. Measured along the track, not in a straight line: with a long train ahead of the
        /// tender on a curve the straight line is shorter, and the tender stopped short of the spout (2.5.3, Whittier).
        /// Cached for the rest of the tick, since the column search asks once per column.
        /// </summary>
        private float HeadToHatch(Location head)
        {
            if (Time.time == _offsetTime) return _offsetValue;
            _offsetTime = Time.time;
            _offsetValue = 0f;
            Graph graph = Graph.Shared;
            if (graph == null || !head.IsValid || !TryHatchGame(out Vector3 hatchGame)) return 0f;
            _offsetValue = AlongBehind(graph, head, hatchGame, out _);
            return _offsetValue;
        }

        /// <summary>The tender's water hatch in game coordinates, as CarLoadTargetLoader.LoadSlotFromCar works it out.</summary>
        private bool TryHatchGame(out Vector3 hatchGame)
        {
            hatchGame = Vector3.zero;
            Graph graph = Graph.Shared;
            Car tender = FuelReader.Tender(_loco);
            int slot = FuelReader.WaterSlot(tender);
            if (graph == null || tender == null || slot < 0) return false;
            CarLoadTarget hatch = tender.GetComponentsInChildren<CarLoadTarget>().FirstOrDefault(t => t.slotIndex == slot);
            if (hatch == null) return false;
            Matrix4x4 m = tender.GetTransformMatrix(graph);
            hatchGame = m.MultiplyPoint3x4(tender.transform.InverseTransformPoint(hatch.transform.position));
            return true;
        }

        /// <summary>
        /// How far back along the track from 'from' (facing ahead) the point nearest to 'game' is (horizontally), and
        /// how far to the side of the track 'game' then is. Steps of 1 m, then 5 cm around the best one. The search
        /// starts at the straight-line distance, which an arc is never shorter than.
        /// </summary>
        private static float AlongBehind(Graph graph, Location from, Vector3 game, out float offset)
        {
            float chord = Horizontal(graph.GetPosition(from), game);
            float lo = Mathf.Max(0f, chord - 2f);
            float hi = chord * 1.15f + 20f;
            float best = lo;
            float bestD = float.MaxValue;
            for (float d = lo; d <= hi; d += 1f) Probe(d);
            float a = Mathf.Max(0f, best - 1f), b = best + 1f;
            for (float d = a; d <= b; d += 0.05f) Probe(d);
            offset = bestD;
            return best;

            void Probe(float d)
            {
                Location p = graph.LocationByMoving(from, -d, false, true);
                if (!p.IsValid) return;
                float h = Horizontal(graph.GetPosition(p), game);
                if (h < bestD)
                {
                    bestD = h;
                    best = d;
                }
            }
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        /// <summary>
        /// Stopped at the column: how far the hatch is from the spout along the track (positive = short of it, so the
        /// train must pull ahead), and how far to the side. Both are measured back from the lead end, so they share
        /// one reference.
        /// </summary>
        private bool TrySpotError(out float miss, out float lateral)
        {
            miss = 0f;
            lateral = 0f;
            Graph graph = Graph.Shared;
            if (graph == null || _column == null || !TryHatchGame(out Vector3 hatchGame)) return false;
            Location lead = HeadLocation(_origForward, out _);
            if (!lead.IsValid) return false;
            float toHatch = AlongBehind(graph, lead, hatchGame, out lateral);
            float toSpout = AlongBehind(graph, lead, graph.GetPosition(_column.Location), out _);
            miss = toHatch - toSpout;
            return true;
        }

        // ---- Columns ----

        private static List<Column> Columns()
        {
            if (_columns != null && _columns.All(c => c.Loader != null)) return _columns;

            var list = new List<Column>();
            Graph graph = Graph.Shared;
            if (graph == null) return list;

            CarLoadTargetLoader[] loaders = UnityEngine.Object.FindObjectsByType<CarLoadTargetLoader>(FindObjectsSortMode.None);
            CarLoaderSequencer[] sequencers = UnityEngine.Object.FindObjectsByType<CarLoaderSequencer>(FindObjectsSortMode.None);
            CTCBlock[] blocks = UnityEngine.Object.FindObjectsByType<CTCBlock>(FindObjectsSortMode.None);

            foreach (CarLoadTargetLoader loader in loaders)
            {
                if (loader == null || !loader.enabled || loader.keyValueObject == null) continue;
                if (!string.Equals(loader.loadId, "rr/water", StringComparison.OrdinalIgnoreCase)) continue;
                string id = loader.keyValueObject.RegisteredId;
                if (!graph.TryGetLocationFromWorldPoint(loader.transform.position, 10f, out Location loc)) continue;

                CarLoaderSequencer sequencer = sequencers.FirstOrDefault(s => s != null && s.keyValueObject != null && s.keyValueObject.RegisteredId == id);
                if (sequencer == null) continue;

                Vector3 game = graph.GetPosition(loc);
                string place = OpsController.Shared?.ClosestAreaForGamePosition(game)?.name ?? id;
                CTCBlock block = blocks.FirstOrDefault(b => b != null && b.GetComponentsInChildren<TrackSpan>(true).Any(s => s != null && s.IsValid && s.Contains(loc)));
                list.Add(new Column { Loader = loader, Sequencer = sequencer, Id = id, Place = place, Location = loc, Block = block });
            }

            _columns = list;
            if (!_loggedColumns && list.Count > 0)
            {
                _loggedColumns = true;
                Main.Mod.Logger.Log("Water stop: " + list.Count + " water columns found: " + string.Join(", ", list.Select(c =>
                    $"{c.Id} ({c.Place}, block {c.Block?.id ?? "none"}, rate {c.Loader.outputRate}/s, reach {c.Loader.radius} m, max {c.Loader.maximumSpeedInMph} mph, key {c.Sequencer.readWantsLoadingKey})")));
            }
            return list;
        }
    }
}
