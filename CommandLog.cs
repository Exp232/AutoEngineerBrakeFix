using System.Runtime.CompilerServices;
using Model;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Remembers the rollout controller's latest command as the game issued it and as it left this mod's filters, so the
    /// CSV can show who set the brakes and the throttle (game plan or mod). Logging only.
    /// </summary>
    internal static class CommandLog
    {
        internal sealed class Record
        {
            public string Kind = "";
            public float Thr, Ind, Tb;
            public bool Bail;
            public float OutThr, OutInd, OutTb;
            public bool OutBail;
            public string Mod = "";
            public float Time = float.NaN;
            public int Count;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, Record> Records = new ConditionalWeakTable<AutoEngineer, Record>();

        internal static Record Get(AutoEngineer ae) => ae != null && Records.TryGetValue(ae, out Record r) ? r : null;

        internal static void Note(AutoEngineer ae, in RolloutCommand before, in RolloutCommand after, bool tap, bool band, bool smooth = false)
        {
            if (ae == null) return;
            Record r = Records.GetOrCreateValue(ae);
            r.Kind = before.Kind.ToString();
            r.Thr = before.ThrottleNotch;
            r.Ind = before.IndependentBrake01;
            r.Tb = before.TrainBrakeSet;
            r.Bail = before.BailOff;
            r.OutThr = after.ThrottleNotch;
            r.OutInd = after.IndependentBrake01;
            r.OutTb = after.TrainBrakeSet;
            r.OutBail = after.BailOff;
            string mod = tap && band ? "tap+band" : tap ? "tap" : band ? "band" : "";
            r.Mod = smooth ? (mod.Length > 0 ? mod + "+smooth" : "smooth") : mod;
            r.Time = Time.fixedTime;
            r.Count++;
        }
    }
}
