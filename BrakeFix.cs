using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model.AI;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    internal enum PidKind
    {
        Independent,
        TrainBrake,
        Throttle
    }

    internal sealed class EngineerInfo
    {
        /// <summary>Times the fix stopped a new brake application the game's PID wanted to make.</summary>
        public int BlockedApplies;

        /// <summary>Times the fix released a brake the game's PID would have left applied.</summary>
        public int ForcedReleases;

        /// <summary>The throttle PID's registration, used by the glider.</summary>
        public PidInfo ThrottlePid;
    }

    internal sealed class PidInfo
    {
        public AutoEngineer Engineer;
        public EngineerInfo Info;
        public PidKind Kind;

        // Throttle smoothing state (only used for PidKind.Throttle).
        public bool HasLast;
        public float Last;
        public float LastTime;

        // Hold-up after the start-up routine hands over (see ThrottleSmoother).
        public float Floor;
        public float FloorStart;

        // Gliding between AI ticks (see ThrottleGlider).
        public bool Gliding;
        public float Shown;
    }

    /// <summary>
    /// The fix. In road mode the auto-engineer regulates speed with a throttle PID and a brake PID that are
    /// centered on the same setpoint, with no gap between them. Logs from a loaded train showed the train brake
    /// being applied by the PID's derivative term as the speed error closed toward zero (even at full throttle),
    /// and then staying on for minutes because it only releases when the PID output drops below
    /// trainBrakeReleaseBelowOutput, which a train holding speed never does.
    ///
    /// The rule: while the throttle is open and speed is not above the padded setpoint by more than a margin,
    /// the brake PID's output is replaced with a release value. Real overspeed (error beyond the margin) is
    /// left to the game's own logic, and nothing changes while the throttle is closed (descents, stops).
    ///
    /// We never touch MaintainSpeed() itself (a coroutine); we only adjust the return value of
    /// PIDController.Compute for the two brake PIDs that belong to a registered AutoEngineer.
    /// </summary>
    internal static class BrakeFix
    {
        private const float MpsPerMph = 0.44703928f; // same constant the game uses
        private const float ThrottleOpenThreshold = 0.05f;
        private const float ApplyThreshold = 0.01f;  // the game applies the train brake above this PID output
        private const float BrakeOnThreshold = 0.001f;
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo FControl = AccessTools.Field(typeof(AutoEngineer), "_control");
        private static readonly FieldInfo FConfig = AccessTools.Field(typeof(AutoEngineer), "_config");
        private static readonly FieldInfo FIndependentPid = AccessTools.Field(typeof(AutoEngineer), "independentController");
        private static readonly FieldInfo FThrottlePid = AccessTools.Field(typeof(AutoEngineer), "throttleController");
        private static readonly FieldInfo FTrainBrakePid = AccessTools.Field(typeof(AutoEngineer), "trainBrakeController");

        private static readonly ConditionalWeakTable<PIDController, PidInfo> Pids =
            new ConditionalWeakTable<PIDController, PidInfo>();

        private static readonly ConditionalWeakTable<AutoEngineer, EngineerInfo> Engineers =
            new ConditionalWeakTable<AutoEngineer, EngineerInfo>();

        // The control helper's Throttle/TrainBrake/LocomotiveBrake may be properties or fields; resolve once per name.
        private sealed class Accessor
        {
            public PropertyInfo Property;
            public FieldInfo Field;
        }

        private static readonly Dictionary<string, Accessor> ControlAccessors = new Dictionary<string, Accessor>();

        private static bool _loggedError;

        /// <summary>Called from the AutoEngineer.OnEnable postfix, once per enable.</summary>
        internal static void Register(AutoEngineer ae)
        {
            EngineerInfo info = Engineers.GetOrCreateValue(ae);
            StallGuard.Reset(ae);
            RegisterPid(ae, info, FIndependentPid, PidKind.Independent);
            RegisterPid(ae, info, FTrainBrakePid, PidKind.TrainBrake);
            RegisterPid(ae, info, FThrottlePid, PidKind.Throttle);
        }

        private static void RegisterPid(AutoEngineer ae, EngineerInfo info, FieldInfo field, PidKind kind)
        {
            var pid = field?.GetValue(ae) as PIDController;
            if (pid == null) return;

            Pids.Remove(pid); // ConditionalWeakTable on .NET Framework has no AddOrUpdate
            var pidInfo = new PidInfo { Engineer = ae, Info = info, Kind = kind };
            Pids.Add(pid, pidInfo);
            if (kind == PidKind.Throttle) info.ThrottlePid = pidInfo;
        }

        internal static int GetBlockedApplies(AutoEngineer ae)
        {
            return Engineers.TryGetValue(ae, out EngineerInfo info) ? info.BlockedApplies : 0;
        }

        internal static int GetForcedReleases(AutoEngineer ae)
        {
            return Engineers.TryGetValue(ae, out EngineerInfo info) ? info.ForcedReleases : 0;
        }

        /// <summary>Postfix body for PIDController.Compute. 'error' is what the PID was given; 'result' its output.</summary>
        internal static void Apply(PIDController pid, float error, ref float result)
        {
            try
            {
                if (!Main.Active) return;
                if (float.IsNaN(error)) return;
                if (!Pids.TryGetValue(pid, out PidInfo owner)) return;

                if (owner.Kind == PidKind.Throttle)
                {
                    if (StallGuard.Check(owner.Engineer, ref result)) { owner.Gliding = false; owner.Last = 0f; owner.HasLast = false; return; }
                    DecelHold.Apply(owner.Engineer, ref result);
                    ThrottleSmoother.Apply(owner, error, ref result);
                    return;
                }

                if (StallGuard.IsLatched(owner.Engineer)) { result = owner.Kind == PidKind.TrainBrake ? StallGuard.BrakeLevel(owner.Engineer) : 1f; return; }
                {
                    var cfg = FConfig?.GetValue(owner.Engineer) as AutoEngineerConfig;
                    if (owner.Kind == PidKind.TrainBrake) DecelHold.HoldBrake(owner.Engineer, cfg, ref result);
                    DecelHold.HoldCoast(owner.Engineer, cfg, error, owner.Kind == PidKind.TrainBrake, ref result);
                }

                if (!Main.Settings.FixEnabled) return;
                if (owner.Kind == PidKind.Independent && !Main.Settings.FixLocomotiveBrake) return;

                // Throttle is set earlier in the same tick, so this is the value the AI just chose.
                if (ReadControl(owner.Engineer, "Throttle") <= ThrottleOpenThreshold) return;

                var config = FConfig?.GetValue(owner.Engineer) as AutoEngineerConfig;

                // The AI squares (brakeErrorPower) the overspeed before feeding the brake PID, and the error is
                // negative when we are too fast. Convert the margin into the same units.
                float power = config != null ? config.brakeErrorPower : 1f;
                float marginMps = Mathf.Max(0f, Main.Settings.OverspeedMarginMph) * MpsPerMph;
                float limit = -Mathf.Pow(marginMps, power);
                if (error < limit) return; // genuinely overspeeding: leave the brake logic alone

                if (owner.Kind == PidKind.TrainBrake)
                {
                    // The caller releases the train brake when output < trainBrakeReleaseBelowOutput, applies it when
                    // output > 0.01 (and above the current setting), and otherwise leaves it exactly as it is.
                    float releaseBelow = config != null ? config.trainBrakeReleaseBelowOutput : -0.01f;
                    if (result < releaseBelow) return; // already a release

                    bool brakeIsOn = ReadControl(owner.Engineer, "TrainBrake") > BrakeOnThreshold;
                    if (brakeIsOn)
                    {
                        // The game would have left this brake applied (or applied it harder).
                        result = releaseBelow - 0.01f;
                        owner.Info.ForcedReleases++;
                    }
                    else if (result > ApplyThreshold)
                    {
                        // The game would have made a new application.
                        result = releaseBelow - 0.01f;
                        owner.Info.BlockedApplies++;
                    }
                    // else: brake is off and the PID is not asking for one, so the game does nothing. Leave it.
                }
                else if (result > 0.0005f)
                {
                    // Locomotive brake: output is applied directly, so zero means released.
                    bool brakeIsOn = ReadControl(owner.Engineer, "LocomotiveBrake") > BrakeOnThreshold;
                    result = 0f;
                    if (brakeIsOn) owner.Info.ForcedReleases++;
                    else owner.Info.BlockedApplies++;
                }
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    Main.Mod.Logger.Log("BrakeFix error (further errors suppressed):");
                    Main.Mod.Logger.LogException(e);
                }
            }
        }

        internal static AutoEngineerConfig GetConfig(AutoEngineer ae)
        {
            return FConfig?.GetValue(ae) as AutoEngineerConfig;
        }

        internal static PidInfo ThrottleInfo(AutoEngineer ae)
        {
            return Engineers.TryGetValue(ae, out EngineerInfo info) ? info.ThrottlePid : null;
        }

        /// <summary>True if the train brake or the locomotive brake is currently applied.</summary>
        internal static bool ReadBrakeRequested(AutoEngineer ae)
        {
            return ReadControl(ae, "TrainBrake") > BrakeOnThreshold || ReadControl(ae, "LocomotiveBrake") > BrakeOnThreshold;
        }

        internal static float ReadThrottle(AutoEngineer ae)
        {
            return ReadControl(ae, "Throttle");
        }

        internal static void WriteThrottle(AutoEngineer ae, float value)
        {
            object control = FControl?.GetValue(ae);
            if (control == null) return;
            ReadControl(ae, "Throttle"); // makes sure the accessor is cached
            if (!ControlAccessors.TryGetValue("Throttle", out Accessor accessor)) return;
            if (accessor.Property != null) accessor.Property.SetValue(control, value, null);
            else if (accessor.Field != null) accessor.Field.SetValue(control, value);
        }

        internal static void WriteControl(AutoEngineer ae, string name, float value)
        {
            object control = FControl?.GetValue(ae);
            if (control == null) return;
            Type type = control.GetType();
            var prop = type.GetProperty(name, Members);
            if (prop != null && prop.CanWrite) { prop.SetValue(control, value, null); return; }
            type.GetField(name, Members)?.SetValue(control, value);
        }

        internal static float ReadControl(AutoEngineer ae, string name)
        {
            object control = FControl?.GetValue(ae);
            if (control == null) return 0f;

            if (!ControlAccessors.TryGetValue(name, out Accessor accessor))
            {
                Type type = control.GetType();
                accessor = new Accessor { Property = type.GetProperty(name, Members) };
                if (accessor.Property == null)
                {
                    accessor.Field = type.GetField(name, Members);
                }
                ControlAccessors[name] = accessor;
            }

            if (accessor.Property != null) return Convert.ToSingle(accessor.Property.GetValue(control, null));
            if (accessor.Field != null) return Convert.ToSingle(accessor.Field.GetValue(control));
            return 0f;
        }
    }

    [HarmonyPatch(typeof(PIDController), nameof(PIDController.Compute))]
    internal static class PIDController_Compute_Patch
    {
        // Parameter names must match the original: Compute(float error, float dt).
        private static void Postfix(PIDController __instance, float error, ref float __result)
        {
            BrakeFix.Apply(__instance, error, ref __result);
        }
    }
}
