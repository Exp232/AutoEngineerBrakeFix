using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Model.AI;
using Track.Signals;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Writes one line to logs\signals_*.csv whenever the signal an auto-engineer is tracking (Targets.NextSignal)
    /// changes, or the aspect that signal shows changes. This is what shows a signal dropping to Stop while a
    /// train is still approaching it: the line carries the train's speed and the distance to the signal at that
    /// moment. The clock column is the wall-clock time, so it lines up with Player.log. It is checked every
    /// physics tick and also while the train stands still (the main CSV skips standing time).
    /// </summary>
    internal static class SignalLog
    {
        private sealed class State
        {
            public string Id = "";
            public string Name = "";
            public string Aspect = "";
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();
        private static readonly object Lock = new object();
        private static StreamWriter _writer;
        private static bool _warned;

        private const string Header = "t,clock,loco,event,signalId,signalName,from,to,mph,sigDistM,nextTarget,targets";

        /// <summary>The tracked signal's id, name and shown aspect; empty strings when the AI has no signal in view.</summary>
        internal static void Read(AutoEngineer.Targets targets, out string id, out string name, out string aspect)
        {
            id = ""; name = ""; aspect = "";
            CTCSignal sig = targets != null ? targets.NextSignal : null;
            if (sig == null) return;
            id = sig.id ?? "";
            name = sig.DisplayName ?? "";
            aspect = HarmonyLib.AccessTools.Property(typeof(CTCSignal), "LastShownAspect")?.GetValue(sig, null)?.ToString() ?? "";   // internal getter
        }

        /// <summary>Metres from the train to the tracked signal, from the target list. A stop target sits 15 m short of its signal.</summary>
        internal static float DistanceToSignal(AutoEngineer.Targets targets)
        {
            if (targets == null || targets.AllTargets == null) return float.NaN;
            foreach (var t in targets.AllTargets)
            {
                if (t.Reason == "Stop Signal") return t.Distance + 15f;
            }
            foreach (var t in targets.AllTargets)
            {
                if (t.Reason == "Signal") return t.Distance;
            }
            return float.NaN;
        }

        internal static void Observe(AutoEngineer ae, AutoEngineer.Targets targets, float mph)
        {
            State st = States.GetOrCreateValue(ae);
            Read(targets, out string id, out string name, out string aspect);
            if (id == st.Id && aspect == st.Aspect) return;

            string kind;
            string shownName = name;
            string previous = st.Aspect;
            if (id != st.Id)
            {
                if (id == "") { kind = "passed"; shownName = st.Name; id = st.Id; }   // signal left view: report it by its old id
                else kind = st.Id == "" ? "seen" : "next";
            }
            else
            {
                kind = "aspect";
            }

            Write(ae, targets, kind, id, shownName, previous, aspect, mph);

            st.Id = kind == "passed" ? "" : id;
            st.Name = name;
            st.Aspect = aspect;
        }

        private static void Write(AutoEngineer ae, AutoEngineer.Targets targets, string kind, string id, string name, string previous, string current, float mph)
        {
            string first = "";
            if (targets != null && targets.AllTargets != null && targets.AllTargets.Count > 0)
            {
                var t = targets.AllTargets[0];
                first = t.SpeedMph.ToString("0", CultureInfo.InvariantCulture) + "@" + t.Distance.ToString("0", CultureInfo.InvariantCulture) + ":" + t.Reason;
            }
            float dist = DistanceToSignal(targets);

            string line = string.Join(",",
                Time.fixedTime.ToString("0.###", CultureInfo.InvariantCulture),
                DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                Csv(ae.Locomotive != null ? ae.Locomotive.DisplayName : ""),
                kind, Csv(id), Csv(name), previous, current,
                mph.ToString("0.#", CultureInfo.InvariantCulture),
                float.IsNaN(dist) ? "" : dist.ToString("0", CultureInfo.InvariantCulture),
                Csv(first), Csv(AutoEngineerSampler.TargetList(targets)));

            lock (Lock)
            {
                try
                {
                    if (_writer == null)
                    {
                        Directory.CreateDirectory(LogWriter.LogDir);
                        string path = Path.Combine(LogWriter.LogDir, $"signals_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                        _writer.WriteLine(Header);
                        Main.Mod.Logger.Log("Logging signal events to " + path);
                    }
                    _writer.WriteLine(line);
                }
                catch (IOException e)
                {
                    // Events are rare and only diagnostic; drop this one rather than hold it.
                    if (!_warned)
                    {
                        _warned = true;
                        Main.Mod.Logger.Warning("Signal log could not be written: " + e.Message);
                    }
                }
            }
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            return value.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        internal static void Close()
        {
            lock (Lock)
            {
                if (_writer != null)
                {
                    try { _writer.Dispose(); } catch (IOException) { }
                    _writer = null;
                }
            }
        }
    }
}
