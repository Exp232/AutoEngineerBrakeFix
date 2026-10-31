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
    ///  - branches, yards and spurs (no CTC): the limit stays (no boards: 2.5.5's boards were too many on sidings).
    /// Speed limits are changed in memory only and put back when the setting is switched off or the mod unloads.
    /// </summary>
    public class LegacySpeedLimits : MonoBehaviour
    {
        internal const string SignPrefix = "aebf-legacy-speed:";
        private const float CheckEverySeconds = 5f;

        private Graph _graph;
        private int _segmentCount;
        private bool _applied;
        private bool _swept;
        private float _next;
        private readonly Dictionary<TrackSegment, int> _cleared = new Dictionary<TrackSegment, int>();

        private void Update()
        {
            if (!Main.Active || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + CheckEverySeconds;
            try
            {
                Graph graph = Graph.Shared;
                if (!_swept && graph != null && StateManager.IsHost && SignController.Shared != null)
                {
                    // 2.5.5 put up speed boards; take any it left in a save down (once per load, on or off).
                    SignController.Shared.RemoveSignsWithPrefix(SignPrefix);
                    _swept = true;
                }
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

                        _graph = graph;
            _segmentCount = count;
            _applied = true;
            int kept = graph.AllSegments.Count(s => s.speedLimit > 0);
            Main.Mod.Logger.Log($"Legacy speed limits: cleared on {_cleared.Count} mainline segments ({mainline.Count} segments in CTC blocks); {kept} branch, yard and spur segments keep theirs (no boards).");
        }

        private void Restore(string why)
        {
            foreach (KeyValuePair<TrackSegment, int> kv in _cleared)
            {
                if (kv.Key != null) kv.Key.speedLimit = kv.Value;
            }
            int restored = _cleared.Count;
            _cleared.Clear();
            _applied = false;
            Main.Mod.Logger.Log($"Legacy speed limits: {restored} mainline limits put back ({why}).");
        }
    }
}
