using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Smooths the throttle PID's output before the auto-engineer applies it. Logs show the throttle flipping
    /// 0 -> 0.25 -> 0 -> 0.25 on consecutive 0.5 s ticks while holding speed: the speed error is multiplied by 10
    /// whenever it is positive, so each time it crosses zero the derivative term spikes, hits the PID's 0.25 step
    /// limit, and the next tick spikes the other way. A low-pass filter plus a rate limit turns that into a gentle
    /// average and makes ordinary ramps look like a hand on the regulator.
    ///
    /// Real overspeed (past the same margin the brake fix uses, so the throttle is never held open while the brake
    /// is allowed to apply) bypasses the filter so the throttle closes at once.
    /// </summary>
    internal static class ThrottleSmoother
    {
        private const float MaxGapSeconds = 3f;          // longer than this between calls: start over
        private const float ArrivalOvershoot = 0.06f;
        private const float SnapBand = 0.015f;
        private const float HoldMaxSeconds = 15f;   // give up holding the hand-over value after this long
        private const float MpsPerMph = 0.44703928f;
        private const float RiseLimitPerSecond = 0.2f;   // 0 -> full in 5 s
        private const float FallLimitPerSecond = 0.4f;   // full -> 0 in 2.5 s

        internal static void Apply(PidInfo owner, float error, ref float result)
        {
            float tau = Main.Settings.ThrottleSmoothingSeconds;
            float now = Time.time;

            if (!Main.Settings.SmoothThrottle || tau <= 0.01f)
            {
                owner.HasLast = false;
                owner.Gliding = false;
                return;
            }

            float dt = now - owner.LastTime;
            owner.LastTime = now;

            // First call, a pause, or a long gap (for example the AI's start-up routine, which holds the throttle at
            // 50% before handing over): start from where the lever actually is so the handover is continuous,
            // then filter toward the PID's value like any other tick.
            if (!owner.HasLast || dt <= 0f || dt > MaxGapSeconds)
            {
                owner.HasLast = true;
                owner.Last = Mathf.Clamp01(BrakeFix.ReadThrottle(owner.Engineer));
                owner.Gliding = false;
                dt = 0.5f; // one AI tick

                // The start-up routine leaves the throttle at 50% while the speed controller's first outputs are
                // lower (25%) and then climb. Rather than dip, hold the lever at its hand-over value until the
                // controller catches up.
                owner.Floor = (owner.Last > 0.05f && error > 0f && result < owner.Last) ? owner.Last : 0f;
                owner.FloorStart = now;
            }

            // Genuine overspeed: let the throttle close immediately.
            float bypass = Mathf.Max(0.05f, Main.Settings.OverspeedMarginMph) * MpsPerMph;
            if (error < -bypass && result < owner.Last)
            {
                // The AI's own PID only lets the throttle fall 0.25 per tick, so it would step down over about two
                // seconds while the brake is already applying. Shut it at once when the brake is on or the train is
                // clearly over the setpoint (mild overspeed with no brake yet keeps the AI's own gradual closing).
                bool brakeOn = BrakeFix.ReadBrakeRequested(owner.Engineer);
                if (Main.Settings.SlamThrottleOnOverspeed && (brakeOn || error < -2f * bypass))
                {
                    result = 0f;
                }
                owner.Last = result;
                owner.Gliding = false;
                owner.Floor = 0f;
                return;
            }

            // Falling is a little quicker than rising.
            float timeConstant = result > owner.Last ? tau : tau * 0.5f;
            float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, timeConstant));
            // Aim a little past the PID's value so the filter actually arrives (an exponential never quite reaches
            // its target, which showed up as a long crawl from 97% to 100%), then snap when close.
            float aim = Mathf.Clamp01(result + Mathf.Sign(result - owner.Last) * ArrivalOvershoot);
            float y = owner.Last + alpha * (aim - owner.Last);
            if ((result - y) * (result - owner.Last) <= 0f || Mathf.Abs(result - y) < SnapBand) y = result;

            y = Mathf.Clamp(y, owner.Last - FallLimitPerSecond * dt, owner.Last + RiseLimitPerSecond * dt);
            if (owner.Floor > 0f)
            {
                // Release the hold once the controller has reached it, is no longer asking for more speed,
                // or the hold has lasted long enough.
                if (result >= owner.Floor || error <= 0f || now - owner.FloorStart > HoldMaxSeconds) owner.Floor = 0f;
                else y = Mathf.Max(y, owner.Floor);
            }
            if (y < 0.01f && result <= 0.001f) y = 0f; // don't creep toward zero forever
            y = Mathf.Clamp01(y);

            owner.Last = y;

            // While the glider is running, hand the AI the value the lever is showing right now; the glider moves
            // it on to y between the AI's half-second ticks.
            result = (Main.Settings.GlideThrottle && owner.Gliding) ? owner.Shown : y;
        }
    }
}
