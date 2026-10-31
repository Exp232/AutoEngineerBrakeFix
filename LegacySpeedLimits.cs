using System;
using System.Collections.Generic;
using System.Linq;
using Game.Mow;
using Game.State;
using Track;
using Track.Signals;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Legacy track speed limits (setting LegacySpeedLimits, off by default). In 0.6 the game removes the old speed
    /// boards ("speed:" signs, TrackConditionController) but every TrackSegment.speedLimit from the map still caps the
    /// AI's track speed, so trains slow for limits nobody can see (forum topic 306). With this on:
    ///  - mainline: the legacy limit is cleared on every segment inside a CTC signal block (main tracks and signalled
    ///    sidings), so the railroad limit, curves and track class decide the speed there;
    ///  - branches, yards and spurs (no CTC): the limit stays and a speed board ("tsr" style, the game's own board for
    ///    player speed signs) is put up at each end of every limited stretch, facing trains entering it.
    /// Speed limits are changed in memory only and put back when the setting is switched off or the mod unloads.
    /// The boards live in the game's sign store under this mod's own key prefix (host only) and are rebuilt each load.
    /// </summary>
    public class LegacySpeedLimits : MonoBehaviour
    {
        internal const string SignPrefix = "aebf-legacy-speed:";
        private const float CheckEverySeconds = 5f;
        private const float BoardInsetMeters = 6f;
        private static readonly Color BoardColor = new Color(0.91f, 0.75f, 0.19f);   // PlayerSign.SpeedColor

        private Graph _graph;
        private int _segmentCount;
        private bool _applied;
        private float _next;
        private readonly Dictionary<TrackSegment, int> _cleared = new Dictionary<TrackSegment, int>();

        private void Update()
        {
            if (!Main.Active || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + CheckEverySeconds;
            try
            {
                Graph graph = Graph.Shared;
                bool want = Main.Settings.LegacySpeedLimits && graph != null;
                if (!want)
                {
                    if (_applied) Restore("switched off");
                    return;
                }
                int count = graph.AllSegments.Count();
                if (_applied && graph == _graph && count == _segmentCount) return;
                if (_applied) Restore("track changed");
                Apply(graph, count);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Legacy speed limits failed: " + e.Message);
                _next = Time.unscaledTime + 60f;
            }
        }

        private void OnDestroy()
        {
            try { if (_applied) Restore("mod unloaded"); }
            catch (Exception) { }
        }

        private void Apply(Graph graph, int count)
        {
            CTCBlock[] blocks = FindObjectsByType<CTCBlock>(FindObjectsSortMode.None);
            if (blocks.Length == 0) return;   // CTC not loaded yet; try again shortly

            var mainline = new HashSet<TrackSegment>();
            foreach (CTCBlock block in blocks)
            {
                if (block == null) continue;
                foreach (TrackSpan span in block.GetComponentsInChildren<TrackSpan>(true))
                {
                    if (span == null || !span.IsValid) continue;
                    foreach (TrackSegment seg in span.GetSegments()) mainline.Add(seg);
                }
            }

            _cleared.Clear();
            foreach (TrackSegment seg in graph.AllSegments)
            {
                if (seg.speedLimit <= 0 || !mainline.Contains(seg)) continue;
                _cleared[seg] = seg.speedLimit;
                seg.speedLimit = 0;
            }

            int boards = StateManager.IsHost ? PlaceBoards(graph, mainline) : 0;
            _graph = graph;
            _segmentCount = count;
            _applied = true;
            int kept = graph.AllSegments.Count(s => s.speedLimit > 0);
            Main.Mod.Logger.Log($"Legacy speed limits: cleared on {_cleared.Count} mainline segments ({mainline.Count} segments in CTC blocks); {kept} branch, yard and spur segments keep theirs, {boards} boards put up.");
        }

        private void Restore(string why)
        {
            foreach (KeyValuePair<TrackSegment, int> kv in _cleared)
            {
                if (kv.Key != null) kv.Key.speedLimit = kv.Value;
            }
            int restored = _cleared.Count;
            _cleared.Clear();
            if (StateManager.IsHost && SignController.Shared != null) SignController.Shared.RemoveSignsWithPrefix(SignPrefix);
            _applied = false;
            Main.Mod.Logger.Log($"Legacy speed limits: {restored} mainline limits put back and boards removed ({why}).");
        }

        /// <summary>
        /// One board at each end of every limited stretch off the mainline: at a segment end whose neighbours there
        /// (other than itself) do not continue the same limit off the mainline. Rebuilt from scratch each time.
        /// </summary>
        private static int PlaceBoards(Graph graph, HashSet<TrackSegment> mainline)
        {
            SignController signs = SignController.Shared;
            if (signs == null) return 0;
            signs.RemoveSignsWithPrefix(SignPrefix);
            int placed = 0;
            using (StateManager.TransactionScope())
            {
                foreach (TrackSegment seg in graph.AllSegments)
                {
                    if (seg.speedLimit <= 0 || mainline.Contains(seg)) continue;
                    foreach (TrackSegment.End end in new[] { TrackSegment.End.A, TrackSegment.End.B })
                    {
                        TrackNode node = end == TrackSegment.End.A ? seg.a : seg.b;
                        if (node == null) continue;
                        bool continues = graph.SegmentsConnectedTo(node).Any(o => o != seg && o.speedLimit == seg.speedLimit && !mainline.Contains(o));
                        if (continues) continue;

                        float length = seg.GetLength();
                        float inset = Mathf.Min(BoardInsetMeters, length * 0.5f);
                        Location at = new Location(seg, inset, end);
                        Location deeper = new Location(seg, Mathf.Min(length, inset + 5f), end);
                        Location facing = graph.LocationOrientedToward(at, deeper);   // the way a train entering travels
                        if (!facing.IsValid) continue;
                        Graph.PositionRotation pr = graph.GetPositionRotation(facing);
                        PlayerSign.LinesidePose(pr.Position, pr.Rotation, out Vector3 position, out Quaternion rotation);
                        string text = seg.speedLimit.ToString();
                        signs.AddSign($"{SignPrefix}{seg.id}:{end}", new SignDescriptor(position, rotation, "tsr", text, BoardColor,
                            $"Speed limit {text} mph", "Legacy track speed limit from the map data, which the auto engineer obeys. Board put up by AutoEngineerBrakeFix."));
                        placed++;
                    }
                }
            }
            return placed;
        }
    }
}
