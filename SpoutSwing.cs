using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Game.Builtins;
using Game.State;
using HarmonyLib;
using Helpers;
using KeyValue.Runtime;
using Model;
using Model.Definition.Data;
using Model.Ops;
using RollingStock;
using RollingStock.Controls;
using Track;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Swinging water spouts (setting SpoutSwing, off by default).
    ///
    /// The game's water column and water tower spouts move along one baked animation clip (ClearanceLimitedController):
    /// the side-to-side column swings out and then drops, the drop-only spout just swings down. Either way the spout
    /// always lands at one fixed spot, and the loader (CarLoadTargetLoader) fills a tender hatch only if the hatch's
    /// centre is within hatch radius + loader radius (0.6 m for the F-71) of that spot.
    ///
    /// This component turns the spout about a vertical axis on top of the clip, toward the nearest tender hatch it can
    /// reach, so the drop (and its clearance probe) still play exactly as before, just over the hatch:
    ///  - the kind is read from where the spout is parked: a spout that sits more than 1.5 m (flat) from the loader's fill
    ///    point swings in from beside the track before it drops (a side-to-side column), one that hangs near it only drops;
    ///  - a side-to-side column's whole animated object is put under a new parent ("wrapper") at its own origin (the
    ///    column's axis) and the wrapper is turned, up to SpoutSwingDegrees each way;
    ///  - a drop-only spout shares its animated object with the tank and legs, so only its own parts (children named
    ///    "spout" or "hose") are turned, each frame after the animation, about the spout's hinge, up to
    ///    DropSpoutSwingDegrees each way (a small, prototypical swivel). spoutswing.txt can set a limit per column;
    ///  - the host picks the angle while the spout is asked to load and not yet loading, and writes it to the column's
    ///    synced key "aebf.swingYaw", so every player sees the same swing; the spout turns back when it is put away;
    ///  - the loader's fill check uses the turned spout tip (transpiler on LoadLoop) and a hatch shape that is longer
    ///    side to side than lengthwise (prefix on LoadSlotFromCar): half-length = the game's hatch radius, half-width =
    ///    radius x HatchWidthFactor, unless spoutswing.txt gives the tender its own sizes.
    ///
    /// Clients with the mod draw the swing from the synced key; clients without it see the spout in its usual place.
    /// </summary>
    public class SpoutSwing : MonoBehaviour
    {
        internal const string YawKey = "aebf.swingYaw";
        private const float AimEverySeconds = 0.5f;
        private const float TurnDoneDegrees = 1.5f;
        private const float FitMarginForWaterStop = 0.9f;   // the water stop counts a hatch as in reach below this fit (1 = edge)

        private static readonly Dictionary<CarLoadTargetLoader, SpoutSwing> ByLoader = new Dictionary<CarLoadTargetLoader, SpoutSwing>();
        private static readonly HashSet<string> Dumped = new HashSet<string>();

        private CarLoadTargetLoader _loader;
        private CarLoaderSequencer _sequencer;
        private ClearanceLimitedController _spout;
        private KeyValueObject _kvo;
        private Transform _wrapper;      // side-to-side columns: the whole animated spout object sits under this, and it is turned
        private Quaternion _wrapperBase;
        private bool _loaderInWrapper;
        private Vector3 _pivotLocal;      // the vertical axis the spout turns about, in this object's space
        private readonly List<Part> _parts = new List<Part>();   // drop-only spouts: only the spout's own parts are turned, after the animation
        private float _parkedMeters;      // how far the spout sits from the fill point before it moves (tells the two kinds apart)

        private sealed class Part
        {
            public Transform T;
            public Vector3 LastPos;
            public Quaternion LastRot;
            public float Applied;
            public bool Seen;
            public float Sign = 1f;   // 1: turns with the spout; -1: held still against the turn of the object it sits in
        }

        private const float FootRadiusMeters = 0.5f;    // a renderer this close (flat) to the column's axis, and no higher than
        private const float FootHeightMeters = 0.25f;   // this above its foot, is the base and stays still
        private const float ParkedFarMeters = 1.5f;   // parked this far from the fill point (flat): it swings in, then drops

        private bool Turnable => _wrapper != null || _parts.Count > 0;
        private Vector3 PivotWorld => transform.TransformPoint(_pivotLocal);
        private Vector3 _fillLocal;       // the loader's fill point in this object's space, with this mod's turn taken out
        private bool _fillSeen;
        private float _armMeters;
        private bool _swingType;          // true: side-to-side column; false: drop-only spout
        private float _maxOverride = float.NaN;
        private float _shown;             // the turn drawn on this machine (eases toward the synced one)
        private float _nextAim;
        private float _lastLoggedYaw = float.NaN;
        private string _lastLoggedCar;
        private string _id;
        private string _prefab;

        internal float MaxDegrees => !float.IsNaN(_maxOverride) ? _maxOverride
            : (_swingType ? Main.Settings.SpoutSwingDegrees : Main.Settings.DropSpoutSwingDegrees);

        private static bool On => Main.Active && Main.Settings != null && Main.Settings.SpoutSwing;

        // ---- Set-up (CarLoaderBuiltin.Configure postfix) ----

        internal static void Attach(CarLoaderBuiltin builtin)
        {
            if (builtin == null || builtin.GetComponent<SpoutSwing>() != null) return;
            CarLoadTargetLoader loader = builtin.GetComponentInChildren<CarLoadTargetLoader>(true);
            if (loader == null || !IsWater(loader)) return;
            builtin.gameObject.AddComponent<SpoutSwing>().Setup(loader);
        }

        private static bool IsWater(CarLoadTargetLoader loader)
        {
            return loader.loadId != null && loader.loadId.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Setup(CarLoadTargetLoader loader)
        {
            _loader = loader;
            _kvo = loader.keyValueObject;
            _sequencer = GetComponentInChildren<CarLoaderSequencer>(true);
            _spout = GetComponentInChildren<ClearanceLimitedController>(true);
            _id = _kvo != null && !string.IsNullOrEmpty(_kvo.RegisteredId) ? _kvo.RegisteredId : name;
            _prefab = name;
            ByLoader[loader] = this;
            HatchTable.LoadIfStale();
            _maxOverride = HatchTable.SpoutDegrees(_id, _prefab);

            if (_spout != null)
            {
                Transform spoutT = _spout.transform;
                Transform probe = _spout.probeOrigin != null ? _spout.probeOrigin : spoutT;
                _parkedMeters = Flat(probe.position - _loader.transform.position).magnitude;
                _swingType = _parkedMeters > ParkedFarMeters;

                if (_swingType && spoutT.parent != null)
                {
                    // A column: the arm swings in from beside the track, then drops. The whole animated object turns
                    // about its own origin (the column's axis), under a wrapper above the Animator so its paths still bind.
                    var wrap = new GameObject("AEBF.SpoutSwing");
                    _wrapper = wrap.transform;
                    _wrapper.SetParent(spoutT.parent, false);
                    _wrapper.position = spoutT.position;
                    _wrapper.rotation = spoutT.parent.rotation;
                    _wrapperBase = _wrapper.rotation;
                    spoutT.SetParent(_wrapper, true);
                    _loaderInWrapper = _loader.transform.IsChildOf(_wrapper);
                    _pivotLocal = transform.InverseTransformPoint(_wrapper.position);

                    // The base (foot, flange, pad) is part of the same animated object but must not turn with the arm: turn
                    // each renderer that sits at the column's foot back by the same angle, about the same axis.
                    Vector3 axis = _wrapper.position;
                    foreach (MeshRenderer r in spoutT.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Transform t = r.transform;
                        Vector3 d = t.position - axis;
                        if (Flat(d).magnitude > FootRadiusMeters || d.y > FootHeightMeters || d.y < -0.5f) continue;
                        if (_parts.Any(x => t.IsChildOf(x.T))) continue;
                        _parts.RemoveAll(x => x.T.IsChildOf(t));
                        _parts.Add(new Part { T = t, Sign = -1f });
                    }

                    // The click handle: the prefab parks it (PrefabRuntimeReparent) somewhere other than the animated arm, so it
                    // would stay put while the arm turns away from it. Turn it with the arm, about the same axis.
                    ScanHandles();
                    _rescanUntil = Time.unscaledTime + 10f;
                }
                else
                {
                    // A hanging spout: the tank and legs share the animated object, so only the spout and its hose
                    // chains turn (each frame, after the animation), about the spout's own hinge.
                    foreach (Transform child in spoutT)
                    {
                        string n = child.name;
                        bool spoutPart = n.IndexOf("spout", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool hose = n.IndexOf("hose", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!spoutPart && !hose) continue;
                        if (n.IndexOf("collider", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        _parts.Add(new Part { T = child });
                    }
                    Part hinge = _parts.FirstOrDefault(x => x.T.name.IndexOf("spout", StringComparison.OrdinalIgnoreCase) >= 0) ?? _parts.FirstOrDefault();
                    if (hinge != null) _pivotLocal = transform.InverseTransformPoint(hinge.T.position);
                }
            }

            _fillLocal = transform.InverseTransformPoint(_loader.transform.position);
            _armMeters = Turnable ? Flat(_loader.transform.position - PivotWorld).magnitude : 0f;
            DumpOnce();
        }

        private float _rescanUntil, _nextRescan;
        private bool _handleLogged;

        /// <summary>Finds click handles that the prefab parks outside the animated arm and turns them with it. Safe to repeat.</summary>
        private void ScanHandles()
        {
            if (_wrapper == null) return;
            foreach (PrefabRuntimeReparent r in GetComponentsInChildren<PrefabRuntimeReparent>(true))
            {
                Transform handle = r.transform;
                if (handle.IsChildOf(_wrapper) || _parts.Any(x => x.T == handle || handle.IsChildOf(x.T))) continue;   // already rides on the arm
                _parts.Add(new Part { T = handle, Sign = 1f });
                Main.Mod.Logger.Log($"Spout swing ({_id}): click handle '{handle.name}' (parent '{(handle.parent != null ? handle.parent.name : "-")}', reparent target '{(r.target != null ? r.target.name : "-")}') now turns with the arm.");
            }
        }

        private void LogHandles(string when)
        {
            foreach (PrefabRuntimeReparent r in GetComponentsInChildren<PrefabRuntimeReparent>(true))
            {
                Transform h = r.transform;
                var chain = new List<string>();
                for (Transform c = h; c != null && c != transform; c = c.parent) chain.Add(c.name);
                bool turning = _parts.Any(x => x.T == h);
                Main.Mod.Logger.Log($"Spout swing ({_id}) handle {when}: '{string.Join(" < ", chain)}' at {V(transform.InverseTransformPoint(h.position))} in the structure, shown {_shown:0}°, in wrapper {h.IsChildOf(_wrapper)}, turned by the mod {turning}, active {h.gameObject.activeInHierarchy}.");
            }
        }

        private void OnDestroy()
        {
            if (_loader != null && ByLoader.TryGetValue(_loader, out SpoutSwing s) && s == this) ByLoader.Remove(_loader);
            if (_wrapper != null) _wrapper.rotation = _wrapperBase;   // back to the game's own spot
            foreach (Part part in _parts)
            {
                if (part.T != null && part.Seen && Mathf.Abs(part.Applied) > 1e-4f) part.T.RotateAround(PivotWorld, Vector3.up, -part.Applied);
            }
        }

        // ---- Per frame ----

        private void LateUpdate()
        {
            try
            {
                if (!Turnable || _kvo == null) return;
                if (_wrapper != null && Time.unscaledTime < _rescanUntil && Time.unscaledTime >= _nextRescan)
                {
                    _nextRescan = Time.unscaledTime + 0.5f;
                    ScanHandles();
                }
                if (_wrapper != null && !_handleLogged && Mathf.Abs(_shown) > 20f)
                {
                    _handleLogged = true;
                    LogHandles("after turning");
                }
                if (StateManager.IsHost && Time.unscaledTime >= _nextAim)
                {
                    _nextAim = Time.unscaledTime + AimEverySeconds;
                    Aim();
                }

                float target = SyncedYaw();
                if (!Mathf.Approximately(target, _shown))
                {
                    _shown = Mathf.MoveTowards(_shown, target, Mathf.Max(5f, Main.Settings.SpoutSwingDegreesPerSecond) * Time.deltaTime);
                    if (_wrapper != null) _wrapper.rotation = Quaternion.AngleAxis(_shown, Vector3.up) * _wrapperBase;
                }
                if (_parts.Count > 0) TurnParts();

                // Learn where the loader really fills (it may ride on the animated arm): while the loader may load, the spout is down.
                if (!string.IsNullOrEmpty(_loader.canLoadBoolKey) && _kvo[_loader.canLoadBoolKey].BoolValue)
                {
                    Vector3 unturned = RotateAbout(_loader.transform.position, PivotWorld, _loaderInWrapper ? -_shown : 0f);
                    Vector3 local = transform.InverseTransformPoint(unturned);
                    if (!_fillSeen || (local - _fillLocal).sqrMagnitude > 0.0025f)
                    {
                        if (!_fillSeen)
                        {
                            Vector3 moved = local - _fillLocal;
                            Main.Mod.Logger.Log($"Spout swing ({_id}): spout down; the loader fills at {V(local)} in the structure (moved {moved.magnitude:0.00} m from where it sat at set-up), arm {Flat(unturned - PivotWorld).magnitude:0.00} m from the pivot.");
                        }
                        _fillSeen = true;
                        _fillLocal = local;
                        _armMeters = Flat(unturned - PivotWorld).magnitude;
                    }
                }
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning($"Spout swing ({_id}): {e.Message}");
                enabled = false;
            }
        }

        /// <summary>
        /// Turns the spout's own parts to the shown angle, after the animation has run. The Animator rewrites a part's pose
        /// when it is playing; when it did (the part is not where this left it) the whole angle is applied, otherwise just
        /// the change since last frame.
        /// </summary>
        private void TurnParts()
        {
            Vector3 pivot = PivotWorld;
            foreach (Part part in _parts)
            {
                Transform t = part.T;
                if (t == null) continue;
                if (part.Sign > 0f && _wrapper != null && t.IsChildOf(_wrapper)) { part.Seen = false; continue; }   // reparented onto the arm: it turns by itself
                bool fresh = !part.Seen
                    || (t.localPosition - part.LastPos).sqrMagnitude > 1e-8f
                    || Quaternion.Angle(t.localRotation, part.LastRot) > 0.01f;
                float want = part.Sign * _shown;
                float delta = fresh ? want : want - part.Applied;
                if (Mathf.Abs(delta) > 1e-4f) t.RotateAround(pivot, Vector3.up, delta);
                part.Applied = want;
                part.LastPos = t.localPosition;
                part.LastRot = t.localRotation;
                part.Seen = true;
            }
        }

        private float SyncedYaw()
        {
            Value v = _kvo[YawKey];
            return v.IsNull ? 0f : v.FloatValue;
        }

        private void SetSyncedYaw(float yaw)
        {
            if (Mathf.Abs(SyncedYaw() - yaw) < 0.5f) return;
            _kvo[YawKey] = Value.Float(yaw);
        }

        /// <summary>Host: turn toward the best hatch while the spout is asked to load and not loading yet; back to 0 otherwise.</summary>
        private void Aim()
        {
            if (!On || _sequencer == null) { SetSyncedYaw(0f); return; }
            bool wants = _kvo[_sequencer.readWantsLoadingKey].BoolValue;
            if (!wants) { SetSyncedYaw(0f); _lastLoggedCar = null; return; }
            if (_kvo[_sequencer.readIsLoadingKey].BoolValue) return;   // keep the angle it is filling at

            if (TryBest(null, -1, out Car car, out CarLoadTarget hatch, out float yaw, out float fit, out Vector2 half))
            {
                SetSyncedYaw(yaw);
                string carId = car.id;
                if (carId != _lastLoggedCar || float.IsNaN(_lastLoggedYaw) || Mathf.Abs(yaw - _lastLoggedYaw) > 2f)
                {
                    _lastLoggedCar = carId;
                    _lastLoggedYaw = yaw;
                    Main.Mod.Logger.Log($"Spout swing ({_id}): turning {yaw:0.0}° (limit {MaxDegrees:0}°) to {car.DisplayName} ({car.DefinitionInfo?.Identifier}), hatch fit {fit:0.00} (1 = edge; half-length {half.x:0.00} m, half-width {half.y:0.00} m).");
                }
            }
            else
            {
                SetSyncedYaw(0f);
            }
        }

        // ---- Geometry ----

        /// <summary>The best reachable hatch (fit at most 1), or false. With 'only' set, just that car's slot.</summary>
        private bool TryBest(Car only, int onlySlot, out Car bestCar, out CarLoadTarget bestHatch, out float bestYaw, out float bestFit, out Vector2 bestHalf)
        {
            bestCar = null; bestHatch = null; bestYaw = 0f; bestFit = float.MaxValue; bestHalf = Vector2.zero;
            if (!Turnable || TrainController.Shared == null) return false;
            var graph = TrainController.Shared.Graph?.Current;
            if (graph == null) return false;

            Vector3 pivot = PivotWorld;
            Vector3 fill = transform.TransformPoint(_fillLocal);
            float max = Mathf.Clamp(MaxDegrees, 0f, 180f);

            IEnumerable<Car> cars;
            if (only != null)
            {
                cars = new[] { only };
            }
            else
            {
                var found = new HashSet<Car>();
                TrainController.Shared.CheckForCarsAtPointIgnoringElevation(WorldTransformer.WorldToGame(pivot), _armMeters + 3f, found);
                cars = found;
            }

            foreach (Car car in cars)
            {
                if (car == null) continue;
                if (only == null && _loader.onlyLoadPlayerCars && !car.IsOwnedByPlayer) continue;
                foreach (CarLoadTarget hatch in car.GetComponentsInChildren<CarLoadTarget>())
                {
                    if (onlySlot >= 0 && hatch.slotIndex != onlySlot) continue;
                    if (!SlotTakes(car, hatch, _loader.loadId)) continue;
                    if (only == null && IsFull(car, hatch.slotIndex)) continue;

                    Vector3 hatchGame = HatchGame(car, hatch, graph);
                    Vector3 hatchWorld = WorldTransformer.GameToWorld(hatchGame);
                    float yaw = Vector3.SignedAngle(Flat(fill - pivot), Flat(hatchWorld - pivot), Vector3.up);
                    yaw = Mathf.Clamp(yaw, -max, max);
                    Vector3 tipGame = WorldTransformer.WorldToGame(RotateAbout(fill, pivot, yaw));
                    float fit = Fit(tipGame, car, hatch, _loader.radius, graph, out Vector2 half);
                    if (fit < bestFit)
                    {
                        bestFit = fit; bestCar = car; bestHatch = hatch; bestYaw = yaw; bestHalf = half;
                    }
                }
            }
            return bestCar != null && bestFit <= 1f;
        }

        private static bool SlotTakes(Car car, CarLoadTarget hatch, string loadId)
        {
            var slots = car.Definition.LoadSlots;
            if (hatch.slotIndex < 0 || hatch.slotIndex >= slots.Count) return false;
            string req = slots[hatch.slotIndex].RequiredLoadIdentifier;
            return !string.IsNullOrEmpty(req) && req == loadId;
        }

        private static bool IsFull(Car car, int slot)
        {
            LoadSlot s = car.Definition.LoadSlots[slot];
            if (s.MaximumCapacity <= 0f) return false;
            float q = car.GetLoadInfo(slot)?.Quantity ?? 0f;
            return q / s.MaximumCapacity > 0.999f;
        }

        /// <summary>The hatch centre in game coordinates, as CarLoadTargetLoader.LoadSlotFromCar works it out.</summary>
        private static Vector3 HatchGame(Car car, CarLoadTarget hatch, IGraphView graph)
        {
            Matrix4x4 m = car.GetTransformMatrix(graph);
            return m.MultiplyPoint3x4(car.transform.InverseTransformPoint(hatch.transform.position));
        }

        /// <summary>
        /// How well a spout tip (game coordinates) sits over a hatch: 0 at the centre, 1 at the edge of the hatch shape
        /// widened by the loader's own radius. The shape is an ellipse in the car's frame (Z along the car).
        /// </summary>
        internal static float Fit(Vector3 tipGame, Car car, CarLoadTarget hatch, float loaderRadius, IGraphView graph, out Vector2 half)
        {
            Matrix4x4 m = car.GetTransformMatrix(graph);
            Vector3 centre = m.MultiplyPoint3x4(car.transform.InverseTransformPoint(hatch.transform.position));
            Vector3 along = Flat(m.MultiplyVector(Vector3.forward)).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, along);
            Vector3 d = Flat(tipGame - centre);
            half = HatchTable.Half(car, hatch);
            float a = half.x + loaderRadius;
            float b = half.y + loaderRadius;
            float u = Vector3.Dot(d, along) / Mathf.Max(0.05f, a);
            float w = Vector3.Dot(d, across) / Mathf.Max(0.05f, b);
            return Mathf.Sqrt(u * u + w * w);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static Vector3 RotateAbout(Vector3 p, Vector3 pivot, float degrees)
        {
            return pivot + Quaternion.AngleAxis(degrees, Vector3.up) * (p - pivot);
        }

        // ---- Hooks used by the patches and the water stop ----

        /// <summary>The loader's position for its fill loop (world): the spout tip turned to the synced angle.</summary>
        internal static Vector3 LoaderPoint(Component component)
        {
            Vector3 live = component.transform.position;
            try
            {
                if (!On || !(component is CarLoadTargetLoader loader) || !ByLoader.TryGetValue(loader, out SpoutSwing s) || s == null || !s.Turnable) return live;
                float target = s.SyncedYaw();
                // Not loading until the stand has finished turning to the hatch: the spout lowers only once it points at it.
                if (Mathf.Abs(target - s._shown) > TurnDoneDegrees) return live + Vector3.up * 1000f;
                float delta = s._loaderInWrapper ? target - s._shown : target;
                return RotateAbout(live, s.PivotWorld, delta);
            }
            catch (Exception)
            {
                return live;
            }
        }

        /// <summary>True when the hatch shape applies to this loader (the setting is on and it loads water).</summary>
        internal static bool ShapeApplies(CarLoadTargetLoader loader)
        {
            return On && loader != null && IsWater(loader);
        }

        /// <summary>
        /// For the water stop: can this column's spout reach the tender's hatch, turning within its limit? Uses a small
        /// margin inside the hatch shape. 'how' describes it for the log.
        /// </summary>
        internal static bool CanReach(CarLoadTargetLoader loader, Car tender, int slot, out string how)
        {
            how = null;
            try
            {
                if (!On || loader == null || tender == null || !ByLoader.TryGetValue(loader, out SpoutSwing s) || s == null) return false;
                bool found = s.TryBest(tender, slot, out Car car, out _, out float yaw, out float fit, out _);
                if (car == null) return false;
                bool ok = found && fit <= FitMarginForWaterStop;
                how = $"{(ok ? "within" : "out of")} the turning spout's reach (fit {fit:0.00} at {yaw:0.0}°, limit {s.MaxDegrees:0}°; 1 = hatch edge, {FitMarginForWaterStop:0.0} needed)";
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---- Diagnostics ----

        private void DumpOnce()
        {
            if (!Dumped.Add(_prefab)) return;
            var sb = new StringBuilder();
            sb.AppendLine($"Spout swing: structure '{_prefab}' (first seen as {_id}), loader {_loader.name} load {_loader.loadId} radius {_loader.radius:0.00} m, keys canLoad '{_loader.canLoadBoolKey}' isLoading '{_loader.isLoadingBoolKey}'.");
            if (_spout == null)
            {
                sb.AppendLine("  No ClearanceLimitedController found: the spout will not turn here (the hatch shape still applies).");
            }
            else
            {
                AnimationClip clip = _spout.animationClip;
                sb.AppendLine($"  Spout '{_spout.name}': key '{_spout.key}', clip '{clip?.name}' {clip?.length:0.00} s, speed {_spout.speed:0.##}, parked {_parkedMeters:0.00} m from the fill point (swings in if over {ParkedFarMeters:0.0} m) => {(_swingType ? "side-to-side column" : "drop-only spout")}, limit {MaxDegrees:0}°{(float.IsNaN(_maxOverride) ? "" : " (spoutswing.txt)")}.");
                sb.AppendLine($"  Probe origin '{(_spout.probeOrigin != null ? _spout.probeOrigin.name : "-")}', stop {_spout.stopDistance:0.00} m. {(_wrapper != null ? "Turns the whole animated object" + (_parts.Any(x => x.Sign < 0f) ? ", holding still: " + string.Join(", ", _parts.Where(x => x.Sign < 0f).Select(x => x.T.name)) : "") + (_parts.Any(x => x.Sign > 0f) ? ", also turning (outside it): " + string.Join(", ", _parts.Where(x => x.Sign > 0f).Select(x => x.T.name)) : "") : _parts.Count > 0 ? "Turns only " + string.Join(", ", _parts.Select(x => x.T.name)) : "Nothing to turn (no spout parts found)")}; pivot {(Turnable ? V(_pivotLocal) : "-")} in the structure; loader {V(_fillLocal)}; arm {_armMeters:0.00} m; loader {(_loaderInWrapper ? "rides on the spout" : "is fixed")}.");
            }
            if (_sequencer != null)
                sb.AppendLine($"  Sequencer keys request '{_sequencer.readWantsLoadingKey}', prepare '{_sequencer.writePrepareLoadKey}', canLoad '{_sequencer.writeCanLoadKey}', animate '{_sequencer.writeAnimateLoadKey}', delays {_sequencer.prepareLoadingDelay:0.#}/{_sequencer.startLoadingDelay:0.#} s.");
            sb.AppendLine("  Object tree:");
            int lines = 0;
            Tree(transform, 0, sb, ref lines);
            Main.Mod.Logger.Log(sb.ToString().TrimEnd());
        }

        private void Tree(Transform t, int depth, StringBuilder sb, ref int lines)
        {
            if (lines >= 120 || depth > 8) return;
            lines++;
            string comps = string.Join(", ", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
            sb.Append(' ', 4 + depth * 2).Append(t.name).Append(" @").Append(V(transform.InverseTransformPoint(t.position)))
                .Append(" yaw ").Append(t.eulerAngles.y.ToString("0", CultureInfo.InvariantCulture));
            if (comps.Length > 0) sb.Append(" [").Append(comps).Append(']');
            sb.AppendLine();
            foreach (Transform c in t) Tree(c, depth + 1, sb, ref lines);
        }

        private static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", v.x, v.y, v.z);
    }

    /// <summary>
    /// Hatch sizes and per-column swing limits. The rule for every tender: half-length = the game's hatch radius,
    /// half-width = radius x HatchWidthFactor. spoutswing.txt in the mod folder can give a tender its own sizes, and a
    /// column (map object id, or structure name for every column of that kind) its own limit.
    /// </summary>
    internal static class HatchTable
    {
        private static readonly Dictionary<string, Vector2> Hatches = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> Spouts = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static float _loadedAt = float.NegativeInfinity;

        private static string FilePath => System.IO.Path.Combine(Main.Mod.Path, "spoutswing.txt");

        private const string Template =
@"# Swinging spouts (AutoEngineerBrakeFix). Lines starting with # are ignored; sizes in metres, angles in degrees.
#
# Hatch shape for a tender (the car definition identifier is written to Player.log the first time a spout turns to it):
#   hatch <car definition identifier> <half-length along the car> <half-width across the car>
# Without a line, a tender's hatch is the game's hatch radius long and radius x HatchWidthFactor (Settings.xml) wide.
#   hatch rr/lt-2102-f71 0.40 0.80
#
# Swing limit each way for one column (map object id, e.g. wh-e-water) or for every column of a kind (its structure
# name, as in the Player.log dump). Without a line: SpoutSwingDegrees for side-to-side columns, DropSpoutSwingDegrees
# for drop-only spouts.
#   spout wh-e-water 20
";

        internal static void LoadIfStale()
        {
            if (Time.realtimeSinceStartup - _loadedAt < 30f) return;
            _loadedAt = Time.realtimeSinceStartup;
            Hatches.Clear();
            Spouts.Clear();
            try
            {
                if (!System.IO.File.Exists(FilePath)) { System.IO.File.WriteAllText(FilePath, Template); return; }
                var inv = CultureInfo.InvariantCulture;
                foreach (string raw in System.IO.File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 4 && p[0].Equals("hatch", StringComparison.OrdinalIgnoreCase)
                        && float.TryParse(p[2], NumberStyles.Float, inv, out float along) && float.TryParse(p[3], NumberStyles.Float, inv, out float across)
                        && along > 0f && across > 0f)
                    {
                        Hatches[p[1]] = new Vector2(along, across);
                    }
                    else if (p.Length >= 3 && p[0].Equals("spout", StringComparison.OrdinalIgnoreCase)
                        && float.TryParse(p[2], NumberStyles.Float, inv, out float deg) && deg >= 0f)
                    {
                        Spouts[p[1]] = Mathf.Min(deg, 180f);
                    }
                    else
                    {
                        Main.Mod.Logger.Warning("spoutswing.txt: line not understood: " + line);
                    }
                }
                if (Hatches.Count > 0 || Spouts.Count > 0)
                    Main.Mod.Logger.Log($"spoutswing.txt: {Hatches.Count} hatch size(s) ({string.Join(", ", Hatches.Keys)}), {Spouts.Count} spout limit(s) ({string.Join(", ", Spouts.Keys)}).");
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("spoutswing.txt could not be read: " + e.Message);
            }
        }

        internal static float SpoutDegrees(string id, string structure)
        {
            if (id != null && Spouts.TryGetValue(id, out float a)) return a;
            if (structure != null && Spouts.TryGetValue(structure, out float b)) return b;
            return float.NaN;
        }

        /// <summary>(half-length along the car, half-width across it) of this hatch.</summary>
        internal static Vector2 Half(Car car, CarLoadTarget hatch)
        {
            string ident = car.DefinitionInfo?.Identifier;
            if (ident != null && Hatches.TryGetValue(ident, out Vector2 v)) return v;
            float r = Mathf.Max(0.05f, hatch.radius);
            if (ident != null && Reported.Add(ident))
                Main.Mod.Logger.Log($"Spout swing: hatch of {ident} by the general rule: {r:0.00} m half-length, {r * Mathf.Max(1f, Main.Settings.HatchWidthFactor):0.00} m half-width (give it a 'hatch' line in spoutswing.txt to change).");
            return new Vector2(r, r * Mathf.Max(1f, Main.Settings.HatchWidthFactor));
        }
    }

    // ---- Patches ----

    /// <summary>Adds the swing to each water column or tower as the map builds it.</summary>
    [HarmonyPatch(typeof(CarLoaderBuiltin), nameof(CarLoaderBuiltin.Configure))]
    internal static class CarLoaderBuiltin_Configure_Patch
    {
        private static void Postfix(CarLoaderBuiltin __instance, bool __result)
        {
            if (!__result || !Main.Active) return;
            try { SpoutSwing.Attach(__instance); }
            catch (Exception e) { Main.Mod.Logger.Warning("Spout swing set-up failed: " + e.Message); }
        }
    }

    /// <summary>
    /// The fill loop reads its own position once a second (transform.position). That read becomes
    /// SpoutSwing.LoaderPoint(this), which is the same position unless the spout has been turned.
    /// </summary>
    [HarmonyPatch]
    internal static class CarLoadTargetLoader_LoadLoop_Patch
    {
        internal static MethodBase Target()
        {
            MethodInfo loop = AccessTools.Method(typeof(CarLoadTargetLoader), "LoadLoop");
            return loop == null ? null : AccessTools.EnumeratorMoveNext(loop);
        }

        /// <summary>How many transform.position reads the loop has (the patch needs exactly one).</summary>
        internal static int PositionReads()
        {
            MethodBase m = Target();
            if (m == null) return 0;
            List<CodeInstruction> code = PatchProcessor.GetOriginalInstructions(m);
            return Matches(code).Count;
        }

        private static readonly MethodInfo GetTransform = AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform));
        private static readonly MethodInfo GetPosition = AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));
        private static readonly MethodInfo Point = AccessTools.Method(typeof(SpoutSwing), nameof(SpoutSwing.LoaderPoint));

        private static List<int> Matches(List<CodeInstruction> code)
        {
            var at = new List<int>();
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].Calls(GetTransform) && code[i + 1].Calls(GetPosition)) at.Add(i);
            }
            return at;
        }

        private static MethodBase TargetMethod() => Target();

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            List<int> at = Matches(code);
            if (at.Count != 1)
            {
                Main.Mod.Logger.Warning($"Spout swing: the loader's fill loop reads its position {at.Count} times (expected 1); the fill check stays at the spout's usual spot.");
                return code;
            }
            int i = at[0];
            code[i] = new CodeInstruction(OpCodes.Call, Point).MoveLabelsFrom(code[i]);
            code[i + 1] = new CodeInstruction(OpCodes.Nop).MoveLabelsFrom(code[i + 1]);
            return code;
        }
    }

    /// <summary>The fill check with the hatch shape (longer side to side) instead of the game's circle, for water loaders while the setting is on.</summary>
    [HarmonyPatch(typeof(CarLoadTargetLoader), "LoadSlotFromCar")]
    internal static class CarLoadTargetLoader_LoadSlotFromCar_Patch
    {
        private static bool Prefix(CarLoadTargetLoader __instance, Car car, Vector3 point, ref int slotIndex, ref LoadSlot __result)
        {
            try
            {
                if (!SpoutSwing.ShapeApplies(__instance)) return true;
                var graph = TrainController.Shared.Graph.Current;
                var slots = car.Definition.LoadSlots;
                foreach (CarLoadTarget hatch in car.GetComponentsInChildren<CarLoadTarget>())
                {
                    if (hatch.slotIndex < 0 || hatch.slotIndex >= slots.Count) continue;
                    LoadSlot slot = slots[hatch.slotIndex];
                    if (string.IsNullOrEmpty(slot.RequiredLoadIdentifier) || slot.RequiredLoadIdentifier != __instance.loadId) continue;
                    if (SpoutSwing.Fit(point, car, hatch, __instance.radius, graph, out _) <= 1f)
                    {
                        slotIndex = hatch.slotIndex;
                        __result = slot;
                        return false;
                    }
                }
                slotIndex = -1;
                __result = null;
                return false;
            }
            catch (Exception)
            {
                return true;   // the game's own check
            }
        }
    }
}
