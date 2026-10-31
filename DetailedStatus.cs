using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Physics;
using TMPro;
using Track;
using UI.EngineControls;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Detailed auto-engineer status (setting DetailedStatus, off by default). The game's status line is
    /// "reason: speed", where "Track Speed" lumps together the posted limit, the track class and the curve speed
    /// (the last one cut down when a curve is sharper than the consist's tightest-rated car). This replaces
    /// AutoEngineerPlanner.SetPlannerStatus with the same choice of limit, then:
    ///  - names what sets a track speed: posted limit, railroad-wide limit, track class, curve, or curve held down by
    ///    equipment;
    ///  - adds the distance to a limit or stop ahead (feet and miles, or metres and kilometres with StatusMetric);
    ///  - shows the engine's top speed when that is lower than the limit;
    ///  - names this mod's own events: water stop, approach hold, stall guard.
    /// The status is the planner's own synced value and changes as often as the planner runs (every 0.5 to 3 s).
    /// On the host the status tooltip also lists the reasoning and the targets ahead.
    /// Speeds and targets are not changed; only the text.
    /// </summary>
    internal static class DetailedStatus
    {
        private const float MetersPerFoot = 0.3048f;
        private const float MetersPerMile = 1609.344f;
        private const float NoCurveLimit = 1000f;     // a curvature rating no car reaches, for "the track alone"
        private const int TooltipTargets = 5;

        private static readonly Type PlannerType = SignalPlanning.PlannerType;
        internal static readonly FieldInfo FEngineer = PlannerType != null ? AccessTools.Field(PlannerType, "_engineer") : null;
        internal static readonly FieldInfo FLocomotive = PlannerType != null ? AccessTools.Field(PlannerType, "_locomotive") : null;
        internal static readonly FieldInfo FCars = PlannerType != null ? AccessTools.Field(PlannerType, "_coupledCarsCached") : null;
        internal static readonly FieldInfo FEquipCurvature = PlannerType != null ? AccessTools.Field(PlannerType, "_equipmentMaximumTrackCurvature") : null;
        internal static readonly FieldInfo FLength = PlannerType != null ? AccessTools.Field(PlannerType, "_maximumLength") : null;
        internal static readonly FieldInfo FStatusLabel = AccessTools.Field(typeof(AutoEngineerControlSetBase), "statusLabel");
        private static readonly MethodInfo MClassSpeed = AccessTools.Method(AccessTools.TypeByName("Game.Mow.TrackConditionModel"), "GetMaxSpeedMphForClass", new[] { typeof(int) });

        private static MethodInfo _tooltip;
        private static bool _tooltipLooked, _warned;

        /// <summary>Tooltip text by locomotive id, with the status line it was written for.</summary>
        private static readonly Dictionary<string, KeyValuePair<string, string>> Details = new Dictionary<string, KeyValuePair<string, string>>();

        /// <summary>Builds and stores the status. False to let the game write its own (setting off, or something failed).</summary>
        internal static bool Write(object planner, float maxSpeedMphAbs, string maxSpeedReason, List<AutoEngineer.Targets.Target> targets)
        {
            if (!Main.Active || !Main.Settings.DetailedStatus) return false;
            try
            {
                var ae = FEngineer.GetValue(planner) as AutoEngineer;
                var loco = FLocomotive.GetValue(planner) as BaseLocomotive;
                if (ae == null || loco == null) return false;
                if (ae.WaitingForBrakes) return false;   // the game's "Charging brake line"

                // The game's choice: the orders or track ceiling, or the nearest lower target ahead.
                float mph = maxSpeedMphAbs;
                string reason = maxSpeedReason;
                float nearest = float.PositiveInfinity;
                if (targets != null)
                {
                    foreach (AutoEngineer.Targets.Target t in targets)
                    {
                        float v = Mathf.Abs(t.SpeedMph);
                        if (v < mph && t.Distance < nearest)
                        {
                            mph = v;
                            reason = t.Reason;
                            nearest = t.Distance;
                        }
                    }
                }
                bool ahead = !float.IsPositiveInfinity(nearest);
                float? at = ahead ? nearest : (float?)null;
                string label = reason;
                var detail = new StringBuilder();
                var persistence = new AutoEngineerPersistence(loco.KeyValueObject);
                bool forward = persistence.Orders.Forward;

                if (reason == "Track Speed" && TrackCause(planner, loco, forward, ahead ? nearest : -1f, mph, out string cause, out float? where, detail))
                {
                    label = cause;
                    if (where.HasValue) at = where;
                }
                else if (reason == "Approach Hold")
                {
                    label = "Approach hold (mod)";
                    detail.AppendLine("Keeping the Approach signal's speed until the next signal (mod setting HoldApproachSpeed).");
                }

                if (WaterStop.TryStatus(loco, out WaterStop.StatusInfo ws))
                {
                    detail.AppendLine(ws.Detail);
                    if (ws.Kind == WaterStop.StatusKind.Filling)
                    {
                        label = ws.Label;
                        mph = 0f;
                        at = null;
                    }
                    else if (ws.Kind == WaterStop.StatusKind.Spotting && (reason == "Running to waypoint" || reason == "At waypoint"))
                    {
                        label = ws.Label;
                    }
                    else if (ws.Kind == WaterStop.StatusKind.Slowing && reason != null && reason.StartsWith("Orders", StringComparison.Ordinal))
                    {
                        label = ws.Label;
                        at = ws.DistanceM;
                    }
                }

                if (StallGuard.IsLatched(ae))
                {
                    label = "Stall guard (mod): stalled on the grade";
                    mph = 0f;
                    at = null;
                    detail.AppendLine("The train could not climb the grade, so the throttle stays shut and the brakes on until the engineer is switched off and on again.");
                }

                float top = EngineTopSpeed(planner, loco);
                if (top > 0f && top < mph - 0.5f)
                {
                    detail.AppendLine($"The limit is {mph:F0} mph ({reason}{(at.HasValue ? ", " + Distance(at.Value) + " ahead" : "")}), above what the engine can reach.");
                    label = "Engine top speed";
                    mph = top;
                    at = null;
                }

                string line = mph < 0.5f ? label : $"{label}: {mph:F0}mph";
                if (at.HasValue && at.Value > 3f) line += " in " + Distance(at.Value);

                var tip = new StringBuilder();
                tip.AppendLine(line);
                if (detail.Length > 0) tip.Append(detail);
                if (top > 0f) tip.AppendLine($"Engine top speed: {top:F0} mph");
                if (targets != null && targets.Count > 0)
                {
                    tip.AppendLine("Ahead:");
                    foreach (AutoEngineer.Targets.Target t in targets.OrderBy(x => x.Distance).Take(TooltipTargets))
                    {
                        float v = Mathf.Abs(t.SpeedMph);
                        tip.AppendLine($"  {t.Reason}: {(v < 0.5f ? "stop" : v.ToString("F0", CultureInfo.InvariantCulture) + " mph")}, {(t.Distance > 3f ? Distance(t.Distance) : "here")}");
                    }
                }

                lock (Details) Details[loco.id] = new KeyValuePair<string, string>(line, tip.ToString().TrimEnd());
                if (persistence.PlannerStatus != line) persistence.PlannerStatus = line;
                return true;
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    Main.Mod.Logger.Warning("Detailed status failed, using the game's own status: " + e);
                }
                return false;
            }
        }

        /// <summary>Puts the stored detail in the status label's tooltip, if the label still shows the line it was made for.</summary>
        internal static void UpdateTooltip(AutoEngineerControlSetBase controls)
        {
            if (!Main.Active || !Main.Settings.DetailedStatus || controls == null) return;
            try
            {
                var label = FStatusLabel?.GetValue(controls) as TMP_Text;
                if (label == null) return;
                var t = Traverse.Create(controls);
                var loco = (t.Property("Locomotive").GetValue() ?? t.Field("Locomotive").GetValue()) as BaseLocomotive;
                if (loco == null) return;
                KeyValuePair<string, string> d;
                lock (Details)
                {
                    if (!Details.TryGetValue(loco.id, out d)) return;
                }
                if (label.text != d.Key) return;
                MethodInfo tooltip = Tooltip();
                if (tooltip == null) return;
                ParameterInfo[] ps = tooltip.GetParameters();
                var args = new object[ps.Length];
                args[0] = label.rectTransform;
                args[1] = "Status";
                args[2] = d.Value;
                for (int i = 3; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                tooltip.Invoke(null, args);
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    Main.Mod.Logger.Warning("Detailed status tooltip failed: " + e.Message);
                }
            }
        }

        // The game's RectTransform.Tooltip(title, text) extension; looked up by shape so its namespace does not matter.
        private static MethodInfo Tooltip()
        {
            if (_tooltipLooked) return _tooltip;
            _tooltipLooked = true;
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(AutoEngineerControlSetBase).Assembly))
            {
                if (!type.IsAbstract || !type.IsSealed) continue;   // static classes only
                foreach (MethodInfo m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "Tooltip") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length < 3 || ps[0].ParameterType != typeof(RectTransform) || ps[1].ParameterType != typeof(string) || ps[2].ParameterType != typeof(string)) continue;
                    if (ps.Skip(3).Any(p => !p.HasDefaultValue)) continue;
                    _tooltip = m;
                    return m;
                }
            }
            Main.Mod.Logger.Warning("Detailed status: the game's Tooltip(RectTransform, string, string) was not found; the tooltip keeps the status line only.");
            return null;
        }

        // ---- Track speed ----

        /// <summary>
        /// Finds the piece of track that sets a "Track Speed" limit and says why. aheadM below 0 means the limit
        /// applies under the train now (the planner's ceiling over the train's length); otherwise it is the planner's
        /// target that far ahead, and the restriction is the first piece of track at or beyond it with that speed
        /// (the target sits before the restriction: 13 m in the game, plus this mod's slowing lead).
        /// </summary>
        private static bool TrackCause(object planner, BaseLocomotive loco, bool forward, float aheadM, float mph, out string cause, out float? at, StringBuilder detail)
        {
            cause = null;
            at = null;
            Graph graph = Graph.Shared;
            if (graph == null) return false;
            float equip = Convert.ToSingle(FEquipCurvature.GetValue(planner));
            float length = Mathf.Max(10f, Convert.ToSingle(FLength.GetValue(planner)));
            Location head = HeadOf(loco, forward);
            if (!head.IsValid) return false;

            TrackSegment segment = null;
            if (aheadM < 0f)
            {
                float best = float.PositiveInfinity;
                RouteWalker walker = graph.WalkRoute(head.Flipped(), length);
                while (walker.TryNext(out WalkSegment ws))
                {
                    float v = SegmentSpeed(graph, ws.Segment, equip);
                    if (v < best)
                    {
                        best = v;
                        segment = ws.Segment;
                    }
                }
            }
            else
            {
                RouteWalker walker = graph.WalkRoute(head, aheadM + 400f);
                while (walker.TryNext(out WalkSegment ws))
                {
                    if (ws.EntryDistance + ws.TraversedLength < aheadM - 1f) continue;
                    if (Mathf.Abs(SegmentSpeed(graph, ws.Segment, equip) - mph) > 0.5f) continue;
                    segment = ws.Segment;
                    at = Mathf.Max(aheadM, ws.EntryDistance);
                    break;
                }
            }
            if (segment == null) return false;

            int posted = segment.EffectiveSpeedLimitMph;
            bool railroadWide = segment.speedLimit == 0 || segment.speedLimit >= TrackSegment.RailroadSpeedLimitMph;
            float curvature = graph.CurvatureOfSegment(segment);
            float withEquipment = graph.CalculateMaxSpeedMph(segment, equip);
            float trackAlone = graph.CalculateMaxSpeedMph(segment, NoCurveLimit);
            float classMph = ClassSpeed(segment.PostedTrackClass);

            if (!float.IsNaN(classMph) && classMph < withEquipment - 0.5f)
            {
                cause = $"Track class {segment.PostedTrackClass}";
                detail.AppendLine($"Track class {segment.PostedTrackClass} allows {classMph:F0} mph; the track's own limit there is {withEquipment:F0} mph.");
            }
            else if (withEquipment < trackAlone - 0.5f)
            {
                cause = "Curve (equipment)";
                Car tightest = TightestCar(planner, loco);
                string who = tightest != null ? $"{tightest.DisplayName} is rated for {tightest.MaximumTrackCurvature:0}°" : $"the train is rated for {equip:0}°";
                detail.AppendLine($"Curve of {curvature:0}°: {who}. The track alone allows {trackAlone:F0} mph.");
            }
            else if (trackAlone < posted - 0.5f)
            {
                cause = $"Curve {curvature:0}°";
                detail.AppendLine($"Curve of {curvature:0}° allows {trackAlone:F0} mph; the posted limit there is {posted} mph.");
            }
            else
            {
                cause = railroadWide ? "Railroad limit" : "Posted limit";
                detail.AppendLine(railroadWide
                    ? $"The railroad-wide limit is {TrackSegment.RailroadSpeedLimitMph} mph."
                    : $"Posted limit {posted} mph.");
            }
            return true;
        }

        // As AutoEngineerPlanner.CalculateMaxSpeedMph: the track's limit for this equipment, capped by the track class.
        private static float SegmentSpeed(Graph graph, TrackSegment segment, float equip)
        {
            float v = graph.CalculateMaxSpeedMph(segment, equip);
            float c = ClassSpeed(segment.PostedTrackClass);
            return float.IsNaN(c) ? v : Mathf.Min(v, c);
        }

        private static float ClassSpeed(int trackClass)
        {
            if (MClassSpeed == null) return float.NaN;
            try { return Convert.ToSingle(MClassSpeed.Invoke(null, new object[] { trackClass })); }
            catch (Exception) { return float.NaN; }
        }

        private static Car TightestCar(object planner, BaseLocomotive loco)
        {
            IEnumerable<Car> cars = FCars.GetValue(planner) as List<Car> ?? loco.EnumerateCoupled();
            Car best = null;
            foreach (Car car in cars)
            {
                if (car != null && (best == null || car.MaximumTrackCurvature < best.MaximumTrackCurvature)) best = car;
            }
            return best;
        }

        /// <summary>The lowest top speed among the train's locomotives (the speed where their tractive effort runs out).</summary>
        private static float EngineTopSpeed(object planner, BaseLocomotive loco)
        {
            IEnumerable<Car> cars = FCars.GetValue(planner) as List<Car> ?? loco.EnumerateCoupled();
            float top = float.PositiveInfinity;
            foreach (Car car in cars)
            {
                if (car is BaseLocomotive l && l.maxSpeedMph > 0f) top = Mathf.Min(top, l.maxSpeedMph);
            }
            return float.IsPositiveInfinity(top) ? loco.maxSpeedMph : top;
        }

        /// <summary>The leading end of the consist in the direction of travel, facing ahead (as the planner's StartLocation).</summary>
        internal static Location HeadOf(BaseLocomotive loco, bool forward)
        {
            Car.End end = forward ? Car.End.F : Car.End.R;
            Car headCar = loco.EnumerateCoupled(end).FirstOrDefault() ?? loco;
            Car.LogicalEnd logical = loco.EndToLogical(end);
            Location loc = headCar.LocationFor(logical);
            return logical == Car.LogicalEnd.A ? loc : loc.Flipped();
        }

        /// <summary>Feet and miles, or metres and kilometres with StatusMetric.</summary>
        internal static string Distance(float meters)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            if (Main.Settings.StatusMetric)
            {
                return meters < 1000f
                    ? (Mathf.Round(meters / 10f) * 10f).ToString("0", c) + " m"
                    : (meters / 1000f).ToString("0.0", c) + " km";
            }
            float feet = meters / MetersPerFoot;
            return feet < 1000f
                ? (Mathf.Round(feet / 10f) * 10f).ToString("0", c) + " ft"
                : (meters / MetersPerMile).ToString("0.0", c) + " mi";
        }
    }

    /// <summary>Replaces the planner's status line when DetailedStatus is on (see DetailedStatus).</summary>
    [HarmonyPatch]
    internal static class Planner_SetPlannerStatus_Patch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(SignalPlanning.PlannerType, "SetPlannerStatus");
        }

        private static bool Prefix(object __instance, float __0, string __1, List<AutoEngineer.Targets.Target> __2)
        {
            return !DetailedStatus.Write(__instance, __0, __1, __2);
        }
    }

    /// <summary>Gives the status label the detailed tooltip (host only; see DetailedStatus).</summary>
    [HarmonyPatch(typeof(AutoEngineerControlSetBase), "UpdateStatusLabel")]
    internal static class ControlSet_UpdateStatusLabel_Patch
    {
        private static void Postfix(AutoEngineerControlSetBase __instance)
        {
            DetailedStatus.UpdateTooltip(__instance);
        }
    }
}
