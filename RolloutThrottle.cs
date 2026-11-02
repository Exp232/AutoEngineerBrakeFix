using System;
using System.Runtime.CompilerServices;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Throttle smoothing on the 0.6 rollout controller (it was on the old PID speed loop until 2.0, which 0.6.0
    /// replaced). The controller sets the lever to a notch once per tick (SpeedControlLoop.ApplyRolloutCommand);
    /// this runs the same low-pass filter and rate limit as before (ThrottleSmoother) on that notch, so the lever
    /// moves like a hand on the regulator instead of stepping, and the glider (ThrottleGlider) then eases the
    /// lever between ticks. A command that brakes, or any overspeed, closes the throttle at once.
    /// </summary>
    internal static class RolloutThrottle
    {
        private const float MpsToMph = 2.23694f;

        private static readonly ConditionalWeakTable<AutoEngineer, PidInfo> States = new ConditionalWeakTable<AutoEngineer, PidInfo>();

        /// <summary>This engineer's smoothing state, or null when it has had no command yet.</summary>
        internal static PidInfo Peek(AutoEngineer ae)
        {
            return ae != null && States.TryGetValue(ae, out PidInfo info) ? info : null;
        }

        private static PidInfo For(AutoEngineer ae)
        {
            return States.GetValue(ae, a => new PidInfo { Engineer = a, Kind = PidKind.Throttle });
        }

        /// <summary>Returns true when the command's throttle was changed.</summary>
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            if (!Main.Settings.SmoothThrottle) return false;
            var ae = BrakeTaps.FAe?.GetValue(loop) as AutoEngineer;
            if (ae == null || ae.Locomotive == null) return false;
            if (StallGuard.IsLatched(ae)) return false;

            PidInfo info = For(ae);

            // A command that brakes never keeps the throttle open: close it now and start the filter over.
            float applied = BrakeTaps.AppliedSet(loop);
            bool braking = command.IndependentBrake01 > 0.01f
                || (command.DrivesTrainBrake && !float.IsNaN(applied) && command.TrainBrakeSet > applied + 0.005f);
            if (braking && command.ThrottleNotch <= 0.001f)
            {
                info.HasLast = true;
                info.Last = 0f;
                info.LastTime = Time.time;
                info.Gliding = false;
                info.Floor = 0f;
                return false;
            }

            float speedMps = Mathf.Abs(ae.Locomotive.velocity);
            float targetMps = 0f;
            if (BrakeTaps.FContextualField != null)
            {
                targetMps = Mathf.Abs(Convert.ToSingle(BrakeTaps.FContextualField.GetValue(ae)));
            }
            float error = targetMps - speedMps;

            float result = command.ThrottleNotch;
            // No slam: the rollout controller lowers the lever one notch (0.125) a tick and brakes by itself when it needs to,
            // so a mild overspeed (a falling target on a steep grade) must not take the throttle to zero.
            ThrottleSmoother.Apply(info, error, ref result, false);
            if (Mathf.Abs(result - command.ThrottleNotch) < 0.0005f) return false;

            command = new RolloutCommand(command.Kind, result, command.IndependentBrake01, command.TrainBrakeSet, command.DrivesTrainBrake, command.BailOff, command.Cost, command.HoldCost);
            return true;
        }
    }
}
