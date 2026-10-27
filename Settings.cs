using System;
using System.IO;
using System.Xml.Serialization;

namespace AutoEngineerBrakeFix
{
    /// <summary>Kept in Settings.xml in the mod folder, in the same XML layout UMM used, so old files still load.</summary>
    public class Settings
    {
        // ---- Brake fix ----

        /// <summary>Master switch for the brake fix.</summary>
        public bool FixEnabled = true;

        /// <summary>
        /// How far (mph) above the auto-engineer's own padded setpoint the train may be before the brake is
        /// allowed to apply or stay applied while the throttle is open. The setpoint already sits about 1 mph
        /// under the track limit at 20 mph and above, so 0.5 mph here is still at or below the limit.
        /// </summary>
        public float OverspeedMarginMph = 0.5f;

        /// <summary>Also apply the rule to the locomotive (independent) brake the AI uses on light engines.</summary>
        public bool FixLocomotiveBrake = true;

        // ---- Cutoff (experimental) ----

        /// <summary>Use a shorter cutoff when it keeps nearly the same power.</summary>
        public bool CutoffEnabled = true;

        /// <summary>How much of the peak power multiplier (percent) the shorter cutoff may give up.</summary>
        public float CutoffTolerancePercent = 3f;

        /// <summary>At or above this uphill grade (percent) the stock cutoff is used.</summary>
        public float CutoffRevertGradePercent = 2f;

        // ---- Throttle smoothing ----

        /// <summary>Smooth the auto-engineer's throttle movements.</summary>
        public bool SmoothThrottle = true;

        /// <summary>Time constant of the smoothing (seconds). Larger is gentler and slower to respond.</summary>
        public float ThrottleSmoothingSeconds = 1.6f;

        /// <summary>Also move the lever continuously between the AI's half-second updates.</summary>
        public bool GlideThrottle = true;

        /// <summary>Close the throttle at once when the train is over the setpoint and braking (instead of the AI's 0.25-per-tick steps).</summary>
        public bool SlamThrottleOnOverspeed = true;

        /// <summary>Keep the throttle closed while slowing for a falling target, unless the train is clearly under it.</summary>
        public bool HoldThrottleWhileSlowing = true;

        /// <summary>While slowing for a falling target, keep the train brake applied until the train is clearly under the target.</summary>
        public bool HoldBrakeWhileSlowing = true;

        /// <summary>Stand still once within a couple of metres of a stop target instead of creeping past it.</summary>
        public bool HoldStopAtStopTargets = true;

        /// <summary>Do not start braking until this many mph over a steady limit (0 = stock). Slowing for a lower limit is unaffected.</summary>
        public float BrakeStartOverMph = 1f;

        /// <summary>No power while a lower speed limit is in sight and the train is at or above that speed.</summary>
        public bool AvoidPowerIntoReductions = true;

        /// <summary>Start slowing for a lower limit as if it began this many seconds of travel sooner (0 = stock).</summary>
        public float SpeedReductionLeadSeconds = 6f;

        /// <summary>Release the train brake when the speed it is heading for this many seconds ahead reaches the target (0 = stock).</summary>
        public float BrakeReleaseLeadSeconds = 3f;

        /// <summary>Count the speed the brake cylinders will still take off while they bleed down: release the train brake once that covers the reduction needed, and do not apply more.</summary>
        public bool BrakeResidual = true;

        /// <summary>Speed lost after a release is about (cylinder psi squared) / this, in mph. 60 fits a 52-car train; lower brakes less, higher brakes more.</summary>
        public float BrakeResidualDivisor = 60f;

        /// <summary>The brake is released once the train is expected to settle within this many mph of the limit.</summary>
        public float BrakeResidualMarginMph = 0.5f;

        /// <summary>Detect a stall on a grade (full throttle, almost stopped, climbing): brake, stay put and announce it.</summary>
        public bool StallGuard = true;

        /// <summary>Seconds of full throttle without making progress on a grade before the stall is declared.</summary>
        public float StallSeconds = 10f;

        // ---- Fuel use (experimental) ----

        /// <summary>Replace the game's coal and water use with a power-based model (see FuelModel).</summary>
        /// <summary>Adds a standing-start resistance the game lacks (its Davis equation only covers a moving train). Off by default.</summary>
        public bool StartingResistance = false;

        /// <summary>Extra resistance at 0 mph, in pounds per ton (real plain-bearing cars: roughly 5-10).</summary>
        public float StartingResistanceLbPerTon = 5f;

        /// <summary>The extra resistance falls to 37% at this speed.</summary>
        public float StartingResistanceFadeMph = 2.5f;

        /// <summary>Scale the standing drag by how long the car has stood (oil drains from the journals). Cars that have not moved since the save was loaded count as fully settled.</summary>
        public bool StartingResistanceStandingTime = true;

        /// <summary>Share of the standing drag a car that has only just stopped still has (percent).</summary>
        public float StartingResistanceFloorPercent = 40f;

        /// <summary>Game minutes for a stopped car to get back to 63% of the way from the floor to the full drag.</summary>
        public float StartingResistanceSettleMinutes = 10f;

        public bool RealisticFuel = false;

        /// <summary>Pounds of steam per indicated horsepower-hour (modern locomotives about 16-20 at a good cutoff; 21.5 with the default cutoff term gives about 112 lb of coal per 1,000 gross ton-miles on a road run).</summary>
        public float SteamLbPerIhpHour = 21.5f;

        /// <summary>How much the steam rate changes with cutoff: factor = 1 + slope x (cutoff - 0.30), limited to 0.8-1.4. 0 turns it off.</summary>
        public float CutoffSteamSlope = 0.5f;

        /// <summary>Rail horsepower divided by indicated horsepower.</summary>
        public float MechanicalEfficiency = 0.85f;

        /// <summary>Multiplier on coal burned (1 = the evaporation curve as is).</summary>
        public float CoalScale = 1f;

        // ---- Signals ----

        /// <summary>How far ahead (m) the AI reacts to a signal's aspect. The game uses 200.</summary>
        public float SignalSightMeters = 500f;

        /// <summary>Put the Approach restriction at the signal instead of "now" (which makes the AI brake at full effort).</summary>
        public bool ApproachTargetAtSignal = true;

        /// <summary>After passing an Approach signal keep its speed limit until the next signal is registered.</summary>
        public bool HoldApproachSpeed = true;

        // ---- Planning (experimental) ----

        /// <summary>Multiplier on how far ahead the AI looks for targets (1 = stock).</summary>
        public float LookaheadMultiplier = 1f;

        /// <summary>Fraction of the train's available braking the AI assumes when planning (1 = stock; lower = earlier, gentler).</summary>
        public float BrakePlanningFraction = 1f;

        // ---- Logger ----

        public bool LogEnabled = false;

        /// <summary>Also log coal and water aboard a steam locomotive's tender (see FuelReader).</summary>
        public bool LogFuel = false;

        /// <summary>Seconds between samples. The game's own control loop ticks about every 0.5 s.</summary>
        public float SampleIntervalSeconds = 0.25f;

        /// <summary>Reads the file, or returns defaults (and writes them out when there is no file yet).</summary>
        internal static Settings Load(string path, out string problem)
        {
            problem = null;
            var serializer = new XmlSerializer(typeof(Settings));
            if (File.Exists(path))
            {
                try
                {
                    using (var stream = File.OpenRead(path)) return (Settings)serializer.Deserialize(stream);
                }
                catch (Exception e)
                {
                    // Leave the user's file alone and run on defaults.
                    problem = "could not read " + path + ", using defaults: " + e.Message;
                    return new Settings();
                }
            }
            var settings = new Settings();
            try
            {
                using (var stream = File.Create(path)) serializer.Serialize(stream, settings);
            }
            catch (Exception e)
            {
                problem = "could not write " + path + ": " + e.Message;
            }
            return settings;
        }
    }
}
