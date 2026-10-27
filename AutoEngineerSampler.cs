using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Physics;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Samples one AutoEngineer's controls and PID state into the CSV log.
    /// Runs in FixedUpdate so timestamps line up with the game's own Time.fixedTime-based control loop.
    /// </summary>
    public class AutoEngineerSampler : MonoBehaviour
    {
        private const float MpsToMph = 2.23694f;

        // Private members of AutoEngineer, cached once.
        private static readonly FieldInfo FControl = AccessTools.Field(typeof(AutoEngineer), "_control");
        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");
        private static readonly FieldInfo FContextualTarget = AccessTools.Field(typeof(AutoEngineer), "_debugContextualTargetVelocity");
        private static readonly FieldInfo FThrottlePid = AccessTools.Field(typeof(AutoEngineer), "throttleController");
        private static readonly FieldInfo FIndependentPid = AccessTools.Field(typeof(AutoEngineer), "independentController");
        private static readonly FieldInfo FTrainBrakePid = AccessTools.Field(typeof(AutoEngineer), "trainBrakeController");
        private static readonly MethodInfo MShouldUseLocoBrake = AccessTools.Method(typeof(AutoEngineer), "ShouldUseLocomotiveBrake");
        private static readonly MethodInfo MLookahead = AccessTools.Method(typeof(AutoEngineer), "CalculateLookaheadDistance");
        private static readonly MethodInfo MAvgTrainCylinder = AccessTools.Method(typeof(AutoEngineer), "AverageTrainBrakeCylinder");

        // PIDController's last output is private; the public API only exposes error and integrator terms.
        // Note this is the PID's own output, before the brake fix adjusts the value returned to the AI.
        private static readonly FieldInfo FPidPreviousControl = AccessTools.Field(typeof(PIDController), "_previousControl");

        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private AutoEngineer _ae;
        private float _nextSampleTime;
        private bool _loggedError;

        // Fuel and water change slowly, so they are re-read about once a second and reused in between.
        private FuelReader.Reading _fuel = FuelReader.Reading.Empty();
        private float _nextFuelTime;
        private bool _loggedFuelError;

        private void Awake()
        {
            _ae = GetComponent<AutoEngineer>();
        }

        private void FixedUpdate()
        {
            if (!Main.Active || !Main.Settings.LogEnabled || _ae == null) return;
            if (!_ae.isActiveAndEnabled || !_ae.Run) return;
            if (Time.fixedTime < _nextSampleTime) return;

            _nextSampleTime = Time.fixedTime + Mathf.Max(0.05f, Main.Settings.SampleIntervalSeconds);

            try
            {
                Sample();
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    Main.Mod.Logger.Log("Sampler error (further errors suppressed for this engineer):");
                    Main.Mod.Logger.LogException(e);
                }
            }
        }

        private float LookaheadMeters()
        {
            try { return MLookahead != null ? Convert.ToSingle(MLookahead.Invoke(_ae, null)) : float.NaN; }
            catch { return float.NaN; }
        }

        /// <summary>Every target the AI currently knows about, as "speed@distance:reason" joined with ';'.</summary>
        private static string TargetList(AutoEngineer.Targets targets)
        {
            if (targets == null || targets.AllTargets == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var t in targets.AllTargets)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(t.SpeedMph.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append('@')
                  .Append(t.Distance.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(t.Reason);
            }
            return sb.ToString();
        }

        private void Sample()
        {
            var loco = _ae.Locomotive;
            if (loco == null) return;
            if (Mathf.Abs(loco.velocity) < 0.01f) return; // don't fill the log while standing still

            object control = FControl?.GetValue(_ae);
            var targets = FTargets?.GetValue(_ae) as AutoEngineer.Targets;

            float nextMph = float.NaN, nextDist = float.NaN;
            string nextReason = "";
            if (targets != null && targets.AllTargets.Count > 0)
            {
                var t = targets.AllTargets[0];
                nextMph = t.SpeedMph;
                nextDist = t.Distance;
                nextReason = t.Reason;
            }

            float throttle = ReadFloat(control, "Throttle");
            float trainBrake = ReadFloat(control, "TrainBrake");
            float locoBrake = ReadFloat(control, "LocomotiveBrake");
            bool overlap = throttle > 0.01f && (trainBrake > 0.01f || locoBrake > 0.01f);
            FuelReader.Reading fuel = ReadFuel(loco);

            var row = new RowBuilder()
                .Add("t", Time.fixedTime)
                .Add("loco", loco.DisplayName)
                .Add("cars", SafeCount(loco))
                .Add("mph", loco.velocity * MpsToMph)
                .Add("ctxTargetMph", ReadFloat(_ae, FContextualTarget) * MpsToMph)
                .Add("maxMph", targets != null ? targets.MaxSpeedMph : float.NaN)
                .Add("nextTargetMph", nextMph)
                .Add("nextTargetDistM", nextDist)
                .Add("nextTargetReason", nextReason)
                .Add("lookaheadM", LookaheadMeters())
                .Add("targets", TargetList(targets))
                .Add("gradeUnderPct", targets != null ? targets.AverageGradeUnder : float.NaN)
                .Add("gradeAheadPct", targets != null ? targets.AverageGradeAhead : float.NaN)
                .Add("gravityKN", _ae.SumGravityForce() / 1000f)
                .Add("throttle", throttle)
                .Add("reverser", ReadFloat(control, "Reverser"))
                .Add("trainBrake", trainBrake)
                .Add("trainBrakePsi", ReadFloat(control, "TrainBrakeSet"))
                .Add("locoBrake", locoBrake)
                .Add("brakePath", BrakePath())
                .Add("locoCylPsi", SafeLocoCylinder(loco))
                .Add("carCylAvgPsi", SafeCarCylinderAverage())
                .Add("overlap", overlap ? 1 : 0)
                .Add("fixBlocked", BrakeFix.GetBlockedApplies(_ae))
                .Add("fixReleased", BrakeFix.GetForcedReleases(_ae))
                .Add("fuelId", fuel.FuelId)
                .Add("fuelQty", fuel.Fuel)
                .Add("waterQty", fuel.Water)
                .Add("tenders", fuel.Tenders)
                .Add("teLbf", SteamValue(loco, 0))
                .Add("waterGps", SteamValue(loco, 1))
                .Add("coalLbs", SteamValue(loco, 2));

            AddPid(row, "thr", FThrottlePid?.GetValue(_ae) as PIDController);
            AddPid(row, "ind", FIndependentPid?.GetValue(_ae) as PIDController);

            var trainPid = FTrainBrakePid?.GetValue(_ae) as PIDController;
            AddPid(row, "tb", trainPid);
            row.Add("tb_dGain", trainPid != null ? trainPid.derivativeGain : float.NaN);

            LogWriter.Write(row);
        }

        /// <summary>Steam locomotives only: 0 = tractive effort (lbf), 1 = water use (gal/s), 2 = coal use (lb/s). NaN otherwise.</summary>
        private static float SteamValue(BaseLocomotive loco, int which)
        {
            var steam = loco as SteamLocomotive;
            if (steam == null || steam.engine == null) return float.NaN;
            switch (which)
            {
                case 0: return steam.engine.tractiveEffort;
                case 1: return steam.engine.WaterConsumptionRate;
                default: return steam.engine.CoalConsumptionRate;
            }
        }

        private FuelReader.Reading ReadFuel(Model.BaseLocomotive loco)
        {
            if (!Main.Settings.LogFuel || Time.fixedTime < _nextFuelTime) return _fuel;
            _nextFuelTime = Time.fixedTime + 1f;

            try
            {
                _fuel = FuelReader.Read(loco);
            }
            catch (Exception e)
            {
                if (!_loggedFuelError)
                {
                    _loggedFuelError = true;
                    Main.Mod.Logger.Log("Fuel read error (further errors suppressed for this engineer):");
                    Main.Mod.Logger.LogException(e);
                }
            }

            return _fuel;
        }

        /// <summary>
        /// Note: only one of the two brake PIDs is updated on any given tick (which one depends on
        /// brakePath), so the other one's columns hold stale values from when it last ran.
        /// </summary>
        private static void AddPid(RowBuilder row, string prefix, PIDController pid)
        {
            if (pid == null)
            {
                row.Add(prefix + "_err", float.NaN).Add(prefix + "_out", float.NaN).Add(prefix + "_int", float.NaN)
                   .Add(prefix + "_iTerm", float.NaN).Add(prefix + "_dTerm", float.NaN);
                return;
            }

            row.Add(prefix + "_err", pid.PreviousError)
               .Add(prefix + "_out", ReadFloat(pid, FPidPreviousControl))
               .Add(prefix + "_int", pid.Integrator)
               .Add(prefix + "_iTerm", pid.DebugIntegrator)
               .Add(prefix + "_dTerm", pid.DebugDerivative);
        }

        private string BrakePath()
        {
            try
            {
                return MShouldUseLocoBrake != null && (bool)MShouldUseLocoBrake.Invoke(_ae, null) ? "loco" : "train";
            }
            catch
            {
                return "?";
            }
        }

        private float SafeCarCylinderAverage()
        {
            try
            {
                return MAvgTrainCylinder != null ? (float)MAvgTrainCylinder.Invoke(_ae, null) : float.NaN;
            }
            catch
            {
                return float.NaN;
            }
        }

        private static float SafeLocoCylinder(Model.BaseLocomotive loco)
        {
            try
            {
                return loco.air.BrakeCylinder.Pressure;
            }
            catch
            {
                return float.NaN;
            }
        }

        private static int SafeCount(Model.BaseLocomotive loco)
        {
            try
            {
                return loco.EnumerateCoupled().Count();
            }
            catch
            {
                return -1;
            }
        }

        private static float ReadFloat(object target, FieldInfo field)
        {
            if (target == null || field == null) return float.NaN;
            object value = field.GetValue(target);
            return value == null ? float.NaN : Convert.ToSingle(value);
        }

        /// <summary>Reads a float property or field by name, so we don't depend on the helper's namespace.</summary>
        private static float ReadFloat(object target, string name)
        {
            if (target == null) return float.NaN;
            Type type = target.GetType();

            PropertyInfo property = type.GetProperty(name, Members);
            if (property != null)
            {
                return Convert.ToSingle(property.GetValue(target, null));
            }

            FieldInfo field = type.GetField(name, Members);
            if (field != null)
            {
                return Convert.ToSingle(field.GetValue(target));
            }

            return float.NaN;
        }
    }
}
