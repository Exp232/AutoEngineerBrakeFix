using System;
using System.Globalization;
using System.Text;
using Track;
using Track.Signals;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Writes logs\ctc_dump_*.txt once per game session: every CTC block with its track spans (end points, length),
    /// and every signal with its position, the blocks it protects and how far each block's span ends are from it.
    /// A signal shows Stop whenever a block it protects is occupied, and a block is occupied when any car is on one of
    /// its spans, so a span that starts short of its signal makes a train trip the signal it is approaching (Sylva
    /// West eastbound: block sy-w goes occupied about 43 m before the signal). This file shows where the spans are.
    /// Read-only: nothing in the game is changed.
    /// </summary>
    internal static class CtcDump
    {
        private static bool _done;

        internal static void DumpOnce()
        {
            if (_done) return;
            try
            {
                var blocks = UnityEngine.Object.FindObjectsByType<CTCBlock>(FindObjectsSortMode.None);
                var signals = UnityEngine.Object.FindObjectsByType<CTCSignal>(FindObjectsSortMode.None);
                if (blocks.Length == 0 && signals.Length == 0 || Graph.Shared == null) return;   // map not loaded yet
                _done = true;

                var sb = new StringBuilder();
                sb.AppendLine("AutoEngineerBrakeFix CTC dump");
                sb.AppendLine("Time: " + DateTime.Now.ToString("s"));
                sb.AppendLine("Game version: " + Application.version);
                sb.AppendLine("Mod version: " + Main.Mod.Info.Version);
                sb.AppendLine("Positions are game coordinates (x, y, z) in metres; along-track figures need both points on one segment.");
                sb.AppendLine();

                Array.Sort(blocks, (a, b) => string.CompareOrdinal(a.id, b.id));
                Array.Sort(signals, (a, b) => string.CompareOrdinal(a.id, b.id));

                sb.AppendLine("== Blocks (" + blocks.Length + ") ==");
                foreach (CTCBlock block in blocks)
                {
                    string where = block.Interlocking != null ? "interlocking " + block.Interlocking.id : block.Intermediate != null ? "intermediate" : "-";
                    sb.AppendLine("block " + block.id + "  [" + where + "]  thrownSwitchesSetOccupied=" + block.thrownSwitchesSetOccupied + "  occupied=" + block.IsOccupied);
                    foreach (TrackSpan span in block.GetComponentsInChildren<TrackSpan>(true))
                    {
                        sb.AppendLine("    span " + span.id + "  valid=" + span.IsValid + "  length=" + F(span.IsValid ? span.Length : float.NaN));
                        sb.AppendLine("        lower " + Describe(span.lower));
                        sb.AppendLine("        upper " + Describe(span.upper));
                    }
                }
                sb.AppendLine();

                sb.AppendLine("== Signals (" + signals.Length + ") ==");
                foreach (CTCSignal signal in signals)
                {
                    sb.AppendLine("signal " + signal.id + "  (" + signal.DisplayName + ", " + signal.GetType().Name + ", direction=" + signal.direction + ", heads=" + signal.headConfiguration + ")  aspect=" + signal.CurrentAspect);
                    Location? at = null;
                    if (Graph.Shared.TryGetLocationFromWorldPoint(signal.transform.position, 25f, out Location found)) at = found;
                    sb.AppendLine("    on track " + Describe(at));

                    var auto = signal as CTCAutoSignal;
                    if (auto == null || auto.blocks == null) continue;
                    foreach (CTCBlock block in auto.blocks)
                    {
                        if (block == null) continue;
                        sb.AppendLine("    protects block " + block.id);
                        foreach (TrackSpan span in block.GetComponentsInChildren<TrackSpan>(true))
                        {
                            sb.AppendLine("        span " + span.id + ":  lower " + Distance(at, span.lower) + ";  upper " + Distance(at, span.upper));
                        }
                    }
                }

                LogWriter.WriteTextFile("ctc_dump", sb.ToString());
            }
            catch (Exception e)
            {
                _done = true;
                Main.Mod.Logger.Warning("CTC dump failed: " + e.Message);
            }
        }

        private static string F(float v)
        {
            return float.IsNaN(v) ? "?" : v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Describe(Location? loc)
        {
            if (!loc.HasValue || !loc.Value.IsValid) return "(none)";
            Vector3 p = loc.Value.GetPosition();
            return Graph.LocationToString(loc.Value) + "  at (" + F(p.x) + ", " + F(p.y) + ", " + F(p.z) + ")";
        }

        /// <summary>Straight-line distance from the signal's track point to a span end, and the along-track distance when both lie on one segment.</summary>
        private static string Distance(Location? from, Location? to)
        {
            if (!from.HasValue || !to.HasValue || !from.Value.IsValid || !to.Value.IsValid) return "(unknown)";
            float line = Vector3.Distance(from.Value.GetPosition(), to.Value.GetPosition());
            string text = F(line) + " m straight";
            if (from.Value.segment == to.Value.segment)
            {
                float a = from.Value.WithEnd(TrackSegment.End.A).distance;
                float b = to.Value.WithEnd(TrackSegment.End.A).distance;
                text += ", " + F(b - a) + " m along the segment from the A end (signed: + is toward the B end)";
            }
            return text;
        }
    }
}
