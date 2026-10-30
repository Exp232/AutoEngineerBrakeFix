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

        /// <summary>Start slowing for a lower limit as if it began this many seconds of travel sooner (0 = stock). 10 since 2.4.3 (6 before): on the Bryson to Sylva round trip it used about 3% less coal than 6 for the same time; 14 gave no more.</summary>
        public float SpeedReductionLeadSeconds = 10f;

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

        /// <summary>
        /// Replace the game's coal and water use with a power-based model (see FuelModel). Off by default. Can be switched in
        /// the settings window while the game runs (the patch is always installed and does nothing while this is off).
        /// </summary>
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

        /// <summary>
        /// Speed (mph) of the Approach restriction when the game finds no signal after the Approach signal. The game
        /// uses 25, which is the default here (no change). A lower value (for example 15) only ever lowers it: the AI
        /// then slows sooner for a Stop signal it cannot see yet. Values of 25 or more, or below 5, change nothing.
        /// </summary>
        public float ApproachNoNextSignalMph = 25f;

        /// <summary>
        /// Temporary map-data workaround: "signalId:blockId" pairs (comma separated) naming blocks to take off a signal's
        /// protected-block list. The default fixes Sylva West eastbound, whose block lies behind the signal and drops it to
        /// Stop about 45 m before a train. Empty = off. Applied at runtime; no files are changed.
        /// </summary>
        public string CtcDropBlocks = "sy-w-eb:sy-w";

        // ---- Planning (experimental) ----

        /// <summary>Multiplier on how far ahead the AI looks for targets (1 = stock).</summary>
        public float LookaheadMultiplier = 1f;

        /// <summary>Fraction of the train's available braking the AI assumes when planning (1 = stock; lower = earlier, gentler).</summary>
        public float BrakePlanningFraction = 1f;

        // ---- Brake guidance (0.6.0 rollout controller) ----

        /// <summary>
        /// While the train is moving with no speed order and nothing ahead to stop for (what a direction change on a
        /// moving train leaves), cap the train brake at this fraction of a full pipe reduction (0.15 is about 13 psi;
        /// the game allows about 0.29 on a charged train). 0 = off. Ends when a target or speed limit appears again.
        /// </summary>
        public float DirectionStopBrakeLimit = 0.15f;

        /// <summary>
        /// Fuel saving, off at 0; 2 since 2.4.3 (0 before). When the train is no more than this many mph over its target, is not gaining speed and
        /// nothing ahead needs it to slow, the controller's first train-brake application is turned into a coast. Road mode
        /// and Waypoint modes. About 1-2% less coal on downhill running, nothing measurable uphill.
        /// </summary>
        public float BrakeTapSuppressMph = 2f;

        /// <summary>Multipliers on the rollout controller's score weights (1 = the game's own value).</summary>
        public float PlanOverspeedScale = 1f;
        public float PlanUndershootScale = 1f;
        public float PlanUndershootDeadbandScale = 1f;
        public float PlanCeilingScale = 1f;
        public float PlanIndependentUseScale = 1f;

        // ---- Logger ----

        public bool LogEnabled = false;

        /// <summary>Also log coal and water aboard a steam locomotive's tender (see FuelReader).</summary>
        public bool LogFuel = false;

        /// <summary>Also log what the rollout controller decided each tick (action, its cost, the cost of holding, and the cost of each first action).</summary>
        public bool LogDecisions = true;

        /// <summary>Also write logs\signals_*.csv: one line each time the signal an engineer is tracking, or the aspect it shows, changes (with the train's speed and distance at that moment), and add sigId, sigAspect and sigDistM to the main CSV.</summary>
        public bool LogSignals = true;

        /// <summary>Also write logs\ctc_dump_*.txt once per session: every CTC block's track spans and every signal's position and protected blocks (read-only; for tracking down signals that drop to Stop early).</summary>
        public bool LogCtc = true;

        /// <summary>Also write logs\fuel_summary_*.csv: coal, water and distance per engineer about once a minute, split by powered, coasting, braking and "both" (throttle open while braked), with brake applications counted.</summary>
        public bool LogFuelSummary = true;

        /// <summary>Opens the in-game settings window: an Input System binding path (for example &lt;Keyboard&gt;/f8), empty for none.</summary>
        public string SettingsHotkey = "<Keyboard>/f8";

        /// <summary>Hold Ctrl with SettingsHotkey (the default, so the key cannot clash with the game's own).</summary>
        public bool SettingsHotkeyCtrl = true;

        /// <summary>
        /// Charge fuel and water for the game time that has really passed when time is sped up (fast-forward). The game
        /// charges one second per call however much time went by, so a fast-forwarded trip used about a third of its coal.
        /// Normal speed is not affected.
        /// </summary>
        public bool FuelFollowsTimeWarp = true;

        /// <summary>
        /// Skip a placed fusee's per-frame update while the main camera is off. Fast-forward disables the camera and the
        /// game's FuseeRenderer.Update then throws a NullReferenceException on every frame. Does nothing otherwise.
        /// </summary>
        public bool GuardFuseeCamera = true;

        /// <summary>Seconds between samples. The game's own control loop ticks about every 0.5 s.</summary>
        public float SampleIntervalSeconds = 0.25f;

        /// <summary>The tuning values the settings window controls, on one line (for the logs, so every run says how it was set).</summary>
        internal string Describe()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return "tap=" + BrakeTapSuppressMph.ToString("0.#", c) + ";lead=" + SpeedReductionLeadSeconds.ToString("0.#", c)
                + ";cut=" + CutoffTolerancePercent.ToString("0.#", c) + ";over=" + PlanOverspeedScale.ToString("0.##", c)
                + ";under=" + PlanUndershootScale.ToString("0.##", c) + ";margin=" + PlanUndershootDeadbandScale.ToString("0.##", c)
                + ";ceil=" + PlanCeilingScale.ToString("0.##", c) + ";indep=" + PlanIndependentUseScale.ToString("0.##", c);
        }

        /// <summary>Writes the file through a temporary copy, so a failed write cannot leave half a file.</summary>
        internal void Save(string path)
        {
            string temp = path + ".tmp";
            using (var stream = File.Create(temp)) new XmlSerializer(typeof(Settings)).Serialize(stream, this);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

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
