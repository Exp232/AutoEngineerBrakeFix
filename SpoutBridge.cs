using System;
using System.Reflection;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// The link to College-Educated Spouts, an optional companion mod (exp232/collegeeducatedspouts): its spouts turn to the
    /// tender hatch, so the water stop should count a hatch the turning spout can reach as in reach. Found by reflection,
    /// so this mod neither needs it nor references it; without it every answer here is "no".
    /// </summary>
    internal static class SpoutBridge
    {
        private static Func<RollingStock.CarLoadTargetLoader, Model.Car, int, bool> _canReach;
        private static Func<string> _note;
        private static float _nextLook;

        /// <summary>True when College-Educated Spouts is loaded and says the column's spout can turn to this tender's hatch. 'how' describes it for the log.</summary>
        internal static bool CanReach(RollingStock.CarLoadTargetLoader loader, Model.Car tender, int slot, out string how)
        {
            how = null;
            try
            {
                if (loader == null || tender == null) return false;
                if (_canReach == null)
                {
                    if (Time.unscaledTime < _nextLook) return false;
                    _nextLook = Time.unscaledTime + 30f;
                    Look();
                    if (_canReach == null) return false;
                }
                bool ok = _canReach(loader, tender, slot);
                how = _note();
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Look()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "CollegeEducatedSpouts") continue;
                Type api = a.GetType("CollegeEducatedSpouts.SpoutApi");
                MethodInfo can = api?.GetMethod("CanReach", new[] { typeof(RollingStock.CarLoadTargetLoader), typeof(Model.Car), typeof(int) });
                FieldInfo note = api?.GetField("LastNote");
                if (can == null || can.ReturnType != typeof(bool) || note == null) return;
                _canReach = (Func<RollingStock.CarLoadTargetLoader, Model.Car, int, bool>)Delegate.CreateDelegate(typeof(Func<RollingStock.CarLoadTargetLoader, Model.Car, int, bool>), can);
                _note = () => note.GetValue(null) as string;
                Main.Mod.Logger.Log("College-Educated Spouts found: the water stop counts a hatch its spouts can turn to as in reach.");
                return;
            }
        }
    }
}
