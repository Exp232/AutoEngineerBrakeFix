using System;
using System.Collections.Generic;
using HarmonyLib;
using Track.Signals;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Temporary workaround for map data where a signal protects a block that lies behind it. A signal shows Stop
    /// whenever a block in its protected-block list (CTCAutoSignal.blocks) is occupied, and a block is occupied when
    /// any car is on one of its spans. The Sylva West eastbound signal (sy-w-eb) lists block sy-w, whose span lies
    /// 12.9 to 50.6 m short of the signal, so the train it is approaching trips it about 45 m before the mast and the
    /// engineer's emergency stop fires. Every other interlocking signal protects a block that starts a few metres in
    /// front of it and runs ahead.
    ///
    /// Setting CtcDropBlocks is a list of "signalId:blockId" pairs. For each pair present on the map, the block is
    /// removed from that signal's list. Nothing in the saved game or the map files changes; it is applied again each
    /// time a map is loaded, and removing the setting restores the game's behaviour.
    /// </summary>
    internal static class CtcFix
    {
        private static readonly System.Reflection.MethodInfo Refresh = AccessTools.Method(typeof(CTCSignal), "SetNeedsUpdate");

        internal static void Apply()
        {
            string spec = Main.Settings.CtcDropBlocks;
            if (string.IsNullOrWhiteSpace(spec)) return;   // not tied to FixEnabled: the self-check switches that off on 0.6.x

            var pairs = new List<KeyValuePair<string, string>>();
            foreach (string item in spec.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = item.IndexOf(':');
                if (colon <= 0 || colon == item.Length - 1) continue;
                pairs.Add(new KeyValuePair<string, string>(item.Substring(0, colon).Trim(), item.Substring(colon + 1).Trim()));
            }
            if (pairs.Count == 0) return;

            foreach (CTCAutoSignal signal in UnityEngine.Object.FindObjectsByType<CTCAutoSignal>(FindObjectsSortMode.None))
            {
                if (signal.blocks == null) continue;
                foreach (var pair in pairs)
                {
                    if (signal.id != pair.Key) continue;
                    int removed = signal.blocks.RemoveAll(b => b != null && b.id == pair.Value);
                    if (removed == 0) continue;
                    Main.Mod.Logger.Log("CTC fix: block " + pair.Value + " removed from signal " + pair.Key + " (its span lies behind the signal).");
                    try { Refresh?.Invoke(signal, null); }
                    catch (Exception e) { Main.Mod.Logger.Warning("CTC fix: could not refresh " + pair.Key + ": " + e.Message); }
                }
            }
        }
    }
}
