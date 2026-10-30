using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Model.AI;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Running fuel and handling totals per engineer, written to logs\fuel_summary_*.csv about once a minute of game
    /// time. It exists so a change to the engineer can be judged by coal per mile on a repeatable run instead of by
    /// eye. Coal is split by what the locomotive was doing: powered (throttle open, no brake), coasting (throttle
    /// closed, no brake), braking (cylinders charged, throttle closed), and both (throttle open while braking: the waste). Brake applications are counted too; a "tap" is one that took off under 3 mph.
    /// Gross tons (everything coupled to the locomotive, short tons) and gross ton-miles are tallied as well, with coal and water per 1,000 gross ton-miles, so runs with different trains can be compared.
    /// Totals start when the engineer is first seen and run until the game ends, so for a trip, note the last row
    /// at the start and at the end and subtract.
    /// </summary>
    internal static class FuelSummary
    {
        private const float WriteEverySeconds = 60f;
        private const float BrakeGapSeconds = 6f;
        private const float MinApplicationSeconds = 3f;
        private const float TapShedMph = 3f;
        private const float MpsToMph = 2.23694f;
        private const float CylinderPsi = 1f;   // average cylinder pressure above this counts as braking

        private const string Header = "clock,loco,minutes,miles,coalLb,coalPerMile,waterGal,poweredLb,coastingLb,brakingLb,bothLb,poweredMin,coastingMin,brakingMin,bothMin,standingMin,brakeApps,tapApps,tapsSuppressed,tapWhy,grossTons,gtMiles,coalPer1000Gtm,waterGalPer1000Gtm,setup";

        private sealed class Tally
        {
            public float LastWrite = float.NaN;
            public double Coal, Water, Dist, Seconds, GrossTonMiles;
            public float Tons = float.NaN;
            public readonly double[] PhaseCoal = new double[5];   // powered, coasting, braking, both, standing
            public readonly double[] PhaseSeconds = new double[5];
            public int Apps, TapApps, Suppressed;
            public bool InBrake;
            public float BrakeStart, BrakeLast, BrakeV0, BrakeVMin;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, Tally> Tallies = new ConditionalWeakTable<AutoEngineer, Tally>();
        private static readonly object Lock = new object();
        private static StreamWriter _writer;
        private static bool _warned;

        internal static void CountSuppressed(AutoEngineer ae)
        {
            if (ae != null) Tallies.GetOrCreateValue(ae).Suppressed++;
        }

        internal static void Tick(AutoEngineer ae, float dt, float speedMps, float throttle, float trainCylPsi, float locoCylPsi, float coalLbs, float waterGps, float grossTons)
        {
            Tally t = Tallies.GetOrCreateValue(ae);
            float now = Time.fixedTime;
            if (float.IsNaN(t.LastWrite)) t.LastWrite = now;

            float v = Mathf.Abs(speedMps);
            float coal = float.IsNaN(coalLbs) ? 0f : coalLbs * dt;
            float water = float.IsNaN(waterGps) ? 0f : waterGps * dt;
            // Braking means pressure in the cylinders, not the handle: the engineer often parks the train brake at a
            // 5 psi reduction that fills the pipe but leaves the cylinders empty (seen: set 0.0556, cylinders 0.02 psi).
            bool trainBraking = trainCylPsi > CylinderPsi;
            bool anyBrake = trainBraking || locoCylPsi > CylinderPsi;
            bool powered = throttle > 0.01f;

            int phase = v < 0.3f ? 4 : powered && anyBrake ? 3 : anyBrake ? 2 : powered ? 0 : 1;
            t.Coal += coal; t.Water += water; t.Dist += v * dt; t.Seconds += dt;
            t.PhaseCoal[phase] += coal; t.PhaseSeconds[phase] += dt;
            if (!float.IsNaN(grossTons))
            {
                t.Tons = grossTons;
                t.GrossTonMiles += grossTons * v * dt / 1609.344;   // the train's weight can change on a trip (cars set out or picked up)
            }

            // Brake applications: one per spell of cylinder pressure, spells closer than BrakeGapSeconds count as one.
            if (trainBraking)
            {
                if (!t.InBrake) { t.InBrake = true; t.BrakeStart = now; t.BrakeV0 = v; t.BrakeVMin = v; }
                t.BrakeLast = now;
                t.BrakeVMin = Mathf.Min(t.BrakeVMin, v);
            }
            else if (t.InBrake && now - t.BrakeLast > BrakeGapSeconds)
            {
                t.InBrake = false;
                if (t.BrakeLast - t.BrakeStart > MinApplicationSeconds)
                {
                    t.Apps++;
                    if ((t.BrakeV0 - t.BrakeVMin) * MpsToMph < TapShedMph) t.TapApps++;
                }
            }

            if (now - t.LastWrite >= WriteEverySeconds)
            {
                t.LastWrite = now;
                Write(ae, t);
            }
        }

        private static string N(double v, string format)
        {
            return v.ToString(format, CultureInfo.InvariantCulture);
        }

        private static void Write(AutoEngineer ae, Tally t)
        {
            double miles = t.Dist / 1609.344;
            string line = string.Join(",",
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                ae.Locomotive != null ? "\"" + (ae.Locomotive.DisplayName ?? "").Replace("\"", "\"\"") + "\"" : "",
                N(t.Seconds / 60.0, "0.0"), N(miles, "0.00"), N(t.Coal, "0"),
                miles > 0.05 ? N(t.Coal / miles, "0.0") : "",
                N(t.Water, "0"),
                N(t.PhaseCoal[0], "0"), N(t.PhaseCoal[1], "0"), N(t.PhaseCoal[2], "0"), N(t.PhaseCoal[3], "0"),
                N(t.PhaseSeconds[0] / 60.0, "0.0"), N(t.PhaseSeconds[1] / 60.0, "0.0"), N(t.PhaseSeconds[2] / 60.0, "0.0"), N(t.PhaseSeconds[3] / 60.0, "0.0"), N(t.PhaseSeconds[4] / 60.0, "0.0"),
                t.Apps.ToString(CultureInfo.InvariantCulture), t.TapApps.ToString(CultureInfo.InvariantCulture), t.Suppressed.ToString(CultureInfo.InvariantCulture), BrakeTaps.RejectSummary(),
                float.IsNaN(t.Tons) ? "" : N(t.Tons, "0"), N(t.GrossTonMiles, "0.0"),
                t.GrossTonMiles > 20 ? N(t.Coal / t.GrossTonMiles * 1000.0, "0.0") : "",
                t.GrossTonMiles > 20 ? N(t.Water / t.GrossTonMiles * 1000.0, "0.0") : "",
                Main.Settings.Describe());

            lock (Lock)
            {
                try
                {
                    if (_writer == null)
                    {
                        Directory.CreateDirectory(LogWriter.LogDir);
                        string path = Path.Combine(LogWriter.LogDir, $"fuel_summary_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                        _writer.WriteLine(Header);
                        Main.Mod.Logger.Log("Logging fuel summary to " + path);
                    }
                    _writer.WriteLine(line);
                }
                catch (IOException e)
                {
                    if (!_warned)
                    {
                        _warned = true;
                        Main.Mod.Logger.Warning("Fuel summary could not be written: " + e.Message);
                    }
                }
            }
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
